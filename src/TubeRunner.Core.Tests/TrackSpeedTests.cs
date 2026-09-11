using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class TrackSpeedTests
{
    private static readonly CrossSection Circle = CrossSection.Circle(6f);

    [Fact]
    public void Speed_BlendsAcrossPieceAndHolds()
    {
        var track = new Track(Circle, startSpeed: 50f);
        track.Append(new TrackPiece(100f, Circle, EndSpeed: 100f));
        track.Append(new TrackPiece(100f, Circle));

        Assert.Equal(50f, track.SpeedAt(0));
        Assert.Equal(75f, track.SpeedAt(50), precision: 3);
        Assert.Equal(100f, track.SpeedAt(100), precision: 3);
        Assert.Equal(100f, track.SpeedAt(150), precision: 3);
        Assert.Equal(100f, track.SpeedAt(500), precision: 3);
    }

    [Fact]
    public void Ship_FollowsTrackSpeed()
    {
        var track = new Track(Circle, startSpeed: 50f);
        track.Append(new TrackPiece(100f, Circle, EndSpeed: 100f));
        track.Append(new TrackPiece(500f, Circle));
        var ship = new ShipSim(new ShipSettings(SteerSpeed: 10f), track, new TrackPosition(200, Surface.Floor, 0f));

        ship.Step(0.1f, steer: 0f);

        Assert.Equal(210.0, ship.Position.S, precision: 3);
        Assert.Equal(100f, ship.ForwardSpeed, precision: 3);
    }

    [Fact]
    public void LevelPieces_SetSpeed()
    {
        var level = LevelLoader.Parse("""
            {
              "speed": 60,
              "sections": { "tube": { "radius": 6 } },
              "start": "tube",
              "track": [ { "length": 100, "speed": 120 }, { "length": 50 } ]
            }
            """);

        Assert.Equal(60f, level.Track.SpeedAt(0));
        Assert.Equal(120f, level.Track.SpeedAt(125), precision: 3);
    }
}
