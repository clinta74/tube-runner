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
**Done.** The reach was already extended to twice the fade with the seamless handover, which is
where the track's own reach is; what was left was that things arrived there at full brightness in
a tube already dimmed to a quarter. Every wall material in a see-through level is now dithered in
from twice the fade to the fade, so nothing pops at any reach, and the reach stays at twice the
fade. `--view <units>` fixes the reach for comparing, and `bench/reach.json` is a straight tube
with a block every 100 units for looking at the band; in the levels themselves almost nothing is
ever more than a few hundred units off in a straight line, so the band mostly matters through the
walls. Both wire levels also got more to fly round: a block in most of Loop the Loop's loops and
hairpins, and one before most of Highwire's wells.

### 9. A level map on the HUD
*From item 3. HUD + a testable projection in Core.* **Dropped** after play testing with the fork
funnels in: the funnels show the branches diverging early enough that a marker before a split is
not needed, and the map was already deferred behind the wireframe style for the same reason.

Deferred behind (6) deliberately: same goal, and if seeing through the tube works, an abstraction of
the tube is redundant. If it does go ahead, the thing worth building is not a top-down map — at 190
u/s anything the player looks away from the tube to read costs them what it was meant to buy. Route
choice at a fork is the one decision made in advance and currently made blind, so a marker that
lights up before a split may beat a map that is always there.

### 10. Fork and merge funnels
*From item 7. Renderer.* **Done.** A fork or merge wall is a funnel (`TrackView.BuildFunnel`): the
chamber's cross-section on a polar grid, each vertex sunk along the track by how far it is from the
nearest opening against how far it is from the chamber wall, so every opening's rim lies a full
throat deep in one plane where the branch tube takes over, and the chamber wall is not sunk at all.
Vertices that land inside an opening are moved out onto its rim along their own spoke - the hole is
cut by the mesh, not the shader - and the branch tubes start where the throats end, twelve units
into the split, and stop the same distance before the merge. The funnel wears the wall's own
material: each spoke carries the texture coordinate of the point where it meets the chamber wall
(`ProfileShape.ParameterAt`) and the depth carries on the distance along the segment, so the
chamber's checker runs off the wall and down into the throats. It went through a flat colour first,
shaded off screen derivatives like a well's rim, and after two play tests that read as dark and
then as a blur; the checker is what makes it a surface. The slopes are lit off the geometric normal
and darkened a little towards the throat. Where an opening touches the chamber wall the surface
between them is a strip the whole depth of the throat - the chamber wall carrying on - and the
wall's own vertex stays on the wall there, or it left a slit onto the void. Wireframe levels have no
caps and no funnels.

The original note: the abruptness was structural, a flat disc across the chamber with the branch
openings cut out by a shader, so the player flew at a wall with holes rather than into diverging
tubes; the fix was the technique warp wells already used, displacing the vertices into a throat
instead of cutting a hole.

### 11. A better ship model
*From item 6. Renderer or asset work.*

Late because it is the item most likely to be invisible in play. There is no ambient light, only a
headlight on the camera, so detail that relies on shading goes black at grazing angles — that is why
the wings went black once already. And at six units back the ship is small on screen.

Three pieces of it are done, across five goes and four play tests.

**The wing is a cropped delta**, cut to its planform (`ShipView.Planform`) rather than a box turned
on its corner: long at the root, short at the tip, the leading edge raked back about forty degrees
and the trailing edge about eight, lit along the leading edge and across the tip.

**The ailerons are strips let into the wing's back edge**, hinged along their length so the lit
trailing edge lifts and drops. They got there by way of the trailing edge of the old wing (right
idea, but mid-wing on a plank), the whole outer quarter of the wing (too much surface), a strip
along the tip hinged at its front (a plank swung about its short end), and the same strip hinged
along its length - which was the right hinge on the wrong wing. The sense is the game's, not an
aircraft's: steering right lifts the ship's right wing, since it is riding up the wall that side,
so it is the right aileron that drops. One goes down and one up, and they lead the ship -
hard over as a turn starts, easing to about half while the stick is held, thrown the other way as
it is let go, and full over through the half roll of a jump. The tail fins swing as rudders. They
used to turn about their own middle, follow the smoothed bank, deflect the wrong way round, and be
dark enough to vanish on a wire level. `tube play --steer 1 --shot` holds the stick for a look.

