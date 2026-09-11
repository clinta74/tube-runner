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
    Jumped,
    Hit,
    TargetDestroyed,
    ShotBlocked,
    Finished,
    GameOver,
}

public readonly record struct ShipInput(float Steer = 0f, bool Jump = false, bool Fire = false);

/// <param name="Ship">Movement settings.</param>
/// <param name="Shields">Hits the ship can take; the run ends when they run out.</param>
/// <param name="RecoveryTime">Seconds of invulnerability and slow-down after a hit.</param>
/// <param name="HitSlowdown">Speed multiplier right after a hit, easing back to 1 over the recovery.</param>
/// <param name="ShotSpeed">Shot speed on top of the ship's own.</param>
/// <param name="ShotRange">Distance a shot travels before it fades.</param>
/// <param name="FireInterval">Seconds between shots while fire is held.</param>
/// <param name="ShipHalfWidth">Collision half-size across the surface.</param>
/// <param name="ShipHalfLength">Collision half-size along the track.</param>
/// <param name="FinishRunOut">Distance before the end of the track where the level counts as finished.</param>
public sealed record SessionSettings(
    ShipSettings Ship,
    int Shields = 3,
    float RecoveryTime = 1.5f,
    float HitSlowdown = 0.45f,
    float ShotSpeed = 220f,
    float ShotRange = 300f,
    float FireInterval = 0.15f,
    float ShipHalfWidth = 0.6f,
    float ShipHalfLength = 0.8f,
    float FinishRunOut = 40f);

/// <summary>A shot flying down the track ahead of the ship.</summary>
public sealed class Shot
{
    public TrackPosition Position { get; internal set; }

    /// <summary>Height off its surface.</summary>
    public float Height { get; init; }

    internal double End { get; init; }
}

/// <summary>
/// One run through a level: the ship, obstacles, shots, shields, and score. All collision is done
/// in track space, swept along the track so nothing is skipped at high speed.
/// </summary>
public sealed class GameSession
{
    public const int TargetPoints = 100;

    // Obstacles and shots are only tested within this distance of the swept range.
    private const double SearchMargin = 20.0;
    private const float ShotRadius = 0.3f;

    private readonly SessionSettings _settings;
    private readonly List<Obstacle> _obstacles;
    private readonly List<Shot> _shots = new();
    private readonly List<SessionEvent> _events = new();
    private readonly ProfileShapeCache _shapes = new();
    private float _fireCooldown;
    private int _targetScore;

    public GameSession(Track track, IEnumerable<Obstacle> obstacles, SessionSettings settings, TrackPosition start)
    {
        Track = track;
        _settings = settings;
        _obstacles = obstacles.OrderBy(o => o.S).ToList();
        Ship = new ShipSim(settings.Ship, track, start);
        Shields = settings.Shields;
    }

    public Track Track { get; }
    public ShipSim Ship { get; }
    public IReadOnlyList<Obstacle> Obstacles => _obstacles;
    public IReadOnlyList<Shot> Shots => _shots;

    /// <summary>What happened during the last step.</summary>
    public IReadOnlyList<SessionEvent> Events => _events;

    public SessionState State { get; private set; } = SessionState.Playing;
    public int Shields { get; private set; }

    /// <summary>Seconds left of post-hit invulnerability.</summary>
    public float RecoveryLeft { get; private set; }

    /// <summary>Points for targets destroyed plus one point per 10 units travelled.</summary>
    public int Score => _targetScore + (int)(Ship.Position.S / 10.0);

    public void Step(float dt, ShipInput input)
    {
        _events.Clear();
        if (State != SessionState.Playing) return;

        RecoveryLeft = Math.Max(0f, RecoveryLeft - dt);
        float recovered = 1f - RecoveryLeft / _settings.RecoveryTime;
        Ship.SpeedScale = _settings.HitSlowdown + (1f - _settings.HitSlowdown) * recovered;

        double before = Ship.Position.S;
        bool wasJumping = Ship.IsJumping;
        Ship.Step(dt, input.Steer, input.Jump);
        if (Ship.IsJumping && !wasJumping) _events.Add(SessionEvent.Jumped);

        CheckShipHits(before, Ship.Position.S);
        MoveShots(dt);

        _fireCooldown = Math.Max(0f, _fireCooldown - dt);
        if (State == SessionState.Playing && input.Fire && _fireCooldown <= 0f) Fire();

        if (State == SessionState.Playing && Ship.Position.S >= Track.Length - _settings.FinishRunOut)
        {
            State = SessionState.Finished;
            _events.Add(SessionEvent.Finished);
        }
    }

    private void CheckShipHits(double from, double to)
    {
        var pos = Ship.Position;
        foreach (var o in Nearby(from, to))
        {
            if (o.Destroyed || o.Branch != pos.Branch) continue;
            double reach = o.Length / 2f + _settings.ShipHalfLength;
            if (to < o.S - reach || from > o.S + reach) continue;

            // Mid-jump the ship is between the surfaces at the same X; otherwise it's on one.
            float lateral = Ship.IsJumping
                ? MathF.Abs(pos.X - o.X)
                : TrackSpace.SurfaceDistance(ShapeAt(o), pos.Surface, pos.X, o.Surface, o.X);
            if (lateral >= o.Width / 2f + _settings.ShipHalfWidth || Ship.HeightAbove(o.Surface) >= o.Height) continue;
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
        _fireCooldown = _settings.FireInterval;
        _events.Add(SessionEvent.Fired);
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
                if (hit.Kind == ObstacleKind.Target)
                {
                    hit.Destroyed = true;
                    _targetScore += TargetPoints;
                    _events.Add(SessionEvent.TargetDestroyed);
                }
                else
                {
                    _events.Add(SessionEvent.ShotBlocked);
                }
                _shots.RemoveAt(i);
            }
            else if (shot.Position.S >= shot.End || shot.Position.S >= Track.Length)
            {
                _shots.RemoveAt(i);
            }
        }
    }

    private Obstacle? FirstShotHit(Shot shot, double from, double to)
    {
        var pos = shot.Position;
        Obstacle? first = null;
        foreach (var o in Nearby(from, to))
        {
            if (o.Destroyed || o.Branch != pos.Branch) continue;
            if (to < o.S - o.Length / 2f || from > o.S + o.Length / 2f || shot.Height >= o.Height) continue;
            float lateral = TrackSpace.SurfaceDistance(ShapeAt(o), pos.Surface, pos.X, o.Surface, o.X);
            if (lateral >= o.Width / 2f + ShotRadius) continue;
            if (first is null || o.S < first.S) first = o;
        }
        return first;
    }

    private ProfileShape ShapeAt(Obstacle o) => _shapes.Get(Track.SectionAt(o.S, o.Branch));

    private IEnumerable<Obstacle> Nearby(double from, double to)
    {
        for (int i = FirstAtOrAfter(from - SearchMargin); i < _obstacles.Count && _obstacles[i].S <= to + SearchMargin; i++)
        {
            yield return _obstacles[i];
        }
    }

    private int FirstAtOrAfter(double s)
    {
        int lo = 0, hi = _obstacles.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_obstacles[mid].S < s) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }
}
