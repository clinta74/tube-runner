using System.Numerics;
using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class ProfileShapeTests
{
    private const float R = 4f;
    private static readonly float Q = MathF.Tau * R / 4f;
    private static readonly CrossSection Circle = CrossSection.Circle(R);
    private static readonly CrossSection Open = Circle with { Opening = 1f };

    [Fact]
    public void Circle_SurfaceCentersAndEdges()
    {
        var shape = new ProfileShape(Circle);

        AssertNear(new Vector2(0f, -R), shape.PointAt(Surface.Floor, 0f));
        AssertNear(new Vector2(0f, R), shape.PointAt(Surface.Ceiling, 0f));
        // The two surfaces meet at the side midpoints.
        AssertNear(new Vector2(R, 0f), shape.PointAt(Surface.Floor, Q));
        AssertNear(new Vector2(R, 0f), shape.PointAt(Surface.Ceiling, Q));
        AssertNear(new Vector2(-R, 0f), shape.PointAt(Surface.Floor, -Q));
        AssertNear(new Vector2(-R, 0f), shape.PointAt(Surface.Ceiling, -Q));
    }

    [Fact]
    public void Circle_PerimeterMatches()
    {
        Assert.Equal(MathF.Tau * R, new ProfileShape(Circle).Perimeter, precision: 2);
    }

    [Fact]
    public void Oval_IsSampledByArcLength()
    {
        var shape = new ProfileShape(new CrossSection(9f, 4.5f));
        const int steps = 32;
        float step = 2f * shape.Quarter / steps;

        for (int i = 0; i < steps; i++)
        {
            float x = -shape.Quarter + i * step;
            float d = Vector2.Distance(shape.PointAt(Surface.Floor, x), shape.PointAt(Surface.Floor, x + step));
            Assert.InRange(d, step * 0.98f, step * 1.001f);
        }
    }

    [Fact]
    public void ClosedTube_WrapsBetweenSurfaces()
    {
        var shape = new ProfileShape(Circle);
        float q = shape.Quarter;

        AssertWrap((Surface.Ceiling, q - 1f), shape.Wrap(Surface.Floor, q + 1f));
        AssertWrap((Surface.Ceiling, -q + 1f), shape.Wrap(Surface.Floor, -q - 1f));
        AssertWrap((Surface.Floor, q - 0.5f), shape.Wrap(Surface.Ceiling, q + 0.5f));
        AssertWrap((Surface.Floor, 0.5f), shape.Wrap(Surface.Floor, 4f * q + 0.5f));
    }

    [Fact]
    public void OpenSection_DoesNotWrap()
    {
        var shape = new ProfileShape(Open);

        Assert.Equal((Surface.Floor, Q + 5f), shape.Wrap(Surface.Floor, Q + 5f));
    }

    [Fact]
    public void Circle_NormalsPointIntoTube()
    {
        var shape = new ProfileShape(Circle);

        AssertNear(new Vector2(0f, 1f), shape.NormalAt(Surface.Floor, 0f));
        AssertNear(new Vector2(0f, -1f), shape.NormalAt(Surface.Ceiling, 0f));
        AssertNear(new Vector2(-1f, 0f), shape.NormalAt(Surface.Floor, Q - 0.2f), tolerance: 0.06f);
    }

    [Fact]
    public void OpenSection_IsFlatAndExtendsToTheHorizon()
    {
        var shape = new ProfileShape(Open);

        foreach (float x in new[] { -Q, -1f, 0f, Q, Q + 50f })
        {
            AssertNear(new Vector2(x, -R), shape.PointAt(Surface.Floor, x));
            AssertNear(new Vector2(x, R), shape.PointAt(Surface.Ceiling, x));
        }
        AssertNear(new Vector2(0f, 1f), shape.NormalAt(Surface.Floor, Q + 50f));
        AssertNear(new Vector2(0f, -1f), shape.NormalAt(Surface.Ceiling, 3f));
        Assert.Equal(Q + ProfileShape.MaxWingLength, shape.SurfaceExtent, precision: 2);
    }

    [Fact]
    public void HalfUnrolled_EdgesMoveTowardTheFlatPosition()
    {
        // Opening 0.25 = halfway through unrolling, with no spread yet.
        var shape = new ProfileShape(Circle with { Opening = 0.25f });

        AssertNear(new Vector2(0f, -R), shape.PointAt(Surface.Floor, 0f));
        AssertNear(new Vector2((R + Q) / 2f, -R / 2f), shape.PointAt(Surface.Floor, Q), tolerance: 0.01f);
        Assert.Equal(0f, shape.WingLength);
    }

    [Fact]
    public void Nearest_FindsSurfacePosition()
    {
        var shape = new ProfileShape(Circle);

        var floor = shape.Nearest(new Vector2(0f, -10f));
        Assert.Equal(Surface.Floor, floor.Surface);
        Assert.Equal(0f, floor.X, precision: 1);

        var ceiling = shape.Nearest(new Vector2(0f, 3f));
        Assert.Equal(Surface.Ceiling, ceiling.Surface);
        Assert.Equal(0f, ceiling.X, precision: 1);

        // 45° right of the floor center is an eighth of the way around.
        var diagonal = shape.Nearest(new Vector2(3f, -3f));
        Assert.Equal(Surface.Floor, diagonal.Surface);
        Assert.Equal(MathF.PI * R / 4f, diagonal.X, precision: 1);
    }

    private static void AssertNear(Vector2 expected, Vector2 actual, float tolerance = 0.02f) =>
        Assert.True(Vector2.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");

    private static void AssertWrap((Surface Surface, float X) expected, (Surface Surface, float X) actual)
    {
        Assert.Equal(expected.Surface, actual.Surface);
        Assert.Equal(expected.X, actual.X, precision: 3);
    }

    // A point off the outline gets the wall's own texture coordinate by the direction it lies in:
    // the curve parameter runs from the right side midpoint, a quarter per quarter turn on a circle.
    [Fact]
    public void ParameterAt_IsTheCurveParameterOfTheOutlinePointInThatDirection()
    {
        var shape = new ProfileShape(CrossSection.Circle(5f));

        Assert.Equal(0f, shape.ParameterAt(0f), 3);
        Assert.Equal(0.25f, shape.ParameterAt(MathF.PI / 2f), 3);
        Assert.Equal(0.5f, shape.ParameterAt(MathF.PI), 3);
        Assert.Equal(0.75f, shape.ParameterAt(-MathF.PI / 2f), 3);

        // Which is what PointAt runs on: the floor's centre is the bottom, parameter three quarters.
        var bottom = shape.PointAt(Surface.Floor, 0f);
        Assert.Equal(0.75f, shape.ParameterAt(MathF.Atan2(bottom.Y, bottom.X)), 3);
    }
}
