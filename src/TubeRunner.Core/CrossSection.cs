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
public readonly record struct CrossSection(float HalfWidth, float HalfHeight, float Squareness = 0f, float Opening = 0f)
{
    public static CrossSection Circle(float radius) => new(radius, radius);

    /// <summary>Superellipse exponent: 2 is an ellipse; larger values flatten the sides.</summary>
    public float Exponent => 2f * MathF.Pow(16f, Squareness);

    public bool IsClosed => Opening <= 0f;

    /// <summary>How flat the two halves have unrolled: 0 = curved tube, 1 = flat.</summary>
    public float Unroll => MathUtil.SmoothStep(Opening * 2f);

    /// <summary>How far the flat floor and ceiling have spread toward the horizon.</summary>
    public float Spread => MathUtil.SmoothStep(Opening * 2f - 1f);

    public static CrossSection Lerp(CrossSection a, CrossSection b, float t) => new(
        a.HalfWidth + (b.HalfWidth - a.HalfWidth) * t,
        a.HalfHeight + (b.HalfHeight - a.HalfHeight) * t,
        a.Squareness + (b.Squareness - a.Squareness) * t,
        a.Opening + (b.Opening - a.Opening) * t);
}
