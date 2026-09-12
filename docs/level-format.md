# Level Format

Levels are JSON files in `game/levels/`. Comments (`//`) and trailing commas are allowed. A test
(`LevelLoaderTests.ShippedLevels_LoadAndLinkUp`) loads every file there, so a broken level fails the build's tests.

Units are world units; the default tube radius is 6 and the default speed is 80 units per second.

## Top level

| Field | Default | Meaning |
|---|---|---|
| `name` | `"Untitled"` | Shown when the level starts. |
| `next` | none | File name of the following level. |
| `speed` | `80` | Starting forward speed, units per second. Track pieces can change it. |
| `segmentLength` | `60` | Distance between wall seams. The checker pattern can change at each seam. |
| `theme` | Earth theme | Colors; see below. |
| `sections` | required | Named cross-sections used by the track. |
| `start` | required | Section name the track starts with. |
| `track` | required | Ordered list of track pieces. |
| `obstacles` | none | Obstacles placed from the start of the track. Usually better inside track pieces; see below. |

## Sections

A section is a cross-section shape. Every shape blends smoothly into every other.

| Field | Meaning |
|---|---|
| `radius` | Shorthand for a circle (sets both half sizes). |
| `halfWidth`, `halfHeight` | Semi-axes. Open planes sit at ±`halfHeight`. |
| `squareness` | 0 = ellipse, 1 = rounded rectangle with flat floor and ceiling. |
| `opening` | 0 = closed tube, 1 = open flat planes (see below). |

```json
"sections": {
  "tube": { "radius": 6 },
  "oval": { "halfWidth": 9, "halfHeight": 4.5 },
  "box":  { "halfWidth": 10, "halfHeight": 4.2, "squareness": 1 },
  "flat": { "radius": 6, "opening": 1 }
}
```

### Flat sections

Blending into a section with `opening: 1` unrolls the tube: its lower half flattens into a floor and
its upper half into a ceiling, then both spread sideways to the horizon. On flat sections the player
strafes left and right (the view stays level) and can jump between floor and ceiling.

Both ends of a flat section go through a **funnel**, so the void beyond the planes is never visible.
Opening out, the tube stays closed while it widens to 420 units — well past the distance fade — and only
then do the floor and ceiling part company, out of sight. Rejoining reverses that: the planes seal into
a funnel 420 units out, which then narrows to the tube, quickly while its walls are far away and easing
off as they close in. Through the rejoin the player stays on the floor or ceiling and is eased toward
the center. Give each of those pieces room: 300 units or more feels smooth.

Keep a theme's `fadeEnd` below 420 so the funnel's walls stay out of sight while they open and seal.

**Pieces that start or end on an open section must be straight** (no `turn` or `climb`).

## Track pieces

Each piece continues from the end of the previous one.

| Field | Default | Meaning |
|---|---|---|
| `length` | required | Length of the piece. |
| `section` | previous | Section to blend into over the piece. Omit to keep the current shape. |
| `turn` | `0` | Total heading change over the piece, in degrees; positive turns right. |
| `climb` | `0` | Total pitch change over the piece, in degrees; positive climbs. |
| `obstacles` | none | Obstacles on this piece, placed from its start (see Obstacles). |
| `pickups` | none | Power-ups on this piece, placed the same way (see Power-ups). |
| `speed` | previous | Speed to reach by the end of the piece (units per second), blended smoothly. The player's throttle multiplies it (0.5× to 1.75×). Screen effects, field of view, and engine sound follow the result. |

```json
"track": [
  { "length": 150 },                                  // straight
  { "length": 160, "turn": 40 },                      // gentle right
  { "length": 80,  "section": "oval", "turn": 15 },   // morph to an oval while turning
  { "length": 100, "section": "flat" },               // unroll into flat planes (straight)
  { "length": 400 },                                  // flat run
  { "length": 120, "section": "tube" }                // roll back into the tube (straight)
]
```

Keep curves gentle: a turn tighter than about 1° per unit length will fold the tube wall.

Tip: annotate pieces with their start and end distance (`// 930 - 1030`); `play.ps1 -Start 930` then
jumps straight to that piece for testing.

## Splits (forks)

A piece with a `split` forks the tube into branches that run side by side (or above and below each
other) and merge again at the end of the piece. At the fork a wall closes off the chamber, with an
opening for each branch; the player takes whichever branch they're in front of, so they choose by
steering toward an opening.

