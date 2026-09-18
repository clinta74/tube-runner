using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class BestTimesTests
{
    [Fact]
    public void Record_KeepsOnlyTheFastestTime()
    {
        var times = new BestTimes();

        Assert.True(times.Record("first-loop", 40.5f));
        Assert.False(times.Record("first-loop", 41f));
        Assert.True(times.Record("first-loop", 39.25f));

        Assert.Equal(39.25f, times.Get("first-loop"));
        Assert.Null(times.Get("power-up"));
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var times = new BestTimes();
        times.Record("first-loop", 38.75f);
        times.Record("power-up", 30.5f);

        var loaded = BestTimes.FromJson(times.ToJson());

        Assert.Equal(38.75f, loaded.Get("first-loop"));
        Assert.Equal(30.5f, loaded.Get("power-up"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    public void MissingOrDamagedSave_StartsFresh(string? json)
    {
        Assert.Null(BestTimes.FromJson(json).Get("first-loop"));
    }

    // A save written before levels had ids keys everything by file name. Those names now carry the
    // running order, so leaving them alone would hand each time to whichever level later took that
    // number - the exact silent corruption the ids exist to prevent.
    [Fact]
    public void ASaveKeyedByFileName_MovesOntoTheLevelIds()
    {
        var loaded = BestTimes.FromJson("""
            {
              "level_01.json": 38.75,
              "level_08b.json": 61.5,
              "level_26.json": 94.25,
              "run.level_01.json": 640.5
            }
            """);

        Assert.Equal(38.75f, loaded.Get("first-loop"));
        Assert.Equal(61.5f, loaded.Get("loop-the-loop"));
        Assert.Equal(94.25f, loaded.Get("finale"));
        Assert.Equal(640.5f, loaded.Get("run.first-loop"));
        Assert.Null(loaded.Get("level_01.json"));
    }

    // Two records of the same race, once the old name has moved onto the id. Whichever run was
    // actually faster is the best time; the order they happen to come out of the file is not a
    // reason to lose one.
    [Fact]
    public void BothFormsOfAKey_KeepTheFasterTime()
    {
        Assert.Equal(30.5f, BestTimes.FromJson("""{ "level_02.json": 30.5, "power-up": 41.0 }""").Get("power-up"));
        Assert.Equal(30.5f, BestTimes.FromJson("""{ "power-up": 41.0, "level_02.json": 30.5 }""").Get("power-up"));
    }

    // A save from a newer build, read by an older one, should come back whole: a key naming nothing
    // this version knows is not a key to throw away.
    [Fact]
    public void AnUnknownKey_IsLeftAlone()
    {
        Assert.Equal(12f, BestTimes.FromJson("""{ "some-later-level": 12.0 }""").Get("some-later-level"));
    }
}
