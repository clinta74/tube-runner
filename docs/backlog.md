# Backlog

Work queued on the `level-options` branch, taken from playtesting `v0.2.2`. Ordered below by what
unblocks or invalidates what, not by size. Each entry says whether it is engine work or authoring,
and whether a test can judge it or only playing can.

## Done on this branch

- **1. Win screen** — scrollable times, personal bests marked, run best keyed by starting level.
- **2. Late hits** — the ship is pulled back to where it met the obstacle; the collision was right
  and the picture was a whole frame of travel ahead of it.
- **3. Jump window** — HUD cue driven by the ship's own rule, plus lines on the wall where the
  window opens and shuts, measured rather than authored.
- **4 / old 5. Engine plume** — it existed and was reading the wrong signal, so it sat pinned at
  full across most of the throttle in late levels. It follows the throttle now.
- **6. Autofire** — one shot a press; holding is what rapid fire buys. Rebalances every shooting
  level and still wants a playthrough from level 1.
- **7. Warp wells** — dust drawn in, and the size bug behind four separate reports: the loader
  carried its own copy of the defaults, so every well in the game was 6 by 6. Width is now a share
  of the tube's perimeter, which also removes the second place it could be defined.
- **13. Escape menu** — resume, restart level, restart run, quit, with confirms on the two that
  throw a run away.
- **5. Thrust bar** — upright on the right edge beside the speed readout.
- **8. Wireframe style** — a per-level theme flag. Lines kept, wall discarded, so the track ahead is
  visible through it. Two systems that exist to hide distance had to be answered: the fade now only
  dims rather than hides, and chunks build as far as the style can see instead of a flat 450.
  Fork and merge walls are dropped in this style, since a disc across the chamber hides the branches
  that are the point of it. Bench at `levels/bench/wire.json`.

Found and fixed along the way, not on the original list:

- Keys and the doors they open are coloured as pairs; an ordered group lights whichever target's
  turn it is. Both mechanics worked, were tested and were documented, and none of that made them
  visible - a rule the player cannot see is not a mechanic.
- An unlocked door carried on being drawn, so the way ahead looked shut and the ship flew through
  a wall.
- Gate sockets were never freed on a level change and leaked into the scene tree for the rest of
  the run.
- `tube play --summary` opens straight onto the results screen, because checking it otherwise meant
  playing thirteen levels without dying.

## The order, and why

### 1. Win screen: congratulate, and scroll the level times
*From item 8. Engine + HUD. The times list is testable; the rest is playtest.*

First because it is the only **defect** in the list. A full run is 26 levels, so the summary is 27
lines and a total. An 11-level run already overflowed its panel and ran off the bottom of the
screen, so a player who finishes the game cannot see what they did — the one thing they earned. The
other three parts of the finale sequence are features; this is something that does not work.

### 2. Hits landing after the ship looks past the obstacle
*New item. Core. Testable once the cause is confirmed.*

Reported on **stationary** obstacles, which rules out the mover and gate timing already fixed. Second
because it is a fairness problem: a hit the player cannot account for reads as the game cheating,
and it undermines every other difficulty decision in the game.

Most likely cause, to be measured rather than assumed. Collision is swept over the distance covered
in a frame, `[before, now]`, and fires if the obstacle lies anywhere in that span — but the ship is
**drawn at the end of the sweep**. At 190 u/s and 60 fps that span is over three units, and an
obstacle is two long, so the ship can be drawn a full body length past something at the instant the
hit registers. The collision is right and the picture is wrong.

If that is it, the fix is presentational, not a hitbox change: report where in the sweep the hit
happened and show the ship and the burst at the impact point for that frame. Worth confirming before
touching `ShipHalfLength` (0.8) or obstacle `Length` (2), since shrinking a box that is already
correct would make contact miss instead.

### 3. Knowing when the jump is available on flat sections
*New item. HUD, plus possibly wall markings. Small to medium.*

