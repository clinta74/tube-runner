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
    public void ShieldSlot_AddsOneExtra_AndNeverASecondWhileItIsHeld()
    {
        var game = Session([], [.. Enumerable.Range(1, 5).Select(i => Power(PickupKind.ShieldSlot, i * 50))]);

        Run(game, 6f);

        // The extra is its own pool of one, and the normal three are untouched by it.
        Assert.Equal(3, game.MaxShields);
        Assert.Equal(3, game.Shields);
        Assert.Equal(1, game.ExtraShields);
        Assert.Equal(4, game.TotalShields);
    }

    [Fact]
    public void ExtraShield_IsSpentBeforeTheNormalOnes_AndCannotBeRefilled()
    {
        // 50 u/s, so 4 seconds carries the ship past the extra at 50 and into the block at 150.
        var game = Session([Block(150)], [Power(PickupKind.ShieldSlot, 50), Power(PickupKind.FullShields, 300)]);

        Run(game, 4f);

        // The extra is what the hit took; the normal three are untouched.
        Assert.Equal(3, game.Shields);
        Assert.Equal(0, game.ExtraShields);

        // On past the full refill: it fills normal shields only, and does not hand the extra back.
        Run(game, 4f);
        Assert.Equal(3, game.Shields);
        Assert.Equal(0, game.ExtraShields);
    }

    [Fact]
    public void ExtraShield_SitsOnTopOfAGapInTheNormalShields()
    {
        // The block comes first this time, so the extra lands on a bar that already has a gap in it.
        var game = Session([Block(100)], [Power(PickupKind.ShieldSlot, 250)]);

        Run(game, 7f);

        // Hit first, then the extra: 2 of 3 normal, plus a fourth point that did not fill the third.
        Assert.Equal(2, game.Shields);
        Assert.Equal(1, game.ExtraShields);
        Assert.Equal(3, game.TotalShields);
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

        // Long enough for three shots to cross the gap at the base fire rate.
        var events = Run(game, 1.5f, new ShipInput(Fire: true));

        Assert.True(block.Destroyed);
        Assert.Equal(2, events.Count(e => e == SessionEvent.BlockDamaged));
        Assert.Contains(SessionEvent.BlockDestroyed, events);
    }

    [Fact]
    public void Unstoppable_SmashesThroughBlocksAndScoresThem()
    {
        var solid = Block(60);
        var tough = new Obstacle { Kind = ObstacleKind.Block, S = 120, Hits = 3 };
        var game = Session([solid, tough], [Power(PickupKind.Unstoppable, 10)]);

        var events = Run(game, 4f);

        Assert.Equal(3, game.Shields);
        Assert.True(solid.Destroyed);
        Assert.True(tough.Destroyed);
        Assert.Equal(2, events.Count(e => e == SessionEvent.Rammed));
        // 50 for the plain block, 150 for the three-hit one, 25 for the pickup.
        Assert.True(game.Score >= 225, $"score was {game.Score}");
    }

    [Fact]
    public void Agility_SteersFaster_ThenWearsOff()
    {
        var normal = Session([], []);
        var agile = Session([], [Power(PickupKind.Agility, 1)], Settings with { AgilityTime = 1f });

        // Steer right the whole way. Over a short stretch the quicker ship gets further round.
        Run(normal, 0.4f, new ShipInput(Steer: 1f));
        Run(agile, 0.4f, new ShipInput(Steer: 1f));
        Assert.True(agile.AgilityLeft > 0f);
        Assert.True(agile.Ship.Position.X > normal.Ship.Position.X * 1.4f,
            $"{agile.Ship.Position.X} agile vs {normal.Ship.Position.X} normal");

        // Once it has run out the two steer at the same rate again.
        Run(agile, 1f);
        Assert.Equal(0f, agile.AgilityLeft);
        float before = agile.Ship.Position.X;
        Run(agile, 0.1f, new ShipInput(Steer: -1f));
        Assert.Equal(Settings.Ship.SteerSpeed * 0.1f, before - agile.Ship.Position.X, 2);
    }

    [Fact]
    public void Unstoppable_WearsOff()
    {
        var game = Session([Block(40), Block(200)], [Power(PickupKind.Unstoppable, 10)],
            Settings with { RamTime = 1f });

        Run(game, 6f);

        Assert.Equal(2, game.Shields);   // the first is smashed, the second is a crash
    }

    [Fact]
    public void ShootingBlocks_PaysByHowToughTheyAre()
    {
        var game = Session([new Obstacle { Kind = ObstacleKind.Block, S = 150, Hits = 3 }], []);

        Run(game, 1.5f, new ShipInput(Fire: true));

        Assert.True(game.Score >= 3 * GameSession.BreakPoints, $"score was {game.Score}");
    }

    [Fact]
    public void Carry_KeepsShieldsPowerUpsAndScoreBetweenLevels()
    {
        var first = Session([], [Power(PickupKind.RingGun, 20), Power(PickupKind.ShieldSlot, 40), Power(PickupKind.Agility, 60)]);
        Run(first, 2f);
        var carried = first.Carry;

        var track = new Track(Circle, startSpeed: 50f);
        track.Append(new TrackPiece(2000f, Circle));
        var second = new GameSession(track, [], Settings, new TrackPosition(0, Surface.Floor, 0f), null, carried);

        Assert.Equal(carried.ExtraShields, second.ExtraShields);
        Assert.Equal(1, second.ExtraShields);
        Assert.Equal(4, second.TotalShields);
        Assert.Equal(carried.RingCharges, second.RingCharges);
        Assert.Equal(3, second.RingCharges);
        Assert.True(carried.AgilityLeft > 0f);
        Assert.Equal(carried.AgilityLeft, second.AgilityLeft);
        Assert.True(second.Score >= carried.Score);
    }

    // A hit taken just before a level line is still being recovered from on the other side of it:
    // the ship is still slowed and still in its grace, rather than back to full speed and hittable
    // the moment the next level starts.
    [Fact]
    public void Carry_KeepsAHitsRecoveryAcrossTheLine()
    {
        var first = Session([Block(80)], []);
        Run(first, 1.8f);
        var carried = first.Carry;
        Assert.True(carried.RecoveryLeft > 0f, "the hit at 80 should still be being recovered from");

        var track = new Track(Circle, startSpeed: 50f);
        track.Append(new TrackPiece(2000f, Circle));
        var second = new GameSession(track, [Block(20)], Settings, new TrackPosition(0, Surface.Floor, 0f), null, carried);
        second.Step(1f / 60f, default);

        Assert.Equal(carried.RecoveryLeft, second.RecoveryLeft, 1);
        Assert.True(second.Ship.SpeedScale < 1f, "still slowed");
        // And the block right at the start is met inside the grace, so it costs nothing.
        Run(second, 1f);
        Assert.Equal(carried.Shields, second.Shields);
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

    private static GameSession Session(Obstacle[] obstacles, Pickup[] pickups, SessionSettings? settings = null)
    {
        var track = new Track(Circle, startSpeed: 50f);
        track.Append(new TrackPiece(2000f, Circle));
        return new GameSession(track, obstacles, settings ?? Settings, new TrackPosition(0, Surface.Floor, 0f), pickups);
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
