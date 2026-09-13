using System.Collections.Generic;
using Godot;
using TubeRunner.Core;
using Theme = TubeRunner.Core.Theme;

namespace TubeRunner.Game;

/// <summary>Applies a level's <see cref="Theme"/> to the wall shader. The ship colors itself.</summary>
public static class ThemeView
{
    public static void Apply(Theme theme, ShaderMaterial wall)
    {
        wall.SetShaderParameter("darks", Palette(theme.Darks));
        wall.SetShaderParameter("dark_count", theme.Darks.Count);
        wall.SetShaderParameter("lights", Palette(theme.Lights));
        wall.SetShaderParameter("light_count", theme.Lights.Count);
        wall.SetShaderParameter("seam_dark", theme.SeamDark.ToVector3());
        wall.SetShaderParameter("seam_light", theme.SeamLight.ToVector3());
        wall.SetShaderParameter("far_color", theme.Far.ToVector3());
        wall.SetShaderParameter("fade_start", theme.FadeStart);
        wall.SetShaderParameter("fade_end", theme.FadeEnd);
        wall.SetShaderParameter("glow", theme.Glow);
        wall.SetShaderParameter("wire", theme.Wire);
    }

    // The shader's palette arrays are fixed-size; unused slots are never picked.
    private static Vector3[] Palette(IReadOnlyList<Rgb> colors)
    {
        var palette = new Vector3[Theme.MaxPaletteColors];
        for (int i = 0; i < palette.Length; i++) palette[i] = colors[System.Math.Min(i, colors.Count - 1)].ToVector3();
        return palette;
    }
}
