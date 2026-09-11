using Godot;

namespace TubeRunner.Game;

/// <summary>Conversions from TubeRunner.Core (System.Numerics) types to Godot types.</summary>
public static class CoreExtensions
{
    public static Vector3 ToGodot(this System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
