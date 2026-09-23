namespace TubeRunner.Core;

/// <summary>What a level file says about itself before anything is built: see <see cref="LevelLoader.ReadHeader"/>.</summary>
public readonly record struct LevelHeader(string Name, string Id, string? Next);

/// <summary>A playable level: a planned track plus its settings and colors. Load with <see cref="LevelLoader"/>.</summary>
public sealed class Level
{
    public required string Name { get; init; }

    /// <summary>
    /// Stable identifier for this level, used as its key in <see cref="BestTimes"/>. It is the one
    /// thing about a level that must never change: file names carry the running order and so move
    /// whenever a level is inserted, and the display name is free to be reworded, but a time saved
    /// against an id stays attached to the level that earned it.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>File name of the level that follows this one, if any.</summary>
    public string? Next { get; init; }

    /// <summary>Starting forward speed in units per second; track pieces can change it (see <see cref="Track.SpeedAt"/>).</summary>
    public required float Speed { get; init; }

    /// <summary>Distance between wall seams, where the checker pattern can change.</summary>
    public required float SegmentLength { get; init; }

    public required Theme Theme { get; init; }

    /// <summary>The background music, which a level names in its file; <see cref="MusicTracks.Default"/> if it says nothing.</summary>
    public MusicTrack Music { get; init; } = MusicTracks.Default;

    public required Track Track { get; init; }

    /// <summary>Obstacles. They hold per-run state, so load the level again to replay it.</summary>
    public required IReadOnlyList<Obstacle> Obstacles { get; init; }

    /// <summary>Power-ups. Like obstacles, they hold per-run state.</summary>
    public required IReadOnlyList<Pickup> Pickups { get; init; }

    /// <summary>Warp zones. Like obstacles, they hold per-run state.</summary>
    public IReadOnlyList<Warp> Warps { get; init; } = [];

    /// <summary>Thrust zones, which narrow the throttle range while the ship is inside one.</summary>
    public IReadOnlyList<ThrustZone> ThrustZones { get; init; } = [];
}
