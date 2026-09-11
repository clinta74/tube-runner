namespace TubeRunner.Core;

public enum ObstacleKind
{
    /// <summary>Solid; dodge it. Absorbs shots, and breaks when the ship hits it.</summary>
    Block,

    /// <summary>Destroyed by shots for points; also hurts on contact.</summary>
    Target,
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

    public bool Destroyed { get; internal set; }
}
