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
| 9 | **Loop the Loop** | A first look at the wireframe style: loops and switchbacks, the whole track in view | Round, wireframe | ✅ |
| 10 | **Sleeve** | A first look at the ring: a bore too wide to see all of, a core overhead, the jump between the two | Rings at 20, 30 and 40, boxy ring | ✅ |
| 11 | **Neon Run** | Everything, faster | All | ✅ |
| 12 | **Clockwork** | Timed gates: obstacles that open and shut on a cycle | Round, oval | ✅ |
| 13 | **Metronome** | Gates in rhythm with jumps | Flat, round | ✅ |
| 14 | **Firing Order** | Target groups that must be shot in order | Round, oval | ✅ |
| 15 | **Gauntlet** | Block A's test: gates and order together, dense | All, incl. flat and box | ✅ |
| 16 | **Speed Trap** | Speed-limit zones: stretches that cost a shield if taken fast | Round, narrow | ✅ |
| 17 | **Restraint** | Limits right after the straights that invite speed | Round, oval | ✅ |
| 18 | **Shoal** | Movers: blocks that slide around the tube as you close | Round, wide | ✅ |
| 19 | **Undertow** | Block B's test: limits, gates and movers at once | All | ✅ |
| 20 | **Scarlands** | Hazard plates: wall you cannot break, shoot or ram | Box, flat, wide | ✅ |
| 21 | **Crossfire** | Plates and movers with a shooting load that won't wait | Round, box | ✅ |
| 22 | **Keys** | Shoot-to-open gates: a target that unlocks the way ahead | Round, box | ✅ |
| 23 | **Attrition** | Block C's test, on thin resources | All | ✅ |
| 24 | **Tollgate** | Power-ups that have to be shot for: a pad is dead until its key falls | Round, wide, box | ✅ |
| 25 | **Ransom** | Locks with a bill: three-key doors, ordered keys, a pad behind a door | All | ✅ |
| 26 | **Iris** | Apertures: a ring of blades that irises open as its keys are shot | Round, wide, box | ✅ |
| 27 | **Shutter** | Block D's test: rings under gates, movers, plates and speed | All | ✅ |
| 28 | **Roulette** | Forks where one branch hides a warp well | Split-heavy | ✅ |
| 29 | **Highwire** | Narrow tubes at speed, warps on the fast line — drawn see-through | Round, split, wire | ✅ |
| 30 | **Blackout** | Visibility: twists that show you almost nothing | All | ✅ |
| 31 | **Long Odds** | Endurance: the longest level, and almost nothing given | All | ✅ |
| 32 | **Finale** | No new ideas, just the hardest mix | All | ✅ |

File names match these numbers, and the run goes all the way through to the finale. They are free to
be renumbered when a level is inserted, because a best time is saved against the level's `id` rather
than its file name; see [level-format.md](level-format.md).

## Levels 12-32: blocks of four

Everything the early game teaches is taught by level 11, so the back half needs new ideas or it
becomes one level played twenty times. Nine arrive across 12-31, and the finale introduces nothing
at all.

They are grouped in **blocks of four**, and the two curves are deliberately out of phase:

- **A block opens with a refill and a new mechanic.** Levels 12, 16, 20, 24 and 28 each hand shields
  back and then teach something, so a new idea is always learned with resources in hand.
- **The rest of the block tests it while resources thin out.** By the fourth level of a block the
  player is running on what they saved.

That phasing is the whole point. Pickups getting rarer and mechanics arriving would otherwise stack,
and learning a new hazard on a last shield is how a run dies to unfairness rather than difficulty.
Each block's opening refill is also weaker than the last: full shields at 12, full at 16, a pair of
shields at 20, a pair at 24 of which one has to be shot for, and a single one at 28.

| Block | Levels | Opens with | New ideas |
|---|---|---|---|
| A | 12-15 | Full shields | Timed gates (12), ordered target groups (14) |
| B | 16-19 | Full shields | Speed-limit zones (16), around-the-tube movers (18) |
| C | 20-23 | Two shields | Hazard plates (20), shoot-to-open gates (22) |
| D | 24-27 | A shield, and one behind a key | Locked power-ups (24), apertures (26) |
| E | 28-31 | One shield | Warps down one fork (28) |
| - | 32 | Nothing | None. The finale is a test, not a lesson. |

**What each new mechanic is for**

Everything here has to work *with* the throttle, not against it. The player already picks their own
speed, from 0.35x to 1.5x of the track's, and holds it until they change it. That rules out a whole
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
- **Power-ups that have to be shot for** — a pad with a `lockedBy` is dead until its key group is
  destroyed. The gentlest lock in the game: missing the key costs the prize, not a shield, which is
  what makes it the right way to introduce the idea that shooting buys things. It also gives the
  resource economy a dial it never had — a shield can be *offered* without being *given*.
- **Apertures** — a ring of blades sealing the tube that irises open as its keys fall, one key's
  worth at a time. The first lock that opens by degrees: a three-key ring is a tube with one side
  clear and the other walled, and which side that is depends on which shot landed. Put one in each
  mouth of a fork and the keys decide which roads are open before the fork is reached.

**Deliberately not built:** tubes that roll or twist, and walls that close as you approach. Both need
engine subsystems that do not exist — a roll channel through frame propagation, and chunk rebuilding
for animated geometry — and neither is worth holding seventeen levels for.