Jumping is gated on the section being fully unrolled (`opening >= 0.5`), and the transition in and
out is gradual, so there is a stretch where the player cannot tell whether the button will work. A
HUD icon with up/down arrows, and/or markings on the walls where the jump window opens and closes.

The gate is already a clean boolean in `ShipSim`, so the HUD side is cheap and the state is exact —
no guessing at thresholds. Wall markings are the more useful half if they can be placed where the
window actually starts, since that keeps the player's eyes on the tube.

### 4. Check the engine plume against v0.2.2 before building anything
*From item 5. Costs nothing.*

`ShipView` already stretches the nozzles with speed, pushes the plumes back as they grow, and scales
nozzle emission with it. Until `v0.2.2` that glow sat under the bloom threshold and the engines were
effectively unlit, so the response may have been there and invisible. Look first: this either closes
the item or turns it into "more pronounced", which is a different and smaller job.

### 5. Thrust bar to vertical
*From item 4. HUD, small.*

Cheap, and it pairs with whatever comes out of (2) — the bar says what the throttle is doing
numerically, the plume says it physically. Settle placement at the same time: a vertical bar wants a
screen edge, and the left is taken by the shield pips, the right by the speed readout.

### 6. Primary fire stops autofiring
*From item 9. Core, small change, wide blast radius. Rebalances most of the game.*

Early on purpose. It changes how every level with shooting in it plays, and doing it now means every
later playtest validates it for free; leaving it late buys a second full pass through 26 levels.

It is also the same complaint as before at its root — the fire interval was slowed from 0.15 to 0.32
for exactly this reason, which treated the symptom. Decide the interval again at the same time,
since a deliberate tap every third of a second may now feel sluggish rather than considered.

Needs a playtest from level 1, not a spot check: sealed rings, breakable walls, ordered groups and
keys were all authored assuming a held trigger.

### 7. Warp wells: bigger, with particles, and fewer signs
*From item 1. Renderer. Playtest only.*

All three parts together: **bigger**, a **swirl of dust being pulled in**, and the **sign ring down
to 4**. The size bump is no longer held back — the wireframe look is wanted for its own sake rather
than as the way wells get taught, so it cannot be relied on to solve this.

Wells are still easy to avoid and barely noticed after three passes, so this one should stop being
timid about size. Around the tube is capped by the rule that two thirds of the wall stays flyable,
but **length along the track, depth of the bore, and the size of the open throat are all free**, and
previous passes only moved the first two by modest amounts. The particles are what make a well read
as active rather than as a dark patch, and they follow an existing pattern — the renderer already
drives particle bursts for breaking blocks.

### 8. The see-through wireframe look
*From item 2. Renderer + shader + theme, plus one authored level. The biggest item here.*

Wanted as **an effect in its own right**, not only as a teaching aid for wells. That changes what it
has to be: not a prototype that might be thrown away, but a second render style good enough to build
levels around, with the wire level being a distinct experience rather than a tutorial.

Two existing systems push back and need answering: the distance fade exists to hide what is far
away, which is the opposite of the point; and the end-of-level wall is hidden by that fade, which is
what the whole run-out design rests on.

It still raises the priority of (10), fork funnels — a flat wall with holes in it would be
conspicuous in a level built to be seen through.

### Small follow-up: obstacles still appear at 450 in a wire level
The track now builds much further in the wire style, but `ObstacleRenderer` keeps its own view
distance, so blocks and wells pop in well inside the visible tube. One line to change; held back
because obstacle views are individual nodes rather than batched chunks, so the cost is worth seeing
before extending it.

### 9. A level map on the HUD
*From item 3. HUD + a testable projection in Core.*

Deferred behind (6) deliberately: same goal, and if seeing through the tube works, an abstraction of
the tube is redundant. If it does go ahead, the thing worth building is not a top-down map — at 190
u/s anything the player looks away from the tube to read costs them what it was meant to buy. Route
choice at a fork is the one decision made in advance and currently made blind, so a marker that
lights up before a split may beat a map that is always there.

### 10. Fork and merge funnels
*From item 7. Renderer. User marked it lower priority.*

