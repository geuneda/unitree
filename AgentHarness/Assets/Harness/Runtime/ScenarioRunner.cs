using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Debug = UnityEngine.Debug;

namespace Harness
{
    /// <summary>
    /// Executes a <see cref="Scenario"/> in play mode: waits for <see cref="HarnessProbe.Ready"/>,
    /// replays input through <see cref="ScriptedInput"/>, captures shots, records frame/render stats and
    /// runtime errors, writes result.json and invokes <see cref="onFinished"/>.
    /// Spawned by the Editor (harness_play); has no Editor dependency itself.
    /// </summary>
    [DefaultExecutionOrder(-2000)]
    public sealed class ScenarioRunner : MonoBehaviour
    {
        public static ScenarioRunner Current { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Current = null;

        public Action<PlayResult> onFinished;

        Scenario m_Scenario;
        string m_OutDir;
        PlayResult m_Result;
        ScriptedInput m_Input;

        readonly Stopwatch m_Wall = new Stopwatch();
        readonly Stopwatch m_Frame = new Stopwatch();
        readonly List<float> m_FrameMs = new List<float>(1024);
        readonly List<float> m_CpuMs = new List<float>(1024);
        readonly List<ShotResult> m_Shots = new List<ShotResult>();
        readonly List<LogEntry> m_Errors = new List<LogEntry>();
        readonly object m_LogLock = new object();
        readonly List<ScenarioEvent> m_Timeline = new List<ScenarioEvent>();
        readonly HashSet<string> m_UsedPresets = new HashSet<string>();

        ProfilerRecorder m_Batches, m_SetPass, m_DrawCalls, m_Tris, m_Verts, m_MainThread;
        double m_BatchesSum, m_SetPassSum, m_DrawSum, m_TrisSum, m_VertsSum;
        int m_RenderSamples;

        bool m_Ready, m_Finished, m_CapturedLastFrame;
        float m_ReadyGameTime;
        int m_NextEvent, m_NextCapture;
        ScenarioCapture[] m_Captures;
        int m_Warnings, m_ErrorsTotal;

        public float ScenarioTime => m_Ready ? Time.time - m_ReadyGameTime : 0f;
        public bool Finished => m_Finished;

        public static ScenarioRunner Spawn(string id, Scenario scenario, string outDir)
        {
            var go = new GameObject("[HarnessScenarioRunner]"); // not DontSave: must die with play mode
            DontDestroyOnLoad(go);
            var r = go.AddComponent<ScenarioRunner>();
            r.m_Scenario = scenario ?? new Scenario();
            r.m_OutDir = outDir;
            r.m_Result = new PlayResult { id = id, scenario = r.m_Scenario.name };
            Current = r;
            return r;
        }

        void Awake()
        {
            m_Wall.Start();
            Application.logMessageReceivedThreaded += OnLog;
        }

        void Start()
        {
            // Expand keyTap into down/up pairs and sort the timeline.
            foreach (var e in m_Scenario.events ?? Array.Empty<ScenarioEvent>())
            {
                if (e == null || string.IsNullOrEmpty(e.type)) continue;
                if (e.type == "keyTap")
                {
                    m_Timeline.Add(new ScenarioEvent { t = e.t, type = "keyDown", key = e.key });
                    m_Timeline.Add(new ScenarioEvent { t = e.t + Mathf.Max(0.02f, e.hold), type = "keyUp", key = e.key });
                }
                else m_Timeline.Add(e);
            }
            m_Timeline.Sort((a, b) => a.t.CompareTo(b.t));
            m_Captures = (ScenarioCapture[])(m_Scenario.captures ?? Array.Empty<ScenarioCapture>()).Clone();
            Array.Sort(m_Captures, (a, b) => a.t.CompareTo(b.t));

            if (m_Scenario.fixedDeltaTime > 0f) Time.captureDeltaTime = m_Scenario.fixedDeltaTime;

            m_Batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            m_SetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            m_DrawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            m_Tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            m_Verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            m_MainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 4);
        }

