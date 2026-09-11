using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class ShipSimTests
{
    private static readonly ShipSettings Settings = new(ForwardSpeed: 50f, SteerRate: 0.5f);

    [Fact]
    public void Step_AdvancesAlongTrack()
    {
        var sim = new ShipSim(Settings);

        sim.Step(0.5f, steer: 0f);

        Assert.Equal(25.0, sim.Position.S, precision: 5);
        Assert.Equal(0f, sim.Position.U);
    }

    [Theory]
    [InlineData(0.9f, 1f, 0.4f, 0.1f)]   // wraps past 1
    [InlineData(0.1f, -1f, 0.4f, 0.9f)]  // wraps below 0
    public void Step_WrapsAroundTube(float startU, float steer, float dt, float expectedU)
    {
        var sim = new ShipSim(Settings, new TrackPosition(0, 0, startU, 0));

        sim.Step(dt, steer);

        Assert.Equal(expectedU, sim.Position.U, precision: 5);
    }

    [Fact]
    public void Step_ClampsSteerInput()
    {
        var sim = new ShipSim(Settings);

        sim.Step(0.1f, steer: 10f);

        Assert.Equal(0.05f, sim.Position.U, precision: 5);
    }

    [Theory]
    [InlineData(-0.25f, 0.75f)]
    [InlineData(-1e-9f, 0f)]
    [InlineData(3.5f, 0.5f)]
    public void Wrap01_StaysInRange(float input, float expected)
    {
        Assert.Equal(expected, ShipSim.Wrap01(input), precision: 5);
    }
}
