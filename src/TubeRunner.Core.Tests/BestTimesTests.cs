using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class BestTimesTests
{
    [Fact]
    public void Record_KeepsOnlyTheFastestTime()
    {
        var times = new BestTimes();

        Assert.True(times.Record("level_01.json", 40.5f));
        Assert.False(times.Record("level_01.json", 41f));
        Assert.True(times.Record("level_01.json", 39.25f));

        Assert.Equal(39.25f, times.Get("level_01.json"));
        Assert.Null(times.Get("level_02.json"));
    }

    [Fact]
    public void Json_RoundTrips()
    {
        var times = new BestTimes();
        times.Record("level_01.json", 38.75f);
        times.Record("level_02.json", 30.5f);

        var loaded = BestTimes.FromJson(times.ToJson());

        Assert.Equal(38.75f, loaded.Get("level_01.json"));
        Assert.Equal(30.5f, loaded.Get("level_02.json"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ not json")]
    public void MissingOrDamagedSave_StartsFresh(string? json)
    {
        Assert.Null(BestTimes.FromJson(json).Get("level_01.json"));
    }
}
