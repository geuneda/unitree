using System;
using System.Collections.Generic;
using UnityEngine;

namespace Harness
{
    /// <summary>Where scenario input goes: <see cref="ScriptedInput"/> (Input System) or <see cref="InputHookReplay"/> (the game's [AgentHarnessInput] methods).</summary>
    public interface IScenarioInput : IDisposable
    {
        string Name { get; }

        /// <summary>
        /// Apply one timeline event (keyDown, keyUp, mouseMove, mousePos, mouseDown, mouseUp, scroll, stick, padDown, padUp,
        /// releaseAll). False when this backend has no such input (a gamepad in the legacy Input Manager); throws on a bad key name.
        /// </summary>
        bool Apply(ScenarioEvent e);
    }

    /// <summary>
    /// Key and mouse button names of scenarios, for both input backends. Keys are named the Input System way ("Space",
    /// "Digit1", "Enter", "LeftCtrl", "Numpad0"); the legacy KeyCode names ("Alpha1", "Return", "LeftControl", "Keypad0")
    /// work too, and each name is translated for the other backend. A single digit is the digit key of the main row.
    /// </summary>
    public static class KeyNames
    {
        // (Input System Key, KeyCode) where the names differ (case aside).
        static readonly (string key, string code)[] s_Pairs =
        {
            ("Digit0", "Alpha0"), ("Digit1", "Alpha1"), ("Digit2", "Alpha2"), ("Digit3", "Alpha3"), ("Digit4", "Alpha4"),
            ("Digit5", "Alpha5"), ("Digit6", "Alpha6"), ("Digit7", "Alpha7"), ("Digit8", "Alpha8"), ("Digit9", "Alpha9"),
            ("Numpad0", "Keypad0"), ("Numpad1", "Keypad1"), ("Numpad2", "Keypad2"), ("Numpad3", "Keypad3"), ("Numpad4", "Keypad4"),
            ("Numpad5", "Keypad5"), ("Numpad6", "Keypad6"), ("Numpad7", "Keypad7"), ("Numpad8", "Keypad8"), ("Numpad9", "Keypad9"),
            ("NumpadEnter", "KeypadEnter"), ("NumpadPlus", "KeypadPlus"), ("NumpadMinus", "KeypadMinus"),
            ("NumpadMultiply", "KeypadMultiply"), ("NumpadDivide", "KeypadDivide"), ("NumpadPeriod", "KeypadPeriod"),
            ("NumpadEquals", "KeypadEquals"), ("Enter", "Return"), ("LeftCtrl", "LeftControl"), ("RightCtrl", "RightControl"),
            ("LeftMeta", "LeftWindows"), ("RightMeta", "RightWindows"), ("ContextMenu", "Menu"), ("PrintScreen", "Print"),
        };

        /// <summary>The Input System Key name for a scenario key name.</summary>
        public static string ToInputSystemName(string name)
        {
            var n = Normalize(name);
            foreach (var (key, code) in s_Pairs)
                if (string.Equals(n, code, StringComparison.OrdinalIgnoreCase)) return key;
            return n;
        }

        /// <summary>The legacy KeyCode for a scenario key name; throws when there is none.</summary>
        public static KeyCode ToKeyCode(string name)
        {
            var n = Normalize(name);
            foreach (var (key, code) in s_Pairs)
                if (string.Equals(n, key, StringComparison.OrdinalIgnoreCase)) { n = code; break; }
            // Enum.TryParse also takes numbers ("5" -> (KeyCode)5): names only.
            if (n.Length > 0 && char.IsLetter(n[0]) && Enum.TryParse<KeyCode>(n, true, out var kc) && kc != KeyCode.None) return kc;
            throw new ArgumentException($"'{name}' is not a key name");
        }

        /// <summary>Legacy mouse button index (Input.GetMouseButton) for "Left" (default), "Right", "Middle", "Back", "Forward".</summary>
        public static int ToMouseButton(string name)
        {
            switch ((name ?? "").Trim().ToLowerInvariant())
            {
                case "": case "left": case "0": return 0;
                case "right": case "1": return 1;
                case "middle": case "2": return 2;
                case "back": case "3": return 3;
                case "forward": case "4": return 4;
                default: throw new ArgumentException($"'{name}' is not a mouse button (Left, Right, Middle, Back, Forward)");
            }
        }

        static string Normalize(string name)
        {
            var n = (name ?? "").Trim();
            if (n.Length == 0) throw new ArgumentException("key name is empty");
            return n.Length == 1 && char.IsDigit(n[0]) ? "Digit" + n : n;
        }
    }