        void Update()
        {
            if (m_Finished) return;

            if (!m_Ready)
            {
                if (HarnessProbe.Ready)
                {
                    m_Ready = true;
                    m_ReadyGameTime = Time.time;
                    m_Result.probeReady = true;
                    m_Result.readySec = (float)m_Wall.Elapsed.TotalSeconds;
                    m_Input = ScriptedInput.Create();
                    m_Frame.Restart();
                }
                else if (m_Wall.Elapsed.TotalSeconds > m_Scenario.readyTimeoutSec)
                {
                    Finish($"HarnessProbe.Ready did not become true within {m_Scenario.readyTimeoutSec}s");
                }
                return;
            }

            // Frame stats (wall clock; captureDeltaTime does not affect the Stopwatch).
            var ms = (float)m_Frame.Elapsed.TotalMilliseconds;
            m_Frame.Restart();
            var st = ScenarioTime;
            var capturedLastFrame = m_CapturedLastFrame;
            m_CapturedLastFrame = false;
            // Frames that paid for an offscreen capture (render + readback + PNG encode) are not game cost.
            if (st > m_Scenario.warmupSec && !capturedLastFrame)
            {
                m_FrameMs.Add(ms);
                if (m_MainThread.Valid && m_MainThread.LastValue > 0) m_CpuMs.Add(m_MainThread.LastValue / 1e6f);
                SampleRender();
            }

            while (m_NextEvent < m_Timeline.Count && m_Timeline[m_NextEvent].t <= st)
            {
                Apply(m_Timeline[m_NextEvent]);
                m_NextEvent++;
            }
        }

        void LateUpdate()
        {
            if (m_Finished || !m_Ready) return;
            var st = ScenarioTime;
            while (m_NextCapture < m_Captures.Length && m_Captures[m_NextCapture].t <= st)
            {
                DoCapture(m_Captures[m_NextCapture], m_NextCapture, st);
                m_NextCapture++;
            }
            if (st >= m_Scenario.durationSec && m_NextCapture >= m_Captures.Length && m_NextEvent >= m_Timeline.Count)
            {
                if (m_PendingScreen > 0 && m_Wall.Elapsed.TotalSeconds - m_ScreenRequestedAt < 3.0) return; // let end-of-frame captures land
                foreach (var (label, path, t) in m_ScreenQueue)
                    m_Shots.Add(new ShotResult { name = label, preset = "screen", path = path, t = t, blank = true, error = "Game view did not render (end of frame never reached) - keep a Game view tab visible" });
                m_ScreenQueue.Clear();
                Finish(null);
            }
        }

        void SampleRender()
        {
            if (!m_Batches.Valid) return;
            m_BatchesSum += m_Batches.LastValue;
            m_SetPassSum += m_SetPass.LastValue;
            m_DrawSum += m_DrawCalls.LastValue;
            m_TrisSum += m_Tris.LastValue;
            m_VertsSum += m_Verts.LastValue;
            m_RenderSamples++;
        }

        void Apply(ScenarioEvent e)
        {
            try
            {
                switch (e.type)
                {
                    case "keyDown": m_Input.KeyDown(ParseEnum<Key>(e.key)); break;
                    case "keyUp": m_Input.KeyUp(ParseEnum<Key>(e.key)); break;
                    case "mouseMove": m_Input.MouseMove(new Vector2(e.x, e.y)); break;
                    case "mousePos": m_Input.MousePosition(new Vector2(e.x, e.y)); break;
                    case "mouseDown": m_Input.MouseButton(ParseEnum<MouseButton>(e.key ?? "Left"), true); break;
                    case "mouseUp": m_Input.MouseButton(ParseEnum<MouseButton>(e.key ?? "Left"), false); break;
                    case "scroll": m_Input.Scroll(new Vector2(e.x, e.y)); break;
                    case "stick": m_Input.Stick(!string.Equals(e.key, "right", StringComparison.OrdinalIgnoreCase), new Vector2(e.x, e.y)); break;
                    case "padDown": m_Input.PadButton(ParseEnum<GamepadButton>(e.key), true); break;
                    case "padUp": m_Input.PadButton(ParseEnum<GamepadButton>(e.key), false); break;
                    case "releaseAll": m_Input.ReleaseAll(); break;
                    default: throw new ArgumentException($"unknown event type '{e.type}'");
                }
                m_Result.inputEventsApplied++;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Harness] scenario event at t={e.t} ({e.type} {e.key}) failed: {ex.Message}");
            }
        }

        static T ParseEnum<T>(string s) where T : struct
        {
            if (!string.IsNullOrEmpty(s) && Enum.TryParse<T>(s, true, out var v)) return v;
            throw new ArgumentException($"'{s}' is not a valid {typeof(T).Name}");
        }

