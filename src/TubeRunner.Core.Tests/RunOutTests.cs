using TubeRunner.Core;
using Xunit.Abstractions;

namespace TubeRunner.Core.Tests;

/// <summary>
/// A level finishes <see cref="SessionSettings.FinishRunOut"/> before its track ends, so the wall
/// closing it is still deep in the distance fade when the next level takes over and the tube reads
/// as carrying on. That makes the last stretch of every level unreachable: anything authored there
/// is shown to the player, flown towards, and then snatched away as the level hands over.
/// </summary>
public class RunOutTests(ITestOutputHelper output)
{
    private static readonly float RunOut = new SessionSettings(new ShipSettings(10f)).FinishRunOut;

    [Theory]
    [MemberData(nameof(ShippedLevels))]
    public void NothingIsAuthoredInsideTheRunOut(string name)
    {
        var level = LevelLoader.Parse(File.ReadAllText(Path.Combine(LevelsDirectory(), name)));
        double finish = level.Track.Length - RunOut;

        var late = new List<string>();
        foreach (var o in level.Obstacles)
        {
            if (o.S > finish) late.Add($"{o.Kind} at {o.S:0}");
        }
        foreach (var p in level.Pickups)
        {
            if (p.S > finish) late.Add($"{p.Kind} at {p.S:0}");
        }
        foreach (var w in level.Warps)
        {
            if (w.S > finish) late.Add($"warp at {w.S:0}");
        }

        output.WriteLine($"{name}: track {level.Track.Length:0}, finishes at {finish:0}, "
            + $"run-out {RunOut:0}, {late.Count} item(s) stranded");

        Assert.True(late.Count == 0,
            $"{name} finishes at {finish:0} but has {late.Count} item(s) past it, which the player "
            + $"can see and fly at but never reach: {string.Join(", ", late)}. "
            + "Lengthen the level's last piece so the run-out is empty.");
    }

    public static TheoryData<string> ShippedLevels()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(LevelsDirectory(), "level_*.json"))
        {
            data.Add(Path.GetFileName(file));
        }
        return data;
    }

    private static string LevelsDirectory() => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(RunOutTests).Assembly.Location)!,
        "..", "..", "..", "..", "..", "game", "levels"));
}
