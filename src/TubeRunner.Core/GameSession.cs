using System.Numerics;

namespace TubeRunner.Core;

public enum SessionState
{
    Playing,
    GameOver,
    Finished,
}

/// <summary>Things that happened during a step, for sound and effects.</summary>
public enum SessionEvent
{
    Fired,
    RingFired,
    Jumped,
    Hit,
    TargetDestroyed,
    BlockDamaged,
    BlockDestroyed,
    ShotBlocked,
    Rammed,
    WarpEntered,
    Warped,
    ThrustLimited,
    ThrustReleased,
    ShieldRestored,
    ShieldsRefilled,
    ShieldSlotAdded,
    RapidFireStarted,
    RingGunCharged,
    UnstoppableStarted,

    /// <summary>Unstoppable has <see cref="GameSession.RamWarning"/> seconds left.</summary>
    UnstoppableEnding,
    UnstoppableEnded,
    Finished,
    GameOver,
}

/// <summary>What a run carries from one level into the next: no pause, nothing reset.</summary>
public sealed record RunState(
    int Shields,
    int ExtraShields,
    float RapidFireLeft,
    int RingCharges,
    float RamLeft,
    float Throttle,
    int Score,
    float Momentum = 0f);

/// <param name="Throttle">In [-1, 1]; positive speeds up, negative slows down.</param>
/// <param name="Special">Fire the ring gun, if it has charges.</param>
/// <param name="Fire">The gun was pressed this frame. One press, one shot.</param>
/// <param name="FireHeld">
/// The gun is being held down. On its own that does nothing: holding only fires while rapid fire is
/// running, which is what rapid fire is for.
/// </param>
public readonly record struct ShipInput(
    float Steer = 0f,
    bool Jump = false,
    bool Fire = false,
    float Throttle = 0f,
    bool Special = false,
    bool FireHeld = false);

/// <param name="Ship">Movement settings.</param>
/// <param name="Shields">Shields at the start; the run ends when they run out.</param>
/// <param name="MaxExtraShields">Most extra shields that can be held at once, on top of the normal ones.</param>
/// <param name="RecoveryTime">Seconds of invulnerability and slow-down after a hit.</param>
/// <param name="HitSlowdown">Speed multiplier right after a hit, easing back to 1 over the recovery.</param>
/// <param name="ShotSpeed">Shot speed on top of the ship's own; ring shots too.</param>
/// <param name="ShotRange">Distance a shot travels before it fades.</param>
/// <param name="FireInterval">
/// Shortest gap between shots. It was slowed to 0.32 when holding the trigger fired continuously,
/// to stop a held button clearing everything; with one shot per press the ceiling is how fast a
/// person can tap, so the cooldown can come back down and stop feeling sluggish.
/// </param>
/// <param name="RapidFireTime">Seconds a rapid-fire pickup lasts.</param>
/// <param name="RapidFireFactor">
/// Fire interval multiplier during rapid fire. Faster than it used to be, on purpose: the default
/// gun lost the ability to be held at all, so the prize that gives it back should be worth finding.
/// Nerf the default, buy the power-up.
/// </param>
/// <param name="RingChargesPerPickup">Ring gun shots each ring-gun pickup gives.</param>
/// <param name="RamTime">Seconds an unstoppable pickup lasts.</param>
/// <param name="RingRange">Distance a ring shot sweeps before it fades.</param>
/// <param name="RingReach">On open planes, how far to either side a ring shot reaches.</param>
/// <param name="PickupRadius">How close the ship has to pass to a pickup to collect it.</param>
/// <param name="ShipHalfWidth">Collision half-size across the surface.</param>
/// <param name="ShipHalfLength">Collision half-size along the track.</param>
/// <param name="FinishRunOut">
/// Distance before the end of the track where the level counts as finished. Well clear of the end,
/// because levels hand over to one another with no pause: the wall closing the track should still be
/// deep in the distance fade when the next level takes over, so the tube reads as carrying on rather
/// than as something the run stopped at. Every level's last piece is empty run-out for this.
/// </param>
/// <param name="WarpBack">How far a warp zone throws the ship back up the track.</param>
/// <param name="WarpDive">Seconds the ship spends falling down a warp mouth before it is thrown back.</param>
/// <param name="ThrustZoneCut">
/// Share of the ship's throttle range, from the bottom, that a thrust zone closes off. Small on
/// purpose: a zone asks the player not to crawl through, it does not pick their speed for them.
/// </param>
/// <param name="ThrustZoneCap">
/// Share of the range, from the top, that a ceiling zone closes off. Large on purpose: being held
/// back is only a cost when it holds the player well below the speed they want.
/// </param>
/// <param name="MomentumBuildTime">Seconds of flying without a hit to take momentum from nothing to full.</param>
/// <param name="MomentumHitLoss">Momentum a hit takes away. Two or three close together empty it.</param>
public sealed record SessionSettings(
    ShipSettings Ship,
    int Shields = 3,
    int MaxExtraShields = 3,
    float RecoveryTime = 1.5f,
    float HitSlowdown = 0.45f,
    float ShotSpeed = 220f,
    float ShotRange = 300f,
    float FireInterval = 0.18f,
    float RapidFireTime = 8f,
    float RapidFireFactor = 0.35f,
    int RingChargesPerPickup = 3,
    float RamTime = 6f,
    float RingRange = 250f,
    float RingReach = 45f,
    float PickupRadius = 1.5f,
    float ShipHalfWidth = 0.6f,
    float ShipHalfLength = 0.8f,
    float FinishRunOut = 260f,
    float WarpBack = 250f,
    float WarpDive = 0.55f,
    float ThrustZoneCut = 0.2f,
    float ThrustZoneCap = 0.75f,
    float MomentumBuildTime = 90f,
    float MomentumHitLoss = 0.35f);

