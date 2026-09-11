using System.Numerics;

namespace TubeRunner.Core;

/// <summary>
/// Surface geometry of a <see cref="CrossSection"/>. A section has two surfaces: the floor (lower
/// half) and the ceiling (upper half). A point on a surface is given by X, the distance along it
/// from its center, positive toward the track's right. In a closed tube the surfaces meet at the
/// side midpoints (X = ±<see cref="Quarter"/>), so moving past one continues onto the other. In an
/// open section they unroll flat, separate, and extend sideways by <see cref="WingLength"/>.
/// </summary>
public sealed class ProfileShape
{
    /// <summary>Wing length when fully spread. Well past the distance fade, so it reads as endless.</summary>
    public const float MaxWingLength = 1000f;

    private const int Samples = 1024;
    // Offset used to estimate surface tangents.
    private const float TangentStep = 0.05f;

    // The closed curve, counter-clockwise from the right side midpoint.
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

        Unroll = section.Unroll;
        Spread = section.Spread;
        WingLength = MaxWingLength * Spread * Spread * Spread;
    }

    public CrossSection Section { get; }

    /// <summary>Length around the closed (un-opened) section.</summary>
    public float Perimeter { get; }

    /// <summary>Distance from a surface's center to its side edge.</summary>
    public float Quarter => Perimeter / 4f;

    public bool IsClosed => Section.IsClosed;

    /// <inheritdoc cref="CrossSection.Unroll"/>
    public float Unroll { get; }

    /// <inheritdoc cref="CrossSection.Spread"/>
    public float Spread { get; }

    /// <summary>How far an open surface extends past its side edge.</summary>
    public float WingLength { get; }

    /// <summary>Largest |X| on a surface of an open section.</summary>
    public float SurfaceExtent => Quarter + WingLength;

    /// <summary>
    /// Distance around the closed curve, counter-clockwise from the floor center. Continuous across
    /// the side edges; used for texturing and for distances in closed tubes.
    /// </summary>
    public float Loop(Surface surface, float x) => surface == Surface.Floor ? x : 2f * Quarter - x;

    /// <summary>
    /// In a closed tube, carries an X past a side edge onto the other surface. Open sections are
    /// returned unchanged.
    /// </summary>
    public (Surface Surface, float X) Wrap(Surface surface, float x)
    {
        if (!IsClosed) return (surface, x);

        float q = Quarter;
        float loop = Loop(surface, x);
        loop -= Perimeter * MathF.Floor((loop + q) / Perimeter);   // into [-q, 3q)
        return loop <= q ? (Surface.Floor, loop) : (Surface.Ceiling, 2f * q - loop);
    }

    public Vector2 PointAt(Surface surface, float x)
    {
        (surface, x) = Wrap(surface, x);
        float q = Quarter;
        float edge = Math.Clamp(x, -q, q);
        bool floor = surface == Surface.Floor;

        var curved = CurvePoint(floor ? 0.75f + edge / Perimeter : 0.25f - edge / Perimeter);
        var flat = new Vector2(edge, floor ? -Section.HalfHeight : Section.HalfHeight);
        var p = Vector2.Lerp(curved, flat, Unroll);
        // Past the edge of an open section the wing continues flat and sideways.
        return p + new Vector2(x - edge, 0f);
    }

    /// <summary>Unit normal pointing off the surface, into the space the ship flies through.</summary>
    public Vector2 NormalAt(Surface surface, float x)
    {
        var t = PointAt(surface, x + TangentStep) - PointAt(surface, x - TangentStep);
        // +X runs rightward on both surfaces; "into the tube" is up from the floor, down from the ceiling.
        var n = surface == Surface.Floor ? new Vector2(-t.Y, t.X) : new Vector2(t.Y, -t.X);
        return Vector2.Normalize(n);
    }

    /// <summary>Surface position closest to <paramref name="p"/>, a point in section space.</summary>
    public (Surface Surface, float X) Nearest(Vector2 p)
    {
        if (!IsClosed)
        {
            return (p.Y < 0f ? Surface.Floor : Surface.Ceiling, Math.Clamp(p.X, -SurfaceExtent, SurfaceExtent));
        }

        int best = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < Samples; i++)
        {
            float d = Vector2.DistanceSquared(p, _points[i]);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }
        return Wrap(Surface.Floor, (_u[best] - 0.75f) * Perimeter);
    }

    private Vector2 CurvePoint(float u)
    {
        u = MathUtil.Wrap01(u);
        int idx = Array.BinarySearch(_u, u);
        if (idx < 0) idx = ~idx;
        int i = Math.Clamp(idx - 1, 0, Samples - 1);
        float span = _u[i + 1] - _u[i];
        float t = span > 0f ? Math.Clamp((u - _u[i]) / span, 0f, 1f) : 0f;
        return Vector2.Lerp(_points[i], _points[i + 1], t);
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
