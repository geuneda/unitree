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
        public float durationSec = 3f;
        public float fixedDeltaTime = 1f / 60f;   // 0 = real time
        public float readyTimeoutSec = 10f;
        public float warmupSec = 0.25f;           // excluded from fps stats (shader warmup hitches)
        public int width = HarnessCapture.DefaultWidth;
        public int height = HarnessCapture.DefaultHeight;
        public ScenarioEvent[] events = Array.Empty<ScenarioEvent>();
        public ScenarioCapture[] captures = Array.Empty<ScenarioCapture>();
    }

    /// <summary>
    /// type: keyDown | keyUp | keyTap | mouseMove | mousePos | mouseDown | mouseUp | scroll |
    ///       stick | padDown | padUp | releaseAll
    ///   key    — Input System Key name for key* ("Space", "W", "LeftShift"); "left"/"right" for stick;
    ///            MouseButton for mouse* ("Left"); GamepadButton for pad* ("South", "Start")
    ///   x, y   — delta (mouseMove), position (mousePos), scroll, stick value
    ///   hold   — keyTap: seconds before the key is released (default 0.1)
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
    }

    /// <summary>preset: a ShotPreset name, "main" (the scene's main camera as-is) or "auto" (next unused preset by name).</summary>
    [Serializable]
    public sealed class ScenarioCapture
    {
        public float t;
        public string preset = "auto";
        public string name;
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
        public NamedCount[] events = Array.Empty<NamedCount>();
    }
}