        void DoCapture(ScenarioCapture c, int index, float st)
        {
            var presetName = string.IsNullOrEmpty(c.preset) ? "auto" : c.preset;
            if (presetName == "screen")
            {
                var screenLabel = !string.IsNullOrEmpty(c.name) ? c.name : "screen";
                var screenPath = Path.Combine(m_OutDir, $"shot{index}_{Sanitize(screenLabel)}.png").Replace('\\', '/');
                StartCoroutine(CaptureScreen(screenLabel, screenPath, st));
                return;
            }

            var template = HarnessCapture.FindMainCamera();
            ShotPreset preset = null;
            if (presetName == "auto")
            {
                foreach (var p in ShotPreset.All())
                    if (!m_UsedPresets.Contains(p.presetName)) { preset = p; break; }
            }
            else if (presetName != "main")
            {
                preset = ShotPreset.Find(presetName);
                if (preset == null) Debug.LogError($"[Harness] ShotPreset '{presetName}' not found; using main camera");
            }

            var label = !string.IsNullOrEmpty(c.name) ? c.name : preset != null ? preset.presetName : "main";
            var path = Path.Combine(m_OutDir, $"shot{index}_{Sanitize(label)}.png").Replace('\\', '/');
            ShotResult r;
            if (template == null)
                r = new ShotResult { name = label, path = path, error = "no camera in scene" };
            else if (preset != null)
            {
                m_UsedPresets.Add(preset.presetName);
                r = HarnessCapture.Capture(template, preset, m_Scenario.width, m_Scenario.height, path);
            }
            else
            {
                var tr = template.transform;
                r = HarnessCapture.Capture(template, tr.position, tr.rotation, template.fieldOfView, m_Scenario.width, m_Scenario.height, path);
                r.preset = "main";
            }
            r.name = label;
            r.t = st;
            m_Shots.Add(r);
            m_CapturedLastFrame = true;
        }

        // "screen": what the Game view shows, including screen-space UI (UI Toolkit / overlay canvases).
        // Needs a rendering Game view; if end-of-frame never arrives the shot is reported as an error.
        int m_PendingScreen;
        double m_ScreenRequestedAt;
        readonly List<(string label, string path, float t)> m_ScreenQueue = new List<(string, string, float)>();

        System.Collections.IEnumerator CaptureScreen(string label, string path, float st)
        {
            m_PendingScreen++;
            m_ScreenRequestedAt = m_Wall.Elapsed.TotalSeconds;
            m_ScreenQueue.Add((label, path, st));
            yield return new WaitForEndOfFrame();
            m_ScreenQueue.Remove((label, path, st));
            var r = new ShotResult { name = label, preset = "screen", path = path, t = st };
            var sw = Stopwatch.StartNew();
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                r.width = tex.width;
                r.height = tex.height;
                HarnessCapture.Analyze(tex.GetPixels32(), r);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            catch (Exception e) { r.error = e.GetType().Name + ": " + e.Message; }
            finally
            {
                if (tex != null) Destroy(tex);
                r.renderMs = (float)sw.Elapsed.TotalMilliseconds;
            }
            m_Shots.Add(r);
            m_CapturedLastFrame = true;
            m_PendingScreen--;
        }

        static string Sanitize(string s)
        {
            foreach (var ch in Path.GetInvalidFileNameChars()) s = s.Replace(ch, '_');
            return s.Replace(' ', '_');
        }

        void OnLog(string message, string stack, LogType type)
        {
            if (type == LogType.Log) return;
            lock (m_LogLock)
            {
                if (type == LogType.Warning) { m_Warnings++; return; }
                m_ErrorsTotal++;
                foreach (var e in m_Errors)
                    if (e.message == message && e.type == type.ToString()) { e.count++; return; }
                if (m_Errors.Count >= 50) return;
                var entry = new LogEntry { type = type.ToString(), message = message, stack = HarnessLogParse.TrimStack(stack), t = m_Ready ? ScenarioTimeSafe() : 0f };
                HarnessLogParse.FillLocation(entry);
                m_Errors.Add(entry);
            }
        }

        // logMessageReceivedThreaded may fire off the main thread, where Time.time is unavailable.
        float ScenarioTimeSafe()
        {
            try { return System.Threading.Thread.CurrentThread.ManagedThreadId == s_MainThreadId ? ScenarioTime : -1f; }
            catch { return -1f; }
        }

        static int s_MainThreadId;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void CaptureMainThread() => s_MainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;

        /// <summary>Live stats for harness_stats while playing.</summary>
        public PlayResult Snapshot()
        {
            var r = new PlayResult
            {
                id = m_Result.id,
                scenario = m_Result.scenario,
                probeReady = m_Result.probeReady,
                readySec = m_Result.readySec,
                wallSec = (float)m_Wall.Elapsed.TotalSeconds,
                gameSec = ScenarioTime,
                frames = m_FrameMs.Count,
                editorFocused = Application.isFocused,
            };
            FillStats(r);
            return r;
        }

