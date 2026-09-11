# Engine & Language Evaluation

## The game, in engine terms

A high-speed "tube runner": the player's ship flies forward along a path. Along the way:

- **Tube sections**: the ship moves around the inside of a cylinder. Walls are multi-colored and patterned to sell the speed.
- **Obstacles**: things to dodge and things to shoot.
- **Forks**: the tube splits, and the player picks a branch.
- **Morphs**: the tube flattens into two parallel planes (floor and ceiling). The player can jump or flip between them while still being pushed forward.

So the engine has to handle **procedural geometry**, **custom shaders**, **post-processing** (bloom, blur, chromatic aberration), **a high frame rate with low input latency**, and **quick iteration on game feel**. It does **not** need realistic physics, large open worlds, networking, or complex character animation. That makes most engines workable, and it means the language and toolchain you're comfortable with count for a lot.

---

## Key architectural idea (true for any engine)

**Run the gameplay in *track space*, not world space.**

The track is a graph of segments. Each segment has:
- a centerline spline (the path through 3D space), and
- a cross-section profile at each point along it (circle → flattened oval → two planes).

The ship's state is stored as `(segment, s, u, h)`:

| Coord | Meaning |
|---|---|
| `segment` | Which node of the track graph the ship is on |
| `s` | Distance along the segment's centerline |
| `u` | Position across the cross-section (angle around a tube, or X on a plane) |
| `h` | Offset from the surface (used for jumping, and for switching floor ↔ ceiling) |

What this gets you:
- **No tunneling at high speed.** Collision is interval math on `(s, u)`, so there's no swept 3D physics to miss a hit.
- **Forks become a graph problem.** At a branch node, the ship's `u` picks the child segment.
- **Tube ↔ plane morphs fall out naturally.** The profile function changes shape, and gameplay code doesn't care.
- **Deterministic and testable.** The sim can run headless in unit tests, and replays and seeded runs come for free.
- **The engine is mostly a renderer.** The core sim can be a plain library, which lowers engine lock-in.

Other cross-cutting concerns:
- **Floating origin**: at high speed over long runs, world coordinates lose float precision and things jitter. Keep the ship near the origin and move or rebase the world.
- **Chunked mesh streaming**: generate tube mesh chunks a few seconds ahead of the ship and recycle them behind it.
- **Fixed-timestep sim + render interpolation**: a stable sim rate (e.g. 120 Hz), with rendering running as fast as the monitor allows.
- **Sense of speed mostly comes from the shader and camera, not the actual velocity**: scrolling UV stripes and rings, hue cycling, emissive color + bloom, FOV kick, radial blur, speed-line particles, camera shake, and audio pitch/whoosh.
- **Fork junction geometry is the hardest mesh problem.** A common trick is to widen into a chamber and hide the seam behind a divider "blade" wall. SDF raymarching of the junction is an option for later.

---

## Options

### Source 2 (Valve)
- **Licensing**: Valve doesn't license Source 2 to indie developers as a standalone engine. The practical route is **s&box** (Facepunch), which runs on Source 2 with **C#** scripting.
- **Pros**: excellent renderer, C#, Hammer editor, fast hot-reload.
- **Cons**: you build on Facepunch's platform and follow their rules for distribution and standalone export. **Check the current status before committing.** The engine and tools are designed around FPS-style games. It's the smallest documentation and community pool of the realistic options, and you carry platform risk.
- **Verdict**: ❌ Not recommended for a standalone commercial PC title.

### Unreal Engine 5
- **Language**: C++ and Blueprints.
- **Licensing**: free until $1M lifetime gross per product, then a 5% royalty.
- **Pros**: the best visuals out of the box (post-processing, Niagara particles, materials editor), a production-proven pipeline, and a good fit for a high-fidelity look.
- **Cons**: heavy (large installs, slow C++ compiles, big builds). Procedural meshes (`ProceduralMeshComponent`/`DynamicMesh`) work but feel less natural than in the others. It's overkill for a stylized neon game, and iteration on game feel is slower.
- **Verdict**: ✅ Viable if near-AAA visual fidelity is the goal and you're comfortable with C++.

### Unity 6
- **Language**: C#.
- **Licensing**: the Runtime Fee was cancelled in 2024. Personal is free under a revenue cap, and Pro is a subscription above it.
- **Pros**: C# (a strong match for your .NET background), a mature editor, a huge ecosystem and asset store, URP/HDRP post-processing, Shader Graph, and easy procedural `Mesh` APIs. Plenty of games in this genre ship on Unity.
- **Cons**: closed source, a history of licensing trust issues, and a heavier editor. The editor-first workflow can clash with a code-first style.
- **Verdict**: ✅ Strong choice.