```json
{ "length": 100, "section": "chamber" },          // widen into a chamber first
{ "length": 400, "turn": 20, "split": {
    "section": "tube",                             // cross-section of each branch
    "branches": [
      { "offsets": [[0, -7.5, 0], [120, -22, 0], [280, -22, 0], [400, -7.5, 0]] },
      { "offsets": [[0,  7.5, 0], [120,  22, 5], [280,  22, 5], [400,  7.5, 0]] }
    ]
} },
{ "length": 100, "section": "tube" }              // narrow back down after
```

- The split piece keeps the current section (the **chamber**), so don't give it a `section`. It can
  `turn`, `climb`, and change `speed`.
- Each branch lists `[along, x, y]` offsets from the track's center: `along` runs from 0 to the piece
  length, `x` is right, `y` is up. The branch blends smoothly between them.
- A branch can set its own `"section"`, overriding the split's. That is how the quick way round can
  also be the tight one.
- Branches have **no** speed setting. The ship flies at the same speed through space whichever way it
  goes; what differs is how far it travels. A branch running round the inside of a turn covers less
  ground and reaches the merge sooner, and the engine measures each branch's real path to work that
  out. Bulging sideways on a *straight* buys almost nothing — under 1% over a 400-unit piece — so a
  split has to genuinely **turn** before a shortcut is worth taking.

  The ground saved is roughly `offset x turn-in-radians`. That makes small bends worthless: 22 units
  of offset through a 20° bend saves about 10 units, which at 100 u/s is a tenth of a second and no
  player will notice it. A fork worth betting on needs both levers pushed — 40 units of offset
  through a 90° bend saves about 60 units, or half a second. Price the short way with a tighter
  `section` and more obstacles, and the choice becomes real: time against shields.
- At the fork and the merge, every branch's opening must fit inside the chamber without overlapping
  another; the level won't load otherwise, and the error says which.
- 2 to 4 branches. The chamber and branches must be closed tubes.
- Obstacles inside a split need a `branch`.

## Obstacles

A **block** is solid: hitting one breaks it, costs a shield, and slows the ship briefly. It stops shots
unless it's breakable — give it `"hits": N` and N shots break it (the ring gun breaks it in one).
A **target** is destroyed by shots for 100 points, and hurts like a block if you fly into it.
Breaking a block scores 50 per hit it takes, so a `"hits": 3` block pays 150.
The ship has three shields; losing them all ends the run.

Put obstacles in the `obstacles` list of the track piece they belong to. Their `at` is measured from the
start of that piece, so lengthening, shortening, or reordering pieces carries their obstacles along.
Repeats may run past the end of the piece. A top-level `obstacles` list is also allowed; there `at` is
measured from the start of the track.

```json
{ "length": 400, "obstacles": [
  { "at": 50, "x": -6, "width": 8 },                 // 50 units into this piece
  { "at": 220, "width": 80, "height": 2.5 }
] }
```

| Field | Default | Meaning |
|---|---|---|
| `at` | required | Distance of the obstacle's center from the start of its piece (or of the track, for top-level obstacles). |
| `kind` | `"block"` | `"block"` or `"target"`. |
| `surface` | `"floor"` | `"floor"` or `"ceiling"`. |
| `branch` | none | Inside a split, which branch (0, 1, ...). Required there, not allowed elsewhere. |
| `x` | `0` | Distance across the surface from its center; positive is right. |
| `angle` | none | **Tubes only**, instead of `surface`/`x`: degrees around the tube from the floor center. 90 = right wall, 180 = ceiling, -90 = left wall. |
| `width`, `length`, `height` | `3`, `2`, `2` | Size across the surface, along the track, and off the surface. |
| `hits` | `0` | Blocks only: shots needed to break it. 0 means shots can't. |
| `count` | `1` | Place several, each one shifted by the steps below. |
| `spacing` | `0` | Distance along the track between repeats. |
| `xStep` | `0` | Change in `x` per repeat. |
| `angleStep` | `0` | Change in `angle` per repeat. |

```json
"obstacles": [
  { "at": 70 },                                                          // block, floor center
  { "at": 170, "kind": "target", "angle": 90 },                          // target on the right wall
  { "at": 50, "angle": 0, "count": 4, "spacing": 45, "angleStep": 90 },  // spiral of blocks
  { "at": 220, "width": 80, "height": 2.5 },                             // full-width wall on a flat section: jump over it
  { "at": 290, "kind": "target", "surface": "ceiling", "x": -10 }
]
```

