namespace TubeRunner.Core;

/// <summary>The two halves of a cross-section.</summary>
public enum Surface
{
    Floor,
    Ceiling,
}

/// <summary>
/// A position in track space (see docs/engine-evaluation.md).
/// </summary>
/// <param name="S">Distance along the track. Double to stay precise over long runs.</param>
/// <param name="Surface">Floor (lower half of the section) or ceiling (upper half).</param>
/// <param name="X">Distance along the surface from its center; positive toward the track's right.</param>
public readonly record struct TrackPosition(double S, Surface Surface, float X);
