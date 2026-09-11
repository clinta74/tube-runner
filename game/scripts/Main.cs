using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// Plays a level: loads it, streams its track through <see cref="TrackRenderer"/>, and drives the
/// ship and camera. Everything is placed relative to the ship's track position (floating origin).
/// </summary>
public partial class Main : Node3D
{
    [Export] public ShaderMaterial WallMaterial { get; set; } = null!;
    [Export(PropertyHint.File, "*.json")] public string LevelPath { get; set; } = "res://levels/level_01.json";
    /// <summary>Units per second across the surface at full steer.</summary>
    [Export] public float SteerSpeed { get; set; } = 22f;
    /// <summary>How far the ship may strafe from the center on open planes.</summary>
    [Export] public float MaxPlaneOffset { get; set; } = 40f;
    [Export] public float RideHeight { get; set; } = 0.6f;
    [Export] public float CameraHeight { get; set; } = 2.2f;
    [Export] public float CameraBehind { get; set; } = 6f;
    [Export] public float LookAhead { get; set; } = 14f;

    private Track _track = null!;
    private ShipSim _sim = null!;
    private TrackRenderer _renderer = null!;
    private MeshInstance3D _ship = null!;
    private Camera3D _camera = null!;
    private float _bank;

    public override void _Ready()
    {
        InputSetup.Register();
        _ship = GetNode<MeshInstance3D>("Ship");
        _camera = GetNode<Camera3D>("Camera3D");
        _renderer = GetNode<TrackRenderer>("TrackRenderer");

        // `godot -- --level=res://levels/level_02.json` overrides the level, for testing.
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--level=")) LevelPath = arg["--level=".Length..];
        }

        Level level;
        try
        {
            level = LevelLoader.Parse(FileAccess.GetFileAsString(LevelPath));
        }
        catch (LevelFormatException e)
        {
            GD.PushError($"{LevelPath}: {e.Message}");
            SetProcess(false);
            return;
        }

        _track = level.Track;
        // Start on the floor, far enough in that the camera has track behind it.
        var start = new TrackPosition(CameraBehind + 10.0, Surface.Floor, 0f);
        _sim = new ShipSim(new ShipSettings(level.Speed, SteerSpeed, MaxPlaneOffset), _track, start);

        ThemeView.Apply(level.Theme, WallMaterial, (StandardMaterial3D)_ship.MaterialOverride);
        WallMaterial.SetShaderParameter("segment_length", level.SegmentLength);
        _renderer.Init(_track, WallMaterial, level.SegmentLength);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        float steer = Input.GetAxis(InputSetup.SteerLeft, InputSetup.SteerRight);
        if (_sim.Position.S < _track.Length) _sim.Step(dt, steer, Input.IsActionJustPressed(InputSetup.Jump));

        var pos = _sim.Position;
        var frame = _track.FrameAt(pos.S);
        var origin = frame.Position;
        _renderer.UpdateView(pos.S, origin);

        var (point, up2) = _sim.Pose(RideHeight);
        var up = frame.DirectionOnSection(up2).ToGodot();
        var forward = frame.Forward.ToGodot();

        // Bank into sideways movement for feel.
        _bank = Mathf.Lerp(_bank, steer, 1f - Mathf.Exp(-8f * dt));
        var shipPos = frame.PointOnSection(point).RelativeTo(origin).ToGodot();
        _ship.LookAtFromPosition(shipPos, shipPos + forward, up.Rotated(forward, -0.4f * _bank));

        // The camera follows the ship's section-space pose, so it stays level on open planes
        // and rolls with the ship around tubes and through jumps.
        var camPos = _track.FrameAt(pos.S - CameraBehind)
            .PointOnSection(point + up2 * (CameraHeight - RideHeight)).RelativeTo(origin).ToGodot();
        var target = _track.FrameAt(pos.S + LookAhead)
            .PointOnSection(point + up2 * 0.4f).RelativeTo(origin).ToGodot();
        _camera.LookAtFromPosition(camPos, target, up);
    }
}
