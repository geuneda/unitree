using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if AGENTHARNESS_URP
using UnityEngine.Rendering.Universal;
#endif

namespace Harness
{
    /// <summary>
    /// The cameras a capture renders into its texture (G3-7), bottom to top - what the Game view composes:
    /// <list type="bullet">
    /// <item>From the main camera ("screen" layout): every Base camera that draws to the screen, in depth order, with its
    /// viewport, clear and URP camera stack; the capture camera takes the template's place. A full-screen camera drawn
    /// later covers the ones before it, as in the game (URP does not layer Base cameras - that is what the stack is for).</item>
    /// <item>From another camera ("camera": name): that camera and its stack only, full frame.</item>
    /// </list>
    /// The template is moved to the capture pose for the render and put back, so what hangs off it comes along (an FPS
    /// weapon and the overlay camera that draws it). The other Base cameras render into the capture texture: their target
    /// is set for the capture (their Screen Space - Camera canvases are laid out at its size); with ui=false the layers of
    /// those canvases are left out of their culling masks. Canvases of stack overlay cameras are not drawn by a render
    /// request (Unity prepares UI only for the camera requested, the frame's own loop prepares every camera) - the capture
    /// composites them (<see cref="CaptureUi"/>). Everything changed is put back by <see cref="Restore"/>.
    /// </summary>
    sealed class CaptureCameras : IDisposable
    {
        struct Saved
        {
            public Camera camera;
            public RenderTexture target;
            public int cullingMask;
        }

        readonly List<Camera> m_Bases = new List<Camera>();      // render order; the capture camera in the template's place
        readonly List<Camera> m_Drawn = new List<Camera>();      // real cameras in draw order (the template for the capture camera, stacks after their base)
        readonly HashSet<Camera> m_Overlays = new HashSet<Camera>();
        readonly List<string> m_Names = new List<string>();
        readonly List<Saved> m_Saved = new List<Saved>();
        Camera m_Template, m_Capture;
        Transform m_Moved;
        Vector3 m_LocalPosition;
        Quaternion m_LocalRotation;
        bool m_Relayout;

        /// <summary>The real cameras whose drawing the capture includes, in draw order (the template stands for the capture camera).</summary>
        public IReadOnlyList<Camera> Drawn => m_Drawn;

        /// <summary>Stack overlay cameras among <see cref="Drawn"/> (the capture composites their canvases).</summary>
        public ICollection<Camera> Overlays => m_Overlays;

        /// <summary>For the report: the cameras drawn, bottom to top ("&lt;path&gt;", overlays "&lt;path&gt; (overlay)").</summary>
        public string[] Names => m_Names.ToArray();

        /// <summary>
        /// Plan the capture of <paramref name="capture"/> (a copy of <paramref name="template"/>, already at the pose) into
        /// <paramref name="target"/>: move the template to the pose, give the capture camera the template's stack, point the
        /// other cameras at the target. Call <see cref="Render"/>, then <see cref="Restore"/>.
        /// </summary>
        public static CaptureCameras Prepare(Camera template, Camera capture, RenderTexture target, bool ui)
        {
            var c = new CaptureCameras { m_Template = template, m_Capture = capture };
            var screen = template != null && template == HarnessCapture.FindMainCamera() && DrawsToScreen(template);
            if (screen)
            {
                var bases = new List<Camera>();
                foreach (var cam in UnityCompat.FindObjects<Camera>(FindObjectsInactive.Exclude))
                    if (DrawsToScreen(cam)) bases.Add(cam);
                // Depth order as the pipelines sort it; ties (unordered there) by path, to be the same every time.
                bases.Sort((a, b) => a.depth != b.depth ? a.depth.CompareTo(b.depth) : string.CompareOrdinal(HarnessCapture.HierarchyPath(a.transform), HarnessCapture.HierarchyPath(b.transform)));
                foreach (var cam in bases) c.Add(cam == template ? capture : cam, cam);
            }
            else
            {
                capture.rect = new Rect(0f, 0f, 1f, 1f);   // that camera's view, full frame
                if (template != null) c.Add(capture, template);
            }
            if (template != null) c.MoveTemplate(capture.transform.position, capture.transform.rotation);
            foreach (var cam in c.m_Drawn)
            {
                if (cam == template || c.m_Overlays.Contains(cam)) continue;   // URP draws a stack into its base's target
                var mask = cam.cullingMask;
                if (!ui) mask &= ~CanvasLayers(cam);
                c.m_Saved.Add(new Saved { camera = cam, target = cam.targetTexture, cullingMask = cam.cullingMask });
                cam.targetTexture = target;
                cam.cullingMask = mask;
                c.m_Relayout |= ui && CanvasLayers(cam) != 0;
            }
            return c;
        }

