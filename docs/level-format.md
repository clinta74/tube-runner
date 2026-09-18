# Level Format

Levels are JSON files in `game/levels/`. Comments (`//`) and trailing commas are allowed. A test
(`LevelLoaderTests.ShippedLevels_LoadAndLinkUp`) loads every file there, so a broken level fails the build's tests.

Units are world units; the default tube radius is 6 and the default speed is 80 units per second.

## Top level

| Field | Default | Meaning |
|---|---|---|
| `name` | `"Untitled"` | Shown when the level starts. |
| `id` | from `name` | Stable key for the level's best time. See below. |
| `next` | none | File name of the following level. |
| `speed` | `80` | Starting forward speed, units per second. Track pieces can change it. |
| `segmentLength` | `60` | Distance between wall seams. The checker pattern can change at each seam. |
| `theme` | Earth theme | Colors; see below. |
| `sections` | required | Named cross-sections used by the track. |
| `start` | required | Section name the track starts with. |
| `track` | required | Ordered list of track pieces. |
| `obstacles` | none | Obstacles placed from the start of the track. Usually better inside track pieces; see below. |
| `apertures` | none | Aperture rings placed from the start of the track. Usually better inside track pieces; see below. |

**Every shipped level declares an `id`**, and once written it never changes. Best times are saved
against it, so a level keeps its times through being renamed, renumbered, or having another level
inserted in front of it - none of which the file name survives. A level without one falls back to
its name folded down (`"Neon Run"` becomes `neon-run`), which is fine for a bench file and nothing
else; `LevelLoaderTests.EveryShippedLevel_DeclaresItsOwnId` holds the real levels to saying it out
loud, and to each one being different.

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

### Joining the next level

The next level takes over with no pause: at a level's finish, 260 units before its track ends, the
next level is loaded and the ship placed **16 units** into it. Whatever the camera was looking at is
replaced in one frame, so both sides of the join have to show the same thing. `LevelJoinTests` checks
every join, from just behind the ship to the old level's end wall.

- **End on a copy of the next level's start.** A level's last pieces are a straight blend into the
  next level's starting section, then the next level's **first 276 units** - its first straight and
  any turn or climb, cut in proportion (a turn is a constant rate, so a cut piece bends exactly like
  the start of the original). If the two levels define their start tube differently, add a
  `"handover"` section with the next level's values. Colours and speed may change at the join.
- **Keep a level's first 276 units plain.** Nothing in them may change the section, and nothing may be
  placed there - an obstacle, pickup or well there would appear from nowhere at the handover. A piece
  that blends its shape eases in and out over its whole length, so a copy cut part-way through can't
  match it; a level's first shape change has to start at 280 or later.

```json
{ "length": 120, "section": "tube" },     // straighten into the next level's tube
{ "length": 180 },                         // its first straight
{ "length": 96, "turn": 9.333333 }         // 96 units of its 35 degree, 360-unit bend
```

### Loops

`turn` and `climb` rotate about the track's own axes, so a loop is just `climb` past 360 — nothing
flips over the top. But **a loop flown at one steady rate closes into a circle and runs straight
back through its own entry**, however it is tilted. Break it into pieces with different rates:

- **Vertical loop:** four quarters of `climb: 90`, with `turn: 30` on the second and `turn: -30` on
  the third. The exit runs beside the entry and points the same way. Turning the same way on both
  middle quarters, on the first and last, or on every quarter, all land the exit back on the entry.
- **Flat loop:** two halves of `turn: 180`, climbing `30` on the first and `-30` on the second, so it
  passes over its own entry. A climb of 20 or less on 320-unit halves is not enough clearance.

`TrackClearanceTests` fails any level, bench levels included, whose track comes within a tube's width
of itself. The tube also comes out rolled around its own axis, which cannot be seen in play.

```json
{ "length": 160, "climb": 90 },
{ "length": 160, "climb": 90, "turn": 30 },
{ "length": 160, "climb": 90, "turn": -30 },
{ "length": 160, "climb": 90 }
```

