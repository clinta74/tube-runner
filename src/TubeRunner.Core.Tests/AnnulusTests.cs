using System.Numerics;
using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

/// <summary>
/// Sections with a core: a cylinder down the middle of the tube, with the ship flying in the ring
/// between the two walls. It is a third arrangement alongside the closed tube and the open planes,
/// and the rules that separate it from both are the ones worth holding: each wall closes on itself,
/// neither carries onto the other, and the only way across is a jump.
/// </summary>
public class AnnulusTests
{
    private static readonly CrossSection Ring = new(12f, 12f, Squareness: 0f, Opening: 0f, Core: 0.5f);

    [Fact]
    public void TheWallsFaceEachOther_LikeAFloorAndACeiling()
    {
        var shape = new ProfileShape(Ring);

        // Both walls are measured from the bottom, so X = 0 is the underside of each.
        Assert.Equal(new Vector2(0f, -12f), shape.PointAt(Surface.Floor, 0f), Compare);
        Assert.Equal(new Vector2(0f, -6f), shape.PointAt(Surface.Ceiling, 0f), Compare);

        // The outer wall's normal points inwards, the core's back out, so the ship hangs under the
        // core exactly as it hangs from the ceiling of a flat section.
        Assert.Equal(new Vector2(0f, 1f), shape.NormalAt(Surface.Floor, 0f), Compare);
        Assert.Equal(new Vector2(0f, -1f), shape.NormalAt(Surface.Ceiling, 0f), Compare);
    }

    [Fact]
    public void EachWallGoesAllTheWayRoundOnItsOwn()
    {
        var shape = new ProfileShape(Ring);
        float outer = shape.PerimeterOf(Surface.Floor);
        float core = shape.PerimeterOf(Surface.Ceiling);

        Assert.Equal(outer / 2f, core, 3);

        // Past the far side of a wall comes back round the same wall. In a hollow tube the same
        // move would have carried the ship onto the other surface.
        var (surface, x) = shape.Wrap(Surface.Floor, outer * 0.75f);
        Assert.Equal(Surface.Floor, surface);
        Assert.Equal(-outer * 0.25f, x, 3);

        (surface, x) = shape.Wrap(Surface.Ceiling, -core * 0.75f);
        Assert.Equal(Surface.Ceiling, surface);
        Assert.Equal(core * 0.25f, x, 3);
    }

    [Fact]
    public void AcrossKeepsThePlaceAroundTheSection_NotTheDistance()
    {
        var shape = new ProfileShape(Ring);
        float quarterWayRound = shape.PerimeterOf(Surface.Floor) / 4f;

        // A quarter of the way round the outer wall is a quarter of the way round the core, which
        // is half as far in units. Landing on the raw X would slide the ship a quarter turn.
        float onCore = shape.Across(Surface.Floor, quarterWayRound);
        Assert.Equal(shape.PerimeterOf(Surface.Ceiling) / 4f, onCore, 3);

        // The two points really are on the same radius out from the middle.
        var outer = shape.PointAt(Surface.Floor, quarterWayRound);
        var core = shape.PointAt(Surface.Ceiling, onCore);
        Assert.Equal(MathF.Atan2(outer.Y, outer.X), MathF.Atan2(core.Y, core.X), 3);

        Assert.Equal(quarterWayRound, shape.Across(Surface.Ceiling, onCore), 3);
    }

    // Nothing on one wall can be flown into from the other, whatever the gap measures - the same
    // rule open planes follow, and what makes a block on the core a thing to jump to rather than
    // steer around.
    [Fact]
    public void TheTwoWallsAreOutOfReachOfEachOther()
    {
        var shape = new ProfileShape(Ring);

        Assert.Equal(float.PositiveInfinity, TrackSpace.SurfaceDistance(shape, Surface.Floor, 0f, Surface.Ceiling, 0f));
        // And along one wall it is measured the short way round, as in any closed section.
        float around = shape.PerimeterOf(Surface.Floor);
        Assert.Equal(around * 0.25f, TrackSpace.SurfaceDistance(shape, Surface.Floor, -around * 0.4f, Surface.Floor, around * 0.35f), 3);
    }

