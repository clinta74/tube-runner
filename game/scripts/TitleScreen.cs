using System;
using System.Collections.Generic;
using Godot;

namespace TubeRunner.Game;

/// <summary>
/// The title: the game's name and a column of choices down the left, over a tube the ship is
/// already flying. Nothing is dimmed and nothing is boxed in, because what is behind it is the game
/// itself and Start hands it over without a cut - the menu goes, the tube stays.
///
/// Pages that need room (settings, the zone list, best times) open in the Escape menu's panel over
/// the top, and this steps aside while one is up so there is only ever one set of buttons to focus.
///
/// Left alone for a while it fades out and leaves the flying to look at. Whatever wakes it is
/// swallowed, so the key that brought the menu back never also picks something from it.
/// </summary>
public partial class TitleScreen : Control
{
    private const float AttractAfter = 20f;
    private const float FadeSeconds = 0.5f;
    private const int Left = 96;

    private Label _name = null!;
    private ColorRect _rule = null!;
    private VBoxContainer _buttons = null!;
    private Label _hint = null!;
    private Label _version = null!;
    private Label _notice = null!;
    private Label _wake = null!;
    private Control _content = null!;
    private Color _accent = Colors.White;

    private float _alpha;
    private float _target;
    private float _idle;
    private float _pulse;
    private bool _asleep;
    private bool _leaving;
    // Whether the mouse has moved since the buttons went up. A cursor left lying where a button then
    // appears has not chosen it, and should not take the focus off the first choice.
    private bool _mouseMoved;
    private ulong _wokeOn;
    private Control? _focused;

    /// <summary>Whether this frame's input was spent waking the screen, and so should do nothing else.</summary>
    public bool JustWoke => _wokeOn == Engine.GetProcessFrames();

