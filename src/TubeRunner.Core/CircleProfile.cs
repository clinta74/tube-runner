using System.Numerics;

namespace TubeRunner.Core;

/// <summary>
/// Circular tube cross-section, centered on the track centerline.
/// </summary>
public sealed class CircleProfile(float radius)
{
    public float Radius { get; } = radius;

    /// <summary>Point on the wall for a normalized position <paramref name="u"/> in [0, 1).</summary>
    public Vector2 PointAt(float u) => PointAt(u, Radius);

    /// <summary>Point at <paramref name="u"/> on a concentric circle of the given radius.</summary>
    public static Vector2 PointAt(float u, float radius)
    {
        var angle = u * MathF.Tau;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
    }
}
