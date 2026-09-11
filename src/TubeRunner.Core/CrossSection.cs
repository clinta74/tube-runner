namespace TubeRunner.Core;

/// <summary>
/// Tube cross-section. One family of shapes covers circle → oval → rounded rectangle → two flat planes,
/// so any two sections can be blended smoothly.
/// </summary>
/// <param name="HalfWidth">Horizontal semi-axis.</param>
/// <param name="HalfHeight">Vertical semi-axis.</param>
/// <param name="Squareness">0 = ellipse, 1 = nearly rectangular (flat floor and ceiling).</param>
/// <param name="Opening">0 = closed tube, 1 = side walls removed, leaving floor and ceiling planes.</param>
public readonly record struct CrossSection(float HalfWidth, float HalfHeight, float Squareness = 0f, float Opening = 0f)
{
    // Fraction of the height opened at Opening = 1. Below 1 so the flat floor and ceiling survive.
    private const float MaxGap = 0.9f;

    public static CrossSection Circle(float radius) => new(radius, radius);

    /// <summary>Superellipse exponent: 2 is an ellipse; larger values flatten the sides.</summary>
    public float Exponent => 2f * MathF.Pow(16f, Squareness);

    /// <summary>Wall points with |y| / HalfHeight below this are open (removed).</summary>
    public float GapThreshold => Opening * MaxGap;

    public static CrossSection Lerp(CrossSection a, CrossSection b, float t) => new(
        a.HalfWidth + (b.HalfWidth - a.HalfWidth) * t,
        a.HalfHeight + (b.HalfHeight - a.HalfHeight) * t,
        a.Squareness + (b.Squareness - a.Squareness) * t,
        a.Opening + (b.Opening - a.Opening) * t);
}