/// <summary>A shot flying down the track ahead of the ship.</summary>
public sealed class Shot
{
    public TrackPosition Position { get; internal set; }

    /// <summary>Height off its surface.</summary>
    public float Height { get; init; }

    internal double End { get; init; }
}

/// <summary>A ring gun shot: it sweeps down the track, hitting everything all the way around.</summary>
public sealed class RingShot
{
    public TrackPosition Position { get; internal set; }

    internal double End { get; init; }

    /// <summary>
    /// Ordered groups this sweep has already taken a member from. A ring overlaps an obstacle for
    /// several frames, so without this it would clear a whole group one member per frame.
    /// </summary>
    internal HashSet<string>? TakenGroups { get; set; }
}

/// <summary>
/// One run through a level: the ship, obstacles, power-ups, shots, shields, and score. All
/// collision is done in track space, swept along the track so nothing is skipped at high speed.
/// </summary>
public sealed class GameSession
{
    public const int TargetPoints = 100;
    public const int BreakPoints = 50;
    public const int PickupPoints = 25;

    /// <summary>
    /// Seconds of unstoppable left when the warning sounds. The player is flying into things on
    /// purpose while it runs, so the moment it stops is the moment a habit starts costing shields -
    /// and a number in the corner of the HUD is not where their eyes are.
    /// </summary>
    public const float RamWarning = 1f;

    // Obstacles and pickups are only tested within this distance of the swept range.
    private const double SearchMargin = 20.0;
    private const float ShotRadius = 0.3f;

    private readonly SessionSettings _settings;
    private readonly List<Obstacle> _obstacles;
    private readonly List<Pickup> _pickups;
    private readonly List<Warp> _warps;
    private readonly List<ThrustZone> _thrustZones;
    private ThrustZone? _inZone;
    private readonly List<Shot> _shots = new();
    private readonly List<RingShot> _rings = new();
    private readonly List<SessionEvent> _events = new();
    private readonly ProfileShapeCache _shapes = new();
    private float _fireCooldown;
    private int _bonus;
    private int _carriedScore;

