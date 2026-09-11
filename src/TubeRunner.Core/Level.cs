namespace TubeRunner.Core;

/// <summary>A playable level: a planned track plus its settings and colors. Load with <see cref="LevelLoader"/>.</summary>
public sealed class Level
{
    public required string Name { get; init; }

    /// <summary>File name of the level that follows this one, if any.</summary>
    public string? Next { get; init; }

    /// <summary>Starting forward speed in units per second; track pieces can change it (see <see cref="Track.SpeedAt"/>).</summary>
    public required float Speed { get; init; }

    /// <summary>Distance between wall seams, where the checker pattern can change.</summary>
    public required float SegmentLength { get; init; }

    public required Theme Theme { get; init; }

    public required Track Track { get; init; }

    /// <summary>Obstacles. They hold per-run state, so load the level again to replay it.</summary>
    public required IReadOnlyList<Obstacle> Obstacles { get; init; }

    /// <summary>Power-ups. Like obstacles, they hold per-run state.</summary>
    public required IReadOnlyList<Pickup> Pickups { get; init; }
}
