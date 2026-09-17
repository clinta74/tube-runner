using System.Text.Json;
using System.Text.Json.Serialization;

namespace TubeRunner.Core;

/// <summary>How the game fills the screen.</summary>
public enum ScreenMode
{
    Windowed,

    /// <summary>Exclusive fullscreen: the game owns the display.</summary>
    Fullscreen,

    /// <summary>A borderless window covering the screen, which alt-tabs away cleanly.</summary>
    Borderless,
}

/// <summary>
/// The player's settings, saved as JSON beside the best times. Every field has a default, so a file
/// from an older version - or no file at all - still loads.
/// </summary>
public sealed record GameSettings
{
    /// <summary>Ask GitHub for a newer release at launch.</summary>
    public bool CheckForUpdates { get; init; } = true;

    public ScreenMode ScreenMode { get; init; } = ScreenMode.Windowed;

    public bool VSync { get; init; } = true;

    /// <summary>Slider positions from 0 to 1. How loud that sounds is the game's business, not this.</summary>
    public float MasterVolume { get; init; } = 1f;

    public float MusicVolume { get; init; } = 1f;

    /// <summary>The engine, wind and every sound cue.</summary>
    public float EffectsVolume { get; init; } = 1f;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// Reads saved settings. A missing field keeps its default; a missing, empty or damaged file gives
    /// the defaults outright rather than stopping the game from starting; anything out of range is
    /// pulled back into it.
    /// </summary>
    public static GameSettings FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new GameSettings();
        try
        {
            return (JsonSerializer.Deserialize<GameSettings>(json, Options) ?? new GameSettings()).Clamped();
        }
        catch (JsonException)
        {
            return new GameSettings();
        }
    }

    /// <summary>The same settings with every value inside its range.</summary>
    public GameSettings Clamped() => this with
    {
        ScreenMode = Enum.IsDefined(ScreenMode) ? ScreenMode : ScreenMode.Windowed,
        MasterVolume = Volume(MasterVolume),
        MusicVolume = Volume(MusicVolume),
        EffectsVolume = Volume(EffectsVolume),
    };

    private static float Volume(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 1f;
}
