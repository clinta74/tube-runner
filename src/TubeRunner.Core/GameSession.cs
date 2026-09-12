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
    Warped,
    ShieldRestored,
    ShieldsRefilled,
    ShieldSlotAdded,
    RapidFireStarted,
    RingGunCharged,
    UnstoppableStarted,
    Finished,
    GameOver,
}

/// <summary>What a run carries from one level into the next: no pause, nothing reset.</summary>
public sealed record RunState(
    int Shields,
    int MaxShields,
    float RapidFireLeft,
    int RingCharges,
    float RamLeft,
    float Throttle,
    int Score);

/// <param name="Throttle">In [-1, 1]; positive speeds up, negative slows down.</param>
/// <param name="Special">Fire the ring gun, if it has charges.</param>
public readonly record struct ShipInput(float Steer = 0f, bool Jump = false, bool Fire = false, float Throttle = 0f, bool Special = false);

/// <param name="Ship">Movement settings.</param>
/// <param name="Shields">Shields at the start; the run ends when they run out.</param>
/// <param name="MaxShieldSlots">Most shield slots extra-slot pickups can build up to.</param>
/// <param name="RecoveryTime">Seconds of invulnerability and slow-down after a hit.</param>
/// <param name="HitSlowdown">Speed multiplier right after a hit, easing back to 1 over the recovery.</param>
/// <param name="ShotSpeed">Shot speed on top of the ship's own; ring shots too.</param>
/// <param name="ShotRange">Distance a shot travels before it fades.</param>
/// <param name="FireInterval">Seconds between shots while fire is held.</param>
/// <param name="RapidFireTime">Seconds a rapid-fire pickup lasts.</param>
/// <param name="RapidFireFactor">Fire interval multiplier during rapid fire.</param>
/// <param name="RingChargesPerPickup">Ring gun shots each ring-gun pickup gives.</param>
/// <param name="RamTime">Seconds an unstoppable pickup lasts.</param>
/// <param name="RingRange">Distance a ring shot sweeps before it fades.</param>
/// <param name="RingReach">On open planes, how far to either side a ring shot reaches.</param>
/// <param name="PickupRadius">How close the ship has to pass to a pickup to collect it.</param>
/// <param name="ShipHalfWidth">Collision half-size across the surface.</param>
/// <param name="ShipHalfLength">Collision half-size along the track.</param>
/// <param name="FinishRunOut">Distance before the end of the track where the level counts as finished.</param>
/// <param name="WarpBack">How far a warp zone throws the ship back up the track.</param>
public sealed record SessionSettings(
    ShipSettings Ship,
    int Shields = 3,
    int MaxShieldSlots = 6,
    float RecoveryTime = 1.5f,
    float HitSlowdown = 0.45f,
    float ShotSpeed = 220f,
    float ShotRange = 300f,
    float FireInterval = 0.32f,
    float RapidFireTime = 8f,
    float RapidFireFactor = 0.35f,
    int RingChargesPerPickup = 3,
    float RamTime = 6f,
    float RingRange = 250f,
    float RingReach = 45f,
    float PickupRadius = 1.5f,
    float ShipHalfWidth = 0.6f,
    float ShipHalfLength = 0.8f,
    float FinishRunOut = 40f,
    float WarpBack = 250f);

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

    // Obstacles and pickups are only tested within this distance of the swept range.
    private const double SearchMargin = 20.0;
    private const float ShotRadius = 0.3f;

    private readonly SessionSettings _settings;
    private readonly List<Obstacle> _obstacles;
    private readonly List<Pickup> _pickups;
    private readonly List<Warp> _warps;
    private readonly List<Shot> _shots = new();
    private readonly List<RingShot> _rings = new();
    private readonly List<SessionEvent> _events = new();
    private readonly ProfileShapeCache _shapes = new();
    private float _fireCooldown;
    private int _bonus;
    private int _carriedScore;

    /// <param name="carry">State from earlier levels of the same run; null starts fresh.</param>
    public GameSession(Track track, IEnumerable<Obstacle> obstacles, SessionSettings settings, TrackPosition start,
        IEnumerable<Pickup>? pickups = null, RunState? carry = null, IEnumerable<Warp>? warps = null)
    {
        Track = track;
        _settings = settings;
        _obstacles = obstacles.OrderBy(o => o.S).ToList();
        _pickups = (pickups ?? []).OrderBy(p => p.S).ToList();
        _warps = (warps ?? []).OrderBy(w => w.S).ToList();
        Ship = new ShipSim(settings.Ship, track, start);
        Shields = MaxShields = settings.Shields;

        if (carry is not null)
        {
            Shields = carry.Shields;
            MaxShields = carry.MaxShields;
            RapidFireLeft = carry.RapidFireLeft;
            RingCharges = carry.RingCharges;
            RamLeft = carry.RamLeft;
            Ship.Throttle = carry.Throttle;
            _carriedScore = carry.Score;
        }
    }

    public Track Track { get; }
    public SessionSettings Settings => _settings;
    public ShipSim Ship { get; }
    public IReadOnlyList<Obstacle> Obstacles => _obstacles;
    public IReadOnlyList<Pickup> Pickups => _pickups;
    public IReadOnlyList<Warp> Warps => _warps;
    public IReadOnlyList<Shot> Shots => _shots;
    public IReadOnlyList<RingShot> Rings => _rings;

    /// <summary>What happened during the last step.</summary>
    public IReadOnlyList<SessionEvent> Events => _events;

    public SessionState State { get; private set; } = SessionState.Playing;
    public int Shields { get; private set; }

    /// <summary>Shield slots; extra-slot pickups raise it.</summary>
    public int MaxShields { get; private set; }

    /// <summary>Seconds left of rapid fire.</summary>
    public float RapidFireLeft { get; private set; }

    /// <summary>Ring gun shots available.</summary>
    public int RingCharges { get; private set; }

    /// <summary>Seconds left of smashing through anything the ship touches.</summary>
    public float RamLeft { get; private set; }

    /// <summary>Seconds left of post-hit invulnerability.</summary>
    public float RecoveryLeft { get; private set; }

    /// <summary>Seconds played so far. It stops when the run ends, so after finishing it's the level time.</summary>
    public float Elapsed { get; private set; }

    /// <summary>Points for targets, broken blocks, and pickups, plus one point per 10 units travelled.</summary>
    public int Score => _carriedScore + _bonus + (int)(Ship.Position.S / 10.0);

    /// <summary>State to carry into the next level of the run.</summary>
    public RunState Carry => new(Shields, MaxShields, RapidFireLeft, RingCharges, RamLeft, Ship.Throttle, Score);

    public void Step(float dt, ShipInput input)
    {
        _events.Clear();
        if (State != SessionState.Playing) return;

        Elapsed += dt;
        RapidFireLeft = Math.Max(0f, RapidFireLeft - dt);
        RamLeft = Math.Max(0f, RamLeft - dt);
        RecoveryLeft = Math.Max(0f, RecoveryLeft - dt);
        float recovered = 1f - RecoveryLeft / _settings.RecoveryTime;
        Ship.SpeedScale = _settings.HitSlowdown + (1f - _settings.HitSlowdown) * recovered;

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
        if (State == SessionState.Playing && input.Fire && _fireCooldown <= 0f) Fire();
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
            if (o.Destroyed || o.Branch != pos.Branch) continue;
            double reach = o.Length / 2f + _settings.ShipHalfLength;
            if (to < o.S - reach || from > o.S + reach) continue;

            // Mid-jump the ship is between the surfaces at the same X; otherwise it's on one.
            float lateral = Ship.IsJumping
                ? MathF.Abs(pos.X - o.X)
                : TrackSpace.SurfaceDistance(ShapeAt(o.S, o.Branch), pos.Surface, pos.X, o.Surface, o.X);
            if (lateral >= o.Width / 2f + _settings.ShipHalfWidth || Ship.HeightAbove(o.Surface) >= o.Height) continue;

            // Unstoppable: smash straight through and score it, with no shield lost.
            if (RamLeft > 0f)
            {
                Break(o, Points(o), SessionEvent.Rammed);
                continue;
            }
            if (RecoveryLeft > 0f) continue;

            // Whatever the ship hits breaks apart, so it doesn't fly on through it.
            o.Destroyed = true;
            Shields--;
            RecoveryLeft = _settings.RecoveryTime;
            _events.Add(SessionEvent.Hit);
            if (Shields <= 0)
            {
                State = SessionState.GameOver;
                _events.Add(SessionEvent.GameOver);
                return;
            }
        }
    }

    private void CollectPickups(double from, double to)
    {
        var pos = Ship.Position;
        double reach = _settings.PickupRadius + _settings.ShipHalfLength;
        foreach (var p in Nearby(_pickups, p => p.S, from, to))
        {
            if (p.Collected || p.Branch != pos.Branch || to < p.S - reach || from > p.S + reach) continue;

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

    // Warp zones: a mouth in the wall that throws the ship back up the track. The cost is time, not
    // a shield, and each fires once so repeatedly clipping the same one can't trap the run. The
    // sweep stays forward-only because this runs before the ship is moved back.
    private void CheckWarps(double from, double to)
    {
        var pos = Ship.Position;
        foreach (var w in Nearby(_warps, w => w.S, from, to))
        {
            if (w.Used || w.Branch != pos.Branch) continue;
            double reach = w.Length / 2f + _settings.ShipHalfLength;
            if (to < w.S - reach || from > w.S + reach) continue;

            float lateral = Ship.IsJumping
                ? MathF.Abs(pos.X - w.X)
                : TrackSpace.SurfaceDistance(ShapeAt(w.S, w.Branch), pos.Surface, pos.X, w.Surface, w.X);
            if (lateral >= w.Width / 2f + _settings.ShipHalfWidth) continue;

            w.Used = true;
            Ship.WarpTo(w.S - (w.Back > 0f ? w.Back : _settings.WarpBack));
            _events.Add(SessionEvent.Warped);
            return;
        }
    }

    private void Apply(PickupKind kind)
    {
        switch (kind)
        {
            case PickupKind.Shield:
                Shields = Math.Min(MaxShields, Shields + 1);
                _events.Add(SessionEvent.ShieldRestored);
                break;
            case PickupKind.FullShields:
                Shields = MaxShields;
                _events.Add(SessionEvent.ShieldsRefilled);
                break;
            case PickupKind.ShieldSlot:
                MaxShields = Math.Min(_settings.MaxShieldSlots, MaxShields + 1);
                Shields = Math.Min(MaxShields, Shields + 1);
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
        if (o.Kind == ObstacleKind.Target) Break(o, TargetPoints, SessionEvent.TargetDestroyed);
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
            ring.Position = Track.MoveTo(ring.Position, from + step);
            double to = ring.Position.S;

            foreach (var o in Nearby(_obstacles, o => o.S, from, to))
            {
                if (o.Destroyed || o.Branch != ring.Position.Branch) continue;
                if (to < o.S - o.Length / 2f || from > o.S + o.Length / 2f) continue;
                // On open planes the ring reaches a fixed distance to either side, on floor and ceiling.
                if (!ShapeAt(o.S, o.Branch).IsClosed && MathF.Abs(o.X) > _settings.RingReach) continue;

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
            if (o.Destroyed || o.Branch != pos.Branch) continue;
            if (to < o.S - o.Length / 2f || from > o.S + o.Length / 2f || shot.Height >= o.Height) continue;
            float lateral = TrackSpace.SurfaceDistance(ShapeAt(o.S, o.Branch), pos.Surface, pos.X, o.Surface, o.X);
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
