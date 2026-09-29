using System.Collections.Generic;
using System.IO;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace Harness.Editor
{
    public static class HarnessCaptureCommand
    {
        [CliCommand("harness_capture",
            "Render shot <preset> (a ShotPreset in the scene or \"shots\" of ProjectSettings/AgentHarness.json; 'all'; 'main' = the main camera) " +
            "of the play scene to <out>/<preset>.png at the project's capture size (config captureSize, else 720x1280 for a portrait project, else 1280x720) " +
            "(offscreen, main camera settings incl. post-processing, screen-space UI laid out at that size and composited unless ui=false). " +
            "Works in edit mode (fast static check) and play mode. " +
            "Returns {ok, shots:[{path, meanLuma, stdLuma, blank, dark, magenta, ui, ...}]}.",
            Tags = new[] { "harness", "capture" })]
        public static object Capture(
            [CliArg("preset", "Shot name, 'all', or 'main'.")] string preset = "all",
            [CliArg("out", "Output directory (relative to the project root).")] string @out = "HarnessOut/capture",
            [CliArg("width", "Width in pixels (0 = the project's capture size).")] int width = 0,
            [CliArg("height", "Height in pixels (0 = the project's capture size).")] int height = 0,
            [CliArg("scene", "Scene to open first in edit mode (default: playScene of ProjectSettings/AgentHarness.json; 'open' = the open scene).")] string scene = "",
            [CliArg("ui", "Composite the screen-space UI (overlay canvases, UI Toolkit panels) over the camera render.")] bool ui = true)
        {
            if (!EditorApplication.isPlaying && scene != "open")
            {
                var scenePath = HarnessPaths.ResolvePlayScene(scene, out var sceneError);
                if (scenePath == null) return new { ok = false, error = sceneError };
                if (!HarnessPaths.OpenScene(scenePath, out var openError)) return new { ok = false, error = openError };
            }

            HarnessPaths.CaptureSize(ref width, ref height);
            var cam = HarnessCapture.FindMainCamera();
            if (cam == null) return new { ok = false, error = "no camera in the open scene" };

            var outDir = HarnessPaths.Resolve(@out);
            Directory.CreateDirectory(outDir);
            var shots = new List<ShotResult>();
            var presets = ShotPose.All();
            if (preset == "main")
            {
                var t = cam.transform;
                var r = HarnessCapture.Capture(cam, t.position, t.rotation, cam.fieldOfView, width, height, HarnessPaths.Combine(outDir, "main.png"), ui);
                r.name = r.preset = "main";
                shots.Add(r);
            }
            else
            {
                foreach (var p in presets)
                {
                    if (preset != "all" && p.name != preset) continue;
                    var r = HarnessCapture.Capture(cam, p, width, height, HarnessPaths.Combine(outDir, p.name + ".png"), ui);
                    r.name = p.name;
                    shots.Add(r);
                }
                if (shots.Count == 0 && preset == "all")
                {
                    var t = cam.transform;
                    var r = HarnessCapture.Capture(cam, t.position, t.rotation, cam.fieldOfView, width, height, HarnessPaths.Combine(outDir, "main.png"), ui);
                    r.name = r.preset = "main";
                    shots.Add(r);
                }
                if (shots.Count == 0)
                {
                    var names = presets.ConvertAll(p => p.name);
                    return new { ok = false, error = $"shot '{preset}' not found (a ShotPreset in the scene or \"shots\" of {HarnessConfig.FileName})", available = names };
                }
            }
            foreach (var s in shots) HarnessCapture.AddHints(s, cam, ui);
            var ok = shots.TrueForAll(s => string.IsNullOrEmpty(s.error));
            return new { ok, shots };
        }
    }
}
