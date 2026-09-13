using System.Collections.Generic;
using Godot;
using TubeRunner.Core;

namespace TubeRunner.Game;

/// <summary>Score, shields, speed, level title, messages, and hit flash. Built in code.</summary>
public partial class Hud : CanvasLayer
{
    private const float TitleSeconds = 3f;
    private const int Margin = 28;

    // Level rows visible at once. Chosen to leave room for the headline, footer and hint on a
    // short window rather than to fill a tall one.
    private const int SummaryRows = 12;

    private readonly List<ColorRect> _pips = new();
    private readonly List<string> _summaryRows = new();
    private string _summaryHead = "";
    private string _summarySub = "";
    private string _summaryFoot = "";
    private string _summaryHint = "";
    private int _summaryTop;
    private Label _score = null!;
    private Label _time = null!;
    private Label _speed = null!;
    private Label _title = null!;
    private Label _message = null!;
    private ColorRect _flash = null!;
    private ColorRect _messageBack = null!;
    private ColorRect _menuBack = null!;
    private Label _menu = null!;
    private Control _jumpCue = null!;
    private Label _jumpLabel = null!;
    private Polygon2D _jumpUp = null!;
    private Polygon2D _jumpDown = null!;
    private Control _thrustBar = null!;
    private ColorRect _thrustTrack = null!;
    private ColorRect _thrustFill = null!;
    private ColorRect _thrustBlockedLow = null!;
    private ColorRect _thrustBlockedHigh = null!;
    private HBoxContainer _shieldBar = null!;
    private static readonly Color ExtraShieldColor = new(1f, 0.82f, 0.25f);

    private Color _accent = Colors.White;
    private int _normalPips;
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

        // The menu sits above the message, with its own panel, so opening it does not disturb a run
        // summary underneath - closing it puts the results back exactly as they were.
        _menuBack = new ColorRect { Color = new Color(0f, 0f, 0f, 0.86f), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_menuBack);
        _menu = AddLabel(40, Control.LayoutPreset.Center, HorizontalAlignment.Center);

        BuildThrustBar();
        BuildJumpCue();