    /// <param name="carry">State from earlier levels of the same run; null starts fresh.</param>
    public GameSession(Track track, IEnumerable<Obstacle> obstacles, SessionSettings settings, TrackPosition start,
        IEnumerable<Pickup>? pickups = null, RunState? carry = null, IEnumerable<Warp>? warps = null,
        IEnumerable<ThrustZone>? thrustZones = null)
    {
        Track = track;
        _settings = settings;
        _obstacles = obstacles.OrderBy(o => o.S).ToList();
        _pickups = (pickups ?? []).OrderBy(p => p.S).ToList();
        _warps = (warps ?? []).OrderBy(w => w.S).ToList();
        _thrustZones = (thrustZones ?? []).OrderBy(z => z.S).ToList();
        Ship = new ShipSim(settings.Ship, track, start);
        MaxShields = settings.Shields;
        Shields = settings.Shields;

        if (carry is not null)
        {
            Shields = carry.Shields;
            ExtraShields = carry.ExtraShields;
            RapidFireLeft = carry.RapidFireLeft;
            RingCharges = carry.RingCharges;
            RamLeft = carry.RamLeft;
            Ship.Throttle = carry.Throttle;
            Momentum = carry.Momentum;
            _carriedScore = carry.Score;
        }
    }

    public Track Track { get; }
    public SessionSettings Settings => _settings;
    public ShipSim Ship { get; }
    public IReadOnlyList<Obstacle> Obstacles => _obstacles;
    public IReadOnlyList<Pickup> Pickups => _pickups;
    public IReadOnlyList<Warp> Warps => _warps;
    public IReadOnlyList<ThrustZone> ThrustZones => _thrustZones;

    /// <summary>The thrust zone the ship is inside, if any. The HUD shows what it is doing.</summary>
    public ThrustZone? InThrustZone => _inZone;
    public IReadOnlyList<Shot> Shots => _shots;
    public IReadOnlyList<RingShot> Rings => _rings;

    /// <summary>What happened during the last step.</summary>
    public IReadOnlyList<SessionEvent> Events => _events;

    public SessionState State { get; private set; } = SessionState.Playing;
    public int Shields { get; private set; }

    /// <summary>Normal shield slots. Fixed: extra shields are a separate pool, not a bigger bar.</summary>
    public int MaxShields { get; }

    /// <summary>
    /// Extra shields held, on top of the normal ones. They are spent first, and neither a shield
    /// pickup nor a full refill touches them - once an extra is gone it takes another extra-shield
    /// pickup to get one back. That is what makes them worth going off the fast line for.
    /// </summary>
    public int ExtraShields { get; private set; }

    /// <summary>Shields of any kind left. The run ends when this reaches zero.</summary>
    public int TotalShields => Shields + ExtraShields;

    /// <summary>Seconds left of rapid fire.</summary>
    public float RapidFireLeft { get; private set; }

    /// <summary>Ring gun shots available.</summary>
    public int RingCharges { get; private set; }

    /// <summary>Seconds left of smashing through anything the ship touches.</summary>
    public float RamLeft { get; private set; }

    /// <summary>
    /// How well the run is going, from 0 to 1: it climbs steadily while the ship flies without being
    /// hit and a hit knocks a chunk off it. The music builds from it. It carries between levels, since
    /// a run is one piece of flying and the music should not start over at every handover.
    /// </summary>
    public float Momentum { get; private set; }

    /// <summary>Seconds left of post-hit invulnerability.</summary>
    public float RecoveryLeft { get; private set; }

    /// <summary>The warp mouth the ship is falling down, if it is in one.</summary>
    public Warp? Diving { get; private set; }

    /// <summary>How far through the dive, from 0 at the mouth to 1 when it throws.</summary>
    public float DiveProgress => Diving is null ? 0f : 1f - DiveLeft / _settings.WarpDive;

    private float DiveLeft { get; set; }

