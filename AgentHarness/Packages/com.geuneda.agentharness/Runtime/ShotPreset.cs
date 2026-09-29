using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    /// <summary>
    /// A named camera pose to capture from: a <see cref="ShotPreset"/> in a loaded scene, or an entry of "shots" in
    /// ProjectSettings/AgentHarness.json - the way to name shots in an existing project without adding objects to its scenes.
    /// </summary>
    public sealed class ShotPose
    {
        public string name;
        public Vector3 position;
        public Quaternion rotation;
        public float fieldOfView;   // 0 = the template camera's
        public string source;       // scene | config | scenario

        /// <summary>Scene presets, then config shots whose scene is loaded (or that name none); each sorted by name.</summary>
        public static List<ShotPose> All()
        {
            var list = new List<ShotPose>();
            foreach (var p in ShotPreset.All())
                list.Add(new ShotPose { name = p.presetName, position = p.transform.position, rotation = p.transform.rotation, fieldOfView = p.fieldOfView, source = "scene" });
            var config = new List<ShotPose>();
            foreach (var s in HarnessConfig.Current.shots)
            {
                if (!string.IsNullOrEmpty(s.scene) && !SceneLoaded(s.scene)) continue;
                if (TryCreate(s.name, s.pos, s.lookAt, s.rot, s.fov, null, out var pose, out _)) { pose.source = "config"; config.Add(pose); }
            }
            config.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            list.AddRange(config);
            return list;
        }

        public static ShotPose Find(string name)
        {
            foreach (var p in All()) if (p.name == name) return p;
            return null;
        }

        /// <summary>
        /// A pose from JSON arrays: pos (x, y, z) and either lookAt (a point) or rot (Euler angles); neither = the rotation of
        /// <paramref name="fallback"/> (identity without one).
        /// </summary>
        public static bool TryCreate(string name, float[] pos, float[] lookAt, float[] rot, float fov, Transform fallback, out ShotPose pose, out string error)
        {
            pose = null; error = null;
            if (pos == null || pos.Length != 3) { error = $"shot '{name}': pos needs 3 numbers"; return false; }
            var p = new Vector3(pos[0], pos[1], pos[2]);
            Quaternion r;
            if (lookAt != null && lookAt.Length == 3)
            {
                var dir = new Vector3(lookAt[0], lookAt[1], lookAt[2]) - p;
                if (dir.sqrMagnitude < 1e-8f) { error = $"shot '{name}': lookAt equals pos"; return false; }
                r = Quaternion.LookRotation(dir, Mathf.Abs(Vector3.Dot(dir.normalized, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up);
            }
            else if (rot != null && rot.Length == 3) r = Quaternion.Euler(rot[0], rot[1], rot[2]);
            else if ((lookAt != null && lookAt.Length > 0) || (rot != null && rot.Length > 0)) { error = $"shot '{name}': lookAt/rot need 3 numbers"; return false; }
            else r = fallback != null ? fallback.rotation : Quaternion.identity;
            pose = new ShotPose { name = name, position = p, rotation = r, fieldOfView = fov > 0f ? fov : 0f };
            return true;
        }

        /// <summary>A loaded scene with this name or path.</summary>
        public static bool SceneLoaded(string nameOrPath)
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isLoaded && SceneMatches(s, nameOrPath)) return true;
            }
            return false;
        }

        public static bool SceneMatches(Scene s, string nameOrPath)
        {
            if (string.IsNullOrEmpty(nameOrPath)) return false;
            var want = nameOrPath.Replace('\\', '/');
            return s.name == want || s.path == want || s.path == want + ".unity";
        }
    }
}
