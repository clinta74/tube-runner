# Level Plan

How the game teaches itself, and what each level is *for*. One idea per level, introduced on its own
before it gets mixed with anything else. Levels are in `game/levels/`; the format is in
[level-format.md](level-format.md).

## Rules of thumb

- **Round tubes first.** Flying around the wall is the core skill; teach it with nothing else going on.
- **Squarish sections come late.** Flat floors change how the ship reads the wall, so they belong after
  the round tube is second nature.
- **Introduce one thing at a time**, in a safe stretch (straight, no other obstacles), then use it under
  pressure a few hundred units later.
- **Every mechanic gets a free look first:** see it, then use it, then be tested on it.
- **Keep early levels short** (20–30 s). Later ones can run 40–60 s.
- Speed climbs across the game: 70 at the start, 160+ by the end. Within a level, ramp up, never down,
  except to breathe after a hard stretch.

## Progression

| # | Level | Teaches | Sections | Built |
|---|---|---|---|---|
| 1 | **First Loop** | Steering around the tube; blocks and targets | Round only | ✅ |
| 2 | **Power Up** | Breakable blocks, all five power-ups, the ring gun | Round, one oval | ✅ |
| 3 | **Throttle** | Speed control: long clear runs to push, tight gauntlets to back off | Round, ovals | ✅ |
| 4 | **Ovals** | Reading a changing wall; obstacles that only fit one way around | Wide and tall ovals | ✅ |
| 5 | **Flatlands** | Flat planes: strafing, the jump between floor and ceiling | Round → flat | ✅ |
| 6 | **Over and Under** | Flat sections under pressure: walls that force jump after jump | Flat, round | ✅ |
| 7 | **Earthworks** | Boxy sections, a long flat run, a fork | Box, flat, split | ✅ |
| 8 | **Crossroads** | Forks as a real choice: short-and-mean vs long-and-clear | Split-heavy | ✅ |
| 9 | **Neon Run** | Everything, faster | All | ✅ |
| 10 | **Clockwork** | Timed gates: obstacles that open and shut on a cycle | Round, oval | ✅ |
| 11 | **Metronome** | Gates in rhythm with jumps | Flat, round | ✅ |
| 12 | **Firing Order** | Target groups that must be shot in order | Round, oval | ✅ |
| 13 | **Gauntlet** | Block A's test: gates and order together, dense | All, incl. flat and box | ✅ |
| 14 | **Speed Trap** | Speed-limit zones: stretches that cost a shield if taken fast | Round, narrow | ✅ |
| 15 | **Restraint** | Limits right after the straights that invite speed | Round, oval | ✅ |
| 16 | **Shoal** | Movers: blocks that slide around the tube as you close | Round, wide | ✅ |
| 17 | **Undertow** | Block B's test: limits, gates and movers at once | All | ✅ |
| 18 | **Scarlands** | Hazard plates: wall you cannot break, shoot or ram | Box, flat, wide | ✅ |
| 19 | **Crossfire** | Plates and movers with a shooting load that won't wait | Round, box | ✅ |
| 20 | **Keys** | Shoot-to-open gates: a target that unlocks the way ahead | Round, box | ✅ |
| 21 | **Attrition** | Block C's test, on thin resources | All | ✅ |
| 22 | **Roulette** | Forks where one branch hides a warp well | Split-heavy | ⬜ |
| 23 | **Highwire** | Narrow tubes at speed, warps sitting on the fast line | Round, split | ⬜ |
| 24 | **Blackout** | Visibility: twists that show you almost nothing | All | ⬜ |
| 25 | **Long Odds** | Endurance: the longest level, and almost nothing given | All | ⬜ |
| 26 | **Finale** | No new ideas, just the hardest mix | All | ⬜ |

File names match these numbers. Level 9 ends the run until level 10 lands.

## Levels 10-26: blocks of four

Everything is taught by level 9, so the back half needs new ideas or it becomes one level played
seventeen times. Seven arrive across 10-25, and 26 introduces nothing at all.

They are grouped in **blocks of four**, and the two curves are deliberately out of phase:

- **A block opens with a refill and a new mechanic.** Levels 10, 14, 18 and 22 each hand shields back
  and then teach something, so a new idea is always learned with resources in hand.
- **The rest of the block tests it while resources thin out.** By the fourth level of a block the
  player is running on what they saved.

That phasing is the whole point. Pickups getting rarer and mechanics arriving would otherwise stack,
and learning a new hazard on a last shield is how a run dies to unfairness rather than difficulty.
Each block's opening refill is also weaker than the last: full shields at 10, full at 14, a pair of
shields at 18, a single one at 22.

| Block | Levels | Opens with | New ideas |
|---|---|---|---|
| A | 10-13 | Full shields | Timed gates (10), ordered target groups (12) |
| B | 14-17 | Full shields | Speed-limit zones (14), around-the-tube movers (16) |
| C | 18-21 | Two shields | Hazard plates (18), shoot-to-open gates (20) |
| D | 22-25 | One shield | Warps down one fork only (22) |
| - | 26 | Nothing | None. The finale is a test, not a lesson. |