**The cockpit is part of the nose.** Dark panes set flush into the nose's two upper faces, meeting
at its ridge, with a lit sill, and a low dark fairing tapering back along the spine. A dome on the
spine read as a ball on the ship's back, and a teardrop pushed through the ridge as a pod lying on
it: nothing round sits in a faceted nose. `tube play --front --shot` looks at it from the front
right, as the victory lap does.

**The lighting is done too.** The one light was a headlight on the camera, so every face turned
to the camera was lit the same and the hull was a white cut-out from the front. It is a rig on the
camera now: a warm key from the upper left, 45 degrees down and 38 across, a cool fill from the
lower right at a quarter of its strength, and a bluish ambient in the environment, with the hull's
own glow brought down from 0.3 to a floor of 0.1 so the shading shows through it, and its
roughness eased so there is a little highlight. Blocks and pads take the same light and read as
they did. Rig, not sun: the tube rolls round the ship as it steers, so a fixed light direction
would have the ship lit from below half the time.

Settle first whether the complaint is *detail* or the ship reading as a cheap shape. If it is the
latter, silhouette and motion buy more than geometry, and the cheap route gets most of it. A paused
screenshot up close should decide it.

### 12. The rest of the finale: autopilot, camera, victory tube
*From item 8. The three parts that are not the win screen.* **Done**, all three: the victory lap
loads an empty flat track, `StepOutro` flies it by feeding the ship synthetic input, and the camera
swings round to its front right. The title screen's autopilot is the same idea on a different tube.
The notes below are kept for the reasoning.

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
  Restraint's zones use it, which keeps that level about holding back.
- **Thrust bar and jump cue enlarged.** The bar has a dark backing and a rim so the full range reads
  over bright walls, and the closed-off part is hatched with the fill ghosted through it. The on-track
  jump lines are thicker too.
- **Unstoppable warning.** Three falling beeps across the last second, then a power-down tone.
- **Restraint rebuilt** for its ceiling zones: slow, so every section bends and the zones
  are the densest stretches, with spirals of blocks and targets threaded through them.
- **Loop the Loop** (between Crossroads and Neon Run): an early wireframe level of vertical,
  flat and dive loops and switchbacks. It shipped as `level_08b.json`, because best times were keyed
  by file name and renumbering would have moved every time after it onto the wrong level; it is
  level 9 now that they are keyed by a level id instead. `TrackClearanceTests` now fails any level whose track passes through itself; a loop at
  one steady rate closes back onto its own entry, and `level-format.md` gives the shapes that clear.
- **Game icon.** `game/icon.svg`, rendered to `icon.ico` by `tube icon`; used for the window, the exe,
  the Start menu shortcut and the installed-apps entry.
- **`tube` build tool.** The PowerShell scripts are now C# in `tools/Build`, run through `tube.cmd`:
  `tube play`, `tube export`, `tube installer`, `tube icon`. The release workflow uses it too.
  `tube export` also finds export templates stranded in a packaged app's private storage - where a
  terminal inside the Claude desktop app redirects `%APPDATA%` writes - and prints the robocopy that
  moves them.

