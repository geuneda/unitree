using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        public string error;
    }

    /// <summary>Offscreen capture of a camera pose to PNG (edit mode and play mode).</summary>
    public static class HarnessCapture
    {
        public const int DefaultWidth = 1280;
        public const int DefaultHeight = 720;

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
        /// </summary>
        public static ShotResult Capture(Camera template, Vector3 position, Quaternion rotation, float fov,
            int width, int height, string path)
        {
            var result = new ShotResult { path = path, width = width, height = height };
            var sw = Stopwatch.StartNew();
            GameObject go = null;
            RenderTexture rt = null;
            Texture2D tex = null;
            try
            {
                go = new GameObject("[HarnessCaptureCamera]") { hideFlags = HideFlags.HideAndDontSave };
                var cam = go.AddComponent<Camera>();
                if (template != null) cam.CopyFrom(template);
                cam.enabled = false;
                go.transform.SetPositionAndRotation(position, rotation);
                cam.fieldOfView = fov;
                cam.aspect = (float)width / height;

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

                var desc = new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.D32_SFloat_S8_UInt)
                {
                    msaaSamples = 1,
                    sRGB = true,
                };
                rt = RenderTexture.GetTemporary(desc);

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
                tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                tex.Apply(false, false);
                RenderTexture.active = prev;

                Analyze(tex.GetPixels32(), result);

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
                if (rt != null) RenderTexture.ReleaseTemporary(rt);
                DestroySafe(tex);
                DestroySafe(go);
                result.renderMs = (float)sw.Elapsed.TotalMilliseconds;
            }
            return result;
        }

        public static ShotResult Capture(Camera template, ShotPreset preset, int width, int height, string path)
        {
            var r = Capture(template, preset.transform.position, preset.transform.rotation, preset.fieldOfView, width, height, path);
            r.preset = preset.presetName;
            return r;
        }

        /// <summary>Fill the mechanical image stats of <paramref name="r"/> from raw pixels.</summary>
        public static void Analyze(Color32[] px, ShotResult r)
        {
            double sum = 0, sumSq = 0;
            long dark = 0;
            var buckets = new HashSet<int>();
            for (var i = 0; i < px.Length; i++)
            {
                var c = px[i];
                var l = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                sum += l; sumSq += l * l;
                if (l < 8) dark++;
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
        }

        static void DestroySafe(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
