using System;
using System.Collections.Generic;
using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// Procedural audio: an engine hum and rushing wind that rise with speed, plus synthesized one-shot
/// effects for game events. Everything is generated into one AudioStreamGenerator, so no sound
/// files are needed.
/// </summary>
public partial class EngineAudio : AudioStreamPlayer
{
    private const int MixRate = 44100;
    private const float SampleTime = 1f / MixRate;
    // Speed at which the engine and wind reach their top pitch and brightness.
    private const float TopSpeed = 160f;

    private readonly List<Voice> _voices = new();
    private readonly MusicSynth _music = new(MixRate);
    private readonly Random _rng = new();
    private AudioStreamGeneratorPlayback _playback = null!;
    private float _targetSpeed;
    private float _speed;
    private float _enginePhase;
    private float _engineFiltered;
    private float _wind;

    [Export] public float Gain { get; set; } = 0.5f;

    private float _masterLevel = 1f;
    private float _musicLevel = 1f;
    private float _effectsLevel = 1f;

    /// <summary>
    /// The settings' volume sliders, each from 0 to 1. Squared on the way in: hearing is roughly
    /// logarithmic, so a straight multiplier put nearly all the change in the bottom of the slider and
    /// half-way sounded barely quieter than full.
    /// </summary>
    public void SetVolumes(float master, float music, float effects)
    {
        _masterLevel = master * master;
        _musicLevel = music * music;
        _effectsLevel = effects * effects;
    }

    public override void _Ready()
    {
        Stream = new AudioStreamGenerator { MixRate = MixRate, BufferLength = 0.1f };
        Play();
        _playback = (AudioStreamGeneratorPlayback)GetStreamPlayback();
    }

    public void SetSpeed(float speed) => _targetSpeed = speed;

    /// <summary>What the music should be doing; see <see cref="MusicSynth.SetState"/>.</summary>
    public void SetMusic(float momentum, float ramLeft, bool playing) => _music.SetState(momentum, ramLeft, playing);

    public void OnEvent(SessionEvent e)
    {
        switch (e)
        {
            case SessionEvent.Fired:
                _voices.Add(Voice.Tone(1500f, 600f, 0.07f, 0.10f, square: true));
                break;
            case SessionEvent.Jumped:
                _voices.Add(Voice.Tone(220f, 880f, 0.35f, 0.18f));
                break;
            case SessionEvent.Hit:
                _voices.Add(Voice.Noise(0.5f, 0.55f));
                _voices.Add(Voice.Tone(120f, 35f, 0.45f, 0.5f));
                break;
            case SessionEvent.TargetDestroyed:
                _voices.Add(Voice.Tone(660f, 1320f, 0.15f, 0.22f));
                _voices.Add(Voice.Noise(0.25f, 0.2f));
                break;
            case SessionEvent.ShotBlocked:
                _voices.Add(Voice.Tone(320f, 180f, 0.06f, 0.1f, square: true));
                break;
            case SessionEvent.BlockDamaged:
                _voices.Add(Voice.Tone(220f, 140f, 0.08f, 0.15f, square: true));
                break;
            case SessionEvent.BlockDestroyed:
                _voices.Add(Voice.Noise(0.3f, 0.3f));
                _voices.Add(Voice.Tone(180f, 60f, 0.25f, 0.25f));
                break;
            case SessionEvent.RingFired:
                _voices.Add(Voice.Tone(1200f, 150f, 0.5f, 0.2f));
                _voices.Add(Voice.Noise(0.3f, 0.15f));
                break;
            case SessionEvent.Rammed:
                _voices.Add(Voice.Noise(0.22f, 0.35f));
                _voices.Add(Voice.Tone(140f, 70f, 0.2f, 0.3f, square: true));
                break;
            case SessionEvent.ShieldRestored:
            case SessionEvent.ShieldsRefilled:
            case SessionEvent.ShieldSlotAdded:
            case SessionEvent.RapidFireStarted:
            case SessionEvent.RingGunCharged:
            case SessionEvent.AgilityStarted:
            case SessionEvent.UnstoppableStarted:
                // A quick rising arpeggio for any power-up.
                foreach (float f in new[] { 660f, 880f, 1320f }) _voices.Add(Voice.Tone(f, f * 1.05f, 0.18f, 0.12f));
                break;
            case SessionEvent.UnstoppableEnding:
                // Three falling beeps spread across the last second, so the warning counts down to
                // the end rather than just marking the start of it.
                _voices.Add(Voice.Tone(1320f, 1320f, 0.09f, 0.16f, square: true));
                _voices.Add(Voice.Tone(990f, 990f, 0.09f, 0.16f, square: true, delay: 0.33f));
                _voices.Add(Voice.Tone(740f, 740f, 0.09f, 0.16f, square: true, delay: 0.66f));
                break;
            case SessionEvent.UnstoppableEnded:
                _voices.Add(Voice.Tone(520f, 110f, 0.4f, 0.22f));
                break;
            case SessionEvent.Finished:
                foreach (float f in new[] { 523f, 659f, 784f, 1047f }) _voices.Add(Voice.Tone(f, f, 1.4f, 0.12f));
                break;
            case SessionEvent.GameOver:
                _voices.Add(Voice.Tone(330f, 55f, 1.2f, 0.35f, square: true));
                break;
        }
    }