    /// <summary>Seconds played so far. It stops when the run ends, so after finishing it's the level time.</summary>
    public float Elapsed { get; private set; }

    /// <summary>Points for targets, broken blocks, and pickups, plus one point per 10 units travelled.</summary>
    public int Score => _carriedScore + _bonus + (int)(Ship.Position.S / 10.0);

    /// <summary>State to carry into the next level of the run.</summary>
    public RunState Carry => new(Shields, ExtraShields, RapidFireLeft, RingCharges, RamLeft, Ship.Throttle, Score, Momentum);

    public void Step(float dt, ShipInput input)
    {
        _events.Clear();
        if (State != SessionState.Playing) return;

        Elapsed += dt;
        RapidFireLeft = Math.Max(0f, RapidFireLeft - dt);
        float ramBefore = RamLeft;
        RamLeft = Math.Max(0f, RamLeft - dt);
        if (ramBefore > RamWarning && RamLeft <= RamWarning) _events.Add(SessionEvent.UnstoppableEnding);
        if (ramBefore > 0f && RamLeft <= 0f) _events.Add(SessionEvent.UnstoppableEnded);
        RecoveryLeft = Math.Max(0f, RecoveryLeft - dt);

        // Down a warp mouth the ship holds station and nothing else happens to it, but the clock
        // keeps running: the whole cost of a warp is time.
        if (Diving is not null)
        {
            DiveLeft -= dt;
            if (DiveLeft <= 0f) FinishWarp();
            return;
        }

        // Only while actually flying: a warp dive is a mistake being paid for, not progress.
        Momentum = Math.Min(1f, Momentum + dt / _settings.MomentumBuildTime);

        float recovered = 1f - RecoveryLeft / _settings.RecoveryTime;
        Ship.SpeedScale = _settings.HitSlowdown + (1f - _settings.HitSlowdown) * recovered;
        ApplyThrustZones();

        double before = Ship.Position.S;
        bool wasJumping = Ship.IsJumping;
        Ship.Step(dt, input.Steer, input.Jump, input.Throttle);
        if (Ship.IsJumping && !wasJumping) _events.Add(SessionEvent.Jumped);

        CheckShipHits(before, Ship.Position.S);
        CollectPickups(before, Ship.Position.S);
        CheckWarps(before, Ship.Position.S);
        MoveShots(dt);
        MoveRings(dt);

        _fireCooldown = Math.Max(0f, _fireCooldown - dt);
        // One press, one shot. Holding the trigger only fires while rapid fire is running - without
        // that, a held button cleared every path in the game and there was no decision left in
        // shooting at all. It also gives rapid fire something to actually be.
        bool wantsShot = input.Fire || (input.FireHeld && RapidFireLeft > 0f);
        if (State == SessionState.Playing && wantsShot && _fireCooldown <= 0f) Fire();
        if (State == SessionState.Playing && input.Special && RingCharges > 0) FireRing();

        if (State == SessionState.Playing && Ship.Position.S >= Track.Length - _settings.FinishRunOut)
        {
            State = SessionState.Finished;
            _events.Add(SessionEvent.Finished);
        }
    }

