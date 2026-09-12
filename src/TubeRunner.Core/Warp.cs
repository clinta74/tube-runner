namespace TubeRunner.Core;

/// <summary>
/// A mouth in the tube wall opening into black: a side tube at right angles to the track. Flying
/// into one throws the ship back up the track, so it costs time rather than a shield. Each fires
/// once, so a player who keeps clipping the same mouth still gets past it.
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

    /// <summary>Extent across the surface.</summary>
    public float Width { get; init; } = 6f;

    /// <summary>Extent along the track.</summary>
    public float Length { get; init; } = 6f;

    /// <summary>How far back it throws the ship; 0 takes the session's default.</summary>
    public float Back { get; init; }

    /// <summary>Whether it has already fired.</summary>
    public bool Used { get; internal set; }
}
