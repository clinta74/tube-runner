using System;
using System.Collections.Generic;
using Godot;
using TubeRunner.Core;
using Theme = TubeRunner.Core.Theme;

namespace TubeRunner.Game;

/// <summary>
/// Draws a <see cref="GameSession"/>'s obstacles, power-ups, shots, ring shots, and bursts. Things
/// are anchored in world space and re-placed relative to the floating origin each frame. Power-ups
/// and breakable blocks use placeholder looks until they get real art.
/// </summary>
public partial class ObstacleRenderer : Node3D
{
    private const float BurstSeconds = 0.8f;

    // Placeholder power-up looks: pad color and floating label.
    private static readonly Dictionary<PickupKind, (Color Color, string Label)> PickupLooks = new()
    {
        [PickupKind.Shield] = (new Color(0.3f, 1f, 0.45f), "+1"),
        [PickupKind.FullShields] = (new Color(0.2f, 0.9f, 1f), "MAX"),
        [PickupKind.ShieldSlot] = (new Color(1f, 0.85f, 0.3f), "SLOT"),
        [PickupKind.RapidFire] = (new Color(1f, 0.5f, 0.15f), "RAPID"),
        [PickupKind.RingGun] = (new Color(1f, 0.3f, 1f), "RING"),
        [PickupKind.Unstoppable] = (new Color(1f, 0.2f, 0.2f), "RAM"),
    };

    // Warning signs stand these far back up the track from a warp mouth.
    private static readonly float[] SignDistances = { 55f, 110f, 170f };

    private readonly Dictionary<Obstacle, View> _views = new();
    private readonly Dictionary<Pickup, View> _pickupViews = new();
    private readonly Dictionary<Warp, View> _warpViews = new();
    private readonly Dictionary<(Warp Warp, int Index), View> _signViews = new();
    private readonly Dictionary<PickupKind, StandardMaterial3D> _pickupMaterials = new();
    private readonly List<MeshInstance3D> _shotViews = new();
    private readonly List<MeshInstance3D> _ringViews = new();
    private readonly List<Burst> _bursts = new();
    private readonly ProfileShapeCache _shapes = new();
    private GameSession _session = null!;
    private Func<Obstacle, View> _createObstacle = null!;
    private Func<Pickup, View> _createPickup = null!;
    private Func<Warp, View> _createWarp = null!;
    private Func<(Warp Warp, int Index), View> _createSign = null!;
    private StandardMaterial3D _voidMaterial = null!;
    private StandardMaterial3D _throatMaterial = null!;
    private StandardMaterial3D _rimMaterial = null!;
    private StandardMaterial3D _signMaterial = null!;
    private Mesh _mouthMesh = null!;
    private Mesh _throatMesh = null!;
    private Mesh _rimMesh = null!;
    private Mesh _signMesh = null!;
    private StandardMaterial3D _blockMaterial = null!;
    private StandardMaterial3D _targetMaterial = null!;
    private StandardMaterial3D _shotMaterial = null!;
    private StandardMaterial3D _ringMaterial = null!;
    private Color _breakable;
    private float _glow;
    private Mesh _shotMesh = null!;
    private Mesh _burstMesh = null!;
    private Mesh _ringMesh = null!;
    private Mesh _padMesh = null!;
    private float _time;

    [Export] public float ViewAhead { get; set; } = 450f;
    [Export] public float ViewBehind { get; set; } = 20f;
    [Export] public float RideHeight { get; set; } = 0.6f;

    /// <summary>Frees the current level's views, ready for <see cref="Init"/> with the next one.</summary>
    public void Reset()
    {
        foreach (var view in _views.Values) view.Node.QueueFree();
        foreach (var view in _pickupViews.Values) view.Node.QueueFree();
        foreach (var view in _warpViews.Values) view.Node.QueueFree();
        foreach (var view in _signViews.Values) view.Node.QueueFree();
        foreach (var node in _shotViews) node.QueueFree();
        foreach (var node in _ringViews) node.QueueFree();
        foreach (var burst in _bursts) burst.Node.QueueFree();
        _views.Clear();
        _pickupViews.Clear();
        _warpViews.Clear();
        _signViews.Clear();
        _shotViews.Clear();
        _ringViews.Clear();
        _bursts.Clear();
    }

