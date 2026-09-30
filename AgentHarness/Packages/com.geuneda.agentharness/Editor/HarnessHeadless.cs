using System;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Harness.Editor
{
    /// <summary>
    /// How this Editor was started (tools/open.ps1, W7 G2-2). A headless Editor is -batchmode without -quit (open.ps1
    /// -Headless or -Own): it stays up, serves the Pipeline commands and renders with the GPU, but has no window and no
    /// Game view. Automated (-automated, open.ps1's default): EditorUtility.DisplayDialog* return their default (cancel)
    /// at once instead of waiting for a person.
    ///
    /// Unity's batch-mode main loop never waits: an idle headless Editor ran ~60,000 update ticks a second (1.2 cores). While
    /// it has nothing to do (not playing, compiling or importing), each tick here sleeps 1 ms, with the Windows timer at
    /// 1 ms for this process (at the default 15.6 ms every main-thread command waited for a sleeping tick: ping 17 → 32 ms).
    /// </summary>
    [InitializeOnLoad]
    public static class HarnessHeadless
    {
        /// <summary>A resident batch-mode Editor (not an asset import worker, not a one-shot -batchmode -quit run).</summary>
        public static bool IsHeadless => Application.isBatchMode && !OneShot && !AssetDatabase.IsAssetImportWorkerProcess();

        /// <summary>-batchmode -quit: a single job (the bootstrap import, a CI build) that exits when done.</summary>
        public static bool OneShot => HasArgument("-quit");

        /// <summary>Started with -automated: dialogs answer themselves (UnityEditorInternal.InternalEditorUtility.isHumanControllingUs is false).</summary>
        public static bool Automated => HasArgument("-automated");

        public static string Mode => Application.isBatchMode ? "headless" : "window";

        static HarnessHeadless()
        {
            if (!IsHeadless) return;
            if (Application.platform == RuntimePlatform.WindowsEditor && !SessionState.GetBool("Harness.TimerPeriod", false))
            {
                // Once per process (SessionState survives domain reloads); the process's timer resolution ends with it.
                try { timeBeginPeriod(1); SessionState.SetBool("Harness.TimerPeriod", true); }
                catch (Exception) { /* no winmm: ticks sleep ~15 ms, still idle */ }
            }
            EditorApplication.update += Idle;
        }

        static void Idle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            Thread.Sleep(1);
        }

        static bool HasArgument(string name)
        {
            foreach (var a in Environment.GetCommandLineArgs())
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        [DllImport("winmm.dll")]
        static extern uint timeBeginPeriod(uint milliseconds);
    }
}
