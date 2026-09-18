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
        // Open planes and the two walls of a ring are alike here: nothing on one is reachable
        // across the surface from the other, whatever the gap between them measures.
        if (!shape.IsClosed || shape.IsAnnulus)
        {
            if (a != b) return float.PositiveInfinity;
            if (!shape.IsClosed) return MathF.Abs(xa - xb);

            float around = shape.PerimeterOf(a);
            float wrapped = MathF.Abs(xa - xb) % around;
            return MathF.Min(wrapped, around - wrapped);
        }

        float d = MathF.Abs(shape.Loop(a, xa) - shape.Loop(b, xb)) % shape.Perimeter;
        return MathF.Min(d, shape.Perimeter - d);
    }
}
