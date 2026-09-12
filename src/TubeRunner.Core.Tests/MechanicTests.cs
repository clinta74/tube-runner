using TubeRunner.Core;

namespace TubeRunner.Core.Tests;

/// <summary>
/// The mechanics levels 10-26 are built on. Each one is a rule the level files rely on, and none of
/// them shows up in a build error if it silently stops working.
/// </summary>
public class MechanicTests
{
    private static readonly CrossSection Tube = CrossSection.Circle(6f);
    private static readonly SessionSettings Settings = new(new ShipSettings(SteerSpeed: 10f));

    [Fact]
    public void Gate_IsSolidForHalfItsCycleAndGoneForTheOther()
    {
        var gate = new Obstacle { Kind = ObstacleKind.Block, S = 400, Period = 2f };

        Assert.True(gate.IsSolidAt(0f));
        Assert.True(gate.IsSolidAt(0.9f));
        Assert.False(gate.IsSolidAt(1.1f));
        Assert.False(gate.IsSolidAt(1.9f));
        Assert.True(gate.IsSolidAt(2.1f));   // round again
    }

    [Fact]
    public void Gate_PhaseStaggersTheCycle_SoGatesCanFormARhythm()
    {
        var early = new Obstacle { Kind = ObstacleKind.Block, S = 400, Period = 2f };
        var late = new Obstacle { Kind = ObstacleKind.Block, S = 400, Period = 2f, Phase = 0.5f };

        Assert.True(early.IsSolidAt(0f));
        Assert.False(late.IsSolidAt(0f));
    }

    [Fact]
    public void Obstacle_WithoutAPeriod_IsAlwaysSolid()
    {
        var block = new Obstacle { Kind = ObstacleKind.Block, S = 400 };

        Assert.True(block.IsSolidAt(0f));
        Assert.True(block.IsSolidAt(97.3f));
    }

    [Fact]
    public void Gate_LetsTheShipThroughWhileItIsOpen()
    {
        // Shut when the ship arrives, it would cost a shield; phased open, it costs nothing.
        Assert.Equal(3, ShieldsAfterFlyingAt(phase: 0.5f));
        Assert.Equal(2, ShieldsAfterFlyingAt(phase: 0f));

        static int ShieldsAfterFlyingAt(float phase)
        {
            // 400 units at 50 u/s is 8 seconds, so a 40-second cycle is still in its first half.
            var gate = new Obstacle { Kind = ObstacleKind.Block, S = 400, Width = 40f, Period = 40f, Phase = phase };
            var game = Game([gate]);
            Fly(game, to: 500);
            return game.Shields;
        }
    }

    [Fact]
    public void Mover_SlidesAroundTheSurfaceOverTime()
    {
        var mover = new Obstacle { Kind = ObstacleKind.Block, S = 400, X = 0f, Sweep = 8f, SweepTime = 4f };

        Assert.Equal(0f, mover.XAt(0f), precision: 3);
        Assert.Equal(8f, mover.XAt(1f), precision: 3);    // a quarter through, fully out
        Assert.Equal(0f, mover.XAt(2f), precision: 3);
        Assert.Equal(-8f, mover.XAt(3f), precision: 3);   // and back the other way
    }

    [Fact]
    public void Plate_CannotBeShot()
    {
        var plate = new Obstacle { Kind = ObstacleKind.Plate, S = 300, Width = 40f };
        var game = Game([plate]);

        // Hold fire all the way up to it; it should still be standing when the ship arrives.
        bool blocked = false;
        Fly(game, to: 250, new ShipInput(Fire: true), step: g => blocked |= g.Events.Contains(SessionEvent.ShotBlocked));

        Assert.False(plate.Destroyed);
        Assert.True(blocked, "shots should have been stopped by the plate");
    }

    [Fact]
    public void Plate_CannotBeRammed_SoUnstoppableIsNotAUniversalAnswer()
    {
        var plate = new Obstacle { Kind = ObstacleKind.Plate, S = 400, Width = 40f };
        var pickup = new Pickup { Kind = PickupKind.Unstoppable, S = 300 };
        var game = new GameSession(Track(), [plate], Settings, Start, [pickup]);

        // Stop just past the plate. Unstoppable lasts 6 s and the track runs at 50 u/s, so flying on
        // to 500 lets it lapse before the assertion - which would then prove nothing about the plate.
        Fly(game, to: 420);

        Assert.True(game.RamLeft > 0f, "the ship should still be unstoppable at the plate");
        Assert.False(plate.Destroyed);
        Assert.Equal(2, game.Shields);
    }

