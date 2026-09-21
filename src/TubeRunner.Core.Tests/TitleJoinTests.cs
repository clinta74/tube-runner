using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

/// <summary>
/// The title screen flies the first level behind a straight lead-in and hands the ship over to the
/// level without a cut. None of the drawing can be tested here, but the three things the drawing
/// relies on can: that the lead-in changes nothing about the level except where it is, that it is
/// long enough to hide the level from a ship holding station in it, and that moving the ship a whole
/// number of wall segments comes with the index shift that keeps the walls looking the same.
/// </summary>
public class TitleJoinTests
{
    private const string Level = """
        {
          "name": "Opening", "id": "opening", "next": "second.json", "speed": 70, "segmentLength": 60,
          "sections": { "tube": { "radius": 6 }, "wide": { "radius": 9 } },
          "start": "tube",
          "track": [
            { "length": 200 },
            { "length": 200, "turn": 25, "obstacles": [ { "at": 40 } ] },
            { "length": 300, "section": "wide", "pickups": [ { "at": 100, "kind": "shield" } ] }
          ],
          "obstacles": [ { "at": 650, "kind": "target", "angle": 90 } ]
        }
        """;

    [Fact]
    public void AHeader_IsReadWithoutBuildingTheLevel()
    {
        var header = LevelLoader.ReadHeader(Level);

        Assert.Equal(new LevelHeader("Opening", "opening", "second.json"), header);
        // A level with no id of its own gets the same one a full parse would give it.
        string unnamed = Level.Replace("\"id\": \"opening\", ", "");
        Assert.Equal(LevelLoader.Parse(unnamed).Id, LevelLoader.ReadHeader(unnamed).Id);
    }

    [Fact]
    public void ALeadIn_MovesEverythingAlong_AndChangesNothingElse()
    {
        var plain = LevelLoader.Parse(Level);
        var led = LevelLoader.ParseWithLeadIn(Level, 300f);

        Assert.Equal(plain.Track.Length + 300.0, led.Track.Length, 6);
        // Things placed inside a piece and things placed against the whole track both move.
        Assert.Equal(plain.Obstacles.Select(o => o.S + 300.0), led.Obstacles.Select(o => o.S));
        Assert.Equal(plain.Pickups.Select(p => p.S + 300.0), led.Pickups.Select(p => p.S));

        // The lead-in is the level's own opening carried back: same bore, same speed, dead straight.
        Assert.Equal(plain.Track.SectionAt(0), led.Track.SectionAt(150));
        Assert.Equal(plain.Track.SpeedAt(0), led.Track.SpeedAt(150));
        Assert.Equal(led.Track.FrameAt(0).Forward, led.Track.FrameAt(300).Forward);

        // And from the join on, the track is the level's, place for place.
        foreach (double s in new[] { 0.0, 16.0, 250.0, 399.0, 560.0 })
        {
            Assert.Equal(plain.Track.SectionAt(s), led.Track.SectionAt(s + 300.0));
            var a = plain.Track.FrameAt(s);
            var b = led.Track.FrameAt(s + 300.0);
            Assert.Equal(a.Forward.X, b.Forward.X, 4);
            Assert.Equal(a.Forward.Z, b.Forward.Z, 4);
        }
    }

    [Fact]
    public void TheStraightStart_EndsAtTheFirstThingThatChangesTheTube()
    {
        Assert.Equal(200.0, LevelLoader.Parse(Level).Track.StraightStart, 6);

        // A change of section counts as much as a bend does.
        var tube = CrossSection.Circle(6f);
        var track = new Track(tube, startSpeed: 70f);
        track.Append(new TrackPiece(120f, tube));
        track.Append(new TrackPiece(80f, tube));
        track.Append(new TrackPiece(200f, CrossSection.Circle(9f)));
        Assert.Equal(200.0, track.StraightStart, 6);

        // A track that never changes is straight to the end of it.
        var plain = new Track(tube, startSpeed: 70f);
        plain.Append(new TrackPiece(500f, tube));
        Assert.Equal(500.0, plain.StraightStart, 6);
    }

    [Theory]
    // First Loop's numbers: 340 of view, 200 straight, holding between 60 and 120.
    [InlineData(60.0, 60.0, 340.0, 200.0, 300.0)]
    // Already a whole number of segments: not rounded up another one.
    [InlineData(60.0, 60.0, 320.0, 200.0, 240.0)]
    // A level straight for longer than anyone can see needs no lead-in at all.
    [InlineData(60.0, 60.0, 340.0, 900.0, 0.0)]
    public void TheLeadIn_HidesTheLevelFromAShipHoldingStation(double segment, double hold, double view, double clear, double expected)
    {
        double lead = SegmentJoin.LeadIn(segment, hold, view, clear);

        Assert.Equal(expected, lead, 6);
        Assert.Equal(0.0, lead % segment, 6);
        // From the far end of where the ship is held, the first change in the tube is out of sight.
        Assert.True(lead + clear - (hold + segment) >= view);
    }

    [Fact]
    public void MovingAWholeNumberOfSegments_ShiftsTheIndicesByThatNumber()
    {
        // The treadmill: back one segment, on one index.
        Assert.Equal(1, SegmentJoin.IndexOffset(121.5, 61.5, 60.0));
        // The handover: out of a 300 lead-in to the level's own start.
        Assert.Equal(5, SegmentJoin.IndexOffset(316.4, 16.4, 60.0));

        // Which is what keeps the walls the same: the segment the ship was in, under its old
        // index, and the one it is in now, under its new index plus the shift, are one number.
        foreach (double s in new[] { 300.0, 316.4, 359.9, 360.0, 1234.5 })
        {
            double to = s - 300.0;
            int shift = SegmentJoin.IndexOffset(s, to, 60.0);
            Assert.Equal((int)Math.Floor(s / 60.0), (int)Math.Floor(to / 60.0) + shift);
        }
    }
}
