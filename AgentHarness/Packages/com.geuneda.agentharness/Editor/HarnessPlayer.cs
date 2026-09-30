using System;
using System.Collections.Generic;
using System.IO;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace Harness.Editor
{
    /// <summary>
    /// The Editor side of a Player run (W8, tools/player.ps1): which development Player to build for a scenario and how to
    /// start it. The tool builds it with the Pipeline's async "build" command and starts it with the arguments of
    /// <see cref="PlayerRun"/>; the Player plays the scenario on its own (no Editor, no Pipeline server in the Player).
    /// </summary>
    public static class HarnessPlayer
    {
        public const string BuildRoot = "HarnessOut/player-build";

        [CliCommand("harness_player_plan",
            "What tools/player.ps1 builds and starts for a scenario: the development Player's scenes (the play scene first, then the " +
            "other enabled Build Settings scenes), build target, output path, the capture size (= the Player's window) and the " +
            "project's config file. Refuses a non-standalone active build target (switching reimports the project). Read-only.",
            Tags = new[] { "harness", "build" })]
        public static object Plan(
            [CliArg("scenario", "Scenario JSON file (relative to the project root) or inline JSON object.")] string scenario = HarnessPaths.DefaultScenario)
        {
            string json;
            var trimmed = (scenario ?? "").Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal)) json = trimmed;
            else
            {
                var file = HarnessPaths.Resolve(trimmed);
                if (!File.Exists(file)) return new { ok = false, error = "scenario file not found: " + file };
                json = File.ReadAllText(file);
            }
            Scenario parsed;
            try { parsed = JsonUtility.FromJson<Scenario>(json); }
            catch (Exception e) { return new { ok = false, error = "invalid scenario JSON: " + e.Message }; }
            if (parsed == null) return new { ok = false, error = "invalid scenario JSON" };

            var target = EditorUserBuildSettings.activeBuildTarget;
            string extension;
            switch (target)
            {
                case BuildTarget.StandaloneWindows: case BuildTarget.StandaloneWindows64: extension = ".exe"; break;
                case BuildTarget.StandaloneOSX: extension = ".app"; break;
                case BuildTarget.StandaloneLinux64: extension = ".x86_64"; break;
                default:
                    return new
                    {
                        ok = false, activeTarget = target.ToString(),
                        error = $"the active build target is {target}: a Player run needs a desktop (standalone) target, and switching to one reimports the " +
                                "whole project - switch it in the Build Profiles window when that is acceptable",
                    };
            }

            // URP refuses to build a Player from its assets that are not at its last version (URPBuildDataValidator) - assets saved by a
            // newer URP, which it never downgrades: say so before a build that would fail (and leave its preprocessors' files behind).
            var stale = StaleUrpAssets();
            if (stale.Count > 0)
                return new
                {
                    ok = false, urpStale = stale, unityVersion = Application.unityVersion,
                    error = "URP will not build a Player: these assets are not at this URP's last version - saved by a newer Unity than " +
                            Application.unityVersion + " (a newer asset is never downgraded): " + string.Join("; ", stale) +
                            ". Open the project with the Unity version that saved them.",
                };

            var scene = HarnessPaths.ResolvePlayScene(parsed.scene, out var sceneError);
            if (scene == null) return new { ok = false, error = sceneError };
            // The play scene first (the Player starts there), then the rest of the Build Settings so the game's flow can load them.
            var scenes = new List<string> { scene };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.enabled && File.Exists(s.path) && !scenes.Contains(s.path)) scenes.Add(s.path);

            int width = parsed.width, height = parsed.height;
            HarnessPaths.CaptureSize(ref width, ref height);
            var product = Sanitize(PlayerSettings.productName);
            var output = HarnessPaths.Combine(HarnessPaths.Resolve(BuildRoot), target + "/" + product + extension);
            var config = HarnessPaths.Resolve(HarnessConfig.FileName);
            return new
            {
                ok = true, scenario = parsed.name, scene, scenes, target = target.ToString(), output, product,
                company = PlayerSettings.companyName, width, height,
                config = File.Exists(config) ? config : null,
                scriptingBackend = PlayerSettings.GetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone).ToString(),
                unityVersion = Application.unityVersion,
                durationSec = parsed.durationSec, readyTimeoutSec = parsed.readyTimeoutSec, waitSec = WaitBudget(parsed),
            };
        }

        [CliCommand("harness_player_built",
            "After a Player build (tools/player.ps1): save what the build left changed in memory (AssetDatabase.SaveAssets, as the build " +
            "itself does and as the Editor would at exit) - e.g. the Input System's settings asset it adds to Preloaded Assets for the " +
            "build and takes out again - so the working tree shows what the build really changed, now instead of at exit.",
            Tags = new[] { "harness", "build" })]
        public static object Built()
        {
            AssetDatabase.SaveAssets();
            return new { ok = true };
        }

        /// <summary>
        /// URP assets a Player build would include that are not at the running URP's last version: the global settings and the
        /// pipeline assets of the Graphics settings and the quality levels ("path (asset version a, this URP b)"). Uses URP's own
        /// internal IsAtLastVersion (reflection): nothing is reported when it is not there.
        /// </summary>
        static List<string> StaleUrpAssets()
        {
            var stale = new List<string>();
#if AGENTHARNESS_URP
            const System.Reflection.BindingFlags any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            void Check(UnityEngine.Object asset)
            {
                if (asset == null) return;
                var type = asset.GetType();
                var atLast = type.GetMethod("IsAtLastVersion", any, null, Type.EmptyTypes, null);
                if (atLast == null || !(atLast.Invoke(asset, null) is bool ok) || ok) return;
                var version = (type.GetField("m_AssetVersion", any) ?? type.GetField("k_AssetVersion", any))?.GetValue(asset);
                var last = type.GetField("k_LastVersion", any)?.GetValue(null);
                var line = $"{AssetDatabase.GetAssetPath(asset)} (asset version {version}, this URP {last})";
                if (!stale.Contains(line)) stale.Add(line);
            }
            try
            {
                Check(UnityEngine.Rendering.GraphicsSettings.GetSettingsForRenderPipeline<UnityEngine.Rendering.Universal.UniversalRenderPipeline>());
                Check(UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset);
                for (var i = 0; i < QualitySettings.count; i++)
                    Check(QualitySettings.GetRenderPipelineAssetAt(i) as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset);
            }
            catch (Exception e) { Debug.LogWarning("[Harness] could not check the URP asset versions: " + e.Message); }
#endif
            return stale;
        }

        static float WaitBudget(Scenario s)
        {
            var sum = 0f;
            foreach (var e in s.events ?? Array.Empty<ScenarioEvent>())
                if (e != null && (e.type == "waitScene" || e.type == "waitTarget")) sum += e.timeoutSec > 0f ? e.timeoutSec : ScenarioRunner.DefaultWaitTimeoutSec;
            return sum;
        }

        static string Sanitize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "Player";
            foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s.Trim();
        }
    }
}
