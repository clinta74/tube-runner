using System.Numerics;

namespace TubeRunner.Core;

/// <param name="SteerSpeed">Units per second across the surface at full steer, whatever the cross-section.</param>
/// <param name="MaxPlaneOffset">How far the ship may stray from the center on open floor/ceiling planes.</param>
/// <param name="JumpDuration">Seconds to cross between floor and ceiling.</param>
/// <param name="MinThrottle">Slowest the player can go, as a multiple of the track's speed.</param>
/// <param name="MaxThrottle">Fastest the player can go, as a multiple of the track's speed.</param>
/// <param name="ThrottleRate">How fast the throttle changes at full input, in multiples per second.</param>
public sealed record ShipSettings(
    float SteerSpeed,
    float MaxPlaneOffset = 40f,
    float JumpDuration = 0.55f,
    float MinThrottle = 0.5f,
    float MaxThrottle = 1.75f,
    float ThrottleRate = 0.75f);

/// <summary>
/// Engine-independent ship simulation. In a closed tube the ship steers around the wall; on open
/// floor/ceiling planes it strafes sideways and can jump between them.
/// </summary>
public sealed class ShipSim
{
    private readonly ShipSettings _settings;
    private readonly Track _track;
    private readonly ProfileShapeCache _shapes = new();

    public ShipSim(ShipSettings settings, Track track, TrackPosition start)
    {
        _settings = settings;
        _track = track;
        Shape = _shapes.Get(track.SectionAt(start.S, start.Branch));
        Position = start;
    }

    /// <summary>Position on the surface the ship is on, or is jumping away from.</summary>
    public TrackPosition Position { get; private set; }

    /// <summary>Cross-section shape at the ship's position.</summary>
    public ProfileShape Shape { get; private set; }

    public bool IsJumping { get; private set; }

    /// <summary>Progress through the current jump, from 0 to 1.</summary>
    public float JumpProgress { get; private set; }

    /// <summary>The player's speed setting, as a multiple of the track's speed. Holds until changed.</summary>
    public float Throttle { get; set; } = 1f;

    /// <summary>Multiplier on forward speed, e.g. slowed after a hit.</summary>
    public float SpeedScale { get; set; } = 1f;

    /// <summary>
    /// Current forward speed in units per second: the track's speed here, times <see cref="Throttle"/>
    /// and <see cref="SpeedScale"/>.
    /// </summary>
    public float ForwardSpeed => _track.SpeedAt(Position.S, Position.Branch) * Throttle * SpeedScale;

    /// <param name="dt">Seconds to advance.</param>
    /// <param name="steer">Steering input in [-1, 1]; positive steers to the ship's right.</param>
    /// <param name="jump">Start a jump to the opposite surface. Only possible on fully flat sections.</param>
    /// <param name="throttle">Throttle input in [-1, 1]; positive speeds up, negative slows down.</param>
    public void Step(float dt, float steer, bool jump = false, float throttle = 0f)
    {
        steer = Math.Clamp(steer, -1f, 1f);
        Throttle = Math.Clamp(
            Throttle + Math.Clamp(throttle, -1f, 1f) * _settings.ThrottleRate * dt,
            _settings.MinThrottle,
            _settings.MaxThrottle);
        if (jump && !IsJumping && Shape.Unroll >= 1f)
        {
            IsJumping = true;
            JumpProgress = 0f;
        }

        double s = Position.S + ForwardSpeed * dt;
        // Carries the ship into a branch at a fork and back out at a merge.
        var moved = _track.MoveTo(Position, s);
        Shape = _shapes.Get(_track.SectionAt(s, moved.Branch));
        var surface = moved.Surface;

        if (IsJumping)
        {
            JumpProgress += dt / _settings.JumpDuration;
            if (JumpProgress >= 1f)
            {
                surface = Opposite(surface);
                IsJumping = false;
                JumpProgress = 0f;
            }
        }

        // The ship's right is +X on the floor and -X on the ceiling, where it rides upside down.
        // Past the middle of a jump it has rolled over, so it steers as if on the destination.
        var facing = IsJumping && JumpProgress >= 0.5f ? Opposite(surface) : surface;
        float x = moved.X + steer * _settings.SteerSpeed * dt * (facing == Surface.Floor ? 1f : -1f);

        // Through the funnel where flat planes close back into a tube, the ship stays on its
        // surface and is eased toward the center instead of sliding up the narrowing walls.
        bool rejoining = _track.IsRejoining(s);
        if (Shape.IsClosed && !IsJumping && !rejoining)
        {
            (surface, x) = Shape.Wrap(surface, x);
        }
        else
        {
            float limit = LateralLimit(rejoining);
            x = Math.Clamp(x, -limit, limit);
        }

        Position = new TrackPosition(s, surface, x, moved.Branch);
    }

    /// <summary>Ship position and up direction in section space, <paramref name="rideHeight"/> above its surface.</summary>
    public (Vector2 Point, Vector2 Up) Pose(float rideHeight)
    {
        var up = Shape.NormalAt(Position.Surface, Position.X);
        var point = Shape.PointAt(Position.Surface, Position.X) + up * rideHeight;
        if (!IsJumping) return (point, up);

        var other = Opposite(Position.Surface);
        var otherUp = Shape.NormalAt(other, Position.X);
        var target = Shape.PointAt(other, Position.X) + otherUp * rideHeight;
        float e = MathUtil.SmoothStep(JumpProgress);
        // Roll over during the jump so the ship lands upright on the other surface.
        return (Vector2.Lerp(point, target, e), MathUtil.Rotate(up, MathF.PI * e));
    }

    /// <summary>
    /// How far the ship is off <paramref name="surface"/> while jumping between floor and ceiling.
    /// 0 while riding a surface (lateral distance is what separates surfaces then).
    /// </summary>
    public float HeightAbove(Surface surface)
    {
        if (!IsJumping) return 0f;
        float gap = Vector2.Distance(Shape.PointAt(Surface.Floor, Position.X), Shape.PointAt(Surface.Ceiling, Position.X));
        float e = MathUtil.SmoothStep(JumpProgress);
        return surface == Position.Surface ? e * gap : (1f - e) * gap;
    }

    public static Surface Opposite(Surface surface) => surface == Surface.Floor ? Surface.Ceiling : Surface.Floor;

    // How far from the center the ship may go. Open planes stretch far past the playable area,
    // so play is capped at MaxPlaneOffset; in a rejoin funnel the cap shrinks with the funnel,
    // keeping the ship on its floor or ceiling, clear of the walls.
    private float LateralLimit(bool rejoining)
    {
        const float funnelFloor = 0.6f;
        float q = Shape.Quarter, max = _settings.MaxPlaneOffset;
        if (Shape.IsClosed) return rejoining ? Math.Min(max, funnelFloor * q) : q;

        float play = Math.Min(q, max) + Math.Max(0f, max - q) * Shape.Spread;
        return Math.Min(Shape.SurfaceExtent, play);
    }
}
