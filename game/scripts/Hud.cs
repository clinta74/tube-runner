using System.Collections.Generic;
using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>Score, shields, speed, level title, messages, and hit flash. Built in code.</summary>
public partial class Hud : CanvasLayer
{
    private const float TitleSeconds = 3f;
    private const int Margin = 28;

    private readonly List<ColorRect> _pips = new();
    private Label _score = null!;
    private Label _time = null!;
    private Label _speed = null!;
    private Label _title = null!;
    private Label _message = null!;
    private ColorRect _flash = null!;
    private ColorRect _messageBack = null!;
    private Control _thrustBar = null!;
    private ColorRect _thrustTrack = null!;
    private ColorRect _thrustFill = null!;
    private ColorRect _thrustBlockedLow = null!;
    private ColorRect _thrustBlockedHigh = null!;
    private HBoxContainer _shieldBar = null!;
    private Color _accent = Colors.White;
    private float _titleLeft;
    private float _flashLeft;
    private float? _best;

    public override void _Ready()
    {
        _flash = new ColorRect { Color = new Color(1f, 0.15f, 0.05f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_flash);
        _flash.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _score = AddLabel(34, Control.LayoutPreset.TopRight, HorizontalAlignment.Right);
        _time = AddLabel(30, Control.LayoutPreset.TopLeft, HorizontalAlignment.Left);
        _speed = AddLabel(24, Control.LayoutPreset.BottomRight, HorizontalAlignment.Right);
        _title = AddLabel(56, Control.LayoutPreset.CenterTop, HorizontalAlignment.Center);
        // A panel behind the message. A run summary is a dozen lines of white text, and over a lit
        // tube wall it is simply unreadable - added before the label so it sits behind it.
        _messageBack = new ColorRect { Color = new Color(0f, 0f, 0f, 0.72f), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_messageBack);
        // Small enough that an end-of-run list of splits fits.
        _message = AddLabel(34, Control.LayoutPreset.Center, HorizontalAlignment.Center);

        BuildThrustBar();

        _shieldBar = new HBoxContainer();
        _shieldBar.AddThemeConstantOverride("separation", 10);
        AddChild(_shieldBar);
        _shieldBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft, Control.LayoutPresetMode.KeepSize, Margin);
        _shieldBar.GrowVertical = Control.GrowDirection.Begin;
    }

    /// <param name="best">The player's best time on this level, if they've finished it before.</param>
    public void Init(string levelName, int shields, Color accent, float? best)
    {
        _accent = accent;
        _best = best;
        // A level starting clears whatever screen was up: the retry prompt, or the run summary.
        _message.Text = "";
        _title.Text = levelName.ToUpperInvariant();
        _titleLeft = TitleSeconds;
        BuildPips(shields);
    }

    /// <summary>Briefly shows a line of text at the top of the screen, e.g. for a power-up.</summary>
    public void Callout(string text)
    {
        _title.Text = text;
        _titleLeft = 1.5f;
    }

    // One pip per shield slot.
    private void BuildPips(int slots)
    {
        foreach (var pip in _pips) pip.QueueFree();
        _pips.Clear();
        for (int i = 0; i < slots; i++)
        {
            var pip = new ColorRect { CustomMinimumSize = new Vector2(34, 14), Color = _accent };
            _shieldBar.AddChild(pip);
            _pips.Add(pip);
        }
    }

    // Width of the thrust bar, and how tall its blocked ends are drawn.
    private const int ThrustWidth = 260;
    private const int ThrustHeight = 16;