    /// <summary>
    /// Scenario input for games that read the legacy Input Manager (UnityEngine.Input). That one cannot be driven from code:
    /// in the Editor it reads the OS keyboard and cursor directly (Input.mousePosition follows the real cursor; events sent
    /// to the Game view reach its OnGUI but neither Input nor the game's OnGUI). So the game takes scenario input itself,
    /// through static methods marked [AgentHarnessInput] (void M(string type, string key, Vector2 value); the attribute is
    /// matched by name, so the game needs no reference to the harness). tools/templates/HarnessInput.cs is a drop-in
    /// UnityEngine.Input with such a method. The Editor finds the methods (Harness.Editor.HarnessInputHooks).
    /// Events: keyDown/keyUp (key = KeyCode name), mouseDown/mouseUp (key = button 0-4), mousePos (value = screen pixels,
    /// origin bottom left; mouseMove is sent as the new position), scroll (value), releaseAll. Every hook also gets "begin"
    /// when a scenario starts (take the scenario's input only from now on) and "end" when it is over, from
    /// <see cref="ScenarioRunner"/>, with or without input events.
    /// </summary>
    public sealed class InputHookReplay : IScenarioInput
    {
        readonly Action<string, string, Vector2> m_Hook;
        Vector2 m_Mouse;

        public string Name => "hook";

        public InputHookReplay(Action<string, string, Vector2> hook)
        {
            m_Hook = hook ?? throw new ArgumentNullException(nameof(hook));
            m_Mouse = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        }

        public bool Apply(ScenarioEvent e)
        {
            switch (e.type)
            {
                case "keyDown": case "keyUp": m_Hook(e.type, KeyNames.ToKeyCode(e.key).ToString(), default); return true;
                case "mouseDown": case "mouseUp": m_Hook(e.type, KeyNames.ToMouseButton(e.key).ToString(), m_Mouse); return true;
                case "mouseMove": m_Mouse += new Vector2(e.x, e.y); m_Hook("mousePos", null, m_Mouse); return true;
                case "mousePos": m_Mouse = new Vector2(e.x, e.y); m_Hook("mousePos", null, m_Mouse); return true;
                case "scroll": m_Hook("scroll", null, new Vector2(e.x, e.y)); return true;
                case "releaseAll": m_Hook("releaseAll", null, default); return true;
                default: return false;   // stick, padDown, padUp
            }
        }

        public void Dispose() { }   // "end" comes from the runner
    }

    /// <summary>
    /// The game's [AgentHarnessInput] methods as one delegate for <see cref="InputHookReplay"/>: static
    /// <c>void M(string type, string key, Vector2 value)</c> methods marked with an attribute class named
    /// AgentHarnessInputAttribute. The game defines that attribute itself (Tools~/templates/HarnessInput.cs), so it needs no
    /// reference to the harness and keeps compiling after uninstall. The Editor lists the marked methods with TypeCache
    /// (Harness.Editor.HarnessInputHooks), a Player run by reflection over the game's assemblies (<see cref="FindMarked"/>).
    /// </summary>
    public static class InputHooks
    {
        public const string AttributeName = "AgentHarnessInputAttribute";

        /// <summary>The methods as one delegate (null if none); <paramref name="names"/> lists them, <paramref name="errors"/> the ones with a wrong signature.</summary>
        public static Action<string, string, Vector2> Combine(IEnumerable<System.Reflection.MethodInfo> methods, List<string> names, List<string> errors)
        {
            Action<string, string, Vector2> all = null;
            foreach (var m in methods)
            {
                var name = m.DeclaringType?.FullName + "." + m.Name;
                var ps = m.GetParameters();
                if (!m.IsStatic || m.ReturnType != typeof(void) || m.ContainsGenericParameters || ps.Length != 3 ||
                    ps[0].ParameterType != typeof(string) || ps[1].ParameterType != typeof(string) || ps[2].ParameterType != typeof(Vector2))
                {
                    errors?.Add($"{name}: [AgentHarnessInput] needs static void {m.Name}(string type, string key, Vector2 value)");
                    continue;
                }
                all += (Action<string, string, Vector2>)Delegate.CreateDelegate(typeof(Action<string, string, Vector2>), m);
                names?.Add(name);
            }
            return all;
        }

        /// <summary>
        /// Methods marked [AgentHarnessInput] in the loaded assemblies that are not Unity's, .NET's or the harness's own
        /// (a Player has no TypeCache). Attributes are matched by name without creating them.
        /// </summary>
        public static List<System.Reflection.MethodInfo> FindMarked()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            var found = new List<System.Reflection.MethodInfo>();
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var n = asm.GetName().Name;
                if (n.StartsWith("Unity", StringComparison.Ordinal) || n.StartsWith("System", StringComparison.Ordinal) ||
                    n.StartsWith("Mono.", StringComparison.Ordinal) || n.StartsWith("Harness.", StringComparison.Ordinal) ||
                    n == "mscorlib" || n == "netstandard" || n == "Newtonsoft.Json") continue;
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (System.Reflection.ReflectionTypeLoadException e) { types = e.Types; }
                foreach (var t in types)
                {
                    if (t == null) continue;
                    foreach (var m in t.GetMethods(flags))
                        foreach (var a in m.CustomAttributes)
                            if (a.AttributeType.Name == AttributeName) { found.Add(m); break; }
                }
            }
            return found;
        }
    }
}
