using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class MathUtilTests
{
    [Theory]
    [InlineData(-0.25f, 0.75f)]
    [InlineData(-1e-9f, 0f)]
    [InlineData(3.5f, 0.5f)]
    public void Wrap01_StaysInRange(float input, float expected)
    {
        Assert.Equal(expected, MathUtil.Wrap01(input), precision: 5);
    }

    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(2f, 1f)]
    public void SmoothStep_ClampsAndIsSymmetric(float t, float expected)
    {
        Assert.Equal(expected, MathUtil.SmoothStep(t), precision: 5);
    }
}
