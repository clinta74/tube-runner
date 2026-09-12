namespace TubeRunner.Core;

public enum PickupKind
{
    /// <summary>Restores one shield.</summary>
    Shield,

    /// <summary>Restores every shield.</summary>
    FullShields,

    /// <summary>Adds a shield slot, already filled.</summary>
    ShieldSlot,

    /// <summary>Fires much faster for a while.</summary>
    RapidFire,

    /// <summary>Charges the ring gun, whose shots sweep the whole surface of the tube.</summary>
    RingGun,

    /// <summary>Smash through anything for a while, without losing a shield.</summary>
    Unstoppable,
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

    public bool Collected { get; internal set; }
}
