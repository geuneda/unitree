using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UIElements;
#if AGENTHARNESS_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Harness
{
    /// <summary>
    /// Screen-space UI in offscreen captures (G3-1). The Game view draws overlay canvases and UI Toolkit panels over the
    /// cameras, laid out for its own size (the user's Editor layout). A capture lays them out again at the capture size,
    /// draws each layer into a transparent texture - uGUI and UI Toolkit shaders leave premultiplied color and coverage
    /// alpha there - and composites the layers over the camera render in their sorting order, in the project's color space.
    /// Canvases in Screen Space - Camera of the template camera are drawn with the scene by the capture camera (with its
    /// post-processing, as in the game); those of the other Base cameras the capture renders (<see cref="CaptureCameras"/>)
    /// are drawn by them, laid out at the capture size by their target; those of stack overlay cameras (which a render
    /// request does not draw) and of on-screen cameras the capture does not render (a capture from another camera) go
    /// over the cameras, below the overlays.
    /// What it changes (a canvas's render mode, camera and plane distance, a PanelSettings' target texture and clear) is put
    /// back in the same frame, the layout included; UI code that reacts to a size change (OnRectTransformDimensionsChange,
    /// GeometryChangedEvent) sees the capture size and then the Game view size again. TextMesh Pro texts of an overlay canvas
    /// whose scale factor is not 1 are generated again for the camera render and again after it (<see cref="RegenerateText"/>).
    /// </summary>
    sealed class CaptureUi : IDisposable
    {
        sealed class Layer
        {
            public string name;
            public int band;            // 0 = Screen Space - Camera canvases of other cameras, 1 = overlays
            public float cameraDepth;   // band 0: the canvas's camera - its place among the cameras drawn, else after them by depth
            public int sortingLayer;    // canvases: SortingLayer value (separates camera renders)
            public float order;         // Canvas.sortingOrder / PanelSettings.sortingOrder
            public int kind;            // 0 canvas, 1 UI Toolkit panel (over a canvas of the same order)
            public Canvas canvas;
            public PanelSettings settings;
            public IPanel panel;
        }

        struct SavedCanvas
        {
            public Canvas canvas;
            public RenderMode mode;
            public Camera camera;
            public float planeDistance;
        }

        // Canvases are laid out on a plane this far in front of the UI camera, which sits far below any scene.
        const float PlaneDistance = 100f;
        static readonly Vector3 UiCameraPosition = new Vector3(0f, -100000f, 0f);

        readonly List<Layer> m_Layers = new List<Layer>();
        readonly List<Canvas> m_SceneCanvases = new List<Canvas>();
        readonly List<Canvas> m_CameraCanvases = new List<Canvas>();   // drawn by the other cameras the capture renders
        readonly List<Camera> m_Drawn = new List<Camera>();
        readonly List<SavedCanvas> m_Saved = new List<SavedCanvas>();
        Camera m_SceneCamera;
        GameObject m_UiCameraObject;
        Camera m_UiCamera;
        RenderTexture m_LayerRt;
        Texture2D m_LayerTex;
        int m_Width, m_Height;

        public bool Empty => m_Layers.Count == 0 && m_SceneCanvases.Count == 0 && m_CameraCanvases.Count == 0;

        /// <summary>
        /// The screen-space UI of the loaded scenes, relative to the camera a capture copies and the real cameras whose
        /// drawing it includes, in draw order (<paramref name="drawn"/>, the template standing for the capture camera).
        /// Nothing is changed yet.
        /// </summary>
        public static CaptureUi Collect(Camera template, IReadOnlyList<Camera> drawn = null, ICollection<Camera> overlays = null)
        {
            var ui = new CaptureUi();
            if (drawn != null) ui.m_Drawn.AddRange(drawn);
            foreach (var c in UnityCompat.FindObjects<Canvas>(FindObjectsInactive.Exclude))
            {
                if (!c.isActiveAndEnabled || !c.isRootCanvas || c.renderMode == RenderMode.WorldSpace) continue;
                var cam = c.renderMode == RenderMode.ScreenSpaceCamera ? c.worldCamera : null;
                if (cam != null && template != null && cam == template) { ui.m_SceneCanvases.Add(c); continue; }
                var stacked = cam != null && overlays != null && overlays.Contains(cam);
                if (cam != null && !stacked && ui.m_Drawn.Contains(cam)) { ui.m_CameraCanvases.Add(c); continue; }
                var layer = new Layer { name = "ugui:" + HarnessCapture.HierarchyPath(c.transform), canvas = c, order = c.sortingOrder,
                    sortingLayer = SortingLayer.GetLayerValueFromID(c.sortingLayerID) };
                if (stacked)
                {
                    // A canvas of a stack overlay camera the capture drew: over the cameras, in the stack's order.
                    layer.band = 0;
                    layer.cameraDepth = ui.m_Drawn.IndexOf(cam);
                }
                else if (cam != null)
                {
                    // Drawn by a camera the capture did not draw: over the scene when that camera draws to the screen, else not on screen at all.
                    if (!cam.isActiveAndEnabled || cam.targetTexture != null || cam.targetDisplay != 0) continue;
                    layer.band = 0;
                    layer.cameraDepth = 100000f + cam.depth;
                }
                else
                {
                    // Screen Space - Overlay (or Camera without a camera, which Unity draws as an overlay) on the Game view's display.
                    if (c.targetDisplay != 0) continue;
                    layer.band = 1;
                }
                ui.m_Layers.Add(layer);
            }
            var seen = new HashSet<PanelSettings>();
            foreach (var d in UnityCompat.PanelDocuments())
            {
                var ps = d.settings;
                if (!d.active || ps == null || !seen.Add(ps)) continue;
                // A panel with a target texture is the game's own render-to-texture UI (part of the scene, if shown at all).
                if (UnityCompat.IsWorldSpace(ps) || ps.targetTexture != null || ps.targetDisplay != 0) continue;
                var panel = d.root?.panel;
                if (panel == null) continue;
                ui.m_Layers.Add(new Layer { name = "uitk:" + ps.name, band = 1, order = ps.sortingOrder, kind = 1, settings = ps, panel = panel });
            }
            ui.m_Layers.Sort(Compare);
            return ui;
        }

        static int Compare(Layer a, Layer b)
        {
            if (a.band != b.band) return a.band.CompareTo(b.band);
            if (a.band == 0)
            {
                if (a.cameraDepth != b.cameraDepth) return a.cameraDepth.CompareTo(b.cameraDepth);
                if (a.sortingLayer != b.sortingLayer) return a.sortingLayer.CompareTo(b.sortingLayer);
            }
            if (a.order != b.order) return a.order.CompareTo(b.order);
            if (a.kind != b.kind) return a.kind.CompareTo(b.kind);
            return string.CompareOrdinal(a.name, b.name);
        }

        /// <summary>
        /// Before the scene render: Screen Space - Camera canvases of the template follow the capture camera, laid out for
        /// <paramref name="target"/> (the camera keeps it as its target; a URP render request sets and restores it anyway).
        /// </summary>
        public void AttachSceneCanvases(Camera capture, RenderTexture target)
        {
            if (m_SceneCanvases.Count == 0 && m_CameraCanvases.Count == 0) return;
            if (m_SceneCanvases.Count > 0)
            {
                m_SceneCamera = capture;
                capture.targetTexture = target;
                foreach (var c in m_SceneCanvases)
                {
                    Save(c);
                    c.worldCamera = capture;
                }
            }
            Canvas.ForceUpdateCanvases();   // also the canvases of the other cameras, whose target is the capture's now
        }

        /// <summary>The canvases drawn with the cameras, in the cameras' draw order (then sorting layer, order, path).</summary>
        List<string> DrawnCanvasNames()
        {
            var canvases = new List<Canvas>(m_SceneCanvases);
            canvases.AddRange(m_CameraCanvases);
            int CameraIndex(Canvas c)
            {
                var i = m_Drawn.IndexOf(c.worldCamera);
                return i < 0 ? int.MaxValue : i;
            }
            canvases.Sort((a, b) =>
            {
                int ia = CameraIndex(a), ib = CameraIndex(b);
                if (ia != ib) return ia.CompareTo(ib);
                int la = SortingLayer.GetLayerValueFromID(a.sortingLayerID), lb = SortingLayer.GetLayerValueFromID(b.sortingLayerID);
                if (la != lb) return la.CompareTo(lb);
                if (a.sortingOrder != b.sortingOrder) return a.sortingOrder.CompareTo(b.sortingOrder);
                return string.CompareOrdinal(HarnessCapture.HierarchyPath(a.transform), HarnessCapture.HierarchyPath(b.transform));
            });
            return canvases.ConvertAll(c => "ugui:" + HarnessCapture.HierarchyPath(c.transform));
        }

        /// <summary>
        /// Draw the layers bottom to top and composite each over <paramref name="pixels"/> (the camera render, 8-bit, as
        /// encoded in the render texture). Returns the names of the UI drawn: the canvases drawn with the scene, then the layers.
        /// </summary>
        public List<string> Composite(Color32[] pixels, int width, int height)
        {
            RestoreCanvases();   // the canvases of the cameras are drawn
            var names = DrawnCanvasNames();
            if (m_Layers.Count == 0) return names;
            m_Width = width;
            m_Height = height;
            var linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            for (var i = 0; i < m_Layers.Count;)
            {
                var first = m_Layers[i];
                var group = new List<Layer> { first };
                if (first.kind == 1) DrawPanel(first);
                else
                {
                    // Consecutive canvases a single camera render sorts the same way (same band, camera and sorting layer).
                    while (i + group.Count < m_Layers.Count)
                    {
                        var next = m_Layers[i + group.Count];
                        if (next.kind != 0 || next.band != first.band || next.cameraDepth != first.cameraDepth || next.sortingLayer != first.sortingLayer) break;
                        group.Add(next);
                    }
                    DrawCanvases(group);
                }
                i += group.Count;
                Over(pixels, ReadLayer(), linear);
                foreach (var l in group) names.Add(l.name);
            }
            return names;
        }

        RenderTexture LayerTarget()
        {
            if (m_LayerRt != null) return m_LayerRt;
            var desc = new RenderTextureDescriptor(m_Width, m_Height, GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.D32_SFloat_S8_UInt)
            {
                msaaSamples = 1,
                sRGB = true,
            };
            m_LayerRt = RenderTexture.GetTemporary(desc);
            return m_LayerRt;
        }

        Color32[] ReadLayer()
        {
            if (m_LayerTex == null) m_LayerTex = new Texture2D(m_Width, m_Height, TextureFormat.RGBA32, false, false);
            var prev = RenderTexture.active;
            RenderTexture.active = m_LayerRt;
            m_LayerTex.ReadPixels(new Rect(0, 0, m_Width, m_Height), 0, 0, false);
            m_LayerTex.Apply(false, false);
            RenderTexture.active = prev;
            return m_LayerTex.GetPixels32();
        }

        // ---- uGUI ---------------------------------------------------------------------------------------------------------

        void DrawCanvases(List<Layer> group)
        {
            var cam = UiCamera();
            var target = LayerTarget();
            var mask = 0;
            foreach (var l in group) mask |= 1 << l.canvas.gameObject.layer;
            cam.cullingMask = mask;
            var regenerated = new List<Canvas>();
            // The target first: CanvasScaler (Canvas.renderingDisplaySize) and the text meshes follow the camera's pixel size.
            cam.targetTexture = target;
            try
            {
                var scales = new float[group.Count];
                for (var i = 0; i < group.Count; i++)
                {
                    var l = group[i];
                    scales[i] = l.canvas.scaleFactor;
                    Save(l.canvas);
                    l.canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    l.canvas.worldCamera = cam;
                    l.canvas.planeDistance = PlaneDistance;
                }
                Canvas.ForceUpdateCanvases();
                for (var i = 0; i < group.Count; i++)
                {
                    var l = group[i];
                    if (l.band != 1 || (Mathf.Approximately(scales[i], 1f) && Mathf.Approximately(l.canvas.scaleFactor, 1f))) continue;
                    if (RegenerateText(l.canvas)) regenerated.Add(l.canvas);
                }
                RenderWithoutFeatures(cam);
            }
            finally
            {
                cam.targetTexture = null;
                RestoreCanvases();
                foreach (var c in regenerated) RegenerateText(c);   // the overlay's SDF scale again, for the game's own frame
            }
        }

        // TextMesh Pro packs each glyph's SDF scale (uv0.w) for the render mode of the canvas it generated the text in
        // (TextMeshProUGUI.GenerateTextMesh: Screen Space - Overlay lossyScale / scaleFactor, Screen Space - Camera lossyScale)
        // and afterwards only follows lossy scale changes of more than 20%. An overlay canvas drawn in Screen Space - Camera by
        // the UI camera (one unit = one pixel) keeps its lossy scale, so its text kept the overlay's SDF scale: edges
        // 1 / scaleFactor times as sharp as on the screen (W16, company project A: reference 1440x3040 at 720x1280, scale factor
        // 0.46 - a "0%" label 96 pixels different from the Player's own screen). TextMesh Pro is optional: found by name.
        static Type s_TmpUgui;
        static MethodInfo s_TmpForceMeshUpdate;
        static bool s_TmpLooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            s_TmpUgui = null;
            s_TmpForceMeshUpdate = null;
            s_TmpLooked = false;
        }

        /// <summary>Generate the TextMesh Pro texts of a canvas again for its current render mode (ForceMeshUpdate). False = none.</summary>
        static bool RegenerateText(Canvas canvas)
        {
            if (!s_TmpLooked)
            {
                s_TmpLooked = true;
                s_TmpUgui = Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
                if (s_TmpUgui != null)
                    foreach (var m in s_TmpUgui.GetMethods(BindingFlags.Instance | BindingFlags.Public))
                        if (m.Name == "ForceMeshUpdate" && Array.TrueForAll(m.GetParameters(), p => p.ParameterType == typeof(bool))
                            && (s_TmpForceMeshUpdate == null || m.GetParameters().Length > s_TmpForceMeshUpdate.GetParameters().Length))
                            s_TmpForceMeshUpdate = m;
            }
            if (s_TmpForceMeshUpdate == null || canvas == null) return false;
            var texts = canvas.GetComponentsInChildren(s_TmpUgui, false);
            if (texts.Length == 0) return false;
            var args = new object[s_TmpForceMeshUpdate.GetParameters().Length];   // ignoreActiveState, forceTextReparsing: false
            for (var i = 0; i < args.Length; i++) args[i] = false;
            foreach (var t in texts) s_TmpForceMeshUpdate.Invoke(t, args);
            return true;
        }

        /// <summary>
        /// Render the UI camera with its renderer's features off for that render: the game draws overlay canvases after the
        /// cameras, without them (a full-screen feature would paint over the UI layer; SSAO and decals have nothing to draw).
        /// URP 17.3's screen space decal pass also throws (NullReferenceException in RenderingUtils.SetScaleBiasRt) for a camera
        /// that renders straight into its target without an intermediate texture, as this one does (W13).
        /// </summary>
        static void RenderWithoutFeatures(Camera cam)
        {
#if AGENTHARNESS_URP
            var off = new List<ScriptableRendererFeature>();
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset asset)
            {
                var renderer = cam.GetUniversalAdditionalCameraData().scriptableRenderer;
                var renderers = asset.rendererDataList;
                for (var i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] == null || asset.GetRenderer(i) != renderer) continue;
                    foreach (var f in renderers[i].rendererFeatures)
                        if (f != null && f.isActive) { f.SetActive(false); off.Add(f); }
                    break;
                }
            }
            try { cam.Render(); }
            finally { foreach (var f in off) f.SetActive(true); }