### Adaptive background music
*Shipped in v0.4.0, procedural. Tracks added after v0.9.5.* The synth plays from a `MusicTrack`
(`src/TubeRunner.Core/MusicTrack.cs`): a tempo, four bars of chords with a root each, and a tune of
one note per eighth. Five so far - `drift` for the title (84, D minor), `run` (the original, 112,
A minor), `pulse` (124, E minor), `undertow` (100, C minor) and `wire` (132, F sharp minor) - and
every level names one with its `music` field: `run` through Over and Under, `pulse` from Earthworks
to Clockwork, `undertow` from Metronome to Shoal, `pulse` again from Undertow to Tollgate, and
`wire` from Ransom to the finale. A change waits for the bar line, so a level line is heard as the
music turning a corner; the title plays `drift` at a held middling momentum, and Start is the first
change. The layers, their arrival with momentum and the unstoppable theme are the same whatever the
track, so tracks differ in key, pace and tune, not instrument; a loader test refuses a track name
that does not exist. Still judgement: whether five is enough, and whether any wants its own
instrument.

After the same play test the throttle range went from 0.5-1.75 to 0.35-1.5 of the track's
speed, both ends brought in; the thrust zones' shares moved with it so a floor zone still leaves
0.75 and a ceiling zone about 0.81, which is what the levels were tuned to.

Two things heard in the first play test, both done in the language momentum already speaks:
- **A change of track is stepped, not cut.** Over the last bar before the line the old track thins
  to its pad, at the line the two pads cross over one bar at equal power, and the new track's bass,
  drums, arpeggio and lead come back over the two bars after it. The synth keeps a player per
  track, so both can sound across the line; a plain crossfade was tried and two keys at once
  smeared, and the cut at the bar line was a lurch. Rendered offline, the loudness dips to a third
  over the bar into the line and is back three bars out, with no jump at the join.
- **A hit dies the music down.** A shield lost drops every layer above the pad at once, and dims
  the pad by half; they come back in the order they first arrived, bass first and lead last, over
  the eight seconds after, for as long as no other hit lands. Momentum still does the same thing
  over a minute and a half; this is it felt at once.

*Before the tracks:* The Unstoppable theme was turned up after a first listen: louder, in
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
- ~~**Upgrade over an older install** hasn't been tried.~~ Tried after v0.9.4, and it found a
  real fault. The procedure: install the previous release's MSI silently (`msiexec /i <msi> /qn`),
  run the installed exe with `-- --title-page=times --shot=<png>` to see it read the best times,
  install the next MSI over it, and check the installed-apps entry (one, at the new version), the
  install folder, the same shot from the new exe, and that `best_times.json` has the same hash.
  The saves survived, as expected: they live in `%APPDATA%\Godot\app_userdata\Tube Runner`,
  outside the install folder. What did not survive was the exe. Every release up to v0.9.4 shipped
  an exe whose file details said 1.0.0.0, and the first build stamped with its real version was
  lower than that; Windows Installer skips a file whose version is below the installed one's, and
  decides so before the old version is removed, so the old exe went and the new one never landed -
  a "successful" upgrade with no game in it. `Package.wxs` now sets `REINSTALLMODE` so every file
  is copied whatever the old one claims, and the upgrade from v0.9.3 to a stamped v0.9.4 was
  rerun to check: exe present at 0.9.4.0, one entry, times intact. Worth rerunning for any change
  to the installer or the export.
- ~~**A folder that needs admin rights** fails with an access error.~~ ~~**No desktop shortcut or
  "launch when done"** option.~~ Done, as two pages of our own after Welcome instead of WiX's
  folder page: *Who is it for?* (just me, or everyone on this computer, which goes to Program
  Files and asks for admin rights when the install starts) and *Where does it go?* (the folder,
  and a checkbox for a desktop shortcut). The finish page has a *Start Tube Runner* checkbox,
  through WixShellExec, impersonated so an everyone install still starts the game as the person
  at the keyboard. The scope page's Next sets the folder default for the scope, unless the folder
  was changed by hand; a radio button's own events were tried first and Windows Installer never
  fired them. All three are properties too, for a silent install: `INSTALLDESKTOPSHORTCUT=""`
  leaves the shortcut out, `ALLUSERS=1 MSIINSTALLPERUSER=""` is everyone. The pages' artwork
  (`installer/banner.bmp` and `dialog.bmp`) is made from the icon by `installer/make_bitmaps.py`;
  WiX's own bitmaps are red placeholders.
  Checked by driving the real dialogs with UI Automation: the folder followed the scope both ways,
  the just-me install put the exe, both shortcuts and the launch where expected, and a silent
  install with the shortcut off left it out. Not checked from this side: the everyone install
  itself, which needs the UAC prompt clicked - worth one manual run, then an uninstall from
  Settings to see it go from Program Files.
