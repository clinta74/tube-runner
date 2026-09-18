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

    public static CrossSection Lerp(CrossSection a, CrossSection b, float t) => new(
        a.HalfWidth + (b.HalfWidth - a.HalfWidth) * t,
        a.HalfHeight + (b.HalfHeight - a.HalfHeight) * t,
        a.Squareness + (b.Squareness - a.Squareness) * t,
        a.Opening + (b.Opening - a.Opening) * t,
        a.Core + (b.Core - a.Core) * t);
}