    private void CheckShipHits(double from, double to)
    {
        var pos = Ship.Position;
        foreach (var o in Nearby(_obstacles, o => o.S, from, to))
        {
            if (o.Destroyed || o.Branch != pos.Branch || !IsSolid(o)) continue;
            double reach = o.Length / 2f + _settings.ShipHalfLength;
            if (to < o.S - reach || from > o.S + reach) continue;

            // Mid-jump the ship is between the surfaces at the same X; otherwise it's on one. A mover
            // is wherever its sweep has carried it by now, not where it was authored.
            float x = o.XAt(Elapsed);
            float lateral = Ship.IsJumping
                ? MathF.Abs(pos.X - x)
                : TrackSpace.SurfaceDistance(ShapeAt(o.S, o.Branch), pos.Surface, pos.X, o.Surface, x);
            if (lateral >= o.Width / 2f + _settings.ShipHalfWidth || Ship.HeightAbove(o.Surface) >= o.Height) continue;

            // Unstoppable: smash straight through and score it, with no shield lost. A plate is the
            // one thing it cannot answer - that is the whole reason plates exist.
            if (RamLeft > 0f && o.Kind != ObstacleKind.Plate)
            {
                Break(o, Points(o), SessionEvent.Rammed);
                continue;
            }
            if (RecoveryLeft > 0f) continue;

            // Whatever the ship hits breaks apart, so it doesn't fly on through it. A plate is wall:
            // it stays, and flying into it again costs again.
            if (o.Kind != ObstacleKind.Plate) o.Destroyed = true;
            // Stop where the two actually met, rather than wherever this frame's travel happened to
            // end. The sweep can carry the ship a body length past an obstacle before the hit is
            // noticed, and it is drawn where it ends up, so the hit appears to land on nothing.
            Ship.StopAt(Math.Max(from, o.S - reach));
            if (!TakeHit()) return;
        }
    }

    // Thrust zones narrow the throttle range while the ship is inside one. Nothing is taken away
    // for flying wrong: the cost is control, and the HUD shows the range closing as it happens, so
    // it is a thing to fly around rather than a hit out of nowhere.
    private void ApplyThrustZones()
    {
        var pos = Ship.Position;
        ThrustZone? found = null;
        foreach (var z in _thrustZones)
        {
            if (z.Branch != pos.Branch || !z.Contains(pos.S)) continue;
            found = z;
            z.Entered = true;
            break;
        }

        if (found != _inZone)
        {
            _events.Add(found is null ? SessionEvent.ThrustReleased : SessionEvent.ThrustLimited);
            _inZone = found;
        }

        // Each kind takes one end of the range, always by the same share. A throttle already inside
        // what is left stays exactly where it is; only one outside it is moved to the cut.
        var ship = _settings.Ship;
        float span = ship.MaxThrottle - ship.MinThrottle;
        Ship.ThrottleFloor = found?.Kind == ThrustZoneKind.Floor
            ? ship.MinThrottle + _settings.ThrustZoneCut * span
            : ship.MinThrottle;
        Ship.ThrottleCeiling = found?.Kind == ThrustZoneKind.Ceiling
            ? ship.MaxThrottle - _settings.ThrustZoneCap * span
            : ship.MaxThrottle;
    }

    /// <returns>False if that was the last shield and the run is over.</returns>
    private bool TakeHit()
    {
        // Extras go first. They sit past the normal shields on the bar, so that is the one the eye
        // expects to lose - and holding them back would mean they were almost never spent, which
        // would make "an extra cannot be refilled" a rule that never came up.
        Momentum = Math.Max(0f, Momentum - _settings.MomentumHitLoss);
        if (ExtraShields > 0) ExtraShields--;
        else Shields--;

        RecoveryLeft = _settings.RecoveryTime;
        _events.Add(SessionEvent.Hit);
        if (TotalShields > 0) return true;

        State = SessionState.GameOver;
        _events.Add(SessionEvent.GameOver);
        return false;
    }

    // A gate is only there for half its cycle. Anything without a period is always solid.
    private bool IsSolid(Obstacle o) => o.IsSolidAt(Elapsed) && !IsUnlocked(o);

    /// <summary>
    /// Whether a locked obstacle has had its keys shot and is no longer in the way. The view needs
    /// this as much as the collision does: an unlocked door is not destroyed, just no longer solid,
    /// so anything drawing obstacles by whether they are destroyed will leave a door standing that
    /// the ship then flies straight through.
    /// </summary>
    public bool IsUnlocked(Obstacle o) => o.LockedBy is not null && KeysAreDown(o.LockedBy);

    /// <summary>
    /// Whether a power-up pad is live: its keys are down, or it never had any. A locked pad is dead
    /// until its group is shot out - flying over it does nothing - so the view has to ask this too,
    /// or a pad that cannot be taken would sit there looking exactly like one that can.
    /// </summary>
    public bool IsLive(Pickup p) => p.LockedBy is null || KeysAreDown(p.LockedBy);