- **Unsigned**, so SmartScreen may warn on download — see *Code signing* below, which now has the
  build side done and is waiting on a certificate.

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
*Shipped in v0.5.0.* The Escape menu is built from real controls (`GameMenu`): clickable, with the
keyboard and gamepad moving the same highlight, and a pick never also jumping or firing. The Settings
page has the update check, screen mode (windowed, fullscreen, borderless), VSync, and master, music and
effects volume. Settings apply and save as they change, to `settings.json` beside the best times;
`GameSettings` in Core is tested for defaults, older files, damaged files and out-of-range values.
`tube play --menu` and `--settings` open straight onto them.

Still open:
- ~~**Remember the window's size and position** between launches.~~ Done: the window's place is
  saved on the way out (`WindowPlace` in the settings, from the menu's Quit and from the close
  button alike) and restored before the screen mode is applied, on whichever screen it was, with
  maximised remembered as a flag over the last plain place. A place on a screen that is no longer
  there keeps only its size. Test runs and shots leave the window alone.
- **Motion options** (shake, speed streaks, hit flash), still only if someone finds the effects too much.
- **Key rebinding**, the item below.

The plan as it was written:

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

### Code signing
*Build side done and unused. Blocked on buying a certificate, which is a decision, not work.*

Unsigned, Windows calls the game an unknown publisher: SmartScreen warns before the installer runs and
Defender offers to delete the download. The publisher shown is the certificate's subject, so the fix is
a certificate and nothing else — `Manufacturer` in `Package.wxs` only names the installed-apps entry.

Done: `tools/Build/Signing.cs` signs through `signtool` whenever a certificate is configured, and says
plainly that the output is unsigned when none is, so a working copy without one still builds. The exe is
signed during export, before the installer packages it — otherwise the copy inside the MSI stays unsigned
and players are warned when they launch the game rather than when they install it. `--signed` on either
command makes an unsigned build an error. The release workflow passes the cloud-signing variables through
from repository secrets and is inert until they exist. Checked against a throwaway self-signed
certificate: signing succeeds and verification correctly rejects an untrusted root.

Two things worth keeping in mind, both found by testing rather than reasoning:
- Godot's embedded PCK is a real PE section ending exactly at the end of the file, and a signature is
  appended after it. The section header and the PCK bytes are untouched, so signing the exe is safe — but
  only in that order: re-exporting discards the signature.
- The WiX SDK hard-links the MSI from `installer/obj` into `builds`, so the two names are one file.
  Signing in place would sign MSBuild's cached copy too, and MSBuild would hand that back as an
  up-to-date output on the next build, carrying a stale signature into a later release. `tube installer`
  gives `builds` its own copy before signing.

Still open: **which certificate**. There is no free-for-everyone code signing CA, and since June 2023 the
key has to live on a hardware token or in a cloud HSM, which decides whether CI can sign at all. SignPath
Foundation is free for qualifying open source projects; Certum's open source certificate is ~$70–100/yr;
Azure Trusted Signing is ~$10/mo. All three are OV, so a new release can still be warned about until it
builds SmartScreen reputation — only an EV certificate (~$300–500/yr, USB token, so local signing only)
avoids that wait. Once one exists: set the secrets, add `--signed` in the workflow, set `Manufacturer` to
the validated name, and check a downloaded MSI on a machine that has never seen the game.

## After v0.5.0

