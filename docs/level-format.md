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
| `obstacles` | none | Blocks to dodge and targets to shoot; see below. |

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
strafes left and right (the view stays level) and can jump between floor and ceiling. Blending back to
a closed section rolls the planes back up, easing the player toward the center first.

**Pieces that start or end on an open section must be straight** (no `turn` or `climb`).

## Track pieces

Each piece continues from the end of the previous one.

| Field | Default | Meaning |
|---|---|---|
| `length` | required | Length of the piece. |
| `section` | previous | Section to blend into over the piece. Omit to keep the current shape. |
| `turn` | `0` | Total heading change over the piece, in degrees; positive turns right. |
| `climb` | `0` | Total pitch change over the piece, in degrees; positive climbs. |
| `speed` | previous | Speed to reach by the end of the piece (units per second), blended smoothly. Screen effects, field of view, and engine sound follow it. |

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

Tip: annotate pieces with their start and end distance (`// 930 - 1030`) so obstacles are easy to place.

## Obstacles

A **block** is solid: hitting one breaks it, costs a shield, and slows the ship briefly. It also stops shots.
A **target** is destroyed by shots for 100 points, and hurts like a block if you fly into it.
The ship has three shields; losing them all ends the run.

| Field | Default | Meaning |
|---|---|---|
| `at` | required | Distance along the track of the obstacle's center. |
| `kind` | `"block"` | `"block"` or `"target"`. |
| `surface` | `"floor"` | `"floor"` or `"ceiling"`. |
| `x` | `0` | Distance across the surface from its center; positive is right. |
| `angle` | none | **Tubes only**, instead of `surface`/`x`: degrees around the tube from the floor center. 90 = right wall, 180 = ceiling, -90 = left wall. |
| `width`, `length`, `height` | `3`, `2`, `2` | Size across the surface, along the track, and off the surface. |
| `count` | `1` | Place several, each one shifted by the steps below. |
| `spacing` | `0` | Distance along the track between repeats. |
| `xStep` | `0` | Change in `x` per repeat. |
| `angleStep` | `0` | Change in `angle` per repeat. |

```json
"obstacles": [
  { "at": 380 },                                                            // block, floor center
  { "at": 700, "kind": "target", "angle": 90 },                             // target on the right wall
  { "at": 1600, "angle": 0, "count": 4, "spacing": 45, "angleStep": 90 },   // spiral of blocks
  { "at": 1250, "width": 80, "height": 2.5 },                               // full-width wall on a flat section: jump over it
  { "at": 1320, "kind": "target", "surface": "ceiling", "x": -10 }
]
```

On flat sections the ship can strafe 40 units either side of center, so a block 80 wide spans
the whole floor. The ship jumps between floor and ceiling in about 0.55 s, covering
`0.55 × speed` units, so leave that much room before a full-width wall.

## Theme

| Field | Meaning |
|---|---|
| `darks`, `lights` | 1–4 colors each (`#rrggbb`). Each wall segment picks one dark and one light for its checkerboard. |
| `seamDark`, `seamLight` | The groove and center line at each seam. |
| `far` | Color the walls fade to with distance; also the horizon of flat sections. |
| `ship` | Ship body color. |
| `block`, `target` | Obstacle colors. |
| `fadeStart`, `fadeEnd` | Distance range of the fade. |
| `glow` | Extra brightness on light cells and seams. Above 0 they bloom, for a neon look. |

Any field left out uses the Earth theme's value.