    public void Init(GameSession session, Theme theme)
    {
        _session = session;
        _createObstacle = CreateObstacleView;
        _createPickup = CreatePickupView;
        _createWarp = CreateWarpView;
        _createSign = CreateSignView;
        _glow = theme.Glow;
        _breakable = theme.Breakable.ToColor();

        _blockMaterial = Solid(theme.Block.ToColor());
        _targetMaterial = Glowing(theme.Target.ToColor(), 1.5f + theme.Glow);
        _shotMaterial = Glowing(theme.SeamLight.ToColor(), 4f);
        _ringMaterial = Glowing(new Color(1f, 0.3f, 1f), 4f);
        _ringMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        foreach (var (kind, look) in PickupLooks) _pickupMaterials[kind] = Glowing(look.Color, 2.5f);

        // A warp mouth is a hole, so it is unlit and near black however the level is lit; the rim
        // and the signs leading up to it are the only bright parts.
        _voidMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.01f, 0.01f, 0.02f),
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        // The throat is lit, unlike the void behind it: catching the headlight is what shows the
        // wall turning inwards, so the black reads as depth rather than a decal.
        _throatMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.2f, 0.21f, 0.25f),
            Roughness = 0.6f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _rimMaterial = Glowing(new Color(1f, 0.5f, 0.08f), 2.5f);
        _signMaterial = Glowing(new Color(1f, 0.72f, 0.1f), 2f);
        _mouthMesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = 1f, RadialSegments = 24 };
        _throatMesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 0.72f, Height = 1f, RadialSegments = 24 };
        _rimMesh = new TorusMesh { InnerRadius = 0.74f, OuterRadius = 1f, Rings = 32, RingSegments = 8 };
        _signMesh = new BoxMesh { Size = new Vector3(2.1f, 1.3f, 0.12f) };

        _shotMesh = new CapsuleMesh { Radius = 0.12f, Height = 1.4f };
        _burstMesh = new SphereMesh { Radius = 0.15f, Height = 0.3f, RadialSegments = 6, Rings = 3 };
        // A unit ring in the XZ plane, stretched to each section's size.
        _ringMesh = new TorusMesh { InnerRadius = 0.94f, OuterRadius = 1f, Rings = 64, RingSegments = 6 };
        _padMesh = new CylinderMesh { TopRadius = 1.5f, BottomRadius = 1.5f, Height = 0.12f, RadialSegments = 24 };
    }

    public void UpdateView(Vector3d origin, float dt)
    {
        _time += dt;
        // From the cockpit a warp is only ever seen edge-on, so its lit rim is the whole read.
        // Pulsing it is what makes the gash catch the eye at speed.
        _rimMaterial.EmissionEnergyMultiplier = 3.4f + 1.8f * MathF.Sin(_time * 4f);
        double s = _session.Ship.Position.S;

        foreach (var o in _session.Obstacles) Sync(_views, o, !o.Destroyed && InView(o.S, s), o.Destroyed, _createObstacle, origin);
        foreach (var p in _session.Pickups) Sync(_pickupViews, p, !p.Collected && InView(p.S, s), p.Collected, _createPickup, origin);
        foreach (var w in _session.Warps)
        {
            // The mouth stays whether or not it has fired; it is a hole in the wall, not a pickup.
            Sync(_warpViews, w, InView(w.S, s), burst: false, _createWarp, origin);
            for (int i = 0; i < SignDistances.Length; i++)
            {
                Sync(_signViews, (w, i), InView(w.S - SignDistances[i], s), burst: false, _createSign, origin);
            }
        }
        foreach (var (o, view) in _views)
        {
            if (o.Hits > 0 && view.HitsShown != o.HitsTaken) ShowDamage(o, view);
        }

        UpdateShots(origin);
        UpdateRings(origin);
        UpdateBursts(origin);
    }

    private bool InView(double at, double s) => at > s - ViewBehind && at < s + ViewAhead;

    // Creates, places, or removes the view for one item. Items removed because they were broken or
    // collected burst apart.
    private void Sync<T>(Dictionary<T, View> views, T item, bool wanted, bool burst, Func<T, View> create, Vector3d origin)
        where T : notnull
    {
        if (views.TryGetValue(item, out var view))
        {
            if (wanted)
            {
                Place(view, origin);
                return;
            }
            if (burst) SpawnBurst(view.Center, view.BurstMaterial);
            view.Node.QueueFree();
            views.Remove(item);
        }
        else if (wanted)
        {
            view = create(item);
            views[item] = view;
            Place(view, origin);
        }
    }

    private View CreateObstacleView(Obstacle o)
    {
        var (center, forward, up) = Pose(o.S, o.Branch, o.Surface, o.X, o.Height / 2f);
        bool target = o.Kind == ObstacleKind.Target;
        float size = Mathf.Min(o.Width, o.Height);
        Mesh mesh = target
            // A four-sided "sphere" with one ring is a diamond.
            ? new SphereMesh { Radius = size / 2f, Height = size, RadialSegments = 4, Rings = 1 }
            : new BoxMesh { Size = new Vector3(o.Width, o.Height, o.Length) };
        // Breakable blocks get their own material so each can darken as it takes hits.
        var material = target ? _targetMaterial : o.Hits > 0 ? Solid(_breakable) : _blockMaterial;
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
        AddChild(node);
        return new View(node, center, forward, up, Spins: target, material);
    }

    // Placeholder power-up: a glowing pad set into the surface with a floating label.
    private View CreatePickupView(Pickup p)
    {
        var (center, forward, up) = Pose(p.S, p.Branch, p.Surface, p.X, 0.06f);
        var (color, text) = PickupLooks[p.Kind];
        var material = _pickupMaterials[p.Kind];
        var pad = new MeshInstance3D { Mesh = _padMesh, MaterialOverride = material };
        pad.AddChild(new Label3D
        {
            Text = text,
            Position = new Vector3(0f, 1.4f, 0f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 72,
            PixelSize = 0.012f,
            OutlineSize = 16,
            Modulate = color,
        });
        AddChild(pad);
        return new View(pad, center, forward, up, Spins: true, material);
    }

    // The mouth of a side tube at right angles to the track, opening into black. The bore sinks into
    // the wall so it reads as a hole, and a lit rim keeps it from looking like a shadow.
    private View CreateWarpView(Warp w)
    {
        var (center, forward, up) = Pose(w.S, w.Branch, w.Surface, w.X, 0f);
        var node = new Node3D();

        // Place points the node's +Y along the surface normal and -Z down the track, so X is the
        // opening across the surface, Z is its extent along the track, and Y is depth into the
        // wall. Everything below y = 0 sits inside the wall rather than standing on it.
        float halfWidth = w.Width / 2f;
        float halfLength = w.Length / 2f;
        const float inset = 1.1f;
        const float depth = 2.5f;

        // A lit throat turns in from the rim before the black starts, which is all the depth that
        // survives being seen from inside the tube.
        node.AddChild(new MeshInstance3D
        {
            Mesh = _throatMesh,
            MaterialOverride = _throatMaterial,
            Scale = new Vector3(halfWidth, inset, halfLength),
            Position = new Vector3(0f, -inset / 2f, 0f),
        });
        node.AddChild(new MeshInstance3D
        {
            Mesh = _mouthMesh,
            MaterialOverride = _voidMaterial,
            Scale = new Vector3(halfWidth * 0.78f, depth, halfLength * 0.93f),
            Position = new Vector3(0f, -inset - depth / 2f, 0f),
        });
        // A torus lies in its XZ plane with its hole along Y, already the wall's normal; stretching
        // X and Z separately makes the ring match an elongated slot instead of a circle.
        node.AddChild(new MeshInstance3D
        {
            Mesh = _rimMesh,
            MaterialOverride = _rimMaterial,
            Scale = new Vector3(halfWidth, MathF.Min(halfWidth, 1.6f), halfLength),
        });

        AddChild(node);
        return new View(node, center, forward, up, Spins: false, _rimMaterial);
    }

    // A warning plate hanging off the wall, back up the track from a mouth, so the hazard is
    // telegraphed. They flank the approach on alternating sides: a sign sitting on the mouth's own
    // line would block the view of exactly the thing it is warning about.
    private View CreateSignView((Warp Warp, int Index) sign)
    {
        var w = sign.Warp;
        double s = w.S - SignDistances[sign.Index];
        float side = sign.Index % 2 == 0 ? 1f : -1f;
        float across = w.X + side * (w.Width / 2f + 5f);

        // Around a closed tube that offset wraps onto the wall; on open planes it just steps aside.
        var shape = _shapes.Get(_session.Track.SectionAt(s, w.Branch));
        var (surface, x) = shape.IsClosed ? shape.Wrap(w.Surface, across) : (w.Surface, across);
        var (center, forward, up) = Pose(s, w.Branch, surface, x, 1.4f);
        var plate = new MeshInstance3D { Mesh = _signMesh, MaterialOverride = _signMaterial };
        plate.AddChild(new Label3D
        {
            Text = "WARP",
            Position = new Vector3(0f, 0f, 0.2f),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 64,
            PixelSize = 0.012f,
            OutlineSize = 14,
            Modulate = new Color(0.12f, 0.06f, 0f),
        });
        AddChild(plate);
        return new View(plate, center, forward, up, Spins: false, _signMaterial);
    }

    // Breakable blocks darken with each hit.
    private void ShowDamage(Obstacle o, View view)
    {
        view.HitsShown = o.HitsTaken;
        ((StandardMaterial3D)view.BurstMaterial).AlbedoColor = _breakable.Darkened(0.6f * o.HitsTaken / o.Hits);
    }

    private void Place(View view, Vector3d origin)
    {
        var pos = view.Center.RelativeTo(origin).ToGodot();
        view.Node.LookAtFromPosition(pos, pos + view.Forward, view.Up);
        if (view.Spins) view.Node.RotateObjectLocal(Vector3.Up, _time * 2.5f);
    }

    private void UpdateShots(Vector3d origin)
    {
        var shots = _session.Shots;
        Grow(_shotViews, shots.Count, _shotMesh, _shotMaterial);
        for (int i = 0; i < _shotViews.Count; i++)
        {
            var node = _shotViews[i];
            node.Visible = i < shots.Count;
            if (!node.Visible) continue;

            var p = shots[i].Position;
            var (center, forward, up) = Pose(p.S, p.Branch, p.Surface, p.X, RideHeight + shots[i].Height);
            var pos = center.RelativeTo(origin).ToGodot();
            node.LookAtFromPosition(pos, pos + forward, up);
            // The capsule's long axis is Y; lay it along the track.
            node.RotateObjectLocal(Vector3.Right, Mathf.Pi / 2f);
        }
    }

    // Placeholder ring shot: a glowing ring standing across the track, sized to the section.
    private void UpdateRings(Vector3d origin)
    {
        var rings = _session.Rings;
        Grow(_ringViews, rings.Count, _ringMesh, _ringMaterial);
        for (int i = 0; i < _ringViews.Count; i++)
        {
            var node = _ringViews[i];
            node.Visible = i < rings.Count;
            if (!node.Visible) continue;

            var p = rings[i].Position;
            var frame = _session.Track.FrameAt(p.S, p.Branch);
            var section = _session.Track.SectionAt(p.S, p.Branch);
            float halfWidth = section.IsClosed ? section.HalfWidth : _session.Settings.RingReach;
            // The torus lies in its XZ plane: map X to the track's right and Z to its up.
            var basis = new Basis(frame.Right.ToGodot() * halfWidth, frame.Forward.ToGodot(), frame.Up.ToGodot() * section.HalfHeight);
            node.Transform = new Transform3D(basis, frame.Position.RelativeTo(origin).ToGodot());
        }
    }

    private void Grow(List<MeshInstance3D> pool, int count, Mesh mesh, Material material)
    {
        while (pool.Count < count)
        {
            var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material };
            AddChild(node);
            pool.Add(node);
        }
    }

    private void SpawnBurst(Vector3d center, Material material)
    {
        var particles = new CpuParticles3D
        {
            OneShot = true,
            Explosiveness = 1f,
            Amount = 32,
            Lifetime = BurstSeconds * 0.75f,
            LocalCoords = true,
            Mesh = _burstMesh,
            MaterialOverride = material,
            Spread = 180f,
            InitialVelocityMin = 6f,
            InitialVelocityMax = 14f,
            Gravity = Vector3.Zero,
            ScaleAmountMin = 0.5f,
            ScaleAmountMax = 1.2f,
        };
        AddChild(particles);
        particles.Emitting = true;
        _bursts.Add(new Burst(particles, center, _time + BurstSeconds));
    }

    private void UpdateBursts(Vector3d origin)
    {
        for (int i = _bursts.Count - 1; i >= 0; i--)
        {
            var burst = _bursts[i];
            if (_time > burst.Expires)
            {
                burst.Node.QueueFree();
                _bursts.RemoveAt(i);
                continue;
            }
            burst.Node.Position = burst.Center.RelativeTo(origin).ToGodot();
        }
    }

    // World center, forward, and up for something `height` off a track surface.
    private (Vector3d Center, Vector3 Forward, Vector3 Up) Pose(double s, int branch, Surface surface, float x, float height)
    {
        var frame = _session.Track.FrameAt(s, branch);
        var shape = _shapes.Get(_session.Track.SectionAt(s, branch));
        var normal = shape.NormalAt(surface, x);
        var point = shape.PointAt(surface, x) + normal * height;
        return (frame.PointOnSection(point), frame.Forward.ToGodot(), frame.DirectionOnSection(normal).ToGodot());
    }

    private StandardMaterial3D Solid(Color color) => new()
    {
        AlbedoColor = color,
        Roughness = 0.5f,
        EmissionEnabled = _glow > 0f,
        Emission = color,
        EmissionEnergyMultiplier = 0.5f * _glow,
    };

    private static StandardMaterial3D Glowing(Color color, float energy) => new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
    };

    private sealed record View(Node3D Node, Vector3d Center, Vector3 Forward, Vector3 Up, bool Spins, Material BurstMaterial)
    {
        public int HitsShown { get; set; }
    }

    private sealed record Burst(CpuParticles3D Node, Vector3d Center, float Expires);
}
