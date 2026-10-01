using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Harness
{
    /// <summary>
    /// Unity API differences between the supported Editor versions (Unity 6.0 LTS and newer), kept in one place.
    /// Branch on the UNITY_x_y_OR_NEWER defines here, never on a version string.
    /// </summary>
    public static class UnityCompat
    {
        /// <summary>Loaded scene objects of type <typeparamref name="T"/>, in no particular order.</summary>
        public static T[] FindObjects<T>(FindObjectsInactive inactive) where T : Object
        {
#if UNITY_6000_4_OR_NEWER
            // 6.4 deprecates the FindObjectsSortMode overloads (InstanceID -> EntityId) and adds these.
            return Object.FindObjectsByType<T>(inactive);
#else
            return Object.FindObjectsByType<T>(inactive, FindObjectsSortMode.None);
#endif
        }

        /// <summary>A UI Toolkit panel placed in the world (6.2+; element bounds are then in the document's local units).</summary>
        public static bool IsWorldSpace(PanelSettings settings)
        {
#if UNITY_6000_2_OR_NEWER
            return settings != null && settings.renderMode == PanelRenderMode.WorldSpace;
#else
            return false;
#endif
        }

        /// <summary>A UI Toolkit document of a loaded scene: a UIDocument or a PanelRenderer.</summary>
        public readonly struct PanelDocument
        {
            public readonly Component component;
            public readonly bool active;           // active and enabled
            public readonly PanelSettings settings;
            public readonly VisualElement root;    // null until the document has built its tree

            public PanelDocument(Component component, bool active, PanelSettings settings, VisualElement root)
            {
                this.component = component;
                this.active = active;
                this.settings = settings;
                this.root = root;
            }
        }

        /// <summary>
        /// The UI Toolkit documents on active GameObjects: UIDocument components and, on 6.5+, PanelRenderer components (the
        /// Renderer that replaces UIDocument; its root is internal and read through IPanelComponent).
        /// </summary>
        public static List<PanelDocument> PanelDocuments()
        {
            var docs = new List<PanelDocument>();
            foreach (var d in FindObjects<UIDocument>(FindObjectsInactive.Exclude))
                docs.Add(new PanelDocument(d, d.isActiveAndEnabled, d.panelSettings, d.rootVisualElement));
#if UNITY_6000_5_OR_NEWER
            foreach (var r in FindObjects<PanelRenderer>(FindObjectsInactive.Exclude))
                docs.Add(new PanelDocument(r, r.enabled && r.gameObject.activeInHierarchy, r.panelSettings,
                    PanelRendererRoot?.Invoke(r, null) as VisualElement));
#endif
            return docs;
        }

#if UNITY_6000_5_OR_NEWER
        static readonly System.Reflection.MethodInfo PanelRendererRoot = typeof(IPanelComponent).GetMethod("GetRootVisualElement",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
#endif
    }
}
