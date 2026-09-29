using System.Collections.Generic;
using UnityEngine;

namespace Harness
{
    /// <summary>
    /// A named camera pose for captures. The pose is this transform; the camera settings
    /// (post-processing, HDR, clear flags…) come from the scene's main camera.
    /// Builders create these with <c>BuildContext.Shot(...)</c>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShotPreset : MonoBehaviour
    {
        public string presetName = "shot";
        [Range(5f, 150f)] public float fieldOfView = 60f;

        /// <summary>All presets in loaded scenes, sorted by name (stable order for "auto" captures).</summary>
        public static List<ShotPreset> All()
        {
            var list = new List<ShotPreset>(UnityCompat.FindObjects<ShotPreset>(FindObjectsInactive.Include));
            list.Sort((a, b) => string.CompareOrdinal(a.presetName, b.presetName));
            return list;
        }

        public static ShotPreset Find(string name)
        {
            foreach (var p in All()) if (p.presetName == name) return p;
            return null;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawFrustum(Vector3.zero, fieldOfView, 2f, 0.1f, 16f / 9f);
        }
    }
}
