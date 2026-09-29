using UnityEngine;

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
        public static bool IsWorldSpace(UnityEngine.UIElements.PanelSettings settings)
        {
#if UNITY_6000_2_OR_NEWER
            return settings != null && settings.renderMode == UnityEngine.UIElements.PanelRenderMode.WorldSpace;
#else
            return false;
#endif
        }
    }
}
