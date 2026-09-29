// AgentHarness input shim for games that read the legacy Input Manager (UnityEngine.Input).
//
// The legacy Input Manager cannot be driven from code: in the Editor it reads the OS keyboard and cursor directly, so
// the scenario player of AgentHarness (tools/loop.ps1) has no way to press a key for Input.GetKey. Read input through
// this class instead - same member names, so `Input.` -> `HarnessInput.` is the whole change:
//   - outside scenarios every member is UnityEngine.Input: real input works exactly as before;
//   - while a scenario plays in the Editor, every member returns the scenario's input only - its keyDown/keyUp/mouse*/
//     click/scroll events, the mouse at the screen center until the scenario moves it - so a key pressed in another
//     window cannot change the replay. The harness finds the [AgentHarnessInput] method below by the attribute's name
//     and calls it (no reference to the harness package): "begin" when a scenario starts, then its input events, "end";
//   - nothing calls the hook outside the Editor, so in a build every member is just UnityEngine.Input.
// GetAxis/GetButton know Unity's default axes (Horizontal, Vertical, Fire1-3, Jump, Submit, Cancel, Mouse X/Y,
// Mouse ScrollWheel); scripted axis values are raw (-1, 0, 1: no gravity/sensitivity smoothing). GetKey(string),
// inputString and touches have no scripted input: nothing while a scenario plays.
// This file belongs to the game (tools/uninstall.ps1 leaves it). Your own input layer can take scenario input the same
// way: any static void M(string type, string key, Vector2 value) marked [AgentHarnessInput].
// An unchanged copy of an older version of this file is replaced by install.ps1 -InputShim.
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a static method <c>void M(string type, string key, Vector2 value)</c> that the AgentHarness scenario player calls
/// in Editor play mode. type: begin (a scenario starts: its input only from now on), keyDown/keyUp (key = KeyCode name),
/// mouseDown/mouseUp (key = button 0-4), mousePos (value = screen pixels, origin bottom left), scroll (value = delta),
/// releaseAll, end (the scenario is over).
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AgentHarnessInputAttribute : Attribute { }

/// <summary>UnityEngine.Input, or the input of an AgentHarness scenario while one plays (Editor play mode).</summary>
public static class HarnessInput
{
    public static bool GetKey(KeyCode key) => s_Scenario ? Held(key) : Input.GetKey(key);
    public static bool GetKeyDown(KeyCode key) => s_Scenario ? ThisFrame(s_KeyDown, key) : Input.GetKeyDown(key);
    public static bool GetKeyUp(KeyCode key) => s_Scenario ? ThisFrame(s_KeyUp, key) : Input.GetKeyUp(key);
    public static bool GetKey(string name) => !s_Scenario && Input.GetKey(name);
    public static bool GetKeyDown(string name) => !s_Scenario && Input.GetKeyDown(name);
    public static bool GetKeyUp(string name) => !s_Scenario && Input.GetKeyUp(name);

    public static bool GetMouseButton(int button) => s_Scenario ? ButtonHeld(button) : Input.GetMouseButton(button);
    public static bool GetMouseButtonDown(int button) => s_Scenario ? ButtonThisFrame(s_ButtonDown, button) : Input.GetMouseButtonDown(button);
    public static bool GetMouseButtonUp(int button) => s_Scenario ? ButtonThisFrame(s_ButtonUp, button) : Input.GetMouseButtonUp(button);

    public static Vector3 mousePosition => s_Scenario ? (Vector3)ScriptedMouse() : Input.mousePosition;
    public static Vector2 mouseScrollDelta => s_Scenario ? ScriptedScroll() : Input.mouseScrollDelta;
    public static bool mousePresent => Input.mousePresent;
    public static bool anyKey => s_Scenario ? AnyHeld() : Input.anyKey;
    public static bool anyKeyDown => s_Scenario ? AnyThisFrame() : Input.anyKeyDown;
    public static string inputString => s_Scenario ? "" : Input.inputString;
    public static int touchCount => s_Scenario ? 0 : Input.touchCount;
    public static Touch GetTouch(int index) => s_Scenario ? throw new ArgumentException("Index out of bounds.") : Input.GetTouch(index);

    public static float GetAxis(string axisName) => s_Scenario ? Mathf.Clamp(ScriptedAxis(axisName), -1f, 1f) : Input.GetAxis(axisName);
    public static float GetAxisRaw(string axisName) => s_Scenario ? Mathf.Clamp(ScriptedAxis(axisName), -1f, 1f) : Input.GetAxisRaw(axisName);
    public static bool GetButton(string buttonName) => s_Scenario ? ScriptedButton(buttonName, 0) : Input.GetButton(buttonName);
    public static bool GetButtonDown(string buttonName) => s_Scenario ? ScriptedButton(buttonName, 1) : Input.GetButtonDown(buttonName);
    public static bool GetButtonUp(string buttonName) => s_Scenario ? ScriptedButton(buttonName, 2) : Input.GetButtonUp(buttonName);
    public static void ResetInputAxes() { Input.ResetInputAxes(); ClearScripted(); }

    static bool s_Scenario;   // a scenario plays: its input only
    static readonly HashSet<KeyCode> s_Held = new HashSet<KeyCode>();
    static readonly Dictionary<KeyCode, int> s_KeyDown = new Dictionary<KeyCode, int>();
    static readonly Dictionary<KeyCode, int> s_KeyUp = new Dictionary<KeyCode, int>();
    static readonly bool[] s_Buttons = new bool[5];
    static readonly int[] s_ButtonDown = { -1, -1, -1, -1, -1 };
    static readonly int[] s_ButtonUp = { -1, -1, -1, -1, -1 };
    static bool s_HasMouse;
    static Vector2 s_Mouse, s_MouseDelta, s_Scroll;
    static int s_MouseDeltaFrame = -1, s_ScrollFrame = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Clear()
    {
        s_Scenario = false;
        ClearScripted();
    }