    /// <summary>Whether the root of the title is up, rather than hidden behind one of its pages.</summary>
    public bool AtRoot => Visible && _content.Visible && !_leaving;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _content = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_content);
        _content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // Shade behind the column, so white text holds up over a lit wall. It runs out to nothing
        // well short of the middle, which is where the ship is.
        var gradient = new Gradient();
        gradient.SetColor(0, new Color(0f, 0f, 0f, 0.78f));
        gradient.SetColor(1, new Color(0f, 0f, 0f, 0f));
        var shade = new TextureRect
        {
            Texture = new GradientTexture2D { Gradient = gradient, Width = 256, Height = 4 },
            StretchMode = TextureRect.StretchModeEnum.Scale,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _content.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.LeftWide);
        shade.AnchorRight = 0.48f;
        shade.OffsetRight = 0f;

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 0);
        _content.AddChild(column);
        column.SetAnchorsAndOffsetsPreset(LayoutPreset.LeftWide);
        column.OffsetLeft = Left;
        column.OffsetRight = Left + 620;
        column.Alignment = BoxContainer.AlignmentMode.Center;

        _name = new Label
        {
            MouseFilter = MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings
            {
                Font = new FontVariation { SpacingGlyph = 10 },
                FontSize = 104,
                OutlineSize = 14,
                OutlineColor = new Color(0f, 0f, 0f, 0.85f),
            },
        };
        column.AddChild(_name);

        var ruleRow = new Control { CustomMinimumSize = new Vector2(0f, 46f), MouseFilter = MouseFilterEnum.Ignore };
        _rule = new ColorRect { Size = new Vector2(180f, 5f), Position = new Vector2(6f, 8f), MouseFilter = MouseFilterEnum.Ignore };
        ruleRow.AddChild(_rule);
        column.AddChild(ruleRow);

        _buttons = new VBoxContainer();
        _buttons.AddThemeConstantOverride("separation", 8);
        _buttons.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        column.AddChild(_buttons);

        _hint = Corner("Arrows or mouse, Enter to pick     Esc to quit", 22, LayoutPreset.BottomLeft, HorizontalAlignment.Left);
        _hint.OffsetLeft = Left;
        _hint.Modulate = new Color(1f, 1f, 1f, 0.55f);
        _version = Corner("", 22, LayoutPreset.BottomRight, HorizontalAlignment.Right);
        _version.Modulate = new Color(1f, 1f, 1f, 0.55f);
        _notice = Corner("", 26, LayoutPreset.TopRight, HorizontalAlignment.Right);

        // Outside the content, so it can come up as the rest goes down.
        _wake = new Label
        {
            Text = "Press any key",
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { FontSize = 26, OutlineSize = 6, OutlineColor = new Color(0f, 0f, 0f, 0.8f) },
        };
        AddChild(_wake);
        _wake.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom, LayoutPresetMode.KeepSize, 40);
        _wake.GrowHorizontal = GrowDirection.Both;
        _wake.GrowVertical = GrowDirection.Begin;
        _wake.Modulate = new Color(1f, 1f, 1f, 0f);
    }

    private Label Corner(string text, int size, LayoutPreset preset, HorizontalAlignment align)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = align,
            MouseFilter = MouseFilterEnum.Ignore,
            LabelSettings = new LabelSettings { FontSize = size, OutlineSize = size / 4, OutlineColor = new Color(0f, 0f, 0f, 0.8f) },
        };
        _content.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(preset, LayoutPresetMode.KeepSize, 32);
        label.GrowHorizontal = align == HorizontalAlignment.Right ? GrowDirection.Begin : GrowDirection.End;
        label.GrowVertical = preset is LayoutPreset.TopRight ? GrowDirection.End : GrowDirection.Begin;
        return label;
    }

    /// <summary>
    /// Puts the title up with one button per option, the first of them focused. Called again to
    /// come back from a page, or when what is on offer has changed.
    /// </summary>
    /// <param name="notice">A line for the top right corner, e.g. that a newer version is out.</param>
    public void Open(string name, string version, string notice, Color accent,
        IReadOnlyList<(string Label, Action Pick)> options)
    {
        _accent = accent;
        _name.Text = name.ToUpperInvariant();
        _rule.Color = accent;
        _version.Text = version;
        _notice.Text = notice;
        _notice.Modulate = accent;

        foreach (var child in _buttons.GetChildren())
        {
            _buttons.RemoveChild(child);
            child.QueueFree();
        }

        Button? first = null;
        foreach (var (label, pick) in options)
        {
            var button = new Button
            {
                Text = label,
                CustomMinimumSize = new Vector2(400f, 58f),
                FocusMode = FocusModeEnum.All,
                Alignment = HorizontalAlignment.Left,
            };
            button.AddThemeFontSizeOverride("font_size", 30);
            GameMenu.StyleButton(button, accent, margin: 18);
            // Deferred for the same reason the Escape menu's are: a pick can rebuild this list under
            // the button being pressed.
            button.Pressed += () =>
            {
                if (!_leaving && !_asleep) Callable.From(pick).CallDeferred();
            };
            button.MouseEntered += () =>
            {
                if (!_asleep && _mouseMoved) button.GrabFocus();
            };
            button.FocusEntered += () => _focused = button;
            _buttons.AddChild(button);
            first ??= button;
        }

        bool fresh = !Visible;
        Visible = true;
        _content.Visible = true;
        _leaving = false;
        _asleep = false;
        _mouseMoved = false;
        _idle = 0f;
        _target = 1f;
        if (fresh) _alpha = 0f;
        _focused = first;
        FocusLater(first);
    }

    /// <summary>Steps aside for a page opened over the top. <see cref="Open"/> brings it back.</summary>
    public void StepAside()
    {
        _content.Visible = false;
        GetViewport()?.GuiReleaseFocus();
    }

    /// <summary>Fades out for good: a run is starting, and the tube behind carries on into it.</summary>
    public void Leave()
    {
        _leaving = true;
        _target = 0f;
        GetViewport()?.GuiReleaseFocus();
    }

    /// <summary>Takes it down at once, for leaving the title by a cut.</summary>
    public void Close()
    {
        Visible = false;
        _leaving = false;
        GetViewport()?.GuiReleaseFocus();
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        float dt = (float)delta;

        if (AtRoot && !_asleep)
        {
            _idle += dt;
            if (_idle >= AttractAfter) Sleep();
        }

        _alpha = Mathf.MoveToward(_alpha, _target, dt / FadeSeconds);
        _content.Modulate = new Color(1f, 1f, 1f, _alpha);
        if (_leaving && _alpha <= 0f) Visible = false;

        // The invitation back breathes, so a screen left showing the game does not look crashed.
        _pulse += dt;
        float wake = _asleep ? (1f - _alpha) * (0.55f + 0.35f * Mathf.Sin(_pulse * 2.2f)) : 0f;
        _wake.Modulate = new Color(1f, 1f, 1f, wake);
    }

    public override void _Input(InputEvent e)
    {
        if (!Visible || _leaving || !_content.Visible) return;
        if (!IsActivity(e)) return;

        if (e is InputEventMouseMotion && !_mouseMoved)
        {
            // The button already under the cursor never gets another "entered", so ask now.
            _mouseMoved = true;
            foreach (var child in _buttons.GetChildren())
            {
                if (child is Button b && b.GetGlobalRect().HasPoint(GetGlobalMousePosition()) && !_asleep) b.GrabFocus();
            }
        }
        _idle = 0f;
        if (!_asleep) return;

        _asleep = false;
        _target = 1f;
        _wokeOn = Engine.GetProcessFrames();
        FocusLater(_focused);
        GetViewport().SetInputAsHandled();
    }

    private void Sleep()
    {
        _asleep = true;
        _target = 0f;
        GetViewport()?.GuiReleaseFocus();
    }

    // A stick resting a hair off centre is not someone coming back to the keyboard.
    private static bool IsActivity(InputEvent e) => e switch
    {
        InputEventKey key => key.Pressed,
        InputEventMouseButton button => button.Pressed,
        InputEventJoypadButton button => button.Pressed,
        InputEventJoypadMotion motion => Mathf.Abs(motion.AxisValue) > 0.5f,
        InputEventMouseMotion motion => motion.Relative.LengthSquared() > 4f,
        _ => false,
    };

    private void FocusLater(Control? control) =>
        Callable.From(() =>
        {
            if (control is not null && IsInstanceValid(control) && control.IsInsideTree() && AtRoot && !_asleep)
            {
                control.GrabFocus();
            }
        }).CallDeferred();
}