Tip: annotate pieces with their start and end distance (`// 930 - 1030`); `tube play --start 930` then
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

  The ground saved is roughly `offset x turn-in-radians`, **times about two thirds** — blending out
  to the offset and back costs real distance the formula ignores, so treat it as an upper bound.
  Measured, 30 units of offset through a 75° bend over 400 units saves 6.3%, where the formula
  promises 9.8%. Small bends are therefore worthless: 22 units through a 20° bend measures 0.9%, a
  tenth of a second at 100 u/s, and no player will trade a tighter tube for that.

  Aim for the **quickest branch saving 4% or more**, and for **10%+ between the quickest and the
  slowest**. That second number is the one a player feels: a fork reads as a real choice because the
  long way round is genuinely long, not only because the short way is short. `SplitSavingTests`
  measures every shipped split against both and fails if one goes flat. Price the short way with a
  tighter `section` and more obstacles, and the choice becomes real: time against shields.
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
| `shield-slot` | Adds one **extra** shield, up to 3, on top of the normal three. |
| `rapid-fire` | Fires about three times as fast for 8 seconds. |
| `ring-gun` | 3 ring-gun shots. Each sweeps the whole tube, breaking every target and breakable block it passes; solid blocks survive. |
| `unstoppable` | For 6 seconds the ship smashes through anything it touches — solid blocks included — scoring each one and losing no shields. |

```json
{ "length": 250, "pickups": [
  { "at": 20, "kind": "ring-gun" },
  { "at": 80, "kind": "shield-slot", "angle": 180 }   // up on the ceiling
```

An **extra shield is its own pool**, not a longer bar. It is spent before the normal shields, and
neither `shield` nor `full-shields` ever gives one back — once an extra is gone it takes another
`shield-slot` to hold one again. With 2 of 3 normal shields, picking one up gives a fourth point and
leaves the empty third still empty.

That is what makes it the rarest prize worth going off the fast line for: everything else the level
hands out can be replaced later, and this cannot.

```json
] }
```

Put a power-up in a calm stretch the first time a level uses it, then follow it with the obstacle it
answers: a ring of targets after the ring gun, tough blocks after rapid fire.

### Power-ups that have to be shot for

A pickup can take a `lockedBy`, naming a target group exactly as a door does. Until every target in
that group is destroyed the pad is **dead**: it is drawn dark, with its label in the key's colour,
and flying over it does nothing at all. Shoot the keys and it lights up in its own colour and can be
taken.

```json
"obstacles": [ { "at": 80, "kind": "target", "angle": 90, "group": "k1" } ],
"pickups":   [ { "at": 260, "kind": "shield", "lockedBy": "k1" } ]
```

This is the **gentlest** lock in the game, and the one to teach the idea with: missing the key costs
a reward, not a shield. A locked door punishes a miss with a wall, so it wants to arrive after the
player already knows what a key is for.

Two rules make it a decision rather than a detour:

- **Put the key off the line to the pad.** If steering to the key also steers to the pad, the lock
  costs nothing and may as well not be there.
- **Leave time between them.** The shot has to land and be seen to land, or a dead pad reads as a
  broken one. Treat the distance between key and pad the way you would a key and its door.

A pad can be locked by a group of several keys, and the same group can open a pad and a door at once
- which is how a stretch can offer "shoot these and you get through *and* get paid".

## The run-out at the end of a level

**Leave the last 260 units of a level empty.** A level counts as finished that far before its track
actually ends, so the wall closing it is still deep in the distance fade when the next level takes
over and the tube reads as carrying on rather than as something the run stopped at.

That makes the final stretch unreachable. Anything authored in it is drawn, flown towards, and then
taken away as the level hands over — the player sees a block or a pickup ahead and the level simply
ends. Finish every level with an empty piece long enough to cover it.

`RunOutTests` checks every shipped level and names anything stranded, so this cannot ship by
accident. It is worth a test because it is invisible while authoring: the level looks complete, and
the fault only shows up in the two seconds before a handover.

## Gates, movers, plates and ordered groups

Four extras on an obstacle, all of them optional, and all of them usable together.

