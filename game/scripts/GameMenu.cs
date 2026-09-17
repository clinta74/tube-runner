using System;
using System.Collections.Generic;
using Godot;
using TubeRunner.Core;

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
        ClearRows();

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
        if (first is not null) FocusLater(first);
    }

    /// <summary>
    /// The settings page. Every change is reported through <paramref name="changed"/> as it happens -
    /// there is no separate apply, so what the player hears and sees is always what is saved.
    /// </summary>
    /// <param name="back">Called by the Back button, once this frame is over.</param>
    public void OpenSettings(GameSettings current, Action<GameSettings> changed, Action back)
    {
        _title.Text = "SETTINGS";
        ClearRows();
        var settings = current;
        void Change(Func<GameSettings, GameSettings> edit)
        {
            settings = edit(settings);
            changed(settings);
        }

        var updates = Toggle(settings.CheckForUpdates, on => Change(s => s with { CheckForUpdates = on }));
        AddRow("Check for updates", updates);

        var screen = new OptionButton();
        // In the same order as ScreenMode, so an item's index is its value.
        screen.AddItem("Windowed");
        screen.AddItem("Fullscreen");
        screen.AddItem("Borderless");
        screen.Selected = (int)settings.ScreenMode;
        screen.GetPopup().AddThemeFontSizeOverride("font_size", RowFontSize);
        screen.ItemSelected += index => Change(s => s with { ScreenMode = (ScreenMode)(int)index });
        AddRow("Screen", screen);

        AddRow("VSync", Toggle(settings.VSync, on => Change(s => s with { VSync = on })));

        AddRow("Master volume", VolumeSlider(settings.MasterVolume, v => Change(s => s with { MasterVolume = v })));
        AddRow("Music", VolumeSlider(settings.MusicVolume, v => Change(s => s with { MusicVolume = v })));
        AddRow("Effects", VolumeSlider(settings.EffectsVolume, v => Change(s => s with { EffectsVolume = v })));

        var done = new Button { Text = "Back", CustomMinimumSize = new Vector2(460f, 64f), FocusMode = FocusModeEnum.All };
        done.AddThemeFontSizeOverride("font_size", ButtonFontSize);
        StyleButton(done);
        done.Pressed += () => Callable.From(back).CallDeferred();
        done.MouseEntered += done.GrabFocus;
        var center = new CenterContainer();
        center.AddChild(done);
        _buttons.AddChild(center);

        Visible = true;
        FocusLater(updates);
    }

    // Focus once the new controls are laid out. Skipped if something replaced the page first in the
    // same frame - opening the menu and then its settings page straight away does exactly that.
    private static void FocusLater(Control control) =>
        Callable.From(() =>
        {
            if (IsInstanceValid(control) && control.IsInsideTree()) control.GrabFocus();
        }).CallDeferred();

    public void Close()
    {
        Visible = false;
        GetViewport()?.GuiReleaseFocus();
    }

    private const int RowFontSize = 30;

    private void ClearRows()
    {
        foreach (var child in _buttons.GetChildren())
        {
            _buttons.RemoveChild(child);
            child.QueueFree();
        }
    }

    // A setting's name on the left and its control on the right. Every control focuses on hover, the
    // same rule as the menu's buttons, so the mouse and the keys never disagree about what is selected.
    private void AddRow(string name, Control control)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);

        var label = new Label { Text = name, CustomMinimumSize = new Vector2(280f, 0f), VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", RowFontSize);
        row.AddChild(label);

        control.CustomMinimumSize = new Vector2(360f, 56f);
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        control.FocusMode = FocusModeEnum.All;
        control.AddThemeFontSizeOverride("font_size", RowFontSize);
        control.AddThemeStyleboxOverride("focus", Box(new Color(_accent, 0.12f), 3, _accent, 8));
        control.MouseEntered += () => control.GrabFocus();
        row.AddChild(control);

        _buttons.AddChild(row);
    }

    // An on/off switch that says which it is. Godot's switch alone is a small grey pill, and at a
    // glance on and off look the same.
    private static CheckButton Toggle(bool value, Action<bool> changed)
    {
        var toggle = new CheckButton { ButtonPressed = value, Text = value ? "On" : "Off" };
        toggle.Toggled += on =>
        {
            toggle.Text = on ? "On" : "Off";
            changed(on);
        };
        return toggle;
    }

    // A 0-100 slider with its value beside it, in steps of five. Left and right move it once it has
    // focus, as do A and D and the stick.
    private HBoxContainer VolumeSlider(float value, Action<float> changed)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 16);

        var slider = new HSlider
        {
            MinValue = 0,
            MaxValue = 100,
            Step = 5,
            Value = Math.Round(value * 100f),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ShrinkCenter,
            FocusMode = FocusModeEnum.All,
        };
        slider.AddThemeStyleboxOverride("focus", Box(new Color(_accent, 0.12f), 3, _accent, 8));
        slider.MouseEntered += slider.GrabFocus;

        var readout = new Label { Text = $"{slider.Value:0}%", CustomMinimumSize = new Vector2(90f, 0f), HorizontalAlignment = HorizontalAlignment.Right };
        readout.AddThemeFontSizeOverride("font_size", RowFontSize);

        slider.ValueChanged += v =>
        {
            readout.Text = $"{v:0}%";
            changed((float)(v / 100.0));
        };

        box.AddChild(slider);
        box.AddChild(readout);
        // The row focuses the slider rather than the box around it, which can't be moved with keys.
        box.FocusEntered += slider.GrabFocus;
        return box;
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
