using System;
using System.Collections.Generic;
using Godot;

namespace TubeRunner.Game;

/// <summary>
/// The Escape menu and its yes/no questions, as real controls: a title and a column of buttons on a
/// panel. It used to be one text label with the choice marked by arrows, which a mouse had nothing to
/// click on - and the settings page coming next needs toggles and sliders, which are miserable
/// without one.
///
/// Mouse, keyboard and gamepad all go through Godot's own focus handling. Hovering a button focuses
/// it, so there is only ever one highlighted choice, and the keys carry on from wherever the mouse
/// left it.
/// </summary>
public partial class GameMenu : Control
{
    private const int ButtonFontSize = 34;
    private const int TitleFontSize = 44;

    private Label _title = null!;
    private VBoxContainer _buttons = null!;
    private Color _accent = Colors.White;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        Visible = false;
        // Stops clicks from reaching anything behind it while it is up.
        MouseFilter = MouseFilterEnum.Stop;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = MouseFilterEnum.Ignore };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var panel = new PanelContainer();
        // Opaque: a start screen or run summary showing through the buttons made both unreadable.
        panel.AddThemeStyleboxOverride("panel", Box(new Color(0.03f, 0.03f, 0.04f, 1f), 2, new Color(1f, 1f, 1f, 0.2f), 32));
        center.AddChild(panel);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        panel.AddChild(column);

        _title = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        _title.AddThemeFontSizeOverride("font_size", TitleFontSize);
        column.AddChild(_title);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 4f) });

        _buttons = new VBoxContainer();
        _buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(_buttons);

        column.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 4f) });
        var hint = new Label
        {
            Text = "Click, or arrows and Enter     Esc to go back",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1f, 1f, 1f, 0.55f),
        };
        hint.AddThemeFontSizeOverride("font_size", 22);
        column.AddChild(hint);
    }

    /// <summary>The level's accent colour, used for the highlighted choice from the next time it opens.</summary>
    public void SetAccent(Color accent) => _accent = accent;

    /// <summary>
    /// Shows <paramref name="title"/> over one button per option, replacing whatever was up. The first
    /// option starts highlighted, so it should always be the one that changes nothing.
    /// </summary>
    public void Open(string title, IReadOnlyList<(string Label, Action Pick)> options)
    {
        _title.Text = title;
        foreach (var child in _buttons.GetChildren())
        {
            _buttons.RemoveChild(child);
            child.QueueFree();
        }

        Button? first = null;
        foreach (var (label, pick) in options)
        {
            var button = new Button { Text = label, CustomMinimumSize = new Vector2(460f, 64f), FocusMode = FocusModeEnum.All };
            button.AddThemeFontSizeOverride("font_size", ButtonFontSize);
            StyleButton(button);
            // Run once this frame is over, not from inside the press. A pick can rebuild the menu under
            // the button being pressed, and the same press would otherwise still read as a jump or a
            // shot when the game carries on this frame: picking Resume with Space made the ship jump.
            button.Pressed += () => Callable.From(pick).CallDeferred();
            button.MouseEntered += button.GrabFocus;
            _buttons.AddChild(button);
            first ??= button;
        }

        Visible = true;
        first?.CallDeferred(Control.MethodName.GrabFocus);
    }

    public void Close()
    {
        Visible = false;
        GetViewport()?.GuiReleaseFocus();
    }

    private void StyleButton(Button button)
    {
        button.AddThemeStyleboxOverride("normal", Box(new Color(1f, 1f, 1f, 0.05f), 2, new Color(1f, 1f, 1f, 0.16f), 12));
        button.AddThemeStyleboxOverride("hover", Box(new Color(_accent, 0.2f), 2, _accent, 12));
        button.AddThemeStyleboxOverride("pressed", Box(new Color(_accent, 0.38f), 2, _accent, 12));
        // Drawn over the others, so the keyboard's choice looks the same as the mouse's.
        button.AddThemeStyleboxOverride("focus", Box(new Color(_accent, 0.2f), 3, _accent, 12));
        foreach (var name in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
        {
            button.AddThemeColorOverride(name, Colors.White);
        }
    }

    private static StyleBoxFlat Box(Color fill, int border, Color borderColor, int margin)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = borderColor };
        box.SetBorderWidthAll(border);
        box.SetCornerRadiusAll(8);
        box.SetContentMarginAll(margin);
        return box;
    }
}
