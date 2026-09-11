namespace TubeRunner.Core;

/// <summary>
/// A position in track space (see docs/engine-evaluation.md).
/// </summary>
/// <param name="Segment">Track graph node the ship is on.</param>
/// <param name="S">Distance along the segment centerline. Double to stay precise over long runs.</param>
/// <param name="U">Position across the cross-section, normalized to [0, 1) (fraction of the way around a tube).</param>
/// <param name="H">Offset from the surface (jumps, floor/ceiling switches).</param>
public readonly record struct TrackPosition(int Segment, double S, float U, float H);
