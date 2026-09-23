using System;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// Background music, synthesized like everything else in <see cref="EngineAudio"/>. It is built from
/// layers - pad, bass, drums, arpeggio, lead - that fade in one after another as the run's momentum
/// rises and back out as it falls, so a clean run is heard as music arriving rather than as one track
/// getting louder, and a run going badly is heard thinning out.
///
/// What the layers play comes from a <see cref="MusicTrack"/>: the tempo, the chords and the tune.
/// The title has one and each level names one. A change of track is made in the same language as
/// momentum: over the last bar before the line the old track thins to its pad, at the line the two
/// pads cross over a bar, and the new track's bass, drums, arpeggio and lead arrive over the two
/// bars after it. A straight crossfade was tried first, and two keys sounding at once smeared; a
/// cut at the bar line was a lurch.
///
/// Unstoppable has a theme of its own that replaces the layers while it runs, rather than playing on
/// top of them. Over its last second the theme hands back to the layers, so the warning beeps land on
/// the music breaking down, not on silence.
/// </summary>
public sealed class MusicSynth
{
    // How quickly a layer follows its target level, in seconds.
    private const float LayerFade = 0.6f;
    // The unstoppable theme cuts in almost at once: it is announcing a pickup, and a slow fade made it
    // quiet at exactly the moment it mattered. Its hand-back is already paced by its target.
    private const float RamFade = 0.08f;
    // Bars over which the new track's layers above the pad come back after a change.
    private const float ArrivalBars = 2f;

    // The unstoppable riff: semitones above A1, one per sixteenth. The same whatever the track.
    private static readonly int[] RamRiff = { 0, 0, 12, 0, 0, 12, 0, 10, 0, 0, 12, 0, 3, 5, 7, 10 };

    private readonly float _dt;
    private readonly float _smooth;
    private readonly float _ramSmooth;

    private TrackPlayer _player;
    private TrackPlayer? _leaving;
    private MusicTrack? _pending;
    // How far the new track has arrived since the line, in bars; past ArrivalBars nothing is held back.
    private float _arrived = ArrivalBars;

    /// <summary>The track playing now; a change asked for waits at the bar line.</summary>
    public MusicTrack Track => _player.Track;

    /// <summary>How loud the unstoppable theme currently is, from 0 to 1. The engine ducks under it.</summary>
    public float RamLevel => _ram;
    private readonly Random _rng = new(7);

    // Target level of each layer from the run's state, before any change of track shapes them.
    private float _padTo, _bassTo, _drumsTo, _arpTo, _leadTo;
    private float _ram, _ramTo;

    private float _ramFreq, _ramPhase, _ramEnv, _ramKickPhase, _ramKickTime = 1f, _ramKickEnv, _ramSnareEnv;
    private int _ramStep = -1;
    private float _ramT;
    private readonly float _ramDecay, _ramKickDecay, _snareDecay;

    public MusicSynth(int mixRate)
    {
        _dt = 1f / mixRate;
        _smooth = 1f - MathF.Exp(-_dt / LayerFade);
        _ramSmooth = 1f - MathF.Exp(-_dt / RamFade);
        _ramDecay = Decay(0.1f);
        _ramKickDecay = Decay(0.09f);
        _snareDecay = Decay(0.08f);
        _player = new TrackPlayer(MusicTracks.Default, _dt, _smooth, _rng);
    }

    /// <summary>
    /// The track to play from the next bar line. Asking for the one already playing cancels a
    /// change still waiting.
    /// </summary>
    public void SetTrack(MusicTrack track)
    {
        _pending = ReferenceEquals(track, _player.Track) ? null : track;
    }

    // Seconds after a hit for the layers above the pad to be all the way back.
    private const float HitDipSeconds = 8f;
    // How much of a hit is still being felt: 1 at the hit, 0 once the dip has run its course.
    private float _dip;

    /// <summary>
    /// A shield lost. The music dies down to its pad and comes back a layer at a time over the next
    /// seconds, bass first and lead last, for as long as no other hit lands - the same thing
    /// momentum does over a minute and a half, felt at once.
    /// </summary>
    public void Hit() => _dip = 1f;