**What each new mechanic is for**

Everything here has to work *with* the throttle, not against it. The player already picks their own
speed, from 0.5x to 1.75x of the track's, and holds it until they change it. That rules out a whole
family of ideas that look obvious on paper: a boost pad is a throttle the player did not press, and a
slow field is one they will simply press through. Neither is a decision. The mechanics below either
ignore speed or make the player *spend* it.

- **Timed gates** — obstacles solid for half a cycle and gone for the other half, turning a dodge into
  a question of *when*. The best of the set for this game, because the throttle is the answer: hurry
  or hold back to meet the gate open.
- **Ordered target groups** — a cluster that only breaks in sequence, so shooting becomes aiming
  rather than holding the trigger. The ring gun has to be gated out or it trivially skips the puzzle.
- **Speed-limit zones** — a stretch that costs a shield if it is taken above a set speed. The exact
  inverse of a boost: it makes the player give speed up, which on a timed run genuinely hurts. The one
  mechanic that turns the throttle into a liability.
- **Movers** — blocks that slide around the tube while you close on them, so the gap you aimed at is
  not the gap you arrive at. Around the tube only; along the track breaks the collision sweep's sort.
- **Hazard plates** — wall that costs a shield and cannot be shot, broken or rammed. Every other
  obstacle has an out; this one only has avoidance, so unstoppable stops being a universal answer.
- **Shoot-to-open gates** — a target that unlocks a gate further down the track. Shooting buys
  passage instead of points, and missing it means arriving at a wall.
- **Warps down one fork** — needs no new code. A fork where the quick branch might also throw you
  back up the track turns a route choice into a gamble.

**Deliberately not built:** tubes that roll or twist, and walls that close as you approach. Both need
engine subsystems that do not exist — a roll channel through frame propagation, and chunk rebuilding
for animated geometry — and neither is worth holding seventeen levels for.

## A run, not a level

Levels play back to back with no pause. Shields, shield slots, rapid fire, ring charges, unstoppable
time, throttle and score all carry across, and each level's time is kept as a split; the end screen
lists them with the run's total. Losing every shield ends the *run*, so a level's difficulty is really
"how much does this cost me for the rest of the run" — a level that takes two shields off you is much
worse than it looks on its own.

That makes the placement of shields between levels part of the pacing: a hard level should hand some
back before the next one starts.

## Difficulty levers

What we actually turn to make a level harder. Most are level-file settings; a few are consequences of
how obstacles are arranged.

**Pace**

1. **Speed** — the track's `speed` and its ramps. Everything else gets harder as this rises, because
   every distance becomes less time.
2. **Reaction distance** — gap between obstacles measured in *seconds* at the current speed, not units.
   40 units is half a second at 80, a third of a second at 120.
3. **Visibility** — curves and climbs hide what's coming; a straight shows it early. A hard pattern
   right after a bend is much harder than the same pattern on a straight. A run of tight alternating
   bends (a **twist**) keeps the reveal short the whole way through, and is one of the cheapest ways to
   make late levels bite without adding a single obstacle.
4. **Throttle pressure** — long clear stretches invite speed, so the gauntlet right after one is
   effectively faster unless the player reads it and eases off.

**Space**

5. **Coverage** — how much of the way around the tube is blocked. A gap wider than the ship is a dodge;
   a *sealed* ring (gaps narrower than the ship) has to be shot or avoided some other way.
6. **Travel demand** — how far around the wall you must move between obstacles. Half-ring walls in
   alternation are the strongest version.
7. **Density** — obstacles per unit of track, and how many at once.
8. **Shape changes** — ovals, tall ovals and flats move the floor under the player; the more often the
   section changes, the more they have to re-read the wall.
9. **Vertical demand** (flat sections) — full-width walls force jumps; the spacing between them versus
   the jump distance (about `0.55 x speed`) sets how tight the rhythm is.

**Pressure**

10. **Shooting load** — breakable `hits`, sealed rings that must be shot, targets that cost a shield if
    hit. Rapid fire and the ring gun are the pressure valves.
11. **Resource economy** — how often shields and power-ups appear, and how far off the fast line they
    sit. Putting a shield on the ceiling costs time to reach.
12. **Cost of a mistake** — a hit already costs a shield, a slow-down and about 0.4 s. Against a best
    time that compounds, so a level that's a fair clear can still be a hard *time*.
13. **Length** — more chances to lose the last shield; endurance rather than spikes.
14. **Branch risk/reward** — a split's branches differ in *path length*: the quick branch cuts the
    corner of the turn and so covers less ground before the merge, which is why it saves time. Its
    price is navigation, not speed — it gets its own tighter cross-section and the obstacles, while
    the long way round is roomy and clear. The fork is then a real bet: time against shields. Put
    splits well before the finish so the choice still has to be paid for.

    A branch only shortens by cutting a corner, and the ground saved is roughly
    `offset x turn-in-radians` times about two thirds — the blend out and back costs distance the
    formula ignores. Earthworks (20° turn) and Neon Run (18° climb, with the fork in the plane of
    the climb, which saves nothing at all) measured 0.9% and 0.8%, and were re-cut to 6.3% and 8.9%
    by turning both pieces hard and forking across the bend. Crossroads runs 5.5–11.4%.

    The number a player actually feels is the **spread between routes**, not either against the
    centerline: Crossroads' best fork is 11.4% one way and −14.0% the other, so the two roads differ
    by a quarter. `SplitSavingTests` holds every shipped split to 4% on the quick branch and 10%
    between branches. Anything gentler is decoration.

