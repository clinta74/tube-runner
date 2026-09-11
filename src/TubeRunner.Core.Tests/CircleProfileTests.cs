using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class CircleProfileTests
{
    [Theory]
    [InlineData(0f, 4f, 0f)]
    [InlineData(0.25f, 0f, 4f)]
    [InlineData(0.5f, -4f, 0f)]
    [InlineData(0.75f, 0f, -4f)]
    public void PointAt_LiesOnCircle(float u, float x, float y)
    {
        var p = new CircleProfile(4f).PointAt(u);

        Assert.Equal(x, p.X, precision: 4);
        Assert.Equal(y, p.Y, precision: 4);
    }
}
