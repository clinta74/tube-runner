namespace TubeRunner.Core;

/// <summary>Distance helpers for positions in track space.</summary>
public static class TrackSpace
{
    /// <summary>
    /// Distance along the surfaces between two positions on the same cross-section. In a closed
    /// tube it is measured the short way around; on open planes, positions on different surfaces
    /// are infinitely far apart.
    /// </summary>
    public static float SurfaceDistance(ProfileShape shape, Surface a, float xa, Surface b, float xb)
    {
        if (!shape.IsClosed) return a == b ? MathF.Abs(xa - xb) : float.PositiveInfinity;

        float d = MathF.Abs(shape.Loop(a, xa) - shape.Loop(b, xb)) % shape.Perimeter;
        return MathF.Min(d, shape.Perimeter - d);
    }
}
