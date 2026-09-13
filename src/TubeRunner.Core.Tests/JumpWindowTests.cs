using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class JumpWindowTests
{
    private static readonly CrossSection Tube = CrossSection.Circle(6f);
    private static readonly CrossSection Flat = new(6f, 6f, 0f, 1f);
    private static readonly SessionSettings Settings = new(new ShipSettings(SteerSpeed: 10f));

    [Fact]
    public void AClosedTrackHasNoWindows()
    {
        var track = new Track(Tube, startSpeed: 50f);
        track.Append(new TrackPiece(600f, Tube));

        Assert.Empty(JumpWindows.Find(track));
    }

    [Fact]
    public void AFlatStretchIsOneWindow()
    {
        var track = new Track(Tube, startSpeed: 50f);
        track.Append(new TrackPiece(300f, Tube));    // closed
        track.Append(new TrackPiece(300f, Flat));    // opening out
        track.Append(new TrackPiece(400f, Flat));    // flat
        track.Append(new TrackPiece(300f, Tube));    // closing back up
        track.Append(new TrackPiece(300f, Tube));    // closed

        var window = Assert.Single(JumpWindows.Find(track));

        // It opens somewhere inside the blend out and shuts somewhere inside the blend back, not at
        // the piece edges - which is the whole reason these have to be measured rather than authored.
        Assert.InRange(window.From, 300, 600);
        Assert.InRange(window.To, 1000, 1300);
    }

    [Fact]
    public void TheWindowMatchesWhatTheShipWillActuallyDo()
    {
        var track = new Track(Tube, startSpeed: 50f);
        track.Append(new TrackPiece(300f, Tube));
        track.Append(new TrackPiece(300f, Flat));
        track.Append(new TrackPiece(400f, Flat));
        track.Append(new TrackPiece(300f, Tube));
        track.Append(new TrackPiece(300f, Tube));

        var window = Assert.Single(JumpWindows.Find(track));
        var ship = new ShipSim(Settings.Ship, track, new TrackPosition(0, Surface.Floor, 0f));

        // Fly the whole track and record where the ship itself says a jump is legal. The marks on
        // the wall are worthless if they disagree with the rule by even a little.
        double firstYes = -1, lastYes = -1;
        while (ship.Position.S < track.Length - 10)
        {
            ship.Step(1f / 60f, steer: 0f);
            if (!ship.CanJump) continue;
            if (firstYes < 0) firstYes = ship.Position.S;
            lastYes = ship.Position.S;
        }

        Assert.True(firstYes >= 0, "the ship was never able to jump");
        // Within a frame's travel at 50 u/s, since the ship only samples where it happens to land.
        Assert.InRange(firstYes, window.From - 0.1, window.From + 1.0);
        Assert.InRange(lastYes, window.To - 1.0, window.To + 0.1);
    }
}
