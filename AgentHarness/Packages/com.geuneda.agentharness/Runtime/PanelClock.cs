using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Harness
{
    /// <summary>
    /// UI Toolkit tells time by the real clock: USS transitions, <c>schedule.Execute</c> timers and the text caret run on
    /// real time, so with a fixed time step a capture lands at a different point of a transition every run (measured: a
    /// 1 s opacity transition read 0.73 / 0.79 / 0.79 at the same game time in three runs). While a scenario with a fixed
    /// time step plays, the panels tell time by a clock the <see cref="ScenarioRunner"/> advances by fixedDeltaTime each
    /// frame instead - continuous from the time they had, and unscaled (a paused game's menus still animate;
    /// <c>Time.unscaledTime</c> itself is real time even with a capture time step).
    /// Internal API: each runtime panel's BaseVisualElementPanel.TimeSinceStartupFunc (6.1+; found with
    /// UIElementsRuntimeUtility.GetSortedPlayerPanels), else the one clock of every panel, Panel.TimeSinceStartup (6.0: Editor
    /// windows follow it too while the scenario plays). Without either the panels keep real time and play.uiClock.error says so.
    /// </summary>
    internal static class PanelClock
    {
        const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        static readonly Assembly UiAssembly = typeof(PanelSettings).Assembly;
        static readonly Type Utility = UiAssembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
        static readonly MethodInfo SortedPanels = Utility?.GetMethod("GetSortedPlayerPanels", Any, null, Type.EmptyTypes, null);
        static readonly Type PanelBase = UiAssembly.GetType("UnityEngine.UIElements.BaseVisualElementPanel");
        static readonly PropertyInfo TimeFunc = PanelBase?.GetProperty("TimeSinceStartupFunc", Any);
        static readonly MethodInfo Seconds = PanelBase?.GetMethod("TimeSinceStartupSeconds", Any, null, Type.EmptyTypes, null);
        static readonly Type PanelType = UiAssembly.GetType("UnityEngine.UIElements.Panel");
        static readonly PropertyInfo GlobalTime = PanelType?.GetProperty("TimeSinceStartup", Any);
        static readonly MethodInfo GlobalMs = PanelType?.GetMethod("TimeSinceStartupMs", Any, null, Type.EmptyTypes, null);
        static readonly MethodInfo PanelMs = PanelBase?.GetMethod("TimeSinceStartupMs", Any, null, Type.EmptyTypes, null);

        static readonly Dictionary<object, Delegate> s_Hooked = new Dictionary<object, Delegate>();   // panel -> its previous time function
        static readonly HashSet<object> s_Seen = new HashSet<object>();
        static Delegate s_PreviousGlobal;
        static double s_Now;
        static bool s_Running, s_Global;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Hooked.Clear();
            s_Seen.Clear();
            s_PreviousGlobal = null;
            s_Now = 0;
            s_Running = false;
            s_Global = false;
        }

        static bool PerPanel => SortedPanels != null && TimeFunc != null && Seconds != null && typeof(Delegate).IsAssignableFrom(TimeFunc.PropertyType);

        /// <summary>Runtime panels that followed the frame clock.</summary>
        public static int Panels => s_Seen.Count;

        /// <summary>"runtime panels", or "every panel" where one clock serves all (6.0: Editor windows too).</summary>
        public static string Scope => s_Global ? "every panel" : "runtime panels";

        /// <summary>Start telling panel time by frames; null, or why it cannot (this Unity version lacks the internal API).</summary>
        public static string Begin()
        {
            s_Hooked.Clear();
            s_Seen.Clear();
            s_Now = 0;
            s_Global = false;
            try
            {
                if (!PerPanel)
                {
                    if (GlobalTime == null || !typeof(Delegate).IsAssignableFrom(GlobalTime.PropertyType) || SortedPanels == null)
                        return $"UI Toolkit panels keep real time in Unity {Application.unityVersion}: neither BaseVisualElementPanel.TimeSinceStartupFunc nor Panel.TimeSinceStartup found";
                    var clock = new Clock { Offset = GlobalNowMs() / 1000.0 };
                    var fn = Delegate.CreateDelegate(GlobalTime.PropertyType, clock, nameof(Clock.NowMs));
                    s_PreviousGlobal = (Delegate)GlobalTime.GetValue(null);
                    GlobalTime.SetValue(null, fn);
                    s_Global = true;
                }
                s_Running = true;
                Hook();
                return null;
            }
            catch (Exception e)
            {
                s_Running = false;
                return $"UI Toolkit panels keep real time in Unity {Application.unityVersion}: {e.GetBaseException().Message}";
            }
        }

        /// <summary>One frame: the clock moves by <paramref name="dt"/>; panels made since the last frame start following it.</summary>
        public static void Advance(double dt)
        {
            if (!s_Running) return;
            s_Now += dt;
            Hook();
        }

        /// <summary>Panels get their own time function back (they end with play mode anyway).</summary>
        public static void End()
        {
            if (!s_Running) return;
            s_Running = false;
            if (s_Global)
            {
                try { GlobalTime.SetValue(null, s_PreviousGlobal); } catch (Exception) { }
                return;
            }
            foreach (var kv in s_Hooked)
            {
                try { TimeFunc.SetValue(kv.Key, kv.Value); }
                catch (Exception) { /* a panel disposed meanwhile */ }
            }
        }

        static void Hook()
        {
            if (!(SortedPanels.Invoke(null, null) is IEnumerable panels)) return;
            foreach (var panel in panels)
            {
                if (panel == null || !s_Seen.Add(panel) || s_Global) continue;
                var clock = new Clock { Offset = (double)Seconds.Invoke(panel, null) - s_Now };
                s_Hooked[panel] = (Delegate)TimeFunc.GetValue(panel);
                TimeFunc.SetValue(panel, Delegate.CreateDelegate(TimeFunc.PropertyType, clock, nameof(Clock.Now)));
            }
        }

        /// <summary>The shared clock's reading before it is replaced (continuity): Panel.TimeSinceStartupMs, else a panel's.</summary>
        static long GlobalNowMs()
        {
            if (GlobalMs != null && GlobalMs.IsStatic) return Convert.ToInt64(GlobalMs.Invoke(null, null));
            if (PanelMs != null && SortedPanels.Invoke(null, null) is IEnumerable panels)
                foreach (var p in panels) if (p != null) return Convert.ToInt64(PanelMs.Invoke(p, null));
            return (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
        }

        sealed class Clock
        {
            public double Offset;
            public double Now() => s_Now + Offset;
            public long NowMs() => (long)((s_Now + Offset) * 1000.0);
        }
    }
}
