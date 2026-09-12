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
                { "length": 300, "warps": [ { "at": 50, "width": 8, "back": 120 } ] }
              ]
            }
            """);

        var warp = Assert.Single(level.Warps);
        Assert.Equal(150.0, warp.S);
        Assert.Equal(8f, warp.Width);
        Assert.Equal(120f, warp.Back);
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
