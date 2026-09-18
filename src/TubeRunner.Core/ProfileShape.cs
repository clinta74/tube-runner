using System.Numerics;

namespace TubeRunner.Core;

/// <summary>
/// Surface geometry of a <see cref="CrossSection"/>. A section has two surfaces: the floor (lower
/// half) and the ceiling (upper half). A point on a surface is given by X, the distance along it
/// from its center, positive toward the track's right. In a closed tube the surfaces meet at the
/// side midpoints (X = ±<see cref="Quarter"/>), so moving past one continues onto the other. In an
/// open section they unroll flat, separate, and extend sideways by <see cref="WingLength"/>.
///
/// A section with a <see cref="CrossSection.Core"/> is a third arrangement: the floor is the whole
/// of the outer wall and the ceiling the whole of the core, each closing on itself, and the two
/// never meet. X therefore runs right around whichever wall the ship is on and wraps there rather
/// than carrying onto the other, and the walls are joined only by a jump. The core is the outer
/// curve scaled down, so it shares the outer curve's samples and differs only in scale.
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
        Core = section.Core;
        IsAnnulus = section.IsAnnulus;
    }

    /// <summary>The core's size as a fraction of the outer wall, or 0 for a hollow tube.</summary>
    public float Core { get; }

    /// <inheritdoc cref="CrossSection.IsAnnulus"/>
    public bool IsAnnulus { get; }

    /// <summary>
    /// Length once around <paramref name="surface"/>. The same as <see cref="Perimeter"/> except on
    /// the core of a ring, which is smaller - and that difference is why a position has to be
    /// converted with <see cref="Across"/> to mean the same place on the other wall.
    /// </summary>
    public float PerimeterOf(Surface surface) =>
        IsAnnulus && surface == Surface.Ceiling ? Perimeter * Core : Perimeter;

    /// <summary>
    /// The same place around the section, expressed on the other surface: what X becomes when the
    /// ship jumps from <paramref name="from"/> to the wall opposite. Unchanged unless the two walls
    /// are different sizes, which only a ring's are.
    /// </summary>
    public float Across(Surface from, float x)
    {
        if (!IsAnnulus) return x;
        var to = from == Surface.Floor ? Surface.Ceiling : Surface.Floor;
        return x * PerimeterOf(to) / PerimeterOf(from);
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
    public float Loop(Surface surface, float x) =>
        IsAnnulus ? x : surface == Surface.Floor ? x : 2f * Quarter - x;

    /// <summary>
    /// In a closed tube, carries an X past a side edge onto the other surface. Open sections are
    /// returned unchanged.
    /// </summary>
    public (Surface Surface, float X) Wrap(Surface surface, float x)
    {
        if (!IsClosed) return (surface, x);

        // Each wall of a ring closes on itself, so going past the far side of one comes back round
        // the same wall. Nothing carries across; that is what the jump is for.
        if (IsAnnulus)
        {
            float around = PerimeterOf(surface);
            float half = around * 0.5f;
            return (surface, x - around * MathF.Floor((x + half) / around));
        }

        float q = Quarter;
        float loop = Loop(surface, x);
        loop -= Perimeter * MathF.Floor((loop + q) / Perimeter);   // into [-q, 3q)
        return loop <= q ? (Surface.Floor, loop) : (Surface.Ceiling, 2f * q - loop);
    }

    public Vector2 PointAt(Surface surface, float x)
    {
        (surface, x) = Wrap(surface, x);
        if (IsAnnulus)
        {
            // Both walls are measured from the bottom and run the same way round, so the ship keeps
            // its sense of right through a jump. The core is the outer curve scaled down.
            float scale = surface == Surface.Floor ? 1f : Core;
            return CurvePoint(0.75f + x / PerimeterOf(surface)) * scale;
        }

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
        // +X runs rightward on both surfaces; "into the tube" is up from the floor, down from the
        // ceiling. A ring's walls run the same way round rather than mirroring, so its core takes
        // the same turn as the outer wall and is flipped afterwards to face back across the gap.
        var n = surface == Surface.Floor || IsAnnulus ? new Vector2(-t.Y, t.X) : new Vector2(t.Y, -t.X);
        if (IsAnnulus && surface == Surface.Ceiling) n = -n;
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
