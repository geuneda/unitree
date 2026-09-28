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
            "Render ShotPreset <preset> (or 'all', or 'main' for the main camera) of the open scene to <out>/<preset>.png at 1280x720 " +
            "(offscreen, main camera settings incl. post-processing). Works in edit mode (fast static check) and play mode. " +
            "Returns {ok, shots:[{path, meanLuma, stdLuma, blank, ...}]}.",
            Tags = new[] { "harness", "capture" })]
        public static object Capture(
            [CliArg("preset", "ShotPreset name, 'all', or 'main'.")] string preset = "all",
            [CliArg("out", "Output directory (relative to the project root).")] string @out = "HarnessOut/capture",
            [CliArg("width", "Width in pixels.")] int width = HarnessCapture.DefaultWidth,
            [CliArg("height", "Height in pixels.")] int height = HarnessCapture.DefaultHeight)
        {
            if (!EditorApplication.isPlaying && EditorSceneManager.GetActiveScene().path != HarnessPaths.MainScene && File.Exists(HarnessPaths.MainScene))
                EditorSceneManager.OpenScene(HarnessPaths.MainScene, OpenSceneMode.Single);

            var cam = HarnessCapture.FindMainCamera();
            if (cam == null) return new { ok = false, error = "no camera in the open scene" };

            var outDir = HarnessPaths.Resolve(@out);
            Directory.CreateDirectory(outDir);
            var shots = new List<ShotResult>();
            var presets = ShotPreset.All();
            if (preset == "main")
            {
                var t = cam.transform;
                var r = HarnessCapture.Capture(cam, t.position, t.rotation, cam.fieldOfView, width, height, HarnessPaths.Combine(outDir, "main.png"));
                r.name = r.preset = "main";
                shots.Add(r);
            }
            else
            {
                foreach (var p in presets)
                {
                    if (preset != "all" && p.presetName != preset) continue;
                    var r = HarnessCapture.Capture(cam, p, width, height, HarnessPaths.Combine(outDir, p.presetName + ".png"));
                    r.name = p.presetName;
                    shots.Add(r);
                }
                if (shots.Count == 0)
                {
                    var names = presets.ConvertAll(p => p.presetName);
                    return new { ok = false, error = $"ShotPreset '{preset}' not found", available = names };
                }
            }
            var ok = shots.TrueForAll(s => string.IsNullOrEmpty(s.error));
            return new { ok, shots };
        }
    }
}
