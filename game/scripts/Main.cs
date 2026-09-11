using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// Plays a level: loads it, runs a <see cref="GameSession"/>, and drives the track, obstacle, ship,
/// camera, and HUD views. Everything is placed relative to the ship's track position (floating origin).
/// </summary>
public partial class Main : Node3D
{
    // Set before reloading the scene to play a different level (retry or next).
    private static string? s_levelOverride;

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

    private Level _level = null!;
    private GameSession _session = null!;
    private TrackRenderer _track = null!;
    private ObstacleRenderer _obstacles = null!;
    private Hud _hud = null!;
    private MeshInstance3D _ship = null!;
    private Camera3D _camera = null!;
    private float _bank;
    private float _endedFor;

    public override void _Ready()
    {
        InputSetup.Register();
        _ship = GetNode<MeshInstance3D>("Ship");
        _camera = GetNode<Camera3D>("Camera3D");
        _track = GetNode<TrackRenderer>("TrackRenderer");
        _obstacles = GetNode<ObstacleRenderer>("ObstacleRenderer");
        _hud = GetNode<Hud>("Hud");

        // `godot -- --level=res://levels/level_02.json` overrides the level, for testing.
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--level=")) LevelPath = arg["--level=".Length..];
        }
        if (s_levelOverride is not null) LevelPath = s_levelOverride;

        try
        {
            _level = LevelLoader.Parse(FileAccess.GetFileAsString(LevelPath));
        }
        catch (LevelFormatException e)
        {
            GD.PushError($"{LevelPath}: {e.Message}");
            _hud.ShowMessage($"Can't load {LevelPath}\n{e.Message}");
            SetProcess(false);
            return;
        }

        var settings = new SessionSettings(new ShipSettings(_level.Speed, SteerSpeed, MaxPlaneOffset));
        // Start on the floor, far enough in that the camera has track behind it.
        var start = new TrackPosition(CameraBehind + 10.0, Surface.Floor, 0f);
        _session = new GameSession(_level.Track, _level.Obstacles, settings, start);

        ThemeView.Apply(_level.Theme, WallMaterial, (StandardMaterial3D)_ship.MaterialOverride);
        WallMaterial.SetShaderParameter("segment_length", _level.SegmentLength);
        _track.Init(_level.Track, WallMaterial, _level.SegmentLength);
        _obstacles.Init(_session, _level.Theme);
        _hud.Init(_level.Name, settings.Shields, _level.Theme.SeamLight.ToColor());
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Input.IsActionJustPressed(InputSetup.Restart))
        {
            Play(LevelPath);
            return;
        }

        float steer = Input.GetAxis(InputSetup.SteerLeft, InputSetup.SteerRight);
        _session.Step(dt, new ShipInput(
            steer,
            Input.IsActionJustPressed(InputSetup.Jump),
            Input.IsActionPressed(InputSetup.Fire)));
        HandleEvents();
        if (_session.State != SessionState.Playing && WaitForContinue(dt)) return;

        var ship = _session.Ship;
        var pos = ship.Position;
        var frame = _level.Track.FrameAt(pos.S);
        var origin = frame.Position;
        _track.UpdateView(pos.S, origin);
        _obstacles.UpdateView(origin, dt);
        _hud.Update(_session, dt);

        var (point, up2) = ship.Pose(RideHeight);
        var up = frame.DirectionOnSection(up2).ToGodot();
        var forward = frame.Forward.ToGodot();

        // Bank into sideways movement, and blink while recovering from a hit.
        _bank = Mathf.Lerp(_bank, _session.State == SessionState.Playing ? steer : 0f, 1f - Mathf.Exp(-8f * dt));
        var shipPos = frame.PointOnSection(point).RelativeTo(origin).ToGodot();
        _ship.LookAtFromPosition(shipPos, shipPos + forward, up.Rotated(forward, -0.4f * _bank));
        _ship.Visible = _session.RecoveryLeft <= 0f || Mathf.PosMod(_session.RecoveryLeft * 12f, 2f) < 1f;

        // The camera follows the ship's section-space pose, so it stays level on open planes
        // and rolls with the ship around tubes and through jumps.
        var camPos = _level.Track.FrameAt(pos.S - CameraBehind)
            .PointOnSection(point + up2 * (CameraHeight - RideHeight)).RelativeTo(origin).ToGodot();
        var target = _level.Track.FrameAt(pos.S + LookAhead)
            .PointOnSection(point + up2 * 0.4f).RelativeTo(origin).ToGodot();
        _camera.LookAtFromPosition(camPos, target, up);
    }

    private void HandleEvents()
    {
        foreach (var e in _session.Events)
        {
            switch (e)
            {
                case SessionEvent.Hit:
                    _hud.Flash();
                    break;
                case SessionEvent.GameOver:
                    _hud.ShowMessage("SHIELDS DOWN\nSpace or R to retry");
                    break;
                case SessionEvent.Finished:
                    _hud.ShowMessage(_level.Next is null
                        ? $"LEVEL COMPLETE\nScore {_session.Score}\nSpace to play again"
                        : $"LEVEL COMPLETE\nScore {_session.Score}\nSpace for the next level");
                    break;
            }
        }
    }

    // After a run ends, jump or fire continues: to the next level if finished, otherwise a retry.
    private bool WaitForContinue(float dt)
    {
        _endedFor += dt;
        if (_endedFor < 1f) return false;
        if (!Input.IsActionJustPressed(InputSetup.Jump) && !Input.IsActionJustPressed(InputSetup.Fire)) return false;

        bool next = _session.State == SessionState.Finished && _level.Next is not null;
        Play(next ? LevelPath.GetBaseDir().PathJoin(_level.Next!) : LevelPath);
        return true;
    }

    private void Play(string levelPath)
    {
        s_levelOverride = levelPath;
        GetTree().ReloadCurrentScene();
    }
}
