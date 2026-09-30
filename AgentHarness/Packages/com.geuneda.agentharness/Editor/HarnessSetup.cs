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
    /// project whenever an Editor session starts (one extra recompile). tools/open.ps1 starts Editors with
    /// -debugCodeOptimization, which makes the session Debug from the start (no extra recompile, W7).
    /// </summary>
    [InitializeOnLoad]
    static class HarnessCodeOptimization
    {
        static HarnessCodeOptimization()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess() || (Application.isBatchMode && !HarnessHeadless.IsHeadless)) return;
            if (UnityEditor.Compilation.CompilationPipeline.codeOptimization == UnityEditor.Compilation.CodeOptimization.Debug) return;
            UnityEditor.Compilation.CompilationPipeline.codeOptimization = UnityEditor.Compilation.CodeOptimization.Debug;
            Debug.Log("[Harness] Code optimization set to Debug for this Editor session (exact exception lines, stable build fingerprint)");
        }
    }

    /// <summary>
    /// Project settings the harness depends on, applied through Editor APIs (never by editing ProjectSettings YAML).
    /// A harness project ("setup": "harness" in ProjectSettings/AgentHarness.json) gets them all; an attached project gets
    /// none unless asked (--apply), so installing the harness changes no project file.
    /// </summary>
    public static class HarnessSetup
    {
        static readonly string[] s_Settings = { "domainReload", "runInBackground", "frameTimingStats", "syncShaders" };

        static readonly string[] s_TemplateJunk =
        {
            "Assets/TutorialInfo",
            "Assets/Readme.asset",
            "Assets/Scenes/SampleScene.unity",
            "Assets/Settings/SampleSceneProfile.asset",
        };

        [CliCommand("harness_setup",
            "Idempotently apply the harness project settings. setup 'harness' (ProjectSettings/AgentHarness.json): Enter Play Mode " +
            "without Domain Reload (scene reload kept), Run In Background, Frame Timing Stats, synchronous shader compilation, remove " +
            "URP template sample content, create the module roots and generated folders, run the ISettingsStep code (render pipeline assets, the ProjectSettings values they own). setup 'attach' (an existing project): changes " +
            "no project setting and deletes nothing; it reports recommendations, and --apply 'domainReload,runInBackground,frameTimingStats,syncShaders' applies those. " +
            "Debug code optimization (Editor session only) always. Returns {ok, setup, changed[], settings, warnings[], issues[], recommendations[]}.",
            Tags = new[] { "harness", "settings" })]
        public static object Setup(
            [CliArg("apply", "Attached project: comma-separated settings to change anyway (domainReload, runInBackground, frameTimingStats, syncShaders, or 'all').")] string apply = "")
        {
            var config = HarnessPaths.Config;
            var changed = new List<string>();
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in (apply ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (a.Equals("all", StringComparison.OrdinalIgnoreCase)) wanted.UnionWith(s_Settings);
                else if (Array.Exists(s_Settings, x => x.Equals(a, StringComparison.OrdinalIgnoreCase))) wanted.Add(a);
                else return new { ok = false, error = $"unknown setting '{a}' (known: {string.Join(", ", s_Settings)}, all)" };
            }
            if (config.IsHarnessProject) wanted.UnionWith(s_Settings);

            if (wanted.Contains("domainReload")) SetEnterPlayModeOptions(changed);

            // Debug code optimization: exception stack traces point at the exact line (Release maps throws to the
            // method's closing brace). Costs some runtime speed in the Editor only, and lasts this Editor session only.
            if (UnityEditor.Compilation.CompilationPipeline.codeOptimization != UnityEditor.Compilation.CodeOptimization.Debug)
            {
                UnityEditor.Compilation.CompilationPipeline.codeOptimization = UnityEditor.Compilation.CodeOptimization.Debug;
                changed.Add("CompilationPipeline.codeOptimization=Debug (triggers a full recompile)");
            }

            if (wanted.Contains("runInBackground") && !PlayerSettings.runInBackground) { PlayerSettings.runInBackground = true; changed.Add("PlayerSettings.runInBackground=true"); }
            if (wanted.Contains("frameTimingStats") && !PlayerSettings.enableFrameTimingStats) { PlayerSettings.enableFrameTimingStats = true; changed.Add("PlayerSettings.enableFrameTimingStats=true"); }
            // A shader variant compiles when it is first drawn; asynchronously, the Editor leaves its objects out meanwhile,
            // so the first captures after an import miss them (and differ from the golden images). Synchronous: that frame waits.
            if (wanted.Contains("syncShaders") && EditorSettings.asyncShaderCompilation) { EditorSettings.asyncShaderCompilation = false; changed.Add("EditorSettings.asyncShaderCompilation=false"); }

            if (config.IsHarnessProject)
            {
                foreach (var p in s_TemplateJunk)
                {
                    if (AssetDatabase.LoadMainAssetAtPath(p) != null || AssetDatabase.IsValidFolder(p))
                    {
                        if (AssetDatabase.DeleteAsset(p)) changed.Add("deleted " + p);
                    }
                }

                var folders = new List<string>(config.moduleRoots) { HarnessPaths.GeneratedRoot, System.IO.Path.GetDirectoryName(HarnessPaths.BuildScene)?.Replace('\\', '/') };
                foreach (var folder in folders)
                {
                    if (string.IsNullOrEmpty(folder) || !folder.StartsWith("Assets", StringComparison.Ordinal)) continue;
                    if (!AssetDatabase.IsValidFolder(folder)) { BuildContext.EnsureFolder(folder); changed.Add("created " + folder); }
                }
            }

            // Settings from code (ISettingsStep: render pipeline, project settings), so a new clone has them before the first loop.
            object settings = null;
            var warnings = new List<string>();
            if (config.IsHarnessProject && config.loadError == null)
            {
                var run = SettingsContext.Run(new List<string>());
                if (run.failed)
                    return new { ok = false, error = "settings step failed", setup = config.setup, changed, steps = run.steps.ConvertAll(s => s.info), warnings = run.ctx.Warnings };
                foreach (var p in run.ctx.Written) changed.Add("settings: wrote " + p);
                foreach (var a in run.ctx.Assigned) changed.Add("settings: " + a);
                foreach (var c in run.ctx.ProjectChanged) changed.Add("settings: project " + c);
                if (run.switched != null) changed.Add("settings: render pipeline " + run.switched + " (domain reload requested)");
                if (run.steps.Count > 0) settings = HarnessBuild.SettingsSummary(run);
                warnings.AddRange(run.ctx.Warnings);
            }

            if (changed.Count > 0) AssetDatabase.SaveAssets();
            return new
            {
                ok = config.loadError == null,
                error = config.loadError,
                setup = config.setup,
                config = config.fromFile ? HarnessConfig.FileName : HarnessConfig.FileName + " (missing: defaults)",
                changed,
                settings,
                warnings,
                issues = Check(),
                recommendations = config.IsHarnessProject ? new List<object>() : Recommendations(),
            };
        }

        internal static bool DomainReloadDisabled() => EnterPlayModeOptionsEnabled() && (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;

        /// <summary>Settings drift that would break the loop. Empty = fine.</summary>
        public static List<string> Check()
        {
            var config = HarnessPaths.Config;
            var issues = new List<string>();
            if (config.loadError != null) issues.Add(config.loadError);
            if (config.IsHarnessProject)
            {
                if (!DomainReloadDisabled()) issues.Add("Enter Play Mode: Domain Reload is enabled (run harness_setup)");
                if (!PlayerSettings.runInBackground) issues.Add("PlayerSettings.runInBackground is false (run harness_setup)");
                if (EditorSettings.asyncShaderCompilation) issues.Add("Asynchronous Shader Compilation is on: captures right after an import miss objects whose shaders compile (run harness_setup)");
            }
            if (UnityEditor.Compilation.CompilationPipeline.codeOptimization != UnityEditor.Compilation.CodeOptimization.Debug)
                issues.Add("Code optimization is Release: stack-trace line numbers are imprecise (run harness_setup)");
            return issues;
        }

        /// <summary>What an attached project could change for a faster or more exact loop (harness_setup --apply ...).</summary>
        static List<object> Recommendations()
        {
            var list = new List<object>();
            if (!DomainReloadDisabled())
                list.Add(new
                {
                    setting = "domainReload",
                    current = "Domain Reload on entering Play Mode",
                    recommended = "off (Enter Play Mode Options)",
                    why = "every play of the loop pays a domain reload (timings.playEnterSec). Only safe when every mutable static is reset in a [RuntimeInitializeOnLoadMethod(SubsystemRegistration)] method; harness_lint checks module code only",
                });
            if (EditorSettings.asyncShaderCompilation)
                list.Add(new
                {
                    setting = "syncShaders",
                    current = "Asynchronous Shader Compilation on (Editor settings)",
                    recommended = "off",
                    why = "the Editor leaves an object out of the frame while its shader variant compiles, so the first captures after an import (or on a new machine) miss objects and differ from the golden images (shotStats[].shadersCompiling). Off: the Editor waits for the compile in that frame",
                });
            return list;
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