### Sequential level numbers, and a stable key for a best time

*Shipped.* A best time used to be keyed by the level's file name, which put the running order into
the save file: inserting a level renumbers every file after it, and each saved time would have slid
silently onto whichever level took that number. That is why Loop the Loop shipped as `8b`.

Levels now declare an `id` that never changes, `BestTimes` keys on that, and a save written by an
older build is migrated onto the ids as it is read - keeping the faster time where both forms of a
key are present, and leaving keys it does not know alone, so a save from a later build survives an
older one. With that in place the numbering is what it should be: 1 to 31, no gaps and no letters.

`LevelLoaderTests.EveryShippedLevel_DeclaresItsOwnId` holds every level to saying its id out loud,
and to each one being different.

### Locks: power-ups that have to be shot for, and apertures

*Shipped.* Two new things a key can open, both built on the `group`/`lockedBy` pair that
shoot-to-open gates already used, and a block of four levels around them (23-26, pushing Roulette
onwards to 27-31).

- **A pickup now takes a `lockedBy`.** Until its group is down the pad is dead: drawn dark with its
  label in the key's colour, and flying over it does nothing. It is the gentlest lock in the game -
  a miss costs the prize, not a shield - which is what makes it the right way to teach the idea.
- **An aperture is a ring of blades** sealing the tube that irises open as its keys fall. The loader
  cuts it into blades and each one is an ordinary locked obstacle from there, so the collision, the
  shooting and unstoppable needed no changes at all; only the view did. Blades are drawn from the
  middle of the tube outwards, shaded across their own width so a ring reads as a count of blades
  rather than a flat disc, and turn round their own sector as they open. A lip stays in the wall
  whether the ring is shut, part open or long gone.
- **A `lockedBy` naming a group nothing is in now fails to load.** Left alone it is a wall that never
  opens or a pad that never lights, and both read as a missed shot rather than as a typo.
- **`tube play --shot <file>`** saves one frame and quits, which is how the blades were checked
  without asking anyone to fly to them and describe what they saw. `levels/bench/aperture.json` is
  the bench it was aimed at.

Still open:

- **The ring gun is a skeleton key, and that is the rule.** One sweep destroys every key it
  passes, so it opens an aperture outright. Decided after play testing rather than changed: the
  levels are already built round it - charges are rationed near rings, and a ring that has to
  resist a sweep uses an ordered group, which a sweep takes one member of - and a key kind the ring
  cannot touch would be a second rule to teach for the sake of the first. The format guide says to
  treat it as deliberate.
- **Blades spin off.** A smashed blade keeps its shape and is knocked away ahead of the ship, a
  little faster than it, out towards its own wall and tumbling, shrinking away over the last of a
  flight of under a second, with the ordinary burst at the impact. It used to burst like a block.
- **A ring has a sound.** `ApertureOpened` is raised once per key that opens some of a ring, so a
  three-key ring says it three times, and the synth answers with a latch, a motor winding up
  through the swing and a settle, timed to the iris.

### Rings: a cylinder down the middle of the bore

A section with a `ringHeight` has a core: the outer wall is the floor, the core is the ceiling
hanging overhead, each wall closes on itself, and the jump is the only way between them. Sleeve
(level 10) is the first use. The decisions that shaped it:

- **Levels say how much room the ring leaves, not how big the core is.** The core is what is left,
  and a taller ring is bought by widening the bore, up to 70% of its narrow half-size.
- **Keep the room at 12, the flat section's gap.** A first pass made the last ring tighter (5 of
  room) for tension; from inside, a collar on the wall and one on the core looked the same band and
  the jump had no slack either end. Tension comes from a bigger bore with a bigger core instead.
- **A core arrives whole and stops whole**, at piece boundaries, with a flat face in the fork-wall
  material. Blending one in from nothing was a needle down the bore, whatever was done to its tip.
  Between two rings it does blend, so a change of room is a taper.
