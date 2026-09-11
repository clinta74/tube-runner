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

To test a section while designing, launch a level partway through it:

```bash
godot --path game -- --level=res://levels/level_02.json --start=3000
```

When exporting the game, add `*.json` to the export preset's non-resource file filter so levels are included.

## Requirements
- [Godot 4.7.x **.NET** edition](https://godotengine.org/download/windows/)
- .NET 8 SDK or newer

## Build & test
```bash
dotnet build game/TubeRunner.sln
dotnet test src/TubeRunner.Core.Tests
```

## Run
Open `game/project.godot` in Godot 4.7 .NET and press **F5**.

| Action | Keyboard / mouse | Gamepad |
|---|---|---|
| Steer | A/D or ←/→ | Left stick |
| Jump between floor and ceiling (flat sections) | Space, W, or ↑ | A |
| Fire (hold) | Ctrl, J, Enter, or left mouse | X or right trigger |
| Restart level | R | Start |
