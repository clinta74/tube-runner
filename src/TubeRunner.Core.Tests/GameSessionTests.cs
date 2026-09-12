using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class GameSessionTests
{
    private static readonly CrossSection Circle = CrossSection.Circle(6f);
    private static readonly CrossSection Open = Circle with { Opening = 1f };
    private static readonly SessionSettings Settings = new(new ShipSettings(SteerSpeed: 10f));

    [Fact]
    public void HittingABlock_CostsAShieldAndSlowsTheShip()
    {
        var game = Session(Circle, [Block(50)]);

        var events = Run(game, 1.2f);

        Assert.Contains(SessionEvent.Hit, events);
        Assert.Equal(2, game.Shields);
        Assert.True(game.RecoveryLeft > 0f);
        Assert.True(game.Ship.SpeedScale < 1f);
    }

    [Fact]
    public void BlockToTheSide_IsMissed()
    {
        var game = Session(Circle, [Block(50, x: 5f)]);

        Run(game, 1.2f);

        Assert.Equal(3, game.Shields);
    }

    [Fact]
    public void LargeStep_StillHits()
    {
        var game = Session(Circle, [Block(50)]);

        game.Step(2f, default);   // 100 units in one step, straight through the block

        Assert.Equal(2, game.Shields);
    }

    [Fact]
    public void RecoveringShip_IsNotHitAgain()
    {
        var game = Session(Circle, [Block(50), Block(56)]);

        Run(game, 2f);

        Assert.Equal(2, game.Shields);
    }

    [Fact]
    public void RunningOutOfShields_EndsTheRun()
    {
        var game = Session(Circle, [Block(50), Block(150), Block(250)]);

        var events = Run(game, 10f);
        double stoppedAt = game.Ship.Position.S;
        game.Step(1f, default);

        Assert.Equal(SessionState.GameOver, game.State);
        Assert.Contains(SessionEvent.GameOver, events);
        Assert.Equal(stoppedAt, game.Ship.Position.S);
    }

    [Fact]
    public void ClosedTube_HitsAcrossTheSideEdge()
    {
        float q = new ProfileShape(Circle).Quarter;
        var game = Session(Circle, [Block(50, x: q - 0.3f, surface: Surface.Ceiling)], startX: q - 0.3f);

        Run(game, 1.2f);

        Assert.Equal(2, game.Shields);
    }

    [Fact]
    public void OpenPlanes_CeilingBlockMissesShipOnFloor()
    {
        var game = Session(Open, [Block(50, surface: Surface.Ceiling)]);

        Run(game, 1.2f);

        Assert.Equal(3, game.Shields);
    }

    [Fact]
    public void Jump_ClearsALowBlock()
    {
        // The ship passes the block about halfway through its jump, well above it.
        var game = Session(Open, [Block(12.5, height: 1f)]);

        game.Step(1f / 60f, new ShipInput(Jump: true));
        Run(game, 1f);

        Assert.Equal(3, game.Shields);
        Assert.Equal(Surface.Ceiling, game.Ship.Position.Surface);
    }

    [Fact]
    public void Shooting_DestroysATarget()
    {
        var game = Session(Circle, [Target(150)]);

        var events = Run(game, 1f, new ShipInput(Fire: true));

        Assert.True(game.Obstacles[0].Destroyed);
        Assert.Contains(SessionEvent.Fired, events);
        Assert.Contains(SessionEvent.TargetDestroyed, events);
        Assert.True(game.Score >= GameSession.TargetPoints);
    }

    [Fact]
    public void Blocks_StopShots()
    {
        var game = Session(Circle, [Block(120), Target(150)]);

        var events = Run(game, 1f, new ShipInput(Fire: true));

        Assert.False(game.Obstacles[1].Destroyed);
        Assert.Contains(SessionEvent.ShotBlocked, events);
    }

    // A level finishes well before its track runs out - the last stretch is empty run-out, so the
    // wall closing it is still deep in the distance fade when the next level takes over. These
    // tracks have to be long enough to have something in front of that, or the level is over before
    // it has begun.
    [Fact]
    public void ReachingTheEnd_FinishesTheLevel()
    {
        var game = Session(Circle, [], length: 560f);

        var events = Run(game, 9f);

        Assert.Equal(SessionState.Finished, game.State);
        Assert.Contains(SessionEvent.Finished, events);
    }

    [Fact]
    public void Elapsed_StopsAtTheFinish()
    {
        var game = Session(Circle, [], length: 560f);

        Run(game, 9f);

        Assert.Equal(SessionState.Finished, game.State);
        // 560 of track less 260 of run-out is 300 units, at 50 u/s, and then the clock stops.
        Assert.InRange(game.Elapsed, 6f, 6.5f);
    }

    [Fact]
    public void SurfaceDistance_WrapsInTubesButNotOnPlanes()
    {
        var tube = new ProfileShape(Circle);
        float q = tube.Quarter;
        Assert.Equal(0.6f, TrackSpace.SurfaceDistance(tube, Surface.Floor, q - 0.3f, Surface.Ceiling, q - 0.3f), precision: 3);
        Assert.Equal(1f, TrackSpace.SurfaceDistance(tube, Surface.Floor, -q + 0.5f, Surface.Ceiling, -q + 0.5f), precision: 3);

        var planes = new ProfileShape(Open);
        Assert.Equal(3f, TrackSpace.SurfaceDistance(planes, Surface.Floor, 1f, Surface.Floor, 4f), precision: 4);
        Assert.Equal(float.PositiveInfinity, TrackSpace.SurfaceDistance(planes, Surface.Floor, 0f, Surface.Ceiling, 0f));
    }

    private static GameSession Session(CrossSection section, Obstacle[] obstacles, float startX = 0f, float length = 2000f)
    {
        var track = new Track(section, startSpeed: 50f);
        track.Append(new TrackPiece(length, section));
        return new GameSession(track, obstacles, Settings, new TrackPosition(0, Surface.Floor, startX));
    }

    private static Obstacle Block(double s, float x = 0f, Surface surface = Surface.Floor, float height = 2f) =>
        new() { Kind = ObstacleKind.Block, S = s, X = x, Surface = surface, Height = height };

    private static Obstacle Target(double s) => new() { Kind = ObstacleKind.Target, S = s };

    private static List<SessionEvent> Run(GameSession game, float seconds, ShipInput input = default)
    {
        const float dt = 1f / 60f;
        var events = new List<SessionEvent>();
        for (int i = 0; i < (int)MathF.Round(seconds / dt); i++)
        {
            game.Step(dt, input);
            events.AddRange(game.Events);
        }
        return events;
    }
}
