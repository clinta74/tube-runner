using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class RejoinFunnelTests
{
    private static readonly CrossSection Tube = CrossSection.Circle(6f);
    private static readonly CrossSection Flat = Tube with { Opening = 1f };

    [Fact]
    public void Funnel_SealsWideThenNarrowsSmoothlyIntoTheTube()
    {
        var track = FlatThenFunnel();

        Assert.False(track.IsRejoining(50));
        Assert.True(track.IsRejoining(300));
        Assert.False(track.IsRejoining(550));

        // Early on the planes are still open.
        Assert.False(track.SectionAt(110).IsClosed);

        // Once sealed, the walls are far out.
        var closed = track.SectionAt(161);
        Assert.True(closed.IsClosed);
        Assert.True(closed.HalfWidth > 100f, $"half width was {closed.HalfWidth}");

        // Then the funnel only ever narrows, easing into the tube.
        float previous = float.MaxValue;
        for (double s = 170; s <= 500; s += 10)
        {
            float width = track.SectionAt(s).HalfWidth;
            Assert.True(width <= previous + 1e-3f, $"widened at {s}");
            previous = width;
        }
        Assert.True(track.SectionAt(490).HalfWidth - Tube.HalfWidth < 0.1f);
        Assert.Equal(Tube, track.SectionAt(500));
    }

    [Fact]
    public void Ship_StaysOnItsSurfaceAndIsEasedIntoTheTube()
    {
        var track = FlatThenFunnel();
        var ship = new ShipSim(new ShipSettings(SteerSpeed: 10f), track, new TrackPosition(50, Surface.Floor, 35f));

        while (ship.Position.S < 499)
        {
            ship.Step(1f / 60f, steer: 1f);   // pushing outward the whole way
            Assert.Equal(Surface.Floor, ship.Position.Surface);
        }

        Assert.InRange(ship.Position.X, 0f, 0.6f * new ProfileShape(Tube).Quarter + 0.5f);
    }

    // 0 - 100 flat planes, 100 - 500 funnel back into the tube, 500 - 600 tube.
    private static Track FlatThenFunnel()
    {
        var track = new Track(Flat, startSpeed: 50f);
        track.Append(new TrackPiece(100f, Flat));
        track.Append(new TrackPiece(400f, Tube));
        track.Append(new TrackPiece(100f, Tube));
        return track;
    }
}
