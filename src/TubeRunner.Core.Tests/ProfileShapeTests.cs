using System.Numerics;
using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class ProfileShapeTests
{
    private static readonly CrossSection Box = new(10f, 4f, Squareness: 1f);
    private static readonly CrossSection Planes = Box with { Opening = 1f };

    [Theory]
    [InlineData(0f, 4f, 0f)]
    [InlineData(0.25f, 0f, 4f)]
    [InlineData(0.5f, -4f, 0f)]
    [InlineData(0.75f, 0f, -4f)]
    public void Circle_PointsLieOnCircle(float u, float x, float y)
    {
        var p = new ProfileShape(CrossSection.Circle(4f)).PointAt(u);

        Assert.Equal(x, p.X, precision: 2);
        Assert.Equal(y, p.Y, precision: 2);
    }

    [Fact]
    public void Circle_PerimeterMatches()
    {
        Assert.Equal(MathF.Tau * 4f, new ProfileShape(CrossSection.Circle(4f)).Perimeter, precision: 2);
    }

    [Fact]
    public void Oval_IsSampledByArcLength()
    {
        var shape = new ProfileShape(new CrossSection(9f, 4.5f));
        const int steps = 64;
        float expected = shape.Perimeter / steps;

        for (int i = 0; i < steps; i++)
        {
            float d = Vector2.Distance(shape.PointAt(i / (float)steps), shape.PointAt((i + 1) / (float)steps));
            Assert.InRange(d, expected * 0.98f, expected * 1.001f);
        }
    }

    [Theory]
    [InlineData(0.65f)]
    [InlineData(0.75f)]
    [InlineData(0.85f)]
    public void Box_FloorIsFlat(float u)
    {
        Assert.Equal(-4f, new ProfileShape(Box).PointAt(u).Y, precision: 1);
    }

    [Theory]
    [InlineData(0.75f, 0f, 1f)]
    [InlineData(0f, -1f, 0f)]
    public void Circle_InwardNormalPointsToCenter(float u, float x, float y)
    {
        var n = new ProfileShape(CrossSection.Circle(4f)).InwardNormalAt(u);

        Assert.Equal(x, n.X, precision: 2);
        Assert.Equal(y, n.Y, precision: 2);
    }

    [Fact]
    public void ClosedTube_HasNoGap()
    {
        var shape = new ProfileShape(Box);

        Assert.Equal(0f, shape.GapEdge);
        Assert.False(shape.IsOpen(0f));
        Assert.Equal(0.01f, shape.ClampToSurface(0.01f));
    }

    [Fact]
    public void Planes_SidesAreOpen_FloorAndCeilingAreNot()
    {
        var shape = new ProfileShape(Planes);

        Assert.True(shape.IsOpen(0f));
        Assert.True(shape.IsOpen(0.5f));
        Assert.False(shape.IsOpen(0.25f));
        Assert.False(shape.IsOpen(0.75f));
    }

    [Fact]
    public void Planes_ClampKeepsShipOnSameSurface()
    {
        var shape = new ProfileShape(Planes);
        float e = shape.GapEdge;

        Assert.InRange(e, 0.01f, 0.2f);
        Assert.Equal(e, shape.ClampToSurface(0.001f));          // upper right → ceiling edge
        Assert.Equal(1f - e, shape.ClampToSurface(0.999f));     // lower right → floor edge
        Assert.Equal(0.5f - e, shape.ClampToSurface(0.499f));   // upper left → ceiling edge
        Assert.Equal(0.5f + e, shape.ClampToSurface(0.501f));   // lower left → floor edge
        Assert.Equal(0.75f, shape.ClampToSurface(0.75f));
    }
}