        /// <summary>A Base camera the Game view shows: enabled, a game camera, drawing to display 1 (not a texture), not a harness camera.</summary>
        public static bool DrawsToScreen(Camera c)
        {
            if (c == null || !c.isActiveAndEnabled || c.cameraType != CameraType.Game || c.targetTexture != null || c.targetDisplay != 0) return false;
            if ((c.hideFlags & HideFlags.HideInHierarchy) != 0) return false;   // capture cameras
            return !IsOverlay(c);
        }

        static bool IsOverlay(Camera c)
        {
#if AGENTHARNESS_URP
            return c.TryGetComponent<UniversalAdditionalCameraData>(out var d) && d.renderType == CameraRenderType.Overlay;
#else
            return false;
#endif
        }

        /// <summary>Layers of the Screen Space - Camera root canvases drawn by <paramref name="cam"/>.</summary>
        static int CanvasLayers(Camera cam)
        {
            var layers = 0;
            foreach (var canvas in UnityCompat.FindObjects<Canvas>(FindObjectsInactive.Exclude))
                if (canvas.isActiveAndEnabled && canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == cam)
                    layers |= 1 << canvas.gameObject.layer;
            return layers;
        }

        void Add(Camera render, Camera real)
        {
            m_Bases.Add(render);
            m_Drawn.Add(real);
            m_Names.Add(HarnessCapture.HierarchyPath(real.transform));
            foreach (var overlay in Stack(real))
            {
                m_Drawn.Add(overlay);
                m_Overlays.Add(overlay);
                m_Names.Add(HarnessCapture.HierarchyPath(overlay.transform) + " (overlay)");
                // The capture camera draws the template's stack.
                if (render != real) AddToStack(render, overlay);
            }
        }

        /// <summary>The overlay cameras URP draws on top of <paramref name="c"/> (its stack's enabled overlays).</summary>
        static List<Camera> Stack(Camera c)
        {
            var list = new List<Camera>();
#if AGENTHARNESS_URP
            if (c.TryGetComponent<UniversalAdditionalCameraData>(out var d) && d.renderType == CameraRenderType.Base)
                foreach (var o in d.cameraStack)
                    if (o != null && o.isActiveAndEnabled && IsOverlay(o) && !list.Contains(o)) list.Add(o);
#endif
            return list;
        }

        static void AddToStack(Camera baseCamera, Camera overlay)
        {
#if AGENTHARNESS_URP
            baseCamera.GetUniversalAdditionalCameraData().cameraStack.Add(overlay);
#endif
        }

        void MoveTemplate(Vector3 position, Quaternion rotation)
        {
            var t = m_Template.transform;
            if (t.position == position && t.rotation == rotation) return;
            m_Moved = t;
            m_LocalPosition = t.localPosition;
            m_LocalRotation = t.localRotation;
            t.SetPositionAndRotation(position, rotation);
        }

        /// <summary>Render the cameras bottom to top into <paramref name="target"/>.</summary>
        public void Render(RenderTexture target)
        {
            if (m_Bases.Count == 0) RenderInto(m_Capture, target);
            foreach (var cam in m_Bases) RenderInto(cam, target);
        }

        static void RenderInto(Camera cam, RenderTexture target)
        {
            var request = new RenderPipeline.StandardRequest { destination = target };
            if (RenderPipeline.SupportsRenderRequest(cam, request))
            {
                RenderPipeline.SubmitRenderRequest(cam, request);   // with the camera's stack (URP)
                return;
            }
            var prev = cam.targetTexture;
            cam.targetTexture = target;
            cam.Render();
            cam.targetTexture = prev;
        }

        /// <summary>Put the cameras and the template back (and the canvases they draw back to the Game view's layout). Idempotent.</summary>
        public void Restore()
        {
            if (m_Moved != null)
            {
                m_Moved.localPosition = m_LocalPosition;
                m_Moved.localRotation = m_LocalRotation;
                m_Moved = null;
            }
            for (var i = m_Saved.Count - 1; i >= 0; i--)
            {
                var s = m_Saved[i];
                if (s.camera == null) continue;
                s.camera.targetTexture = s.target;
                s.camera.cullingMask = s.cullingMask;
            }
            m_Saved.Clear();
            if (m_Relayout)
            {
                m_Relayout = false;
                Canvas.ForceUpdateCanvases();
            }
        }

        public void Dispose() => Restore();
    }
}
