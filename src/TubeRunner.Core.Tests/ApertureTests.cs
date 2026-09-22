using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

/// <summary>
/// Apertures and locked power-ups: the two things a key can open besides a door. An aperture is a
/// ring of blades sealing the tube that irises open as its keys fall; a locked pad is a power-up
/// that has to be shot for before it can be taken.
/// </summary>
public class ApertureTests
{
    private const string Keys = """
        { "at": 100, "kind": "target", "group": "k1" },
        { "at": 110, "kind": "target", "group": "k2" },
        { "at": 120, "kind": "target", "group": "k3" }
        """;

    [Fact]
    public void AnAperture_BecomesOneBladePerSector_EachLockedByAKey()
    {
        var level = Parse("""{ "at": 300, "blades": 6, "keys": ["k1", "k2", "k3"] }""");
        var blades = level.Obstacles.Where(o => o.Aperture is not null).OrderBy(o => o.Blade).ToList();

        Assert.Equal(6, blades.Count);
        Assert.All(blades, b => Assert.Equal(6, b.BladeCount));
        Assert.All(blades, b => Assert.Equal(300, b.S));
        // Three keys over six blades open opposite pairs together, which is what keeps an aperture
        // inside the four colours a player can tell apart.
        Assert.Equal(["k1", "k2", "k3", "k1", "k2", "k3"], blades.Select(b => b.LockedBy));

        // Together the blades cover the whole way round: an aperture nobody has shot for is a wall.
        var shape = new ProfileShape(level.Track.SectionAt(300));
        Assert.Equal(shape.Perimeter, blades.Sum(b => b.Width), precision: 3);
    }

    [Fact]
    public void Open_LeavesThatManyBladesOut_SoThereIsAWayThrough()
    {
        var level = Parse("""{ "at": 300, "blades": 8, "open": 2, "keys": ["k1"] }""");
        var blades = level.Obstacles.Where(o => o.Aperture is not null).ToList();

        Assert.Equal(6, blades.Count);
        Assert.DoesNotContain(blades, b => b.Blade < 2);
        Assert.All(blades, b => Assert.Equal("k1", b.LockedBy));
    }

    [Theory]
    // A ring needs blades, and a key that opens nothing is a key the player will hunt for in vain.
    [InlineData("""{ "at": 300, "blades": 2, "keys": ["k1"] }""", "blades")]
    [InlineData("""{ "at": 300, "blades": 6, "open": 6, "keys": ["k1"] }""", "open")]
    [InlineData("""{ "at": 300, "blades": 6, "keys": [] }""", "keys")]
    [InlineData("""{ "at": 300, "blades": 3, "keys": ["k1", "k2", "k3", "k1"] }""", "open nothing")]
    [InlineData("""{ "at": 300, "blades": 6, "keys": ["nope"] }""", "nothing is in")]
    public void ABadAperture_FailsToLoad(string aperture, string says)
    {
        Assert.Contains(says, Assert.Throws<LevelFormatException>(() => Parse(aperture)).Message);
    }

    [Fact]
    public void AnApertureOnAFlatSection_FailsToLoad()
    {
        var e = Assert.Throws<LevelFormatException>(() => LevelLoader.Parse("""
            {
              "sections": { "tube": { "radius": 6 }, "flat": { "radius": 6, "opening": 1 } },
              "start": "flat",
              "track": [ { "length": 600, "obstacles": [ { "at": 100, "kind": "target", "group": "k1" } ],
                           "apertures": [ { "at": 300, "keys": ["k1"] } ] } ]
            }
            """));

        Assert.Contains("closed tube", e.Message);
    }

    // A door or a pad naming a group nothing is in loads and plays, and is simply a wall that never
    // opens or a pad that never lights. Both read as the player having missed a shot.
    [Fact]
    public void AMisspelledLock_FailsToLoad()
    {
        Assert.Contains("'kee'", Assert.Throws<LevelFormatException>(() => LevelLoader.Parse("""
            {
              "sections": { "tube": { "radius": 6 } },
              "start": "tube",
              "track": [ { "length": 600 } ],
              "obstacles": [ { "at": 100, "kind": "target", "group": "key" }, { "at": 300, "lockedBy": "kee" } ]
            }
            """)).Message);

        Assert.Contains("'kee'", Assert.Throws<LevelFormatException>(() => LevelLoader.Parse("""
            {
              "sections": { "tube": { "radius": 6 } },
              "start": "tube",
              "track": [ { "length": 600 } ],
              "obstacles": [ { "at": 100, "kind": "target", "group": "key" } ],
              "pickups": [ { "at": 300, "kind": "shield", "lockedBy": "kee" } ]
            }
            """)).Message);
    }

