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
./play.ps1 -Level level_02.json -Start 3000
```

## Export a standalone build

One-time setup: install Godot's export templates (in the editor, **Editor > Manage Export Templates >
Download and Install**). Then from the repo root:

```bash
./export.ps1
```

This writes `builds/windows/TubeRunner.exe` plus its `data_TubeRunner_windows_x86_64` folder (the game's
.NET code and a private .NET runtime, so players don't need .NET installed), and zips both into
`builds/TubeRunner-windows.zip`. Ship the whole folder or the zip; the exe won't run on its own.

The export preset is `game/export_presets.cfg`. It includes `levels/*.json`, which Godot wouldn't
package otherwise, so new level files are picked up automatically.

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
./play.ps1
```

`play.ps1` builds the C# code, then runs the game (`-Editor` opens the editor instead). Use it rather
than calling winget's `godot` alias directly: that alias is a symlink, and Godot .NET launched through
it can't find its .NET assemblies. Set `$env:GODOT` to point the script at a specific Godot executable.

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Steer | A/D or ←/→ | Left stick left/right |
| Speed up / slow down (0.5× to 1.75× the level's speed; holds when released) | W/S or ↑/↓ | Left stick up/down, RB/LB |
| Jump between floor and ceiling (flat sections) | Space | A |
| Fire (hold) | Ctrl, J, Enter, or left mouse | X or right trigger |
| Restart level | R | Start |

Each level is timed: finish it, then try to beat your best time. Best times are saved per level in
`%APPDATA%\Godot\app_userdata\Tube Runner\best_times.json`; delete that file to reset them. Runs started
with `-Start` are practice and aren't recorded.
