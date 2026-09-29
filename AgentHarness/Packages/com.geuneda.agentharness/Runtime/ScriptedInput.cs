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
    /// A scenario adds only the kinds of device its events use: a virtual gamepad would otherwise switch a game that
    /// shows gamepad prompts when one is connected.
    /// </summary>
    public sealed class ScriptedInput : IScenarioInput
    {
        public Keyboard Keyboard { get; private set; }
        public Mouse Mouse { get; private set; }
        public Gamepad Gamepad { get; private set; }

        readonly HashSet<Key> m_Keys = new HashSet<Key>();
        MouseState m_Mouse;
        GamepadState m_Pad;
        InputSettings m_OriginalSettings;
        InputSettings m_RuntimeSettings;

        public string Name => "inputSystem";

        public static ScriptedInput Create() => Create(true, true, true);

        public static ScriptedInput Create(bool keyboard, bool mouse, bool gamepad)
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

            if (keyboard) si.Keyboard = InputSystem.AddDevice<Keyboard>("HarnessKeyboard");
            if (mouse) si.Mouse = InputSystem.AddDevice<Mouse>("HarnessMouse");
            if (gamepad) si.Gamepad = InputSystem.AddDevice<Gamepad>("HarnessGamepad");
            si.m_Mouse = new MouseState { position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) };
            si.m_Pad = new GamepadState();
            si.Keyboard?.MakeCurrent();
            si.Mouse?.MakeCurrent();
            si.Gamepad?.MakeCurrent();
            return si;
        }

        public bool Apply(ScenarioEvent e)
        {
            switch (e.type)
            {
                case "keyDown": KeyDown(ParseEnum<Key>(KeyNames.ToInputSystemName(e.key))); return true;
                case "keyUp": KeyUp(ParseEnum<Key>(KeyNames.ToInputSystemName(e.key))); return true;
                case "mouseMove": MouseMove(new Vector2(e.x, e.y)); return true;
                case "mousePos": MousePosition(new Vector2(e.x, e.y)); return true;
                case "mouseDown": MouseButton(ParseEnum<MouseButton>(string.IsNullOrEmpty(e.key) ? "Left" : e.key), true); return true;
                case "mouseUp": MouseButton(ParseEnum<MouseButton>(string.IsNullOrEmpty(e.key) ? "Left" : e.key), false); return true;
                case "scroll": Scroll(new Vector2(e.x, e.y)); return true;
                case "stick": Stick(!string.Equals(e.key, "right", StringComparison.OrdinalIgnoreCase), new Vector2(e.x, e.y)); return true;
                case "padDown": PadButton(ParseEnum<GamepadButton>(e.key), true); return true;
                case "padUp": PadButton(ParseEnum<GamepadButton>(e.key), false); return true;
                case "releaseAll": ReleaseAll(); return true;
                default: return false;
            }
        }

        static T ParseEnum<T>(string s) where T : struct
        {
            if (!string.IsNullOrEmpty(s) && !char.IsDigit(s[0]) && Enum.TryParse<T>(s, true, out var v)) return v;
            throw new ArgumentException($"'{s}' is not a valid {typeof(T).Name}");
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
            if (Keyboard != null) PushKeyboard();
            m_Mouse = new MouseState { position = m_Mouse.position };
            if (Mouse != null) InputSystem.QueueStateEvent(Mouse, m_Mouse);
            m_Pad = new GamepadState();
            if (Gamepad != null) InputSystem.QueueStateEvent(Gamepad, m_Pad);
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

    /// <summary>
    /// Keeps real devices out of a scenario (G3-6). While a scenario plays, the native devices (the keyboard, mouse and
    /// gamepads the backend reports) are disabled, so the game gets the scenario's input only. Otherwise a key pressed in
    /// another window mixes into the replay: <see cref="ScriptedInput"/> makes input ignore Editor focus, and a binding such
    /// as "&lt;Keyboard&gt;/space" takes the real keyboard as it takes the virtual one. The devices are disabled on the
    /// managed side only (keepSendingEvents, as TouchSimulation does with the mouse): their events still arrive, and are
    /// marked handled instead of changing state; the ones with a key or button press are counted (<see cref="Report"/>).
    /// Each device is hard-reset when isolated (no key held, the pointer at 0,0), so every run starts from the same state.
    /// A device that is disabled already (by the game, TouchSimulation, or while the Editor is in the background) is left
    /// alone, and isolated if it is enabled during the scenario (focus comes back, a gamepad is plugged in). Dispose enables
    /// exactly the devices it disabled.
    /// </summary>
    public sealed class RealInputIsolation : IDisposable
    {
        // Devices disabled by an isolation and not enabled again: RestoreAll (the Editor, when play mode is over) puts them
        // back should a scenario not have ended normally.
        static readonly HashSet<InputDevice> s_Disabled = new HashSet<InputDevice>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => RestoreAll();

        readonly HashSet<InputDevice> m_Isolated = new HashSet<InputDevice>();
        readonly List<InputDevice> m_Order = new List<InputDevice>();
        readonly Dictionary<InputDevice, int> m_Presses = new Dictionary<InputDevice, int>();
        readonly Action<InputEventPtr, InputDevice> m_OnEvent;
        readonly Action<InputDevice, InputDeviceChange> m_OnDeviceChange;
        bool m_Disposed;

        RealInputIsolation()
        {
            m_OnEvent = OnEvent;
            m_OnDeviceChange = OnDeviceChange;
        }

        public static RealInputIsolation Begin()
        {
            var iso = new RealInputIsolation();
            InputSystem.onEvent += iso.m_OnEvent;
            InputSystem.onDeviceChange += iso.m_OnDeviceChange;
            foreach (var device in InputSystem.devices.ToArray()) iso.Isolate(device);   // a copy: disabling notifies onDeviceChange
            return iso;
        }

        /// <summary>Every device isolated during the scenario, with the number of its key or button presses kept from the game.</summary>
        public IsolatedDevice[] Report() => m_Order.ConvertAll(d => new IsolatedDevice { name = d.name, presses = m_Presses[d] }).ToArray();

        void Isolate(InputDevice device)
        {
            if (m_Disposed || device == null || !device.native || !device.added || !device.enabled) return;
            InputSystem.DisableDevice(device, keepSendingEvents: true);
            if (device.enabled) return;
            InputSystem.ResetDevice(device, alsoResetDontResetControls: true);
            m_Isolated.Add(device);
            s_Disabled.Add(device);
            if (m_Presses.ContainsKey(device)) return;
            m_Presses[device] = 0;
            m_Order.Add(device);
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected || change == InputDeviceChange.Enabled)
                Isolate(device);
        }

        void OnEvent(InputEventPtr eventPtr, InputDevice device)
        {
            if (device == null || !m_Isolated.Contains(device)) return;
            // Editor updates carry the Editor's input (Game view not focused), never the game's.
            if (InputState.currentUpdateType == InputUpdateType.Editor) return;
            var type = eventPtr.type;
            if (type == DeviceRemoveEvent.Type || type == DeviceConfigurationEvent.Type) return;
            eventPtr.handled = true;
            // Presses only: a device also reports its state when play mode starts or the Editor gets focus (a sync).
            if (eventPtr.HasButtonPress()) m_Presses[device]++;
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            InputSystem.onEvent -= m_OnEvent;
            InputSystem.onDeviceChange -= m_OnDeviceChange;
            foreach (var device in m_Isolated) Enable(device);
            m_Isolated.Clear();
        }

        /// <summary>Enables the devices a scenario left disabled (none when every scenario ended normally); how many.</summary>
        public static int RestoreAll()
        {
            if (s_Disabled.Count == 0) return 0;
            var n = 0;
            foreach (var device in new List<InputDevice>(s_Disabled))
                if (Enable(device)) n++;
            s_Disabled.Clear();
            return n;
        }

        static bool Enable(InputDevice device)
        {
            s_Disabled.Remove(device);
            if (!device.added || device.enabled) return false;
            try { InputSystem.EnableDevice(device); }
            catch (Exception e) { Debug.LogException(e); }
            return true;
        }
    }
}
#endif