        _shieldBar = new HBoxContainer();
        _shieldBar.AddThemeConstantOverride("separation", 10);
        AddChild(_shieldBar);
        _shieldBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft, Control.LayoutPresetMode.KeepSize, Margin);
        _shieldBar.GrowVertical = Control.GrowDirection.Begin;
    }

    /// <param name="best">The player's best time on this level, if they've finished it before.</param>
    public void Init(string levelName, int shields, int extras, Color accent, float? best)
    {
        _accent = accent;
        _best = best;
        // A level starting clears whatever screen was up: the retry prompt, or the run summary.
        ShowMessage("");
        _title.Text = levelName.ToUpperInvariant();
        _titleLeft = TitleSeconds;
        BuildPips(shields, extras);
    }

    /// <summary>Briefly shows a line of text at the top of the screen, e.g. for a power-up.</summary>
    public void Callout(string text)
    {
        _title.Text = text;
        _titleLeft = 1.5f;
    }

    // One pip per normal shield slot, then one more for each extra being carried. An extra has no
    // empty slot of its own: it is not a slot, it is a shield, so when it is spent it just goes.
    private void BuildPips(int slots, int extras)
    {
        foreach (var pip in _pips) pip.QueueFree();
        _pips.Clear();
        for (int i = 0; i < slots + extras; i++)
        {
            var pip = new ColorRect { CustomMinimumSize = new Vector2(34, 14), Color = _accent };
            _shieldBar.AddChild(pip);
            _pips.Add(pip);
        }
        _normalPips = slots;
    }

    // Width of the thrust bar, and how tall its blocked ends are drawn.
    // Upright, because more thrust reading as higher is one less thing to learn. It sits on the
    // right edge beside the speed readout, so how hard the engines are working and how fast that is
    // actually going are in one place.
    private const int ThrustWidth = 18;
    private const int ThrustHeight = 220;

    // A bar showing where the throttle sits in its range, with the ends a thrust zone has closed off
    // drawn over it. Without this the zones are invisible: the player feels the ship refuse to slow
    // down and has nothing telling them why, which is what made the earlier version of this mechanic
    // read as an arbitrary punishment.
    private void BuildThrustBar()
    {
        _thrustBar = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_thrustBar);
        // Anchored by hand rather than with a preset. Setting a preset and then overwriting Position
        // fights the anchors, and the bar ends up somewhere off screen.
        _thrustBar.AnchorLeft = 1f;
        _thrustBar.AnchorRight = 1f;
        _thrustBar.AnchorTop = 0.5f;
        _thrustBar.AnchorBottom = 0.5f;
        _thrustBar.OffsetLeft = -(Margin + ThrustWidth);
        _thrustBar.OffsetRight = -Margin;
        _thrustBar.OffsetTop = -ThrustHeight / 2f;
        _thrustBar.OffsetBottom = ThrustHeight / 2f;

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

    private const int JumpWidth = 190;
    private const int JumpHeight = 30;

    // Whether a jump is legal right now. It is only possible on a fully unrolled section, and the
    // way in and out of one is gradual, so without this there is a stretch where the player cannot
    // tell whether the button will do anything. Drawn as arrows because the thing it offers is the
    // crossing between floor and ceiling, not an action in the abstract.
    private void BuildJumpCue()
    {
        _jumpCue = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        AddChild(_jumpCue);
        _jumpCue.AnchorLeft = 0.5f;
        _jumpCue.AnchorRight = 0.5f;
        _jumpCue.AnchorTop = 1f;
        _jumpCue.AnchorBottom = 1f;
        _jumpCue.OffsetLeft = -JumpWidth / 2f;
        _jumpCue.OffsetRight = JumpWidth / 2f;
        // Where the thrust bar used to sit, now that it has moved to the edge.
        _jumpCue.OffsetTop = -(Margin + JumpHeight);
        _jumpCue.OffsetBottom = -Margin;

        // Real triangles rather than characters: the default font is not guaranteed to carry arrow
        // glyphs, and a pair of tofu boxes would say nothing at all.
        _jumpUp = Arrow(up: true, x: 6f);
        _jumpDown = Arrow(up: false, x: JumpWidth - 32f);

        _jumpLabel = new Label
        {
            Text = "JUMP",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings
            {
                FontSize = 24,
                OutlineSize = 5,
                OutlineColor = new Color(0f, 0f, 0f, 0.8f),
            },
        };
        _jumpCue.AddChild(_jumpLabel);
        _jumpLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        Polygon2D Arrow(bool up, float x)
        {
            var poly = new Polygon2D
            {
                Polygon = up
                    ? new[] { new Vector2(13f, 0f), new Vector2(26f, 24f), new Vector2(0f, 24f) }
                    : new[] { new Vector2(0f, 0f), new Vector2(26f, 0f), new Vector2(13f, 24f) },
                Position = new Vector2(x, 3f),
            };
            _jumpCue.AddChild(poly);
            return poly;
        }
    }

    private void UpdateJumpCue(GameSession session)
    {
        _jumpCue.Visible = session.Ship.CanJump;
        if (!_jumpCue.Visible) return;

        _jumpLabel.Modulate = _accent;
        _jumpUp.Color = _accent;
        _jumpDown.Color = _accent;
    }

    private void UpdateThrustBar(GameSession session)
    {
        var ship = session.Ship.Settings;
        float span = Mathf.Max(0.001f, ship.MaxThrottle - ship.MinThrottle);
        // Height above the foot of the bar, so low throttle is low on screen.
        float At(float throttle) => Mathf.Clamp((throttle - ship.MinThrottle) / span, 0f, 1f) * ThrustHeight;

        _thrustTrack.Position = Vector2.Zero;
        _thrustTrack.Size = new Vector2(ThrustWidth, ThrustHeight);

        // Godot counts y downwards, so the fill is placed by its top edge and grown towards the foot.
        float fill = At(session.Ship.Throttle);
        _thrustFill.Color = _accent;
        _thrustFill.Position = new Vector2(3f, ThrustHeight - fill);
        _thrustFill.Size = new Vector2(ThrustWidth - 6f, fill);

        // Whatever a zone has taken off each end, drawn over the top of it. The bar itself is always
        // there - the throttle is in play every second of the game - but these only appear when
        // something is actually closing the range.
        float low = At(session.Ship.ThrottleFloor);
        float high = At(session.Ship.ThrottleCeiling);
        _thrustBlockedLow.Position = new Vector2(0f, ThrustHeight - low);
        _thrustBlockedLow.Size = new Vector2(ThrustWidth, low);
        _thrustBlockedLow.Visible = low > 0.5f;
        _thrustBlockedHigh.Position = Vector2.Zero;
        _thrustBlockedHigh.Size = new Vector2(ThrustWidth, ThrustHeight - high);
        _thrustBlockedHigh.Visible = high < ThrustHeight - 0.5f;
    }

    /// <param name="runTime">Time from earlier levels of this run; the level's own time is added.</param>
    public void Update(GameSession session, float dt, float runTime)
    {
        UpdateThrustBar(session);
        UpdateJumpCue(session);

        // Keep the backing panels wrapped around whatever their labels currently say.
        _messageBack.Visible = _message.Text.Length > 0;
        if (_messageBack.Visible)
        {
            var rect = _message.GetGlobalRect();
            _messageBack.GlobalPosition = rect.Position - new Vector2(30f, 20f);
            _messageBack.Size = rect.Size + new Vector2(60f, 40f);
        }
        _menuBack.Visible = _menu.Text.Length > 0;
        if (_menuBack.Visible)
        {
            var rect = _menu.GetGlobalRect();
            _menuBack.GlobalPosition = rect.Position - new Vector2(48f, 32f);
            _menuBack.Size = rect.Size + new Vector2(96f, 64f);
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

        if (_pips.Count != session.MaxShields + session.ExtraShields || _normalPips != session.MaxShields)
        {
            BuildPips(session.MaxShields, session.ExtraShields);
        }
        for (int i = 0; i < _pips.Count; i++)
        {
            // Extras are drawn past the normal slots, in their own colour, and are always full -
            // an extra that has been spent has already been taken off the bar.
            _pips[i].Color = i >= _normalPips ? ExtraShieldColor
                : i < session.Shields ? _accent
                : new Color(_accent, 0.15f);
        }

        _titleLeft = Mathf.Max(0f, _titleLeft - dt);
        _title.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(_titleLeft, 0f, 1f));
        _flashLeft = Mathf.Max(0f, _flashLeft - dt * 2.5f);
        _flash.Color = _flash.Color with { A = 0.45f * _flashLeft };
    }

    public void SetBest(float best) => _best = best;

    public void Flash() => _flashLeft = 1f;

    public void ShowMessage(string text)
    {
        _summaryRows.Clear();
        _message.Text = text;
    }

    /// <summary>Whether a scrollable summary is up, so the caller knows to feed it scroll input.</summary>
    public bool HasSummary => _summaryRows.Count > 0;

    /// <summary>Shows the menu under <paramref name="title"/>, with <paramref name="selected"/> marked.</summary>
    public void ShowMenu(string title, IReadOnlyList<string> options, int selected)
    {
        var lines = new List<string> { title, "" };
        for (int i = 0; i < options.Count; i++)
        {
            // Marked on both sides, because a marker only on the left shifts the text and the whole
            // list jitters sideways as the selection moves.
            lines.Add(i == selected ? $">   {options[i]}   <" : $"    {options[i]}    ");
        }
        lines.Add("");
        lines.Add("Up / down to choose     Space to pick     Esc to go back");
        _menu.Text = string.Join("\n", lines);
    }

    public void HideMenu() => _menu.Text = "";

    /// <summary>
    /// Shows a run's results: a headline, a window onto <paramref name="rows"/> that can be
    /// scrolled, then a footer and a hint. A full run is 26 levels, so the list cannot simply be
    /// printed - it is longer than the screen, and the player would lose the end of what they earned.
    /// </summary>
    public void ShowSummary(string headline, string subline, IReadOnlyList<string> rows, string footer, string hint)
    {
        _summaryHead = headline;
        _summarySub = subline;
        _summaryRows.Clear();
        _summaryRows.AddRange(rows);
        _summaryFoot = footer;
        _summaryHint = hint;
        _summaryTop = 0;
        RenderSummary();
    }

    /// <summary>Scrolls the summary by <paramref name="delta"/> rows, stopping at either end.</summary>
    public void ScrollSummary(int delta)
    {
        if (!HasSummary) return;
        int top = Mathf.Clamp(_summaryTop + delta, 0, Mathf.Max(0, _summaryRows.Count - SummaryRows));
        if (top == _summaryTop) return;
        _summaryTop = top;
        RenderSummary();
    }

    private void RenderSummary()
    {
        int top = Mathf.Clamp(_summaryTop, 0, Mathf.Max(0, _summaryRows.Count - SummaryRows));
        int shown = Mathf.Min(SummaryRows, _summaryRows.Count);
        int below = _summaryRows.Count - top - shown;

        var lines = new List<string> { _summaryHead };
        if (_summarySub.Length > 0) lines.Add(_summarySub);
        lines.Add("");
        // Spelled out rather than drawn with arrows: the default font is not guaranteed to have
        // them, and a row of tofu boxes on the results screen would be a poor way to find that out.
        lines.Add(top > 0 ? $"{top} more above" : " ");
        for (int i = 0; i < shown; i++) lines.Add(_summaryRows[top + i]);
        lines.Add(below > 0 ? $"{below} more below" : " ");
        lines.Add("");
        lines.Add(_summaryFoot);
        lines.Add(_summaryHint);
        _message.Text = string.Join("\n", lines);
    }

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