| Field | Default | Meaning |
|---|---|---|
| `period` | `0` | Seconds in an open/shut cycle: out for the first half, withdrawn for the second, sliding between. 0 means always there. |
| `phase` | `0` | Where in that cycle it starts, 0 to 1. Stagger these across a row to make a rhythm. |
| `sweep` | `0` | How far it slides to either side of its `x`, around the surface. 0 stands still. |
| `sweepTime` | `2` | Seconds for one full sweep out and back. |
| `group` | none | Names a group. Targets in one only break in `order`, lowest first. |
| `order` | `0` | Place in the group's firing order. Needs a `group`. |
| `lockedBy` | none | Stays solid until every obstacle in the named group is destroyed. |

```json
{ "at": 100, "period": 3, "phase": 0.5 },          // a gate, open when its neighbour is shut
{ "at": 200, "sweep": 9, "sweepTime": 5 },         // a block sliding around the tube
{ "at": 300, "kind": "plate", "width": 14 },       // wall that cannot be answered
{ "at": 400, "kind": "target", "group": "k" },     // the key
{ "at": 520, "lockedBy": "k", "width": 30 }        // the door it opens
```

A `lockedBy` naming a group nothing is in is a mistake the level refuses to load with, and so is a
group whose last key stands further down the track than the thing it opens. Left to load, the first
is a wall that never opens or a pad that never lights, and the second is a key that cannot be shot
in time however well the level is flown - and both read as the player having missed a shot rather
than as a typo.

**A `plate` cannot be shot, broken or rammed** — only flown around. Every other obstacle has a way
out, including unstoppable, which turns any collision into a free scored break. A plate is the one
thing that stays, so unstoppable stops being a universal answer. Introduce plates alone on a clear
stretch: arriving at one with unstoppable running otherwise reads as a bug rather than a rule.

**Gates work with the throttle rather than against it.** The player already sets their own speed, so
a gate cycle asks them to hurry or hold back — which is the one thing a speed pickup could never do,
since a pad only hands out speed the throttle already had.

**A gate slides out of a socket in the wall, and only hurts while it is nearly all the way out.**
Both halves of that matter. Blinking in and out gives the eye nothing to track, so the cycle can only
be counted, never watched; and a gate that kills while it is still rising does not look like a wall
yet, so the hit reads as landing *after* the ship went past. The socket is left on the wall whether
or not the gate is standing in it, so a stretch of gates is readable on the approach. The lethal
window is therefore slightly **shorter** than the stated half-cycle — budget for that when spacing a
run of them, because a gate you can just scrape past is doing what it should.

**The ring gun takes one member of an ordered group per shot** — the one whose turn it is — and is
then spent on that group. It ignores plates entirely. Making it skip groups altogether was worse: a
ring passing through a target that was ready to break just reads as the gun not firing. Testing the
order alone is not enough either, because a ring overlaps an obstacle for several frames and would
clear the whole group a member at a time.

**Order gates shooting, not ramming.** A target in a group still breaks and still costs a shield if
the ship flies into it, whatever its turn. So put an ordered group **off the flight line** — out at an
`angle`, or wide on an `x` — or the player will clear it by driving through it.

**In an ordered group, the target whose turn it is burns at full and the rest sit dim**, and that
updates after every shot. The useful thing to know is not which group a target belongs to but
whether shooting it now will do anything, so a fixed marker saying "this one is third" would be
worse: it tells the player something they have to hold in their head rather than something they can
read off the wall at the moment it matters.

A key that is also ordered keeps its pair colour while it waits, so the two signals stack instead of
fighting — the colour says which door it opens, the brightness says whether it is next.

**A key and the door it opens are drawn in the same colour**, so a target that opens something does
not look like an ordinary target and a locked door does not look like an ordinary block. The door
sits in the same hue but dark, as the shut version of the bright thing that opens it, and bursts
apart when its last key goes. Colours are handed out in the order the pairs are met and there are
four of them, so **keep the number of pairs open at once to four or fewer** — beyond that they start
repeating, and telling which key opens which is the whole puzzle wherever pairs overlap.

