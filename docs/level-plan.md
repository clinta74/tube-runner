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
| 8 | *Crossroads* | Forks as a real choice: safe-but-slow vs fast-but-mean branches | Split-heavy | ⬜ |
| 9 | **Neon Run** | Everything, faster | All | ✅ |
| 10 | *Finale* | A long run with no new ideas, just the hardest mix | All | ⬜ |

File names match these numbers, so `level_08.json` is missing until Crossroads exists and level 7
points straight at level 9. The same goes for a level 10.

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
14. **Branch risk/reward** — a split's branches can set their own `speed`, so one route runs faster.
    Load the quick one with obstacles and leave the slow one clear, and the fork becomes a real bet:
    time against shields. Put splits well before the finish so the choice still has to be paid for.

## How each level advances

| # | Level | Speed | Coverage | Travel | Shapes | Jumps | Shooting | Pickups | Length |
|---|---|---|---|---|---|---|---|---|---|
| 1 | First Loop | 70 flat | Gaps and half-rings | High: over the top and back | Round only | – | Optional | 1 shield | 1500 |
| 2 | Power Up | 80 flat | First **sealed** rings | Medium | Round, one oval | – | Required | All five | 1900 |
| 3 | Throttle | 85 → 120 | Sealed rings, spirals | Medium | Round, oval | – | Required | Shield, rapid, ring | 2300 |
| 4 | Ovals | 95 → 125 | Half-blocked by shape | High: sides, then floor/ceiling | Wide and tall ovals | – | Required | Shield, rapid, ring | 2100 |
| 5 | Flatlands | 90 → 100 | Wide gaps | Low: strafing | Round → flat | 2, unhurried | Light | Shield, full shields | 2100 |
| 6 | Over and Under | 105 → 130 | Full-width walls | Medium | Flat, round | 5, in rhythm | Breakable rows | Rapid, ring, shield | 2750 |
| 7 | Earthworks | 80 → 125 | Mixed | Medium | Box, flat, split | 2 | Mixed | Shields, rapid, ring, slot in a branch | 4200 |
| 9 | Neon Run | 100 → 160 | Mixed, dense | High | All | Several | Mixed | Shields, rapid, two rings, slot | 4320 |

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
| Throttle | 3 | long straights and tight gauntlets |
| Flat planes, jumping | 5 | 6, full-width walls |
| Forks | 7 | 8, branches with different rewards |

## Placeholder art to replace later

The gameplay shapes are settled; these all need a real look:

- **Ship:** a flat-shaded prism.
- **Blocks:** plain boxes. Breakable ones are steel blue and just darken as they take hits; they want
  cracks or chunks flying off.
- **Targets:** four-sided diamonds.
- **Power-ups:** colored pads with a text label (`+1`, `MAX`, `SLOT`, `RAPID`, `RING`); they want icons.
- **Ring gun shot:** a plain glowing ring.
- **Bursts:** small spheres flying apart.
