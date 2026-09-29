using System;

namespace Harness
{
    /// <summary>
    /// Play scenario (tools/scenarios/*.json). Times are seconds on the scenario clock, which starts
    /// when <see cref="HarnessProbe.Ready"/> flips. With fixedDeltaTime &gt; 0 game time advances a fixed
    /// step per frame (Time.captureDeltaTime), so t=1.5 shows the same state on every machine.
    /// </summary>
    [Serializable]
    public sealed class Scenario
    {
        public string name = "default";
        public string scene;                      // scene to play; empty = playScene of ProjectSettings/AgentHarness.json
        public float durationSec = 3f;
        public float fixedDeltaTime = 1f / 60f;   // 0 = real time
        public float readyTimeoutSec = 10f;
        public float warmupSec = 0.25f;           // excluded from fps stats (shader warmup hitches)
        public int width = HarnessCapture.DefaultWidth;
        public int height = HarnessCapture.DefaultHeight;
        /// <summary>
        /// Units of x, y in mousePos/click/mouseMove: "pixels" (Game view pixels, origin bottom left) or "normalized"
        /// (0..1 of the Game view, whose size is the user's Editor layout - use this for positions that must hit the same spot).
        /// </summary>
        public string mouseSpace = "pixels";
        public ScenarioEvent[] events = Array.Empty<ScenarioEvent>();
        public ScenarioCapture[] captures = Array.Empty<ScenarioCapture>();
    }

    /// <summary>
    /// type: keyDown | keyUp | keyTap | mouseMove | mousePos | mouseDown | mouseUp | click | scroll |
    ///       stick | padDown | padUp | releaseAll | waitScene | waitTarget
    ///   key    — key name for key* (Input System names "Space", "Digit1", "Enter", "LeftCtrl" or KeyCode names "Alpha1",
    ///            "Return", "LeftControl"); "left"/"right" for stick; mouse button for mouse*/click ("Left", "Right", "Middle");
    ///            GamepadButton for pad* ("South", "Start")
    ///   x, y   — delta (mouseMove), screen position in pixels, origin bottom left (mousePos, click), scroll, stick value
    ///   target — click: a GameObject (name or hierarchy path; uGUI element, or a renderer/collider seen by the main camera)
    ///            or a UI Toolkit element name, clicked at its center instead of x, y. waitTarget: the same, waited for
    ///   hold   — keyTap/click: seconds before the key/button is released (default 0.1)
    ///   scene  — waitScene: scene name or path. The scenario clock stops at t until that scene is loaded (waitTarget: until
    ///            the target exists, is active and visible), so later events and captures are relative to it (boot -> menu ->
    ///            level flows). The scenario fails after timeoutSec of waiting (default 30, wall clock)
    /// </summary>
    [Serializable]
    public sealed class ScenarioEvent
    {
        public float t;
        public string type;
        public string key;
        public float x;
        public float y;
        public float hold = 0.1f;
        public string target;
        public string scene;
        public float timeoutSec;
    }

    /// <summary>
    /// preset: a shot name (ShotPreset in the scene, or "shots" of ProjectSettings/AgentHarness.json), "main" (the main
    /// camera as-is), "auto" (next unused shot by name, else the main camera) or "screen" (the Game view with its UI).
    /// camera: render from this camera (GameObject name or path) instead of the main camera.
    /// pos (+ lookAt or rot as Euler angles, fov): render from this pose, with the settings of the main camera (or camera).
    /// </summary>
    [Serializable]
    public sealed class ScenarioCapture
    {
        public float t;
        public string preset = "auto";
        public string name;
        public string camera;
        public float[] pos;
        public float[] lookAt;
        public float[] rot;
        public float fov;
    }

    /// <summary>A scene that finished loading during the scenario (t = scenario clock, -1 before it started).</summary>
    [Serializable]
    public sealed class SceneLoad
    {
        public string name;
        public string path;
        public string mode;
        public float t;
        public float wallSec;
    }

    /// <summary>A waitScene/waitTarget that was satisfied: how long (wall clock) and how many frames the clock stood still.</summary>
    [Serializable]
    public sealed class ScenarioWait
    {
        public string type;
        public string target;   // the scene or the target
        public float t;
        public float waitedSec;
        public int frames;
    }

    /// <summary>Where a click with a target landed (screen pixels, origin bottom left).</summary>
    [Serializable]
    public sealed class ClickTarget
    {
        public string target;
        public float t;
        public float x;
        public float y;
        public string via;   // ugui | world | uitk
    }

    /// <summary>A real input device the scenario kept out, with the number of its key or button presses the game did not get (G3-6).</summary>
    [Serializable]
    public sealed class IsolatedDevice
    {
        public string name;
        public int presses;   // events with a key or button press (a mouse move or the state a device reports on focus is kept out too, not counted)
    }

    [Serializable]
    public sealed class FpsStats
    {
        public int samples;
        public float avg;       // frames per second (wall clock)
        public float min;       // worst frame, as fps
        public float p95ms;     // 95th percentile frame time
        public float p99ms;
        public float avgMs;
        public float maxMs;
        public int hitches;     // frames > 50 ms
        public float cpuMainAvgMs;  // ProfilerRecorder "Main Thread" (includes Editor overhead in play mode)
        public float cpuMainP95Ms;
    }

    [Serializable]
    public sealed class RenderStats
    {
        public int samples;
        public float batches;       // averages per frame
        public float setPassCalls;
        public float drawCalls;
        public float triangles;
        public float vertices;
    }

    [Serializable]
    public sealed class NamedCount
    {
        public string name;
        public int count;
    }

    [Serializable]
    public sealed class PlayResult
    {
        public string id;
        public string scenario;
        public bool success;
        public string error;
        public bool probeReady;
        public float readySec;          // play start → HarnessProbe.Ready (wall)
        public string[] modules = Array.Empty<string>();
        public string[] failedModules = Array.Empty<string>();
        public float wallSec;           // play start → finished
        public float gameSec;           // scenario clock at finish
        public int frames;
        public bool editorFocused;      // unfocused Editor ticks slower → fps is a lower bound
        public FpsStats fps = new FpsStats();
        public RenderStats render = new RenderStats();
        public ShotResult[] shots = Array.Empty<ShotResult>();
        public LogEntry[] runtimeErrors = Array.Empty<LogEntry>();
        public int errorCount;
        public int warningCount;
        public int inputEventsApplied;
        public string[] inputBackends = Array.Empty<string>();   // inputSystem (virtual devices) and/or hook (the game's [AgentHarnessInput] methods)
        public string[] inputHooks = Array.Empty<string>();      // those methods (Type.Method)
        public IsolatedDevice[] isolatedDevices = Array.Empty<IsolatedDevice>();   // real Input System devices off during the scenario
        public ClickTarget[] clicks = Array.Empty<ClickTarget>();
        public SceneLoad[] scenes = Array.Empty<SceneLoad>();
        public ScenarioWait[] waits = Array.Empty<ScenarioWait>();
        public string activeScene;
        public NamedCount[] events = Array.Empty<NamedCount>();
    }
}
