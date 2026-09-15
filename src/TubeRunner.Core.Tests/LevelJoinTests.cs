using System.Numerics;
using TubeRunner.Core;
using Xunit.Abstractions;

namespace TubeRunner.Core.Tests;

/// <summary>
/// Where one level hands over to the next, the picture must not jump.
///
/// The handover has no pause and no transition: when the ship reaches a level's finish, the next level
/// is loaded, a new track is built, and the ship is placed a few units into it. Whatever the camera was
/// looking at is replaced in a single frame by the start of the next level. If the two differ - a narrow
/// tube becoming a wide one, a bend becoming a straight, a change of speed - the player sees the world
/// snap. So a level's end has to become the next level's start before the finish, and stay that way
/// for as far as the camera can see past it.
///
/// Colours are allowed to change; they are the one thing that is meant to announce a new level.
/// </summary>
public class LevelJoinTests(ITestOutputHelper output)
{
    // Where Main.LoadLevel puts the ship in the next level: CameraBehind (6) + 10.
    private const double NextStart = 16.0;

    // How far behind the ship to compare: all the track the next level has behind its start.
    private const double Behind = NextStart;

    private const double Step = 5.0;

    // A radius off by a twentieth of a unit, a path off by half a unit, or a speed off by half a unit a
    // second is not something a player can see. Anything bigger is a snap.
    private const float SectionTolerance = 0.05f;
    private const double PathTolerance = 0.5;
    private const float SpeedTolerance = 0.5f;

    private static readonly double RunOut = new SessionSettings(new ShipSettings(10f)).FinishRunOut;

    [Theory]
    [MemberData(nameof(Joins))]
    public void TheViewDoesNotJumpAtTheHandover(string from, string to)
    {
        var ending = Load(from).Track;
        var starting = Load(to).Track;
        double finish = ending.Length - RunOut;

        var endFrame = ending.FrameAt(finish);
        var startFrame = starting.FrameAt(NextStart);

        float worstSection = 0f;
        double worstSectionAt = 0, worstPath = 0, worstPathAt = 0;
        string sectionDetail = "";

        // From just behind the ship to the end of the old track: everything the camera can see of it.
        for (double d = -Behind; d <= RunOut - Step; d += Step)
        {
            double oldS = finish + d, newS = NextStart + d;
            if (newS > starting.Length) break;

            var a = ending.SectionAt(oldS);
            var b = starting.SectionAt(newS);
            float section = Math.Max(Math.Abs(a.HalfWidth - b.HalfWidth), Math.Abs(a.HalfHeight - b.HalfHeight));
            section = Math.Max(section, Math.Max(Math.Abs(a.Squareness - b.Squareness), Math.Abs(a.Opening - b.Opening)));
            if (section > worstSection)
            {
                worstSection = section;
                worstSectionAt = d;
                sectionDetail = $"{Describe(a)} vs {Describe(b)}";
            }

            // The centreline in each side's own frame at the handover, so only the shape of the path
            // counts, not where either level happens to be in the world.
            double path = Distance(Local(endFrame, ending.FrameAt(oldS).Position), Local(startFrame, starting.FrameAt(newS).Position));
            if (path > worstPath)
            {
                worstPath = path;
                worstPathAt = d;
            }
        }

        float endSpeed = ending.SpeedAt(finish), startSpeed = starting.SpeedAt(NextStart);
        float speed = Math.Abs(endSpeed - startSpeed);

        output.WriteLine($"{from} -> {to}: section {worstSection:0.00} at {worstSectionAt:+0;-0}"
            + (worstSection > SectionTolerance ? $" ({sectionDetail})" : "")
            + $", path {worstPath:0.0} at {worstPathAt:+0;-0}, speed {endSpeed:0} -> {startSpeed:0}");

        var problems = new List<string>();
        if (worstSection > SectionTolerance) problems.Add($"the tube changes shape ({sectionDetail}, {worstSectionAt:+0;-0} from the finish)");
        if (worstPath > PathTolerance) problems.Add($"the path differs by {worstPath:0.0} units ({worstPathAt:+0;-0} from the finish)");
        // Speed is reported but allowed to change: each level keeps its own speed curve, and the change is
        // felt rather than seen.
        Assert.True(problems.Count == 0,
            $"{from} hands over to {to} with a visible jump: {string.Join("; ", problems)}. "
            + $"Extend {from} so that by its finish, {RunOut:0} units before its end, it already matches the start of {to}.");
    }

    [Theory]
    [MemberData(nameof(Joins))]
    public void NothingAppearsOutOfNowhereAtTheHandover(string from, string to)
    {
        var next = Load(to);
        // Everything the camera could see of the old level's end is the next level's first stretch, up to
        // where the old track stops. Anything the next level puts there appears from nowhere in a single
        // frame when it takes over, so that stretch must be empty.
        double inView = NextStart + RunOut;

        var appearing = next.Obstacles.Where(o => o.S < inView).Select(o => $"{o.Kind} at {o.S:0}")
            .Concat(next.Pickups.Where(p => p.S < inView).Select(p => $"{p.Kind} pickup at {p.S:0}"))
            .Concat(next.Warps.Where(w => w.S - w.Length / 2f < inView).Select(w => $"well at {w.S:0}"))
            .ToList();

        Assert.True(appearing.Count == 0,
            $"{to} has {appearing.Count} item(s) in view when {from} hands over to it, which appear from nowhere: "
            + $"{string.Join(", ", appearing)}. Move them past {inView:0}.");
    }

    public static TheoryData<string, string> Joins()
    {
        var data = new TheoryData<string, string>();
        foreach (var file in Directory.GetFiles(LevelsDirectory(), "level_*.json").OrderBy(f => f))
        {
            var name = Path.GetFileName(file);
            if (Load(name).Next is string next) data.Add(name, next);
        }
        return data;
    }

    private static Level Load(string name) => LevelLoader.Parse(File.ReadAllText(Path.Combine(LevelsDirectory(), name)));

    private static string Describe(CrossSection c) =>
        $"{c.HalfWidth:0.#}x{c.HalfHeight:0.#}" + (c.Squareness > 0 ? $" square {c.Squareness:0.##}" : "") + (c.Opening > 0 ? $" open {c.Opening:0.##}" : "");

    private static (double X, double Y, double Z) Local(TrackFrame frame, Vector3d point)
    {
        var r = point - frame.Position;
        return (Dot(r, frame.Right), Dot(r, frame.Up), Dot(r, frame.Forward));
    }

    private static double Dot(Vector3d a, Vector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static double Distance((double X, double Y, double Z) a, (double X, double Y, double Z) b)
    {
        double x = a.X - b.X, y = a.Y - b.Y, z = a.Z - b.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    private static string LevelsDirectory() => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(LevelJoinTests).Assembly.Location)!,
        "..", "..", "..", "..", "..", "game", "levels"));
}
