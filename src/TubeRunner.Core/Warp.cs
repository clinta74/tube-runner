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
    /// How far round the tube the mouth reaches, as a share of the section's perimeter.
    ///
    /// A share rather than a count of units, because the same absolute width is a third of the way
    /// round a standard tube and nearly half of a narrow one - so one authored number behaved like
    /// three different hazards depending on where it sat. A third leaves two thirds of the wall
    /// flyable, which is the rule: a well is something to steer around, not a gap to thread.
    ///
    /// At a third the arithmetic is tidy: half the arc is pi*r/3, so the well's radius across the
    /// surface comes out about equal to the tube's own radius.
    /// </summary>
    public float Span { get; init; } = 0.33f;

    /// <summary>The mouth's width across the surface of <paramref name="shape"/>, in units.</summary>
    public float WidthOn(ProfileShape shape) => Span * shape.Perimeter;

    /// <summary>
    /// Extent along the track. Longer than it is wide: the way round the tube is capped by
    /// <see cref="Width"/> so the rest of the tube stays flyable, which leaves along the track as the
    /// only axis free to grow, and a mouth you meet end-on needs the length to read at all.
    /// </summary>
    public float Length { get; init; } = 30f;

    /// <summary>How far back it throws the ship; 0 takes the session's default.</summary>
    public float Back { get; init; }

    /// <summary>Whether it has taken the ship at least once. It stays armed either way.</summary>
    public bool Used { get; internal set; }
}
