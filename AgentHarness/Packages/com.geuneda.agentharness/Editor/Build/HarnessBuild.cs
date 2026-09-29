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
            public int order;
            public double ms;
            public string error;
            public string file;
            public int line;
        }

        /// <summary>Build steps in &lt;module folder&gt;/Builders/ assemblies, sorted. Steps elsewhere are reported in <paramref name="ignored"/>.</summary>
        public static List<(IBuildStep step, StepInfo info)> DiscoverSteps(List<string> ignored)
        {
            // Assemblies whose sources live in a module's Builders/ folder (modules: ProjectSettings/AgentHarness.json).
            var config = HarnessPaths.Config;
            var builderAssemblies = new Dictionary<string, string>(StringComparer.Ordinal); // assembly → module
            foreach (var asm in CompilationPipeline.GetAssemblies(AssembliesType.Editor))
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

            var result = new List<(IBuildStep, StepInfo)>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IBuildStep>())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                var asmName = type.Assembly.GetName().Name;
                if (!builderAssemblies.TryGetValue(asmName, out var module))
                {
                    ignored?.Add($"{type.FullName} (assembly {asmName}) is not in a module's Builders/ folder - ignored");
                    continue;
                }
                IBuildStep step;
                try { step = (IBuildStep)Activator.CreateInstance(type); }
                catch (Exception e) { ignored?.Add($"{type.FullName}: cannot construct ({e.GetBaseException().Message})"); continue; }
                result.Add((step, new StepInfo { type = type.FullName, module = module, assembly = asmName, order = step.Order }));
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
            "Regenerate buildScene from code: run every IBuildStep in <module>/Builders/ in Order on an empty scene, " +
            "write generated assets to <generatedRoot>/<Module>/ (in place, stable GUIDs), delete stale generated assets, save. " +
            "Idempotent: same code -> same 'fingerprint'. Without build steps and a playScene other than 'build' (an attached " +
            "project) nothing is built: it opens the play scene and fingerprints it ('skipped'). Returns JSON {ok, steps, fingerprint, ...}.",
            Tags = new[] { "harness", "scenes" })]
        public static object Build(
            [CliArg("dry_run", "List the steps that would run without building.")] bool dryRun = false,
            [CliArg("no_cache", "Regenerate everything, ignoring BuildContext.CacheHit entries.")] bool noCache = false)
        {
            var sw = Stopwatch.StartNew();
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return new { ok = false, error = "Editor is in play mode; stop it first (harness_play finishes by itself)." };
            if (EditorApplication.isCompiling)
                return new { ok = false, error = "Editor is compiling; wait for recompile_status." };

            var config = HarnessPaths.Config;
            var ignored = new List<string>();
            var steps = DiscoverSteps(ignored);
            if (dryRun)
                return new { ok = true, dryRun = true, steps = steps.Select(s => s.info).ToArray(), ignored };
            var settingsIssues = HarnessSetup.Check();
            if (steps.Count == 0)
            {
                if (string.Equals(config.playScene, "build", StringComparison.OrdinalIgnoreCase))
                    return new { ok = false, error = "No IBuildStep found in a module's Builders/ folder (playScene is 'build').", ignored };
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
                    steps = Array.Empty<StepInfo>(),
                    ignored,
                    warnings = settingsIssues.ToArray(),
                    durationMs = Math.Round(sw.Elapsed.TotalMilliseconds),
                };
            }

            var dirty = HarnessPaths.DirtyScenes();
            if (dirty.Count > 0)
                return new { ok = false, error = "unsaved changes in " + string.Join(", ", dirty) + ": save or discard them in the Editor first (harness_build replaces the open scenes)", ignored };
            // An attached project: never overwrite a scene the harness did not generate (buildScene may name an existing one).
            if (!config.IsHarnessProject && File.Exists(HarnessPaths.BuildScene) && !IsGenerated(HarnessPaths.BuildScene))
                return new { ok = false, error = $"{HarnessPaths.BuildScene} exists and was not generated by harness_build; set another buildScene in {HarnessConfig.FileName}", ignored };

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var ctx = new BuildContext(scene, useCache: !noCache);
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
                    var entry = new LogEntry { type = "Exception", message = e.GetType().Name + ": " + e.Message, stack = HarnessLogParse.TrimStack(e.StackTrace) };
                    HarnessLogParse.FillLocation(entry);
                    if (string.IsNullOrEmpty(entry.file)) FillFromExceptionStack(entry, e);
                    info.error = entry.message;
                    info.file = entry.file;
                    info.line = entry.line;
                    Debug.LogException(e);
                }
                info.ms = Math.Round(st.Elapsed.TotalMilliseconds, 1);
            }

            if (failed)
            {
                // Keep the last good build scene on disk and open it again.
                if (File.Exists(HarnessPaths.BuildScene)) EditorSceneManager.OpenScene(HarnessPaths.BuildScene, OpenSceneMode.Single);
                return new
                {
                    ok = false,
                    error = "build step failed - " + HarnessPaths.BuildScene + " not written",
                    steps = steps.Select(s => s.info).ToArray(),
                    ignored,
                    warnings = ctx.Warnings,
                    durationMs = Math.Round(sw.Elapsed.TotalMilliseconds),
                };
            }

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

            if (!config.IsHarnessProject) foreach (var p in ctx.TouchedAssets) MarkGenerated(p);
            AssetDatabase.SaveAssets();
            ctx.CommitCache();
            BuildContext.EnsureFolder(Path.GetDirectoryName(HarnessPaths.BuildScene));
            if (!EditorSceneManager.SaveScene(scene, HarnessPaths.BuildScene))
                return new { ok = false, error = "SaveScene failed for " + HarnessPaths.BuildScene };
            // The Build Settings belong to an attached project; only a harness project starts with the generated scene.
            if (config.IsHarnessProject) EnsureInBuildSettings(HarnessPaths.BuildScene);
            else MarkGenerated(HarnessPaths.BuildScene);

            var fp = SceneFingerprint.Compute(scene);
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
                steps = steps.Select(s => s.info).ToArray(),
                ignored,
                warnings = ctx.Warnings.Concat(settingsIssues).ToArray(),
                durationMs = Math.Round(sw.Elapsed.TotalMilliseconds),
            };
        }

        static bool IsGenerated(string assetPath)
        {
            var a = AssetDatabase.LoadMainAssetAtPath(assetPath);
            return a != null && Array.IndexOf(AssetDatabase.GetLabels(a), HarnessConfig.GeneratedLabel) >= 0;
        }

        static void MarkGenerated(string assetPath)
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
