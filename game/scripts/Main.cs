using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// Prototype scene: an endless generated track streamed by <see cref="TrackRenderer"/>.
/// Everything is placed relative to the ship's track position (floating origin).
/// </summary>
public partial class Main : Node3D
{
    [Export] public ShaderMaterial WallMaterial { get; set; } = null!;
    /// <summary>Track seed. 2 reaches an oval stretch early and a flat-plane section at ~880 units.</summary>
    [Export] public int Seed { get; set; } = 2;
    [Export] public float TubeRadius { get; set; } = 6f;
    [Export] public float ForwardSpeed { get; set; } = 80f;
    /// <summary>Units per second across the wall at full steer.</summary>
    [Export] public float SteerSpeed { get; set; } = 22f;
    /// <summary>Distance between wall seams; the pattern can change at each seam.</summary>
    [Export] public float SegmentLength { get; set; } = 60f;
    [Export] public float RideHeight { get; set; } = 0.6f;
    [Export] public float CameraHeight { get; set; } = 2.2f;
    [Export] public float CameraBehind { get; set; } = 6f;
    [Export] public float LookAhead { get; set; } = 14f;

    private readonly ProfileShapeCache _cameraShapes = new();
    private readonly ProfileShapeCache _targetShapes = new();
    private Track _track = null!;
    private ShipSim _sim = null!;
    private TrackRenderer _renderer = null!;
    private Node3D _ship = null!;
    private Camera3D _camera = null!;

    public override void _Ready()
    {
        _ship = GetNode<Node3D>("Ship");
        _camera = GetNode<Camera3D>("Camera3D");
        _renderer = GetNode<TrackRenderer>("TrackRenderer");

        var generator = new TrackGenerator(Seed, TubeRadius);
        _track = new Track(generator.Circle, generator);
        // Start on the floor (U = 0.75), far enough in that the camera has track behind it.
        var start = new TrackPosition(0, CameraBehind + 10.0, 0.75f, 0f);
        _sim = new ShipSim(new ShipSettings(ForwardSpeed, SteerSpeed), _track, start);

        WallMaterial.SetShaderParameter("segment_length", SegmentLength);
        _renderer.Init(_track, WallMaterial, SegmentLength);
    }

    public override void _Process(double delta)
    {
        float steer = Input.GetAxis("ui_left", "ui_right");
        if (Input.IsKeyPressed(Key.A)) steer -= 1f;
        if (Input.IsKeyPressed(Key.D)) steer += 1f;

        _sim.Step((float)delta, steer);
        var pos = _sim.Position;
        var origin = _track.FrameAt(pos.S).Position;
        _renderer.UpdateView(pos.S, origin);

        var frame = _track.FrameAt(pos.S);
        var shape = _sim.Shape;
        // "Up" for the ship points from the wall into the tube.
        var up = frame.DirectionOnSection(shape.InwardNormalAt(pos.U)).ToGodot();
        var shipPos = SurfacePoint(frame, shape, pos.U, RideHeight, origin);
        _ship.LookAtFromPosition(shipPos, shipPos + frame.Forward.ToGodot(), up);

        var camPos = SurfacePoint(pos.S - CameraBehind, pos.U, CameraHeight, origin, _cameraShapes);
        var target = SurfacePoint(pos.S + LookAhead, pos.U, 1f, origin, _targetShapes);
        _camera.LookAtFromPosition(camPos, target, up);
    }

    private Vector3 SurfacePoint(double s, float u, float height, Vector3d origin, ProfileShapeCache shapes) =>
        SurfacePoint(_track.FrameAt(s), shapes.Get(_track.SectionAt(s)), u, height, origin);

    private static Vector3 SurfacePoint(TrackFrame frame, ProfileShape shape, float u, float height, Vector3d origin)
    {
        var p = shape.PointAt(u) + shape.InwardNormalAt(u) * height;
        return frame.PointOnSection(p).RelativeTo(origin).ToGodot();
    }
}
