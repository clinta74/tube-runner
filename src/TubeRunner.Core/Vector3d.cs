using System.Numerics;

namespace TubeRunner.Core;

/// <summary>
/// Double-precision position, so world coordinates stay exact over long runs.
/// Convert to single precision only relative to a nearby origin (floating origin).
/// </summary>
public readonly record struct Vector3d(double X, double Y, double Z)
{
    public static Vector3d operator +(Vector3d a, Vector3d b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vector3d operator -(Vector3d a, Vector3d b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vector3d operator +(Vector3d a, Vector3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vector3d Lerp(Vector3d a, Vector3d b, double t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);

    /// <summary>This position as an offset from <paramref name="origin"/>, in single precision.</summary>
    public Vector3 RelativeTo(Vector3d origin) =>
        new((float)(X - origin.X), (float)(Y - origin.Y), (float)(Z - origin.Z));
}
