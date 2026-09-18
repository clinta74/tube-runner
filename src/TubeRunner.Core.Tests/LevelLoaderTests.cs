using System.Numerics;
using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class LevelLoaderTests
{
    private const string Minimal = """
        {
          // Comments and trailing commas are allowed.
          "name": "Test",
          "speed": 90,
          "sections": {
            "tube": { "radius": 6 },
            "oval": { "halfWidth": 9, "halfHeight": 4.5 },
          },
          "start": "tube",
          "track": [
            { "length": 100 },
            { "length": 50, "section": "oval" },
          ],
        }
        """;

    [Fact]
    public void Parse_BuildsTrackFromPieces()
    {
        var level = LevelLoader.Parse(Minimal);

        Assert.Equal("Test", level.Name);
        Assert.Equal(90f, level.Speed);
        Assert.Equal(150.0, level.Track.Length, precision: 5);
        Assert.Equal(CrossSection.Circle(6f), level.Track.SectionAt(50));
        Assert.Equal(new CrossSection(9f, 4.5f), level.Track.SectionAt(150));
        Assert.Equal(Theme.Earth, level.Theme);
    }

    [Fact]
    public void Turn_IsDegreesToTheRight()
    {
        var level = LevelLoader.Parse(Level("""{ "length": 100, "turn": 90 }"""));

        var forward = level.Track.FrameAt(100).Forward;
        Assert.True(Vector3.Distance(Vector3.UnitX, forward) < 1e-3f, $"forward was {forward}");
    }

    [Fact]
    public void Theme_OverridesDefaults()
    {
        var level = LevelLoader.Parse("""
            {
              "theme": { "darks": ["#000000"], "glow": 2 },
              "sections": { "tube": { "radius": 6 } },
              "start": "tube",
              "track": [ { "length": 10 } ]
            }
            """);

        Assert.Equal([new Rgb(0f, 0f, 0f)], level.Theme.Darks);
        Assert.Equal(2f, level.Theme.Glow);
        Assert.Equal(Theme.Earth.Lights, level.Theme.Lights);
    }

    [Theory]
    [InlineData("""{ "length": 100, "section": "nope" }""", "unknown section 'nope'")]
    [InlineData("""{ "length": 0 }""", "length must be positive")]
    [InlineData("""{ "length": 100, "section": "flat", "turn": 10 }""", "must be straight")]
    public void Parse_ReportsBadPieces(string piece, string message)
    {
        var e = Assert.Throws<LevelFormatException>(() => LevelLoader.Parse(Level(piece)));

        Assert.Contains("Track piece 0", e.Message);
        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void Parse_ReportsBadColor()
    {
        var e = Assert.Throws<LevelFormatException>(() => LevelLoader.Parse("""
            {
              "theme": { "far": "#12" },
              "sections": { "tube": { "radius": 6 } },
              "start": "tube",
              "track": [ { "length": 10 } ]
            }
            """));

        Assert.Contains("'#12'", e.Message);
    }

    [Fact]
    public void ShippedLevels_LoadAndLinkUp()
    {
        var dir = LevelsDirectory();
        var files = Directory.GetFiles(dir, "*.json");
        Assert.NotEmpty(files);

        foreach (var file in files)
        {
            var level = LevelLoader.Parse(File.ReadAllText(file));
            Assert.True(level.Track.Length > 0, file);
            if (level.Next is not null) Assert.True(File.Exists(Path.Combine(dir, level.Next)), $"{file}: next '{level.Next}' missing");
        }
    }

    // A level's id is its key in the save file, so it has to be said out loud rather than fall out
    // of the level's name: reword "Neon Run" and the fallback would quietly move every time saved
    // against it. Two levels sharing one would be worse still - they would share a best time.
    [Fact]
    public void EveryShippedLevel_DeclaresItsOwnId()
    {
        var ids = new Dictionary<string, string>();
        foreach (var file in Directory.GetFiles(LevelsDirectory(), "*.json"))
        {
            var text = File.ReadAllText(file);
            Assert.Contains("\"id\"", text);
            var id = LevelLoader.Parse(text).Id;
            Assert.False(ids.TryGetValue(id, out string? other), $"{file} and {other} share the id '{id}'");
            ids[id] = file;
        }
    }

    [Fact]
    public void TheRunIsAChain_NotALoop()
    {
        // Checking that each 'next' file exists is not enough: a level pointing back at an earlier
        // one passes that and sends the run round forever, which looks exactly like a player having
        // hit retry. Walk it instead, and require it to end.
        var dir = LevelsDirectory();
        var seen = new List<string>();
        string? name = "level_01.json";

        while (name is not null)
        {
            Assert.DoesNotContain(name, seen);   // would run forever in play
            seen.Add(name);
            name = LevelLoader.Parse(File.ReadAllText(Path.Combine(dir, name))).Next;
        }

        // Every level in the folder should be on the chain, or it is unreachable in a real run.
        // level_00 is the standalone warp test bench and is deliberately off it.
        var onDisk = Directory.GetFiles(dir, "level_*.json")
            .Select(Path.GetFileName)
            .Where(f => f != "level_00.json")
            .ToList();
        Assert.Equal(onDisk.OrderBy(f => f), seen.OrderBy(f => f));
    }

    [Fact]
    public void Obstacles_RepeatAndConvertAngles()
    {
        var level = LevelLoader.Parse(Level("""{ "length": 500 }""", """
            { "at": 100, "kind": "target", "x": -2, "count": 3, "spacing": 20, "xStep": 2 },
            { "at": 200, "angle": 180 },
            """));

        Assert.Equal(4, level.Obstacles.Count);
        Assert.Equal(new[] { 100.0, 120.0, 140.0 }, level.Obstacles.Take(3).Select(o => o.S));
        Assert.Equal(new[] { -2f, 0f, 2f }, level.Obstacles.Take(3).Select(o => o.X));
        Assert.All(level.Obstacles.Take(3), o => Assert.Equal(ObstacleKind.Target, o.Kind));

        // Halfway around the tube is the center of the ceiling.
        var top = level.Obstacles[3];
        Assert.Equal(ObstacleKind.Block, top.Kind);
        Assert.Equal(Surface.Ceiling, top.Surface);
        Assert.Equal(0f, top.X, precision: 2);
    }

    [Fact]
    public void PieceObstacles_ArePlacedFromThePieceStart()
    {
        var level = LevelLoader.Parse(Level("""
            { "length": 100 },
            { "length": 200, "obstacles": [ { "at": 30 }, { "at": 50, "count": 2, "spacing": 100 } ] }
            """, """{ "at": 20 }"""));

        Assert.Equal(new[] { 20.0, 130.0, 150.0, 250.0 }, level.Obstacles.Select(o => o.S).OrderBy(s => s));
    }

    [Fact]
    public void PieceObstacleErrors_NameThePiece()
    {
        var e = Assert.Throws<LevelFormatException>(() => LevelLoader.Parse(Level("""
            { "length": 100 },
            { "length": 100, "obstacles": [ { "at": 500 } ] }
            """)));

        Assert.Contains("Track piece 1, obstacle 0", e.Message);
        Assert.Contains("off the track", e.Message);
    }

    [Theory]
    [InlineData("""{ "at": 50, "kind": "boulder" }""", "unknown kind 'boulder'")]
    [InlineData("""{ "at": 900 }""", "off the track")]
    [InlineData("""{ "at": 250, "angle": 90 }""", "only works in closed tubes")]
    public void Parse_ReportsBadObstacles(string obstacle, string message)
    {
        // The middle piece opens out into flat planes; the last one runs flat.
        var track = """{ "length": 100 }, { "length": 100, "section": "flat" }, { "length": 100 }""";

        var e = Assert.Throws<LevelFormatException>(() => LevelLoader.Parse(Level(track, obstacle)));

        Assert.Contains("Obstacle 0", e.Message);
        Assert.Contains(message, e.Message);
    }

    private static string Level(string pieces, string obstacles = "") => $$"""
        {
          "sections": { "tube": { "radius": 6 }, "flat": { "radius": 6, "opening": 1 } },
          "start": "tube",
          "track": [ {{pieces}} ],
          "obstacles": [ {{obstacles}} ]
        }
        """;

    private static string LevelsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "game", "levels"))) dir = dir.Parent;
        return dir is null
            ? throw new DirectoryNotFoundException("game/levels not found above the test output directory.")
            : Path.Combine(dir.FullName, "game", "levels");
    }
}
