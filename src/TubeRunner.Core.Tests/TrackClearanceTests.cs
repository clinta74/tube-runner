using TubeRunner.Core;
using Xunit.Abstractions;

namespace TubeRunner.Core.Tests;

/// <summary>
/// No part of a level's track may pass through another part of it.
///
/// Nothing in the track builder prevents it: pieces turn about the track's own axes, so a loop is
/// easy to write, and a loop flown at one steady rate closes into a circle and comes straight back
/// through its own entry. In the solid style the player might never notice. In the wireframe style
/// every other stretch of track is in view through the wall, so a tube running through a tube is the
/// first thing they see.
/// </summary>
public class TrackClearanceTests(ITestOutputHelper output)
{
    private const double Step = 4.0;

    // Open planes are drawn out towards the horizon; this is how far across them play reaches.
    private const float OpenExtent = 40f;

    // Space to leave between two walls, beyond merely not touching.
    private const float Margin = 4f;

    [Theory]
    [MemberData(nameof(AllLevels))]
    public void TheTrackNeverPassesThroughItself(string name)
    {
        var track = LevelLoader.Parse(File.ReadAllText(Path.Combine(LevelsDirectory(), name))).Track;
        int n = (int)(track.Length / Step);
        var points = new Vector3d[n + 1];
        var extents = new float[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double s = Math.Min(i * Step, track.Length);
            points[i] = track.FrameAt(s).Position;
            extents[i] = Extent(track, s);
        }

        double worst = double.MaxValue;
        string where = "";
        for (int i = 0; i <= n; i++)
        {
            for (int j = i + 1; j <= n; j++)
            {
                double need = extents[i] + extents[j] + Margin;
                // Near each other along the track, two points are near in space too, and that is just
                // the tube. Half a turn's worth of track further on, even the tightest bend a level is
                // allowed has carried them apart, so anything close past that is a real crossing.
                if ((j - i) * Step < Math.PI * need) continue;

                double d = Distance(points[i], points[j]);
                if (d - need < worst)
                {
                    worst = d - need;
                    where = $"{i * Step:0} and {j * Step:0} are {d:0.0} apart, needing {need:0.0}";
                }
            }
        }

        output.WriteLine($"{name}: closest approach - {where} (slack {worst:0.0})");
        Assert.True(worst >= 0, $"{name}: the track passes through itself: {where}.");
    }

    // How far from the centerline the track's walls reach at s: the section's own size, or at a fork
    // however far out the widest branch has swung.
    private static float Extent(Track track, double s)
    {
        float extent = SectionExtent(track.SectionAt(s));
        var split = track.SplitAt(s);
        if (split is null) return extent;

        float along = (float)(s - split.StartS);
        for (int b = 0; b < split.BranchCount; b++)
        {
            extent = Math.Max(extent, split.OffsetAt(b, along).Length() + SectionExtent(split.Section(b)));
        }
        return extent;
    }

    private static float SectionExtent(CrossSection c)
    {
        float round = Math.Max(c.HalfWidth, c.HalfHeight);
        // A squared-off section reaches out to its corners.
        float corner = MathF.Sqrt(c.HalfWidth * c.HalfWidth + c.HalfHeight * c.HalfHeight);
        float extent = round + (corner - round) * c.Squareness;
        return c.IsClosed ? extent : Math.Max(extent, OpenExtent);
    }

    private static double Distance(Vector3d a, Vector3d b)
    {
        double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    public static TheoryData<string> AllLevels()
    {
        var data = new TheoryData<string>();
        var root = LevelsDirectory();
        foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories))
        {
            data.Add(Path.GetRelativePath(root, file));
        }
        return data;
    }

    private static string LevelsDirectory() => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(TrackClearanceTests).Assembly.Location)!,
        "..", "..", "..", "..", "..", "game", "levels"));
}