    // A key further down the track than the thing it opens cannot be shot in time however well the
    // level is flown, and the door or dead pad that results looks exactly like a missed shot.
    [Fact]
    public void AKeyBehindWhatItOpens_FailsToLoad()
    {
        var e = Assert.Throws<LevelFormatException>(() => LevelLoader.Parse("""
            {
              "sections": { "tube": { "radius": 6 } },
              "start": "tube",
              "track": [ { "length": 600 } ],
              "obstacles": [ { "at": 100, "kind": "target", "group": "k" }, { "at": 400, "kind": "target", "group": "k" } ],
              "pickups": [ { "at": 300, "kind": "shield", "lockedBy": "k" } ]
            }
            """));

        // The whole group has to be down, so it is the last key that has to come first.
        Assert.Contains("last key is at 400", e.Message);
    }

    [Fact]
    public void ALockedPad_IsDeadUntilItsKeyIsShot()
    {
        // The key is out of the ship's line on the ceiling, so it can only be shot, never rammed.
        var key = new Obstacle { Kind = ObstacleKind.Target, S = 250, Surface = Surface.Ceiling, Group = "k" };
        var pad = new Pickup { Kind = PickupKind.RingGun, S = 400, LockedBy = "k" };
        var game = Game([key], [pad]);

        Fly(game, to: 500);

        Assert.False(key.Destroyed);
        Assert.False(pad.Collected);
        Assert.Equal(0, game.RingCharges);
    }

    [Fact]
    public void ALockedPad_LightsUpOnceItsKeyIsShot()
    {
        // Wide enough to be in the ship's line of fire without being in its way.
        var key = new Obstacle { Kind = ObstacleKind.Target, S = 250, Surface = Surface.Ceiling, Width = 40f, Group = "k" };
        var pad = new Pickup { Kind = PickupKind.RingGun, S = 400, LockedBy = "k" };
        var game = Game([key], [pad]);

        Fly(game, to: 500, new ShipInput(Fire: true));

        Assert.True(key.Destroyed, "the key should have been shot on the way");
        Assert.True(pad.Collected);
        Assert.True(game.RingCharges > 0);
    }

    // A ring opening is machinery moving, and the session says so once per key that opens some
    // of it - so a three-key ring says it three times, and a key that opens nothing says nothing.
    [Fact]
    public void ARingSaysWhenAKeyOpensSomeOfIt()
    {
        var key = new Obstacle { Kind = ObstacleKind.Target, S = 250, Surface = Surface.Ceiling, Width = 40f, Group = "k" };
        var loose = new Obstacle { Kind = ObstacleKind.Target, S = 300, Surface = Surface.Ceiling, Width = 40f, Group = "nothing" };
        var blade = new Obstacle { Kind = ObstacleKind.Block, S = 500, Width = 6f, Aperture = "ring", Blade = 0, BladeCount = 6, LockedBy = "k" };
        var game = Game([key, loose, blade], []);

        int opened = 0;
        for (int i = 0; i < 60 * 8 && game.Ship.Position.S < 450; i++)
        {
            game.Step(1f / 60f, new ShipInput(Fire: true));
            opened += game.Events.Count(e => e == SessionEvent.ApertureOpened);
        }

        Assert.True(key.Destroyed && loose.Destroyed, "both targets should have been shot on the way");
        Assert.Equal(1, opened);
    }

    [Fact]
    public void APadWithNoLock_IsAlwaysThere()
    {
        var pad = new Pickup { Kind = PickupKind.RingGun, S = 400 };
        var game = Game([], [pad]);

        Fly(game, to: 500);

        Assert.True(pad.Collected);
    }

    private static Level Parse(string aperture) => LevelLoader.Parse($$"""
        {
          "sections": { "tube": { "radius": 6 } },
          "start": "tube",
          "track": [ { "length": 600 } ],
          "obstacles": [ {{Keys}} ],
          "apertures": [ {{aperture}} ]
        }
        """);

    private static readonly CrossSection Tube = CrossSection.Circle(6f);
    private static readonly SessionSettings Settings = new(new ShipSettings(SteerSpeed: 10f));
    private static readonly TrackPosition Start = new(0, Surface.Floor, 0f);

    private static GameSession Game(Obstacle[] obstacles, Pickup[] pickups)
    {
        var track = new Track(Tube, startSpeed: 50f);
        track.Append(new TrackPiece(2000f, Tube));
        return new GameSession(track, obstacles, Settings, Start, pickups);
    }

    private static void Fly(GameSession game, double to, ShipInput input = default)
    {
        for (int i = 0; i < 20_000 && game.Ship.Position.S < to && game.State == SessionState.Playing; i++)
        {
            game.Step(1f / 60f, input);
        }
    }
}
