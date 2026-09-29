using System;
using System.Diagnostics;
using System.IO;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Harness.Editor
{
    [Serializable]
    sealed class PlayState
    {
        public string id;
        public string state;        // entering | running | exiting | done | failed
        public string scenarioJson;
        public string scenarioSource;
        public string outDir;
        public string requestedAt;
        public string startedAt;
        public string finishedAt;
        public float timeoutSec;
        public string error;

        public static PlayState Load()
        {
            try
            {
                return File.Exists(HarnessPaths.PlayStateFile)
                    ? JsonUtility.FromJson<PlayState>(File.ReadAllText(HarnessPaths.PlayStateFile))
                    : null;
            }
            catch { return null; }
        }

        public void Save()
        {
            var tmp = HarnessPaths.PlayStateFile + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(this, true));
            if (File.Exists(HarnessPaths.PlayStateFile)) File.Delete(HarnessPaths.PlayStateFile);
            File.Move(tmp, HarnessPaths.PlayStateFile);
        }

        public string ResultPath => HarnessPaths.Combine(outDir, "result.json");
    }

    /// <summary>
    /// harness_play is fire-and-forget: it records a request in Library/Harness/play_state.json and enters play mode.
    /// This driver reacts to play-mode transitions (it also survives a domain reload, should one happen), spawns the
    /// runtime <see cref="ScenarioRunner"/>, and exits play mode when the scenario is finished.
    /// </summary>
    [InitializeOnLoad]
    public static class HarnessPlay
    {
        static readonly Stopwatch s_Watch = new Stopwatch();

        static HarnessPlay()
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;   // asset import workers load Editor assemblies too
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += Watchdog;
        }

        [CliCommand("harness_play",
            "Enter play mode and run a scenario (timed input + captures). Returns immediately; poll harness_play_status until " +
            "state is done|failed. Result: <out>/result.json (+ PNG shots). --scenario is a JSON file path (default " +
            HarnessPaths.DefaultScenario + ") or inline JSON. Plays the scenario's \"scene\", else playScene of " + HarnessConfig.FileName + ".",
            Tags = new[] { "harness", "editor/playmode" })]
        public static object Play(
            [CliArg("scenario", "Scenario JSON file (relative to the project root) or inline JSON object.")] string scenario = HarnessPaths.DefaultScenario,
            [CliArg("out", "Output directory for result.json and shots (relative to the project root).")] string @out = "HarnessOut/play",
            [CliArg("timeout_sec", "Abort if the scenario has not finished after this many seconds (0 = duration + 60).")] float timeoutSec = 0f)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return new { ok = false, error = "already in play mode" };
            if (EditorApplication.isCompiling)
                return new { ok = false, error = "Editor is compiling; wait for recompile_status" };
            if (EditorUtility.scriptCompilationFailed)
                return new { ok = false, error = "scripts have compile errors (harness_console)" };

            string json, source;
            var trimmed = (scenario ?? "").Trim();
            if (trimmed.StartsWith("{", StringComparison.Ordinal)) { json = trimmed; source = "inline"; }
            else
            {
                var path = HarnessPaths.Resolve(trimmed);
                if (!File.Exists(path)) return new { ok = false, error = "scenario file not found: " + path };
                json = File.ReadAllText(path);
                source = trimmed;
            }
            Scenario parsed;
            try { parsed = JsonUtility.FromJson<Scenario>(json); }
            catch (Exception e) { return new { ok = false, error = "invalid scenario JSON: " + e.Message }; }
            if (parsed == null) return new { ok = false, error = "invalid scenario JSON" };

            var scenePath = HarnessPaths.ResolvePlayScene(parsed.scene, out var sceneError);
            if (scenePath == null) return new { ok = false, error = sceneError };
            DestroyLeakedRuntimeObjects();
            if (!HarnessPaths.OpenScene(scenePath, out var openError)) return new { ok = false, error = openError };

            var outDir = HarnessPaths.Resolve(@out);
            try
            {
                if (Directory.Exists(outDir))
                {
                    foreach (var f in Directory.GetFiles(outDir, "*.png")) File.Delete(f);
                    var old = Path.Combine(outDir, "result.json");
                    if (File.Exists(old)) File.Delete(old);
                }
                Directory.CreateDirectory(outDir);
            }
            catch (Exception e) { return new { ok = false, error = "cannot prepare out dir: " + e.Message }; }

            var st = new PlayState
            {
                id = Guid.NewGuid().ToString("N").Substring(0, 12),
                state = "entering",
                scenarioJson = json,
                scenarioSource = source,
                outDir = outDir,
                requestedAt = HarnessPaths.UtcNow(),
                timeoutSec = timeoutSec > 0 ? timeoutSec : parsed.durationSec + parsed.readyTimeoutSec + 60f,
            };
            st.Save();
            s_Watch.Restart();
            // Not delayCall: it runs "after inspectors update", which never happens in an unfocused Editor.
            // Setting isPlaying is itself deferred to the end of the frame, so this reply still goes out first.
            EditorApplication.isPlaying = true;
            return new { ok = true, id = st.id, state = st.state, outDir, scenario = source, scene = scenePath, poll = "harness_play_status" };
        }

        /// <summary>Destroy runtime harness objects that outlived play mode (defensive; they must never tick in a later session).</summary>
        static int DestroyLeakedRuntimeObjects()
        {
            var n = 0;
            foreach (var r in Resources.FindObjectsOfTypeAll<GameRoot>())
                if (r != null && !EditorUtility.IsPersistent(r)) { UnityEngine.Object.DestroyImmediate(r.gameObject); n++; }
            foreach (var r in Resources.FindObjectsOfTypeAll<ScenarioRunner>())
                if (r != null && !EditorUtility.IsPersistent(r)) { UnityEngine.Object.DestroyImmediate(r.gameObject); n++; }
            if (n > 0) Debug.LogWarning($"[Harness] destroyed {n} runtime object(s) that survived play mode");
            return n;
        }

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode) DestroyLeakedRuntimeObjects();
            var st = PlayState.Load();
            if (st == null) return;
            try
            {
                switch (change)
                {
                    case PlayModeStateChange.EnteredPlayMode when st.state == "entering":
                    {
                        var scenario = JsonUtility.FromJson<Scenario>(st.scenarioJson);
                        var runner = ScenarioRunner.Spawn(st.id, scenario, st.outDir);
                        var id = st.id;
                        runner.onFinished = result =>
                        {
                            var s = PlayState.Load();
                            if (s == null || s.id != id) return;
                            s.state = "exiting";
                            s.error = result.error;
                            s.Save();
                            EditorApplication.isPlaying = false;
                        };
                        st.state = "running";
                        st.startedAt = HarnessPaths.UtcNow();
                        st.Save();
                        if (!s_Watch.IsRunning) s_Watch.Restart();
                        break;
                    }
                    case PlayModeStateChange.EnteredEditMode when st.state == "exiting" || st.state == "running" || st.state == "entering":
                    {
                        if (st.state != "exiting" && string.IsNullOrEmpty(st.error))
                            st.error = st.state == "entering" ? "play mode did not start" : "play mode exited before the scenario finished";
                        var resultOk = false;
                        if (File.Exists(st.ResultPath))
                        {
                            var r = JsonUtility.FromJson<PlayResult>(File.ReadAllText(st.ResultPath));
                            resultOk = r != null && r.success;
                            if (r != null && !r.success && string.IsNullOrEmpty(st.error)) st.error = r.error;
                        }
                        st.state = st.state == "exiting" && resultOk ? "done" : "failed";
                        st.finishedAt = HarnessPaths.UtcNow();
                        st.Save();
                        s_Watch.Stop();
                        break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                st.state = "failed";
                st.error = "harness driver error: " + e.Message;
                st.Save();
                if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            }
        }

        static double s_NextWatchdog;

        static void Watchdog()
        {
            if (EditorApplication.timeSinceStartup < s_NextWatchdog) return;
            s_NextWatchdog = EditorApplication.timeSinceStartup + 0.5;
            if (!s_Watch.IsRunning) return;
            var st = PlayState.Load();
            if (st == null || (st.state != "running" && st.state != "entering")) return;
            if (s_Watch.Elapsed.TotalSeconds < st.timeoutSec) return;
            Debug.LogError($"[Harness] scenario timed out after {st.timeoutSec:0}s");
            if (ScenarioRunner.Current != null) ScenarioRunner.Current.Abort($"timeout after {st.timeoutSec:0}s");
            else
            {
                st.state = "failed";
                st.error = $"timeout after {st.timeoutSec:0}s";
                st.Save();
            }
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            s_Watch.Stop();
        }

        [CliCommand("harness_play_status",
            "State of the last harness_play: entering | running | exiting | done | failed, with the parsed result.json once finished.",
            MainThreadRequired = false, Tags = new[] { "harness", "editor/playmode" })]
        public static object Status()
        {
            var st = PlayState.Load();
            if (st == null) return new { ok = true, state = "idle" };
            PlayResult result = null;
            if ((st.state == "done" || st.state == "failed") && File.Exists(st.ResultPath))
            {
                try { result = JsonUtility.FromJson<PlayResult>(File.ReadAllText(st.ResultPath)); } catch { }
            }
            return new
            {
                ok = true,
                id = st.id,
                state = st.state,
                error = st.error,
                scenario = st.scenarioSource,
                outDir = st.outDir,
                requestedAt = st.requestedAt,
                startedAt = st.startedAt,
                finishedAt = st.finishedAt,
                resultPath = File.Exists(st.ResultPath) ? st.ResultPath : null,
                result,
            };
        }

        [CliCommand("harness_stats",
            "Frame stats: while playing, live numbers from the running scenario; otherwise the last play result. " +
            "fps {avg, min, p95ms, p99ms, hitches, cpuMainAvgMs}, render {batches, setPassCalls, drawCalls, triangles, vertices}.",
            Tags = new[] { "harness", "observability/performance" })]
        public static object Stats()
        {
            if (EditorApplication.isPlaying && ScenarioRunner.Current != null)
            {
                var s = ScenarioRunner.Current.Snapshot();
                return new { ok = true, live = true, s.id, s.scenario, s.frames, s.gameSec, s.wallSec, s.editorFocused, s.fps, s.render };
            }
            var st = PlayState.Load();
            if (st == null || !File.Exists(st.ResultPath)) return new { ok = false, error = "no play result yet (run harness_play)" };
            var r = JsonUtility.FromJson<PlayResult>(File.ReadAllText(st.ResultPath));
            return new { ok = true, live = false, r.id, r.scenario, r.frames, r.gameSec, r.wallSec, r.editorFocused, r.fps, r.render };
        }
    }
}