    static void ClearScripted()
    {
        s_Held.Clear(); s_KeyDown.Clear(); s_KeyUp.Clear();
        for (var i = 0; i < s_Buttons.Length; i++) { s_Buttons[i] = false; s_ButtonDown[i] = -1; s_ButtonUp[i] = -1; }
        s_HasMouse = false; s_MouseDeltaFrame = -1; s_ScrollFrame = -1;
    }

    /// <summary>Called by the AgentHarness scenario player (Editor play mode) before the game's Update of that frame.</summary>
    [AgentHarnessInput]
    static void OnScenarioInput(string type, string key, Vector2 value)
    {
        if (type == "end") { Clear(); return; }
        s_Scenario = true;   // "begin", or the first input event from a harness that sends no "begin"
        var frame = Time.frameCount;
        switch (type)
        {
            case "begin":
                ClearScripted();
                break;
            case "keyDown":
            case "keyUp":
            {
                var k = (KeyCode)Enum.Parse(typeof(KeyCode), key);
                if (type == "keyDown") { if (s_Held.Add(k)) s_KeyDown[k] = frame; }
                else if (s_Held.Remove(k)) s_KeyUp[k] = frame;
                break;
            }
            case "mouseDown":
            case "mouseUp":
            {
                var b = int.Parse(key);
                if (b < 0 || b >= s_Buttons.Length) break;
                var down = type == "mouseDown";
                if (s_Buttons[b] == down) break;
                s_Buttons[b] = down;
                if (down) s_ButtonDown[b] = frame; else s_ButtonUp[b] = frame;
                break;
            }
            case "mousePos":
                if (s_MouseDeltaFrame != frame) { s_MouseDelta = Vector2.zero; s_MouseDeltaFrame = frame; }
                s_MouseDelta += value - ScriptedMouse();
                s_Mouse = value;
                s_HasMouse = true;
                break;
            case "scroll":
                if (s_ScrollFrame != frame) { s_Scroll = Vector2.zero; s_ScrollFrame = frame; }
                s_Scroll += value;
                break;
            case "releaseAll":
                foreach (var k in new List<KeyCode>(s_Held)) OnScenarioInput("keyUp", k.ToString(), default);
                for (var b = 0; b < s_Buttons.Length; b++) if (s_Buttons[b]) OnScenarioInput("mouseUp", b.ToString(), default);
                break;
        }
    }

    static bool Held(KeyCode key) => s_Held.Contains(key);
    static bool ThisFrame(Dictionary<KeyCode, int> frames, KeyCode key) => frames.TryGetValue(key, out var f) && f == Time.frameCount;
    static bool ButtonHeld(int b) => b >= 0 && b < s_Buttons.Length && s_Buttons[b];
    static bool ButtonThisFrame(int[] frames, int b) => b >= 0 && b < frames.Length && frames[b] == Time.frameCount;
    static bool AnyHeld() { if (s_Held.Count > 0) return true; foreach (var b in s_Buttons) if (b) return true; return false; }
    static bool AnyThisFrame()
    {
        var frame = Time.frameCount;
        foreach (var f in s_KeyDown.Values) if (f == frame) return true;
        foreach (var f in s_ButtonDown) if (f == frame) return true;
        return false;
    }
    static Vector2 ScriptedMouse() => s_HasMouse ? s_Mouse : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
    static Vector2 ScriptedScroll() => s_ScrollFrame == Time.frameCount ? s_Scroll : Vector2.zero;

    // Unity's default Input Manager axes and buttons, from the scripted keys.
    static float ScriptedAxis(string axis)
    {
        switch (axis)
        {
            case "Horizontal": return Pair(KeyCode.RightArrow, KeyCode.D) - Pair(KeyCode.LeftArrow, KeyCode.A);
            case "Vertical": return Pair(KeyCode.UpArrow, KeyCode.W) - Pair(KeyCode.DownArrow, KeyCode.S);
            case "Mouse X": return s_MouseDeltaFrame == Time.frameCount ? s_MouseDelta.x * 0.1f : 0f;
            case "Mouse Y": return s_MouseDeltaFrame == Time.frameCount ? s_MouseDelta.y * 0.1f : 0f;
            case "Mouse ScrollWheel": return ScriptedScroll().y * 0.1f;
            default: return ScriptedButton(axis, 0) ? 1f : 0f;
        }
    }

    static float Pair(KeyCode a, KeyCode b) => Held(a) || Held(b) ? 1f : 0f;

    // when: 0 = held, 1 = pressed this frame, 2 = released this frame
    static bool ScriptedButton(string button, int when)
    {
        switch (button)
        {
            case "Fire1": return Key(KeyCode.LeftControl, when) || Button(0, when);
            case "Fire2": return Key(KeyCode.LeftAlt, when) || Button(1, when);
            case "Fire3": return Key(KeyCode.LeftShift, when) || Button(2, when);
            case "Jump": return Key(KeyCode.Space, when);
            case "Submit": return Key(KeyCode.Return, when) || Key(KeyCode.KeypadEnter, when) || Key(KeyCode.Space, when);
            case "Cancel": return Key(KeyCode.Escape, when);
            default: return false;
        }
    }

    static bool Key(KeyCode k, int when) => when == 0 ? Held(k) : when == 1 ? ThisFrame(s_KeyDown, k) : ThisFrame(s_KeyUp, k);
    static bool Button(int b, int when) => when == 0 ? ButtonHeld(b) : when == 1 ? ButtonThisFrame(s_ButtonDown, b) : ButtonThisFrame(s_ButtonUp, b);
}