    /// <summary>Whether every target in a key group has been destroyed.</summary>
    private bool KeysAreDown(string group)
    {
        foreach (var key in _obstacles)
        {
            if (key.Group == group && !key.Destroyed) return false;
        }
        return true;
    }

    /// <summary>
    /// Whether an obstacle's turn has come: within an ordered group, a target only breaks once
    /// everything earlier in it has gone. Public because the view needs it too - a group where every
    /// target looks the same is a puzzle with its answer hidden, not a puzzle.
    /// </summary>
    public bool CanBreak(Obstacle o)
    {
        if (o.Group is null) return true;
        foreach (var other in _obstacles)
        {
            if (other.Group == o.Group && other.Order < o.Order && !other.Destroyed) return false;
        }
        return true;
    }

    private void CollectPickups(double from, double to)
    {
        var pos = Ship.Position;
        double reach = _settings.PickupRadius + _settings.ShipHalfLength;
        foreach (var p in Nearby(_pickups, p => p.S, from, to))
        {
            if (p.Collected || p.Branch != pos.Branch || to < p.S - reach || from > p.S + reach) continue;
            // A locked pad is not there yet. Passing over one before its keys are down leaves it
            // behind rather than arming it, which is what makes the key worth the detour.
            if (!IsLive(p)) continue;

            float lateral = Ship.IsJumping
                ? MathF.Abs(pos.X - p.X)
                : TrackSpace.SurfaceDistance(ShapeAt(p.S, p.Branch), pos.Surface, pos.X, p.Surface, p.X);
            // Pickups are set into the surface, so the ship has to be riding it (or just leaving it).
            if (lateral >= _settings.PickupRadius + _settings.ShipHalfWidth || Ship.HeightAbove(p.Surface) > 1f) continue;

            p.Collected = true;
            _bonus += PickupPoints;
            Apply(p.Kind);
        }
    }

    // Warp wells: a hole in the wall that throws the ship back up the track. The cost is time, not a
    // shield, and a well stays armed - fly into the same one again and it takes you again. The sweep
    // stays forward-only because this runs before the ship is moved back.
    private void CheckWarps(double from, double to)
    {
        var pos = Ship.Position;
        foreach (var w in Nearby(_warps, w => w.S, from, to))
        {
            if (w.Branch != pos.Branch) continue;
            double reach = w.Length / 2f + _settings.ShipHalfLength;
            if (to < w.S - reach || from > w.S + reach) continue;

            var shape = ShapeAt(w.S, w.Branch);
            float lateral = Ship.IsJumping
                ? MathF.Abs(pos.X - w.X)
                : TrackSpace.SurfaceDistance(shape, pos.Surface, pos.X, w.Surface, w.X);
            if (lateral >= w.WidthOn(shape) / 2f + _settings.ShipHalfWidth) continue;

            w.Used = true;
            Diving = w;
            DiveLeft = _settings.WarpDive;
            _events.Add(SessionEvent.WarpEntered);
            return;
        }
    }

    private void FinishWarp()
    {
        var w = Diving!;
        Diving = null;
        DiveLeft = 0f;
        Ship.WarpTo(w.S - (w.Back > 0f ? w.Back : _settings.WarpBack));
        _events.Add(SessionEvent.Warped);
    }

    /// <summary>
    /// Where the ship sits while falling down a warp mouth, in the section space of the mouth's own
    /// distance along the track. <paramref name="depth"/> is how far it has sunk through the wall.
    /// </summary>
    public (Vector2 Point, Vector2 Up) DivePose(float depth)
    {
        var w = Diving ?? throw new InvalidOperationException("The ship is not in a warp.");
        var shape = ShapeAt(w.S, w.Branch);
        var up = shape.NormalAt(w.Surface, w.X);
        return (shape.PointAt(w.Surface, w.X) - up * depth, up);
    }

