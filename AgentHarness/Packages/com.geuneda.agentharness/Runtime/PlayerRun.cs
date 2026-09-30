using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Harness
{
    /// <summary>
    /// A scenario in a built Player (W8, tools/player.ps1: G3-2 performance, G3-8 the real screen). The development build is
    /// started with <c>-harness-scenario &lt;file&gt; -harness-out &lt;dir&gt;</c> (and <c>-harness-config</c> the project's
    /// AgentHarness.json, <c>-harness-size WxH</c> the capture size = the window's, <c>-harness-screen</c> screen twins,
    /// <c>-harness-id</c>) and runs the same <see cref="ScenarioRunner"/> as the Editor's play mode: result.json and the shots
    /// go to the out folder, then the Player quits (exit code 0 = the scenario succeeded). Frames are not paced (vSync off, no
    /// frame cap) so the frame times are the game's work. A Player started without -harness-scenario is not touched.
    /// </summary>
    public static class PlayerRun
    {
        public const string ScenarioArg = "-harness-scenario";
        public const string OutArg = "-harness-out";
        public const string ConfigArg = "-harness-config";
        public const string SizeArg = "-harness-size";
        public const string ScreenArg = "-harness-screen";
        public const string IdArg = "-harness-id";
        public const string PacedArg = "-harness-paced";   // keep the project's vSync / frame cap

        static Scenario s_Scenario;
        static string s_OutDir, s_Id, s_Error;
        static bool s_Active, s_ScreenTwins;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_Scenario = null;
            s_OutDir = s_Id = s_Error = null;
            s_Active = s_ScreenTwins = false;
        }

        /// <summary>"-name value" pairs and "-flag"s of a command line (a value is the next argument unless it starts with "-harness-").</summary>
        public static Dictionary<string, string> ParseArgs(string[] args)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < args.Length; i++)
            {
                var a = args[i];
                if (!a.StartsWith("-harness-", StringComparison.OrdinalIgnoreCase)) continue;
                var hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("-harness-", StringComparison.OrdinalIgnoreCase);
                map[a] = hasValue ? args[++i] : "";
            }
            return map;
        }

#if !UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Prepare()
        {
            var args = ParseArgs(Environment.GetCommandLineArgs());
            if (!args.TryGetValue(ScenarioArg, out var scenarioPath)) return;
            s_Active = true;
            s_OutDir = args.TryGetValue(OutArg, out var o) && o.Length > 0 ? o : Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "HarnessOut");
            s_Id = args.TryGetValue(IdArg, out var id) && id.Length > 0 ? id : "player";
            s_ScreenTwins = args.ContainsKey(ScreenArg);
            if (args.TryGetValue(ConfigArg, out var config) && config.Length > 0) HarnessConfig.UseFile(config);
            try
            {
                s_Scenario = JsonUtility.FromJson<Scenario>(File.ReadAllText(scenarioPath));
                if (s_Scenario == null) s_Error = "invalid scenario JSON: " + scenarioPath;
            }
            catch (Exception e) { s_Error = $"cannot read the scenario {scenarioPath}: {e.Message}"; }
            if (s_Scenario == null) return;
            if (args.TryGetValue(SizeArg, out var size))
            {
                var wh = size.ToLowerInvariant().Split('x');
                if (wh.Length == 2 && int.TryParse(wh[0], out var w) && int.TryParse(wh[1], out var h) && w > 0 && h > 0) { s_Scenario.width = w; s_Scenario.height = h; }
            }
            HarnessCapture.DefaultSize(ref s_Scenario.width, ref s_Scenario.height);
            if (!args.ContainsKey(PacedArg))
            {
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
            }
            // A development build opens its console over the game on an error: it would be in the screen shots.
            Debug.developerConsoleEnabled = false;
            // The window is the capture size (tools/player.ps1 passes -screen-width/-height too): UI laid out as the shots.
            if (Screen.width != s_Scenario.width || Screen.height != s_Scenario.height || Screen.fullScreenMode != FullScreenMode.Windowed)
                Screen.SetResolution(s_Scenario.width, s_Scenario.height, FullScreenMode.Windowed);
            Debug.Log($"[Harness] player run: scenario '{s_Scenario.name}' ({scenarioPath}) at {s_Scenario.width}x{s_Scenario.height}, out {s_OutDir}");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Begin()
        {
            if (!s_Active) return;
            if (s_Scenario == null)
            {
                var failed = new PlayResult { id = s_Id, success = false, error = s_Error ?? "no scenario" };
                Describe(failed.player);
                try
                {
                    Directory.CreateDirectory(s_OutDir);
                    File.WriteAllText(Path.Combine(s_OutDir, "result.json"), JsonUtility.ToJson(failed, true));
                }
                catch (Exception e) { Debug.LogException(e); }
                Application.Quit(2);
                return;
            }
            // The runner comes at the end of the first frame, as in the Editor (its play mode spawns it after the game's first
            // Update): the game ticks once, the runner's clock starts on the second frame (ScenarioRunner.BeginClock).
            var go = new GameObject("[HarnessPlayerRun]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Starter>();
        }

        sealed class Starter : MonoBehaviour
        {
            void LateUpdate()
            {
                Destroy(gameObject);
                // The Editor's first two play mode frames are Time.fixedDeltaTime long (measured: 0.02, 0.02, then the scenario's
                // step); a Player's first frame is too, its second one is not. Then the Player plays the Editor's frames: same
                // events, same pixels at the same t.
                if (s_Scenario.fixedDeltaTime > 0f) Time.captureDeltaTime = Time.fixedDeltaTime;
                var names = new List<string>();
                var errors = new List<string>();
                var hook = InputHooks.Combine(InputHooks.FindMarked(), names, errors);
                foreach (var err in errors) Debug.LogError("[Harness] " + err);
                var runner = ScenarioRunner.Spawn(s_Id, s_Scenario, s_OutDir, hook);
                Debug.Log($"[Harness] scenario runner started (frame {Time.frameCount}, {Time.realtimeSinceStartup:0.00} s after start)");
                runner.Result.inputHooks = names.ToArray();
                runner.Result.player.startupSec = Time.realtimeSinceStartup;
                runner.ScreenTwins = s_ScreenTwins;
                runner.onFinished = r =>
                {
                    Debug.Log($"[Harness] scenario finished: {(r.success ? "ok" : "failed: " + r.error)} ({r.frames} frames, {r.wallSec:0.00} s); quitting");
                    Application.Quit(r.success ? 0 : 1);
                };
            }
        }
#endif

        /// <summary>The Player's window, frame pacing and device, for the result (<see cref="PlayResult.player"/>).</summary>
        public static void Describe(PlayerInfo p)
        {
            p.platform = Application.platform.ToString();
            p.development = Debug.isDebugBuild;
#if ENABLE_IL2CPP
            p.scriptingBackend = "il2cpp";
#else
            p.scriptingBackend = "mono";
#endif
            p.screenWidth = Screen.width;
            p.screenHeight = Screen.height;
            p.fullScreenMode = Screen.fullScreenMode.ToString();
            p.vSyncCount = QualitySettings.vSyncCount;
            p.targetFrameRate = Application.targetFrameRate;
            p.graphicsDevice = SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName;
        }
    }
}
