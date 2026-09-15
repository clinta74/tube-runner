using System;
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

    [Export] public ShaderMaterial WallMaterial { get; set; } = null!;
    [Export(PropertyHint.File, "*.json")] public string LevelPath { get; set; } = "res://levels/level_01.json";
    /// <summary>Units per second across the surface at full steer.</summary>
    [Export] public float SteerSpeed { get; set; } = 22f;
    /// <summary>How far the ship may strafe from the center on open planes.</summary>
    [Export] public float MaxPlaneOffset { get; set; } = 40f;
    [Export] public float RideHeight { get; set; } = 0.6f;
    /// <summary>How far the ship sinks through the wall while falling down a warp mouth.</summary>
    [Export] public float DiveDepth { get; set; } = 7f;
    [Export] public float CameraHeight { get; set; } = 2.2f;
    [Export] public float CameraBehind { get; set; } = 6f;
    [Export] public float LookAhead { get; set; } = 14f;
    [Export] public float BaseFov { get; set; } = 75f;
    /// <summary>Field of view at full speed effect; widening it sells the speed.</summary>
    [Export] public float MaxFov { get; set; } = 100f;

    private readonly List<(string Level, float Time, bool Best)> _splits = new();
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
    private bool _debugSummary;
    private bool _running;
    private bool _paused;
    private bool _levelDone;
    private float _bank;
    private float _shake;
    private float _endedFor;
    private float _scrollHeld;
    private float _scrollRepeat;
    // Seconds for the camera to swing round at the end of a run, and where it ends up relative to
    // the ship: ahead of it, out to the right, and a little above.
    private const float OutroSwing = 2.2f;

    // Close in on the ship's front right quarter: near enough that it fills the frame and its detail
    // is worth having, and near enough that the view back past it does not reach the end of the
    // built world. It sits a little closer than the camera does in play.
    private const float OutroAhead = 4.5f;
    private const float OutroSide = 2.4f;
    private const float OutroLift = 1.1f;

    /// <summary>
    /// How much track the victory lap keeps behind the ship. The camera looks back at it, so what is
    /// normally off-screen history is now the whole backdrop, and chunks being freed at the usual
    /// distance would be freed in plain sight.
    /// </summary>
    private const float OutroBehind = 260f;

    /// <summary>How much track the victory lap adds at a time, and how close to the end it gets first.</summary>
    private const float OutroExtend = 2000f;

    /// <summary>
    /// Where the victory lap stops growing and starts over. The track holds a frame every unit, so
    /// an endless lap is an endless list: about 190 KB a minute at the speed it cruises. This caps
    /// it near half an hour, which is far longer than anyone reads a results screen.
    /// </summary>
    private const double OutroMaxLength = 100_000.0;

    /// <summary>
    /// How far into the victory track the ship starts. The camera looks back at it from ahead, so it
    /// needs real track behind it - starting near the beginning put the edge of the world in shot.
    /// </summary>
    private const double OutroStart = 900.0;

    /// <summary>
    /// The victory track. A fixed path rather than one beside the level that finished: a bench level
    /// in its own folder ends the same way the finale does, and there is only one victory track.
    /// </summary>
    private const string VictoryPath = "res://levels/victory.json";

    private bool _outro;
    private float _outroTime;
    private float _outroBlend;
    private int _finalScore;
    private float _finalRun;
    private float _trackViewBehind;
    private readonly List<(string Label, Action Pick)> _menu = new();
    private bool _menuOpen;
    private bool _menuConfirming;
    private int _menuIndex;
    private string _menuTitle = "PAUSED";

    // Last frame's pose, to ease the ship across when a fork or merge moves it to another tube.
    private int _lastBranch = -1;
    private TrackSplit? _lastSplit;
    private System.Numerics.Vector2 _lastPoint;
    private System.Numerics.Vector2 _lastUp;
    private Vector3 _snapOffset;
    private Vector3 _snapUp;
    private float _snap;

    private string LevelId => LevelPath.GetFile();

    /// <summary>
    /// Best-times key for a whole run, keyed by the level it started from. Level keys are file
    /// names, so this cannot collide with one. A run from level 1 and a run from level 16 are not
    /// the same race, and holding both under one key had the finish screen offering a best of 24
    /// seconds against a total of 640.
    /// </summary>
    private string RunKey => $"run.{_runStart.GetFile()}";

    private float SplitTotal
    {
        get
        {
            float total = 0f;
            foreach (var split in _splits) total += split.Time;
            return total;
        }
    }

    public override void _Ready()
    {
        InputSetup.Register();
        _ship = GetNode<ShipView>("Ship");
        _camera = GetNode<Camera3D>("Camera3D");
        _track = GetNode<TrackRenderer>("TrackRenderer");
        _trackViewBehind = _track.ViewBehind;
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
            if (arg == "--summary") _debugSummary = true;
        }

        _bestTimes = BestTimes.FromJson(FileAccess.FileExists(BestTimesPath) ? FileAccess.GetFileAsString(BestTimesPath) : null);
        _runStart = LevelPath;
        LoadLevel(LevelPath, carry: null, startS: _startS);

        // A test run with --start skips the start screen, so recordings and quick checks just go.
        _running = _practice || _debugSummary;
        if (_debugSummary) FillDebugSummary();
        else if (!_running) _hud.ShowMessage(StartScreen);
    }

    private const string StartScreen = """
        TUBE RUNNER

        Steer            A / D   or   left / right
        Speed up, slow   W / S   or   up / down
        Jump             Space   (on flat sections)
        Fire             Ctrl / J / Enter / left mouse   (one shot a press)
        Ring gun         E / K / right mouse
        Retry level      R
        Pause            P
        Menu, quit       Esc

        Gamepad: stick to steer and set speed, A to jump, X to fire, Y for the ring gun, Start for the menu

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
        // The victory lap widens this and nothing else does; put it back for an ordinary level.
        _track.ViewBehind = _trackViewBehind;
        _outro = false;
        _paused = false;
        _levelDone = false;
        _endedFor = 0f;
        _snap = 0f;
        _lastBranch = -1;
        _lastSplit = null;

        var settings = new SessionSettings(new ShipSettings(SteerSpeed, MaxPlaneOffset));
        // Start on the floor; by default far enough in that the camera has track behind it.
        _levelStart = startS ?? CameraBehind + 10.0;
        var start = new TrackPosition(_levelStart, Surface.Floor, 0f);
        _session = new GameSession(level.Track, level.Obstacles, settings, start, level.Pickups, carry, level.Warps,
            level.ThrustZones);

        ThemeView.Apply(level.Theme, WallMaterial);
        _ship.ApplyTheme(level.Theme);
        WallMaterial.SetShaderParameter("segment_length", level.SegmentLength);
        _track.Reset();
        _track.Init(level.Track, WallMaterial, level.SegmentLength, level.Theme, level.Warps, level.Next is null);
        _obstacles.Reset();
        _obstacles.Init(_session, level.Theme, JumpWindows.Find(level.Track));
        _fx.SetStreakColor(level.Theme.SeamLight.ToColor());
        _hud.Init(level.Name, _session.MaxShields, _session.ExtraShields, level.Theme.SeamLight.ToColor(),
            _bestTimes.Get(LevelId));
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        // The menu holds everything still while it is up: the session is never stepped, so a run
        // cannot end or advance behind it.
        if (Input.IsActionJustPressed(InputSetup.Menu))
        {
            // Backing out of a question returns to the menu rather than dismissing everything, so
            // Escape never means "yes" by accident.
            if (_menuConfirming) OpenMenu();
            else if (_menuOpen) CloseMenu();
            else OpenMenu();
        }
        if (_menuOpen)
        {
            UpdateMenu();
            DrawWorld(0f, steer: 0f);
            return;
        }

        // R does nothing on the victory lap. The level being flown there is the victory track, which
        // is not a level anyone can retry, and quietly turning "retry this level" into "throw the
        // finished run away" would be a nasty thing for one key to do. Space starts a new run and
        // Escape opens the menu; both say what they are.
        if (!_outro && Input.IsActionJustPressed(InputSetup.Restart))
        {
            LoadLevel(LevelPath, _levelEntry, _levelStart);
            DrawWorld(dt, steer: 0f);
            return;
        }

        // The victory lap: the ship flies itself while the results are up, and space starts a new run.
        if (_outro)
        {
            if (WaitForContinue(dt)) return;
            StepOutro(dt);
            DrawWorld(dt, steer: 0f);
            return;
        }

        // Sitting on the results screen with --summary: scroll it, draw the world behind it, and
        // never step the session. Nothing here is a real run, so there is nothing to advance.
        if (_debugSummary)
        {
            ScrollSummary(dt);
            DrawWorld(0f, steer: 0f);
            return;
        }

        // Pause holds the world still for a screenshot: the session isn't stepped and the frame is
        // drawn with no time passing, so nothing drifts or shakes. The HUD still gets the real delta,
        // so the PAUSED callout fades out of the shot instead of sitting frozen on top of it.
        if (_running && _session.State == SessionState.Playing && Input.IsActionJustPressed(InputSetup.Pause))
        {
            _paused = !_paused;
            _shake = 0f;
            if (_paused) _hud.Callout("PAUSED");
        }
        if (_paused)
        {
            _hud.Update(_session, dt, SplitTotal);
            DrawWorld(0f, steer: 0f);
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
            Input.IsActionJustPressed(InputSetup.Fire),
            Input.GetAxis(InputSetup.ThrottleDown, InputSetup.ThrottleUp),
            Input.IsActionJustPressed(InputSetup.Special),
            Input.IsActionPressed(InputSetup.Fire)));

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
        _audio.SetMusic(_session.Momentum, _session.RamLeft, _running && _session.State == SessionState.Playing);

        var track = _level.Track;
        var split = pos.Branch >= 0 ? track.SplitAt(pos.S) : null;
        var frame = FrameOnPath(pos.S);
        var origin = frame.Position;
        _track.UpdateView(pos.S, origin);
        _obstacles.UpdateView(origin, dt);
        _hud.Update(_session, dt, SplitTotal);

        var (point, up2) = ship.Pose(RideHeight);
        var forward = frame.Forward.ToGodot();

        // Down a warp well the ship sinks through the wall and tips nose-first into it. The camera
        // keeps the pose the ship had on the track, so it stays in the tube and turns to watch,
        // rather than plunging along with it - which just reads as falling.
        var (ridePoint, rideUp) = (point, up2);
        if (_session.Diving is not null)
        {
            (point, up2) = _session.DivePose(DiveDepth * _session.DiveProgress);
            var into = -frame.DirectionOnSection(up2).ToGodot();
            forward = forward.Lerp(into, _session.DiveProgress).Normalized();
        }

        var shipWorld = frame.PointOnSection(point);
        var up = frame.DirectionOnSection(up2).ToGodot();

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
        // Where the throttle sits in its own range, which is what the engines follow. Measured
        // against the ship's full range rather than whatever a thrust zone has narrowed it to, so
        // the plume and the thrust bar are reading the same thing.
        var limits = _session.Ship.Settings;
        float thrust = Mathf.InverseLerp(limits.MinThrottle, limits.MaxThrottle, _session.Ship.Throttle);
        _ship.UpdateState(dt, _bank, _session.Ship.Throttle, thrust, _fx.Intensity,
            _session.RamLeft, _session.RecoveryLeft);

        // The camera follows the ship's section-space pose along its path, so it stays level on
        // open planes and rolls with the ship around tubes and through jumps.
        var camPos = FrameOnPath(pos.S - CameraBehind)
            .PointOnSection(ridePoint + rideUp * (CameraHeight - RideHeight)).RelativeTo(origin).ToGodot() + snapOffset;
        var target = FrameOnPath(pos.S + LookAhead)
            .PointOnSection(ridePoint + rideUp * 0.4f).RelativeTo(origin).ToGodot() + snapOffset;
        // Going down a well, swing the aim onto the ship so the view tilts to follow it in. The swing
        // eases out hard, because tracking the dive at an even rate leaves the aim behind the ship the
        // whole way down and you end up watching it drop out of frame instead of following it in.
        if (_session.Diving is not null)
        {
            float left = 1f - _session.DiveProgress;
            target = target.Lerp(shipPos, 1f - left * left * left);
        }

        // The victory lap swings the camera round to the ship's front right and holds it there,
        // eased in from wherever the run left it rather than cutting.
        if (_outro)
        {
            var over = FrameOnPath(pos.S + OutroAhead)
                .PointOnSection(ridePoint + new System.Numerics.Vector2(OutroSide, OutroLift))
                .RelativeTo(origin).ToGodot() + snapOffset;
            float swing = _outroBlend * _outroBlend * (3f - 2f * _outroBlend);
            camPos = camPos.Lerp(over, swing);
            target = target.Lerp(shipPos, swing);
        }
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
                case SessionEvent.WarpEntered:
                    _shake = 0.7f;
                    break;
                case SessionEvent.Warped:
                    // No shield lost; the cost is the ground you have to cover again.
                    _hud.Callout("WARPED BACK");
                    _shake = 1f;
                    break;
                case SessionEvent.ThrustLimited:
                    _hud.Callout("THRUST LIMITED");
                    break;
                case SessionEvent.ThrustReleased:
                    _hud.Callout("THRUST CLEAR");
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
                    // The level that just ended the run is not in the splits: it was not finished.
                    ShowRunSummary("SHIELDS DOWN", "Up / down to scroll     Space to run it again     Esc for options");
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
        // Recorded before the split is kept, so the summary can mark which levels were personal bests.
        bool best = !_practice && _bestTimes.Record(LevelId, time);
        _splits.Add((_level.Name, time, best));

        if (_level.Next is null)
        {
            if (!_practice) _bestTimes.Record(RunKey, SplitTotal);
            if (!_practice) SaveBestTimes();
            EnterOutro();
            return true;
        }

        if (best) SaveBestTimes();
        _hud.Callout($"{_level.Name.ToUpperInvariant()}   {time:0.00}s{(best ? "   NEW BEST" : "")}");
        LoadLevel(LevelPath.GetBaseDir().PathJoin(_level.Next), _session.Carry);
        return true;
    }

    // The run's splits, with its total and the best total to beat. A full run is 26 levels, so the
    // list is handed over as rows the HUD can scroll rather than as one block of text.
    private void ShowRunSummary(string headline, string hint)
    {
        var rows = new List<string>();
        foreach (var (level, time, best) in _splits)
        {
            rows.Add($"{level}   {time:0.00}s{(best ? "   NEW BEST" : "")}");
        }

        int count = _splits.Count;
        string subline = count == 0
            ? "no zones cleared"
            : _runStart.GetFile() == "level_01.json"
                ? $"{count} zone{(count == 1 ? "" : "s")}, start to finish"
                : $"{count} zone{(count == 1 ? "" : "s")} cleared";

        string bestRun = _bestTimes.Get(RunKey) is float best2 ? $"   (best {best2:0.00}s)" : "";
        _hud.ShowSummary(headline, subline, rows, $"TOTAL   {SplitTotal:0.00}s{bestRun}", hint);
    }

    /// <summary>
    /// A finished run does not stop. The ship carries on into an empty tube and flies itself while
    /// the results are up, with the camera swung round to watch it go past. Ending on a frozen frame
    /// of whatever happened to be on screen is a poor way to finish twenty-six levels.
    /// </summary>
    private void EnterOutro()
    {
        _outroTime = 0f;
        _outroBlend = 0f;
        // Taken before the victory level replaces the session: these are the run's numbers, and the
        // lap that follows is a fresh session on a fresh track with nothing to do with them.
        _finalScore = _session.Score;
        _finalRun = SplitTotal;
        LoadVictoryLap();
    }

    // Loading a level clears whatever message is up, so the summary goes back on afterwards.
    private void LoadVictoryLap()
    {
        LoadLevel(VictoryPath, carry: null, startS: OutroStart);
        _track.ViewBehind = OutroBehind;
        _running = true;
        _outro = true;
        _hud.Freeze(_finalScore, _finalRun);
        ShowRunSummary("RUN COMPLETE", "Up / down to scroll     Space to run it again     Esc for options");
    }

    // Flying itself: a slow weave around the tube. The ship is fed input like on any other frame
    // rather than being moved directly, so there is one movement path in the game and the victory
    // lap obeys the same rules the run did.
    private void StepOutro(float dt)
    {
        _outroTime += dt;
        _outroBlend = Mathf.Min(1f, _outroBlend + dt / OutroSwing);
        // Throttle held all the way down: the run is over, so the lap is a cruise rather than a
        // sprint, and a slower ship is one the camera can actually look at.
        _session.Step(dt, new ShipInput(Steer: 0.55f * Mathf.Sin(_outroTime * 0.55f), Throttle: -1f));

        // The track grows ahead of the ship instead of the lap looping. Looping cannot be made
        // seamless: each wall segment picks its palette and checker size from a hash of its index,
        // so coming back round to an earlier stretch changes the pattern even though the geometry
        // matches. Chunks behind are already freed as the ship goes, so this only ever costs the
        // track's own frames.
        var track = _level.Track;
        if (track.Length - _session.Ship.Position.S < OutroExtend && track.Length < OutroMaxLength)
        {
            track.Append(new TrackPiece(OutroExtend, track.SectionAt(track.Length)));
        }

        // Growing for ever is not free - the track keeps a frame every unit - so past a point it
        // stops extending, the lap runs out, and this loads it fresh. That costs one seam in the
        // wall pattern somewhere around half an hour in, against memory that would otherwise climb
        // for as long as the screen is left up.
        if (_session.State != SessionState.Playing) LoadVictoryLap();
    }

    // What the menu offers depends on where the run is: there is nothing to resume once it is over,
    // and nothing to restart a run from if one was never really started.
    private void OpenMenu()
    {
        _menuTitle = "PAUSED";
        _menuConfirming = false;
        _menu.Clear();
        // Always first, and always the one selected on opening: the default pick has to be the one
        // that changes nothing, since Escape is also what people hit by accident.
        _menu.Add((_session.State == SessionState.Playing && _running ? "Resume" : "Back", CloseMenu));
        // Not offered on the victory lap: the level being flown there is the victory track, and
        // restarting it would hand the player a tube with nothing in it and no way out.
        if (!_outro)
        {
            _menu.Add(("Restart this level", () =>
            {
                CloseMenu();
                LoadLevel(LevelPath, _levelEntry, _levelStart);
            }));
        }
        // The two that throw away a whole run ask first. They sit next to things picked in a hurry.
        _menu.Add(("Restart the run", () => Confirm("Start the run over?", "Yes, start over", () =>
        {
            CloseMenu();
            RestartRun();
        })));
        _menu.Add(("Quit", () => Confirm("Quit the game?", "Yes, quit", () => GetTree().Quit())));

        _menuIndex = 0;
        _menuOpen = true;
        _shake = 0f;
        RefreshMenu();
    }

    // A yes/no question in place of the menu, with "no" selected. Backing out reopens the menu.
    private void Confirm(string question, string yes, Action act)
    {
        _menuTitle = question;
        _menuConfirming = true;
        _menu.Clear();
        _menu.Add(("No, go back", OpenMenu));
        _menu.Add((yes, act));
        _menuIndex = 0;
        RefreshMenu();
    }

    private void RefreshMenu() => _hud.ShowMenu(_menuTitle, _menu.ConvertAll(o => o.Label), _menuIndex);

    private void CloseMenu()
    {
        _menuOpen = false;
        _menuConfirming = false;
        _hud.HideMenu();
    }

    private void UpdateMenu()
    {
        int move = Input.IsActionJustPressed(InputSetup.ThrottleUp) ? -1
            : Input.IsActionJustPressed(InputSetup.ThrottleDown) ? 1
            : 0;
        if (move != 0)
        {
            _menuIndex = (_menuIndex + move + _menu.Count) % _menu.Count;
            RefreshMenu();
        }

        if (Input.IsActionJustPressed(InputSetup.Jump) || Input.IsActionJustPressed(InputSetup.Fire))
        {
            // Taken before the call: picking one can load a level and rebuild the menu underneath.
            var pick = _menu[_menuIndex].Pick;
            pick();
        }
    }

    /// <summary>
    /// Fills the results screen with the levels this run would have covered, so it can be looked at
    /// without playing to the end of the game. The scrolling only engages past a dozen rows, and
    /// splits are only kept for levels actually finished, so checking it honestly meant a very long
    /// sitting. Debug only: reached with --summary, and nothing here is a real run.
    /// </summary>
    private void FillDebugSummary()
    {
        string dir = LevelPath.GetBaseDir();
        string path = LevelPath;
        for (int i = 0; i < 40; i++)
        {
            Level level;
            try
            {
                level = LevelLoader.Parse(FileAccess.GetFileAsString(path));
            }
            catch (LevelFormatException)
            {
                break;
            }

            // Spread of plausible times, with a best every few levels so both looks are visible.
            _splits.Add((level.Name, 38f + i * 11 % 47 + i * 0.37f, i % 4 == 0));
            if (level.Next is null) break;
            path = dir.PathJoin(level.Next);
        }
        ShowRunSummary("RUN COMPLETE", "Up / down to scroll     Space to run it again     Esc for options");
    }

    // Up and down walk the results list. They are the throttle keys, which are free here because
    // the ship is no longer flying, so nothing new has to be learned to read your own run.
    private void ScrollSummary(float dt)
    {
        int dir = Input.IsActionPressed(InputSetup.ThrottleUp) ? -1
            : Input.IsActionPressed(InputSetup.ThrottleDown) ? 1
            : 0;
        if (dir == 0)
        {
            _scrollHeld = 0f;
            return;
        }

        // One row on the press, then a steady walk once it is clearly being held.
        if (_scrollHeld <= 0f) _hud.ScrollSummary(dir);
        _scrollHeld += dt;
        if (_scrollHeld < 0.4f) return;

        _scrollRepeat -= dt;
        if (_scrollRepeat > 0f) return;
        _hud.ScrollSummary(dir);
        _scrollRepeat = 0.07f;
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
        if (_hud.HasSummary) ScrollSummary(dt);
        _endedFor += dt;
        if (_endedFor < 1f) return false;
        if (!Input.IsActionJustPressed(InputSetup.Jump) && !Input.IsActionJustPressed(InputSetup.Fire)) return false;

        RestartRun();
        return true;
    }

    // Back to the level the run began on, with the splits, the score and the clock all starting
    // again. Loading a level clears the frozen numbers the victory lap was holding.
    private void RestartRun()
    {
        _splits.Clear();
        LoadLevel(_runStart, carry: null, startS: _startS);
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