**Keep the old mechanics running underneath the new ones.** The first cut of the back half
scheduled a new idea into every block and quietly let forks and wells lapse: they appeared at 7–11,
vanished for twelve levels, and came back at 28. That is long enough for a player to forget the game has
branching at all, and it dumped the whole reintroduction on one block, making its opener far denser
than the others. A fork now lands every third level from 12, and a well every third from 13, offset
so they alternate rather than stack. **Nothing already taught should go more than three or
four levels unused** — a mechanic the game stops using reads as a mechanic the game abandoned.

## A run, not a level

Levels play back to back with no pause. Shields, extra shields, rapid fire, ring charges, unstoppable
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
| 1 | First Loop | 70 flat | Gaps and half-rings | High: over the top and back | Round only | – | Optional | 1 shield, rapid fire | 1646 |
| 2 | Power Up | 80 flat | First **sealed** rings | Medium | Round, one oval | – | Required | All five | 2046 |
| 3 | Throttle | 85 → 120 | Sealed rings, spirals | Medium | Round, oval | – | Required | Shield, rapid, ring | 2496 |
| 4 | Ovals | 95 → 125 | Half-blocked by shape | High: sides, then floor/ceiling | Wide and tall ovals | – | Required | Shield, rapid, ring | 2376 |
| 5 | Flatlands | 90 → 100 | Wide gaps | Low: strafing | Round → flat | 2, unhurried | Light | Shield, full shields | 2296 |
| 6 | Over and Under | 105 → 130 | Full-width walls | Medium | Flat, round | 5, in rhythm | Breakable rows | Rapid, ring, shield | 3146 |
| 7 | Earthworks | 80 → 125 | Mixed | Medium | Box, flat, split | 2 | Mixed | Shields, rapid, ring, slot in a branch | 4926 |
| 8 | Crossroads | 95 → 138 | Per branch | High: the inside line | Chambers, tubes | – | Mixed | Shield, rapid, ring, unstoppable | 4496 |
| 9 | Loop the Loop | 110 flat | Light, spaced, a block in most loops and hairpins | Low: the track does the turning | Round, wireframe | – | Mixed | Two shields, rapid, ring | 4556 |
| 10 | Sleeve | 105 → 118 | Collars: the whole way round a wall | High: the jump between walls, both ways | Rings at 20, 30 and 40 with 12 of room, boxy ring | Compulsory, in pairs | Light | Shields, ring gun in fours | 6186 |
| 11 | Neon Run | 100 → 160 | Mixed, dense | High | All | Several | Mixed | Shields, rapid, two rings, slot | 5116 |

Both late levels hand out shields *after* their hard stretches and put the tools *before* the obstacle
they answer. Their rarer prizes — the extra shield, and shields inside a split — sit off the fast
line or down the meaner branch, so they cost something to take.

## Where each mechanic is introduced

| Mechanic | First seen | Then tested by |
|---|---|---|
| Steering around the tube | 1, blocks on the floor | 1, half-ring walls |
| Shooting targets | 1, targets on the wall | 2, rings of targets |
| Breakable blocks (`hits`) | 2, a single 1-hit block | 2, a full ring to shoot through |
| Shield / full shields | 1 shield, 2 full | placed after hard stretches |
| Extra shield | 2, on the ceiling | going off-line to reach it; it is spent first and never refilled; 28, 29 and 31 down the quick branch past its well |
| Rapid fire | 2, before tough blocks | 2, 4-hit blocks |
| Ring gun | 2, before a ring of targets | 2, second ring at an offset angle |
| Unstoppable | 6, before the breakable rows | 7 at the box, 11 before the twisting finish; 26, 27 and 31 after a three-key ring's keys, as the answer to the ones that were missed; 27, against a ring no shot opens |
| Agility | 10, in fours round the wide rings, where a wall is too big to get across | 16, 17, 18, 19, 20, 21, 23 and 28 just ahead of each level's quickest stretch; 29, 30 and 31 before their runs for home, where the blocks come faster than the ship can leave |
| Throttle | 3 | long straights and tight gauntlets |
| Flat planes, jumping | 5 | 6, full-width walls |
| Rings, and the jump between their walls | 10, a wide bore with a core overhead and a shield on it | 10, collars that force the jump both ways, then the biggest bore at speed |
| Forks | 7 | 8, three junctions where the short way is the tight one; then every third level from 12 |
| Warp zones | 8, in the run for home | every third level from 13, and all through block E |
| Timed gates | 12, a single gate on a straight | 13, gates in rhythm with jumps |
| Ordered target groups | 14, one group at a time | 15, groups mixed into a dense run |
| Speed-limit zones | 16, one limit after a straight | 17, limits where the track invites speed |
| Movers | 18, one block sliding on a straight | 19, movers with gates and limits |
| Hazard plates | 20, a plate you cannot shoot | 21, plates narrowing the only line |
| Shoot-to-open gates | 22, one target, one gate | 23, keys you must hit while dodging |
| Locked power-ups | 24, one key, one dead pad on a straight | 25, pads behind doors and wells; then the branch prize at 28, the dark at 30, the last prize at 31, and the finale's one offer |
| Apertures | 26, a ring with a gap you can fly through | 27, rings under gates, movers, plates and speed; then see-through at 29, in the dark at 30, divided at 31, and divided again into the finale's fork |
| Warps down one fork | 28, a fork where one way is a gamble | 31, when there is nothing left to lose |

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
