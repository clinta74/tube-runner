using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

/// <summary>
/// What a ring may hold beyond blocks and pads: a collar that is a gate, and a well in the outer
/// wall. Each was refused once, and the refusals that remain say why.
/// </summary>
public class RingRoomTests
{
    private const string Ring = """
        {
          "name": "Ring Room",
          "sections": { "tube": { "radius": 20 }, "ring": { "radius": 20, "ringHeight": 12 } },
          "start": "tube",
          "track": [
            { "length": 300 },
            { "length": 600, "section": "ring", INSIDE },
            { "length": 300, "section": "tube" }
          ]
        }
        """;

    private static Level Parse(string inside) => LevelLoader.Parse(Ring.Replace("INSIDE", inside));

    [Fact]
    public void ACollar_CanBeAGate()
    {
        var level = Parse("\"obstacles\": [ { \"at\": 200, \"full\": true, \"period\": 3, \"height\": 3 } ]");

        var collar = Assert.Single(level.Obstacles);
        Assert.True(collar.Full);
        Assert.Equal(3f, collar.Period);
        Assert.True(collar.IsSolidAt(0.5f));
        Assert.False(collar.IsSolidAt(2f));
    }

    // It goes the whole way round, so there is nowhere for it to sweep to.
    [Fact]
    public void ACollar_CannotBeAMover()
    {
        var error = Assert.Throws<LevelFormatException>(() => Parse("\"obstacles\": [ { \"at\": 200, \"full\": true, \"sweep\": 6 } ]"));

        Assert.Contains("sweep", error.Message);
        Assert.Contains("period", error.Message);
    }

    [Fact]
    public void AWell_GoesInARingsOuterWall_NotItsCore()
    {
        var level = Parse("\"warps\": [ { \"at\": 200, \"angle\": 180 } ]");
        Assert.Single(level.Warps);

        var error = Assert.Throws<LevelFormatException>(() => Parse("\"warps\": [ { \"at\": 200, \"surface\": \"ceiling\" } ]"));
        Assert.Contains("core", error.Message);
    }
}
