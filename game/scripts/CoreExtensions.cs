using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>Conversions from TubeRunner.Core types to Godot types.</summary>
public static class CoreExtensions
{
    public static Vector3 ToGodot(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    public static Color ToColor(this Rgb c) => new(c.R, c.G, c.B);

    public static Vector3 ToVector3(this Rgb c) => new(c.R, c.G, c.B);
}
