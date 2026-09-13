namespace TubeRunner.Core;

/// <summary>
/// A well sunk into the tube wall: a hole cut through it, with a funnel hanging below. Flying into
/// one carries the ship down it and then throws it back up the track, so it costs time rather than
/// a shield. A well stays armed, so flying into the same one again takes the ship again.
/// </summary>
public sealed class Warp
{
    /// <summary>Distance along the track of the mouth's center.</summary>
    public required double S { get; init; }

    /// <summary>Branch index inside a split, or -1 on the main track.</summary>
    public int Branch { get; init; } = -1;

    public Surface Surface { get; init; }

    /// <summary>Center across the surface (see <see cref="TrackPosition.X"/>).</summary>
    public float X { get; init; }

    /// <summary>
    /// Extent across the surface. A well should never block more than about a third of the way round
    /// the tube, so the rest of it always stays safe to fly: this is a hazard to steer around, not a
    /// wall to thread. 14 units is a little over a third of the way around a radius-6 tube.
    /// </summary>
    public float Width { get; init; } = 14f;

    /// <summary>
    /// Extent along the track. Longer than it is wide: the way round the tube is capped by
    /// <see cref="Width"/> so the rest of the tube stays flyable, which leaves along the track as the
    /// only axis free to grow, and a mouth you meet end-on needs the length to read at all.
    /// </summary>
    public float Length { get; init; } = 52f;

    /// <summary>How far back it throws the ship; 0 takes the session's default.</summary>
    public float Back { get; init; }

    /// <summary>Whether it has taken the ship at least once. It stays armed either way.</summary>
    public bool Used { get; internal set; }
}
