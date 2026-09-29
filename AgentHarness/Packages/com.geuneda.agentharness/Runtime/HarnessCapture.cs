using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
#if AGENTHARNESS_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Harness
{
    [Serializable]
    public sealed class ShotResult
    {
        public string name;
        public string preset;
        public string path;
        public float t;
        public int width;
        public int height;
        public float renderMs;
        // Mechanical sanity numbers so an agent can reject a blank/black frame without opening the PNG.
        public float meanLuma;   // 0..255
        public float stdLuma;    // 0 = flat image
        public float darkRatio;  // fraction of pixels with luma < 8
        public int colorBuckets; // distinct 4-bit/channel colors (4096 max)
        public bool blank;       // stdLuma < 2 or colorBuckets < 8
        public bool dark;        // darkRatio >= 0.98: almost all black (e.g. lighting missing). Suspicious, not a failure
        public float magentaRatio; // fraction of pixels in the error shader's magenta
        public bool magenta;     // magentaRatio >= 0.0005: a material the render pipeline cannot draw (see hint). Not a failure
        public string[] ui;      // screen-space UI composited over the camera render, bottom to top ("ugui:<canvas>", "uitk:<PanelSettings>")
        public string uiError;   // the UI could not be composited (the shot is the camera render, plus the layers before the error)
        public string error;
        public string hint;      // what explains a blank or magenta shot (other cameras on screen, the renderers drawn magenta)
    }

    /// <summary>
    /// Offscreen capture of a camera pose to PNG (edit mode and play mode), with the screen-space UI laid out at the
    /// capture size and composited over it (<see cref="CaptureUi"/>).
    /// </summary>
    public static class HarnessCapture
    {
        public const int DefaultWidth = 1280;
        public const int DefaultHeight = 720;
        public const float MagentaThreshold = 0.0005f;   // ~460 pixels of 1280x720

        /// <summary>
        /// Size for a capture that sets none (0): "captureSize" of the config, else 1280x720. The Editor also turns it for a
        /// portrait project before a scenario starts (HarnessPaths.CaptureSize).
        /// </summary>
        public static void DefaultSize(ref int width, ref int height)
        {
            if (width > 0 && height > 0) return;
            var c = HarnessConfig.Current.captureSize;
            if (c.Length == 2) { width = c[0]; height = c[1]; }
            else { width = DefaultWidth; height = DefaultHeight; }
        }

        public static Camera FindMainCamera()
        {
            var main = Camera.main;
            if (main != null) return main;
            foreach (var c in UnityCompat.FindObjects<Camera>(FindObjectsInactive.Exclude))
                if (c.cameraType == CameraType.Game) return c;
            return null;
        }

        /// <summary>
        /// Render <paramref name="template"/>'s settings from the given pose. The template camera itself is never moved.
        /// <paramref name="ui"/>: composite the screen-space UI over it (what the Game view would show at this size).
        /// </summary>
        public static ShotResult Capture(Camera template, Vector3 position, Quaternion rotation, float fov,
            int width, int height, string path, bool ui = true)
        {
            var result = new ShotResult { path = path, width = width, height = height, ui = Array.Empty<string>() };
            var sw = Stopwatch.StartNew();
            GameObject go = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            CaptureUi screenUi = null;
            try
            {
                go = new GameObject("[HarnessCaptureCamera]") { hideFlags = HideFlags.HideAndDontSave };
                var cam = go.AddComponent<Camera>();
                if (template != null) cam.CopyFrom(template);
                cam.enabled = false;
                go.transform.SetPositionAndRotation(position, rotation);
                cam.fieldOfView = fov;
                cam.aspect = (float)width / height;

#if AGENTHARNESS_URP
                if (template != null && template.TryGetComponent<UniversalAdditionalCameraData>(out var src))
                {
                    var dst = cam.GetUniversalAdditionalCameraData();
                    dst.renderPostProcessing = src.renderPostProcessing;
                    dst.antialiasing = src.antialiasing;
                    dst.antialiasingQuality = src.antialiasingQuality;
                    dst.renderShadows = src.renderShadows;
                    dst.volumeLayerMask = src.volumeLayerMask;
                    dst.dithering = src.dithering;
                    dst.stopNaN = src.stopNaN;
                    dst.requiresColorOption = src.requiresColorOption;
                    dst.requiresDepthOption = src.requiresDepthOption;
                }
#endif

                var desc = new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.D32_SFloat_S8_UInt)
                {
                    msaaSamples = 1,
                    sRGB = true,
                };
                rt = RenderTexture.GetTemporary(desc);

                if (ui)
                {
                    try
                    {
                        screenUi = CaptureUi.Collect(template);
                        screenUi.AttachSceneCanvases(cam, rt);
                    }
                    catch (Exception e)
                    {
                        result.uiError = e.GetType().Name + ": " + e.Message;
                        screenUi?.Dispose();
                        screenUi = null;
                    }
                }

                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    RenderPipeline.SubmitRenderRequest(cam, request);
                }
                else
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                }

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                // RGB: a camera cleared to a transparent color would otherwise give a see-through PNG (not what the game shows).
                tex = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                tex.Apply(false, false);
                RenderTexture.active = prev;

                var pixels = tex.GetPixels32();
                if (screenUi != null)
                {
                    try { result.ui = screenUi.Composite(pixels, width, height).ToArray(); }
                    catch (Exception e) { result.uiError = e.GetType().Name + ": " + (e.InnerException ?? e).Message; }
                    tex.SetPixels32(pixels);
                    tex.Apply(false, false);
                }
                Analyze(pixels, result);

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            catch (Exception e)
            {
                result.error = e.GetType().Name + ": " + e.Message;
                UnityEngine.Debug.LogException(e);
            }
            finally
            {
                try { screenUi?.Dispose(); }
                catch (Exception e) { UnityEngine.Debug.LogException(e); }
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                DestroySafe(tex);
                DestroySafe(go);
                result.renderMs = (float)sw.Elapsed.TotalMilliseconds;
            }
            return result;
        }

        public static ShotResult Capture(Camera template, ShotPreset preset, int width, int height, string path, bool ui = true)
        {
            var r = Capture(template, preset.transform.position, preset.transform.rotation, preset.fieldOfView, width, height, path, ui);
            r.preset = preset.presetName;
            return r;
        }

        public static ShotResult Capture(Camera template, ShotPose pose, int width, int height, string path, bool ui = true)
        {
            var fov = pose.fieldOfView > 0f ? pose.fieldOfView : template != null ? template.fieldOfView : 60f;
            var r = Capture(template, pose.position, pose.rotation, fov, width, height, path, ui);
            r.preset = pose.name;
            return r;
        }

        /// <summary>An enabled camera whose GameObject has this name or hierarchy path (any loaded scene), or null.</summary>
        public static Camera FindCamera(string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath)) return null;
            foreach (var c in UnityCompat.FindObjects<Camera>(FindObjectsInactive.Exclude))
                if (c.name == nameOrPath || HierarchyPath(c.transform) == nameOrPath.TrimStart('/')) return c;
            return null;
        }

        /// <summary>Names of the enabled cameras (for "not found" errors).</summary>
        public static List<string> CameraNames()
        {
            var names = new List<string>();
            foreach (var c in UnityCompat.FindObjects<Camera>(FindObjectsInactive.Exclude)) names.Add(HierarchyPath(c.transform));
            names.Sort(string.CompareOrdinal);
            return names;
        }

        public static string HierarchyPath(Transform t)
        {
            var path = t.name;
            for (var p = t.parent; p != null; p = p.parent) path = p.name + "/" + path;
            return path;
        }

        /// <summary>Fill the mechanical image stats of <paramref name="r"/> from raw pixels.</summary>
        public static void Analyze(Color32[] px, ShotResult r)
        {
            double sum = 0, sumSq = 0;
            long dark = 0, magenta = 0;
            var buckets = new HashSet<int>();
            for (var i = 0; i < px.Length; i++)
            {
                var c = px[i];
                var l = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                sum += l; sumSq += l * l;
                if (l < 8) dark++;
                if (IsErrorMagenta(c)) magenta++;
                if ((i & 3) == 0) buckets.Add(((c.r >> 4) << 8) | ((c.g >> 4) << 4) | (c.b >> 4));
            }
            var n = Math.Max(1, px.Length);
            var mean = sum / n;
            r.meanLuma = (float)mean;
            r.stdLuma = (float)Math.Sqrt(Math.Max(0, sumSq / n - mean * mean));
            r.darkRatio = (float)dark / n;
            r.colorBuckets = buckets.Count;
            r.blank = r.stdLuma < 2f || r.colorBuckets < 8;
            r.dark = r.darkRatio >= 0.98f;
            r.magentaRatio = (float)magenta / n;
            r.magenta = r.magentaRatio >= MagentaThreshold;
        }

        /// <summary>
        /// The error shader's color (1, 0, 1), as it comes out of post-processing: bright, red = blue within 15%, green under
        /// 10% of them. Measured on the sample: 253,0,238 after ACES + color adjustments; none of 3 clean shots (a pink-rimmed
        /// knot with bloom) has such a pixel.
        /// </summary>
        public static bool IsErrorMagenta(Color32 c)
        {
            int r = c.r, b = c.b;
            var hi = Math.Max(r, b);
            var lo = Math.Min(r, b);
            return lo >= 128 && c.g * 10 <= hi && (hi - lo) * 100 <= hi * 15;
        }

        /// <summary>Fill <see cref="ShotResult.hint"/> for a blank or magenta shot (<paramref name="template"/>: the camera it copied, or null).</summary>
        public static void AddHints(ShotResult r, Camera template, bool uiIncluded)
        {
            if (!string.IsNullOrEmpty(r.error)) return;
            var hints = new List<string>();
            if (r.blank)
            {
                var h = BlankHint(template, uiIncluded);
                if (h != null) hints.Add(h);
            }
            if (r.magenta) hints.Add(MagentaHint());
            if (hints.Count > 0) r.hint = string.Join("; ", hints);
        }

        /// <summary>
        /// For a blank camera shot: what the render left out - screen-space UI when the capture excluded it, and the other
        /// cameras that draw to the screen (a capture renders one camera).
        /// </summary>
        public static string BlankHint(Camera template, bool uiIncluded)
        {
            if (template == null) return null;
            var parts = new List<string>();
            if (!uiIncluded)
            {
                var overlay = 0;
                foreach (var c in UnityCompat.FindObjects<Canvas>(FindObjectsInactive.Exclude))
                    if (c.isRootCanvas && c.renderMode != RenderMode.WorldSpace) overlay++;
                var panels = UnityCompat.FindObjects<UnityEngine.UIElements.UIDocument>(FindObjectsInactive.Exclude).Length;
                if (overlay > 0 || panels > 0)
                    parts.Add($"the capture left out screen-space UI (\"ui\": false; {overlay} canvas(es), {panels} UI Toolkit document(s) here)");
            }
            var others = new List<string>();
            foreach (var c in UnityCompat.FindObjects<Camera>(FindObjectsInactive.Exclude))
            {
                if (c == template || !c.isActiveAndEnabled || c.cameraType != CameraType.Game || c.targetTexture != null || c.targetDisplay != 0) continue;
                if ((c.hideFlags & HideFlags.HideInHierarchy) != 0) continue;   // capture cameras
                others.Add(HierarchyPath(c.transform));
            }
            if (others.Count > 0)
            {
                others.Sort(string.CompareOrdinal);
                parts.Add($"only camera '{template.name}' is rendered, {others.Count} other camera(s) draw to the screen too ({string.Join(", ", others.GetRange(0, Math.Min(5, others.Count)))}): " +
                    "capture with \"camera\": \"<name>\", or preset \"screen\" for the Game view");
            }
            return parts.Count == 0 ? null : string.Join("; ", parts);
        }

