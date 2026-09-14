namespace TubeRunner.Core;

/// <summary>Which end of the throttle range a thrust zone closes off.</summary>
public enum ThrustZoneKind
{
    /// <summary>Takes away the slow end: the player cannot crawl through.</summary>
    Floor,

    /// <summary>Takes away the fast end: the player is held back.</summary>
    Ceiling,
}

/// <summary>
/// A stretch of track that closes off one end of the throttle range instead of costing a shield.
///
/// It replaces an earlier idea - a zone that took a shield for being flown too fast - which was
/// wrong twice over. It punished the one thing the rest of the game rewards, and it did it with no
/// warning a player could act on: there was nothing on screen saying how close to the limit they
/// were, so the first they knew of it was the hit.
///
/// Each kind makes a fixed cut, set in <see cref="SessionSettings"/>, rather than carrying its own
/// numbers. Zones used to author their own floor and ceiling, and a floor of 1.3 dragged every ship
/// to the middle of its range on the way in - which took the throttle away rather than narrowing it.
/// A floor cut is small, so it only stops crawling; a ceiling cut is large, because holding a player
/// back is only felt when it really holds them back.
///
/// A zone should cover its whole section. One that ends part way through a narrow bore gives the
/// range back while the walls are still telling the player to be careful, and a limit that changes
/// in the middle of something is harder to read than one that matches what the tube is doing.
/// </summary>
public sealed class ThrustZone
{
    /// <summary>Distance along the track of the zone's center.</summary>
    public required double S { get; init; }

    /// <summary>Branch index inside a split, or -1 on the main track.</summary>
    public int Branch { get; init; } = -1;

    /// <summary>Extent along the track.</summary>
    public float Length { get; init; } = 120f;

    public ThrustZoneKind Kind { get; init; } = ThrustZoneKind.Floor;

    /// <summary>Whether the ship has been inside it at least once.</summary>
    public bool Entered { get; internal set; }

    /// <summary>Whether <paramref name="s"/> lies inside the zone.</summary>
    public bool Contains(double s) => s >= S - Length / 2f && s <= S + Length / 2f;
}