    private void Apply(PickupKind kind)
    {
        switch (kind)
        {
            // Both of these fill normal shields only. An extra is never handed back this way.
            case PickupKind.Shield:
                Shields = Math.Min(MaxShields, Shields + 1);
                _events.Add(SessionEvent.ShieldRestored);
                break;
            case PickupKind.FullShields:
                Shields = MaxShields;
                _events.Add(SessionEvent.ShieldsRefilled);
                break;
            case PickupKind.ShieldSlot:
                // Purely an extra: with 2 of 3 normal shields this gives a fourth point, and leaves
                // the empty third still empty.
                ExtraShields = Math.Min(_settings.MaxExtraShields, ExtraShields + 1);
                _events.Add(SessionEvent.ShieldSlotAdded);
                break;
            case PickupKind.RapidFire:
                RapidFireLeft = _settings.RapidFireTime;
                _events.Add(SessionEvent.RapidFireStarted);
                break;
            case PickupKind.RingGun:
                RingCharges += _settings.RingChargesPerPickup;
                _events.Add(SessionEvent.RingGunCharged);
                break;
            case PickupKind.Unstoppable:
                RamLeft = _settings.RamTime;
                _events.Add(SessionEvent.UnstoppableStarted);
                break;
        }
    }

    private void Fire()
    {
        var pos = Ship.Position;
        // Mid-jump, shots leave along whichever surface the ship is closer to.
        var surface = Ship.IsJumping && Ship.JumpProgress >= 0.5f ? ShipSim.Opposite(pos.Surface) : pos.Surface;
        _shots.Add(new Shot
        {
            Position = Track.MoveTo(pos with { Surface = surface }, pos.S + _settings.ShipHalfLength),
            Height = Ship.HeightAbove(surface),
            End = pos.S + _settings.ShotRange,
        });
        _fireCooldown = _settings.FireInterval * (RapidFireLeft > 0f ? _settings.RapidFireFactor : 1f);
        _events.Add(SessionEvent.Fired);
    }

    private void FireRing()
    {
        RingCharges--;
        var pos = Ship.Position;
        _rings.Add(new RingShot
        {
            Position = Track.MoveTo(pos, pos.S + _settings.ShipHalfLength),
            End = pos.S + _settings.RingRange,
        });
        _events.Add(SessionEvent.RingFired);
    }

