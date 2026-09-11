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

    private static string Level(string piece) => $$"""
        {
          "sections": { "tube": { "radius": 6 }, "flat": { "radius": 6, "opening": 1 } },
          "start": "tube",
          "track": [ {{piece}} ]
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
