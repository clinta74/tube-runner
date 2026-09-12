namespace TubeRunner.Core;

/// <summary>
/// A stretch of track that costs a shield if it is flown too fast. The exact inverse of a speed
/// boost: the player already sets their own throttle, so handing out speed decides nothing, while
/// taking it away does. On a timed run giving up speed genuinely hurts, which is what makes a limit
/// a real choice rather than an instruction.
/// </summary>
public sealed class SpeedLimit
{
    /// <summary>Distance along the track of the zone's center.</summary>
    public required double S { get; init; }

    /// <summary>Branch index inside a split, or -1 on the main track.</summary>
    public int Branch { get; init; } = -1;

    /// <summary>Extent along the track.</summary>
    public float Length { get; init; } = 120f;

    /// <summary>Fastest the ship may go through it without losing a shield.</summary>
    public required float MaxSpeed { get; init; }

    /// <summary>Whether it has caught the ship at least once. It stays armed either way.</summary>
    public bool Tripped { get; internal set; }

    /// <summary>
    /// Whether it has already bitten on this pass. A zone is longer than the post-hit recovery covers
    /// - 1.5 s at 50 u/s is 75 units, and a zone is 120 by default - so without this a single pass
    /// through one takes every shield the player has, a frame or so apart.
    /// </summary>
    internal bool BitThisPass { get; set; }
}
