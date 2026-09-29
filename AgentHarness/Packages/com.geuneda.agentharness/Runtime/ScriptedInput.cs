// Needs the Input System package (com.unity.inputsystem -> AGENTHARNESS_INPUT_SYSTEM, see Harness.Runtime.asmdef).
#if AGENTHARNESS_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Harness
{
    /// <summary>
    /// Virtual Input System devices (keyboard, mouse, gamepad) driven from code. Game code reads them
    /// like real hardware (InputActions, Keyboard.current, …). While alive it also switches the
    /// Input System to ignore Editor/Game-view focus, so replay works with the Editor in the background.
    /// </summary>
    public sealed class ScriptedInput : IDisposable
    {
        public Keyboard Keyboard { get; private set; }
        public Mouse Mouse { get; private set; }
        public Gamepad Gamepad { get; private set; }

        readonly HashSet<Key> m_Keys = new HashSet<Key>();
        MouseState m_Mouse;
        GamepadState m_Pad;
        InputSettings m_OriginalSettings;
        InputSettings m_RuntimeSettings;

        public static ScriptedInput Create()
        {
            var si = new ScriptedInput();
            // Work on a copy so the project's settings asset is never modified.
            si.m_OriginalSettings = InputSystem.settings;
            si.m_RuntimeSettings = UnityEngine.Object.Instantiate(si.m_OriginalSettings);
            si.m_RuntimeSettings.name = "HarnessInputSettings";
            si.m_RuntimeSettings.hideFlags = HideFlags.HideAndDontSave;
            si.m_RuntimeSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            si.m_RuntimeSettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings = si.m_RuntimeSettings;

            si.Keyboard = InputSystem.AddDevice<Keyboard>("HarnessKeyboard");
            si.Mouse = InputSystem.AddDevice<Mouse>("HarnessMouse");
            si.Gamepad = InputSystem.AddDevice<Gamepad>("HarnessGamepad");
            si.m_Mouse = new MouseState { position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) };
            si.m_Pad = new GamepadState();
            si.Keyboard.MakeCurrent();
            si.Mouse.MakeCurrent();
            si.Gamepad.MakeCurrent();
            return si;
        }

        // ---- Keyboard -------------------------------------------------------------------------

        public void KeyDown(Key key) { m_Keys.Add(key); PushKeyboard(); }
        public void KeyUp(Key key) { m_Keys.Remove(key); PushKeyboard(); }

        void PushKeyboard()
        {
            var keys = new Key[m_Keys.Count];
            m_Keys.CopyTo(keys);
            InputSystem.QueueStateEvent(Keyboard, new KeyboardState(keys));
            Keyboard.MakeCurrent();
        }

        // ---- Mouse ------------------------------------------------------------------------------

        public void MouseMove(Vector2 delta)
        {
            m_Mouse.position += delta;
            m_Mouse.delta = delta;
            InputSystem.QueueStateEvent(Mouse, m_Mouse);
            m_Mouse.delta = Vector2.zero; // delta is per-event; the next event resets it
            Mouse.MakeCurrent();
        }

        public void MousePosition(Vector2 position)
        {
            m_Mouse.delta = position - m_Mouse.position;
            m_Mouse.position = position;
            InputSystem.QueueStateEvent(Mouse, m_Mouse);
            m_Mouse.delta = Vector2.zero;
            Mouse.MakeCurrent();
        }

        public void MouseButton(UnityEngine.InputSystem.LowLevel.MouseButton button, bool down)
        {
            m_Mouse = m_Mouse.WithButton(button, down);
            InputSystem.QueueStateEvent(Mouse, m_Mouse);
            Mouse.MakeCurrent();
        }

        public void Scroll(Vector2 scroll)
        {
            m_Mouse.scroll = scroll;
            InputSystem.QueueStateEvent(Mouse, m_Mouse);
            m_Mouse.scroll = Vector2.zero;
            Mouse.MakeCurrent();
        }

        // ---- Gamepad ----------------------------------------------------------------------------

        public void Stick(bool left, Vector2 value)
        {
            if (left) m_Pad.leftStick = value; else m_Pad.rightStick = value;
            InputSystem.QueueStateEvent(Gamepad, m_Pad);
            Gamepad.MakeCurrent();
        }

        public void PadButton(GamepadButton button, bool down)
        {
            m_Pad = m_Pad.WithButton(button, down);
            InputSystem.QueueStateEvent(Gamepad, m_Pad);
            Gamepad.MakeCurrent();
        }

        /// <summary>Release everything (keys up, buttons up, sticks centered).</summary>
        public void ReleaseAll()
        {
            m_Keys.Clear();
            PushKeyboard();
            m_Mouse = new MouseState { position = m_Mouse.position };
            InputSystem.QueueStateEvent(Mouse, m_Mouse);
            m_Pad = new GamepadState();
            InputSystem.QueueStateEvent(Gamepad, m_Pad);
        }

        public void Dispose()
        {
            if (Keyboard != null && Keyboard.added) InputSystem.RemoveDevice(Keyboard);
            if (Mouse != null && Mouse.added) InputSystem.RemoveDevice(Mouse);
            if (Gamepad != null && Gamepad.added) InputSystem.RemoveDevice(Gamepad);
            Keyboard = null; Mouse = null; Gamepad = null;
            if (m_OriginalSettings != null && InputSystem.settings == m_RuntimeSettings)
                InputSystem.settings = m_OriginalSettings;
            if (m_RuntimeSettings != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(m_RuntimeSettings);
                else UnityEngine.Object.DestroyImmediate(m_RuntimeSettings);
            }
            m_RuntimeSettings = null;
        }
    }
}
#endif