- **A full block is a collar**, `"full": true`, as wide as its wall; on the core it is the way to
  bring a player down before the core stops.
- **Mid-jump, a thing is met on its own wall.** The ship's X is converted across with the perimeter
  ratio before it is compared; the raw X had hit blocks it was nowhere near.
- **The ship keeps its place round a changing wall**, in rings and tubes alike, except through the
  funnel into a flat section.

**Agility** came out of the ring: the steering rate is a distance per second whatever the wall,
and the outer wall of a 40 bore is 250 round, so a block on the far side is simply out of reach.
The pickup multiplies the rate by 1.6 for 10 s and carries between levels like the others. It is
also handed out before the fast finishes of 29, 30 and 31, and left out of the finale, which gives
nothing on purpose.

The far end of the widest bores no longer shimmers: it was the wall seams, a fixed width in
world units and so narrower than a pixel far off, and a line thinner than a pixel flickers rather
than thins. A seam now fades out as it thins, and the far end settles into plain checker. The
hairline seam down the top of a ring's wall, noted earlier, could not be reproduced afterwards -
the strip's two ends land on the same point and the same texture coordinate - and is taken as
fixed along the way.

Still open, as design room rather than faults: collars cannot be gates or movers; the jump is a
fixed 0.55 s whatever the gap; and splits, apertures and warps are refused in a ring.

### A title screen, and a Start with no cut

The game opens on a title: its name, and Start game, Start from a zone, Best times, Settings,
Controls, About and Quit down the left, over a tube the ship is already flying. The pages open in
the Escape menu's panel. Left alone for twenty seconds the menu fades out and leaves the flying, and
whatever wakes it is swallowed so that key does nothing else. The Escape menu gained Quit to title.

**Start does not cut.** The tube behind the menu is the first level itself with a straight lead-in
in front of it (`LevelLoader.ParseWithLeadIn`). The ship holds station in the lead-in on a
treadmill: every time it has flown one wall segment it is put back one, and the walls are drawn one
index further on (`segment_offset` in the wall shader), so the picture never changes and the level
never gets nearer. Start stops putting it back and hands over the stick; where the level begins the
ship is handed to the level proper, a whole number of segments further back with the index shift to
match. `tube play --title-page start --shot` saves that instant drawn both ways, and the two files
are identical on every wall pixel.

- **Why a treadmill and not an endless straight tube.** That was the first version, and it left one
  difference: the level bends 184 units past its start, well inside the fade, so the far end of
  the tube visibly changed at the swap. The join is only invisible if what is ahead is the same,
  which means it has to be the level that is ahead.
- **The lead-in is sized from the level**: long enough that from where the ship is held, the first
  thing that is not plain straight tube is beyond the fade (`SegmentJoin.LeadIn`). For First Loop
  that is 300 units, and the run-up after Start is about three seconds, with the stick live and
  the throttle held where every level starts it so the run-up cannot be used to arrive faster.
- **Blocks are not drawn while the ship holds station.** They have no distance fade and show out to
  450, further than the walls, so the treadmill would make them jump. They come on at Start.
- **Starting from a later zone is a cut**, softened by a fade. Its bore and colours are its own.
- **A retry keeps the index shift** the level was handed, so restarting does not repaint its walls.
  First Loop's palette sequence now depends on how long the title was up; nothing reads it.
- **Zones unlock by being reached**: one with a best time, or whose predecessor has one.
- The name comes from the project's `config/name`, so renaming the game renames the title.

Still open: the Controls page is a fixed list, which key rebinding will have to feed. The title
now has a track of its own; see *Adaptive background music*. About now credits Clint Andrews with a
copyright line, and the installer's publisher and the exe's file details say the same.

### Levels that run into each other without a cut

A level line used to be a stall and a repaint: the next level was read and its track laid on the
frame the ship crossed the line, which took 60 to 270 ms depending on the level, and every wall in
sight took the new colours at once. Timed with a stopwatch before the change; the parse was a
third to a half of it, and finding the jump windows - a search along the whole track - was most of
the rest. Now:

