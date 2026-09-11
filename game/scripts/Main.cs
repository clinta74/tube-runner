using System.Globalization;
using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// Plays a level: loads it, runs a <see cref="GameSession"/>, and drives the track, obstacle, ship,
/// camera, HUD, effects, and audio. Everything is placed relative to the ship's track position
/// (floating origin).
/// </summary>
public partial class Main : Node3D
{
    private const string BestTimesPath = "user://best_times.json";

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
    [Export] public float BaseFov { get; set; } = 75f;
    /// <summary>Field of view at full speed effect; widening it sells the speed.</summary>
    [Export] public float MaxFov { get; set; } = 100f;

    private Level _level = null!;
    private GameSession _session = null!;
    private BestTimes _bestTimes = null!;
    // Runs started partway through (for testing) don't count toward best times.
    private bool _practice;
    private TrackRenderer _track = null!;
    private ObstacleRenderer _obstacles = null!;
    private Hud _hud = null!;
    private SpeedFx _fx = null!;
    private EngineAudio _audio = null!;
    private MeshInstance3D _ship = null!;
    private Camera3D _camera = null!;
    private float _bank;
    private float _shake;
    private float _endedFor;

    // Last frame's pose, to ease the ship across when a fork or merge moves it to another tube.
    private int _lastBranch = -1;
    private TrackSplit? _lastSplit;
    private System.Numerics.Vector2 _lastPoint;
    private System.Numerics.Vector2 _lastUp;
    private Vector3 _snapOffset;
    private Vector3 _snapUp;
    private float _snap;