    /// <param name="momentum">The run's momentum, from 0 to 1.</param>
    /// <param name="ramLeft">Seconds of unstoppable left; 0 when it is not running.</param>
    /// <param name="playing">False before the start and after the run ends, which fades everything out.</param>
    public void SetState(float momentum, float ramLeft, bool playing)
    {
        // Full unstoppable theme until its last second, then a straight crossfade back to the layers.
        float ram = playing && ramLeft > 0f ? Math.Min(1f, ramLeft / GameSession.RamWarning) : 0f;
        float layers = playing ? 1f - ram : 0f;

        _padTo = layers * Ramp(momentum, 0.02f, 0.15f);
        _bassTo = layers * Ramp(momentum, 0.2f, 0.35f);
        _drumsTo = layers * Ramp(momentum, 0.4f, 0.55f);
        _arpTo = layers * Ramp(momentum, 0.6f, 0.75f);
        _leadTo = layers * Ramp(momentum, 0.82f, 0.95f);
        _ramTo = ram;
    }

    public float Next()
    {
        // The change happens at the playing track's bar line: the old player steps aside, still
        // sounding, and a new one starts the new track from the top of its loop.
        if (_pending is not null && _player.AtBarLine)
        {
            _leaving = _player;
            _player = new TrackPlayer(_pending, _dt, _smooth, _rng);
            _pending = null;
            _arrived = 0f;
        }
        _arrived = Math.Min(ArrivalBars, _arrived + _dt / _player.BarLength);

        // Into the line the old track thins to its pad over its last bar; after it the pads cross
        // over one bar, equal power, and the rest of the new track comes back over two.
        float thin = _pending is null ? 1f : Math.Clamp(_player.TimeToBarLine / _player.BarLength, 0f, 1f);
        float cross = Math.Min(1f, _arrived);
        float arrive = _arrived / ArrivalBars;
        float padIn = MathF.Sin(cross * MathF.PI / 2f);
        float padOut = MathF.Cos(cross * MathF.PI / 2f);

        // After a hit the layers come back in the order they first arrived, each over its own part
        // of the dip, and the pad only dims: the music dies down rather than stopping.
        _dip = Math.Max(0f, _dip - _dt / HitDipSeconds);
        float back = 1f - _dip;
        float padHit = 0.5f + 0.5f * back;
        float bassHit = Ramp(back, 0f, 0.4f), drumsHit = Ramp(back, 0.2f, 0.65f), arpHit = Ramp(back, 0.45f, 0.85f), leadHit = Ramp(back, 0.65f, 1f);

        float up = thin * arrive;
        _player.SetTargets(_padTo * padIn * padHit, _bassTo * up * bassHit, _drumsTo * up * drumsHit, _arpTo * up * arpHit, _leadTo * up * leadHit);
        float s = _player.Next();
        if (_leaving is not null)
        {
            _leaving.SetTargets(_padTo * padOut * padHit, 0f, 0f, 0f, 0f);
            s += _leaving.Next();
            if (cross >= 1f && _leaving.Silent) _leaving = null;
        }

        _ram += (_ramTo - _ram) * _ramSmooth;
        if (_ram > 0.0005f) s += Ram() * _ram;
        return s;
    }

