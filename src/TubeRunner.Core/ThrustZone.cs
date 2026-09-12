namespace TubeRunner.Core;

/// <summary>
/// A stretch of track that narrows the throttle range instead of costing a shield.
///
/// It replaces an earlier idea - a zone that took a shield for being flown too fast - which was
/// wrong twice over. It punished the one thing the rest of the game rewards, and it did it with no
/// warning a player could act on: there was nothing on screen saying how close to the limit they
/// were, so the first they knew of it was the hit.
///
/// Narrowing the range is a cost you can see coming and steer by. Raising <see cref="MinThrottle"/>
/// is the sharper edge of it: being forced to carry speed through something tight is a real price,
/// where being forced to slow down is mostly just slower. The cost is control, not shields.
/// </summary>
public sealed class ThrustZone
{
    /// <summary>Distance along the track of the zone's center.</summary>
    public required double S { get; init; }

    /// <summary>Branch index inside a split, or -1 on the main track.</summary>
    public int Branch { get; init; } = -1;

    /// <summary>Extent along the track.</summary>
    public float Length { get; init; } = 120f;

    /// <summary>
    /// Lowest throttle allowed inside, as a multiple of the track's speed, or null to leave the
    /// ship's own floor alone. Raising this forces the player to carry speed they may not want.
    /// </summary>
    public float? MinThrottle { get; init; }

    /// <summary>
    /// Highest throttle allowed inside, or null to leave the ship's own ceiling alone. Lowering this
    /// takes away the option to hurry, which is the gentler half of the mechanic.
    /// </summary>
    public float? MaxThrottle { get; init; }

    /// <summary>Whether the ship has been inside it at least once.</summary>
    public bool Entered { get; internal set; }

    /// <summary>Whether <paramref name="s"/> lies inside the zone.</summary>
    public bool Contains(double s) => s >= S - Length / 2f && s <= S + Length / 2f;
}
