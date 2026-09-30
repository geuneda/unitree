using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Harness.Editor
{
    public static class HarnessBuild
    {
        public sealed class StepInfo
        {
            public string type;
            public string module;
            public string assembly;
            public string phase;   // "settings" for an ISettingsStep, null for a build step
            public int order;
            public double ms;
            public string error;
            public string file;
            public int line;
        }

        /// <summary>Build steps in &lt;module folder&gt;/Builders/ assemblies, sorted. Steps elsewhere are reported in <paramref name="ignored"/>.</summary>
        public static List<(IBuildStep step, StepInfo info)> DiscoverSteps(List<string> ignored) => DiscoverSteps<IBuildStep>(s => s.Order, ignored);

        /// <summary>Implementations of <typeparamref name="T"/> in &lt;module folder&gt;/Builders/ assemblies, sorted by order, module, type.</summary>
        public static List<(T step, StepInfo info)> DiscoverSteps<T>(Func<T, int> order, List<string> ignored) where T : class
        {
            // Assemblies whose sources live in a module's Builders/ folder (modules: ProjectSettings/AgentHarness.json).
            var config = HarnessPaths.Config;
            var builderAssemblies = new Dictionary<string, string>(StringComparer.Ordinal); // assembly → module
            foreach (var asm in HarnessPaths.Assemblies(AssembliesType.Editor))
            {
                foreach (var f in asm.sourceFiles)
                {
                    var src = f.Replace('\\', '/');
                    var module = config.ModuleOf(src, out var folder, out _);
                    if (module.Length == 0 || module == "Harness") continue;
                    if (src.StartsWith(folder + "/Builders/", StringComparison.Ordinal)) builderAssemblies[asm.name] = module;
                    break;
                }
            }

            var result = new List<(T, StepInfo)>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<T>())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                var asmName = type.Assembly.GetName().Name;
                if (!builderAssemblies.TryGetValue(asmName, out var module))
                {
                    ignored?.Add($"{type.FullName} (assembly {asmName}) is not in a module's Builders/ folder - ignored");
                    continue;
                }
                T step;
                try { step = (T)Activator.CreateInstance(type); }
                catch (Exception e) { ignored?.Add($"{type.FullName}: cannot construct ({e.GetBaseException().Message})"); continue; }
                result.Add((step, new StepInfo { type = type.FullName, module = module, assembly = asmName, order = order(step) }));
            }
            result.Sort((a, b) =>
            {
                var c = a.Item2.order.CompareTo(b.Item2.order);
                if (c != 0) return c;
                c = string.CompareOrdinal(a.Item2.module, b.Item2.module);
                return c != 0 ? c : string.CompareOrdinal(a.Item2.type, b.Item2.type);
            });
            return result;
        }

        [CliCommand("harness_build",
            "Regenerate buildScene from code: run every ISettingsStep (render pipeline assets and assignments; harness projects only), " +
            "then every IBuildStep in <module>/Builders/ in Order on an empty scene, " +
            "write generated assets to <generatedRoot>/<Module>/ (in place, stable GUIDs), delete stale generated assets, save. " +
            "Idempotent: same code -> same 'fingerprint' (scene, generated assets and render settings). Without build steps and a playScene other than 'build' (an attached " +
            "project) nothing is built: it opens the play scene and fingerprints it ('skipped'). Returns JSON {ok, steps, fingerprint, settings, ...}.",
            Tags = new[] { "harness", "scenes" })]
        public static object Build(
            [CliArg("dry_run", "List the steps that would run without building.")] bool dryRun = false,
            [CliArg("no_cache", "Regenerate everything, ignoring BuildContext.CacheHit entries.")] bool noCache = false)
        {
            var sw = Stopwatch.StartNew();
            var phases = new Phases();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return new { ok = false, error = "Editor is in play mode; stop it first (harness_play finishes by itself)." };
            if (EditorApplication.isCompiling)
                return new { ok = false, error = "Editor is compiling; wait for recompile_status." };

            var config = HarnessPaths.Config;
            var ignored = new List<string>();
            var steps = DiscoverSteps(ignored);
            if (dryRun)
            {
                var settingsSteps = DiscoverSteps<ISettingsStep>(s => s.Order, ignored);
                foreach (var (_, info) in settingsSteps) info.phase = "settings";
                return new { ok = true, dryRun = true, steps = settingsSteps.Select(s => s.info).Concat(steps.Select(s => s.info)).ToArray(), ignored };
            }
            var settingsIssues = HarnessSetup.Check();
            if (steps.Count == 0)
            {
                if (string.Equals(config.playScene, "build", StringComparison.OrdinalIgnoreCase))
                    return new { ok = false, error = "No IBuildStep found in a module's Builders/ folder (playScene is 'build').", ignored };
                var only = SettingsContext.Run(ignored);
                if (only.failed) return SettingsFailed(only, ignored, sw);
                // Nothing generates a scene here: the loop plays an existing one. Fingerprint its assets, so a change shows.
                var playScene = HarnessPaths.ResolvePlayScene(null, out var sceneError);
                if (playScene == null) return new { ok = false, error = sceneError, ignored };
                if (!HarnessPaths.OpenScene(playScene, out var openError)) return new { ok = false, error = openError, ignored };
                var sfp = SceneFingerprint.ComputeFromAssets(playScene);
                return new
                {
                    ok = true,
                    skipped = true,
                    scene = playScene,
                    fingerprint = sfp.hash,
                    fingerprintOf = "scene file + dependencies (import hashes)",
                    gameObjects = sfp.gameObjects,
                    assets = sfp.assets,
                    steps = only.steps.Select(s => s.info).ToArray(),
                    ignored,
                    warnings = only.ctx.Warnings.Concat(settingsIssues).ToArray(),
                    durationMs = Math.Round(sw.Elapsed.TotalMilliseconds),
                };
            }

            var dirty = HarnessPaths.DirtyScenes();
            if (dirty.Count > 0)
                return new { ok = false, error = "unsaved changes in " + string.Join(", ", dirty) + ": save or discard them in the Editor first (harness_build replaces the open scenes)", ignored };
            // An attached project: never overwrite a scene the harness did not generate (buildScene may name an existing one).
            if (!config.IsHarnessProject && File.Exists(HarnessPaths.BuildScene) && !IsGenerated(HarnessPaths.BuildScene))
                return new { ok = false, error = $"{HarnessPaths.BuildScene} exists and was not generated by harness_build; set another buildScene in {HarnessConfig.FileName}", ignored };

            phases.Lap("check");
            // Render settings first: build steps render with the pipeline they set (BakeSkyReflection).
            var settings = SettingsContext.Run(ignored);
            phases.Lap("settings");
            if (settings.failed) return SettingsFailed(settings, ignored, sw);
            var allSteps = settings.steps.Select(s => s.info).Concat(steps.Select(s => s.info)).ToArray();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ctx = new BuildContext(scene, useCache: !noCache);
            foreach (var p in settings.ctx.Assets) ctx.TouchedAssets.Add(p);
            var failed = false;
            foreach (var (step, info) in steps)
            {
                ctx.Module = info.module;
                ctx.StepAssembly = step.GetType().Assembly;
                var st = Stopwatch.StartNew();
                try
                {
                    step.Build(ctx);
                }
                catch (Exception e)
                {
                    failed = true;
                    FillError(info, e);
                }
                info.ms = Math.Round(st.Elapsed.TotalMilliseconds, 1);
            }
            phases.Lap("steps");

            if (failed)
            {
                // Keep the last good build scene on disk and open it again.
                if (File.Exists(HarnessPaths.BuildScene)) EditorSceneManager.OpenScene(HarnessPaths.BuildScene, OpenSceneMode.Single);
                return new
                {
                    ok = false,
                    error = "build step failed - " + HarnessPaths.BuildScene + " not written",
                    steps = allSteps,
                    ignored,
                    warnings = settings.ctx.Warnings.Concat(ctx.Warnings).ToArray(),
                    durationMs = Math.Round(sw.Elapsed.TotalMilliseconds),
                };
            }
            ctx.AfterSteps();

            // Delete generated assets that no step produced this time (keeps Assets/Generated == f(code)).
            var deleted = new List<string>();
            if (AssetDatabase.IsValidFolder(HarnessPaths.GeneratedRoot))
            {
                foreach (var guid in AssetDatabase.FindAssets("", new[] { HarnessPaths.GeneratedRoot }))
                {
                    var p = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(p) || ctx.TouchedAssets.Contains(p)) continue;
                    // An attached project deletes only what an earlier build generated (labelled), whatever generatedRoot says.
                    if (!config.IsHarnessProject && !IsGenerated(p)) continue;
                    if (AssetDatabase.DeleteAsset(p)) deleted.Add(p);
                }
                DeleteEmptyFolders(HarnessPaths.GeneratedRoot);
            }
            phases.Lap("cleanup");

            if (!config.IsHarnessProject) foreach (var p in ctx.TouchedAssets) MarkGenerated(p);
            AssetDatabase.SaveAssets();
            ctx.CommitCache();
            BuildContext.EnsureFolder(Path.GetDirectoryName(HarnessPaths.BuildScene));
            if (!EditorSceneManager.SaveScene(scene, HarnessPaths.BuildScene))
                return new { ok = false, error = "SaveScene failed for " + HarnessPaths.BuildScene };
            phases.Lap("save");
            // Lighting data belongs to a saved scene (ctx.SkyAmbient); the scene then references it.
            if (ctx.SaveLightingData() && !EditorSceneManager.SaveScene(scene))
                return new { ok = false, error = "SaveScene failed for " + HarnessPaths.BuildScene + " (lighting data)" };
            // The Build Settings belong to an attached project; only a harness project starts with the generated scene.
            if (config.IsHarnessProject) EnsureInBuildSettings(HarnessPaths.BuildScene);
            else MarkGenerated(HarnessPaths.BuildScene);
            phases.Lap("lighting");

            var applied = settings.steps.Count > 0 && !settings.skipped;
            var fp = SceneFingerprint.Compute(scene, applied ? settings.ctx.Fingerprint() : null);
            phases.Lap("fingerprint");
            return new
            {
                ok = true,
                scene = HarnessPaths.BuildScene,
                fingerprint = fp.hash,
                gameObjects = fp.gameObjects,
                components = fp.components,
                generatedAssets = ctx.TouchedAssets.Count,
                cacheHits = ctx.CacheHits,
                deletedAssets = deleted,
                settings = applied ? SettingsSummary(settings) : null,
                steps = allSteps,
                ignored,
                warnings = settings.ctx.Warnings.Concat(ctx.Warnings).Concat(settingsIssues).ToArray(),
                durationMs = Math.Round(sw.Elapsed.TotalMilliseconds),
                phases = phases.Ms,
            };
        }

        /// <summary>Where a build's time went (report build.phases, ms): each lap since the previous one.</summary>
        sealed class Phases
        {
            readonly Stopwatch m_Sw = Stopwatch.StartNew();
            double m_Last;
            public readonly Dictionary<string, double> Ms = new Dictionary<string, double>();

            public void Lap(string name)
            {
                var now = m_Sw.Elapsed.TotalMilliseconds;
                Ms[name] = Math.Round(now - m_Last, 1);
                m_Last = now;
            }
        }

        static object SettingsFailed(SettingsContext.RunResult r, List<string> ignored, Stopwatch sw) => new
        {
            ok = false,
            error = "settings step failed - nothing built",
            steps = r.steps.Select(s => s.info).ToArray(),
            ignored,
            warnings = r.ctx.Warnings,
            durationMs = Math.Round(sw.Elapsed.TotalMilliseconds),
        };

        /// <summary>
        /// What the settings steps produced: their assets, what this run wrote or reassigned, and the active pipeline. When the
        /// active pipeline changed, a domain reload was requested: wait until domainReloads passes the reported value.
        /// </summary>
        internal static object SettingsSummary(SettingsContext.RunResult r) => new
        {
            assets = r.ctx.Assets,
            written = r.ctx.Written,
            assigned = r.ctx.Assigned,
            pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null ? "none (Built-in)" : AssetDatabase.GetAssetPath(UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline),
            switched = r.switched,
            reloadRequested = r.switched != null,
            domainReloads = HarnessConsole.DomainReloads,
        };

        /// <summary>Report a step's exception at the step's own line (not the harness frame that called it).</summary>
        internal static void FillError(StepInfo info, Exception e)
        {
            var entry = new LogEntry { type = "Exception", message = e.GetType().Name + ": " + e.Message, stack = HarnessLogParse.TrimStack(e.StackTrace) };
            HarnessLogParse.FillLocation(entry);
            if (string.IsNullOrEmpty(entry.file)) FillFromExceptionStack(entry, e);
            info.error = entry.message;
            info.file = entry.file;
            info.line = entry.line;
            Debug.LogException(e);
        }

        static bool IsGenerated(string assetPath)
        {
            var a = AssetDatabase.LoadMainAssetAtPath(assetPath);
            return a != null && Array.IndexOf(AssetDatabase.GetLabels(a), HarnessConfig.GeneratedLabel) >= 0;
        }

        internal static void MarkGenerated(string assetPath)
        {
            var a = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (a == null) return;
            var labels = AssetDatabase.GetLabels(a);
            if (Array.IndexOf(labels, HarnessConfig.GeneratedLabel) >= 0) return;
            AssetDatabase.SetLabels(a, labels.Concat(new[] { HarnessConfig.GeneratedLabel }).ToArray());
        }

        static void FillFromExceptionStack(LogEntry entry, Exception e)
        {
            var st = new StackTrace(e, true);
            foreach (var f in st.GetFrames() ?? Array.Empty<StackFrame>())
            {
                var file = f.GetFileName();
                if (string.IsNullOrEmpty(file)) continue;
                var rel = HarnessLogParse.ToProjectPath(file);
                if (!HarnessPaths.Config.IsProjectPath(rel)) continue;
                entry.file = rel;
                entry.line = f.GetFileLineNumber();
                entry.module = HarnessLogParse.ModuleOf(rel);
                return;
            }
        }

        static void EnsureInBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != scenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            var current = EditorBuildSettings.scenes;
            if (current.Length == scenes.Count && current.Length > 0 && current[0].path == scenePath && current[0].enabled
                && current[0].guid.ToString() == AssetDatabase.AssetPathToGUID(scenePath)) return;
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void DeleteEmptyFolders(string folder)
        {
            foreach (var sub in AssetDatabase.GetSubFolders(folder)) DeleteEmptyFolders(sub);
            if (folder != HarnessPaths.GeneratedRoot && AssetDatabase.FindAssets("", new[] { folder }).Length == 0)
                AssetDatabase.DeleteAsset(folder);
        }
    }
}
