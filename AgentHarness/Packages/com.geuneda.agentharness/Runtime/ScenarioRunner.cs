using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Debug = UnityEngine.Debug;

namespace Harness
{
    /// <summary>
    /// Executes a <see cref="Scenario"/> in play mode: keeps real input out (<see cref="RealInputIsolation"/>, "begin"/"end"
    /// to the game's input hooks), waits for <see cref="HarnessProbe.Ready"/>,
    /// replays input through <see cref="ScriptedInput"/> (Input System) and/or the game's [AgentHarnessInput] methods
    /// (<see cref="InputHookReplay"/>, for the legacy Input Manager), waits for scenes (waitScene), captures shots, records
    /// frame/render stats, scene loads and runtime errors, writes result.json and invokes <see cref="onFinished"/>.
    /// Spawned by the Editor (harness_play); has no Editor dependency itself (the Editor hands in the input hooks).
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
        Action<string, string, Vector2> m_InputHook;
        readonly List<IScenarioInput> m_Inputs = new List<IScenarioInput>();
#if AGENTHARNESS_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        RealInputIsolation m_Isolation;
#endif
        bool m_RunInBackground;

        readonly Stopwatch m_Wall = new Stopwatch();
        readonly Stopwatch m_Frame = new Stopwatch();
        readonly List<float> m_FrameMs = new List<float>(1024);
        readonly List<float> m_CpuMs = new List<float>(1024);
        readonly List<ShotResult> m_Shots = new List<ShotResult>();
        readonly List<LogEntry> m_Errors = new List<LogEntry>();
        readonly object m_LogLock = new object();
        readonly List<ScenarioEvent> m_Timeline = new List<ScenarioEvent>();
        readonly HashSet<string> m_UsedPresets = new HashSet<string>();
        readonly List<SceneLoad> m_Scenes = new List<SceneLoad>();
        readonly List<ScenarioWait> m_Waits = new List<ScenarioWait>();
        readonly List<ClickTarget> m_Clicks = new List<ClickTarget>();
        readonly HashSet<ScenarioEvent> m_NextFrame = new HashSet<ScenarioEvent>();   // applied a frame after the event before it
        int m_LastApplyFrame = -1;

        // waitScene/waitTarget: the scenario clock stands still at m_WaitAt until the scene is loaded / the target is there.
        ScenarioEvent m_Wait;
        float m_WaitAt, m_WaitStartGame, m_PausedGame;
        double m_WaitStartWall;
        int m_WaitFrames;

        ProfilerRecorder m_Batches, m_SetPass, m_DrawCalls, m_Tris, m_Verts, m_MainThread;
        readonly List<ProfilerRecorder> m_DrawCallKinds = new List<ProfilerRecorder>();
        double m_BatchesSum, m_SetPassSum, m_DrawSum, m_TrisSum, m_VertsSum;

        // Unity 6.6 has no Render "Batches Count" / "Draw Calls Count" any more (a recorder by those names reads UI Toolkit's):
        // the draw calls are the sum of its kinds, the batches are not reported (-1).
        static readonly string[] s_DrawCallKinds =
        {
            "Standard Draw Calls Count", "Standard Indirect Draw Calls Count", "Standard Instanced Draw Calls Count", "SRP Batcher Draw Calls Count",
            "BRG Draw Calls Count", "BRG Indirect Draw Calls Count", "Null Geometry Draw Calls Count", "Null Geometry Indirect Draw Calls Count",
        };

        /// <summary>The counters of the Render category by name.</summary>
        static Dictionary<string, Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle> RenderCounters()
        {
            var handles = new List<Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
            Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetAvailable(handles);
            var render = new Dictionary<string, Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle>();
            foreach (var h in handles)
            {
                var d = Unity.Profiling.LowLevel.Unsafe.ProfilerRecorderHandle.GetDescription(h);
                if (d.Category.Name == ProfilerCategory.Render.Name) render[d.Name] = h;
            }
            return render;
        }
        int m_RenderSamples;

        bool m_Ready, m_Finished, m_CapturedLastFrame;
        float m_ReadyGameTime;
        int m_NextEvent, m_NextCapture;
        ScenarioCapture[] m_Captures;
        int m_Warnings, m_ErrorsTotal;

        public float ScenarioTime => !m_Ready ? 0f : m_Wait != null ? m_WaitAt : Time.time - m_ReadyGameTime - m_PausedGame;
        public bool Finished => m_Finished;

        /// <summary>
        /// A Player run (G3-8): every capture from the game's own camera (no shot pose, no "camera") also saves what the screen
        /// showed in that frame, next to the shot (<see cref="ShotResult.screen"/>).
        /// </summary>
        public bool ScreenTwins { get; set; }

