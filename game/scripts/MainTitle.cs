using System;
using System.Collections.Generic;
using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>
/// The title screen's half of <see cref="Main"/>: the tube it flies behind the menu, the pages the
/// menu opens, and the handover into a run.
///
/// The tube is the first level itself, with a straight lead-in put in front of it. The ship holds
/// station in the lead-in: every time it has flown a wall segment it is put back one, and the walls
/// are drawn one index further on, so the picture never changes and the level beyond never gets
/// any nearer (see <see cref="SegmentJoin"/>). It is kept far enough back that nothing of the level
/// is in sight - no bend, no block - since being put back would make those jump.
///
/// Start stops putting it back and gives the player the stick. The ship flies the rest of the
/// lead-in, and where the level begins it is handed to the level proper: the same tube, the same
/// walls, the same things ahead, a whole number of segments further back. Nothing on screen changes,
/// because nothing on screen is different.
/// </summary>
public partial class Main
{
    /// <summary>Where in the lead-in the ship holds station: between here and one segment further on.</summary>
    private const double TitleHold = 60.0;

    /// <summary>How far past the end of the distance fade a wall is still treated as in sight.</summary>
    private const float FadeMargin = 20f;

    // A slow lean from side to side, small enough to stay on the floor of the tube. The camera rolls
    // with the ship, and a menu over a world turning right over is not a menu anyone can read.
    private const float TitleSteer = 0.16f;
    private const float TitleSteerRate = 0.7f;

    private static readonly (string Name, string Keys)[] Controls =
    {
        ("Steer", "A / D   or   left / right"),
        ("Speed up, slow down", "W / S   or   up / down"),
        ("Jump", "Space   (flat sections and rings)"),
        ("Fire", "Ctrl / J / Enter / left mouse"),
        ("Ring gun", "E / K / right mouse"),
        ("Retry level", "R"),
        ("Pause", "P"),
        ("Menu", "Esc"),
    };

    private const string GamepadControls =
        "Gamepad: stick to steer and set speed, A to jump, X to fire, Y for the ring gun, Start for the menu";

    private static string GameTitle => ProjectSettings.GetSetting("application/config/name").AsString();

    private TitleScreen Title => _hud.Title;

    private readonly List<(string Path, string Name, string Id)> _zones = new();
    private double _titleLead;
    private string _firstLevel = "";
    private bool _titleUp;
    private bool _starting;
    private string _startPath = "";
    private float _titleTime;
    private float _playBlend = 1f;
    private float _engineBlend = 1f;

    // The shift on the wall's segment indices, and the level it belongs to. A retry of that level
    // keeps it, so restarting does not repaint the walls; any other level starts from none.
    private int _segmentOffset;
    private string _offsetLevel = "";

    // The handover held over a frame, which only a --shot of it ever does: see HandOver.
    private TrackPosition? _heldShip;
    private bool _wrapHeld;

    // Opens the title with one of its pages already up, for looking at it without a keypress.
    private string? _debugTitlePage;

    /// <summary>Puts the title up over a fresh tube. A cut: what was on screen before is gone.</summary>
    private void EnterTitle()
    {
        CloseMenu();
        _splits.Clear();
        _practice = false;
        _starting = false;
        _titleTime = 0f;
        _segmentOffset = 0;
        _offsetLevel = "";
        _titleUp = true;
        _playBlend = 0f;
        _engineBlend = 0f;
        _hud.ShowMessage("");

        Level tube;
        try
        {
            tube = BuildTitleLevel();
        }
        catch (LevelFormatException)
        {
            // A first level that will not load has no tube to fly. Loading it the ordinary way puts
            // the reason on screen, which is more use than a title over nothing.
            _titleUp = false;
            LoadLevel(_firstLevel, carry: null, startS: _startS);
            return;
        }

        BeginLevel(tube, carry: null, new TrackPosition(TitleHold, Surface.Floor, 0f));
        _running = true;
        ShowTitleMenu();
    }

    // The first level behind a lead-in long enough to hide it. Parsed afresh each time, like any
    // level, because what is in it holds per-run state.
    private Level BuildTitleLevel()
    {
        string json = FileAccess.GetFileAsString(_firstLevel);
        if (_titleLead <= 0.0)
        {
            var source = LevelLoader.Parse(json);
            float view = (source.Theme.Wire ? source.Theme.FadeEnd * 2f : source.Theme.FadeEnd) + FadeMargin;
            _titleLead = SegmentJoin.LeadIn(source.SegmentLength, TitleHold, view, source.Track.StraightStart);
        }
        return LevelLoader.ParseWithLeadIn(json, (float)_titleLead);
    }

