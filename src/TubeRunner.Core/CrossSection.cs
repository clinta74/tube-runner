using System.Numerics;

namespace TubeRunner.Core;

/// <summary>
/// Tube cross-section. One family of shapes covers circle → oval → rounded rectangle → open
/// floor and ceiling planes, so any two sections can be blended smoothly.
/// </summary>
/// <param name="HalfWidth">Horizontal semi-axis.</param>
/// <param name="HalfHeight">Vertical semi-axis; open planes sit at ±HalfHeight.</param>
/// <param name="Squareness">0 = ellipse, 1 = nearly rectangular (flat floor and ceiling).</param>
/// <param name="Opening">
/// 0 = closed tube. From 0 to 0.5 the lower half unrolls into a flat floor and the upper half into
/// a flat ceiling; from 0.5 to 1 they spread sideways toward the horizon. Open pieces must be straight.
/// </param>
/// <param name="Core">
/// 0 = a hollow tube. Above 0 a second cylinder of that fraction of the section's size runs down
/// the middle, and the ship flies in the ring between the two: the outer wall is its floor and the
/// core is its ceiling, hanging overhead. Each wall goes all the way round on its own, and the only
/// way between them is a jump - the same crossing flat sections use, which is why the two walls
/// face each other exactly as a floor and a ceiling do.
///
/// Levels say how much room the ring has rather than how big the core is (see
/// <see cref="RingHeight"/>); this is what that works out to, and what the geometry scales by.
/// </param>
public readonly record struct CrossSection(
    float HalfWidth, float HalfHeight, float Squareness = 0f, float Opening = 0f, float Core = 0f)
{
    public static CrossSection Circle(float radius) => new(radius, radius);

    /// <summary>Superellipse exponent: 2 is an ellipse; larger values flatten the sides.</summary>
    public float Exponent => 2f * MathF.Pow(16f, Squareness);

    public bool IsClosed => Opening <= 0f;

    /// <summary>Whether a cylinder runs down the middle, making the playable space a ring.</summary>
    public bool IsAnnulus => Core > 0f && IsClosed;

    /// <summary>The section's narrowest half-size, where a ring's gap is tightest.</summary>
    public float Narrow => MathF.Min(HalfWidth, HalfHeight);

    /// <summary>
    /// How much room a ring leaves to fly in, measured where the gap is tightest. The gap is wider
    /// than this everywhere the section is wider than its narrow axis, so it is the number that
    /// says whether the ship fits.
    /// </summary>
    public float RingHeight => Narrow * (1f - Core);

    /// <summary>
    /// The most room a ring of this size may be given. A wider bore can carry a taller ring, because
    /// what has to be left behind is a core big enough to be a wall in its own right rather than a
    /// pole - so the limit is a share of the bore, and grows with it.
    /// </summary>
    public float MaxRingHeight => Narrow * (1f - MinCore);

    /// <summary>The least of the section a core may keep and still read as a cylinder to ride.</summary>
    public const float MinCore = 0.3f;

    /// <summary>The least room a ring may leave: enough for the ship and its jump.</summary>
    public const float MinRingHeight = 3f;

    /// <summary>This section with a ring of <paramref name="height"/> cut out of the middle of it.</summary>
    public CrossSection WithRing(float height) => this with { Core = 1f - height / Narrow };

    /// <summary>
    /// Whether a section-space point lies inside the closed shape. A positive tolerance counts points
    /// just outside the edge as inside; a negative one requires them to be clearly inside.
    /// </summary>
    public bool Contains(Vector2 p, float tolerance = 1e-3f)
    {
        float n = Exponent;
        return MathF.Pow(MathF.Abs(p.X) / HalfWidth, n) + MathF.Pow(MathF.Abs(p.Y) / HalfHeight, n) <= 1f + tolerance;
    }

    /// <summary>How flat the two halves have unrolled: 0 = curved tube, 1 = flat.</summary>
    public float Unroll => MathUtil.SmoothStep(Opening * 2f);

    /// <summary>How far the flat floor and ceiling have spread toward the horizon.</summary>
    public float Spread => MathUtil.SmoothStep(Opening * 2f - 1f);

    /// <summary>
    /// Blends between two sections. Every measurement eases across except the core, which does not
    /// blend at all: it takes <paramref name="b"/>'s from the first unit of the piece.
    ///
    /// A core is not a shape the bore eases into, it is a thing standing inside the bore, and easing
    /// one in means growing it from nothing - which is a needle down the middle for as long as the
    /// blend lasts, whatever is done to its tip. Snapping instead puts its ends at two definite
    /// places, the piece boundaries, where the renderer closes them with a flat cap. The bore is
    /// still free to widen or turn across the same piece; only the core arrives whole.
    /// </summary>
    public static CrossSection Lerp(CrossSection a, CrossSection b, float t) => new(
        a.HalfWidth + (b.HalfWidth - a.HalfWidth) * t,
        a.HalfHeight + (b.HalfHeight - a.HalfHeight) * t,
        a.Squareness + (b.Squareness - a.Squareness) * t,
        a.Opening + (b.Opening - a.Opening) * t,
        t > 0f ? b.Core : a.Core);
}
