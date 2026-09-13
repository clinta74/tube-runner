# Backlog

Work queued on the `level-options` branch, taken from playtesting `v0.2.2`. Ordered below by what
unblocks or invalidates what, not by size. Each entry says whether it is engine work or authoring,
and whether a test can judge it or only playing can.

## The order, and why

### 1. Win screen: congratulate, and scroll the level times
*From item 8. Engine + HUD. The times list is testable; the rest is playtest.*

First because it is the only **defect** in the list. A full run is 26 levels, so the summary is 27
lines and a total. An 11-level run already overflowed its panel and ran off the bottom of the
screen, so a player who finishes the game cannot see what they did — the one thing they earned. The
other three parts of the finale sequence are features; this is something that does not work.

### 2. Check the engine plume against v0.2.2 before building anything
*From item 5. Costs nothing.*

`ShipView` already stretches the nozzles with speed, pushes the plumes back as they grow, and scales
nozzle emission with it. Until `v0.2.2` that glow sat under the bloom threshold and the engines were
effectively unlit, so the response may have been there and invisible. Look first: this either closes
the item or turns it into "more pronounced", which is a different and smaller job.

### 3. Thrust bar to vertical
*From item 4. HUD, small.*

Cheap, and it pairs with whatever comes out of (2) — the bar says what the throttle is doing
numerically, the plume says it physically. Settle placement at the same time: a vertical bar wants a
screen edge, and the left is taken by the shield pips, the right by the speed readout.

### 4. Primary fire stops autofiring
*From item 9. Core, small change, wide blast radius. Rebalances most of the game.*

Early on purpose. It changes how every level with shooting in it plays, and doing it now means every
later playtest validates it for free; leaving it late buys a second full pass through 26 levels.

It is also the same complaint as before at its root — the fire interval was slowed from 0.15 to 0.32
for exactly this reason, which treated the symptom. Decide the interval again at the same time,
since a deliberate tap every third of a second may now feel sluggish rather than considered.

Needs a playtest from level 1, not a spot check: sealed rings, breakable walls, ordered groups and
keys were all authored assuming a held trigger.

### 5. Warp wells: particles and fewer signs
*From item 1. Renderer. Playtest only.*

Do the **particles and the sign count** now; hold the **size** until (6) is decided. This is the
fourth pass at well visibility and the previous three each helped without finishing it, so another
size bump is the weakest half of the item. A swirl of dust being pulled in is the part that makes a
well read as active rather than as a dark patch, and it follows an existing pattern — the renderer
already drives particle bursts for breaking blocks.

### 6. Spike the see-through wireframe look
*From item 2. Renderer + shader + theme, plus one authored level. The biggest item here.*

A timeboxed prototype rather than a commitment, because its result decides three other things:

- it is the most complete answer to "warp wells are hard to see" there is — a well becomes an
  obvious hole in a visible surface — so it may retire the rest of item 1;
- it makes item 7 (the map) redundant, or proves it is still wanted;
- it raises the priority of item 8 (fork funnels), because a flat wall with holes in it would be
  conspicuous in a level built to be seen through.

Two existing systems push back and need answering in the spike: the distance fade exists to hide
what is far away, which is the opposite of the point; and the end-of-level wall is hidden by that
fade, which is what the whole run-out design rests on.

### 7. A level map on the HUD
*From item 3. HUD + a testable projection in Core.*

Deferred behind (6) deliberately: same goal, and if seeing through the tube works, an abstraction of
the tube is redundant. If it does go ahead, the thing worth building is not a top-down map — at 190
u/s anything the player looks away from the tube to read costs them what it was meant to buy. Route
choice at a fork is the one decision made in advance and currently made blind, so a marker that
lights up before a split may beat a map that is always there.

### 8. Fork and merge funnels
*From item 7. Renderer. User marked it lower priority.*

Its priority depends on (6). The abruptness is structural: a fork is a flat disc across the chamber
with the branch openings cut out by a shader, so the player flies at a wall with holes rather than
into diverging tubes. The fix is the technique warp wells already use — displace the vertices into a
throat instead of cutting a hole.

### 9. A better ship model
*From item 6. Renderer or asset work.*

Late because it is the item most likely to be invisible in play. There is no ambient light, only a
headlight on the camera, so detail that relies on shading goes black at grazing angles — that is why
the wings went black once already. And at six units back the ship is small on screen.

Settle first whether the complaint is *detail* or the ship reading as a cheap shape. If it is the
latter, silhouette and motion buy more than geometry, and the cheap route gets most of it. A paused
screenshot up close should decide it.

### 10. The rest of the finale: autopilot, camera, victory tube
*From item 8. The three parts that are not the win screen.*

Last because it is the largest and the least load-bearing. The looping item-free tube is cheap and
reuses everything. The autopilot should feed `ShipSim` synthetic input rather than add a second
movement path. The camera is the real work: it is a fixed offset behind and above with a look-ahead
target, and the floating-origin placement runs through it, so a front-right framing is a change
rather than a parameter.

## Standing constraints

Any of this has to keep three guards passing, all of which exist because something shipped wrong:

- `SplitSavingTests` — every fork saves 4% on its quick branch and spreads 10% between branches.
- The chain-cycle check in `LevelLoaderTests` — the run is a line from level 1 to the finale, every
  level reachable exactly once. A victory level would need thinking about here.
- `RunOutTests` — nothing authored in the last 260 units of a level.
