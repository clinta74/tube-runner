# tube-runner (working title)

A 3D high-speed PC game: pilot a ship down a tunnel at speed, dodging and shooting obstacles, choosing branches at forks, and switching between floor and ceiling where the tube flattens into two planes. The walls are multi-colored and patterned to sell the sense of speed.

Built with **Godot 4.7 (.NET)** and **C#**. See [docs/engine-evaluation.md](docs/engine-evaluation.md) for why.

## Layout

```
game/                        Godot project (open game/project.godot in the editor)
  TubeRunner.sln             Solution: game + Core + tests
  levels/                    Level files (JSON)
  scenes/  scripts/  shaders/
src/
  TubeRunner.Core/           Engine-independent sim: levels, track, cross-sections, ship (no Godot refs)
  TubeRunner.Core.Tests/     xUnit tests for Core, including a check that every level loads
docs/
```

Gameplay runs in *track space* inside `TubeRunner.Core`. The Godot project renders it and feeds it input.

## Levels

Levels are planned, not random: each is a JSON file listing track pieces (lengths, turns, cross-section
changes) and a color theme. See [docs/level-format.md](docs/level-format.md). The level played is set
by `LevelPath` on the `Main` node.

To test a section while designing, launch a level partway through it (from the repo root):

```bash
tube play --level level_02.json --start 3000
```

To see how a stretch actually looks without flying to it, add `--shot`: the game runs on for a moment,
saves one frame to that file, and quits.

```bash
tube play --level bench/aperture.json --start 370 --shot builds/shots/iris.png
```

`tube play` with nothing after it opens on the title screen; any of the options above goes straight
to the level instead. To look at the title or one of its pages, ask for it:

```bash
tube play --title --shot builds/shots/title.png
tube play --title-page times --shot builds/shots/times.png   # zones, times, controls, about, settings
```

Two more pages are for checking that the title's tube really does carry on into the first level with
nothing changing. `--title-page start` picks Start by itself, and `--title-page wrap` waits for the
ship to be put back a wall segment; with `--shot` each saves the same instant twice, as
`<name>-before.png` and `<name>.png`, drawn either side of the move. Apart from the ship's engines
the two files should be identical.

## Export a standalone build

One-time setup: install Godot's export templates (in the editor, **Editor > Manage Export Templates >
Download and Install**). Then from the repo root:

```bash
tube export
tube export --version 0.4.1   # stamps the version, so the game checks for newer releases
tube installer                # builds builds/TubeRunner-<version>.msi from the export
```

Download the templates from an ordinary terminal or the editor. A terminal inside a packaged app (the
Claude desktop app, for one) has its writes to `%APPDATA%` redirected to that app's private storage,
where nothing outside it can see them. `tube export` spots a copy stranded there and prints the command
to move it.

This writes `builds/windows/TubeRunner.exe` plus its `data_TubeRunner_windows_x86_64` folder (the game's
.NET code and a private .NET runtime, so players don't need .NET installed), and zips both into
`builds/TubeRunner-windows.zip`. Ship the whole folder or the zip; the exe won't run on its own.

The export preset is `game/export_presets.cfg`. It includes `levels/*.json`, which Godot wouldn't
package otherwise, so new level files are picked up automatically.

### Signing

Unsigned, Windows calls the game an unknown publisher: SmartScreen warns before the installer runs and
Defender may offer to delete the download. Signing is what replaces that with a publisher name — the
name on the certificate, not anything written in the installer.

That needs a certificate from a certificate authority. A self-signed one does not help, and there is no
free-for-everyone code signing CA (Let's Encrypt issues TLS certificates only). Since June 2023 the key
must also live on a hardware token or in a cloud HSM, which is what decides whether CI can sign:

| Route | Cost | Signs in CI |
|---|---|---|
| [SignPath Foundation](https://signpath.org/) — free for qualifying open source projects | free | yes |
| [Certum Open Source](https://shop.certum.eu/open-source-code-signing.html) | ~$70–100/yr | with SimplySign |
| [Azure Trusted Signing](https://learn.microsoft.com/azure/trusted-signing/) | ~$10/mo | yes |
| EV certificate on a USB token (DigiCert, Sectigo) | ~$300–500/yr | no — the token must be plugged in |

Only an EV certificate gets SmartScreen's trust immediately. The others are OV, so early downloads of a
new release can still be warned about until it builds reputation.

`tube export` and `tube installer` sign whatever they produce once a certificate is configured, and say
plainly that the output is unsigned when none is. Exactly one of these names the key:

```bash
TUBE_SIGN_THUMBPRINT=<sha1>    # a certificate in the Windows store, which is how a USB token signs
TUBE_SIGN_PFX=<file>           # with TUBE_SIGN_PFX_PASSWORD; only for a key issued before June 2023
TUBE_SIGN_DLIB=<dll>           # with TUBE_SIGN_DLIB_METADATA; how the cloud services sign, and the
                               # only one that works unattended in CI
```

The exe is signed during export, before the installer packages it — otherwise the copy inside the MSI
stays unsigned and players are warned when they launch the game rather than when they install it. Add
`--signed` to either command to fail rather than produce an unsigned build; the release workflow should
use it once its secrets are set. See `tools/Build/Signing.cs`.

`Manufacturer` in `installer/Package.wxs` only names the publisher in the installed-apps list; set it to
match the certificate once one exists.

### Releasing from GitHub

`.github/workflows/release.yml` does the same build on a Windows runner and attaches the zip to a
GitHub release, so players can download it from the repo's Releases page:

```bash
git tag v0.1.0 && git push origin v0.1.0
```

It installs Godot and its export templates itself, so nothing needs setting up on the runner. Running
it from the Actions tab (workflow_dispatch) builds the zip as a workflow artifact without publishing a
release. `.github/workflows/tests.yml` runs the tests on every push and pull request.

## Requirements
- [Godot 4.7.x **.NET** edition](https://godotengine.org/download/windows/)
- .NET 8 SDK or newer

## Build & test
```bash
dotnet build game/TubeRunner.sln
dotnet test src/TubeRunner.Core.Tests
```

## Run
Open `game/project.godot` in Godot 4.7 .NET and press **F5**, or from the repo root:

```bash
tube play
```

`tube` is the project's build tool, a small C# program in `tools/Build` (`tube help` lists its commands
and options). `tube play` builds the C# code, then runs the game (`--editor` opens the editor instead).
Use it rather than calling winget's `godot` alias directly: that alias is a symlink, and Godot .NET
launched through it can't find its .NET assemblies. Set the `GODOT` environment variable to use a
specific Godot executable.

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Steer | A/D or ←/→ | Left stick left/right |
| Speed up / slow down (0.5× to 1.75× the level's speed; holds when released) | W/S or ↑/↓ | Left stick up/down, RB/LB |
| Jump between floor and ceiling (flat sections) | Space | A |
| Fire (hold) | Ctrl, J, Enter, or left mouse | X or right trigger |
| Ring gun (needs charges) | E, K, or right mouse | Y |
| Restart level | R | Start |

Each level is timed: finish it, then try to beat your best time. Best times are saved per level in
`%APPDATA%\Godot\app_userdata\Tube Runner\best_times.json`; delete that file to reset them. Runs started
with `--start` are practice and aren't recorded.