        void FillStats(PlayResult r)
        {
            r.fps = ComputeFps(m_FrameMs, m_CpuMs);
            if (m_RenderSamples > 0)
            {
                r.render = new RenderStats
                {
                    samples = m_RenderSamples,
                    batches = (float)(m_BatchesSum / m_RenderSamples),
                    setPassCalls = (float)(m_SetPassSum / m_RenderSamples),
                    drawCalls = (float)(m_DrawSum / m_RenderSamples),
                    triangles = (float)(m_TrisSum / m_RenderSamples),
                    vertices = (float)(m_VertsSum / m_RenderSamples),
                };
            }
        }

        static FpsStats ComputeFps(List<float> frameMs, List<float> cpuMs)
        {
            var s = new FpsStats { samples = frameMs.Count };
            if (frameMs.Count == 0) return s;
            var sorted = frameMs.ToArray();
            Array.Sort(sorted);
            double sum = 0;
            foreach (var v in sorted) { sum += v; if (v > 50f) s.hitches++; }
            s.avgMs = (float)(sum / sorted.Length);
            s.avg = s.avgMs > 0 ? 1000f / s.avgMs : 0f;
            s.maxMs = sorted[sorted.Length - 1];
            s.min = s.maxMs > 0 ? 1000f / s.maxMs : 0f;
            s.p95ms = Percentile(sorted, 0.95f);
            s.p99ms = Percentile(sorted, 0.99f);
            if (cpuMs.Count > 0)
            {
                var c = cpuMs.ToArray();
                Array.Sort(c);
                double cs = 0; foreach (var v in c) cs += v;
                s.cpuMainAvgMs = (float)(cs / c.Length);
                s.cpuMainP95Ms = Percentile(c, 0.95f);
            }
            return s;
        }

        static float Percentile(float[] sorted, float p)
        {
            var idx = Mathf.Clamp(Mathf.CeilToInt(p * sorted.Length) - 1, 0, sorted.Length - 1);
            return sorted[idx];
        }

        void Finish(string error)
        {
            if (m_Finished) return;
            m_Finished = true;
            m_Result.success = error == null;
            m_Result.error = error;
            m_Result.wallSec = (float)m_Wall.Elapsed.TotalSeconds;
            m_Result.gameSec = ScenarioTime;
            m_Result.frames = m_FrameMs.Count;
            m_Result.editorFocused = Application.isFocused;
            m_Result.modules = HarnessProbe.InitializedModules.ToArray();
            m_Result.failedModules = HarnessProbe.FailedModules.ToArray();
            FillStats(m_Result);
            m_Shots.Sort((a, b) => a.t.CompareTo(b.t));
            m_Result.shots = m_Shots.ToArray();
            lock (m_LogLock)
            {
                m_Result.runtimeErrors = m_Errors.ToArray();
                m_Result.errorCount = m_ErrorsTotal;
                m_Result.warningCount = m_Warnings;
            }
            var counts = new List<NamedCount>();
            foreach (var kv in EventBus.Counts) counts.Add(new NamedCount { name = kv.Key, count = kv.Value });
            counts.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            m_Result.events = counts.ToArray();
            if (m_Result.success && m_Result.failedModules.Length > 0)
            {
                m_Result.success = false;
                m_Result.error = "module Init failed: " + string.Join(", ", m_Result.failedModules);
            }

            Time.captureDeltaTime = 0f;
            m_Input?.Dispose();
            m_Input = null;

            try
            {
                Directory.CreateDirectory(m_OutDir);
                File.WriteAllText(Path.Combine(m_OutDir, "result.json"), JsonUtility.ToJson(m_Result, true));
            }
            catch (Exception e) { Debug.LogException(e); }

            try { onFinished?.Invoke(m_Result); }
            catch (Exception e) { Debug.LogException(e); }
        }

        /// <summary>Abort from outside (Editor watchdog / play mode exit).</summary>
        public void Abort(string reason) => Finish(reason);

        public PlayResult Result => m_Result;

        void OnDestroy()
        {
            if (!m_Finished) Finish("play mode ended before the scenario finished");
            Application.logMessageReceivedThreaded -= OnLog;
            m_Batches.Dispose(); m_SetPass.Dispose(); m_DrawCalls.Dispose();
            m_Tris.Dispose(); m_Verts.Dispose(); m_MainThread.Dispose();
            if (Current == this) Current = null;
        }
    }
}