    private void ProcessTitle(float dt)
    {
        if (_heldShip is TrackPosition held)
        {
            HandOver(held);
            return;
        }
        if (_wrapHeld)
        {
            // The second half of a --shot of the treadmill: see below.
            GetViewport().GetTexture().GetImage().SavePng(_shotPath!.GetBaseName() + "-before.png");
            _wrapHeld = false;
            _shotFrames = 1;
            StepBack();
            DrawWorld(0f, steer: 0f);
            return;
        }

        if (Input.IsActionJustPressed(InputSetup.Menu) && !Title.JustWoke && !_starting)
        {
            // Out of a page back to the title; from the title itself, Escape offers the way out.
            if (_menuOpen) ShowTitleMenu();
            else ConfirmFromTitle("Quit the game?", "Yes, quit", QuitGame);
        }

        // Once Start is picked the stick is the player's. Only the stick: the throttle stays where
        // every level starts it, so the run-up is not a way to arrive faster than a retry would.
        _titleTime += dt;
        float steer = _starting
            ? Input.GetAxis(InputSetup.SteerLeft, InputSetup.SteerRight)
            : TitleSteer * Mathf.Sin(_titleTime * TitleSteerRate);
        _session.Step(dt, new ShipInput(Steer: steer));
        var ship = _session.Ship.Position;

        double segment = _level.SegmentLength;
        if (_starting)
        {
            if (ship.S >= _titleLead + CameraBehind + 10.0 && HandOver(ship)) return;
        }
        else if (ship.S >= TitleHold + segment)
        {
            if (_debugTitlePage == "wrap" && _shotPath is not null && _shotFrames > 1)
            {
                // Looking at the treadmill's step the same way as the handover: this instant drawn
                // and held, then drawn again from a segment back, and the two files compared.
                _wrapHeld = true;
                DrawWorld(0f, steer: 0f);
                return;
            }
            StepBack();
        }

        DrawWorld(dt, steer);
    }

    // Back a segment, and the walls on an index: the same picture from one segment earlier.
    private void StepBack()
    {
        _session.Ship.WarpTo(_session.Ship.Position.S - _level.SegmentLength);
        _segmentOffset += 1;
        _track.SegmentOffset = _segmentOffset;
    }

    /// <summary>
    /// Hands the ship from the lead-in to the level it leads into, at the place in the level that
    /// the ship has reached. The lead-in is a whole number of segments, so this is the same move
    /// the treadmill makes, only further.
    /// </summary>
    /// <returns>True if the level is now loaded.</returns>
    private bool HandOver(TrackPosition ship)
    {
        if (_shotPath is not null)
        {
            // Checking the join by eye needs the same instant drawn twice, once as the title and
            // once as the level. So the title is drawn where the ship now is and held for a frame;
            // the next frame saves that picture, swaps, and draws the level without moving, and the
            // ordinary shot the frame after is the level's. Any difference between the two files is
            // a real one, not a frame's worth of flying.
            if (_heldShip is null)
            {
                _heldShip = ship;
                DrawWorld(0f, steer: 0f);
                return true;
            }
            GetViewport().GetTexture().GetImage().SavePng(_shotPath.GetBaseName() + "-before.png");
            _heldShip = null;
            _shotFrames = 1;
        }

        double startS = ship.S - _titleLead;
        _segmentOffset += SegmentJoin.IndexOffset(ship.S, startS, _level.SegmentLength);
        _offsetLevel = _startPath;

        // The menu is left to finish fading by itself; taking it down here would be the one pop
        // in a handover that otherwise has none.
        _titleUp = false;
        _starting = false;
        _runStart = _startPath;
        _startS = CameraBehind + 10.0;
        LoadLevel(_startPath, carry: null, startS, ship);
        _running = true;
        DrawWorld(0f, steer: 0f);
        return true;
    }

    private void ShowTitleMenu()
    {
        _menuRoot = ShowTitleMenu;
        _menuOpen = false;
        _menuConfirming = false;
        _hud.Menu.Close();
        _hud.ShowMessage("");

        var options = new List<(string, Action)> { ("Start game", () => StartFromTitle(_firstLevel)) };
        if (ReachedZones().Count > 1) options.Add(("Start from a zone", OpenZones));
        options.Add(("Best times", OpenBestTimes));
        options.Add(("Settings", () => OpenTitlePage(OpenSettings)));
        options.Add(("Controls", OpenControls));
        options.Add(("About", OpenAbout));
        options.Add(("Quit", () => ConfirmFromTitle("Quit the game?", "Yes, quit", QuitGame)));

        var version = UpdateChecker.CurrentVersion;
        string notice = _update is null ? "" : $"Version {_update.Version} is out     see About";
        Title.Open(GameTitle, version.IsDevelopment ? "development build" : $"v{version}", notice,
            _level.Theme.SeamLight.ToColor(), options);
    }

    // Every page over the title is the Escape menu's panel, with the title stepped aside under it.
    private void OpenTitlePage(Action open)
    {
        Title.StepAside();
        _menuOpen = true;
        _menuConfirming = true;
        open();
    }

    private void ConfirmFromTitle(string question, string yes, Action act) =>
        OpenTitlePage(() => Confirm(question, yes, act));

    private void StartFromTitle(string path)
    {
        _startPath = path;
        if (path == _firstLevel)
        {
            // The tube behind the menu is this level's own opening, so it simply carries on.
            _starting = true;
            _hud.Menu.Close();
            _menuOpen = false;
            Title.Leave();
            return;
        }

        // Any other zone has its own bore and colours, so this one is a cut, softened by a fade.
        _titleUp = false;
        Title.Close();
        CloseMenu();
        _runStart = path;
        _startS = CameraBehind + 10.0;
        LoadLevel(path, carry: null, startS: _startS);
        _running = true;
        _hud.FadeIn();
    }