Its priority depends on (6). The abruptness is structural: a fork is a flat disc across the chamber
with the branch openings cut out by a shader, so the player flies at a wall with holes rather than
into diverging tubes. The fix is the technique warp wells already use — displace the vertices into a
throat instead of cutting a hole.

### 11. A better ship model
*From item 6. Renderer or asset work.*

Late because it is the item most likely to be invisible in play. There is no ambient light, only a
headlight on the camera, so detail that relies on shading goes black at grazing angles — that is why
the wings went black once already. And at six units back the ship is small on screen.

Settle first whether the complaint is *detail* or the ship reading as a cheap shape. If it is the
latter, silhouette and motion buy more than geometry, and the cheap route gets most of it. A paused
screenshot up close should decide it.

### 12. The rest of the finale: autopilot, camera, victory tube
*From item 8. The three parts that are not the win screen.*

Last because it is the largest and the least load-bearing. The looping item-free tube is cheap and
reuses everything. The autopilot should feed `ShipSim` synthetic input rather than add a second
movement path. The camera is the real work: it is a fixed offset behind and above with a look-ahead
target, and the floating-origin placement runs through it, so a front-right framing is a change
rather than a parameter.

### 13. A game menu on Escape
*New item. HUD + input. Quit, restart, re-run; the existing prompts move into it.*

Higher than it looks. **There is currently no way to quit the game** - nothing is bound to Escape,
and the only exit is closing the window. That is a missing basic rather than a nicety, and it should
probably jump most of the list below it.

Notes for eval:
- Wants: resume, restart this level, restart the run, quit. The retry and re-run prompts currently
  live as hint text on the results and shields-down screens, and would move here, which also frees
  those screens from explaining their own controls.
- It needs pause semantics, and pause already exists (P) - the menu should use the same hold rather
  than inventing a second one. Worth deciding whether the menu *is* the pause screen, which would
  make P and Escape the same thing and leave one concept instead of two.
- Escape is `ui_cancel` in Godot by default, so the binding is free but wants registering explicitly
  alongside the rest in InputSetup rather than relying on the built-in.
- Mostly untestable as UI, though "which options are offered in which state" is small enough to keep
  honest by hand: mid-level, run over, and run complete each want a different set.

## After v0.3.0

Done, and shipped across v0.4.0 (gameplay, music, Loop the Loop), v0.4.1 (MSI installer, icon) and
v0.4.2 (update check, build tool):

- **Thrust zones toned down.** Every zone now closes only the slowest 20% of the range (floor 0.75)
  and nothing else. Zones had carried their own `min`/`max`, and floors of 1.3 dragged every ship to
  the middle of the range on entry. The keys are now rejected by the loader. Every zone covers its
  whole section. A second kind, `"kind": "ceiling"`, cuts the top 75% instead (ceiling about 0.81);
  level 15's zones use it, which keeps Restraint about holding back.
- **Thrust bar and jump cue enlarged.** The bar has a dark backing and a rim so the full range reads
  over bright walls, and the closed-off part is hatched with the fill ghosted through it. The on-track
  jump lines are thicker too.
- **Unstoppable warning.** Three falling beeps across the last second, then a power-down tone.
- **Restraint rebuilt** (level 15) for its ceiling zones: slow, so every section bends and the zones
  are the densest stretches, with spirals of blocks and targets threaded through them.
- **Loop the Loop** (level 8b, between Crossroads and Neon Run): an early wireframe level of vertical,
  flat and dive loops and switchbacks. Named 8b rather than renumbering, since best times are keyed by
  file name. `TrackClearanceTests` now fails any level whose track passes through itself; a loop at
  one steady rate closes back onto its own entry, and `level-format.md` gives the shapes that clear.
- **Game icon.** `game/icon.svg`, rendered to `icon.ico` by `tube icon`; used for the window, the exe,
  the Start menu shortcut and the installed-apps entry.