    [Fact]
    public void OrderedGroup_OnlyBreaksInTurn()
    {
        // The claim is about one instant: while the first still stands, the second cannot be broken.
        // Holding fire past that moment proves nothing, because once the first goes the second's turn
        // has legitimately come and the next shot takes it - which is the mechanic working.
        var second = new Obstacle { Kind = ObstacleKind.Target, S = 300, Width = 40f, Group = "gate", Order = 1 };
        var first = new Obstacle { Kind = ObstacleKind.Target, S = 360, Width = 40f, Group = "gate", Order = 0 };
        var game = Game([second, first]);

        bool secondSurvivedItsTurn = true;
        Fly(game, to: 250, new ShipInput(Fire: true),
            step: _ => { if (!first.Destroyed) secondSurvivedItsTurn &= !second.Destroyed; });

        Assert.True(first.Destroyed, "the far target is first in the order and should have gone");
        Assert.True(secondSurvivedItsTurn, "the second broke while the first was still standing");
        Assert.Equal(3, game.Shields);   // nothing was flown into
    }

    [Fact]
    public void OrderedGroup_BreaksOnceItsTurnComes()
    {
        // Nothing earlier in the group, so it is shootable from the start.
        var only = new Obstacle { Kind = ObstacleKind.Target, S = 400, Width = 40f, Group = "gate", Order = 1 };
        var gone = new Obstacle { Kind = ObstacleKind.Target, S = 900, Width = 40f, Group = "gate", Order = 0 };
        gone.Destroyed = true;
        var game = Game([only, gone]);

        Fly(game, to: 300, new ShipInput(Fire: true));

        Assert.True(only.Destroyed, "with everything earlier in its group gone, it should break");
    }

    [Fact]
    public void RingGun_DoesNotSkipAnOrderedGroup()
    {
        var second = new Obstacle { Kind = ObstacleKind.Target, S = 300, Width = 40f, Group = "gate", Order = 1 };
        var first = new Obstacle { Kind = ObstacleKind.Target, S = 300, Width = 40f, Group = "gate", Order = 0 };
        var pickup = new Pickup { Kind = PickupKind.RingGun, S = 100 };
        var game = new GameSession(Track(), [second, first], Settings, Start, [pickup]);

        Fly(game, to: 280, new ShipInput(Special: true));

        // A ring passes an ordered group by entirely. Letting it take them in turn is not enough:
        // it overlaps for several frames, so it would clear the whole group one member per frame.
        Assert.False(first.Destroyed);
        Assert.False(second.Destroyed);
    }

    [Fact]
    public void LockedGate_OpensOnceItsKeysAreShot()
    {
        var key = new Obstacle { Kind = ObstacleKind.Target, S = 250, Width = 40f, Group = "key" };
        var gate = new Obstacle { Kind = ObstacleKind.Block, S = 400, Width = 40f, LockedBy = "key" };
        var game = Game([key, gate]);

        Fly(game, to: 500, new ShipInput(Fire: true));

        Assert.True(key.Destroyed, "the key should have been shot on the way");
        Assert.Equal(3, game.Shields);   // the gate opened, so the wall cost nothing
    }

    [Fact]
    public void LockedGate_StaysShutWhileItsKeyStands()
    {
        // The key sits on the ceiling and out of the ship's line, so flying past cannot take it:
        // a collision destroys an obstacle too, which would unlock the gate without a shot fired.
        var key = new Obstacle { Kind = ObstacleKind.Target, S = 250, Surface = Surface.Ceiling, Group = "key" };
        var gate = new Obstacle { Kind = ObstacleKind.Block, S = 400, Width = 40f, LockedBy = "key" };
        var game = Game([key, gate]);

        // Never fire: the key survives, so the gate is a wall.
        Fly(game, to: 500);

        Assert.False(key.Destroyed);
        Assert.Equal(2, game.Shields);
    }

