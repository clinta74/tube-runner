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
/// Where the window stood, in the windowed mode, when the game was last closed, so it comes back
/// there. The position and size are the client area's, as the display server reports them.
/// Maximised, the last plain windowed place is kept under the flag, since a maximised window's own
/// size and position are the screen's.
/// </summary>
public sealed record WindowPlace(int X, int Y, int Width, int Height, bool Maximized = false);

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

    /// <summary>Where the window was last closed from; null until it has been closed once from a window.</summary>
    public WindowPlace? Window { get; init; }

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
        // A window too small to use, or absurdly large, is forgotten rather than restored.
        Window = Window is { Width: >= 320 and <= 16384, Height: >= 180 and <= 16384 } ? Window : null,
    };

    private static float Volume(float value) => float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : 1f;
}
