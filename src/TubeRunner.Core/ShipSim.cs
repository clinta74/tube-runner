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
    float MinThrottle = 0.35f,
    float MaxThrottle = 1.5f,
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

    /// <summary>The movement settings this ship was built with; the HUD reads its throttle range.</summary>
    public ShipSettings Settings => _settings;

    public ShipSim(ShipSettings settings, Track track, TrackPosition start)
    {
        _settings = settings;
        _track = track;
        Shape = _shapes.Get(track.SectionAt(start.S, start.Branch));
        Position = start;
        ThrottleFloor = settings.MinThrottle;
        ThrottleCeiling = settings.MaxThrottle;
    }

    /// <summary>Position on the surface the ship is on, or is jumping away from.</summary>
    public TrackPosition Position { get; private set; }

    /// <summary>Cross-section shape at the ship's position.</summary>
    public ProfileShape Shape { get; private set; }

    public bool IsJumping { get; private set; }

    /// <summary>
    /// Whether a jump can be started right now. Only on a fully unrolled section: in a tube, or
    /// anywhere part way through opening out or closing back up, there is nowhere to jump to.
    /// The HUD reads this rather than working it out again, so what it promises and what the ship
    /// will actually do cannot drift apart.
    /// </summary>
    public bool CanJump => !IsJumping && JumpWindows.Jumpable(Shape);

    /// <summary>Progress through the current jump, from 0 to 1.</summary>
    public float JumpProgress { get; private set; }

    /// <summary>The player's speed setting, as a multiple of the track's speed. Holds until changed.</summary>
    public float Throttle { get; set; } = 1f;

    /// <summary>Multiplier on forward speed, e.g. slowed after a hit.</summary>
    public float SpeedScale { get; set; } = 1f;

    /// <summary>Multiplier on steering speed; an agility pickup raises it for a while.</summary>
    public float SteerScale { get; set; } = 1f;

    /// <summary>
    /// Lowest throttle the player may hold right now. Normally the ship's own floor; a thrust zone
    /// can raise it, which forces speed on them rather than taking it away.
    /// </summary>
    public float ThrottleFloor { get; set; }

    /// <summary>Highest throttle the player may hold right now; a thrust zone can lower it.</summary>
    public float ThrottleCeiling { get; set; }

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
        // Clamped to whatever range is in force, not the ship's own: inside a thrust zone the range
        // narrows, and a throttle already outside the new range is dragged into it. That drag is the
        // whole mechanic - a raised floor makes the ship speed up whether the player wants it or not.
        Throttle = Math.Clamp(
            Throttle + Math.Clamp(throttle, -1f, 1f) * _settings.ThrottleRate * dt,
            ThrottleFloor,
            ThrottleCeiling);
        if (jump && CanJump)
        {
            IsJumping = true;
            JumpProgress = 0f;
        }

        double s = Position.S + ForwardSpeed * dt;
        // Carries the ship into a branch at a fork and back out at a merge.
        var moved = _track.MoveTo(Position, s);
        var before = Shape;
        Shape = _shapes.Get(_track.SectionAt(s, moved.Branch));
        var surface = moved.Surface;

        float landed = moved.X;
        if (before.IsAnnulus != Shape.IsAnnulus && surface == Surface.Ceiling && !IsJumping)
        {
            (surface, landed) = CoreArrivedOrLeft(before, landed);
        }
        // A fork or a merge has already put the ship at the nearest point on its new wall.
        else if (before.IsClosed && Shape.IsClosed && moved.Branch == Position.Branch && !_track.IsFunnel(s))
        {
            landed = KeepPlace(before, Shape, surface, landed);
        }

        if (IsJumping)
        {
            JumpProgress += dt / _settings.JumpDuration;
            if (JumpProgress >= 1f)
            {
                // The walls of a ring are different sizes, so the same place around the section is a
                // different X on each. Landing without converting would slide the ship sideways by
                // however much the two differ.
                landed = Shape.Across(surface, landed);
                surface = Opposite(surface);
                IsJumping = false;
                JumpProgress = 0f;
            }
        }

        // The ship's right is +X on the floor and -X on the ceiling, where it rides upside down.
        // Past the middle of a jump it has rolled over, so it steers as if on the destination.
        var facing = IsJumping && JumpProgress >= 0.5f ? Opposite(surface) : surface;
        float x = landed + steer * _settings.SteerSpeed * SteerScale * dt * (facing == Surface.Floor ? 1f : -1f);

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

    /// <summary>
    /// The same place round the section on a wall that has changed size since the last step. X is
    /// a distance round the wall, and a wall that widens under the ship - a bore opening out, a
    /// core tapering - would otherwise carry the ship round itself: the same X is a smaller share
    /// of a bigger wall, so a ship a third of the way round a tube slid toward the bottom as the
    /// tube opened, and the whole bore seemed to roll. It rides the flaring wall instead, keeping
    /// its place the way it does through a jump (see <see cref="ProfileShape.Across"/>). A tube
    /// unrolling into planes is the one widening this must leave alone: it goes out to the horizon
    /// first, and there the distance from the middle is what has to hold.
    /// </summary>
    private static float KeepPlace(ProfileShape before, ProfileShape now, Surface surface, float x)
    {
        float was = before.PerimeterOf(surface);
        return was > 0f ? x * now.PerimeterOf(surface) / was : x;
    }

    /// <summary>
    /// Moves a ship riding the ceiling onto the right surface when a core grows in or shrinks away
    /// under it. A tube's ceiling is the upper half of its wall; a ring's is the core, which is a
    /// different surface entirely, so the name means two different places either side of the change.
    ///
    /// Gaining a core is seamless: the upper half of the wall simply becomes part of the outer wall,
    /// which is one surface all the way round, so the ship carries on from the same place under a
    /// different name. Losing one is not, because the surface the ship was riding has gone - it is
    /// put down on the wall at the same place around the section, and levels should bring the player
    /// off the core before closing a ring rather than rely on that.
    /// </summary>
    private (Surface Surface, float X) CoreArrivedOrLeft(ProfileShape before, float x) =>
        Shape.IsAnnulus
            ? (Surface.Floor, Shape.Wrap(Surface.Floor, before.Loop(Surface.Ceiling, x)).X)
            : (Surface.Floor, before.Across(Surface.Ceiling, x));

    /// <summary>
    /// Drops the ship at another point on the track, keeping where it sits on the surface. Warp
    /// zones use this to throw it back the way it came, which is the one time S goes backwards.
    /// Any jump in progress is cancelled, since the section it was crossing is no longer there.
    /// </summary>
    public void WarpTo(double s)
    {
        s = Math.Clamp(s, 0, _track.Length);
        var moved = _track.MoveTo(Position, s);
        Shape = _shapes.Get(_track.SectionAt(s, moved.Branch));
        IsJumping = false;
        JumpProgress = 0f;
        Position = moved;
    }

    /// <summary>
    /// Pulls the ship back to where it met something, on the frame it hit it.
    ///
    /// Collision is swept across a whole frame of travel - over three units at full speed, against
    /// obstacles two units long - but the ship is drawn at the end of that sweep. Without this the
    /// hit lands with the ship already drawn clear of the thing it hit, which reads as being struck
    /// by nothing. Never moves the ship further back than where it began the frame.
    /// </summary>
    internal void StopAt(double s)
    {
        if (s >= Position.S) return;
        var moved = _track.MoveTo(Position, s);
        Shape = _shapes.Get(_track.SectionAt(s, moved.Branch));
        Position = moved;
    }

    /// <summary>Ship position and up direction in section space, <paramref name="rideHeight"/> above its surface.</summary>
    public (Vector2 Point, Vector2 Up) Pose(float rideHeight)
    {
        var up = Shape.NormalAt(Position.Surface, Position.X);
        var point = Shape.PointAt(Position.Surface, Position.X) + up * rideHeight;
        if (!IsJumping) return (point, up);

        var other = Opposite(Position.Surface);
        float otherX = Shape.Across(Position.Surface, Position.X);
        var otherUp = Shape.NormalAt(other, otherX);
        var target = Shape.PointAt(other, otherX) + otherUp * rideHeight;
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
        var other = Opposite(Position.Surface);
        float gap = Vector2.Distance(
            Shape.PointAt(Position.Surface, Position.X),
            Shape.PointAt(other, Shape.Across(Position.Surface, Position.X)));
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
        // Mid-jump across a ring, X is clamped rather than wrapped, and a wall of a ring is twice
        // the way round that half a tube is - clamping to a quarter would pin the ship to the side.
        if (Shape.IsAnnulus) return Shape.PerimeterOf(Position.Surface) * 0.5f;

        float q = Shape.Quarter, max = _settings.MaxPlaneOffset;
        if (Shape.IsClosed) return rejoining ? Math.Min(max, funnelFloor * q) : q;

        float play = Math.Min(q, max) + Math.Max(0f, max - q) * Shape.Spread;
        return Math.Min(Shape.SurfaceExtent, play);
    }
}
