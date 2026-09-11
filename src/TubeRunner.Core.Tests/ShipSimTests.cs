using System.Numerics;
using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class ShipSimTests
{
    private static readonly CrossSection Circle = CrossSection.Circle(4f);
    private static readonly CrossSection Open = Circle with { Opening = 1f };
    private static readonly ShipSettings Settings = new(SteerSpeed: 10f, MaxPlaneOffset: 30f, JumpDuration: 0.5f);

    [Fact]
    public void Step_AdvancesAlongTrack()
    {
        var sim = Sim(Circle);

        sim.Step(0.5f, steer: 0f);

        Assert.Equal(25.0, sim.Position.S, precision: 5);
        Assert.Equal(new TrackPosition(25.0, Surface.Floor, 0f), sim.Position);
    }

    [Fact]
    public void SteerRight_OnFloor_IncreasesX()
    {
        var sim = Sim(Circle);

        sim.Step(0.1f, steer: 1f);

        Assert.Equal(1f, sim.Position.X, precision: 4);
    }

    [Fact]
    public void SteerRight_OnCeiling_DecreasesX()
    {
        // Upside down on the ceiling, the ship's right is the track's left.
        var sim = Sim(Circle, Surface.Ceiling);

        sim.Step(0.1f, steer: 1f);

        Assert.Equal(-1f, sim.Position.X, precision: 4);
    }

    [Fact]
    public void Steering_WrapsFromFloorOntoCeiling()
    {
        var sim = Sim(Circle);
        float q = sim.Shape.Quarter;
        sim = Sim(Circle, Surface.Floor, q - 0.5f);

        sim.Step(0.1f, steer: 1f);

        Assert.Equal(Surface.Ceiling, sim.Position.Surface);
        Assert.Equal(q - 0.5f, sim.Position.X, precision: 3);
    }

    [Fact]
    public void Step_ClampsSteerInput()
    {
        var sim = Sim(Circle);

        sim.Step(0.1f, steer: 10f);

        Assert.Equal(1f, sim.Position.X, precision: 4);
    }

    [Fact]
    public void Steering_CoversSameWallDistanceOnAnyShape()
    {
        var sim = Sim(new CrossSection(9f, 4.5f), Surface.Floor, 5f);
        var before = sim.Shape.PointAt(Surface.Floor, 5f);

        sim.Step(0.1f, steer: 1f);

        Assert.Equal(1f, Vector2.Distance(before, sim.Shape.PointAt(sim.Position.Surface, sim.Position.X)), precision: 2);
    }

    [Fact]
    public void OpenPlanes_StrafeToLimitWithoutWrapping()
    {
        var sim = Sim(Open);

        for (int i = 0; i < 100; i++) sim.Step(0.1f, steer: 1f);

        Assert.Equal(Surface.Floor, sim.Position.Surface);
        Assert.Equal(30f, sim.Position.X, precision: 4);
    }

    [Fact]
    public void Jump_CrossesToCeilingOnOpenPlanes()
    {
        var sim = Sim(Open);

        sim.Step(0.25f, steer: 0f, jump: true);
        Assert.True(sim.IsJumping);
        sim.Step(0.3f, steer: 0f);

        Assert.False(sim.IsJumping);
        Assert.Equal(Surface.Ceiling, sim.Position.Surface);
        var (_, up) = sim.Pose(0.5f);
        Assert.Equal(-1f, up.Y, precision: 3);
    }

    [Fact]
    public void Jump_MidwayIsBetweenSurfacesAndRolledSideways()
    {
        var sim = Sim(Open);

        sim.Step(0.25f, steer: 0f, jump: true);   // halfway through a 0.5 s jump

        var (point, up) = sim.Pose(0.5f);
        Assert.Equal(0f, point.Y, precision: 3);
        Assert.Equal(-1f, up.X, precision: 3);
    }

    [Fact]
    public void Jump_IgnoredInClosedTube()
    {
        var sim = Sim(Circle);

        sim.Step(0.1f, steer: 0f, jump: true);

        Assert.False(sim.IsJumping);
    }

    [Fact]
    public void Throttle_ChangesSpeedWithinLimits()
    {
        var sim = Sim(Circle);   // track speed 50

        for (int i = 0; i < 60; i++) sim.Step(0.1f, steer: 0f, throttle: 1f);
        Assert.Equal(1.75f, sim.Throttle, precision: 4);
        Assert.Equal(87.5f, sim.ForwardSpeed, precision: 3);

        for (int i = 0; i < 60; i++) sim.Step(0.1f, steer: 0f, throttle: -1f);
        Assert.Equal(0.5f, sim.Throttle, precision: 4);
    }

    [Fact]
    public void Throttle_HoldsWhenReleased()
    {
        var sim = Sim(Circle);

        sim.Step(0.4f, steer: 0f, throttle: 1f);   // 1 + 0.75 × 0.4
        sim.Step(1f, steer: 0f);

        Assert.Equal(1.3f, sim.Throttle, precision: 4);
    }

    private static ShipSim Sim(CrossSection section, Surface surface = Surface.Floor, float x = 0f)
    {
        var track = new Track(section, startSpeed: 50f);
        track.Append(new TrackPiece(1000f, section));
        return new ShipSim(Settings, track, new TrackPosition(0, surface, x));
    }
}