    // The levels in running order, read once. Only the headers: building thirty-odd tracks to list
    // their names would be a visible pause on opening a menu.
    private IReadOnlyList<(string Path, string Name, string Id)> Zones()
    {
        if (_zones.Count > 0) return _zones;

        string dir = _firstLevel.GetBaseDir();
        string path = _firstLevel;
        for (int i = 0; i < 200 && FileAccess.FileExists(path); i++)
        {
            LevelHeader header;
            try
            {
                header = LevelLoader.ReadHeader(FileAccess.GetFileAsString(path));
            }
            catch (LevelFormatException)
            {
                break;
            }
            _zones.Add((path, header.Name, header.Id));
            if (header.Next is null) break;
            path = dir.PathJoin(header.Next);
        }
        return _zones;
    }

    // A zone can be started from once it has been reached: it has a time of its own, or the one
    // before it does. The first is always there.
    private List<(string Path, string Name, string Id, int Number)> ReachedZones()
    {
        var zones = Zones();
        var reached = new List<(string, string, string, int)>();
        for (int i = 0; i < zones.Count; i++)
        {
            bool open = i == 0 || _bestTimes.Get(zones[i].Id) is not null || _bestTimes.Get(zones[i - 1].Id) is not null;
            if (open) reached.Add((zones[i].Path, zones[i].Name, zones[i].Id, i + 1));
        }
        return reached;
    }

    private void OpenZones() => OpenTitlePage(() =>
    {
        var rows = new List<(string, string, Action?)>();
        foreach (var (path, name, id, number) in ReachedZones())
        {
            rows.Add(($"{number,2}   {name}", Time(_bestTimes.Get(id)), () => StartFromTitle(path)));
        }
        _hud.Menu.OpenList("START FROM A ZONE", rows,
            "A run from a later zone keeps its own best total, apart from a full run's.", ShowTitleMenu);
    });

    private void OpenBestTimes() => OpenTitlePage(() =>
    {
        var zones = Zones();
        var rows = new List<(string, string, Action?)>();
        float total = 0f;
        int timed = 0;
        for (int i = 0; i < zones.Count; i++)
        {
            float? best = _bestTimes.Get(zones[i].Id);
            if (best is float time)
            {
                total += time;
                timed++;
            }
            rows.Add(($"{i + 1,2}   {zones[i].Name}", Time(best), null));
        }

        string run = zones.Count > 0 && _bestTimes.Get(BestTimes.RunPrefix + zones[0].Id) is float full
            ? $"Best full run   {full:0.00}s"
            : "No full run finished yet";
        string note = timed == 0
            ? run
            : $"{run}\n{timed} of {zones.Count} zones timed, {total:0.00}s between them";
        _hud.Menu.OpenList("BEST TIMES", rows, note, ShowTitleMenu);
    });

    private static string Time(float? time) => time is float t ? $"{t:0.00}s" : "--";

    private void OpenControls() => OpenTitlePage(() =>
        _hud.Menu.OpenPage("CONTROLS", Controls, GamepadControls, new (string, Action)[] { ("Back", ShowTitleMenu) }));

    private void OpenAbout() => OpenTitlePage(() =>
    {
        var version = UpdateChecker.CurrentVersion;
        string updates = _update is not null ? $"Version {_update.Version} is out"
            : version.IsDevelopment ? "A development build does not check"
            : !_settings.CheckForUpdates ? "Checking is turned off in Settings"
            : "Nothing newer found";
        var rows = new (string, string)[]
        {
            ("Version", version.IsDevelopment ? "development build" : $"v{version}"),
            ("Updates", updates),
            ("Made by", "Clint Andrews"),
            ("Copyright", "© 2026 Clint Andrews"),
            ("Built with", "Godot 4 and C#"),
            ("Project", $"github.com/{Updates.Repository}"),
        };

        // Back first, as everywhere: the choice focused on opening is the one that changes nothing.
        var options = new List<(string, Action)> { ("Back", ShowTitleMenu) };
        options.Add(("Open the project page", () => OS.ShellOpen($"https://github.com/{Updates.Repository}")));
        if (_update is not null)
        {
            var release = _update;
            // Checked when it was read to be this game's own release page on GitHub.
            options.Add(($"Get version {release.Version}", () => OS.ShellOpen(release.Url)));
        }
        _hud.Menu.OpenPage($"ABOUT {GameTitle.ToUpperInvariant()}", rows,
            "Godot Engine is free software under the MIT licence: godotengine.org/license", options);
    });

    // The start screen a test run with --level still gets, built from the same rows as the Controls page.
    private static string ControlsText()
    {
        var lines = new List<string> { GameTitle.ToUpperInvariant(), "" };
        foreach (var (name, keys) in Controls) lines.Add($"{name,-22}{keys}");
        lines.Add("");
        lines.Add(GamepadControls);
        lines.Add("");
        lines.Add("Space to start");
        return string.Join("\n", lines);
    }
}
