using System.Numerics;

namespace TubeRunner.Core;

/// <param name="SteerSpeed">Units per second across the surface at full steer, whatever the cross-section.</param>
/// <param name="MaxPlaneOffset">How far the ship may stray from the center on open floor/ceiling planes.</param>
/// <param name="JumpDuration">Seconds to cross between floor and ceiling.</param>
public sealed record ShipSettings(float SteerSpeed, float MaxPlaneOffset = 40f, float JumpDuration = 0.55f);

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

    /// <summary>Multiplier on forward speed, e.g. slowed after a hit.</summary>
    public float SpeedScale { get; set; } = 1f;

    /// <summary>Current forward speed in units per second: the track's speed here, times <see cref="SpeedScale"/>.</summary>
    public float ForwardSpeed => _track.SpeedAt(Position.S) * SpeedScale;

    /// <param name="dt">Seconds to advance.</param>
    /// <param name="steer">Steering input in [-1, 1]; positive steers to the ship's right.</param>
    /// <param name="jump">Start a jump to the opposite surface. Only possible on fully flat sections.</param>
    public void Step(float dt, float steer, bool jump = false)
    {
        steer = Math.Clamp(steer, -1f, 1f);
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

        if (Shape.IsClosed && !IsJumping)
        {
            (surface, x) = Shape.Wrap(surface, x);
        }
        else
        {
            float limit = LateralLimit();
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

    // Open planes stretch far past the playable area. The limit eases in with the spread, so the
    // ship is funneled back toward the center before a tube closes around it.
    private float LateralLimit()
    {
        float q = Shape.Quarter;
        if (Shape.IsClosed) return q;
        float play = q + Math.Max(0f, _settings.MaxPlaneOffset - q) * Shape.Spread;
        return Math.Min(Shape.SurfaceExtent, play);
    }
}