**Shots leave at the ship's own position across the surface**, and travel straight down the track.
There is no aiming, so the player lines a target up by steering onto it. That is what makes an
ordered group a puzzle worth having: they must steer to each one in turn, in the order you set,
rather than hold the trigger and sweep. It also means a group spread around the tube takes real
travel to clear, while one stacked at a single `angle` takes almost none.

## Apertures

An **aperture** is a ring of blades that seals the tube and irises open as its keys are shot. It goes
in an `apertures` list on a piece (or at the top level), and it is the one obstacle authored as a
whole rather than a piece at a time: the loader cuts it into blades, and each blade is an ordinary
locked obstacle from then on.

```json
{ "length": 500, "obstacles": [
    { "at": 80,  "kind": "target", "angle": 90,  "group": "k1" },
    { "at": 140, "kind": "target", "angle": -90, "group": "k2" }
  ], "apertures": [
    { "at": 360, "blades": 6, "keys": ["k1", "k2"] }
  ] }
```

| Field | Default | Meaning |
|---|---|---|
| `at` | required | Distance of the ring from the start of its piece (or of the track). |
| `branch` | none | Inside a split, which branch. Required there, not allowed elsewhere. |
| `blades` | `6` | How many sectors the ring is cut into, 3 to 12. |
| `keys` | required | Target groups. Blade *n* is opened by key *n* in turn, wrapping round. |
| `open` | `0` | Blades left out entirely, counting from the floor, so there is a way through from the start. |
| `angle` | `0` | Turns the whole ring, in degrees, so the blades (and any gap) sit where you want them. |
| `height` | `2.5` | How far a blade stands off the wall, for the collision. |
| `length` | `2.5` | Thickness along the track. |
| `hits` | `0` | Shots to break a single blade. 0, the default, means shots do nothing - the keys are the way through. |

**One key over every blade is the pure version:** the whole ring spirals open at once, and it is the
shape to introduce the idea with. More keys divide the ring, and a blade opens when *its* key falls,
so a part-opened aperture is a tube with a way round one side of it and a wall on the other. Three
keys over six blades open opposite pairs together, which reads much better than three neighbours
leaving in a row.

**Keep `keys` to three or fewer.** Every key group takes one of the four pair colours, and an
aperture the player cannot read the colours off is just a wall with extra steps.

**A shut aperture is a sealed ring.** Arriving at one with its keys still standing costs a shield,
so the first aperture a level shows should leave a sector `open`, and the first fully sealed one
should come after the player has opened a couple. Blades are drawn from the middle of the tube
outwards and shaded across their own width, so how much of the ring is left can be read at distance;
the lip they sit in stays in the wall whether the ring is shut, part open or long gone.

**Unstoppable smashes blades, the ring gun opens them.** A ring sweep destroys every key it passes,
so one charge opens an aperture outright - treat that as a deliberate skeleton key and ration the
charges, or make the keys an **ordered group**, which the ring takes one member of per shot. Then a
three-key aperture costs three charges, which is a price worth paying rather than a free pass.

**Apertures need a closed tube.** A flat section has no way round, so the level fails to load rather
than ringing half a tube.

**An aperture over a fork** is the strongest use of the idea: put one in each branch's mouth and the
keys decide which way the player is allowed to go, instead of which way they choose to go.

## Thrust zones

A `thrustZones` list, on a piece or at the top level, marks a stretch that **closes off the bottom
of the throttle range**. Nothing is taken away for flying wrong: the cost is control, and the thrust
bar on the HUD shows the closed-off part while the ship is inside.

| Field | Default | Meaning |
|---|---|---|
| `at` | required | Distance of the zone's center from the start of its piece (or of the track). |
| `length` | `120` | Extent along the track. |
| `kind` | `floor` | `floor` closes off the slow end; `ceiling` closes off the fast end. |
| `branch` | none | Branch index, inside a split. |

Each kind makes a fixed cut of the ship's range (0.5 to 1.75):

- **`floor`** takes the slowest **20%**, so the floor becomes 0.75. It stops the player crawling
  through; a ship already above the cut is left alone.
- **`ceiling`** takes the fastest **75%**, so the ceiling becomes about 0.81. It holds the player
  back, and a ship arriving faster than that is brought down to it.

