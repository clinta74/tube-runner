using Godot;

namespace TubeRunner.Game;

/// <summary>
/// Full-screen speed effects (speed_fx.gdshader): blur, color split, streaks, and vignette that
/// grow with the ship's speed. Sits below the HUD so text stays sharp.
/// </summary>
public partial class SpeedFx : CanvasLayer
{
    private ShaderMaterial _material = null!;

    /// <summary>At or below this speed there is no effect.</summary>
    [Export] public float StartSpeed { get; set; } = 50f;

    /// <summary>At or above this speed the effect is full strength.</summary>
    [Export] public float FullSpeed { get; set; } = 170f;

    /// <summary>Smoothed effect strength, 0 to 1; also useful for camera effects.</summary>
    public float Intensity { get; private set; }

    public override void _Ready()
    {
        Layer = 0;
        _material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/speed_fx.gdshader") };
        var rect = new ColorRect { Material = _material, MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(rect);
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    public void SetStreakColor(Color color) =>
        _material.SetShaderParameter("streak_color", new Vector3(color.R, color.G, color.B));

    public void Update(float speed, float dt)
    {
        float target = Mathf.Clamp((speed - StartSpeed) / (FullSpeed - StartSpeed), 0f, 1f);
        Intensity = Mathf.Lerp(Intensity, target, 1f - Mathf.Exp(-4f * dt));
        _material.SetShaderParameter("intensity", Intensity);
    }
}
