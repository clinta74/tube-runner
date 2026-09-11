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
    };

    private readonly Dictionary<Obstacle, View> _views = new();
    private readonly Dictionary<Pickup, View> _pickupViews = new();
    private readonly Dictionary<PickupKind, StandardMaterial3D> _pickupMaterials = new();
    private readonly List<MeshInstance3D> _shotViews = new();
    private readonly List<MeshInstance3D> _ringViews = new();
    private readonly List<Burst> _bursts = new();
    private readonly ProfileShapeCache _shapes = new();
    private GameSession _session = null!;
    private Func<Obstacle, View> _createObstacle = null!;
    private Func<Pickup, View> _createPickup = null!;
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

    public void Init(GameSession session, Theme theme)
    {
        _session = session;
        _createObstacle = CreateObstacleView;
        _createPickup = CreatePickupView;
        _glow = theme.Glow;
        _breakable = theme.Breakable.ToColor();

        _blockMaterial = Solid(theme.Block.ToColor());
        _targetMaterial = Glowing(theme.Target.ToColor(), 1.5f + theme.Glow);
        _shotMaterial = Glowing(theme.SeamLight.ToColor(), 4f);
        _ringMaterial = Glowing(new Color(1f, 0.3f, 1f), 4f);
        _ringMaterial.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        foreach (var (kind, look) in PickupLooks) _pickupMaterials[kind] = Glowing(look.Color, 2.5f);

        _shotMesh = new CapsuleMesh { Radius = 0.12f, Height = 1.4f };
        _burstMesh = new SphereMesh { Radius = 0.15f, Height = 0.3f, RadialSegments = 6, Rings = 3 };
        // A unit ring in the XZ plane, stretched to each section's size.
        _ringMesh = new TorusMesh { InnerRadius = 0.94f, OuterRadius = 1f, Rings = 64, RingSegments = 6 };
        _padMesh = new CylinderMesh { TopRadius = 1.5f, BottomRadius = 1.5f, Height = 0.12f, RadialSegments = 24 };
    }

    public void UpdateView(Vector3d origin, float dt)
    {
        _time += dt;
        double s = _session.Ship.Position.S;

        foreach (var o in _session.Obstacles) Sync(_views, o, !o.Destroyed && InView(o.S, s), o.Destroyed, _createObstacle, origin);
        foreach (var p in _session.Pickups) Sync(_pickupViews, p, !p.Collected && InView(p.S, s), p.Collected, _createPickup, origin);
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
