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
| 3 | *Throttle* | Speed control: long clear runs to push, tight gauntlets to back off | Round, ovals | ⬜ |
| 4 | *Ovals* | Reading a changing wall; obstacles that only fit one way around | Ovals, pinches | ⬜ |
| 5 | *Flatlands* | Flat planes: strafing, the jump between floor and ceiling | Round → flat | ⬜ |
| 6 | *Over and Under* | Flat sections under pressure: full-width walls, ceiling runs | Flat, round | ⬜ |
| 7 | **Earthworks** (current level 3) | Boxy sections, a long flat run, a fork | Box, flat, split | ✅ |
| 8 | *Crossroads* | Forks as a real choice: safe-but-slow vs fast-but-mean branches | Split-heavy | ⬜ |
| 9 | **Neon Run** (current level 4) | Everything, faster | All | ✅ |
| 10 | *Finale* | A long run with no new ideas, just the hardest mix | All | ⬜ |

Levels 3–6 and 8 are gaps to fill; the built ones are chained 1 → 2 → 3 (Earthworks) → 4 (Neon Run)
until they land in their final order.

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
