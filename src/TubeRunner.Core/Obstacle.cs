using System;

namespace TubeRunner.Core;

public enum ObstacleKind
{
    /// <summary>Solid; dodge it. Stops shots unless it's breakable, and breaks when the ship hits it.</summary>
    Block,

    /// <summary>Destroyed by shots for points; also hurts on contact.</summary>
    Target,

    /// <summary>
    /// Wall that cannot be shot, broken or rammed - only flown around. Every other obstacle has a way
    /// out; this one has none, so it is the only thing in the game that pure aggression cannot answer.
    /// </summary>
    Plate,
}

/// <summary>Something standing on a track surface. Sizes are in world units.</summary>
public sealed class Obstacle
{
    public required ObstacleKind Kind { get; init; }

    /// <summary>Distance along the track of the obstacle's center.</summary>
    public required double S { get; init; }

    /// <summary>Branch index inside a split, or -1 on the main track.</summary>
    public int Branch { get; init; } = -1;

    public Surface Surface { get; init; }

    /// <summary>Center across the surface (see <see cref="TrackPosition.X"/>).</summary>
    public float X { get; init; }

    /// <summary>Extent across the surface.</summary>
    public float Width { get; init; } = 3f;

    /// <summary>Extent along the track.</summary>
    public float Length { get; init; } = 2f;

    /// <summary>How far it stands off the surface.</summary>
    public float Height { get; init; } = 2f;

    /// <summary>
    /// For blocks: shots needed to break it, or 0 if shots can't. Targets always break in one.
    /// </summary>
    public int Hits { get; init; }

    /// <summary>
    /// Seconds in a gate's open/shut cycle, or 0 for an obstacle that is always there. A gate is
    /// solid for the first half of its cycle and gone for the second, so the dodge becomes a
    /// question of when rather than where.
    /// </summary>
    public float Period { get; init; }

    /// <summary>Where in its cycle a gate starts, from 0 to 1. Stagger these to make a rhythm.</summary>
    public float Phase { get; init; }

    /// <summary>
    /// How far around the surface a mover slides from its <see cref="X"/>, or 0 to stand still. It
    /// sweeps back and forth, so the gap you aimed at is not the gap you arrive at.
    /// </summary>
    public float Sweep { get; init; }

    /// <summary>Seconds for a mover to complete one full sweep out and back.</summary>
    public float SweepTime { get; init; } = 2f;

    /// <summary>
    /// Group name for targets that have to be shot in order, or null. Within a group, a target only
    /// breaks once every lower <see cref="Order"/> in it is gone.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>Position in its <see cref="Group"/>'s firing order; lower goes first.</summary>
    public int Order { get; init; }

    /// <summary>
    /// Name of a group that unlocks this one, or null. The obstacle stays solid until every target in
    /// that group is destroyed, so shooting buys passage instead of points - and missing means
    /// arriving at a wall.
    /// </summary>
    public string? LockedBy { get; init; }

    /// <summary>Shots it has taken so far.</summary>
    public int HitsTaken { get; internal set; }

    public bool Destroyed { get; internal set; }

    /// <summary>Whether a gate is solid at <paramref name="time"/>. Always true for anything else.</summary>
    public bool IsSolidAt(float time) =>
        Period <= 0f || (time / Period + Phase) % 1f < 0.5f;

    /// <summary>Where it sits across the surface at <paramref name="time"/>, once any sweep is applied.</summary>
    public float XAt(float time) =>
        Sweep == 0f ? X : X + Sweep * MathF.Sin(2f * MathF.PI * time / (SweepTime <= 0f ? 2f : SweepTime));
}
