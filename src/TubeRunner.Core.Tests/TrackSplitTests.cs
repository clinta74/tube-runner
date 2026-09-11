using System.Numerics;
using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

public class TrackSplitTests
{
    private static readonly CrossSection Chamber = new(14f, 6.5f, Squareness: 0.6f);
    private static readonly CrossSection Tube = CrossSection.Circle(6f);
    private static readonly SessionSettings Settings = new(new ShipSettings(SteerSpeed: 10f));

    [Fact]
    public void SplitAt_CoversTheSplitPiece()
    {
        var track = SplitTrack(out var split);

        Assert.Null(track.SplitAt(99.9));
        Assert.Same(split, track.SplitAt(100));
        Assert.Same(split, track.SplitAt(499.9));
        Assert.Null(track.SplitAt(500));
    }

    [Fact]
    public void Branches_FollowTheirOffsets()
    {
        var track = SplitTrack(out _);

        var right = track.FrameAt(300, branch: 1);
        var left = track.FrameAt(300, branch: 0);

        Assert.True(Distance(new Vector3d(22, 0, -300), right.Position) < 0.01, $"right was {right.Position}");
        Assert.True(Distance(new Vector3d(-22, 0, -300), left.Position) < 0.01, $"left was {left.Position}");
        Assert.True(Vector3.Distance(-Vector3.UnitZ, right.Forward) < 1e-3f);
        Assert.Equal(Tube, track.SectionAt(300, branch: 1));
        Assert.Equal(Chamber, track.SectionAt(300));
    }

    [Theory]
    [InlineData(7f, 1)]
    [InlineData(-7f, 0)]
    public void MoveTo_ForksIntoTheBranchAhead(float x, int branch)
    {
        var track = SplitTrack(out _);

        var p = track.MoveTo(new TrackPosition(99, Surface.Floor, x), 100.5);

        Assert.Equal(branch, p.Branch);
        Assert.Equal(Surface.Floor, p.Surface);
        Assert.Equal(100.5, p.S);
    }

    [Fact]
    public void MoveTo_MergesBackOntoTheMainTrack()
    {
        var track = SplitTrack(out _);

        var p = track.MoveTo(new TrackPosition(499, Surface.Floor, 0f, Branch: 1), 501);

        Assert.Equal(-1, p.Branch);
        Assert.Equal(Surface.Floor, p.Surface);
        Assert.InRange(p.X, 6f, 9f);
    }

    [Fact]
    public void Ship_RidesThroughTheSplit()
    {
        var track = SplitTrack(out _);
        var ship = new ShipSim(Settings.Ship, track, new TrackPosition(50, Surface.Floor, 5f));
        int? branchInside = null;

        while (ship.Position.S < 560)
        {
            ship.Step(1f / 60f, steer: 0f);
            if (ship.Position.S is > 300 and < 301) branchInside = ship.Position.Branch;
        }

        Assert.Equal(1, branchInside);
        Assert.Equal(-1, ship.Position.Branch);
    }

    [Theory]
    [InlineData(0, 3)]   // on the other branch: missed
    [InlineData(1, 2)]   // on the ship's branch: hit
    public void Obstacles_OnlyHitShipsOnTheirBranch(int branch, int shieldsLeft)
    {
        var block = new Obstacle { Kind = ObstacleKind.Block, S = 300, Branch = branch, Width = 6f };
        var game = new GameSession(SplitTrack(out _), [block], Settings, new TrackPosition(50, Surface.Floor, 5f));

        for (int i = 0; i < 400; i++) game.Step(1f / 60f, default);

        Assert.Equal(shieldsLeft, game.Shields);
    }

    [Fact]
    public void Branches_CanRunAtDifferentSpeeds()
    {
        var track = new Track(Chamber, startSpeed: 50f);
        track.Append(new TrackPiece(100f, Chamber));
        track.AppendSplit(new TrackPiece(400f, Chamber), Tube,
            [Keys((0, -7.5f), (400, -7.5f)), Keys((0, 7.5f), (400, 7.5f))],
            [1f, 1.2f]);

        Assert.Equal(50f, track.SpeedAt(300, 0), precision: 3);
        Assert.Equal(60f, track.SpeedAt(300, 1), precision: 3);
        Assert.Equal(50f, track.SpeedAt(300, -1), precision: 3);   // the centerline itself
    }

    [Theory]
    [InlineData(12f, "doesn't fit")]
    [InlineData(3f, "overlap")]
    public void AppendSplit_ChecksTheOpenings(float offset, string message)
    {
        var track = new Track(Chamber);

        var e = Assert.Throws<ArgumentException>(() => track.AppendSplit(new TrackPiece(400f, Chamber), Tube,
            [Keys((0, -offset), (400, -offset)), Keys((0, offset), (400, offset))]));

        Assert.Contains(message, e.Message);
    }

    [Fact]
    public void AppendSplit_KeepsTheSection()
    {
        var track = new Track(Chamber);

        Assert.Throws<ArgumentException>(() => track.AppendSplit(new TrackPiece(400f, Tube), Tube,
            [Keys((0, -7.5f), (400, -7.5f)), Keys((0, 7.5f), (400, 7.5f))]));
    }

    [Fact]
    public void Loader_ReadsSplitsAndBranchObstacles()
    {
        var level = LevelLoader.Parse(SplitLevel("""{ "at": 300, "branch": 1, "angle": 180 }"""));

        var split = Assert.Single(level.Track.Splits);
        Assert.Equal(100.0, split.StartS);
        Assert.Equal(1f, split.SpeedFactor(0));
        Assert.Equal(1.25f, split.SpeedFactor(1));
        var obstacle = Assert.Single(level.Obstacles);
        Assert.Equal(1, obstacle.Branch);
        Assert.Equal(Surface.Ceiling, obstacle.Surface);
    }

    [Fact]
    public void Loader_RequiresBranchForObstaclesInSplits()
    {
        var e = Assert.Throws<LevelFormatException>(() => LevelLoader.Parse(SplitLevel("""{ "at": 300 }""")));

        Assert.Contains("set 'branch'", e.Message);
    }

    private static Track SplitTrack(out TrackSplit split)
    {
        var track = new Track(Chamber, startSpeed: 50f);
        track.Append(new TrackPiece(100f, Chamber));
        split = track.AppendSplit(new TrackPiece(400f, Chamber), Tube,
        [
            Keys((0, -7.5f), (120, -22f), (280, -22f), (400, -7.5f)),
            Keys((0, 7.5f), (120, 22f), (280, 22f), (400, 7.5f)),
        ]);
        track.Append(new TrackPiece(200f, Chamber));
        return track;
    }

    private static IReadOnlyList<OffsetKey> Keys(params (float Along, float X)[] keys) =>
        keys.Select(k => new OffsetKey(k.Along, new Vector2(k.X, 0f))).ToList();

    private static double Distance(Vector3d a, Vector3d b)
    {
        var d = a - b;
        return Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
    }

    private static string SplitLevel(string obstacle) => $$"""
        {
          "sections": {
            "chamber": { "halfWidth": 14, "halfHeight": 6.5, "squareness": 0.6 },
            "tube": { "radius": 6 }
          },
          "start": "chamber",
          "track": [
            { "length": 100 },
            { "length": 400, "split": {
                "section": "tube",
                "branches": [
                  { "offsets": [[0, -7.5, 0], [400, -7.5, 0]] },
                  { "offsets": [[0, 7.5, 0], [400, 7.5, 0]], "speed": 1.25 }
                ]
            } },
            { "length": 100 }
          ],
          "obstacles": [ {{obstacle}} ]
        }
        """;
}
