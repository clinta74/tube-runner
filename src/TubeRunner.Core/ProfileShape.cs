using System.Numerics;

namespace TubeRunner.Core;

/// <summary>
/// A <see cref="CrossSection"/> resampled by arc length, so moving through U covers the same wall
/// distance whatever the shape. U = 0 is the right side, 0.25 the top, 0.5 the left, 0.75 the bottom.
/// </summary>
public sealed class ProfileShape
{
    private const int Samples = 1024;

    private readonly Vector2[] _points = new Vector2[Samples + 1];
    // Normalized arc length at each point; _u[0] = 0, _u[Samples] = 1.
    private readonly float[] _u = new float[Samples + 1];

    public ProfileShape(CrossSection section)
    {
        Section = section;
        float n = section.Exponent;
        float length = 0f;

        for (int i = 0; i <= Samples; i++)
        {
            // Polar parameterization keeps samples spread out even when the shape is nearly rectangular.
            float phi = i * MathF.Tau / Samples;
            float c = MathF.Cos(phi), s = MathF.Sin(phi);
            float a = MathF.Abs(c) / section.HalfWidth, b = MathF.Abs(s) / section.HalfHeight;
            // Normalized by the larger term so the large exponent can't underflow.
            float m = MathF.Max(a, b);
            float r = 1f / (m * MathF.Pow(MathF.Pow(a / m, n) + MathF.Pow(b / m, n), 1f / n));
            _points[i] = new Vector2(c * r, s * r);

            if (i > 0) length += Vector2.Distance(_points[i], _points[i - 1]);
            _u[i] = length;
        }

        Perimeter = length;
        for (int i = 0; i <= Samples; i++) _u[i] /= length;
        _u[Samples] = 1f;
        GapEdge = FindGapEdge();
    }

    public CrossSection Section { get; }

    /// <summary>Wall length around the whole section.</summary>
    public float Perimeter { get; }

    /// <summary>
    /// Half-width, in U, of each side gap. Gaps are centered on U = 0 and U = 0.5; 0 when the tube is closed.
    /// </summary>
    public float GapEdge { get; }

    public Vector2 PointAt(float u)
    {
        Locate(u, out int i, out float t);
        return Vector2.Lerp(_points[i], _points[i + 1], t);
    }

    /// <summary>Unit normal pointing from the wall into the tube.</summary>
    public Vector2 InwardNormalAt(float u)
    {
        Locate(u, out int i, out _);
        var tangent = _points[i + 1] - _points[i];
        // Samples run counter-clockwise, so the interior is to the left of the tangent.
        return Vector2.Normalize(new Vector2(-tangent.Y, tangent.X));
    }

    /// <summary>
    /// Positive where the wall exists, negative inside a side gap. Linear in height, so it
    /// interpolates cleanly across mesh triangles.
    /// </summary>
    public float OpenMargin(Vector2 point) =>
        Section.Opening <= 0f ? 1f : MathF.Abs(point.Y) / Section.HalfHeight - Section.GapThreshold;

    public bool IsOpen(float u) => OpenMargin(PointAt(u)) < 0f;

    /// <summary>Moves U out of a side gap to the nearest wall edge on the same (upper or lower) half.</summary>
    public float ClampToSurface(float u)
    {
        u = MathUtil.Wrap01(u);
        float e = GapEdge;
        if (e <= 0f) return u;

        if (u < e) return e;                               // upper right
        if (u > 1f - e) return 1f - e;                     // lower right
        if (u > 0.5f - e && u <= 0.5f) return 0.5f - e;    // upper left
        if (u > 0.5f && u < 0.5f + e) return 0.5f + e;     // lower left
        return u;
    }

    private float FindGapEdge()
    {
        if (Section.Opening <= 0f) return 0f;
        // The shape is symmetric, so the first wall point above the right-hand gap gives every edge.
        for (int i = 0; i <= Samples / 4; i++)
        {
            if (OpenMargin(_points[i]) >= 0f) return _u[i];
        }
        return 0.25f;
    }

    private void Locate(float u, out int i, out float t)
    {
        u = MathUtil.Wrap01(u);
        int idx = Array.BinarySearch(_u, u);
        if (idx < 0) idx = ~idx;
        i = Math.Clamp(idx - 1, 0, Samples - 1);
        float span = _u[i + 1] - _u[i];
        t = span > 0f ? Math.Clamp((u - _u[i]) / span, 0f, 1f) : 0f;
    }
}

/// <summary>Reuses the last shape while the section is unchanged (the common case on constant stretches).</summary>
public sealed class ProfileShapeCache
{
    private ProfileShape? _last;

    public ProfileShape Get(CrossSection section)
    {
        if (_last is null || _last.Section != section) _last = new ProfileShape(section);
        return _last;
    }
}