- **`tube` build tool.** The PowerShell scripts are now C# in `tools/Build`, run through `tube.cmd`:
  `tube play`, `tube export`, `tube installer`, `tube icon`. The release workflow uses it too.
  `tube export` also finds export templates stranded in a packaged app's private storage - where a
  terminal inside the Claude desktop app redirects `%APPDATA%` writes - and prints the robocopy that
  moves them.

### Adaptive background music
*Shipped in v0.4.0, procedural.* The Unstoppable theme was turned up after a first listen: louder, in
within a tenth of a second, and the engine and wind duck under it. `GameSession.Momentum` (tested) climbs over 90 s of clean
flying, loses 0.35 per hit and carries between levels. `MusicSynth` fades in pad, bass, drums,
arpeggio and lead at rising momentum, and Unstoppable's own riff replaces them, crossfading back over
its last second. What is left is judgement: tempo, key, levels against the engine and cues, and
whether synthesized music is good enough or this wants recorded stems.

Original notes:

Music that builds while the player keeps progressing without losing a shield, and fades back out
when they start to struggle. Notes for eval:
- `EngineAudio` already synthesizes everything into one generator, so a music layer fits there with
  no asset pipeline. Whether procedural music is good enough, or this wants real stems, is the first
  thing to settle.
- Drive it from one "momentum" value in Core: it rises with distance since the last hit, drops sharply
  on a hit, and is testable. Layers (pad, then bass, then lead) fade in as it rises, rather than
  one track getting louder, so progress is heard as music arriving.
- **Unstoppable gets its own music, which replaces the background track while it runs** rather than
  playing on top of it. That also gives the one-second warning something to land on: the ram theme
  can break down over its last second and hand back to whatever momentum has built.
- Watch the mix against the engine and wind, which already rise with speed, and against the cues. A
  warning beep lost under the lead defeats the point of it.

