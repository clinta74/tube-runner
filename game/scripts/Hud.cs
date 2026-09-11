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
    private Label _speed = null!;
    private Label _title = null!;
    private Label _message = null!;
    private ColorRect _flash = null!;
    private HBoxContainer _shieldBar = null!;
    private Color _accent = Colors.White;
    private float _titleLeft;
    private float _flashLeft;

    public override void _Ready()
    {
        _flash = new ColorRect { Color = new Color(1f, 0.15f, 0.05f, 0f), MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_flash);
        _flash.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _score = AddLabel(34, Control.LayoutPreset.TopRight, HorizontalAlignment.Right);
        _speed = AddLabel(24, Control.LayoutPreset.BottomRight, HorizontalAlignment.Right);
        _title = AddLabel(56, Control.LayoutPreset.CenterTop, HorizontalAlignment.Center);
        _message = AddLabel(44, Control.LayoutPreset.Center, HorizontalAlignment.Center);

        _shieldBar = new HBoxContainer();
        _shieldBar.AddThemeConstantOverride("separation", 10);
        AddChild(_shieldBar);
        _shieldBar.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.BottomLeft, Control.LayoutPresetMode.KeepSize, Margin);
        _shieldBar.GrowVertical = Control.GrowDirection.Begin;
    }

    public void Init(string levelName, int shields, Color accent)
    {
        _accent = accent;
        _title.Text = levelName.ToUpperInvariant();
        _titleLeft = TitleSeconds;

        foreach (var pip in _pips) pip.QueueFree();
        _pips.Clear();
        for (int i = 0; i < shields; i++)
        {
            var pip = new ColorRect { CustomMinimumSize = new Vector2(34, 14), Color = accent };
            _shieldBar.AddChild(pip);
            _pips.Add(pip);
        }
    }

    public void Update(GameSession session, float dt)
    {
        _score.Text = $"SCORE  {session.Score}";
        _speed.Text = $"{session.Ship.ForwardSpeed:0} u/s";
        for (int i = 0; i < _pips.Count; i++)
        {
            _pips[i].Color = i < session.Shields ? _accent : new Color(_accent, 0.15f);
        }

        _titleLeft = Mathf.Max(0f, _titleLeft - dt);
        _title.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(_titleLeft, 0f, 1f));
        _flashLeft = Mathf.Max(0f, _flashLeft - dt * 2.5f);
        _flash.Color = _flash.Color with { A = 0.45f * _flashLeft };
    }

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
