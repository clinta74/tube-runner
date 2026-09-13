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

        Assert.True(gate.IsSolidAt(0.3f));
        Assert.True(gate.IsSolidAt(0.7f));
        Assert.False(gate.IsSolidAt(1.3f));
        Assert.False(gate.IsSolidAt(1.7f));
        Assert.True(gate.IsSolidAt(2.3f));   // round again

        // Mid-slide it cannot hurt anything, at either end of the cycle. A gate on its way out does
        // not look like a wall yet, and one on its way in has stopped looking like one.
        Assert.False(gate.IsSolidAt(0f));
        Assert.False(gate.IsSolidAt(1f));
    }

    [Fact]
    public void Gate_PhaseStaggersTheCycle_SoGatesCanFormARhythm()
    {
        var early = new Obstacle { Kind = ObstacleKind.Block, S = 400, Period = 2f };
        var late = new Obstacle { Kind = ObstacleKind.Block, S = 400, Period = 2f, Phase = 0.5f };

        // Sampled clear of the slides, where one is fully out and the other fully withdrawn.
        Assert.True(early.IsSolidAt(0.3f));
        Assert.False(late.IsSolidAt(0.3f));
    }

    [Fact]
    public void Gate_SlidesRatherThanPopping()
    {
        var gate = new Obstacle { Kind = ObstacleKind.Block, S = 400, Period = 4f };

        // Fully out through the middle of the solid half, fully in through the middle of the open
        // half, and part way at each boundary - so there is something for the eye to follow.
        Assert.Equal(1f, gate.ExtensionAt(1f), precision: 2);
        Assert.Equal(0f, gate.ExtensionAt(3f), precision: 2);
        Assert.Equal(0.5f, gate.ExtensionAt(2f), precision: 2);
        Assert.Equal(0.5f, gate.ExtensionAt(0f), precision: 2);

        // It leaves and returns smoothly, never jumping.
        Assert.InRange(gate.ExtensionAt(2.2f), 0.01f, 0.5f);
        Assert.InRange(gate.ExtensionAt(1.8f), 0.5f, 0.99f);
    }

    [Fact]
    public void Obstacle_WithoutAPeriod_StandsFullyOut()
    {
        Assert.Equal(1f, new Obstacle { Kind = ObstacleKind.Block, S = 400 }.ExtensionAt(7.3f));
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
    public void RingGun_TakesOneMemberOfAnOrderedGroup_NotTheWholeThing()
    {
        var second = new Obstacle { Kind = ObstacleKind.Target, S = 300, Width = 40f, Group = "gate", Order = 1 };
        var first = new Obstacle { Kind = ObstacleKind.Target, S = 300, Width = 40f, Group = "gate", Order = 0 };
        var pickup = new Pickup { Kind = PickupKind.RingGun, S = 100 };
        var game = new GameSession(Track(), [second, first], Settings, Start, [pickup]);

        // Collect the charge, then fire exactly one ring - holding the button would spend all three
        // and take the group a member at a time, which is the sweep working rather than failing.
        Fly(game, to: 150);
        game.Step(1f / 60f, new ShipInput(Special: true));
        Fly(game, to: 280);

        // The one whose turn it is goes; the rest of the group is left for the next shot. A ring
        // that skipped the group entirely would pass through a target that was ready to break,
        // which just reads as the gun not working.
        Assert.True(first.Destroyed, "the ring should take the one whose turn it is");
        Assert.False(second.Destroyed, "one ring should not clear a whole group");
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
    public void ThrustZone_RaisesTheFloorAndDragsTheThrottleUpToIt()
    {
        var zone = new ThrustZone { S = 400, Length = 200f, MinThrottle = 1.3f };
        var game = new GameSession(Track(), [], Settings, Start, null, null, null, [zone]);

        // Held all the way down on the way in, so the throttle is at the ship's own floor.
        Fly(game, to: 250, new ShipInput(Throttle: -1f));
        Assert.Equal(Settings.Ship.MinThrottle, game.Ship.Throttle, precision: 2);

        // Inside, the floor rises and the ship is carried up to it whether it likes it or not.
        Fly(game, to: 420, new ShipInput(Throttle: -1f));
        Assert.True(zone.Entered);
        Assert.Equal(1.3f, game.Ship.ThrottleFloor, precision: 2);
        Assert.Equal(1.3f, game.Ship.Throttle, precision: 2);
        Assert.Equal(3, game.Shields);   // it costs control, never a shield
    }

    [Fact]
    public void ThrustZone_LowersTheCeilingAndHoldsTheThrottleDown()
    {
        var zone = new ThrustZone { S = 400, Length = 200f, MaxThrottle = 0.8f };
        var game = new GameSession(Track(), [], Settings, Start, null, null, null, [zone]);

        // Wide open the whole way, so the throttle is pinned at the ship's ceiling before the zone.
        Fly(game, to: 250, new ShipInput(Throttle: 1f));
        Assert.True(game.Ship.Throttle > 1f);

        Fly(game, to: 420, new ShipInput(Throttle: 1f));
        Assert.Equal(0.8f, game.Ship.ThrottleCeiling, precision: 2);
        Assert.Equal(0.8f, game.Ship.Throttle, precision: 2);
        Assert.Equal(3, game.Shields);
    }

    [Fact]
    public void ThrustZone_GivesTheRangeBackOnTheWayOut()
    {
        var zone = new ThrustZone { S = 300, Length = 120f, MinThrottle = 1.4f };
        var game = new GameSession(Track(), [], Settings, Start, null, null, null, [zone]);

        Fly(game, to: 320);
        Assert.NotNull(game.InThrustZone);

        Fly(game, to: 600, new ShipInput(Throttle: -1f));

        Assert.Null(game.InThrustZone);
        Assert.Equal(Settings.Ship.MinThrottle, game.Ship.ThrottleFloor, precision: 2);
        Assert.True(game.Ship.Throttle < 1.4f, "the throttle should be free to come down again");
    }

    [Fact]
    public void Jump_IsOnlyOfferedWhereItActuallyWorks()
    {
        var flat = new CrossSection(6f, 6f, 0f, 1f);
        var track = new Track(Tube, startSpeed: 50f);
        track.Append(new TrackPiece(300f, Tube));    // closed tube
        track.Append(new TrackPiece(300f, flat));    // opening out
        track.Append(new TrackPiece(600f, flat));    // fully flat
        var ship = new ShipSim(Settings.Ship, track, new TrackPosition(0, Surface.Floor, 0f));

        Assert.False(ship.CanJump);   // nowhere to jump to inside a tube

        // Part way through opening out it is still refused, which is the stretch the HUD cue exists
        // for: the section looks flat enough to try from, and the button does nothing.
        while (ship.Position.S < 420) ship.Step(1f / 60f, steer: 0f);
        Assert.False(ship.CanJump);

        while (ship.Position.S < 900) ship.Step(1f / 60f, steer: 0f);
        Assert.True(ship.CanJump);

        // And not while one is already in the air.
        ship.Step(1f / 60f, steer: 0f, jump: true);
        Assert.True(ship.IsJumping);
        Assert.False(ship.CanJump);
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
                  "thrustZones": [ { "at": 550, "length": 80, "min": 1.2, "max": 1.6 } ]
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
        var zone = Assert.Single(level.ThrustZones);
        Assert.Equal(550.0, zone.S);
        Assert.Equal(1.2f, zone.MinThrottle);
        Assert.Equal(1.6f, zone.MaxThrottle);
    }

    [Fact]
    public void Loader_RejectsAThrustZoneThatChangesNothing()
    {
        var e = Assert.Throws<LevelFormatException>(() => LevelLoader.Parse("""
            {
              "sections": { "tube": { "radius": 6 } },
              "start": "tube",
              "track": [ { "length": 300, "thrustZones": [ { "at": 100 } ] } ]
            }
            """));

        Assert.Contains("does nothing", e.Message);
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
