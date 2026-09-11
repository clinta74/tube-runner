using System.Globalization;

namespace TubeRunner.Core;

/// <summary>An sRGB color with components in [0, 1].</summary>
public readonly record struct Rgb(float R, float G, float B)
{
    /// <summary>Parses "#rrggbb" (the '#' is optional).</summary>
    public static Rgb Parse(string hex)
    {
        var s = hex.StartsWith('#') ? hex[1..] : hex;
        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
        {
            throw new FormatException($"'{hex}' is not a #rrggbb color.");
        }
        return new Rgb(((v >> 16) & 0xff) / 255f, ((v >> 8) & 0xff) / 255f, (v & 0xff) / 255f);
    }
}

/// <summary>
/// A level's colors. Each wall segment picks one dark and one light for its checkerboard.
/// </summary>
/// <param name="Darks">1–4 colors for the dark checker cells.</param>
/// <param name="Lights">1–4 colors for the light checker cells.</param>
/// <param name="SeamDark">Groove around each segment seam.</param>
/// <param name="SeamLight">Thin line at the center of each seam; also shots and HUD accents.</param>
/// <param name="Far">Color the walls fade to with distance.</param>
/// <param name="Ship">Ship body color.</param>
/// <param name="Block">Obstacles to dodge.</param>
/// <param name="Target">Obstacles to shoot.</param>
/// <param name="Breakable">Blocks that shots can break.</param>
/// <param name="FadeStart">Distance where the fade begins.</param>
/// <param name="FadeEnd">Distance where walls are fully faded.</param>
/// <param name="Glow">Extra brightness on light cells and seams; above 0 they bloom (a neon look).</param>
public sealed record Theme(
    IReadOnlyList<Rgb> Darks,
    IReadOnlyList<Rgb> Lights,
    Rgb SeamDark,
    Rgb SeamLight,
    Rgb Far,
    Rgb Ship,
    Rgb Block,
    Rgb Target,
    Rgb Breakable,
    float FadeStart,
    float FadeEnd,
    float Glow)
{
    public const int MaxPaletteColors = 4;

    /// <summary>Brown, green, and gold. Used when a level doesn't specify a theme.</summary>
    public static Theme Earth { get; } = new(
        Darks: [Rgb.Parse("#3b2919"), Rgb.Parse("#2e4a2b"), Rgb.Parse("#4a4f24")],
        Lights: [Rgb.Parse("#b08c3d"), Rgb.Parse("#7a592e"), Rgb.Parse("#8c8a42")],
        SeamDark: Rgb.Parse("#1f140a"),
        SeamLight: Rgb.Parse("#d9ad4d"),
        Far: Rgb.Parse("#0d0a05"),
        Ship: Rgb.Parse("#d9ad4d"),
        Block: Rgb.Parse("#e8dfc8"),
        Target: Rgb.Parse("#ff5a1f"),
        Breakable: Rgb.Parse("#8fb8d8"),
        FadeStart: 30f,
        FadeEnd: 320f,
        Glow: 0f);
}
