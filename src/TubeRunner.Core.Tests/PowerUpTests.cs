using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class PowerUpTests
{
    private static readonly CrossSection Circle = CrossSection.Circle(6f);
    private static readonly SessionSettings Settings = new(new ShipSettings(SteerSpeed: 10f));

    [Fact]
    public void Shield_RestoresOneAfterAHit()
    {
        var game = Session([Block(50)], [Power(PickupKind.Shield, 150)]);

        var events = Run(game, 5f);

        Assert.Contains(SessionEvent.Hit, events);
        Assert.Contains(SessionEvent.ShieldRestored, events);
        Assert.Equal(3, game.Shields);
    }

    [Fact]
    public void Shield_DoesNotOverfill()
    {
        var game = Session([], [Power(PickupKind.Shield, 50)]);

        Run(game, 2f);

        Assert.Equal(3, game.Shields);
        Assert.Equal(3, game.MaxShields);
    }

    [Fact]
    public void FullShields_RefillsEverySlot()
    {
        var game = Session([Block(50), Block(150)], [Power(PickupKind.FullShields, 350)]);

        Run(game, 10f);

        Assert.Equal(3, game.Shields);
        Assert.True(game.Pickups[0].Collected);
    }

    [Fact]
    public void ShieldSlot_AddsAFilledSlotUpToTheCap()
    {
        var game = Session([], [.. Enumerable.Range(1, 5).Select(i => Power(PickupKind.ShieldSlot, i * 50))]);

        Run(game, 6f);

        Assert.Equal(6, game.MaxShields);
        Assert.Equal(6, game.Shields);
    }

    [Fact]
    public void RapidFire_FiresFaster()
    {
        var normal = Session([], []);
        var rapid = Session([], [Power(PickupKind.RapidFire, 1)]);

        int normalShots = Run(normal, 1f, new ShipInput(Fire: true)).Count(e => e == SessionEvent.Fired);
        int rapidShots = Run(rapid, 1f, new ShipInput(Fire: true)).Count(e => e == SessionEvent.Fired);

        Assert.True(rapidShots > 2 * normalShots, $"{rapidShots} rapid vs {normalShots} normal");
        Assert.True(rapid.RapidFireLeft > 0f);
    }

    [Fact]
    public void RingGun_SweepsTheWholeTube()
    {
        float rightWall = new ProfileShape(Circle).Quarter;
        var ceilingTarget = new Obstacle { Kind = ObstacleKind.Target, S = 150, Surface = Surface.Ceiling };
        var sideTarget = new Obstacle { Kind = ObstacleKind.Target, S = 150, X = 5f };
        var toughBlock = new Obstacle { Kind = ObstacleKind.Block, S = 160, X = rightWall, Hits = 3 };
        var solidBlock = new Obstacle { Kind = ObstacleKind.Block, S = 170, Surface = Surface.Ceiling };
        var game = Session([ceilingTarget, sideTarget, toughBlock, solidBlock], [Power(PickupKind.RingGun, 1)]);

        game.Step(1f / 60f, default);   // collect
        game.Step(1f / 60f, new ShipInput(Special: true));
        var events = Run(game, 1f);

        Assert.Equal(2, game.RingCharges);
        Assert.True(ceilingTarget.Destroyed);
        Assert.True(sideTarget.Destroyed);
        Assert.True(toughBlock.Destroyed);
        Assert.False(solidBlock.Destroyed);
        Assert.Contains(SessionEvent.BlockDestroyed, events);
    }

    [Fact]
    public void RingGun_NeedsCharges()
    {
        var game = Session([], []);

        game.Step(1f / 60f, new ShipInput(Special: true));

        Assert.DoesNotContain(SessionEvent.RingFired, game.Events);
    }

    [Fact]
    public void BreakableBlock_TakesItsHitsThenBreaks()
    {
        var block = new Obstacle { Kind = ObstacleKind.Block, S = 150, Hits = 3 };
        var game = Session([block], []);

        var events = Run(game, 1f, new ShipInput(Fire: true));

        Assert.True(block.Destroyed);
        Assert.Equal(2, events.Count(e => e == SessionEvent.BlockDamaged));
        Assert.Contains(SessionEvent.BlockDestroyed, events);
    }

    [Fact]
    public void Pickup_OnTheOtherSurfaceOfOpenPlanes_IsMissed()
    {
        var planes = Circle with { Opening = 1f };
        var track = new Track(planes, startSpeed: 50f);
        track.Append(new TrackPiece(2000f, planes));
        var game = new GameSession(track, [], Settings, new TrackPosition(0, Surface.Floor, 0f),
            [Power(PickupKind.Shield, 50, Surface.Ceiling)]);

        Run(game, 2f);

        Assert.False(game.Pickups[0].Collected);
    }

    [Fact]
    public void Loader_ReadsPickupsAndBreakableBlocks()
    {
        var level = LevelLoader.Parse("""
            {
              "sections": { "tube": { "radius": 6 } },
              "start": "tube",
              "track": [
                { "length": 100 },
                { "length": 200,
                  "pickups": [ { "at": 20, "kind": "ring-gun", "angle": 180 } ],
                  "obstacles": [ { "at": 50, "hits": 2 } ] }
              ]
            }
            """);

        var pickup = Assert.Single(level.Pickups);
        Assert.Equal(PickupKind.RingGun, pickup.Kind);
        Assert.Equal(120.0, pickup.S);
        Assert.Equal(Surface.Ceiling, pickup.Surface);
        Assert.Equal(2, Assert.Single(level.Obstacles).Hits);
    }

    [Theory]
    [InlineData("""{ "length": 100, "pickups": [ { "at": 20, "kind": "jetpack" } ] }""", "unknown kind 'jetpack'")]
    [InlineData("""{ "length": 100, "obstacles": [ { "at": 20, "kind": "target", "hits": 2 } ] }""", "'hits' is for blocks")]
    public void Loader_ReportsBadPowerUpsAndHits(string piece, string message)
    {
        var e = Assert.Throws<LevelFormatException>(() => LevelLoader.Parse($$"""
            { "sections": { "tube": { "radius": 6 } }, "start": "tube", "track": [ {{piece}} ] }
            """));

        Assert.Contains(message, e.Message);
    }

    private static GameSession Session(Obstacle[] obstacles, Pickup[] pickups)
    {
        var track = new Track(Circle, startSpeed: 50f);
        track.Append(new TrackPiece(2000f, Circle));
        return new GameSession(track, obstacles, Settings, new TrackPosition(0, Surface.Floor, 0f), pickups);
    }

    private static Obstacle Block(double s) => new() { Kind = ObstacleKind.Block, S = s };

    private static Pickup Power(PickupKind kind, double s, Surface surface = Surface.Floor) =>
        new() { Kind = kind, S = s, Surface = surface };

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