#if AGENTHARNESS_URP
        static readonly ShaderTagId LightModeTag = new ShaderTagId("LightMode");
#endif

        /// <summary>For a magenta shot: the enabled renderers whose material the active render pipeline draws with its error shader.</summary>
        public static string MagentaHint()
        {
            var found = new List<string>();
            var total = 0;
            var reasons = new Dictionary<Shader, string>();
            foreach (var r in UnityCompat.FindObjects<Renderer>(FindObjectsInactive.Exclude))
            {
                if (!r.enabled) continue;
                foreach (var m in r.sharedMaterials)
                {
                    var why = CannotDraw(m, reasons);
                    if (why == null) continue;
                    if (found.Count < 5) found.Add($"{HierarchyPath(r.transform)} ({why})");
                    total++;
                    break;
                }
            }
            if (total == 0)
                return "magenta pixels, but no renderer has a material the render pipeline cannot draw: magenta art, or a broken shader in UI or procedural draws";
            found.Sort(string.CompareOrdinal);
            return $"drawn magenta - material the render pipeline cannot draw: {string.Join(", ", found)}{(total > found.Count ? $" (+{total - found.Count} more)" : "")}";
        }

        static string CannotDraw(Material m, Dictionary<Shader, string> reasons)
        {
            if (m == null) return "no material";
            var s = m.shader;
            if (s == null) return "no shader";
            if (reasons.TryGetValue(s, out var why)) return why;
            why = null;
            if (!s.isSupported || s.name == "Hidden/InternalErrorShader") why = $"shader '{s.name}' is missing, broken or not supported here";
            else if (DrawnAsError(s)) why = $"shader '{s.name}' is a Built-in render pipeline shader";
            reasons[s] = why;
            return why;
        }

        /// <summary>
        /// URP draws objects whose passes have only Built-in pipeline light modes (ForwardBase, Always, Vertex...) with its
        /// error material, e.g. a material made with Shader.Find("Standard") in a URP project.
        /// </summary>
        static bool DrawnAsError(Shader s)
        {
#if AGENTHARNESS_URP
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return false;
            var legacy = false;
            for (var i = 0; i < s.passCount; i++)
            {
                var mode = s.FindPassTagValue(i, LightModeTag).name ?? "";
                switch (mode.ToLowerInvariant())
                {
                    case "always": case "forwardbase": case "prepassbase": case "vertex": case "vertexlmrgbm": case "vertexlm":
                        legacy = true; break;
                    case "": case "srpdefaultunlit": case "universalforward": case "universalforwardonly": case "universalgbuffer":
                    case "universal2d": case "lightweightforward":
                        return false;   // a pass URP draws
                }
            }
            return legacy;
#else
            return false;
#endif
        }

        static void DestroySafe(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
