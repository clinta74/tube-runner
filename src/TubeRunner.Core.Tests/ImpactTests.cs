using TubeRunner.Core;
using Xunit.Abstractions;

namespace TubeRunner.Core.Tests;

/// <summary>
/// Where a hit lands relative to where the ship is when it lands. Collision is swept over the
/// distance covered in a frame, but the ship is drawn at the end of that sweep - so at speed the
/// picture and the collision can disagree, and the hit reads as arriving after the ship went past.
/// </summary>
public class ImpactTests(ITestOutputHelper output)
{
    private static readonly CrossSection Circle = CrossSection.Circle(6f);
    private static readonly SessionSettings Settings = new(new ShipSettings(SteerSpeed: 10f));

    // Where the ship's nose first touches the block's rear face: half the ship (0.8) plus half the
    // block (1) back from the block's centre.
    private const double Contact = 400.0 - 1.8;

    [Theory]
    [InlineData(50f)]
    [InlineData(120f)]
    [InlineData(190f)]
    public void AHitLandsWhereTheShipMeetsTheObstacle(float speed)
    {
        var block = new Obstacle { Kind = ObstacleKind.Block, S = 400, Width = 40f };
        var game = Game(speed, block);

        double atHit = -1;
        for (int i = 0; i < 5000 && game.State == SessionState.Playing; i++)
        {
            game.Step(1f / 60f, default);
            if (!game.Events.Contains(SessionEvent.Hit)) continue;
            atHit = game.Ship.Position.S;
            break;
        }

        double step = speed / 60.0;
        output.WriteLine($"{speed} u/s: {step:0.00} units a frame, contact at {Contact:0.00}, "
            + $"hit registered with the ship at {atHit:0.00} ({atHit - Contact:+0.00;-0.00} past it)");

        Assert.True(atHit >= 0, "the ship never hit the block");
        // Half a unit of slack for the frame it happens to land on; anything more and the ship is
        // visibly clear of the block when the hit arrives.
        Assert.InRange(atHit, Contact - 0.1, Contact + 0.5);
    }

    private static GameSession Game(float speed, params Obstacle[] obstacles)
    {
        var track = new Track(Circle, startSpeed: speed);
        track.Append(new TrackPiece(2000f, Circle));
        return new GameSession(track, obstacles, Settings, new TrackPosition(0, Surface.Floor, 0f));
    }
}
