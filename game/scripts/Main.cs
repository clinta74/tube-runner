using System.Collections.Generic;
using System.Globalization;
using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// Plays a run: levels follow one another with no pause, carrying shields, power-ups, and score
/// between them, and each level's time is kept as a split. Drives the track, obstacle, ship, camera,
/// HUD, effects, and audio. Everything is placed relative to the ship (floating origin).
/// </summary>
public partial class Main : Node3D
{
    private const string BestTimesPath = "user://best_times.json";
    // Best-times key for a whole run; level keys are file names, so it can't collide.
    private const string RunTimeKey = "run.total";

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

    private readonly List<(string Level, float Time)> _splits = new();
    private BestTimes _bestTimes = null!;
    private Level _level = null!;
    private GameSession _session = null!;
    private TrackRenderer _track = null!;
    private ObstacleRenderer _obstacles = null!;
    private Hud _hud = null!;
    private SpeedFx _fx = null!;
    private EngineAudio _audio = null!;
    private ShipView _ship = null!;
    private Camera3D _camera = null!;

    // The level the run starts from, and the state the current level began with (for a retry).
    private string _runStart = "";
    private RunState? _levelEntry;
    private double _startS;
    private double _levelStart;
    private bool _practice;
    private bool _running;
    private bool _levelDone;
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

    private string LevelId => LevelPath.GetFile();

    private float SplitTotal
    {
        get
        {
            float total = 0f;
            foreach (var (_, time) in _splits) total += time;
            return total;
        }
    }

    public override void _Ready()
    {
        InputSetup.Register();
        _ship = GetNode<ShipView>("Ship");
        _camera = GetNode<Camera3D>("Camera3D");
        _track = GetNode<TrackRenderer>("TrackRenderer");
        _obstacles = GetNode<ObstacleRenderer>("ObstacleRenderer");
        _hud = GetNode<Hud>("Hud");
        _fx = GetNode<SpeedFx>("SpeedFx");
        _audio = GetNode<EngineAudio>("EngineAudio");

        // For testing: `godot -- --level=res://levels/level_05.json --start=3000` plays a given
        // level, starting partway through it. It only applies to the level the run begins on.
        _startS = CameraBehind + 10.0;
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--level=")) LevelPath = arg["--level=".Length..];
            if (arg.StartsWith("--start="))
            {
                _startS = double.Parse(arg["--start=".Length..], CultureInfo.InvariantCulture);
                _practice = true;
            }
        }

        _bestTimes = BestTimes.FromJson(FileAccess.FileExists(BestTimesPath) ? FileAccess.GetFileAsString(BestTimesPath) : null);
        _runStart = LevelPath;
        LoadLevel(LevelPath, carry: null, startS: _startS);

