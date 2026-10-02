#nullable enable
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Triband.Storey.PlayKit
{
    /// <summary>
    /// The play kit's input (docs/PLAY.md, slice 5.4): keyboard (WASD or the arrows, Shift to run), a gamepad's sticks,
    /// the mouse (right-drag or middle-drag to orbit, the wheel to zoom), and touch (an on-screen joystick bottom left;
    /// a drag anywhere else orbits, a pinch zooms). The Input System when the project uses it, the old Input Manager otherwise.
    /// </summary>
    public sealed class PlayInput
    {
        /// <summary>The on-screen joystick: its centre (pixels from the bottom left), its radius, and where the thumb is.</summary>
        public Vector2 joyCentre; public float joyRadius;
        public Vector2 joyThumb; public bool joyActive;
        int joyFinger = -1;

        /// <summary>Move relative to the camera: x right, y forward, each −1..1.</summary>
        public Vector2 Move;
        public bool Run;
        /// <summary>Orbit this frame: yaw and pitch in radians.</summary>
        public Vector2 Orbit;
        /// <summary>Zoom this frame: positive closer.</summary>
        public float Zoom;

        public void Read(float dt)
        {
            float h = Screen.height;
            joyRadius = Mathf.Max(48, h * 0.09f);
            joyCentre = new Vector2(joyRadius * 1.6f, joyRadius * 1.6f);
            Move = Vector2.zero; Run = false; Orbit = Vector2.zero; Zoom = 0;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                Move.x = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1 : 0) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1 : 0);
                Move.y = (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1 : 0) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1 : 0);
                Run = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                Orbit.x += ((kb.eKey.isPressed ? 1 : 0) - (kb.qKey.isPressed ? 1 : 0)) * 1.8f * dt;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                var l = pad.leftStick.ReadValue(); if (l.sqrMagnitude > 0.02f) Move = l;
                var r = pad.rightStick.ReadValue(); Orbit += new Vector2(r.x * 2.2f, -r.y * 1.4f) * dt;
                Run |= pad.leftStickButton.isPressed || pad.rightTrigger.ReadValue() > 0.5f;
            }
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed || mouse.middleButton.isPressed) { var d = mouse.delta.ReadValue(); Orbit += new Vector2(d.x, -d.y) * 0.006f; }
                Zoom += mouse.scroll.ReadValue().y * 0.01f;
            }
            var touch = Touchscreen.current;
            joyActive = false;
            if (touch != null)
            {
                int pressed = 0; Vector2 pinchA = default, pinchB = default, pinchDa = default, pinchDb = default;
                foreach (var t in touch.touches)
                {
                    if (!t.press.isPressed) { if (t.touchId.ReadValue() == joyFinger) joyFinger = -1; continue; }
                    int id = t.touchId.ReadValue(); var pos = t.position.ReadValue();
                    if (joyFinger < 0 && t.press.wasPressedThisFrame && (pos - joyCentre).magnitude < joyRadius * 1.4f) joyFinger = id;
                    if (id == joyFinger)
                    {
                        var v = Vector2.ClampMagnitude(pos - joyCentre, joyRadius);
                        joyThumb = v; joyActive = true; Move = v / joyRadius;
                        continue;
                    }
                    if (pressed == 0) { pinchA = pos; pinchDa = t.delta.ReadValue(); } else if (pressed == 1) { pinchB = pos; pinchDb = t.delta.ReadValue(); }
                    pressed++;
                }
                if (pressed == 1) Orbit += new Vector2(pinchDa.x, -pinchDa.y) * 0.008f;
                else if (pressed >= 2)
                {
                    float now = (pinchA - pinchB).magnitude, before = ((pinchA - pinchDa) - (pinchB - pinchDb)).magnitude;
                    Zoom += (now - before) * 0.01f;
                }
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            Move.x = Input.GetAxisRaw("Horizontal"); Move.y = Input.GetAxisRaw("Vertical");
            Run = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Orbit.x += ((Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0)) * 1.8f * dt;
            if (Input.GetMouseButton(1) || Input.GetMouseButton(2)) Orbit += new Vector2(Input.GetAxis("Mouse X"), -Input.GetAxis("Mouse Y")) * 0.1f;
            Zoom += Input.mouseScrollDelta.y * 0.1f;
            joyActive = false;
#endif
            if (!joyActive) joyThumb = Vector2.zero;
            if (Move.sqrMagnitude > 1) Move.Normalize();
        }
    }
}
