// Hand-written declarations of the Input System (com.unity.inputsystem 1.11) members the play kit reads. Same rules as
// UnityEngine.cs: exactly what is used, bodies empty. The stub build defines ENABLE_INPUT_SYSTEM, as Unity does when a
// project's active input handling includes the Input System.

namespace UnityEngine.InputSystem.Controls
{
    public class ButtonControl { public bool isPressed => false; public bool wasPressedThisFrame => false; public float ReadValue() => 0; }
    public class KeyControl : ButtonControl { }
    public class AxisControl { public float ReadValue() => 0; }
    public class Vector2Control { public UnityEngine.Vector2 ReadValue() => default; }
    public class StickControl : Vector2Control { }
    public class DeltaControl : Vector2Control { }
    public class IntegerControl { public int ReadValue() => 0; }
    public class TouchControl
    {
        public ButtonControl press => null;
        public IntegerControl touchId => null;
        public Vector2Control position => null;
        public DeltaControl delta => null;
    }
}

namespace UnityEngine.InputSystem.Utilities
{
    public struct ReadOnlyArray<T> : System.Collections.Generic.IEnumerable<T>
    {
        public System.Collections.Generic.IEnumerator<T> GetEnumerator() { yield break; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

namespace UnityEngine.InputSystem
{
    using UnityEngine.InputSystem.Controls;

    public class Keyboard
    {
        public static Keyboard current => null;
        public KeyControl wKey => null;
        public KeyControl aKey => null;
        public KeyControl sKey => null;
        public KeyControl dKey => null;
        public KeyControl qKey => null;
        public KeyControl eKey => null;
        public KeyControl upArrowKey => null;
        public KeyControl downArrowKey => null;
        public KeyControl leftArrowKey => null;
        public KeyControl rightArrowKey => null;
        public KeyControl leftShiftKey => null;
        public KeyControl rightShiftKey => null;
    }

    public class Gamepad
    {
        public static Gamepad current => null;
        public StickControl leftStick => null;
        public StickControl rightStick => null;
        public ButtonControl leftStickButton => null;
        public ButtonControl rightTrigger => null;
    }

    public class Mouse
    {
        public static Mouse current => null;
        public ButtonControl rightButton => null;
        public ButtonControl middleButton => null;
        public DeltaControl delta => null;
        public Vector2Control scroll => null;
    }

    public class Touchscreen
    {
        public static Touchscreen current => null;
        public UnityEngine.InputSystem.Utilities.ReadOnlyArray<TouchControl> touches => default;
    }
}
