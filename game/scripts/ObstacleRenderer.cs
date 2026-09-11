using System.Collections.Generic;
using Godot;
using TubeRunner.Core;
using Theme = TubeRunner.Core.Theme;

namespace TubeRunner.Game;

/// <summary>
/// Draws a <see cref="GameSession"/>'s obstacles, shots, and target bursts. Everything is anchored
/// in world space and re-placed relative to the floating origin each frame.
/// </summary>
public partial class ObstacleRenderer : Node3D
{
    private const float BurstSeconds = 0.8f;

    private readonly Dictionary<Obstacle, View> _views = new();
    private readonly List<MeshInstance3D> _shotViews = new();
    private readonly List<Burst> _bursts = new();
    private readonly ProfileShapeCache _shapes = new();
    private GameSession _session = null!;
    private StandardMaterial3D _blockMaterial = null!;
    private StandardMaterial3D _targetMaterial = null!;
    private StandardMaterial3D _shotMaterial = null!;
    private Mesh _shotMesh = null!;
    private Mesh _burstMesh = null!;
    private float _time;

    [Export] public float ViewAhead { get; set; } = 450f;
    [Export] public float ViewBehind { get; set; } = 20f;
    [Export] public float RideHeight { get; set; } = 0.6f;

    public void Init(GameSession session, Theme theme)
    {
        _session = session;
        _blockMaterial = new StandardMaterial3D
        {
            AlbedoColor = theme.Block.ToColor(),
            Roughness = 0.5f,
            EmissionEnabled = theme.Glow > 0f,
            Emission = theme.Block.ToColor(),
            EmissionEnergyMultiplier = 0.5f * theme.Glow,
        };
        _targetMaterial = Glowing(theme.Target.ToColor(), 1.5f + theme.Glow);
        _shotMaterial = Glowing(theme.SeamLight.ToColor(), 4f);
        _shotMesh = new CapsuleMesh { Radius = 0.12f, Height = 1.4f };
        _burstMesh = new SphereMesh { Radius = 0.15f, Height = 0.3f, RadialSegments = 6, Rings = 3 };
    }

    public void UpdateView(Vector3d origin, float dt)
    {
        _time += dt;
        double s = _session.Ship.Position.S;

        foreach (var o in _session.Obstacles)
        {
            bool wanted = !o.Destroyed && o.S > s - ViewBehind && o.S < s + ViewAhead;
            if (_views.TryGetValue(o, out var view))
            {
                if (wanted)
                {
                    Place(view, origin);
                    continue;
                }
                if (o.Destroyed) SpawnBurst(view.Center, view.Node.MaterialOverride);
                view.Node.QueueFree();
                _views.Remove(o);
            }
            else if (wanted)
            {
                view = CreateView(o);
                _views[o] = view;
                Place(view, origin);
            }
        }

        UpdateShots(origin);
        UpdateBursts(origin);
    }

    private View CreateView(Obstacle o)
    {
        var (center, forward, up) = Pose(o.S, o.Surface, o.X, o.Height / 2f);
        bool target = o.Kind == ObstacleKind.Target;
        float size = Mathf.Min(o.Width, o.Height);
        Mesh mesh = target
            // A four-sided "sphere" with one ring is a diamond.
            ? new SphereMesh { Radius = size / 2f, Height = size, RadialSegments = 4, Rings = 1 }
            : new BoxMesh { Size = new Vector3(o.Width, o.Height, o.Length) };
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = target ? _targetMaterial : _blockMaterial };
        AddChild(node);
        return new View(node, center, forward, up, Spins: target);
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
        while (_shotViews.Count < shots.Count)
        {
            var node = new MeshInstance3D { Mesh = _shotMesh, MaterialOverride = _shotMaterial };
            AddChild(node);
            _shotViews.Add(node);
        }

        for (int i = 0; i < _shotViews.Count; i++)
        {
            var node = _shotViews[i];
            node.Visible = i < shots.Count;
            if (!node.Visible) continue;

            var shot = shots[i];
            var (center, forward, up) = Pose(shot.S, shot.Surface, shot.X, RideHeight + shot.Height);
            var pos = center.RelativeTo(origin).ToGodot();
            node.LookAtFromPosition(pos, pos + forward, up);
            // The capsule's long axis is Y; lay it along the track.
            node.RotateObjectLocal(Vector3.Right, Mathf.Pi / 2f);
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
    private (Vector3d Center, Vector3 Forward, Vector3 Up) Pose(double s, Surface surface, float x, float height)
    {
        var frame = _session.Track.FrameAt(s);
        var shape = _shapes.Get(_session.Track.SectionAt(s));
        var normal = shape.NormalAt(surface, x);
        var point = shape.PointAt(surface, x) + normal * height;
        return (frame.PointOnSection(point), frame.Forward.ToGodot(), frame.DirectionOnSection(normal).ToGodot());
    }

    private static StandardMaterial3D Glowing(Color color, float energy) => new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
    };

    private sealed record View(MeshInstance3D Node, Vector3d Center, Vector3 Forward, Vector3 Up, bool Spins);

    private sealed record Burst(CpuParticles3D Node, Vector3d Center, float Expires);
}
