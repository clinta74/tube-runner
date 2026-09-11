using System.Numerics;
using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class TrackTests
{
    private static readonly CrossSection Circle = CrossSection.Circle(6f);
    private static readonly CrossSection Oval = new(9f, 4.5f);

    [Fact]
    public void Straight_RunsDownNegativeZ()
    {
        var track = new Track(Circle);
        track.Append(new TrackPiece(100f, Circle));

        var f = track.FrameAt(50.5);

        AssertNear(new Vector3d(0, 0, -50.5), f.Position, 1e-4);
        AssertNear(-Vector3.UnitZ, f.Forward);
    }

    [Fact]
    public void QuarterTurnLeft_EndsFacingNegativeX()
    {
        var track = new Track(Circle);
        track.Append(new TrackPiece(100f, Circle, YawRate: MathF.PI / 2f / 100f));

        var f = track.FrameAt(100);
        double r = 100 / (Math.PI / 2);

        AssertNear(-Vector3.UnitX, f.Forward);
        AssertNear(new Vector3d(-r, 0, -r), f.Position, 0.05);
    }

    [Fact]
    public void CurvedFrames_StayOrthonormal()
    {
        var track = new Track(Circle);
        track.Append(new TrackPiece(500f, Circle, YawRate: 0.01f, PitchRate: -0.007f));

        for (double s = 0; s <= 500; s += 37.3)
        {
            var f = track.FrameAt(s);
            Assert.Equal(1f, f.Up.Length(), precision: 4);
            Assert.Equal(1f, f.Right.Length(), precision: 4);
            Assert.Equal(0f, Vector3.Dot(f.Up, f.Forward), precision: 4);
            Assert.Equal(0f, Vector3.Dot(f.Right, f.Forward), precision: 4);
        }
    }

    [Fact]
    public void Section_BlendsAcrossPiece()
    {
        var track = new Track(Circle);
        track.Append(new TrackPiece(100f, Oval));

        Assert.Equal(Circle, track.SectionAt(0));
        Assert.Equal(CrossSection.Lerp(Circle, Oval, 0.5f), track.SectionAt(50));
        Assert.Equal(Oval, track.SectionAt(100));
        Assert.Equal(Oval, track.SectionAt(150));
    }

    [Fact]
    public void OpenPieces_MustBeStraight()
    {
        var track = new Track(Circle);

        Assert.Throws<ArgumentException>(() =>
            track.Append(new TrackPiece(100f, Circle with { Opening = 1f }, YawRate: 0.01f)));
    }

    [Fact]
    public void EnsureLength_ExtendsFromSource()
    {
        var track = new Track(Circle, new TrackGenerator(seed: 1));

        track.EnsureLength(2000);

        Assert.True(track.Length >= 2000);
        Assert.NotEqual(track.FrameAt(1000).Position, track.FrameAt(1999).Position);
    }

    [Fact]
    public void TrimBefore_KeepsLaterDataIntact()
    {
        var track = new Track(Circle, new TrackGenerator(seed: 2));
        track.EnsureLength(1500);
        var frame = track.FrameAt(900.25);
        var section = track.SectionAt(900.25);

        track.TrimBefore(800);

        Assert.Equal(frame, track.FrameAt(900.25));
        Assert.Equal(section, track.SectionAt(900.25));
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-3f) =>
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");

    private static void AssertNear(Vector3d expected, Vector3d actual, double tolerance)
    {
        var d = expected - actual;
        Assert.True(Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z) < tolerance, $"expected {expected}, got {actual}");
    }
}