    private void MoveShots(float dt)
    {
        double step = (Ship.ForwardSpeed + _settings.ShotSpeed) * dt;
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var shot = _shots[i];
            double from = shot.Position.S;
            shot.Position = Track.MoveTo(shot.Position, from + step);

            var hit = FirstShotHit(shot, from, shot.Position.S);
            if (hit is not null)
            {
                ShotHit(hit);
                _shots.RemoveAt(i);
            }
            else if (shot.Position.S >= shot.End || shot.Position.S >= Track.Length)
            {
                _shots.RemoveAt(i);
            }
        }
    }

    private void ShotHit(Obstacle o)
    {
        // A plate cannot be shot, and a target out of its group's turn refuses the shot, so the
        // player has to work out the order rather than hold the trigger down.
        if (o.Kind == ObstacleKind.Plate || !CanBreak(o)) _events.Add(SessionEvent.ShotBlocked);
        else if (o.Kind == ObstacleKind.Target) Break(o, TargetPoints, SessionEvent.TargetDestroyed);
        else if (o.Hits <= 0) _events.Add(SessionEvent.ShotBlocked);
        else if (++o.HitsTaken >= o.Hits) Break(o, Points(o), SessionEvent.BlockDestroyed);
        else _events.Add(SessionEvent.BlockDamaged);
    }

    // Targets pay a flat rate; a block pays by how much shooting it takes to break.
    private static int Points(Obstacle o) =>
        o.Kind == ObstacleKind.Target ? TargetPoints : BreakPoints * Math.Max(1, o.Hits);

    private void Break(Obstacle o, int points, SessionEvent e)
    {
        o.Destroyed = true;
        _bonus += points;
        _events.Add(e);
    }

    // Ring shots break every target and breakable block they pass, all the way around the tube,
    // and pass unbreakable blocks by.
    private void MoveRings(float dt)
    {
        double step = (Ship.ForwardSpeed + _settings.ShotSpeed) * dt;
        for (int i = _rings.Count - 1; i >= 0; i--)
        {
            var ring = _rings[i];
            double from = ring.Position.S;
            int branchFrom = ring.Position.Branch;
            ring.Position = Track.MoveTo(ring.Position, from + step);
            double to = ring.Position.S;
            int branchTo = ring.Position.Branch;

            foreach (var o in Nearby(_obstacles, o => o.S, from, to))
            {
                // Match either branch the ring was on this step. A ring crossing a fork or merge
                // changes branch mid-step, and testing only where it ended made it pass straight
                // through everything it had just swept on the branch it left.
                if (o.Destroyed || (o.Branch != branchFrom && o.Branch != branchTo)) continue;
                if (to < o.S - o.Length / 2f || from > o.S + o.Length / 2f) continue;
                // On open planes the ring reaches a fixed distance to either side, on floor and ceiling.
                if (!ShapeAt(o.S, o.Branch).IsClosed && MathF.Abs(o.XAt(Elapsed)) > _settings.RingReach) continue;
                if (o.Kind == ObstacleKind.Plate) continue;

                // A ring takes at most one member of an ordered group: the one whose turn it is, and
                // then it is spent on that group. Skipping groups outright made the ring pass through
                // targets that were ready to break, which reads as the shot simply not working; just
                // testing the order is not enough either, because a ring overlaps an obstacle for
                // several frames and would clear the whole group a member at a time.
                if (o.Group is not null)
                {
                    if (!CanBreak(o)) continue;
                    ring.TakenGroups ??= [];
                    if (!ring.TakenGroups.Add(o.Group)) continue;
                }

                if (o.Kind == ObstacleKind.Target) Break(o, TargetPoints, SessionEvent.TargetDestroyed);
                else if (o.Hits > 0) Break(o, Points(o), SessionEvent.BlockDestroyed);
            }

            if (to >= ring.End || to >= Track.Length) _rings.RemoveAt(i);
        }
    }

    private Obstacle? FirstShotHit(Shot shot, double from, double to)
    {
        var pos = shot.Position;
        Obstacle? first = null;
        foreach (var o in Nearby(_obstacles, o => o.S, from, to))
        {
            if (o.Destroyed || o.Branch != pos.Branch || !IsSolid(o)) continue;
            // A target waiting its turn lets shots pass. Swallowing them would make an ordered group
            // unsolvable whenever the one to shoot first stands behind one that comes later.
            if (o.Kind == ObstacleKind.Target && !CanBreak(o)) continue;
            if (to < o.S - o.Length / 2f || from > o.S + o.Length / 2f || shot.Height >= o.Height) continue;
            float lateral = TrackSpace.SurfaceDistance(ShapeAt(o.S, o.Branch), pos.Surface, pos.X, o.Surface, o.XAt(Elapsed));
            if (lateral >= o.Width / 2f + ShotRadius) continue;
            if (first is null || o.S < first.S) first = o;
        }
        return first;
    }

    private ProfileShape ShapeAt(double s, int branch) => _shapes.Get(Track.SectionAt(s, branch));

    // Items from a list sorted by distance that lie within the search margin of [from, to].
    private static IEnumerable<T> Nearby<T>(List<T> sorted, Func<T, double> s, double from, double to)
    {
        int lo = 0, hi = sorted.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (s(sorted[mid]) < from - SearchMargin) lo = mid + 1;
            else hi = mid;
        }
        for (int i = lo; i < sorted.Count && s(sorted[i]) <= to + SearchMargin; i++) yield return sorted[i];
    }
}