    // Driving and dirty on purpose: a clipped saw riff on every sixteenth, kick on the eighths, and a
    // snare on two and four. It should sound like a different mode, not a louder version of the run.
    // It keeps its own clock at the run track's pace, so it is the same riff whatever is playing.
    private float Ram()
    {
        const float stepLength = 60f / 112f / 4f;
        int step = (int)(_ramT / stepLength);
        if (step != _ramStep)
        {
            _ramStep = step;
            int inBar = step % 16;
            _ramFreq = Midi(33 + RamRiff[inBar]);
            _ramEnv = 1f;
            if (inBar % 2 == 0)
            {
                _ramKickTime = 0f;
                _ramKickEnv = 1f;
            }
            if (inBar == 4 || inBar == 12) _ramSnareEnv = 1f;
        }
        _ramT += _dt;
        if (_ramT >= 16 * 16 * stepLength) _ramT -= 16 * 16 * stepLength;

        _ramPhase = (_ramPhase + _ramFreq * _dt) % 1f;
        float s = MathF.Tanh((2f * _ramPhase - 1f) * 2.5f) * _ramEnv * 0.21f;
        _ramEnv *= _ramDecay;

        if (_ramKickEnv > 0.001f)
        {
            _ramKickTime += _dt;
            _ramKickPhase = (_ramKickPhase + (50f + 120f * MathF.Exp(-_ramKickTime / 0.025f)) * _dt) % 1f;
            s += MathF.Sin(_ramKickPhase * MathF.Tau) * _ramKickEnv * 0.55f;
            _ramKickEnv *= _ramKickDecay;
        }
        if (_ramSnareEnv > 0.001f)
        {
            s += ((float)_rng.NextDouble() * 2f - 1f) * _ramSnareEnv * 0.19f;
            _ramSnareEnv *= _snareDecay;
        }
        return s;
    }

    private float Decay(float seconds) => MathF.Exp(-_dt / seconds);

    private static float Midi(int note) => 440f * MathF.Pow(2f, (note - 69) / 12f);

    private static float Ramp(float value, float from, float to) => Math.Clamp((value - from) / (to - from), 0f, 1f);

    /// <summary>
    /// One track being played: its clock, and the five layers with their oscillators. Two of these
    /// sound at once across a change of track, which is why the layers are not on the synth itself.
    /// </summary>
    private sealed class TrackPlayer
    {
        // Bars in one loop of the clock. The clock wraps so a long session never loses float precision.
        private const int LoopSteps = 16 * 16;

        public MusicTrack Track { get; }
        public float BarLength { get; }

        private readonly float _dt;
        private readonly float _smooth;
        private readonly float _stepLength;
        private readonly Random _rng;

        private float _t;
        private int _lastStep = -1;
        private int _arpIndex;

        private float _pad, _bass, _drums, _arp, _lead;
        private float _padTo, _bassTo, _drumsTo, _arpTo, _leadTo;

        private readonly float[] _padFreq = new float[3];
        private readonly float[] _padPhase = new float[3];
        private float _bassFreq, _bassPhase, _bassLow, _bassEnv;
        private float _kickPhase, _kickTime = 1f, _kickEnv;
        private float _hatLow, _hatEnv;
        private float _arpFreq, _arpPhase, _arpEnv;
        private float _leadFreq, _leadPhase, _leadEnv;
        private readonly float _bassDecay, _kickDecay, _hatDecay, _arpDecay, _leadDecay;

        public TrackPlayer(MusicTrack track, float dt, float smooth, Random rng)
        {
            Track = track;
            _dt = dt;
            _smooth = smooth;
            _rng = rng;
            _stepLength = 60f / track.Bpm / 4f;
            BarLength = _stepLength * 16f;
            _bassDecay = Decay(0.2f);
            _kickDecay = Decay(0.12f);
            _hatDecay = Decay(0.03f);
            _arpDecay = Decay(0.08f);
            _leadDecay = Decay(0.35f);
        }

        /// <summary>True on the sample that starts a bar, before that bar's first notes are struck.</summary>
        public bool AtBarLine => (int)(_t / _stepLength) % 16 == 0 && (int)(_t / _stepLength) != _lastStep;

        public float TimeToBarLine => (16 - (int)(_t / _stepLength) % 16) * _stepLength - (_t % _stepLength);

        /// <summary>Whether every layer has faded to nothing.</summary>
        public bool Silent => _pad + _bass + _drums + _arp + _lead < 0.002f;

        public void SetTargets(float pad, float bass, float drums, float arp, float lead)
        {
            _padTo = pad;
            _bassTo = bass;
            _drumsTo = drums;
            _arpTo = arp;
            _leadTo = lead;
        }

