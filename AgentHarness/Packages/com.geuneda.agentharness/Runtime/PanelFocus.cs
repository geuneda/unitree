using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Harness
{
    /// <summary>
    /// UI Toolkit's runtime event system drops all input while the application is not focused (desktop platforms, unless Unity
    /// Remote is connected): a scenario's click on a UI Toolkit button did nothing with the Editor or player in the background
    /// (G3-14), and a loop played differently depending on which window had focus. uGUI's InputSystemUIInputModule follows
    /// runInBackground instead, which the runner turns on. While a scenario plays, the check answers "remote connected", so
    /// panels take input in the background as uGUI does (real devices are kept out by <see cref="RealInputIsolation"/>).
    /// Internal API: DefaultEventSystem.IsEditorRemoteConnected (6.0-6.6), read only by its ShouldIgnoreEventsOnAppNotFocused.
    /// Without it the panels ignore input in the background and play.uiFocusError says so.
    /// </summary>
    internal static class PanelFocus
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        static readonly FieldInfo Remote = typeof(PanelSettings).Assembly.GetType("UnityEngine.UIElements.DefaultEventSystem")?.GetField("IsEditorRemoteConnected", Any);
        static readonly Func<bool> Always = () => true;

        static Func<bool> s_Previous;
        static bool s_Running;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => End();

        /// <summary>Panels take input in the background from now on; null, or why they cannot (this Unity version lacks the internal API).</summary>
        public static string Begin()
        {
            if (s_Running) return null;
            if (Remote == null || Remote.FieldType != typeof(Func<bool>))
                return $"UI Toolkit ignores input while the application is in the background in Unity {Application.unityVersion}: DefaultEventSystem.IsEditorRemoteConnected not found";
            try
            {
                s_Previous = (Func<bool>)Remote.GetValue(null);
                Remote.SetValue(null, Always);
                s_Running = true;
                return null;
            }
            catch (Exception e) { return $"UI Toolkit ignores input while the application is in the background: {e.GetBaseException().Message}"; }
        }

        /// <summary>The check as it was.</summary>
        public static void End()
        {
            if (!s_Running) return;
            s_Running = false;
            try { if (ReferenceEquals(Remote.GetValue(null), Always)) Remote.SetValue(null, s_Previous); }
            catch (Exception) { }
            s_Previous = null;
        }
    }
}
