# tube-runner (working title)

A 3D high-speed PC game: pilot a ship down a tunnel at speed, dodging and shooting obstacles, choosing branches at forks, and switching between floor and ceiling where the tube flattens into two planes. The walls are multi-colored and patterned to sell the sense of speed.

Built with **Godot 4.7 (.NET)** and **C#**. See [docs/engine-evaluation.md](docs/engine-evaluation.md) for why.

## Layout

```
game/                        Godot project (open game/project.godot in the editor)
  TubeRunner.sln             Solution: game + Core + tests
  scenes/  scripts/  shaders/
src/
  TubeRunner.Core/           Engine-independent sim: track generation, cross-sections, ship (no Godot refs)
  TubeRunner.Core.Tests/     xUnit tests for Core
docs/
```

Gameplay runs in *track space* inside `TubeRunner.Core`. The Godot project renders it and feeds it input.

## Requirements
- [Godot 4.7.x **.NET** edition](https://godotengine.org/download/windows/)
- .NET 8 SDK or newer

## Build & test
```bash
dotnet build game/TubeRunner.sln
dotnet test src/TubeRunner.Core.Tests
```

## Run
Open `game/project.godot` in Godot 4.7 .NET and press **F5**. Steer with **A/D** or the **arrow keys**.