### Windows installer
*Shipped in v0.4.1; installed by hand from the v0.4.2 release, which showed the right version.* An
MSI with its cabinet embedded, from WiX 5.0.2 (`installer/TubeRunner.wixproj` and `Package.wxs`,
built by `tube installer` through `dotnet build`, so nothing extra to install). Per-user install with
no admin prompt, defaulting to `%LOCALAPPDATA%\Programs\Tube Runner` with a page to choose another
folder (WiX's InstallDir dialogs, license page skipped). Start menu shortcut and installed-apps entry
with the game's icon, uninstall from Windows settings, major upgrades that replace the old version and
refuse downgrades. The release workflow attaches `TubeRunner-<version>.msi` beside the zip.

Still open:
- **Upgrade over an older install** hasn't been tried: install one release, set a best time, install
  the next, and check the time survives. Saves live in `%APPDATA%\Godot\app_userdata\Tube Runner`,
  outside the install folder, so it should.
- **A folder that needs admin rights** (`C:\Program Files`) fails with an access error, since the
  install is per user. WiX's Advanced dialogs would offer "just me / everyone" and elevate for the
  second.
- **No desktop shortcut or "launch when done"** option; both need a custom dialog in an MSI.
- **Unsigned**, so SmartScreen may warn on download. Signing costs money.

Original notes, from before the MSI was chosen over Inno Setup:

The release today is a zip of the exported game. An installer wants a Start menu entry, an
uninstaller, and a per-user install so it needs no admin rights. Notes for eval:
- Inno Setup is the likely fit: one script, free, runs on the existing GitHub Actions Windows runner
  after the Godot export, and the `.exe` attaches to the same `v*` release as the zip.
- The .NET export already bundles its runtime, so there is nothing to install alongside it.
- Save data (bests, settings) lives in Godot's `user://`, not the install folder, so an uninstall or
  upgrade keeps it. Worth checking rather than assuming.
- Unsigned installers trip SmartScreen. Code signing costs money; decide whether "More info → Run
  anyway" is acceptable for now.

### Checking for updates
*Shipped in v0.4.2, and checked by hand: the installed release shows v0.4.2, and "Get version" in the
Escape menu opened the GitHub release page.* `tube export --version x.y.z` stamps the version into the game's
assembly (the `GameVersion` MSBuild property, from the environment); the release workflow passes the
tag. An unstamped build reads 0.0.0 and never checks, so development runs stay offline. At launch
`UpdateChecker` asks GitHub's latest-release API once, with an 8 s timeout, and says nothing on any
failure. `Updates` in Core (tested) reads the answer, ignores drafts and pre-releases, compares
versions numerically, and only accepts this repository's own release page as the link. A newer
release shows on the start screen and as "Get version x.y.z" in the Escape menu, which opens the page.
Test runs (`--start`, `--summary`) and `--no-update-check` skip it.

Still open:
- **No in-game way to turn it off.** Only the `--no-update-check` flag, which a player launching from
  the Start menu can't use. A setting belongs with the menu once there are settings.
- **Downloading and running the new installer in place**, rather than opening the release page. The
  bigger step: fetch the MSI, verify it, hand over to msiexec and quit. Notifying was most of the value
  for a fraction of the risk.

Original notes:

- The game compares its own version against the latest GitHub release
  (`api.github.com/repos/<owner>/<repo>/releases/latest`) and says so on the title or menu screen,
  with the download link. Once per launch, fails silently offline, never blocks play.
- The version has to be stamped into the build from the tag in CI, so the check has something true
  to compare against.
- Downloading and running the new installer in place is the bigger step. Notifying is most of the
  value for a fraction of the risk; start there.

### Settings menu, and menus that take the mouse
*New item. Game + HUD. Settings file and defaults are testable; the menus are playtest.*

There is nowhere for a player to change anything: every option is a command-line flag, which nobody
launching from the Start menu can use.

**The settings:**

| Setting | Why |
|---|---|
| Check for updates, on/off | The reason for the item: the check contacts GitHub at every launch. |
| Display mode: windowed, fullscreen, borderless | Expected everywhere; one Godot call. Remember the window's size and position too. |
| Volume: master, music, effects | The engine, music and Unstoppable theme were balanced by ear on one machine. The mix is already built from separate layers in `EngineAudio`/`MusicSynth`, so each slider is a multiplier. |
| VSync, on/off | Sits naturally beside display mode. |

Not now: graphics quality (nothing heavy to scale yet) and motion options - shake, speed streaks,
hit flash - until someone finds the effects too much.

**Menus that take the mouse.** The Escape menu is one text label with `>` markers, so there is nothing
to click, and sliders and toggles are miserable on keys alone. Rebuild it from real controls - buttons,
checkboxes, an option picker, sliders - which Godot already makes work with the mouse, keyboard and
gamepad through focus. Rebuild rather than bolt mouse hit-testing onto the label. The confirms ("Quit
the game?") move over too.

**Order:**
1. The Escape menu as real controls, keyboard and gamepad still working.
2. A Settings page in it with the four settings above. Saved beside `best_times.json` in `user://`,
   loaded at start, with defaults for anything missing so an old or damaged file never stops the game.
   Settings take effect as they change, not on a separate apply.
3. Key rebinding, below, as its own item.

Keep it small. A setting nobody asked for is one more thing to test at every release.

### Key rebinding
*New item. Game + HUD. Follows the settings menu. The saved bindings are testable; the capture screen is playtest.*

Wanted, but larger than every other setting together, so it waits for the menu to exist. Notes:
- Actions are already registered in one place, `InputSetup`, so a saved binding just replaces what it
  registers at start.
- Needs a "press a key" capture, conflict handling when two actions share a key, keyboard and gamepad
  columns, and reset to defaults.
- The start screen lists the controls as fixed text. With rebinding it has to read the live bindings, or
  it will tell players the wrong keys.

## Standing constraints

Any of this has to keep three guards passing, all of which exist because something shipped wrong:

- `SplitSavingTests` — every fork saves 4% on its quick branch and spreads 10% between branches.
- The chain-cycle check in `LevelLoaderTests` — the run is a line from level 1 to the finale, every
  level reachable exactly once. A victory level would need thinking about here.
- `RunOutTests` — nothing authored in the last 260 units of a level.