        /// <param name="inputHook">The game's [AgentHarnessInput] methods (found by the Editor), or null.</param>
        public static ScenarioRunner Spawn(string id, Scenario scenario, string outDir, Action<string, string, Vector2> inputHook = null)
        {
            var go = new GameObject("[HarnessScenarioRunner]"); // not DontSave: must die with play mode
            DontDestroyOnLoad(go);
            var r = go.AddComponent<ScenarioRunner>();
            go.AddComponent<ScenarioCaptureDriver>().runner = r;
            r.m_Scenario = scenario ?? new Scenario();
            r.m_OutDir = outDir;
            r.m_InputHook = inputHook;
            r.m_Result = new PlayResult { id = id, scenario = r.m_Scenario.name };
            Current = r;
            return r;
        }

        void Awake()
        {
            m_Wall.Start();
            Application.logMessageReceivedThreaded += OnLog;
            // The play scene is already loaded when the Editor spawns the runner: list it (and any others) first.
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded) m_Scenes.Add(new SceneLoad { name = s.name, path = s.path, mode = "Start", t = -1f, wallSec = 0f });
            }
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            m_Scenes.Add(new SceneLoad { name = scene.name, path = scene.path, mode = mode.ToString(), t = m_Ready ? ScenarioTime : -1f, wallSec = (float)m_Wall.Elapsed.TotalSeconds });
        }

        void Start()
        {
            // Expand keyTap and click into their parts and sort the timeline (stable: equal times keep the file's order).
            var list = new List<ScenarioEvent>();
            foreach (var e in m_Scenario.events ?? Array.Empty<ScenarioEvent>())
            {
                if (e == null || string.IsNullOrEmpty(e.type)) continue;
                if (e.type == "keyTap")
                {
                    list.Add(new ScenarioEvent { t = e.t, type = "keyDown", key = e.key });
                    list.Add(new ScenarioEvent { t = e.t + Mathf.Max(0.02f, e.hold), type = "keyUp", key = e.key });
                }
                else if (e.type == "click")
                {
                    // Move there, then press and release on later frames (UI input modules see the pointer arrive first).
                    list.Add(new ScenarioEvent { t = e.t, type = "mousePos", x = e.x, y = e.y, target = e.target });
                    var down = new ScenarioEvent { t = e.t, type = "mouseDown", key = e.key };
                    var up = new ScenarioEvent { t = e.t + Mathf.Max(0.02f, e.hold), type = "mouseUp", key = e.key };
                    list.Add(down); m_NextFrame.Add(down);
                    list.Add(up); m_NextFrame.Add(up);
                }
                else list.Add(e);
            }
            var order = new Dictionary<ScenarioEvent, int>();
            for (var i = 0; i < list.Count; i++) order[list[i]] = i;
            list.Sort((a, b) => a.t != b.t ? a.t.CompareTo(b.t) : order[a].CompareTo(order[b]));
            m_Timeline.AddRange(list);
            m_Captures = (ScenarioCapture[])(m_Scenario.captures ?? Array.Empty<ScenarioCapture>()).Clone();
            Array.Sort(m_Captures, (a, b) => a.t.CompareTo(b.t));

            HarnessCapture.DefaultSize(ref m_Scenario.width, ref m_Scenario.height);   // the Editor has set it already
            // Keep playing while the Editor is in the background, for this session only (the project's Player setting
            // is not changed; an attached project may ship with Run In Background off).
            m_RunInBackground = Application.runInBackground;
            Application.runInBackground = true;
            BeginIsolation();

            var render = RenderCounters();
            if (render.TryGetValue("Batches Count", out var batches)) m_Batches = new ProfilerRecorder(batches, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately);
            m_SetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            if (render.TryGetValue("Draw Calls Count", out var draws)) m_DrawCalls = new ProfilerRecorder(draws, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately);
            else
                foreach (var kind in s_DrawCallKinds)
                    if (render.TryGetValue(kind, out var h)) m_DrawCallKinds.Add(new ProfilerRecorder(h, 1, ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately));
            m_Tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            m_Verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count");
            m_MainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 4);
        }

        /// <summary>
        /// The fixed time step from the next frame on (this frame's delta time is already set), on the runner's first Update -
        /// in the Editor the frame its Start runs, in a Player run (<see cref="PlayerRun"/>) the frame after, so both play the
        /// same frames. UI Toolkit transitions and timers run on real time: they follow the frames too (PanelClock).
        /// </summary>
        void BeginClock()
        {
            m_ClockStarted = true;
            if (m_Scenario.fixedDeltaTime <= 0f) return;
            Time.captureDeltaTime = m_Scenario.fixedDeltaTime;
            m_Result.uiClock.error = PanelClock.Begin();
            if (m_Result.uiClock.error == null) m_Result.uiClock.mode = "frames";
        }

        bool m_ClockStarted;

        void Update()
        {
            if (m_Finished) return;
            if (!m_ClockStarted) BeginClock();
            PanelClock.Advance(m_Scenario.fixedDeltaTime);

            if (!m_Ready)
            {
#if !UNITY_EDITOR
                // A Player shows the splash screen over the first scene: the scenario starts when the game is on screen.
                if (!UnityEngine.Rendering.SplashScreen.isFinished)
                {
                    m_Result.player.splashSec = (float)m_Wall.Elapsed.TotalSeconds;
                    if (m_Wall.Elapsed.TotalSeconds > m_Scenario.readyTimeoutSec + 30f) Finish("the splash screen did not finish");
                    return;
                }
#endif
                if (HarnessProbe.Ready)
                {
                    m_Ready = true;
                    m_ReadyGameTime = Time.time;
                    m_Result.probeReady = true;
                    m_Result.readySec = (float)m_Wall.Elapsed.TotalSeconds;
#if !UNITY_EDITOR
                    Debug.Log($"[Harness] scenario clock started (frame {Time.frameCount}, splash {m_Result.player.splashSec:0.00} s)");
#endif
                    CreateInputs();
                    m_Frame.Restart();
                }
                else if (m_Wall.Elapsed.TotalSeconds - m_Result.player.splashSec > m_Scenario.readyTimeoutSec)
                {
                    Finish($"HarnessProbe.Ready did not become true within {m_Scenario.readyTimeoutSec}s");
                }
                return;
            }

            // Frame stats (wall clock; captureDeltaTime does not affect the Stopwatch).
            var ms = (float)m_Frame.Elapsed.TotalMilliseconds;
            m_Frame.Restart();
            // Frames spent waiting for a scene are loading, not game cost; the clock resumes where it stopped.
            var waited = m_Wait != null;
            if (waited)
            {
                if (!ContinueWait()) return;
                m_LastApplyFrame = Time.frameCount;
                m_NextEvent++;   // the wait event itself
            }
            var st = ScenarioTime;
            var capturedLastFrame = m_CapturedLastFrame;
            m_CapturedLastFrame = false;
            // Frames that paid for an offscreen capture (render + readback + PNG encode) are not game cost.
            if (st > m_Scenario.warmupSec && !capturedLastFrame && !waited)
            {
                m_FrameMs.Add(ms);
                if (m_MainThread.Valid && m_MainThread.LastValue > 0) m_CpuMs.Add(m_MainThread.LastValue / 1e6f);
                SampleRender();
            }

            while (m_NextEvent < m_Timeline.Count && m_Timeline[m_NextEvent].t <= st)
            {
                var e = m_Timeline[m_NextEvent];
                if (m_NextFrame.Contains(e) && m_LastApplyFrame == Time.frameCount) break;
                if (e.type == "waitScene" || e.type == "waitTarget")
                {
                    StartWait(e, st);
                    if (!ContinueWait()) return;
                }
                else Apply(e);
                m_LastApplyFrame = Time.frameCount;
                m_NextEvent++;
            }
        }

        // ---- waitScene / waitTarget ------------------------------------------------------------------------------------

        void StartWait(ScenarioEvent e, float st)
        {
            m_Wait = e;
            m_WaitAt = st;
            m_WaitStartGame = Time.time;
            m_WaitStartWall = m_Wall.Elapsed.TotalSeconds;
            m_WaitFrames = 0;
        }

        /// <summary>True once the awaited scene is loaded or target is there (the clock runs again from where it stopped).</summary>
        bool ContinueWait()
        {
            var e = m_Wait;
            var scene = e.type == "waitScene";
            var what = scene ? e.scene : e.target;
            if (string.IsNullOrWhiteSpace(what))
            {
                m_Wait = null;
                Debug.LogError($"[Harness] scenario event at t={e.t} ({e.type}) has no \"{(scene ? "scene" : "target")}\"");
                return true;
            }
            var waited = m_Wall.Elapsed.TotalSeconds - m_WaitStartWall;
            if (scene ? ShotPose.SceneLoaded(what) : TryTargetPoint(what, out _, out _))
            {
                m_PausedGame += Time.time - m_WaitStartGame;
                m_Wait = null;
                m_Waits.Add(new ScenarioWait { type = e.type, target = what, t = m_WaitAt, waitedSec = (float)waited, frames = m_WaitFrames });
                return true;
            }
            m_WaitFrames++;
            var limit = e.timeoutSec > 0f ? e.timeoutSec : DefaultWaitTimeoutSec;
            if (waited > limit)
                Finish(scene ? $"waitScene '{what}' at t={e.t}: not loaded after {limit:0.#}s (loaded: {LoadedScenes()})"
                             : $"waitTarget '{what}' at t={e.t}: no such active, visible GameObject or UI Toolkit element after {limit:0.#}s (active scene: {SceneManager.GetActiveScene().name})");
            return false;
        }

        public const float DefaultWaitTimeoutSec = 30f;

        static string LoadedScenes()
        {
            var names = new List<string>();
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                names.Add(s.isLoaded ? s.name : s.name + " (loading)");
            }
            return string.Join(", ", names);
        }

        /// <summary>
        /// Captures and the end check run after every other LateUpdate (<see cref="ScenarioCaptureDriver"/>): game code
        /// that moves the camera (Cinemachine) or submits draws (Graphics.DrawMeshInstancedIndirect) in LateUpdate is
        /// then part of the shot. This runner's own Update runs first, so scripted input is seen by the same frame.
        /// </summary>
        internal void LateCaptures()
        {
            if (m_Finished || !m_Ready) return;
            var st = ScenarioTime;
            foreach (var s in m_Sequences.ToArray())
                if (Time.frameCount >= s.nextFrame) CaptureFrame(s, st);
            while (m_NextCapture < m_Captures.Length && m_Captures[m_NextCapture].t <= st)
            {
                DoCapture(m_Captures[m_NextCapture], m_NextCapture, st);
                m_NextCapture++;
            }
            if (st >= m_Scenario.durationSec && m_NextCapture >= m_Captures.Length && m_NextEvent >= m_Timeline.Count && m_Sequences.Count == 0)
            {
                if (m_PendingScreen > 0 && m_Wall.Elapsed.TotalSeconds - m_ScreenRequestedAt < 3.0) return; // let end-of-frame captures land
                foreach (var q in m_ScreenQueue)
                {
                    const string never = "the screen did not render (end of frame never reached) - keep a Game view tab visible";
                    if (q.twinOf != null) q.twinOf.screenError = never;
                    else m_Shots.Add(new ShotResult { name = q.label, preset = "screen", path = q.path, t = q.t, blank = true, error = never });
                }
                m_ScreenQueue.Clear();
                Finish(null);
            }
        }

        void SampleRender()
        {
            if (!m_SetPass.Valid) return;
            if (m_Batches.Valid) m_BatchesSum += m_Batches.LastValue;
            m_SetPassSum += m_SetPass.LastValue;
            if (m_DrawCalls.Valid) m_DrawSum += m_DrawCalls.LastValue;
            else foreach (var r in m_DrawCallKinds) m_DrawSum += r.LastValue;
            m_TrisSum += m_Tris.LastValue;
            m_VertsSum += m_Verts.LastValue;
            m_RenderSamples++;
        }

        // ---- Input ------------------------------------------------------------------------------------------------------

        /// <summary>
        /// From the first frame on, the game gets the scenario's input only (G3-6): real Input System devices are disabled
        /// (<see cref="RealInputIsolation"/>) and the game's [AgentHarnessInput] methods get "begin" (HarnessInput.cs then
        /// stops reading UnityEngine.Input). <see cref="EndIsolation"/> undoes both.
        /// </summary>
        void BeginIsolation()
        {
#if AGENTHARNESS_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            try { m_Isolation = RealInputIsolation.Begin(); }
            catch (Exception e) { Debug.LogException(e); }
#endif
            NotifyHooks("begin");
        }

        void EndIsolation()
        {
#if AGENTHARNESS_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            if (m_Isolation != null)
            {
                m_Result.isolatedDevices = m_Isolation.Report();
                try { m_Isolation.Dispose(); } catch (Exception e) { Debug.LogException(e); }
                m_Isolation = null;
            }
#endif
            NotifyHooks("end");
        }

        /// <summary>"begin"/"end" to every input hook; one that throws (an input layer that knows input events only) gets a warning.</summary>
        void NotifyHooks(string type)
        {
            if (m_InputHook == null) return;
            foreach (var d in m_InputHook.GetInvocationList())
            {
                try { ((Action<string, string, Vector2>)d)(type, null, default); }
                catch (Exception e) { Debug.LogWarning($"[Harness] input hook {d.Method.DeclaringType?.FullName}.{d.Method.Name} failed on \"{type}\": {e.Message}"); }
            }
        }

        /// <summary>
        /// Input backends for the kinds of input the timeline uses (none without input events): Input System virtual
        /// devices when the Input System is active, and the game's [AgentHarnessInput] methods when it has any (both at
        /// once is fine: a real keyboard also reaches both input systems with Active Input Handling 'Both').
        /// </summary>
        void CreateInputs()
        {
            bool keys = false, mouse = false, pad = false;
            foreach (var e in m_Timeline)
            {
                switch (e.type)
                {
                    case "keyDown": case "keyUp": keys = true; break;
                    case "mouseMove": case "mousePos": case "mouseDown": case "mouseUp": case "scroll": mouse = true; break;
                    case "stick": case "padDown": case "padUp": pad = true; break;
                }
            }
            if (!keys && !mouse && !pad) return;
#if AGENTHARNESS_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            m_Inputs.Add(ScriptedInput.Create(keys, mouse, pad));
#endif
            if (m_InputHook != null && (keys || mouse)) m_Inputs.Add(new InputHookReplay(m_InputHook));
            m_Result.inputBackends = m_Inputs.ConvertAll(i => i.Name).ToArray();
        }

        static string NoInputBackend()
        {
#if ENABLE_INPUT_SYSTEM && !AGENTHARNESS_INPUT_SYSTEM
            return "Active Input Handling is 'Input System Package' but com.unity.inputsystem is not installed";
#else
            return "nothing takes scenario input: the legacy Input Manager (UnityEngine.Input) cannot be driven from code. Read input through " +
                "HarnessInput (install.ps1 -InputShim adds Assets/AgentHarness/HarnessInput.cs, same members as Input), or mark a static " +
                "void M(string type, string key, Vector2 value) of your input code [AgentHarnessInput] (tools/AgentHarness.md)";
#endif
        }

        void Apply(ScenarioEvent e)
        {
            try
            {
                if (e.type == "mousePos" && !string.IsNullOrEmpty(e.target)) e = ResolveTarget(e);
                else if ((e.type == "mousePos" || e.type == "mouseMove") && string.Equals(m_Scenario.mouseSpace, "normalized", StringComparison.OrdinalIgnoreCase))
                    e = new ScenarioEvent { t = e.t, type = e.type, key = e.key, x = e.x * Screen.width, y = e.y * Screen.height };
                if (m_Inputs.Count == 0) throw new NotSupportedException(NoInputBackend());
                var applied = false;
                Exception error = null;
                foreach (var input in m_Inputs)
                {
                    try { applied |= input.Apply(e); }
                    catch (ArgumentException ex) { error = error ?? ex; }
                }
                if (!applied)
                {
                    if (error != null) throw error;
                    if (e.type == "stick" || e.type == "padDown" || e.type == "padUp")
                        throw new NotSupportedException("gamepad input needs the Input System (the legacy Input Manager reads joysticks from the OS)");
                    throw new ArgumentException($"unknown event type '{e.type}'");
                }
                m_Result.inputEventsApplied++;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Harness] scenario event at t={e.t} ({e.type} {e.key}{e.target}) failed: {ex.Message}");
            }
        }

        /// <summary>A mousePos at the screen center of <see cref="ScenarioEvent.target"/>.</summary>
        ScenarioEvent ResolveTarget(ScenarioEvent e)
        {
            if (!TryTargetPoint(e.target, out var p, out var via))
                throw new ArgumentException($"click target '{e.target}' not found (an active GameObject name or path, or a UI Toolkit element name)");
            m_Clicks.Add(new ClickTarget { target = e.target, t = ScenarioTime, x = p.x, y = p.y, via = via });
            return new ScenarioEvent { t = e.t, type = e.type, x = p.x, y = p.y };
        }

        /// <summary>Screen point (pixels, origin bottom left) of a uGUI element, a scene object, or a UI Toolkit element.</summary>
        static bool TryTargetPoint(string target, out Vector2 point, out string via)
        {
            point = default; via = null;
            var go = GameObject.Find(target);
            if (go != null)
            {
                if (go.transform is RectTransform rt)
                {
                    var canvas = go.GetComponentInParent<Canvas>();
                    Camera uiCam = null;
                    if (canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay) uiCam = canvas.rootCanvas.worldCamera;
                    var corners = new Vector3[4];
                    rt.GetWorldCorners(corners);
                    point = RectTransformUtility.WorldToScreenPoint(uiCam, (corners[0] + corners[2]) * 0.5f);
                    via = "ugui";
                    return true;
                }
                var cam = HarnessCapture.FindMainCamera();
                if (cam == null) return false;
                var center = go.transform.position;
                if (go.TryGetComponent<Renderer>(out var r)) center = r.bounds.center;
                else if (go.TryGetComponent<Collider>(out var c)) center = c.bounds.center;
                else if (go.TryGetComponent<Collider2D>(out var c2)) center = c2.bounds.center;
                var sp = cam.WorldToScreenPoint(center);
                if (sp.z <= 0f) return false;
                point = sp;
                via = "world";
                return true;
            }
            foreach (var doc in UnityCompat.FindObjects<UIDocument>(FindObjectsInactive.Exclude))
            {
                var root = doc.rootVisualElement;
                var el = root?.Q(target);
                var panelRoot = root?.panel?.visualTree;
                if (el == null || panelRoot == null) continue;
                // Laid out and shown (an element under a display:none ancestor has no size).
                var b = el.worldBound;
                if (float.IsNaN(b.x) || b.width <= 0f || b.height <= 0f || el.resolvedStyle.display == DisplayStyle.None || el.resolvedStyle.visibility != Visibility.Visible) continue;
                var c = b.center;
                if (UnityCompat.IsWorldSpace(doc.panelSettings))
                {
                    // A panel in the world: bounds are in the document's local units, y up (checked against a menu's button
                    // order); project that point with the main camera.
                    var worldCam = HarnessCapture.FindMainCamera();
                    if (worldCam == null) continue;
                    var wp = worldCam.WorldToScreenPoint(doc.transform.TransformPoint(new Vector3(c.x, c.y, 0f)));
                    if (wp.z <= 0f) continue;
                    point = wp;
                    via = "uitk-world";
                    return true;
                }
                var size = panelRoot.worldBound.size;
                if (size.x <= 0f || size.y <= 0f) continue;
                point = new Vector2(c.x / size.x * Screen.width, Screen.height - c.y / size.y * Screen.height);
                via = "uitk";
                return true;
            }
            return false;
        }

        void DoCapture(ScenarioCapture c, int index, float st)
        {
            var presetName = string.IsNullOrEmpty(c.preset) ? "auto" : c.preset;
            if (presetName == "screen")
            {
                var screenLabel = !string.IsNullOrEmpty(c.name) ? c.name : "screen";
                var screenPath = Path.Combine(m_OutDir, $"shot{index}_{Sanitize(screenLabel)}.png").Replace('\\', '/');
                if (c.frames > 1)
                {
                    m_Shots.Add(new ShotResult { name = screenLabel, preset = "screen", path = screenPath, t = st, error = "\"frames\" (a sequence) is not supported with preset \"screen\": use \"main\" or a shot name" });
                    return;
                }
                if (Application.isBatchMode)
                {
                    m_Shots.Add(new ShotResult { name = screenLabel, preset = "screen", path = screenPath, t = st, error = "preset \"screen\" needs a Game view: this Editor is headless (-batchmode, tools/open.ps1 -Headless or -Own). Use \"auto\", \"main\" or a shot name, or an Editor with a window" });
                    return;
                }
                StartCoroutine(CaptureScreen(screenLabel, screenPath, st, c.golden));
                return;
            }

            // Camera settings from the named camera or the main camera; the pose from the capture's pos, a named shot, or that camera.
            string error = null;
            var template = string.IsNullOrEmpty(c.camera) ? HarnessCapture.FindMainCamera() : HarnessCapture.FindCamera(c.camera);
            if (template == null)
                error = string.IsNullOrEmpty(c.camera) ? "no camera in scene" : $"camera '{c.camera}' not found (cameras: {string.Join(", ", HarnessCapture.CameraNames())})";
            ShotPose pose = null;
            if (c.pos != null && c.pos.Length > 0)
            {
                if (ShotPose.TryCreate(!string.IsNullOrEmpty(c.name) ? c.name : "pose", c.pos, c.lookAt, c.rot, c.fov, template != null ? template.transform : null, out pose, out var poseError)) pose.source = "scenario";
                else error = error ?? poseError;
            }
            else if (presetName == "auto" && string.IsNullOrEmpty(c.camera))
            {
                foreach (var p in ShotPose.All())
                    if (!m_UsedPresets.Contains(p.name)) { pose = p; break; }
            }
            else if (presetName != "main" && presetName != "auto")
            {
                pose = ShotPose.Find(presetName);
                if (pose == null) Debug.LogError($"[Harness] shot '{presetName}' not found (a ShotPreset in the scene or \"shots\" of {HarnessConfig.FileName}); using the main camera");
            }

            var label = !string.IsNullOrEmpty(c.name) ? c.name : pose != null ? pose.name : !string.IsNullOrEmpty(c.camera) ? c.camera : "main";
            var path = Path.Combine(m_OutDir, $"shot{index}_{Sanitize(label)}.png").Replace('\\', '/');
            var preset = pose == null ? (string.IsNullOrEmpty(c.camera) ? "main" : "camera") : pose.source == "scenario" ? "pose" : pose.name;
            if (error == null && pose != null && pose.source != "scenario") m_UsedPresets.Add(pose.name);
            if (error == null && c.frames > 1)
            {
                var seq = new Sequence { capture = c, label = label, path = path, preset = preset, template = template, pose = pose, count = c.frames, every = Mathf.Max(1, c.every) };
                m_Sequences.Add(seq);
                CaptureFrame(seq, st);
                return;
            }
            ShotResult r;
            if (error != null)
                r = new ShotResult { name = label, path = path, error = error };
            else
            {
                PoseOf(template, pose, out var position, out var rotation, out var fov);
                r = HarnessCapture.Capture(template, position, rotation, fov, m_Scenario.width, m_Scenario.height, path, c.ui);
                r.preset = preset;
            }
            r.name = label;
            r.t = st;
            r.golden = c.golden;
            r.ignore = c.ignore ?? Array.Empty<ShotRect>();
            HarnessCapture.AddHints(r, template, c.ui);
            m_Shots.Add(r);
            m_CapturedLastFrame = true;
            // What the screen shows in this frame, when the shot is what the game's camera sees (G3-8, Player runs).
            if (ScreenTwins && error == null && r.error == null && pose == null && string.IsNullOrEmpty(c.camera) && c.ui)
                StartCoroutine(CaptureScreen(label, Path.ChangeExtension(path, null) + ".screen.png", st, false, r));
        }

        /// <summary>A shot pose, or the camera's own pose (it follows the camera frame after frame).</summary>
        static void PoseOf(Camera template, ShotPose pose, out Vector3 position, out Quaternion rotation, out float fov)
        {
            if (pose != null)
            {
                position = pose.position;
                rotation = pose.rotation;
                fov = pose.fieldOfView > 0f ? pose.fieldOfView : template.fieldOfView;
                return;
            }
            var tr = template.transform;
            position = tr.position;
            rotation = tr.rotation;
            fov = template.fieldOfView;
        }

        // ---- sequences ("frames" > 1, G3-3) -------------------------------------------------------------------------

        sealed class Sequence
        {
            public ScenarioCapture capture;
            public string label, path, preset;
            public Camera template;
            public ShotPose pose;
            public int count, every, taken, nextFrame;
            public ContactSheet sheet;
            public Color32[] previous;
            public ShotResult result;
            public double luma, std;
            public float renderMs;
            public readonly List<float> times = new List<float>();
            public readonly List<float> motion = new List<float>();
        }

        readonly List<Sequence> m_Sequences = new List<Sequence>();

        /// <summary>One frame of a sequence into its contact sheet; the sheet is written after the last one.</summary>
        void CaptureFrame(Sequence s, float st)
        {
            m_CapturedLastFrame = true;
            if (s.template == null)
            {
                CompleteSequence(s, $"the camera was destroyed after {s.taken} of {s.count} frames");
                return;
            }
            PoseOf(s.template, s.pose, out var position, out var rotation, out var fov);
            var r = HarnessCapture.Render(s.template, position, rotation, fov, m_Scenario.width, m_Scenario.height, s.capture.ui, out var px);
            s.renderMs += r.renderMs;
            if (px == null)
            {
                if (s.result == null) s.result = r;
                CompleteSequence(s, r.error ?? "capture failed");
                return;
            }
            if (s.result == null)
            {
                s.result = r;
                s.sheet = new ContactSheet(s.count, r.width, r.height);
            }
            else
            {
                var a = s.result;
                a.blank |= r.blank;
                a.dark |= r.dark;
                a.magenta |= r.magenta;
                a.shadersCompiling |= r.shadersCompiling;
                a.magentaRatio = Mathf.Max(a.magentaRatio, r.magentaRatio);
                a.darkRatio = Mathf.Max(a.darkRatio, r.darkRatio);
                a.colorBuckets = Mathf.Min(a.colorBuckets, r.colorBuckets);
                a.ui = r.ui;
                a.cameras = r.cameras;
                a.drawn = r.drawn;
                a.uiError = a.uiError ?? r.uiError;
            }
            s.luma += r.meanLuma;
            s.std += r.stdLuma;
            if (s.previous != null) s.motion.Add((float)Math.Round(HarnessCapture.LumaDifference(s.previous, px), 3));
            s.previous = px;
            s.sheet.Add(s.taken, px, r.width, r.height, st.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
            s.times.Add(st);
            s.taken++;
            s.nextFrame = Time.frameCount + s.every;
            if (s.taken >= s.count) CompleteSequence(s, null);
        }

        void CompleteSequence(Sequence s, string error)
        {
            m_Sequences.Remove(s);
            var r = s.result ?? new ShotResult();
            r.name = s.label;
            r.preset = s.preset;
            r.path = s.path;
            r.t = s.times.Count > 0 ? s.times[0] : ScenarioTime;
            r.frames = s.taken;
            r.every = s.every;
            r.times = s.times.ToArray();
            r.motion = s.motion.ToArray();
            r.golden = s.capture.golden;
            r.ignore = s.capture.ignore ?? Array.Empty<ShotRect>();
            r.renderMs = s.renderMs;
            if (s.taken > 0)
            {
                r.meanLuma = (float)(s.luma / s.taken);
                r.stdLuma = (float)(s.std / s.taken);
                r.sheet = s.sheet.Layout;
                try { s.sheet.Save(s.path); }
                catch (Exception e) { error = error ?? e.GetType().Name + ": " + e.Message; }
            }
            if (error != null) r.error = error;
            if (s.template != null) HarnessCapture.AddHints(r, s.template, s.capture.ui);
            m_Shots.Add(r);
        }

        // "screen": what the Game view shows, at the Game view's size (the other presets lay the UI out at the capture size);
        // in a Player, the window. Needs a rendering Game view; if end-of-frame never arrives the shot is reported as an error.
        // twinOf: the screen of a Player run's capture (ScreenTwins), stored on that shot instead of as a shot of its own.
        int m_PendingScreen;
        double m_ScreenRequestedAt;
        sealed class PendingScreen
        {
            public string label, path;
            public float t;
            public ShotResult twinOf;
        }
        readonly List<PendingScreen> m_ScreenQueue = new List<PendingScreen>();

        System.Collections.IEnumerator CaptureScreen(string label, string path, float st, bool golden, ShotResult twinOf = null)
        {
            m_PendingScreen++;
            m_ScreenRequestedAt = m_Wall.Elapsed.TotalSeconds;
            var pending = new PendingScreen { label = label, path = path, t = st, twinOf = twinOf };
            m_ScreenQueue.Add(pending);
            yield return new WaitForEndOfFrame();
            m_ScreenQueue.Remove(pending);
            var r = new ShotResult { name = label, preset = "screen", path = path, t = st, golden = golden };
            var sw = Stopwatch.StartNew();
            Texture2D tex = null;
            try
            {
                tex = ScreenCapture.CaptureScreenshotAsTexture();
                r.width = tex.width;
                r.height = tex.height;
                if (twinOf == null)
                {
                    HarnessCapture.Analyze(tex.GetPixels32(), r);
                    HarnessCapture.AddHints(r, null, true);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            catch (Exception e) { r.error = e.GetType().Name + ": " + e.Message; }
            finally
            {
                if (tex != null) Destroy(tex);
                r.renderMs = (float)sw.Elapsed.TotalMilliseconds;
            }
            if (twinOf != null)
            {
                twinOf.screen = r.error == null ? path : null;
                twinOf.screenWidth = r.width;
                twinOf.screenHeight = r.height;
                twinOf.screenError = r.error;
            }
            else m_Shots.Add(r);
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
                    batches = m_Batches.Valid ? (float)(m_BatchesSum / m_RenderSamples) : -1f,
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
            // Time and input first, whatever fails below.
            Time.captureDeltaTime = 0f;
            m_Result.uiClock.panels = PanelClock.Panels;
            if (m_Result.uiClock.mode == "frames") m_Result.uiClock.scope = PanelClock.Scope;
            PanelClock.End();
            Application.runInBackground = m_RunInBackground;
            foreach (var input in m_Inputs)
            {
                try { input.Dispose(); } catch (Exception e) { Debug.LogException(e); }
            }
            m_Inputs.Clear();
            EndIsolation();
            // Sequences the scenario did not wait out (it failed or was stopped): their sheets with the frames taken.
            foreach (var s in m_Sequences.ToArray())
            {
                try { CompleteSequence(s, $"the scenario ended after {s.taken} of {s.count} frames"); }
                catch (Exception e) { Debug.LogException(e); }
            }

            m_Result.success = error == null;
            m_Result.error = error;
            m_Result.wallSec = (float)m_Wall.Elapsed.TotalSeconds;
            m_Result.gameSec = ScenarioTime;
            m_Result.frames = m_FrameMs.Count;
            m_Result.editorFocused = Application.isFocused;
#if !UNITY_EDITOR
            PlayerRun.Describe(m_Result.player);
#endif
            m_Result.modules = HarnessProbe.InitializedModules.ToArray();
            m_Result.failedModules = HarnessProbe.FailedModules.ToArray();
            m_Result.clicks = m_Clicks.ToArray();
            m_Result.scenes = m_Scenes.ToArray();
            m_Result.waits = m_Waits.ToArray();
            m_Result.activeScene = SceneManager.GetActiveScene().name;
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
            SceneManager.sceneLoaded -= OnSceneLoaded;
            m_Batches.Dispose(); m_SetPass.Dispose(); m_DrawCalls.Dispose();
            foreach (var r in m_DrawCallKinds) r.Dispose();
            m_Tris.Dispose(); m_Verts.Dispose(); m_MainThread.Dispose();
            if (Current == this) Current = null;
        }
    }

    /// <summary>Runs <see cref="ScenarioRunner"/>'s captures after every other script's LateUpdate of the frame.</summary>
    [DefaultExecutionOrder(32000)]   // the largest order the Script Execution Order settings allow
    sealed class ScenarioCaptureDriver : MonoBehaviour
    {
        internal ScenarioRunner runner;

        void LateUpdate()
        {
            if (runner != null) runner.LateCaptures();
        }
    }
}
