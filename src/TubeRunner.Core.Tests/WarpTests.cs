using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class WarpTests
{
    private static readonly CrossSection Circle = CrossSection.Circle(6f);
    private static readonly SessionSettings Settings = new(new ShipSettings(SteerSpeed: 10f));

    [Fact]
    public void Warp_ThrowsTheShipBackWithoutCostingAShield()
    {
        var warp = new Warp { S = 400 };
        var game = Session(warp);
        double peak = 0, landed = -1;

        for (int i = 0; i < 1200 && landed < 0; i++)
        {
            peak = Math.Max(peak, game.Ship.Position.S);
            game.Step(1f / 60f, default);
            if (game.Events.Contains(SessionEvent.Warped)) landed = game.Ship.Position.S;
        }

        Assert.True(landed >= 0, "the ship never reached the warp");
        Assert.True(landed < peak - 100, $"warped to {landed:0} from {peak:0}, which is barely back at all");
        Assert.Equal(3, game.Shields);   // it costs time, not a shield
        Assert.True(warp.Used);
    }

    [Fact]
    public void Warp_StaysArmed_SoTheSameWellCanTakeTheShipAgain()
    {
        var warp = new Warp { S = 400 };
        var game = Session(warp);
        int warps = 0;

        // Long enough to be thrown back and fly into the same well a second time.
        for (int i = 0; i < 3000; i++)
        {
            game.Step(1f / 60f, default);
            warps += game.Events.Count(e => e == SessionEvent.Warped);
        }

        Assert.True(warps >= 2, $"the well only took the ship {warps} time(s)");
    }

    [Fact]
    public void Warp_IsMissedWhenTheShipIsElsewhereOnTheWall()
    {
        // Sitting on the ceiling, with the mouth in the floor.
        var warp = new Warp { S = 400, Surface = Surface.Floor };
        var track = Track();
        var game = new GameSession(track, [], Settings, new TrackPosition(0, Surface.Ceiling, 0f), null, null, [warp]);

        for (int i = 0; i < 1200; i++) game.Step(1f / 60f, default);

        Assert.False(warp.Used);
        Assert.DoesNotContain(SessionEvent.Warped, game.Events);
    }

    [Fact]
    public void Loader_ReadsWarps()
    {
        var level = LevelLoader.Parse("""
            {
              "sections": { "tube": { "radius": 6 } },
              "start": "tube",
              "track": [
                { "length": 100 },
                { "length": 300, "warps": [ { "at": 50, "span": 0.25, "back": 120 } ] }
              ]
            }
            """);

        var warp = Assert.Single(level.Warps);
        Assert.Equal(150.0, warp.S);
        Assert.Equal(0.25f, warp.Span);
        Assert.Equal(120f, warp.Back);
    }

    [Fact]
    public void AWarpWithNothingDeclared_GetsTheGameSDefaults()
    {
        // This is the test that was missing. The loader used to carry its own copy of these, and its
        // copy won: every well in the game was a sixth of its intended size, through four rounds of
        // raising a default that never reached a level.
        var level = LevelLoader.Parse("""
            {
              "sections": { "tube": { "radius": 6 } },
              "start": "tube",
              "track": [ { "length": 400, "warps": [ { "at": 200 } ] } ]
            }
            """);

        var loaded = Assert.Single(level.Warps);
        var expected = new Warp { S = 0 };
        Assert.Equal(expected.Span, loaded.Span);
        Assert.Equal(expected.Length, loaded.Length);
    }

    [Fact]
    public void AWellSpansTheSameShareOfEveryTube()
    {
        // The point of a share: one authored number used to be a third of the way round a standard
        // tube and nearly half of a narrow one, so the same well was three different hazards.
        var warp = new Warp { S = 100 };
        float wide = warp.WidthOn(new ProfileShape(CrossSection.Circle(7.5f)));
        float narrow = warp.WidthOn(new ProfileShape(CrossSection.Circle(4.8f)));

        Assert.Equal(0.33f, wide / new ProfileShape(CrossSection.Circle(7.5f)).Perimeter, precision: 3);
        Assert.Equal(0.33f, narrow / new ProfileShape(CrossSection.Circle(4.8f)).Perimeter, precision: 3);
        Assert.True(wide > narrow, "a wider tube should get a wider mouth");

        // And the tidy consequence: at a third, a well's radius is about the tube's own radius.
        Assert.Equal(6f, warp.WidthOn(new ProfileShape(CrossSection.Circle(6f))) / 2f, precision: 0);
    }

    [Theory]
    // A misspelled key, and an invented one. Both used to load fine and quietly do nothing, which is
    // how a level ends up not being the level that was written.
    [InlineData("""{ "sections": { "t": { "radius": 6 } }, "start": "t", "track": [ { "length": 100, "turnn": 40 } ] }""")]
    [InlineData("""{ "sections": { "t": { "radius": 6 } }, "start": "t", "track": [ { "length": 100, "boost": 2 } ] }""")]
    public void Loader_RejectsKeysItDoesNotKnow(string json)
    {
        Assert.Throws<LevelFormatException>(() => LevelLoader.Parse(json));
    }

    [Fact]
    public void Warp_CarriesTheShipDownTheWellBeforeThrowingItBack()
    {
        var warp = new Warp { S = 400 };
        var game = Session(warp);

        while (game.Diving is null && game.Ship.Position.S < 600) game.Step(1f / 60f, default);
        Assert.NotNull(game.Diving);
        Assert.Contains(SessionEvent.WarpEntered, game.Events);

        // Partway down the well the ship holds station and has not been thrown back yet.
        double atTheMouth = game.Ship.Position.S;
        game.Step(1f / 60f, default);
        Assert.Equal(atTheMouth, game.Ship.Position.S);
        Assert.DoesNotContain(SessionEvent.Warped, game.Events);
        Assert.InRange(game.DiveProgress, 0f, 1f);

        while (game.Diving is not null) game.Step(1f / 60f, default);

        Assert.Contains(SessionEvent.Warped, game.Events);
        Assert.True(game.Ship.Position.S < atTheMouth - 100,
            $"landed at {game.Ship.Position.S:0} from {atTheMouth:0}");
    }

    private static Track Track()
    {
        var track = new Track(Circle, startSpeed: 50f);
        track.Append(new TrackPiece(2000f, Circle));
        return track;
    }

    private static GameSession Session(params Warp[] warps) =>
        new(Track(), [], Settings, new TrackPosition(0, Surface.Floor, 0f), null, null, warps);
}
