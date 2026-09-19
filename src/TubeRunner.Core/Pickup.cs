namespace TubeRunner.Core;

public enum PickupKind
{
    /// <summary>Restores one shield.</summary>
    Shield,

    /// <summary>Restores every shield.</summary>
    FullShields,

    /// <summary>
    /// Adds one extra shield, on top of the normal three. The extra is its own pool of one: it is
    /// spent before the normal shields, nothing else refills it, a second pad while it is held does
    /// nothing, and once spent it is gone until another of these is found.
    /// </summary>
    ShieldSlot,

    /// <summary>Fires much faster for a while.</summary>
    RapidFire,

    /// <summary>Charges the ring gun, whose shots sweep the whole surface of the tube.</summary>
    RingGun,

    /// <summary>Smash through anything for a while, without losing a shield.</summary>
    Unstoppable,

    /// <summary>
    /// Steers faster for a while. The ship's steering rate is a distance across the surface per
    /// second, whatever the section, so the wider the wall the longer everything on it takes to
    /// reach - which is what makes this worth having in a ring, and at speed, where the blocks come
    /// faster than the ship can get out of their way.
    /// </summary>
    Agility,
}

/// <summary>A power-up set into a track surface. Flying over it collects it.</summary>
public sealed class Pickup
{
    public required PickupKind Kind { get; init; }

    /// <summary>Distance along the track of the pickup's center.</summary>
    public required double S { get; init; }

    /// <summary>Branch index inside a split, or -1 on the main track.</summary>
    public int Branch { get; init; } = -1;

    public Surface Surface { get; init; }

    /// <summary>Center across the surface (see <see cref="TrackPosition.X"/>).</summary>
    public float X { get; init; }

    /// <summary>
    /// Name of a target group that switches this on, or null for one that is simply there. Until
    /// every target in that group is destroyed the pad is dead and flying over it does nothing, so
    /// the power-up has to be shot for before it can be taken. It is the gentlest form of a lock:
    /// missing the key costs a reward rather than a shield, which is why it is worth meeting before
    /// a locked door ever stands in the way.
    /// </summary>
    public string? LockedBy { get; init; }

    public bool Collected { get; internal set; }
}