    public override void _Process(double delta)
    {
        int frames = _playback.GetFramesAvailable();
        for (int i = 0; i < frames; i++)
        {
            float s = NextSample();
            _playback.PushFrame(new Vector2(s, s));
        }
    }

    private float NextSample()
    {
        // Glide toward the target speed over roughly a tenth of a second.
        _speed += (_targetSpeed - _speed) * 0.0002f;
        float s = Math.Clamp(_speed / TopSpeed, 0f, 1.5f);

        // Engine: a sawtooth whose pitch follows speed, softened by a one-pole low-pass.
        float freq = 45f + 90f * s;
        _enginePhase = (_enginePhase + freq * SampleTime) % 1f;
        _engineFiltered += (2f * _enginePhase - 1f - _engineFiltered) * 0.08f;
        float engine = _engineFiltered * (0.05f + 0.07f * s);

        // Wind: low-passed noise that gets louder and brighter with speed.
        float white = (float)_rng.NextDouble() * 2f - 1f;
        _wind += (white - _wind) * (0.01f + 0.1f * s);
        float wind = _wind * (0.1f + 0.45f * s);

        // The engine and wind duck under the unstoppable theme, which otherwise has to fight the
        // loudest the engine ever gets - unstoppable is usually picked up flying flat out.
        float music = _music.Next();
        float effects = (engine + wind) * (1f - 0.6f * _music.RamLevel);
        for (int i = _voices.Count - 1; i >= 0; i--)
        {
            effects += _voices[i].Next(_rng);
            if (_voices[i].Done) _voices.RemoveAt(i);
        }
        float mix = music * _musicLevel + effects * _effectsLevel;
        return Math.Clamp(mix * Gain * _masterLevel, -1f, 1f);
    }

    /// <summary>A short synthesized sound: a pitch sweep or a noise burst with a decaying envelope.</summary>
    private sealed class Voice
    {
        private const float Attack = 0.004f;

        private readonly float _from;
        private readonly float _to;
        private readonly float _duration;
        private readonly float _gain;
        private readonly bool _noise;
        private readonly bool _square;
        private float _t;
        private float _phase;
        private float _wait;

        private Voice(float from, float to, float duration, float gain, bool noise, bool square, float delay)
        {
            (_from, _to, _duration, _gain, _noise, _square, _wait) = (from, to, duration, gain, noise, square, delay);
        }

        public bool Done => _t >= _duration;

        /// <param name="delay">Seconds of silence before it starts, for sequencing a few notes from one event.</param>
        public static Voice Tone(float from, float to, float duration, float gain, bool square = false, float delay = 0f) =>
            new(from, to, duration, gain, noise: false, square, delay);

        public static Voice Noise(float duration, float gain) =>
            new(0f, 0f, duration, gain, noise: true, square: false, delay: 0f);

        public float Next(Random rng)
        {
            if (_wait > 0f)
            {
                _wait -= SampleTime;
                return 0f;
            }

            float k = _t / _duration;
            float envelope = (1f - k) * (1f - k) * Math.Min(1f, _t / Attack);
            _t += SampleTime;

            if (_noise) return ((float)rng.NextDouble() * 2f - 1f) * envelope * _gain;

            _phase = (_phase + (_from + (_to - _from) * k) * SampleTime) % 1f;
            float wave = _square ? (_phase < 0.5f ? 0.6f : -0.6f) : MathF.Sin(_phase * MathF.Tau);
            return wave * envelope * _gain;
        }
    }
}
