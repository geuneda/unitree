using System.Collections.Generic;
using UnityEngine;

namespace Harness
{
    /// <summary>
    /// Readiness signal for automation. <see cref="Ready"/> flips to true once every registered
    /// module finished Init (successfully or not — failures are listed in <see cref="FailedModules"/>).
    /// Play-mode captures and scripted input wait for it.
    /// </summary>
    public static class HarnessProbe
    {
        public static bool Ready { get; private set; }

        /// <summary>Time.realtimeSinceStartup when Ready flipped.</summary>
        public static float ReadyAt { get; private set; }

        public static readonly List<string> InitializedModules = new List<string>();
        public static readonly List<string> FailedModules = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Ready = false;
            ReadyAt = 0f;
            InitializedModules.Clear();
            FailedModules.Clear();
        }

        internal static void MarkReady()
        {
            Ready = true;
            ReadyAt = Time.realtimeSinceStartup;
        }
    }
}