Only one end moves; the other stays open.

**Cover the whole section.** Put `at` at half the piece's length and `length` at the piece's
length, so the limit starts and ends with the section that motivates it rather than giving the range
back part way through:

```json
{ "length": 400, "section": "narrow", "thrustZones": [ { "at": 200, "length": 400, "kind": "ceiling" } ] }
```

Zones used to carry their own `min` and `max`. Floors authored at 1.3 dragged every ship into the
middle of its range on the way in, which took the throttle away rather than narrowing it. Those keys
are now rejected, so an old level fails to load instead of flying differently from how it reads.

This replaced a zone that took a shield for being flown too fast, which was wrong twice over: it
punished the one thing the rest of the game rewards, and it did it with no warning a player could act
on — nothing on screen said how close to the limit they were, so the first they knew of it was the
hit. A cost you can watch arrive is a mechanic; one you can't is a gotcha.

## Warp zones

A `warps` list, on a piece or at the top level, sinks a **well** into the tube wall: the wall itself
bends down into a throat that opens through it. Fly into one and the ship falls down it for a moment
before being thrown back up the track. It costs **time, never a shield**, and a well stays armed —
fly into the same one again and it takes you again.

Dust is drawn down into every well: spawned in a shell around the mouth, pulled inwards and given a
twist on the way, so it spirals in. This is what says the thing is live. Three passes at making
wells bigger never quite finished the job, and the reason was that a well is a hole in a dark wall
that holds perfectly still — nothing about it moved.

A ring of four warning signs is placed automatically 75 units back up the track: a yellow triangular
plate with a dark border, carrying a black disc, held off the wall on a post, and pulsing. Four is
enough that one faces the player whichever way round the tube they are flying, now that the well
does some of its own warning. Nothing needs declaring for them.

Every part of that is there to stop a sign reading as an obstacle, which is worse than no sign at
all — it gets dodged rather than heeded. Blocks sit flat on the wall and are solid, so the **post and
the gap under it** are most of what separates them at a glance, and **nothing else on a wall pulses**.

| Field | Default | Meaning |
|---|---|---|
| `at` | required | Distance of the mouth's center from the start of its piece (or of the track). |
| `surface`, `x`, `angle`, `branch` | as obstacles | Where on the wall it sits; placed exactly like an obstacle. |
| `span` | `0.33` | How far round the tube the mouth reaches, as a share of the section's perimeter. At most 0.4. |
| `length` | `52` | Opening along the track. Longer than it is wide, so a mouth met end-on still reads. |
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

**A mouth is measured as a share of the way round, not in units.** A warp is something the player
steers around rather than a gap they thread, so two thirds of the wall has to stay flyable — and an
absolute width cannot hold that promise, because the same number is a third of a standard tube and
nearly half of a narrow one. One authored well would behave like three different hazards depending
on which section it landed in.

At a third the arithmetic is tidy: half the arc is `pi x r / 3`, so **a well's radius across the
surface comes out about equal to the tube's own radius**. A radius-10 tube gets a mouth about 10
across the wall; a radius-6 tube gets about 6.

`length` along the track is still in units, since nothing constrains it.

**The wall is drawn down into the well**, rather than the opening being a hole with something else
hung behind it. Vertices inside the mouth are displaced inwards along the surface normal, so the
tube extrudes into the throat as one continuous surface with no seam to mismatch at the lip, and
only the cells past the throat are omitted. Each vertex carries how far in it lies as `UV2.x`, and
`tube_wall.gdshader` takes the inside to black just past the rim: an unlit wall bending inwards
gives the lip nothing to read against, which is what made earlier versions read as a flat smudge.
A separate funnel mesh was tried first and abandoned — its rim never matched the wall it met.

The cut is measured around the tube rather than across one strip, so a mouth can sit on the seam
between the floor and ceiling halves without tearing either open.

Going down a well the camera holds the pose the ship had on the track and swings its aim onto the
ship, easing out hard so the view has caught up early in the dive. Tracking the ship at an even rate
leaves the aim trailing it the whole way down, which reads as the ship dropping out of frame rather
than the player following it in.

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