15. **Warp zones** — a black spot in the tube that throws the ship back up the track if hit, costing
    time rather than a shield. Warning signs hang from the walls on the approach, so it reads as a
    hazard to be dodged rather than a gotcha. Score is left to refund itself; the cost that matters
    is the clock.

## How each level advances

| # | Level | Speed | Coverage | Travel | Shapes | Jumps | Shooting | Pickups | Length |
|---|---|---|---|---|---|---|---|---|---|
| 1 | First Loop | 70 flat | Gaps and half-rings | High: over the top and back | Round only | – | Optional | 1 shield | 1500 |
| 2 | Power Up | 80 flat | First **sealed** rings | Medium | Round, one oval | – | Required | All five | 1900 |
| 3 | Throttle | 85 → 120 | Sealed rings, spirals | Medium | Round, oval | – | Required | Shield, rapid, ring | 2300 |
| 4 | Ovals | 95 → 125 | Half-blocked by shape | High: sides, then floor/ceiling | Wide and tall ovals | – | Required | Shield, rapid, ring | 2100 |
| 5 | Flatlands | 90 → 100 | Wide gaps | Low: strafing | Round → flat | 2, unhurried | Light | Shield, full shields | 2100 |
| 6 | Over and Under | 105 → 130 | Full-width walls | Medium | Flat, round | 5, in rhythm | Breakable rows | Rapid, ring, shield | 2750 |
| 7 | Earthworks | 80 → 125 | Mixed | Medium | Box, flat, split | 2 | Mixed | Shields, rapid, ring, slot in a branch | 4650 |
| 8 | Crossroads | 95 → 138 | Per branch | High: the inside line | Chambers, tubes | – | Mixed | Shield, rapid, ring, unstoppable | 4350 |
| 9 | Neon Run | 100 → 160 | Mixed, dense | High | All | Several | Mixed | Shields, rapid, two rings, slot | 4920 |

Both late levels hand out shields *after* their hard stretches and put the tools *before* the obstacle
they answer. Their rarer prizes — the extra shield slot, and shields inside a split — sit off the fast
line or down the meaner branch, so they cost something to take.

## Where each mechanic is introduced

| Mechanic | First seen | Then tested by |
|---|---|---|
| Steering around the tube | 1, blocks on the floor | 1, half-ring walls |
| Shooting targets | 1, targets on the wall | 2, rings of targets |
| Breakable blocks (`hits`) | 2, a single 1-hit block | 2, a full ring to shoot through |
| Shield / full shields | 1 shield, 2 full | placed after hard stretches |
| Extra shield slot | 2, on the ceiling | going off-line to reach it |
| Rapid fire | 2, before tough blocks | 2, 4-hit blocks |
| Ring gun | 2, before a ring of targets | 2, second ring at an offset angle |
| Unstoppable | 6, before the breakable rows | 7 at the box, 9 before the twisting finish |
| Throttle | 3 | long straights and tight gauntlets |
| Flat planes, jumping | 5 | 6, full-width walls |
| Forks | 7 | 8, three junctions where the short way is the tight one |
| Warp zones | 8, in the run for home | 23, sitting on the tempting line |
| Timed gates | 10, a single gate on a straight | 11, gates in rhythm with jumps |
| Ordered target groups | 12, one group at a time | 13, groups mixed into a dense run |
| Speed-limit zones | 14, one limit after a straight | 15, limits where the track invites speed |
| Movers | 16, one block sliding on a straight | 17, movers with gates and limits |
| Hazard plates | 18, a plate you cannot shoot | 19, plates narrowing the only line |
| Shoot-to-open gates | 20, one target, one gate | 21, keys you must hit while dodging |
| Warps down one fork | 22, a fork where one way is a gamble | 25, when there is nothing left to lose |

## Placeholder art to replace later

The gameplay shapes are settled; these all need a real look:

- **Ship:** built from primitives (wedge fuselage, swept wings, tail fins, twin engines) with parts
  that move with throttle, bank and state. Wants a real model eventually, but it is no longer a
  placeholder shape.
- **Blocks:** plain boxes. Breakable ones are steel blue and just darken as they take hits; they want
  cracks or chunks flying off.
- **Targets:** four-sided diamonds.
- **Power-ups:** colored pads with a text label (`+1`, `MAX`, `SLOT`, `RAPID`, `RING`); they want icons.
- **Ring gun shot:** a plain glowing ring.
- **Bursts:** small spheres flying apart.