- **The next level is read ahead**, on another thread, as soon as a level starts. Parsing and the
  jump-window search are pure sums in Core, so they can run there; only the file read stays on the
  main thread. Once read, its opening is built as scene nodes mid-level, which costs a few
  milliseconds once.
- **Its opening is drawn over the end of the current level**, in its own colours and on its own
  segment grid, since the end of a level is a copy of that opening (`LevelJoin.Handover`, 276
  units) and one rigid move lays the one over the other (`LevelJoin.Alignment`, checked for every
  join by `LevelJoinTests`). The track and obstacle renderers each hold two views for this; the next
  view sits under a rotated node so its own world lies along the end of the current one.
- **At the line the next view is kept**, not rebuilt: the same chunks, materials and indices, with
  the view's transform reset and its obstacle view pointed at the real session. The ship is put
  down exactly where it was, on the same wall, rather than at 16 in the middle of the floor. The
  line costs about 2.5 ms and changes nothing on screen; `tube play --join --shot` proves it.
- **The colours change at a wall seam 276 units ahead**, where every seam already changes the
  pattern, and the ship, streaks and HUD take the new colours as the ship crosses that seam.
- **Speed is carried too.** Every level's handover blend piece now sets the next level's start
  speed, and the join test holds speed along the whole copy; Throttle's first speed change moved
  past 276 for it. Before this, every one of the 31 lines changed speed on the spot, by up to 85 u/s.

Two loose ends closed after v0.9.1: a hit's recovery now carries across the line in the run state,
so the slowdown and the grace run their course; and the next level's view runs its movers and
gates on a clock set to the time still to go until the line, counting up to zero at the ship's
present speed, so they are already moving on the approach and are exactly where the real clock
finds them when the level starts.

### Power-ups spread, and the thin stretches filled
*After v0.9.5, from play testing with the new throttle range.*

Every level now carries at least two power-ups (First Loop had one; it has rapid fire for its
closing spiral of targets). Agility, which lived in Sleeve and the three fast finishes, is in
twelve levels: Speed Trap, Restraint, Shoal, Undertow, Scarlands, Crossfire, Attrition and
Roulette each got one placed just ahead of their quickest stretch, where three things come inside
a second and a half of travel. And the tube pieces that had nothing in them for four to eight
seconds - a survey found the longest in Speed Trap, Sleeve, Flatlands, Undertow and Earthworks -
got targets, with a couple of breakables, in fourteen levels. Left alone on purpose: Sleeve's
widening bore and the core's arrival, which are there to be looked at, and every level's
handover run-out. The survey script lives with the session notes; the rules it checks are the
ones `LevelLoaderTests` and `RunOutTests` hold, and all levels still load under them.

### Gates rising under the ship
*After v0.9.4, from play testing Clockwork: a shield lost over what looked like clear wall.*

A gate counted as solid from 85% out, judged every frame. So a gate the player had seen was down,
and flown over, cost a shield the moment it rose past that line under the ship - hidden by the ship
itself, which is why nothing was seen to be hit. The rule now: only a gate that was solid when the
ship's nose reached it is a wall. The session records when the nose reaches each gate, from where
in that frame's travel the gate's near edge lay, and a gate that goes solid after that is something
the ship is floating over. Arriving at one standing, or on its way in, still costs. The test flies
the same gate phased to rise mid-overpass (no hit) and half a second before arrival (hit), and
fails under the old rule.

## Standing constraints

Any of this has to keep three guards passing, all of which exist because something shipped wrong:

- `SplitSavingTests` — every fork saves 4% on its quick branch and spreads 10% between branches.
- The chain-cycle check in `LevelLoaderTests` — the run is a line from level 1 to the finale, every
  level reachable exactly once. A victory level would need thinking about here.
- `RunOutTests` — nothing authored in the last 260 units of a level.
