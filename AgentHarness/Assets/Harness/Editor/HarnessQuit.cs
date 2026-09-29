using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Harness.Editor
{
    /// <summary>
    /// Close this Editor from a script (tools/quit.ps1). Pipeline's own "quit" is a player command: it calls
    /// DontDestroyOnLoad, which fails in edit mode. EditorApplication.delayCall never runs in an unfocused Editor.
    /// So the exit runs from EditorApplication.update (it ticks in the background too: Pipeline's dispatcher runs on it)
    /// a moment after the reply, which the HTTP thread writes only after this method has returned.
    /// </summary>
    public static class HarnessQuit
    {
        const double k_DelaySec = 0.3;
        static double s_ExitAt;

        [CliCommand("harness_quit",
            "Close this Editor normally (EditorApplication.Exit: no save prompts, scene changes are discarded) ~0.3 s after replying. " +
            "Returns {ok, pid}. tools/quit.ps1 takes the Editor lock, calls this and waits for the process to end.",
            Tags = new[] { "harness", "editor" })]
        public static object Quit()
        {
            s_ExitAt = EditorApplication.timeSinceStartup + k_DelaySec;
            EditorApplication.update -= ExitWhenDue;
            EditorApplication.update += ExitWhenDue;
            return new { ok = true, pid = System.Diagnostics.Process.GetCurrentProcess().Id };
        }

        static void ExitWhenDue()
        {
            if (EditorApplication.timeSinceStartup < s_ExitAt) return;
            EditorApplication.update -= ExitWhenDue;
            Debug.Log("[Harness] harness_quit: closing the Editor");
            EditorApplication.Exit(0);
        }
    }
}