    [Fact]
    public void AJumpIsAlwaysAvailableInARing_AndTheShipLandsWhereItLeft()
    {
        var track = new Track(Ring, startSpeed: 60f);
        track.Append(new TrackPiece(1200f, Ring));
        var game = new GameSession(track, [], new SessionSettings(new ShipSettings(SteerSpeed: 22f)),
            new TrackPosition(0, Surface.Floor, 0f));

        Assert.True(game.Ship.CanJump, "a ring has a wall overhead everywhere in it");

        // Steer a quarter of the way round the outer wall, then cross to the core.
        for (int i = 0; i < 36; i++) game.Step(1f / 60f, new ShipInput(Steer: 1f));
        float before = game.Ship.Position.X;
        var shape = new ProfileShape(Ring);
        float angle = before / shape.PerimeterOf(Surface.Floor);

        game.Step(1f / 60f, new ShipInput(Jump: true));
        for (int i = 0; i < 60 && game.Ship.IsJumping; i++) game.Step(1f / 60f, default);

        Assert.Equal(Surface.Ceiling, game.Ship.Position.Surface);
        // Same place around the ring, which on the smaller wall is a smaller X.
        Assert.Equal(angle, game.Ship.Position.X / shape.PerimeterOf(Surface.Ceiling), 2);
    }

    // A tube's ceiling is the upper half of its wall; a ring's is the core. The name means two
    // different places either side of the change, so a ship riding the ceiling when a core grows in
    // has to be carried onto the outer wall - which is where it already was - rather than dropped
    // onto the core, which would be a teleport across the whole bore.
    [Fact]
    public void AShipOnTheCeiling_StaysWhereItIsWhenACoreGrowsIn()
    {
        // A hollow piece, then one carrying a core: the core takes the whole of its own piece, so it
        // begins exactly at the join.
        var tube = CrossSection.Circle(12f);
        var track = new Track(tube, startSpeed: 60f);
        track.Append(new TrackPiece(300f, tube));
        track.Append(new TrackPiece(300f, Ring));

        var game = new GameSession(track, [], new SessionSettings(new ShipSettings(SteerSpeed: 22f)),
            new TrackPosition(0, Surface.Ceiling, 0f));

        // Riding the top of the hollow tube, which is where a ceiling is when there is no core.
        var before = game.Ship.Pose(0f).Point;
        Assert.Equal(new Vector2(0f, 12f), before, Compare);

        while (game.Ship.Position.S < 301) game.Step(1f / 60f, default);

        // The top of the tube has become part of the outer wall, which is one surface all the way
        // round. Same place, under a different name - not a drop onto the core.
        Assert.Equal(Surface.Floor, game.Ship.Position.Surface);
        Assert.Equal(before, game.Ship.Pose(0f).Point, Compare);
    }

    // A ring is authored by how much room it leaves to fly in, and the most it may leave is a share
    // of the bore - so a taller ring is something a level buys by making the tube wider, rather than
    // by whittling the core down to a pole.
    [Fact]
    public void ATallerRing_NeedsAWiderBore()
    {
        Assert.Contains("between 3 and 8.4", Error(12f, 10f));

        var wider = LevelLoader.Parse(Level(20f, 10f)).Track.SectionAt(0);
        Assert.True(wider.IsAnnulus);
        Assert.Equal(10f, wider.RingHeight, 3);
        Assert.Equal(14f, wider.MaxRingHeight, 3);
    }

    [Theory]
    [InlineData(2f)]    // no room for the ship, let alone its jump
    [InlineData(10f)]   // nothing left of the core but a pole
    public void ARingOutsideTheUsableRange_FailsToLoad(float height)
    {
        Assert.Contains("must be between", Error(12f, height));
    }

    [Fact]
    public void ARingInAnOpenSection_FailsToLoad()
    {
        var e = Assert.Throws<LevelFormatException>(() => LevelLoader.Parse("""
            {
              "sections": { "bad": { "radius": 12, "ringHeight": 5, "opening": 1 } },
              "start": "bad",
              "track": [ { "length": 400 } ]
            }
            """));

        Assert.Contains("nothing for the core to sit inside", e.Message);
    }

    // A core arrives whole and stops whole, but between two rings it blends: a sleeve widening into
    // a roomier ring is a taper in the core, not a step in it at the piece boundary.
    [Fact]
    public void ACoreSnapsInFromNothing_ButBlendsBetweenRings()
    {
        var hollow = CrossSection.Circle(20f);
        var tight = hollow.WithRing(5f);
        var roomy = hollow.WithRing(12f);

        Assert.Equal(roomy.Core, CrossSection.Lerp(hollow, roomy, 0.01f).Core, 5);
        Assert.Equal(roomy.Core, CrossSection.Lerp(hollow, roomy, 0.5f).Core, 5);
        Assert.Equal(0f, CrossSection.Lerp(roomy, hollow, 0.5f).Core, 5);

        float mid = CrossSection.Lerp(tight, roomy, 0.5f).Core;
        Assert.InRange(mid, Math.Min(tight.Core, roomy.Core) + 0.01f, Math.Max(tight.Core, roomy.Core) - 0.01f);
    }