### Godot 4.x
- **Language**: C# (.NET 8+) or GDScript. C++ via GDExtension if you ever need it.
- **Licensing**: MIT. Free, no royalties, and you own everything.
- **Pros**: lightweight, fast to launch and iterate, and code-first friendly. `ArrayMesh`/`SurfaceTool` make procedural tubes easy. The shader language is simple and powerful, and built-in Glow/Bloom and environment post-processing suit a neon look. Scenes are text-based (git-friendly), and the open source means you can fix things yourself.
- **Cons**: a smaller ecosystem and asset store. Advanced rendering (heavy volumetrics, very high-end effects) lags Unreal. Some post effects (radial blur, chromatic aberration) you'll write yourself as screen-space shaders, but that's small work. C# can't export to web (irrelevant for a PC game).
- **Verdict**: ✅ **Recommended.**

### Custom engine
Possible stacks:
- **C#**: Silk.NET or MonoGame/FNA for low level, or **Stride** (an MIT-licensed C# engine).
- **C++**: SDL3 + bgfx/Vulkan/D3D12, plus Dear ImGui for tools.
- **Rust**: Bevy (ECS) or wgpu directly.

- **Pros**: total control, minimal overhead, and lots of learning. Thumper was built on a custom engine. This genre's rendering needs are narrow enough that custom is *possible*.
- **Cons**: you'd build the audio, input and controller support, UI, asset pipeline, editor and debug tools, shader hot-reload, packaging, and Steam integration yourself. That's months of work before the game gets fun.
- **Verdict**: ⚠️ Only if building the engine *is* the goal. The track-space architecture above keeps this door open: the sim is a plain library, so you could move to a custom renderer later.

---

## Comparison

| | Godot 4 (C#) | Unity 6 | Unreal 5 | s&box / Source 2 | Custom |
|---|---|---|---|---|---|
| Language | C# / GDScript | C# | C++ / Blueprints | C# | Your choice |
| Cost | Free, MIT | Free → subscription | 5% over $1M | Platform terms | Free |
| Procedural mesh | Easy | Easy | Moderate | Moderate | Easy (you write it) |
| Custom shaders / post-processing | Good | Very good | Excellent | Excellent | Whatever you build |
| Iteration speed | Excellent | Good | Fair | Very good | Depends |
| Fit to your .NET background | High | High | Low | High | High (if C#) |
| Standalone PC shipping | ✅ | ✅ | ✅ | ⚠️ Verify | ✅ |
| Lock-in risk | Lowest | Medium | Medium | High | None |
| Time to first playable | ~Days | ~Days | ~1–2 weeks | ~Days | ~Months |

---

## Recommendation

**Godot 4.x with C#**, with the core simulation in a separate, engine-independent .NET class library:

```
tube-runner/
  src/
    TubeRunner.Core/          # netX.0 class library: track graph, profiles, sim, collision (no engine refs)
    TubeRunner.Core.Tests/    # xUnit tests for the sim
  game/                       # Godot project: rendering, mesh gen, shaders, input, audio, UI
  docs/
```

Why:
1. C# builds on your existing .NET skills.
2. MIT license with no royalties and no platform risk.
3. Procedural tube generation and shader-driven walls are first-class, easy tasks in Godot.
4. The fast edit–run loop matters most for tuning game feel.
5. With the sim decoupled, switching to Unity (also C#) later would mean rewriting the rendering layer only.

**Runner-up**: Unity 6. Choose it if you want the bigger asset store and more mature tooling, and don't mind the licensing and editor weight.

---

## Suggested first milestones
1. **Track core**: spline centerline + circular profile, then `(s, u)` ship movement with unit tests.
2. **Endless tube**: chunked procedural mesh streaming, floating origin, and a basic scrolling-stripe wall shader.
3. **Speed feel pass**: FOV kick, bloom, radial blur, speed lines, camera shake, and audio.
4. **Obstacles**: dodge blocks and shootable targets, with track-space collision.
5. **Forks**: the track graph, a branch-selection mechanic, and junction geometry.
6. **Morphs**: a tube → twin-plane profile transition, with a floor ↔ ceiling jump.
7. **Content pipeline**: data-driven track definitions (JSON) and seeded procedural generation.
