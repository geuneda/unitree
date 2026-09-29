using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Harness.Editor
{
    /// <summary>
    /// CompilationPipeline.codeOptimization lasts one Editor session: a restarted Editor comes back as Release (the
    /// user-wide "Code Optimization On Startup" preference, which is left alone). Release moves exception lines to the
    /// method's closing brace and changes procedural float results, i.e. the build fingerprint. Re-apply Debug for this
    /// project whenever an Editor session starts (one extra recompile).
    /// </summary>
    [InitializeOnLoad]
    static class HarnessCodeOptimization
    {
        static HarnessCodeOptimization()
        {
            if (Application.isBatchMode) return;
            if (UnityEditor.Compilation.CompilationPipeline.codeOptimization == UnityEditor.Compilation.CodeOptimization.Debug) return;
            UnityEditor.Compilation.CompilationPipeline.codeOptimization = UnityEditor.Compilation.CodeOptimization.Debug;
            Debug.Log("[Harness] Code optimization set to Debug for this Editor session (exact exception lines, stable build fingerprint)");
        }
    }

    /// <summary>Project settings the harness depends on, applied through Editor APIs (never by editing ProjectSettings YAML).</summary>
    public static class HarnessSetup
    {
        static readonly string[] s_TemplateJunk =
        {
            "Assets/TutorialInfo",
            "Assets/Readme.asset",
            "Assets/Scenes/SampleScene.unity",
            "Assets/Settings/SampleSceneProfile.asset",
        };

        [CliCommand("harness_setup",
            "Idempotently apply harness project settings: Enter Play Mode without Domain Reload (scene reload kept), " +
            "Run In Background, Frame Timing Stats, remove URP template sample content, create Assets/Game and Assets/Generated. Returns {ok, changed[]}.",
            Tags = new[] { "harness", "settings" })]
        public static object Setup()
        {
            var changed = new List<string>();

            SetEnterPlayModeOptions(changed);

            // Debug code optimization: exception stack traces point at the exact line (Release maps throws to the
            // method's closing brace). Costs some runtime speed in the Editor only.
            if (UnityEditor.Compilation.CompilationPipeline.codeOptimization != UnityEditor.Compilation.CodeOptimization.Debug)
            {
                UnityEditor.Compilation.CompilationPipeline.codeOptimization = UnityEditor.Compilation.CodeOptimization.Debug;
                changed.Add("CompilationPipeline.codeOptimization=Debug (triggers a full recompile)");
            }

            if (!PlayerSettings.runInBackground) { PlayerSettings.runInBackground = true; changed.Add("PlayerSettings.runInBackground=true"); }
            if (!PlayerSettings.enableFrameTimingStats) { PlayerSettings.enableFrameTimingStats = true; changed.Add("PlayerSettings.enableFrameTimingStats=true"); }

            foreach (var p in s_TemplateJunk)
            {
                if (AssetDatabase.LoadMainAssetAtPath(p) != null || AssetDatabase.IsValidFolder(p))
                {
                    if (AssetDatabase.DeleteAsset(p)) changed.Add("deleted " + p);
                }
            }

            foreach (var folder in new[] { HarnessPaths.GameRoot, HarnessPaths.GeneratedRoot, "Assets/Scenes" })
            {
                if (!AssetDatabase.IsValidFolder(folder)) { BuildContext.EnsureFolder(folder); changed.Add("created " + folder); }
            }

            AssetDatabase.SaveAssets();
            return new { ok = true, changed, issues = Check() };
        }

        /// <summary>Settings drift that would break the fast loop. Empty = fine.</summary>
        public static List<string> Check()
        {
            var issues = new List<string>();
            var opts = EditorSettings.enterPlayModeOptions;
            if (!EnterPlayModeOptionsEnabled() || (opts & EnterPlayModeOptions.DisableDomainReload) == 0)
                issues.Add("Enter Play Mode: Domain Reload is enabled (run harness_setup)");
            if (!PlayerSettings.runInBackground) issues.Add("PlayerSettings.runInBackground is false (run harness_setup)");
            if (UnityEditor.Compilation.CompilationPipeline.codeOptimization != UnityEditor.Compilation.CodeOptimization.Debug)
                issues.Add("Code optimization is Release: stack-trace line numbers are imprecise (run harness_setup)");
            return issues;
        }

        static void SetEnterPlayModeOptions(List<string> changed)
        {
            if (!EnterPlayModeOptionsEnabled())
            {
                SetEnterPlayModeOptionsEnabled(true);
                changed.Add("EditorSettings.enterPlayModeOptionsEnabled=true");
            }
            var wanted = EnterPlayModeOptions.DisableDomainReload;
            if (EditorSettings.enterPlayModeOptions != wanted)
            {
                EditorSettings.enterPlayModeOptions = wanted;
                changed.Add("EditorSettings.enterPlayModeOptions=DisableDomainReload");
            }
        }

        // enterPlayModeOptionsEnabled is obsolete in newer Unity versions (the options alone decide); use reflection
        // so the harness compiles warning-free across 6.x.
        static PropertyInfo EnabledProperty =>
            typeof(EditorSettings).GetProperty("enterPlayModeOptionsEnabled", BindingFlags.Public | BindingFlags.Static);

        static bool EnterPlayModeOptionsEnabled()
        {
            var p = EnabledProperty;
            return p == null || (bool)p.GetValue(null);
        }

        static void SetEnterPlayModeOptionsEnabled(bool value)
        {
            var p = EnabledProperty;
            if (p != null && p.CanWrite) p.SetValue(null, value);
        }

        [CliCommand("harness_sync_csproj",
            "Generate .sln/.csproj files (Visual Studio package generator) without changing the user's external-editor preference. " +
            "Used by tools/compile-check.ps1.",
            Tags = new[] { "harness", "scripts" })]
        public static object SyncCsproj()
        {
            // Microsoft.Unity.VisualStudio.Editor.Cli.GenerateSolution(): picks a discovered VS installation, switches the
            // external editor to it just for the sync, then restores the user's previous choice.
            Type t = null;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType("Microsoft.Unity.VisualStudio.Editor.Cli", false);
                if (t != null) break;
            }
            if (t == null) return new { ok = false, error = "com.unity.ide.visualstudio is not installed" };
            var m = t.GetMethod("GenerateSolution", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
            if (m == null) return new { ok = false, error = "Cli.GenerateSolution not found in this com.unity.ide.visualstudio version" };
            try { m.Invoke(null, null); }
            catch (TargetInvocationException e) { return new { ok = false, error = e.InnerException?.ToString() }; }
            var files = System.IO.Directory.GetFiles(HarnessPaths.ProjectRoot, "*.csproj");
            return new { ok = true, csproj = Array.ConvertAll(files, f => System.IO.Path.GetFileName(f)) };
        }
    }
}