    // A collar is the ring's version of a flat section's full-width wall: no way past it on the
    // surface, only the jump. Its width is the wall's own perimeter, looked up rather than written,
    // so a level can say "full" on a core without knowing how far round the core is.
    [Fact]
    public void AFullBlock_IsAsWideAsItsWall()
    {
        var level = LevelLoader.Parse("""
            {
              "sections": { "ring": { "radius": 20, "ringHeight": 10 } },
              "start": "ring",
              "track": [ { "length": 600, "obstacles": [
                { "at": 300, "surface": "ceiling", "full": true },
                { "at": 400, "full": true }
              ] } ]
            }
            """);
        var shape = new ProfileShape(level.Track.SectionAt(300));

        var onCore = level.Obstacles[0];
        Assert.True(onCore.Full);
        Assert.Equal(shape.PerimeterOf(Surface.Ceiling), onCore.Width, 3);
        Assert.Equal(shape.PerimeterOf(Surface.Floor), level.Obstacles[1].Width, 3);
    }

    [Fact]
    public void ACollarOnTheCore_HitsFromAnywhereRoundIt_AndIsJumpedOver()
    {
        var track = new Track(Ring, startSpeed: 60f);
        track.Append(new TrackPiece(1200f, Ring));
        var shape = new ProfileShape(Ring);
        // Obstacles hold per-run state, so each session gets its own.
        Obstacle Collar() => new()
        {
            Kind = ObstacleKind.Block, S = 400, Surface = Surface.Ceiling,
            Width = shape.PerimeterOf(Surface.Ceiling), Full = true, Height = 2f,
        };

        // Riding the core a third of the way round from where the collar is centred: still hit.
        var settings = new SessionSettings(new ShipSettings(SteerSpeed: 22f));
        var game = new GameSession(track, [Collar()], settings, new TrackPosition(0, Surface.Ceiling, shape.PerimeterOf(Surface.Ceiling) / 3f));
        for (int i = 0; i < 600 && game.Ship.Position.S < 500; i++) game.Step(1f / 60f, default);
        Assert.Equal(2, game.Shields);

        // Jumping off the core just before it clears it, the way a flat's full wall is cleared.
        var jumper = new GameSession(track, [Collar()], settings, new TrackPosition(0, Surface.Ceiling, 0f));
        for (int i = 0; i < 600 && jumper.Ship.Position.S < 380; i++) jumper.Step(1f / 60f, default);
        jumper.Step(1f / 60f, new ShipInput(Jump: true));
        for (int i = 0; i < 600 && jumper.Ship.Position.S < 500; i++) jumper.Step(1f / 60f, default);
        Assert.Equal(3, jumper.Shields);
    }

    [Theory]
    [InlineData("""{ "at": 300, "kind": "target", "full": true }""", "blocks and plates")]
    [InlineData("""{ "at": 300, "full": true, "period": 2 }""", "gate or a mover")]
    public void AFullBlock_ThatCannotBeOne_FailsToLoad(string obstacle, string says)
    {
        var e = Assert.Throws<LevelFormatException>(() => LevelLoader.Parse($$"""
            {
              "sections": { "ring": { "radius": 20, "ringHeight": 10 } },
              "start": "ring",
              "track": [ { "length": 600, "obstacles": [ {{obstacle}} ] } ]
            }
            """));
        Assert.Contains(says, e.Message);
    }

    [Fact]
    public void AFullBlockOnAFlatSection_SaysToUseWidth80()
    {
        var e = Assert.Throws<LevelFormatException>(() => LevelLoader.Parse("""
            {
              "sections": { "flat": { "radius": 6, "opening": 1 } },
              "start": "flat",
              "track": [ { "length": 600, "obstacles": [ { "at": 300, "full": true } ] } ]
            }
            """));
        Assert.Contains("'width' 80", e.Message);
    }

    private static string Error(float radius, float height) =>
        Assert.Throws<LevelFormatException>(() => LevelLoader.Parse(Level(radius, height))).Message;

    private static string Level(float radius, float height) => $$"""
        {
          "sections": { "ring": { "radius": {{radius}}, "ringHeight": {{height}} } },
          "start": "ring",
          "track": [ { "length": 400 } ]
        }
        """;

    private static readonly VectorComparer Compare = new();

    private sealed class VectorComparer : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) < 1e-3f;
        public int GetHashCode(Vector2 v) => 0;
    }
}