        // A test run with --start skips the start screen, so recordings and quick checks just go.
        _running = _practice;
        if (!_running) _hud.ShowMessage(StartScreen);
    }

    private const string StartScreen = """
        TUBE RUNNER

        Steer            A / D   or   left / right
        Speed up, slow   W / S   or   up / down
        Jump             Space   (on flat sections)
        Fire             Ctrl / J / Enter / left mouse
        Ring gun         E / K / right mouse
        Retry level      R

        Gamepad: stick to steer and set speed, A to jump, X to fire, Y for the ring gun

        Space to start
        """;

    private void LoadLevel(string path, RunState? carry, double? startS = null)
    {
        LevelPath = path;
        Level level;
        try
        {
            level = LevelLoader.Parse(FileAccess.GetFileAsString(path));
        }
        catch (LevelFormatException e)
        {
            GD.PushError($"{path}: {e.Message}");
            _hud.ShowMessage($"Can't load {path}\n{e.Message}");
            SetProcess(false);
            return;
        }

        _level = level;
        _levelEntry = carry;
        _levelDone = false;
        _endedFor = 0f;
        _snap = 0f;
        _lastBranch = -1;
        _lastSplit = null;

        var settings = new SessionSettings(new ShipSettings(SteerSpeed, MaxPlaneOffset));
        // Start on the floor; by default far enough in that the camera has track behind it.
        _levelStart = startS ?? CameraBehind + 10.0;
        var start = new TrackPosition(_levelStart, Surface.Floor, 0f);
        _session = new GameSession(level.Track, level.Obstacles, settings, start, level.Pickups, carry, level.Warps);

        ThemeView.Apply(level.Theme, WallMaterial);
        _ship.ApplyTheme(level.Theme);
        WallMaterial.SetShaderParameter("segment_length", level.SegmentLength);
        _track.Reset();
        _track.Init(level.Track, WallMaterial, level.SegmentLength, level.Theme);
        _obstacles.Reset();
        _obstacles.Init(_session, level.Theme);
        _fx.SetStreakColor(level.Theme.SeamLight.ToColor());
        _hud.Init(level.Name, _session.MaxShields, level.Theme.SeamLight.ToColor(), _bestTimes.Get(LevelId));
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        if (Input.IsActionJustPressed(InputSetup.Restart))
        {
            LoadLevel(LevelPath, _levelEntry, _levelStart);
            DrawWorld(dt, steer: 0f);
            return;
        }

        // The start screen sits over the level until the player is ready.
        if (!_running)
        {
            if (!Input.IsActionJustPressed(InputSetup.Jump) && !Input.IsActionJustPressed(InputSetup.Fire))
            {
                DrawWorld(dt, steer: 0f);
                return;
            }
            _running = true;
            _hud.ShowMessage("");
        }

        float steer = Input.GetAxis(InputSetup.SteerLeft, InputSetup.SteerRight);
        _session.Step(dt, new ShipInput(
            steer,
            Input.IsActionJustPressed(InputSetup.Jump),
            Input.IsActionPressed(InputSetup.Fire),
            Input.GetAxis(InputSetup.ThrottleDown, InputSetup.ThrottleUp),
            Input.IsActionJustPressed(InputSetup.Special)));

        // The next level may have just loaded; draw its first frame so none goes out blank.
        if (HandleEvents())
        {
            DrawWorld(dt, steer: 0f);
            return;
        }
        if (_session.State != SessionState.Playing && WaitForContinue(dt)) return;

        DrawWorld(dt, steer);
    }

    private void DrawWorld(float dt, float steer)
    {
        var ship = _session.Ship;
        var pos = ship.Position;
        // Before the start and after the run, the ship stands still, so effects and engine wind down.
        float speed = _running && _session.State == SessionState.Playing ? ship.ForwardSpeed : 0f;
        _fx.Update(speed, dt);
        _audio.SetSpeed(speed);

        var track = _level.Track;
        var split = pos.Branch >= 0 ? track.SplitAt(pos.S) : null;
        var frame = FrameOnPath(pos.S);
        var origin = frame.Position;
        _track.UpdateView(pos.S, origin);
        _obstacles.UpdateView(origin, dt);
        _hud.Update(_session, dt, SplitTotal);

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

        // Bank into sideways movement; the ship itself handles blinking, engines, and the hot hull.
        _bank = Mathf.Lerp(_bank, steer, 1f - Mathf.Exp(-8f * dt));
        var shipPos = shipWorld.RelativeTo(origin).ToGodot() + snapOffset;
        _ship.LookAtFromPosition(shipPos, shipPos + forward, up.Rotated(forward, -0.4f * _bank));
        _ship.UpdateState(dt, _bank, _session.Ship.Throttle, _fx.Intensity, _session.RamLeft, _session.RecoveryLeft);

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

    /// <returns>True if the next level loaded, so this frame's drawing should be skipped.</returns>
    private bool HandleEvents()
    {
        foreach (var e in _session.Events)
        {
            _audio.OnEvent(e);
            switch (e)
            {
                case SessionEvent.Fired:
                    _ship.Fire();
                    break;
                case SessionEvent.Warped:
                    // No shield lost; the cost is the ground you have to cover again.
                    _hud.Callout("WARPED BACK");
                    _shake = 1f;
                    break;
                case SessionEvent.Hit:
                    _hud.Flash();
                    _shake = 1f;
                    break;
                case SessionEvent.ShieldRestored:
                    _hud.Callout("SHIELD +1");
                    break;
                case SessionEvent.ShieldsRefilled:
                    _hud.Callout("SHIELDS FULL");
                    break;
                case SessionEvent.ShieldSlotAdded:
                    _hud.Callout("EXTRA SHIELD SLOT");
                    break;
                case SessionEvent.RapidFireStarted:
                    _hud.Callout("RAPID FIRE");
                    break;
                case SessionEvent.RingGunCharged:
                    _hud.Callout($"RING GUN x{_session.RingCharges}");
                    break;
                case SessionEvent.UnstoppableStarted:
                    _hud.Callout("UNSTOPPABLE");
                    break;
                case SessionEvent.Finished:
                    _levelDone = true;
                    break;
                case SessionEvent.GameOver:
                    _hud.ShowMessage($"SHIELDS DOWN\n{Summary()}\nSpace to run it again   R to retry this level");
                    break;
            }
        }
        return _levelDone && FinishLevel();
    }

    // A level ended: keep its split, then roll straight into the next one with no pause.
    private bool FinishLevel()
    {
        _levelDone = false;
        float time = _session.Elapsed;
        _splits.Add((_level.Name, time));
        bool best = !_practice && _bestTimes.Record(LevelId, time);

        if (_level.Next is null)
        {
            if (!_practice) _bestTimes.Record(RunTimeKey, SplitTotal);
            if (!_practice) SaveBestTimes();
            _hud.ShowMessage($"RUN COMPLETE\n{Summary()}\nSpace to run it again");
            return false;
        }

        if (best) SaveBestTimes();
        _hud.Callout($"{_level.Name.ToUpperInvariant()}   {time:0.00}s{(best ? "   NEW BEST" : "")}");
        LoadLevel(LevelPath.GetBaseDir().PathJoin(_level.Next), _session.Carry);
        return true;
    }

    // The run's splits, with its total and the best total to beat.
    private string Summary()
    {
        var lines = new List<string>();
        foreach (var (level, time) in _splits) lines.Add($"{level}   {time:0.00}s");
        string bestRun = _bestTimes.Get(RunTimeKey) is float best ? $"   (best {best:0.00}s)" : "";
        lines.Add($"TOTAL   {SplitTotal:0.00}s{bestRun}");
        return string.Join("\n", lines);
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

    // After a run ends, jump or fire starts a fresh run from the level it began with.
    private bool WaitForContinue(float dt)
    {
        _endedFor += dt;
        if (_endedFor < 1f) return false;
        if (!Input.IsActionJustPressed(InputSetup.Jump) && !Input.IsActionJustPressed(InputSetup.Fire)) return false;

        _splits.Clear();
        LoadLevel(_runStart, carry: null, startS: _startS);
        return true;
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
}
