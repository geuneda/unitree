using System;
using System.Collections.Generic;
using UnityEngine;

namespace Harness
{
    /// <summary>
    /// Typed publish/subscribe — the only channel between modules.
    /// Publish counts per event type are recorded so a play scenario can assert on gameplay
    /// mechanically (result.json → "events": {"SpinnerLap": 3}).
    /// </summary>
    public static class EventBus
    {
        static readonly Dictionary<Type, List<Delegate>> s_Handlers = new Dictionary<Type, List<Delegate>>();
        static readonly Dictionary<string, int> s_Counts = new Dictionary<string, int>();

        // Domain Reload is disabled: every mutable static is reset here.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Handlers.Clear();
            s_Counts.Clear();
        }

        public static IDisposable Subscribe<T>(Action<T> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!s_Handlers.TryGetValue(typeof(T), out var list))
                s_Handlers[typeof(T)] = list = new List<Delegate>();
            list.Add(handler);
            return new Subscription(() => list.Remove(handler));
        }

        public static void Publish<T>(T evt) => Publish(evt, typeof(T).Name);

        /// <summary>Publish, counted under <paramref name="key"/> instead of the type name (ClipPlayer: "ClipEvent:&lt;name&gt;").</summary>
        internal static void Publish<T>(T evt, string key)
        {
            s_Counts.TryGetValue(key, out var n);
            s_Counts[key] = n + 1;

            if (!s_Handlers.TryGetValue(typeof(T), out var list)) return;
            // Snapshot: handlers may unsubscribe while being invoked.
            var snapshot = list.ToArray();
            foreach (var d in snapshot)
            {
                try { ((Action<T>)d)(evt); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>Publish counts since play start, keyed by event type name.</summary>
        public static IReadOnlyDictionary<string, int> Counts => s_Counts;

        internal static void Clear()
        {
            s_Handlers.Clear();
        }

        sealed class Subscription : IDisposable
        {
            Action m_Dispose;
            public Subscription(Action dispose) { m_Dispose = dispose; }
            public void Dispose() { m_Dispose?.Invoke(); m_Dispose = null; }
        }
    }
}
