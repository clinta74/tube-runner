using System;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// Background music, synthesized like everything else in <see cref="EngineAudio"/>. It is built from
/// layers - pad, bass, drums, arpeggio, lead - that fade in one after another as the run's momentum
/// rises and back out as it falls, so a clean run is heard as music arriving rather than as one track
/// getting louder, and a run going badly is heard thinning out.
///
/// Unstoppable has a theme of its own that replaces the layers while it runs, rather than playing on
/// top of them. Over its last second the theme hands back to the layers, so the warning beeps land on
/// the music breaking down, not on silence.
/// </summary>
public sealed class MusicSynth
{
    private const float Bpm = 112f;
    // Bars in one loop of the clock. The clock wraps so a long session never loses float precision.
    private const int LoopSteps = 16 * 16;
    // How quickly a layer follows its target level, in seconds.
    private const float LayerFade = 0.6f;

    // A minor: Am, F, C, G, one bar each.
    private static readonly int[][] Chords = { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 48, 52, 55 }, new[] { 55, 59, 62 } };
    private static readonly int[] Roots = { 45, 41, 36, 43 };
    // One note per eighth over four bars; 0 is a rest.
    private static readonly int[] Melody =
    {
        76, 0, 74, 72, 69, 0, 72, 0,   72, 0, 69, 67, 65, 0, 64, 0,
        67, 0, 72, 74, 76, 0, 74, 0,   74, 72, 71, 0, 67, 0, 0, 0,
    };
    // The unstoppable riff: semitones above A1, one per sixteenth.
    private static readonly int[] RamRiff = { 0, 0, 12, 0, 0, 12, 0, 10, 0, 0, 12, 0, 3, 5, 7, 10 };

    private readonly float _dt;
    private readonly float _stepLength;
    private readonly float _smooth;
    private readonly Random _rng = new(7);

    private float _t;
    private int _lastStep = -1;
    private int _arpIndex;

    // Current and target level of each layer, and of the unstoppable theme.
    private float _pad, _bass, _drums, _arp, _lead, _ram;
    private float _padTo, _bassTo, _drumsTo, _arpTo, _leadTo, _ramTo;

    private readonly float[] _padFreq = new float[3];
    private readonly float[] _padPhase = new float[3];
    private float _bassFreq, _bassPhase, _bassLow, _bassEnv;
    private float _kickPhase, _kickTime = 1f, _kickEnv;
    private float _hatLow, _hatEnv;
    private float _arpFreq, _arpPhase, _arpEnv;
    private float _leadFreq, _leadPhase, _leadEnv;
    private float _ramFreq, _ramPhase, _ramEnv, _ramKickPhase, _ramKickTime = 1f, _ramKickEnv, _ramSnareEnv;

    private readonly float _bassDecay, _kickDecay, _hatDecay, _arpDecay, _leadDecay, _ramDecay, _ramKickDecay, _snareDecay;

    public MusicSynth(int mixRate)
    {
        _dt = 1f / mixRate;
        _stepLength = 60f / Bpm / 4f;
        _smooth = 1f - MathF.Exp(-_dt / LayerFade);
        _bassDecay = Decay(0.2f);
        _kickDecay = Decay(0.12f);
        _hatDecay = Decay(0.03f);
        _arpDecay = Decay(0.08f);
        _leadDecay = Decay(0.35f);
        _ramDecay = Decay(0.1f);
        _ramKickDecay = Decay(0.09f);
        _snareDecay = Decay(0.08f);
    }

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
        _ram += (_ramTo - _ram) * _smooth;

        float s = 0f;
        if (_pad > 0.0005f) s += Pad() * 0.09f * _pad;
        if (_bass > 0.0005f) s += Bass() * 0.22f * _bass;
        if (_drums > 0.0005f) s += Drums() * _drums;
        if (_arp > 0.0005f) s += Arp() * 0.06f * _arp;
        if (_lead > 0.0005f) s += Lead() * 0.07f * _lead;
        if (_ram > 0.0005f) s += Ram() * _ram;
        return s;
    }

    private void Trigger(int step)
    {
        int bar = step / 16 % 4;
        int inBar = step % 16;
        var chord = Chords[bar];

        for (int i = 0; i < 3; i++) _padFreq[i] = Midi(chord[i]);
        if (inBar % 2 == 0)
        {
            _bassFreq = Midi(Roots[bar]);
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

        if (step % 2 == 0 && Melody[step / 2 % Melody.Length] is int note and > 0)
        {
            _leadFreq = Midi(note);
            _leadEnv = 1f;
        }

        _ramFreq = Midi(33 + RamRiff[inBar]);
        _ramEnv = 1f;
        if (inBar % 2 == 0)
        {
            _ramKickTime = 0f;
            _ramKickEnv = 1f;
        }
        if (inBar == 4 || inBar == 12) _ramSnareEnv = 1f;
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

    // Driving and dirty on purpose: a clipped saw riff on every sixteenth, kick on the eighths, and a
    // snare on two and four. It should sound like a different mode, not a louder version of the run.
    private float Ram()
    {
        _ramPhase = (_ramPhase + _ramFreq * _dt) % 1f;
        float s = MathF.Tanh((2f * _ramPhase - 1f) * 2.5f) * _ramEnv * 0.13f;
        _ramEnv *= _ramDecay;

        if (_ramKickEnv > 0.001f)
        {
            _ramKickTime += _dt;
            _ramKickPhase = (_ramKickPhase + (50f + 120f * MathF.Exp(-_ramKickTime / 0.025f)) * _dt) % 1f;
            s += MathF.Sin(_ramKickPhase * MathF.Tau) * _ramKickEnv * 0.35f;
            _ramKickEnv *= _ramKickDecay;
        }
        if (_ramSnareEnv > 0.001f)
        {
            s += ((float)_rng.NextDouble() * 2f - 1f) * _ramSnareEnv * 0.12f;
            _ramSnareEnv *= _snareDecay;
        }
        return s;
    }

    private float Decay(float seconds) => MathF.Exp(-_dt / seconds);

    private static float Midi(int note) => 440f * MathF.Pow(2f, (note - 69) / 12f);

    private static float Ramp(float value, float from, float to) => Math.Clamp((value - from) / (to - from), 0f, 1f);
}
