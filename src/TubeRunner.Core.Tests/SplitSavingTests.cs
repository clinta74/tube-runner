using TubeRunner.Core;
using Xunit.Abstractions;

namespace TubeRunner.Core.Tests;

/// <summary>
/// Guards that every split in a shipped level is a real shortcut. A branch only shortens by cutting
/// a corner, so a split through a gentle bend saves nothing whatever its offsets are - and that is
/// easy to write by accident, because the level reads the same either way. Levels 7 and 9 shipped
/// like that twice, saving under 1%, which is a tenth of a second no player would take a tighter
/// tube for. These measure the branches' real paths, so a re-cut that quietly flattens one fails.
/// </summary>
public class SplitSavingTests(ITestOutputHelper output)
{
    // The best branch has to beat this, and the gap between best and worst has to beat the second.
    // What a fork feels like is the difference between the routes, not either one against the
    // centerline: the long way round being genuinely long is half of what makes the choice real.
    private const float MinSaving = 4f;
    private const float MinSpread = 10f;

    [Theory]
    [InlineData("level_07.json")]
    [InlineData("level_08.json")]
    [InlineData("level_09.json")]
    [InlineData("level_11.json")]
    [InlineData("level_14.json")]
    [InlineData("level_17.json")]
    [InlineData("level_20.json")]
    [InlineData("level_22.json")]
    [InlineData("level_23.json")]
    [InlineData("level_25.json")]
    [InlineData("level_26.json")]
    public void EverySplitIsWorthTaking(string name)
    {
        var level = LevelLoader.Parse(File.ReadAllText(LevelPath(name)));
        Assert.NotEmpty(level.Track.Splits);

        foreach (var split in level.Track.Splits)
        {
            float best = 100f, worst = -100f;
            for (int b = 0; b < split.BranchCount; b++)
            {
                float saving = (1f - split.PathScale(b)) * 100f;
                best = MathF.Max(best, saving);
                worst = MathF.Min(worst, saving);
                output.WriteLine($"{name} split at {split.StartS:0} branch {b}: saves {saving:0.0}%");
            }

            Assert.True(best >= MinSaving,
                $"{name}: the split at {split.StartS:0} only saves {best:0.0}%, so no one would take it. "
                + "Turn the piece harder or push the offsets wider.");
            Assert.True(best - worst >= MinSpread,
                $"{name}: the split at {split.StartS:0} spreads only {best - worst:0.0}% between its "
                + "quickest and slowest branches, so the routes feel the same.");
        }
    }

    private static string LevelPath(string name) => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(SplitSavingTests).Assembly.Location)!,
        "..", "..", "..", "..", "..", "game", "levels", name));
}
