using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class TrackGeneratorTests
{
    [Fact]
    public void SameSeed_SameTrack()
    {
        var a = new TrackGenerator(seed: 42);
        var b = new TrackGenerator(seed: 42);

        for (int i = 0; i < 50; i++) Assert.Equal(a.Next(), b.Next());
    }

    [Fact]
    public void ProducesEveryShapeAndReturnsToCircle()
    {
        var gen = new TrackGenerator(seed: 7);
        var seen = new HashSet<CrossSection>();
        TrackPiece last = default;

        for (int i = 0; i < 500; i++)
        {
            last = gen.Next();
            Assert.True(last.Length > 0);
            seen.Add(last.EndSection);
        }

        Assert.Contains(gen.Oval, seen);
        Assert.Contains(gen.Planes, seen);
        // Drain to the end of the current sequence; sequences end on the circle.
        while (last.EndSection != gen.Circle) last = gen.Next();
    }
}
