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
    public const string ThrottleUp = "throttle_up";
    public const string ThrottleDown = "throttle_down";
    public const string Jump = "jump";
    public const string Fire = "fire";
    public const string Special = "special";
    public const string Restart = "restart";
    public const string Pause = "pause";

    public static void Register()
    {
        Add(SteerLeft, KeyEvent(Key.A), KeyEvent(Key.Left), Stick(JoyAxis.LeftX, -1f));
        Add(SteerRight, KeyEvent(Key.D), KeyEvent(Key.Right), Stick(JoyAxis.LeftX, 1f));
        Add(ThrottleUp, KeyEvent(Key.W), KeyEvent(Key.Up), Button(JoyButton.RightShoulder), Stick(JoyAxis.LeftY, -1f));
        Add(ThrottleDown, KeyEvent(Key.S), KeyEvent(Key.Down), Button(JoyButton.LeftShoulder), Stick(JoyAxis.LeftY, 1f));
        Add(Jump, KeyEvent(Key.Space), Button(JoyButton.A));
        Add(Fire, KeyEvent(Key.Ctrl), KeyEvent(Key.J), KeyEvent(Key.Enter), Button(JoyButton.X),
            new InputEventMouseButton { ButtonIndex = MouseButton.Left },
            new InputEventJoypadMotion { Axis = JoyAxis.TriggerRight, AxisValue = 1f });
        Add(Special, KeyEvent(Key.E), KeyEvent(Key.K), Button(JoyButton.Y),
            new InputEventMouseButton { ButtonIndex = MouseButton.Right });
        Add(Restart, KeyEvent(Key.R), Button(JoyButton.Start));
        Add(Pause, KeyEvent(Key.P), Button(JoyButton.Back));
    }

    private static void Add(string action, params InputEvent[] events)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action, 0.2f);
        foreach (var e in events) InputMap.ActionAddEvent(action, e);
    }

    private static InputEventKey KeyEvent(Key key) => new() { PhysicalKeycode = key };

    private static InputEventJoypadButton Button(JoyButton button) => new() { ButtonIndex = button };

    private static InputEventJoypadMotion Stick(JoyAxis axis, float direction) =>
        new() { Axis = axis, AxisValue = direction };
}
