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
    private Control _jumpCue = null!;
    private Label _jumpLabel = null!;
    private Polygon2D _jumpUp = null!;
    private Polygon2D _jumpDown = null!;
    private Control _thrustBar = null!;
    private Panel _thrustTrack = null!;
    private StyleBoxFlat _thrustFrame = null!;
    private ColorRect _thrustFill = null!;
    private ColorRect _thrustBlocked = null!;
    private ColorRect _thrustCut = null!;
    private ColorRect _thrustBlockedTop = null!;
    private ColorRect _thrustCutTop = null!;
    private HBoxContainer _shieldBar = null!;
    private static readonly Color ExtraShieldColor = new(1f, 0.82f, 0.25f);

    private Color _accent = Colors.White;
    private int _normalPips;
    private float _titleLeft;
    private float _flashLeft;
    private float? _best;
    private int? _frozenScore;
    private float _frozenRun;

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
        BuildJumpCue();

        _shieldBar = new HBoxContainer();
        _shieldBar.AddThemeConstantOverride("separation", 10);
        AddChild(_shieldBar);
        _shieldBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft, Control.LayoutPresetMode.KeepSize, Margin);
        _shieldBar.GrowVertical = Control.GrowDirection.Begin;

        // Added last so it draws over everything, including a run summary - which it leaves alone, so
        // closing the menu puts the results back exactly as they were.
        Menu = new GameMenu();
        AddChild(Menu);
    }

    /// <summary>The Escape menu. Main decides what it offers; this only shows it.</summary>
    public GameMenu Menu { get; private set; } = null!;

    /// <param name="best">The player's best time on this level, if they've finished it before.</param>
    public void Init(string levelName, int shields, int extras, Color accent, float? best)
    {
        _accent = accent;
        Menu.SetAccent(accent);
        _best = best;
        _frozenScore = null;
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
    private const int ThrustWidth = 30;
    private const int ThrustHeight = 340;

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

        // A dark backing with a rim, so the whole range reads over any wall. A faint strip vanished
        // against the brighter themes and left only the fill, with no sense of how much range there was.
        _thrustFrame = new StyleBoxFlat { BgColor = new Color(0f, 0f, 0f, 0.55f), BorderColor = new Color(1f, 1f, 1f, 0.7f) };
        _thrustFrame.SetBorderWidthAll(2);
        _thrustTrack = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore };
        _thrustTrack.AddThemeStyleboxOverride("panel", _thrustFrame);
        _thrustBar.AddChild(_thrustTrack);
        _thrustFill = AddRect(Colors.White);
        // Hatched rather than filled, so the whole bar stays readable: the closed-off part is ghosted
        // and struck through, not painted over. The fill still shows through the gaps, so the player
        // can see where the throttle sits even when it is sitting at the cut.
        var hatch = new ShaderMaterial { Shader = new Shader { Code = HatchShader } };
        _thrustBlocked = AddRect(Colors.White);
        _thrustBlocked.Material = hatch;
        _thrustBlockedTop = AddRect(Colors.White);
        _thrustBlockedTop.Material = hatch;
        // A solid line at each cut itself, so the new limit reads at a glance.
        _thrustCut = AddRect(new Color(1f, 0.45f, 0.25f));
        _thrustCutTop = AddRect(new Color(1f, 0.45f, 0.25f));

        ColorRect AddRect(Color color)
        {
            var rect = new ColorRect { Color = color, MouseFilter = Control.MouseFilterEnum.Ignore };
            _thrustBar.AddChild(rect);
            return rect;
        }
    }

    // Diagonal stripes in screen space. The gaps darken what is underneath rather than hiding it.
    private const string HatchShader = """
        shader_type canvas_item;
        void fragment() {
            float band = mod(FRAGCOORD.x + FRAGCOORD.y, 10.0);
            float stripe = step(band, 3.5);
            COLOR = mix(vec4(0.0, 0.0, 0.0, 0.3), vec4(1.0, 0.4, 0.22, 0.7), stripe);
        }
        """;

    private const int JumpWidth = 300;
    private const int JumpHeight = 48;
    private const float JumpArrow = 40f;

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
        _jumpUp = Arrow(up: true, x: 8f);
        _jumpDown = Arrow(up: false, x: JumpWidth - JumpArrow - 8f);

        _jumpLabel = new Label
        {
            Text = "JUMP",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings
            {
                FontSize = 38,
                OutlineSize = 8,
                OutlineColor = new Color(0f, 0f, 0f, 0.8f),
            },
        };
        _jumpCue.AddChild(_jumpLabel);
        _jumpLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        Polygon2D Arrow(bool up, float x)
        {
            float w = JumpArrow, h = JumpHeight - 10f;
            var poly = new Polygon2D
            {
                Polygon = up
                    ? new[] { new Vector2(w / 2f, 0f), new Vector2(w, h), new Vector2(0f, h) }
                    : new[] { new Vector2(0f, 0f), new Vector2(w, 0f), new Vector2(w / 2f, h) },
                Position = new Vector2(x, 5f),
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
        _thrustFrame.BorderColor = new Color(_accent, 0.75f);

        // Godot counts y downwards, so the fill is placed by its top edge and grown towards the foot.
        float fill = At(session.Ship.Throttle);
        _thrustFill.Color = _accent;
        _thrustFill.Position = new Vector2(5f, ThrustHeight - fill);
        _thrustFill.Size = new Vector2(ThrustWidth - 10f, fill);

        // Whatever a zone has taken off either end, drawn over the bar. The bar itself is always
        // there - the throttle is in play every second of the game - but these only appear when
        // something is actually closing the range.
        float low = At(session.Ship.ThrottleFloor);
        _thrustBlocked.Position = new Vector2(0f, ThrustHeight - low);
        _thrustBlocked.Size = new Vector2(ThrustWidth, low);
        _thrustBlocked.Visible = low > 0.5f;
        _thrustCut.Position = new Vector2(-4f, ThrustHeight - low - 1.5f);
        _thrustCut.Size = new Vector2(ThrustWidth + 8f, 3f);
        _thrustCut.Visible = _thrustBlocked.Visible;

        float high = At(session.Ship.ThrottleCeiling);
        _thrustBlockedTop.Position = Vector2.Zero;
        _thrustBlockedTop.Size = new Vector2(ThrustWidth, ThrustHeight - high);
        _thrustBlockedTop.Visible = high < ThrustHeight - 0.5f;
        _thrustCutTop.Position = new Vector2(-4f, ThrustHeight - high - 1.5f);
        _thrustCutTop.Size = new Vector2(ThrustWidth + 8f, 3f);
        _thrustCutTop.Visible = _thrustBlockedTop.Visible;
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

        // A finished run holds its numbers. The victory lap is a fresh session flying a fresh track,
        // so left alone the score would climb on distance the player never earned and the clock
        // would start again from nothing, both over the top of the results they are reading.
        if (_frozenScore is int final)
        {
            _score.Text = $"SCORE  {final}";
            _time.Text = $"RUN   {_frozenRun:0.00}";
        }
        else
        {
            _score.Text = $"SCORE  {session.Score}";
            _time.Text = $"TIME  {session.Elapsed:0.00}\nBEST  {(_best is float best ? best.ToString("0.00") : "--")}" +
                $"\nRUN   {runTime + session.Elapsed:0.00}";
        }
        var status = new List<string>(4);
        if (session.RamLeft > 0f) status.Add($"UNSTOPPABLE  {session.RamLeft:0.0}s");
        if (session.RapidFireLeft > 0f) status.Add($"RAPID FIRE  {session.RapidFireLeft:0.0}s");
        if (session.AgilityLeft > 0f) status.Add($"AGILITY  {session.AgilityLeft:0.0}s");
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

    /// <summary>Holds the score and run time at what the run ended on, for the victory lap.</summary>
    public void Freeze(int score, float runTime)
    {
        _frozenScore = score;
        _frozenRun = runTime;
    }

    public void Flash() => _flashLeft = 1f;

    public void ShowMessage(string text)
    {
        _summaryRows.Clear();
        _message.Text = text;
    }

    /// <summary>Whether a scrollable summary is up, so the caller knows to feed it scroll input.</summary>
    public bool HasSummary => _summaryRows.Count > 0;

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