    [Fact]
    public void SpeedLimit_CostsAShieldWhenItIsTakenTooFast()
    {
        var zone = new SpeedLimit { S = 400, Length = 120f, MaxSpeed = 30f };   // track runs at 50
        var game = new GameSession(Track(), [], Settings, Start, null, null, null, [zone]);

        Fly(game, to: 500);

        Assert.True(zone.Tripped);
        Assert.Equal(2, game.Shields);
    }

    [Fact]
    public void SpeedLimit_LetsAShipUnderTheLimitThrough()
    {
        var zone = new SpeedLimit { S = 400, Length = 120f, MaxSpeed = 80f };
        var game = new GameSession(Track(), [], Settings, Start, null, null, null, [zone]);

        Fly(game, to: 500);

        Assert.False(zone.Tripped);
        Assert.Equal(3, game.Shields);
    }

    [Fact]
    public void SpeedLimit_CostsOneShieldPerPass_NotOnePerFrame()
    {
        var zone = new SpeedLimit { S = 400, Length = 200f, MaxSpeed = 10f };
        var game = new GameSession(Track(), [], Settings, Start, null, null, null, [zone]);

        Fly(game, to: 600);

        // A long zone flown far too fast still only bites while recovery is down.
        Assert.True(game.Shields >= 1, $"the zone took {3 - game.Shields} shields in one pass");
    }

    [Fact]
    public void Loader_ReadsTheNewMechanics()
    {
        var level = LevelLoader.Parse("""
            {
              "sections": { "tube": { "radius": 6 } },
              "start": "tube",
              "track": [
                { "length": 600,
                  "obstacles": [
                    { "at": 100, "period": 3, "phase": 0.25 },
                    { "at": 200, "sweep": 9, "sweepTime": 5 },
                    { "at": 300, "kind": "plate" },
                    { "at": 400, "kind": "target", "group": "a", "order": 2 },
                    { "at": 500, "lockedBy": "a" }
                  ],
                  "speedLimits": [ { "at": 550, "length": 80, "maxSpeed": 90 } ]
                }
              ]
            }
            """);

        Assert.Equal(3f, level.Obstacles[0].Period);
        Assert.Equal(0.25f, level.Obstacles[0].Phase);
        Assert.Equal(9f, level.Obstacles[1].Sweep);
        Assert.Equal(5f, level.Obstacles[1].SweepTime);
        Assert.Equal(ObstacleKind.Plate, level.Obstacles[2].Kind);
        Assert.Equal("a", level.Obstacles[3].Group);
        Assert.Equal(2, level.Obstacles[3].Order);
        Assert.Equal("a", level.Obstacles[4].LockedBy);
        var zone = Assert.Single(level.SpeedLimits);
        Assert.Equal(550.0, zone.S);
        Assert.Equal(90f, zone.MaxSpeed);
    }

    [Fact]
    public void Loader_RejectsAnOrderWithoutAGroup()
    {
        var e = Assert.Throws<LevelFormatException>(() => LevelLoader.Parse("""
            {
              "sections": { "tube": { "radius": 6 } },
              "start": "tube",
              "track": [ { "length": 300, "obstacles": [ { "at": 100, "order": 2 } ] } ]
            }
            """));

        Assert.Contains("group", e.Message);
    }

    private static TrackPosition Start => new(0, Surface.Floor, 0f);

    private static Track Track()
    {
        var track = new Track(Tube, startSpeed: 50f);
        track.Append(new TrackPiece(2000f, Tube));
        return track;
    }

    private static GameSession Game(Obstacle[] obstacles) =>
        new(Track(), obstacles, Settings, Start);

    /// <summary>
    /// Flies to <paramref name="to"/>, or stops early if the run ends. Stepping a finished session
    /// returns without moving the ship, so a bare "while S &lt; x" loop spins forever the moment a
    /// test costs its last shield - which is exactly what these tests are built to do.
    /// <paramref name="step"/> runs after each step, for gathering events before they are cleared.
    /// </summary>
    private static void Fly(GameSession game, double to, ShipInput input = default, Action<GameSession>? step = null)
    {
        // Enough frames to cross the whole test track; a bound, not a duration.
        for (int i = 0; i < 20_000 && game.Ship.Position.S < to && game.State == SessionState.Playing; i++)
        {
            game.Step(1f / 60f, input);
            step?.Invoke(game);
        }
    }
}
