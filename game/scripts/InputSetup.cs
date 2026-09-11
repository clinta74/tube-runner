using Godot;

namespace TubeRunner.Game;

/// <summary>
/// Default input bindings (keyboard, mouse, gamepad), registered at startup. Actions already
/// defined in the project's Input Map are left alone, so they can be rebound there.
/// </summary>
public static class InputSetup
{
    public const string SteerLeft = "steer_left";
    public const string SteerRight = "steer_right";
    public const string Jump = "jump";
    public const string Fire = "fire";
    public const string Restart = "restart";

    public static void Register()
    {
        Add(SteerLeft, KeyEvent(Key.A), KeyEvent(Key.Left), Stick(-1f));
        Add(SteerRight, KeyEvent(Key.D), KeyEvent(Key.Right), Stick(1f));
        Add(Jump, KeyEvent(Key.Space), KeyEvent(Key.W), KeyEvent(Key.Up), Button(JoyButton.A));
        Add(Fire, KeyEvent(Key.Ctrl), KeyEvent(Key.J), KeyEvent(Key.Enter), Button(JoyButton.X),
            new InputEventMouseButton { ButtonIndex = MouseButton.Left },
            new InputEventJoypadMotion { Axis = JoyAxis.TriggerRight, AxisValue = 1f });
        Add(Restart, KeyEvent(Key.R), Button(JoyButton.Start));
    }

    private static void Add(string action, params InputEvent[] events)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action, 0.2f);
        foreach (var e in events) InputMap.ActionAddEvent(action, e);
    }

    private static InputEventKey KeyEvent(Key key) => new() { PhysicalKeycode = key };

    private static InputEventJoypadButton Button(JoyButton button) => new() { ButtonIndex = button };

    private static InputEventJoypadMotion Stick(float direction) =>
        new() { Axis = JoyAxis.LeftX, AxisValue = direction };
}