    // A bar showing where the throttle sits in its range, with the ends a thrust zone has closed off
    // drawn over it. Without this the zones are invisible: the player feels the ship refuse to slow
    // down and has nothing telling them why, which is what made the earlier version of this mechanic
    // read as an arbitrary punishment.
    private void BuildThrustBar()
    {
        _thrustBar = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_thrustBar);
        _thrustBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterBottom, Control.LayoutPresetMode.KeepSize, Margin);
        _thrustBar.CustomMinimumSize = new Vector2(ThrustWidth, ThrustHeight);
        _thrustBar.Size = new Vector2(ThrustWidth, ThrustHeight);
        _thrustBar.Position = new Vector2(-ThrustWidth / 2f, _thrustBar.Position.Y);

        _thrustTrack = AddRect(new Color(1f, 1f, 1f, 0.16f));
        _thrustFill = AddRect(Colors.White);
        _thrustBlockedLow = AddRect(new Color(1f, 0.35f, 0.2f, 0.55f));
        _thrustBlockedHigh = AddRect(new Color(1f, 0.35f, 0.2f, 0.55f));

        ColorRect AddRect(Color color)
        {
            var rect = new ColorRect { Color = color, MouseFilter = Control.MouseFilterEnum.Ignore };
            _thrustBar.AddChild(rect);
            return rect;
        }
    }

    private void UpdateThrustBar(GameSession session)
    {
        var ship = session.Ship.Settings;
        float span = Mathf.Max(0.001f, ship.MaxThrottle - ship.MinThrottle);
        float At(float throttle) => Mathf.Clamp((throttle - ship.MinThrottle) / span, 0f, 1f) * ThrustWidth;

        _thrustTrack.Position = Vector2.Zero;
        _thrustTrack.Size = new Vector2(ThrustWidth, ThrustHeight);

        // The fill runs from the bottom of the range to where the throttle currently sits.
        _thrustFill.Color = _accent;
        _thrustFill.Position = new Vector2(0f, 3f);
        _thrustFill.Size = new Vector2(At(session.Ship.Throttle), ThrustHeight - 6f);

        // Whatever a zone has taken off each end, drawn over the top of it.
        float low = At(session.Ship.ThrottleFloor);
        float high = At(session.Ship.ThrottleCeiling);
        _thrustBlockedLow.Position = Vector2.Zero;
        _thrustBlockedLow.Size = new Vector2(low, ThrustHeight);
        _thrustBlockedHigh.Position = new Vector2(high, 0f);
        _thrustBlockedHigh.Size = new Vector2(ThrustWidth - high, ThrustHeight);
    }

    /// <param name="runTime">Time from earlier levels of this run; the level's own time is added.</param>
    public void Update(GameSession session, float dt, float runTime)
    {
        UpdateThrustBar(session);

        // Keep the backing panel wrapped around whatever the message currently says.
        _messageBack.Visible = _message.Text.Length > 0;
        if (_messageBack.Visible)
        {
            var rect = _message.GetGlobalRect();
            _messageBack.GlobalPosition = rect.Position - new Vector2(30f, 20f);
            _messageBack.Size = rect.Size + new Vector2(60f, 40f);
        }

        _score.Text = $"SCORE  {session.Score}";
        _time.Text = $"TIME  {session.Elapsed:0.00}\nBEST  {(_best is float best ? best.ToString("0.00") : "--")}" +
            $"\nRUN   {runTime + session.Elapsed:0.00}";
        var status = new List<string>(4);
        if (session.RamLeft > 0f) status.Add($"UNSTOPPABLE  {session.RamLeft:0.0}s");
        if (session.RapidFireLeft > 0f) status.Add($"RAPID FIRE  {session.RapidFireLeft:0.0}s");
        if (session.RingCharges > 0) status.Add($"RING GUN  x{session.RingCharges}");
        status.Add($"{session.Ship.ForwardSpeed:0} u/s");
        _speed.Text = string.Join("\n", status);

        if (_pips.Count != session.MaxShields) BuildPips(session.MaxShields);
        for (int i = 0; i < _pips.Count; i++)
        {
            _pips[i].Color = i < session.Shields ? _accent : new Color(_accent, 0.15f);
        }

        _titleLeft = Mathf.Max(0f, _titleLeft - dt);
        _title.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(_titleLeft, 0f, 1f));
        _flashLeft = Mathf.Max(0f, _flashLeft - dt * 2.5f);
        _flash.Color = _flash.Color with { A = 0.45f * _flashLeft };
    }

    public void SetBest(float best) => _best = best;

    public void Flash() => _flashLeft = 1f;

    public void ShowMessage(string text) => _message.Text = text;

    private Label AddLabel(int size, Control.LayoutPreset preset, HorizontalAlignment align)
    {
        var label = new Label
        {
            HorizontalAlignment = align,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings
            {
                FontSize = size,
                OutlineSize = size / 5,
                OutlineColor = new Color(0f, 0f, 0f, 0.8f),
            },
        };
        AddChild(label);
        label.SetAnchorsAndOffsetsPreset(preset, Control.LayoutPresetMode.KeepSize, Margin);
        // Grow away from the anchored edge as the text changes.
        label.GrowHorizontal = align switch
        {
            HorizontalAlignment.Right => Control.GrowDirection.Begin,
            HorizontalAlignment.Center => Control.GrowDirection.Both,
            _ => Control.GrowDirection.End,
        };
        label.GrowVertical = preset switch
        {
            Control.LayoutPreset.BottomRight or Control.LayoutPreset.BottomLeft => Control.GrowDirection.Begin,
            Control.LayoutPreset.Center => Control.GrowDirection.Both,
            _ => Control.GrowDirection.End,
        };
        return label;
    }
}