On flat sections the ship can strafe 40 units either side of center, so a block 80 wide spans
the whole floor. The ship jumps between floor and ceiling in about 0.55 s, covering
`0.55 × speed` units, so leave that much room before a full-width wall.

## Power-ups

Power-ups sit in a `pickups` list, on a piece or at the top level, and are placed exactly like
obstacles (`at`, `surface`/`x` or `angle`, `branch`, and the repeat fields). Flying over one collects it.

| `kind` | Effect |
|---|---|
| `shield` | Restores one shield. |
| `full-shields` | Restores every shield. |
| `shield-slot` | Adds a shield slot, already filled, up to 6. |
| `rapid-fire` | Fires about three times as fast for 8 seconds. |
| `ring-gun` | 3 ring-gun shots. Each sweeps the whole tube, breaking every target and breakable block it passes; solid blocks survive. |
| `unstoppable` | For 6 seconds the ship smashes through anything it touches — solid blocks included — scoring each one and losing no shields. |

```json
{ "length": 250, "pickups": [
  { "at": 20, "kind": "ring-gun" },
  { "at": 80, "kind": "shield-slot", "angle": 180 }   // up on the ceiling
] }
```

Put a power-up in a calm stretch the first time a level uses it, then follow it with the obstacle it
answers: a ring of targets after the ring gun, tough blocks after rapid fire.

## Warp zones

A `warps` list, on a piece or at the top level, puts a mouth in the tube wall: a side tube at right
angles to the track, opening into black. Fly into one and it throws the ship back up the track. It
costs **time, never a shield**, and each fires once, so repeatedly clipping the same mouth cannot
trap a run.

Three warning signs are placed automatically at 55, 110 and 170 units back up the track from the
mouth, so the hazard is always telegraphed. Nothing needs declaring for them.

| Field | Default | Meaning |
|---|---|---|
| `at` | required | Distance of the mouth's center from the start of its piece (or of the track). |
| `surface`, `x`, `angle`, `branch` | as obstacles | Where on the wall it sits; placed exactly like an obstacle. |
| `width` | `7` | Opening across the surface. |
| `length` | `18` | Opening along the track. Long by default, see below. |
| `back` | `0` | How far it throws the ship back. 0 uses the game's default of 250. |
| `count`, `spacing`, `xStep`, `angleStep` | | Repeats, as for obstacles. |

```json
{ "length": 400, "warps": [
  { "at": 150, "angle": 180 },              // a mouth in the ceiling
  { "at": 300, "x": -8, "back": 400 }       // wider of the mark, and a longer way back
] }
```

**Put a warp on a surface that curves away from the player's line.** This matters more than any
other choice about it. On the surface the ship is riding, the camera sits about 2 units up, so a
mouth ahead is seen at roughly 4 degrees and smears into the floor right under the nose — you get
almost no warning. Put the same mouth on the ceiling, or high on the far side of the tube, and the
curve of the wall turns that patch back towards the ship: it stands out as a clear shape from a
long way out and only goes edge-on once you are on top of it, which is exactly the right order for
a hazard.

Either way it reads as a **glowing slot in the wall**, not a hole into blackness. The lit rim is
what the player actually sees, which is why it pulses and why mouths are long along the track
rather than round; the dark bore behind it only really shows from outside the tube.

A warp is worth more than an obstacle in a time trial, because a block costs a shield and about
0.4 s while a warp costs whatever the ship takes to fly that ground again — at 120 u/s a default
warp is a little over two seconds. Put one where the fast line is obvious and tempting, so the
greedy route is the one that risks it.

## Theme

| Field | Meaning |
|---|---|
| `darks`, `lights` | 1–4 colors each (`#rrggbb`). Each wall segment picks one dark and one light for its checkerboard. |
| `seamDark`, `seamLight` | The groove and center line at each seam. |
| `far` | Color the walls fade to with distance; also the horizon of flat sections. |
| `ship` | Ship body color. |
| `block`, `target`, `breakable` | Obstacle colors: solid blocks, targets, and blocks that shots can break. |
| `fadeStart`, `fadeEnd` | Distance range of the fade. |
| `glow` | Extra brightness on light cells and seams. Above 0 they bloom, for a neon look. |

Any field left out uses the Earth theme's value.