#else
            cam.Render();
#endif
        }

        Camera UiCamera()
        {
            if (m_UiCamera != null) return m_UiCamera;
            m_UiCameraObject = new GameObject("[HarnessCaptureUiCamera]") { hideFlags = HideFlags.HideAndDontSave };
            m_UiCameraObject.transform.SetPositionAndRotation(UiCameraPosition, Quaternion.identity);
            var cam = m_UiCameraObject.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = m_Height * 0.5f;
            cam.aspect = (float)m_Width / m_Height;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = PlaneDistance * 20f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.clear;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.useOcclusionCulling = false;
#if AGENTHARNESS_URP
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;   // overlay UI is not post-processed
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            data.requiresColorOption = CameraOverrideOption.Off;
            data.requiresDepthOption = CameraOverrideOption.Off;
#endif
            m_UiCamera = cam;
            return cam;
        }

        void Save(Canvas c) => m_Saved.Add(new SavedCanvas { canvas = c, mode = c.renderMode, camera = c.worldCamera, planeDistance = c.planeDistance });

        void RestoreCanvases()
        {
            if (m_SceneCamera != null)
            {
                m_SceneCamera.targetTexture = null;
                m_SceneCamera = null;
            }
            if (m_Saved.Count == 0) return;
            for (var i = m_Saved.Count - 1; i >= 0; i--)
            {
                var s = m_Saved[i];
                if (s.canvas == null) continue;
                s.canvas.renderMode = s.mode;
                s.canvas.worldCamera = s.camera;
                s.canvas.planeDistance = s.planeDistance;
            }
            m_Saved.Clear();
            Canvas.ForceUpdateCanvases();   // back to the Game view's layout
        }

        // ---- UI Toolkit ---------------------------------------------------------------------------------------------------

        void DrawPanel(Layer l)
        {
            var ps = l.settings;
            var target = LayerTarget();
            PanelApi.Require();
            var prevTarget = ps.targetTexture;
            var prevClear = ps.clearColor;
            var prevClearValue = ps.colorClearValue;
            var prevClearDepth = ps.clearDepthStencil;
            try
            {
                ps.targetTexture = target;
                ps.clearColor = true;
                ps.colorClearValue = Color.clear;
                ps.clearDepthStencil = true;
                PanelApi.Update(l.panel);   // panel size, scale and layout from the target texture
                PanelApi.Draw(l.panel);
            }
            finally
            {
                ps.targetTexture = prevTarget;
                ps.clearColor = prevClear;
                ps.colorClearValue = prevClearValue;
                ps.clearDepthStencil = prevClearDepth;
                try { PanelApi.Update(l.panel); } catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>
        /// The internal UI Toolkit calls that update and draw one runtime panel now (Unity 6.0-6.6: RuntimePanel.Update,
        /// UIElementsRuntimeUtility.RepaintPanel/RenderPanel). Checked by selftest 1 (the HUD in every shot) per Unity version.
        /// </summary>
        static class PanelApi
        {
            const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            static readonly Type Utility = typeof(PanelSettings).Assembly.GetType("UnityEngine.UIElements.UIElementsRuntimeUtility");
            static readonly MethodInfo Repaint = Utility?.GetMethod("RepaintPanel", Any);
            static readonly MethodInfo Render = Utility?.GetMethod("RenderPanel", Any);

            public static void Require()
            {
                if (Repaint == null || Render == null)
                    throw new NotSupportedException($"UI Toolkit panels are not captured in Unity {Application.unityVersion}: UIElementsRuntimeUtility.RepaintPanel/RenderPanel not found");
            }

            public static void Update(IPanel panel)
            {
                var m = panel.GetType().GetMethod("Update", Any, null, Type.EmptyTypes, null);
                if (m == null) throw new NotSupportedException($"UI Toolkit panels are not captured in Unity {Application.unityVersion}: {panel.GetType().Name}.Update() not found");
                m.Invoke(panel, null);
            }

            public static void Draw(IPanel panel)
            {
                Repaint.Invoke(null, new object[] { panel });
                var args = Render.GetParameters().Length == 2 ? new object[] { panel, true } : new object[] { panel };
                Render.Invoke(null, args);
            }
        }

        // ---- compositing ------------------------------------------------------------------------------------------------

        static readonly float[] SrgbToLinear = BuildDecode();
        static readonly byte[] LinearToSrgb = BuildEncode();
        const int EncodeSteps = 65535;

        static float[] BuildDecode()
        {
            var t = new float[256];
            for (var i = 0; i < 256; i++) t[i] = Mathf.GammaToLinearSpace(i / 255f);
            return t;
        }

        static byte[] BuildEncode()
        {
            var t = new byte[EncodeSteps + 1];
            for (var i = 0; i <= EncodeSteps; i++) t[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.LinearToGammaSpace((float)i / EncodeSteps) * 255f), 0, 255);
            return t;
        }

        static byte Encode(float linear) => LinearToSrgb[(int)(Mathf.Clamp01(linear) * EncodeSteps + 0.5f)];

        /// <summary>
        /// dst = src + dst * (1 - src.a) with src premultiplied - the blend the GPU does for UI, in linear space when the
        /// project renders in linear (the 8-bit values are sRGB-encoded) and on the stored values in gamma space.
        /// </summary>
        static void Over(Color32[] dst, Color32[] src, bool linear)
        {
            for (var i = 0; i < dst.Length; i++)
            {
                var s = src[i];
                if (s.a == 0 && s.r == 0 && s.g == 0 && s.b == 0) continue;
                if (s.a == 255) { dst[i] = new Color32(s.r, s.g, s.b, 255); continue; }
                var d = dst[i];
                var k = 1f - s.a / 255f;
                if (linear)
                    dst[i] = new Color32(Encode(SrgbToLinear[s.r] + SrgbToLinear[d.r] * k), Encode(SrgbToLinear[s.g] + SrgbToLinear[d.g] * k),
                        Encode(SrgbToLinear[s.b] + SrgbToLinear[d.b] * k), 255);
                else
                    dst[i] = new Color32((byte)Mathf.Min(255, s.r + (int)(d.r * k + 0.5f)), (byte)Mathf.Min(255, s.g + (int)(d.g * k + 0.5f)),
                        (byte)Mathf.Min(255, s.b + (int)(d.b * k + 0.5f)), 255);
            }
        }

        public void Dispose()
        {
            try { RestoreCanvases(); }
            finally
            {
                if (m_LayerRt != null) RenderTexture.ReleaseTemporary(m_LayerRt);
                m_LayerRt = null;
                Destroy(m_LayerTex);
                Destroy(m_UiCameraObject);
                m_LayerTex = null;
                m_UiCameraObject = null;
                m_UiCamera = null;
            }
        }

        static void Destroy(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }
    }
}