    public override void _Ready()
    {
        InputSetup.Register();
        _ship = GetNode<MeshInstance3D>("Ship");
        _camera = GetNode<Camera3D>("Camera3D");
        _track = GetNode<TrackRenderer>("TrackRenderer");
        _obstacles = GetNode<ObstacleRenderer>("ObstacleRenderer");
        _hud = GetNode<Hud>("Hud");
        _fx = GetNode<SpeedFx>("SpeedFx");
        _audio = GetNode<EngineAudio>("EngineAudio");

        // For testing: `godot -- --level=res://levels/level_02.json --start=3000` plays a given
        // level, starting partway through it.
        double startS = CameraBehind + 10.0;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--level=")) LevelPath = arg["--level=".Length..];
            if (arg.StartsWith("--start="))
            {
                startS = double.Parse(arg["--start=".Length..], CultureInfo.InvariantCulture);
                _practice = true;
            }
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

        var settings = new SessionSettings(new ShipSettings(SteerSpeed, MaxPlaneOffset));
        // Start on the floor; by default far enough in that the camera has track behind it.
        var start = new TrackPosition(startS, Surface.Floor, 0f);
        _session = new GameSession(_level.Track, _level.Obstacles, settings, start);

        ThemeView.Apply(_level.Theme, WallMaterial, (StandardMaterial3D)_ship.MaterialOverride);
        WallMaterial.SetShaderParameter("segment_length", _level.SegmentLength);
        _track.Init(_level.Track, WallMaterial, _level.SegmentLength, _level.Theme);
        _obstacles.Init(_session, _level.Theme);
        _bestTimes = BestTimes.FromJson(FileAccess.FileExists(BestTimesPath) ? FileAccess.GetFileAsString(BestTimesPath) : null);
        _hud.Init(_level.Name, settings.Shields, _level.Theme.SeamLight.ToColor(), _bestTimes.Get(LevelId));
        _fx.SetStreakColor(_level.Theme.SeamLight.ToColor());
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
            Input.IsActionPressed(InputSetup.Fire),
            Input.GetAxis(InputSetup.ThrottleDown, InputSetup.ThrottleUp)));
        HandleEvents();
        if (_session.State != SessionState.Playing && WaitForContinue(dt)) return;

        var ship = _session.Ship;
        var pos = ship.Position;
        // Once the run is over the ship stops, so the speed effects and engine wind down.
        float speed = _session.State == SessionState.Playing ? ship.ForwardSpeed : 0f;
        _fx.Update(speed, dt);
        _audio.SetSpeed(speed);

        var track = _level.Track;
        var split = pos.Branch >= 0 ? track.SplitAt(pos.S) : null;
        var frame = FrameOnPath(pos.S);
        var origin = frame.Position;
        _track.UpdateView(pos.S, origin);
        _obstacles.UpdateView(origin, dt);
        _hud.Update(_session, dt);

        var (point, up2) = ship.Pose(RideHeight);
        var shipWorld = frame.PointOnSection(point);
        var up = frame.DirectionOnSection(up2).ToGodot();
        var forward = frame.Forward.ToGodot();

        if (pos.Branch != _lastBranch)
        {
            // A fork or merge moved the ship onto another tube; ease across from where it was.
            var before = _lastSplit is null ? track.FrameAt(pos.S) : _lastSplit.BranchFrame(track, pos.S, _lastBranch);
            _snapOffset = (before.PointOnSection(_lastPoint) - shipWorld).ToVector3().ToGodot();
            _snapUp = before.DirectionOnSection(_lastUp).ToGodot();
            _snap = 1f;
        }
        (_lastBranch, _lastSplit, _lastPoint, _lastUp) = (pos.Branch, split, point, up2);
        _snap = Mathf.Max(0f, _snap - 4f * dt);
        float ease = _snap * _snap;
        var snapOffset = _snapOffset * ease;
        up = up.Lerp(_snapUp, ease).Normalized();

        // Bank into sideways movement, and blink while recovering from a hit.
        _bank = Mathf.Lerp(_bank, _session.State == SessionState.Playing ? steer : 0f, 1f - Mathf.Exp(-8f * dt));
        var shipPos = shipWorld.RelativeTo(origin).ToGodot() + snapOffset;
        _ship.LookAtFromPosition(shipPos, shipPos + forward, up.Rotated(forward, -0.4f * _bank));
        _ship.Visible = _session.RecoveryLeft <= 0f || Mathf.PosMod(_session.RecoveryLeft * 12f, 2f) < 1f;

        // The camera follows the ship's section-space pose along its path, so it stays level on
        // open planes and rolls with the ship around tubes and through jumps.
        var camPos = FrameOnPath(pos.S - CameraBehind)
            .PointOnSection(point + up2 * (CameraHeight - RideHeight)).RelativeTo(origin).ToGodot() + snapOffset;
        var target = FrameOnPath(pos.S + LookAhead)
            .PointOnSection(point + up2 * 0.4f).RelativeTo(origin).ToGodot() + snapOffset;
        _camera.LookAtFromPosition(camPos, target, up);
        _camera.Fov = Mathf.Lerp(BaseFov, MaxFov, _fx.Intensity);
        Shake(dt);

        // Frames along the ship's own path: its branch inside a split, otherwise the centerline.
        TrackFrame FrameOnPath(double s) => split is null ? track.FrameAt(s) : split.BranchFrame(track, s, pos.Branch);
    }

    private void HandleEvents()
    {
        foreach (var e in _session.Events)
        {
            _audio.OnEvent(e);
            switch (e)
            {
                case SessionEvent.Hit:
                    _hud.Flash();
                    _shake = 1f;
                    break;
                case SessionEvent.GameOver:
                    _hud.ShowMessage("SHIELDS DOWN\nSpace or R to retry");
                    break;
                case SessionEvent.Finished:
                    _hud.ShowMessage(FinishMessage() + (_level.Next is null ? "\nSpace to play again" : "\nSpace for the next level"));
                    break;
            }
        }
    }

    private string LevelId => LevelPath.GetFile();

    // Records the level time (unless this is a practice run) and describes it against the best.
    private string FinishMessage()
    {
        float time = _session.Elapsed;
        float? best = _bestTimes.Get(LevelId);
        string result = $"LEVEL COMPLETE\nTime {time:0.00}s   ";

        if (_practice)
        {
            result += "(practice run, not recorded)";
        }
        else if (_bestTimes.Record(LevelId, time))
        {
            SaveBestTimes();
            _hud.SetBest(time);
            result += best is float previous ? $"NEW BEST!  (-{previous - time:0.00}s)" : "FIRST CLEAR";
        }
        else
        {
            result += $"Best {best:0.00}s  (+{time - best:0.00}s)";
        }
        return result + $"\nScore {_session.Score}";
    }

    private void SaveBestTimes()
    {
        using var file = FileAccess.Open(BestTimesPath, FileAccess.ModeFlags.Write);
        if (file is null)
        {
            GD.PushError($"Couldn't save best times: {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(_bestTimes.ToJson());
    }

    // Camera shake: strong after a hit, plus a faint rumble at high speed.
    private void Shake(float dt)
    {
        _shake = Mathf.Max(0f, _shake - 2f * dt);
        float amount = 0.35f * _shake * _shake + 0.04f * _fx.Intensity;
        if (amount <= 0f) return;

        var basis = _camera.GlobalTransform.Basis;
        _camera.GlobalPosition += (basis.X * (GD.Randf() * 2f - 1f) + basis.Y * (GD.Randf() * 2f - 1f)) * amount;
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
