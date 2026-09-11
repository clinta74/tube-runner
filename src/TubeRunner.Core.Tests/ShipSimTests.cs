using System.Numerics;
using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class ShipSimTests
{
    private static readonly CrossSection Circle = CrossSection.Circle(4f);
    private static readonly ShipSettings Settings = new(ForwardSpeed: 50f, SteerSpeed: 10f);

    [Fact]
    public void Step_AdvancesAlongTrack()
    {
        var sim = new ShipSim(Settings, Straight(Circle));

        sim.Step(0.5f, steer: 0f);

        Assert.Equal(25.0, sim.Position.S, precision: 5);
        Assert.Equal(0f, sim.Position.U);
    }

    [Theory]
    // 0.4 s at 10 units/s = 4 units = 4 / (2π·4) ≈ 0.15915 of the way around.
    [InlineData(0.9f, 1f, 0.05915f)]    // wraps past 1
    [InlineData(0.1f, -1f, 0.94085f)]   // wraps below 0
    public void Step_WrapsAroundTube(float startU, float steer, float expectedU)
    {
        var sim = new ShipSim(Settings, Straight(Circle), new TrackPosition(0, 0, startU, 0));

        sim.Step(0.4f, steer);

        Assert.Equal(expectedU, sim.Position.U, precision: 3);
    }

    [Fact]
    public void Step_ClampsSteerInput()
    {
        var sim = new ShipSim(Settings, Straight(Circle));

        sim.Step(0.1f, steer: 10f);

        Assert.Equal(1f / (MathF.Tau * 4f), sim.Position.U, precision: 3);
    }

    [Fact]
    public void Steering_CoversSameWallDistanceOnAnyShape()
    {
        var sim = new ShipSim(Settings, Straight(new CrossSection(9f, 4.5f)), new TrackPosition(0, 0, 0.75f, 0));
        var before = sim.Shape.PointAt(sim.Position.U);

        sim.Step(0.1f, steer: 1f);

        Assert.Equal(1f, Vector2.Distance(before, sim.Shape.PointAt(sim.Position.U)), precision: 2);
    }

    [Fact]
    public void Planes_ShipStopsAtFloorEdge()
    {
        var planes = new CrossSection(10f, 4f, Squareness: 1f, Opening: 1f);
        var sim = new ShipSim(Settings, Straight(planes), new TrackPosition(0, 0, 0.75f, 0));

        // 5 s at 10 units/s is far wider than the floor.
        for (int i = 0; i < 50; i++) sim.Step(0.1f, steer: -1f);

        Assert.Equal(0.5f + sim.Shape.GapEdge, sim.Position.U, precision: 4);
    }

    private static Track Straight(CrossSection section)
    {
        var track = new Track(section);
        track.Append(new TrackPiece(1000f, section));
        return track;
    }
}