        public float Next()
        {
            int step = (int)(_t / _stepLength);
            if (step != _lastStep)
            {
                _lastStep = step;
                Trigger(step);
            }
            _t += _dt;
            if (_t >= LoopSteps * _stepLength) _t -= LoopSteps * _stepLength;

            _pad += (_padTo - _pad) * _smooth;
            _bass += (_bassTo - _bass) * _smooth;
            _drums += (_drumsTo - _drums) * _smooth;
            _arp += (_arpTo - _arp) * _smooth;
            _lead += (_leadTo - _lead) * _smooth;

            float s = 0f;
            if (_pad > 0.0005f) s += Pad() * 0.09f * _pad;
            if (_bass > 0.0005f) s += Bass() * 0.22f * _bass;
            if (_drums > 0.0005f) s += Drums() * _drums;
            if (_arp > 0.0005f) s += Arp() * 0.06f * _arp;
            if (_lead > 0.0005f) s += Lead() * 0.07f * _lead;
            return s;
        }

        private void Trigger(int step)
        {
            int bar = step / 16 % 4;
            int inBar = step % 16;
            var chord = Track.Chords[bar];

            for (int i = 0; i < 3; i++) _padFreq[i] = Midi(chord[i]);
            if (inBar % 2 == 0)
            {
                _bassFreq = Midi(Track.Roots[bar]);
                _bassEnv = 1f;
            }
            if (inBar % 4 == 0)
            {
                _kickTime = 0f;
                _kickEnv = 1f;
            }
            if (inBar % 4 == 2) _hatEnv = 1f;

            _arpFreq = Midi(chord[_arpIndex++ % 3] + 12);
            _arpEnv = 1f;

            if (step % 2 == 0 && Track.Melody[step / 2 % Track.Melody.Length] is int note and > 0)
            {
                _leadFreq = Midi(note);
                _leadEnv = 1f;
            }
        }

        private float Pad()
        {
            float sum = 0f;
            for (int i = 0; i < 3; i++)
            {
                _padPhase[i] = (_padPhase[i] + _padFreq[i] * _dt) % 1f;
                sum += MathF.Sin(_padPhase[i] * MathF.Tau);
            }
            return sum / 3f;
        }

        private float Bass()
        {
            _bassPhase = (_bassPhase + _bassFreq * _dt) % 1f;
            _bassLow += (2f * _bassPhase - 1f - _bassLow) * 0.06f;
            _bassEnv *= _bassDecay;
            return _bassLow * _bassEnv;
        }

        private float Drums()
        {
            float s = 0f;
            if (_kickEnv > 0.001f)
            {
                _kickTime += _dt;
                _kickPhase = (_kickPhase + (45f + 110f * MathF.Exp(-_kickTime / 0.03f)) * _dt) % 1f;
                s += MathF.Sin(_kickPhase * MathF.Tau) * _kickEnv * 0.35f;
                _kickEnv *= _kickDecay;
            }
            if (_hatEnv > 0.001f)
            {
                float noise = (float)_rng.NextDouble() * 2f - 1f;
                _hatLow += (noise - _hatLow) * 0.3f;
                s += (noise - _hatLow) * _hatEnv * 0.05f;
                _hatEnv *= _hatDecay;
            }
            return s;
        }

        private float Arp()
        {
            _arpPhase = (_arpPhase + _arpFreq * _dt) % 1f;
            _arpEnv *= _arpDecay;
            return (4f * MathF.Abs(_arpPhase - 0.5f) - 1f) * _arpEnv;
        }

        private float Lead()
        {
            float vibrato = 1f + 0.006f * MathF.Sin(_t * MathF.Tau * 5.5f);
            _leadPhase = (_leadPhase + _leadFreq * vibrato * _dt) % 1f;
            _leadEnv *= _leadDecay;
            return MathF.Sin(_leadPhase * MathF.Tau) * _leadEnv;
        }

        private float Decay(float seconds) => MathF.Exp(-_dt / seconds);
    }
}
