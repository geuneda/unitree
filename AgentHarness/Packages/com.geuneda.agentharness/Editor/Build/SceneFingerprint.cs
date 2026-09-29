using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Harness.Editor
{
    /// <summary>
    /// Semantic hash of a scene plus the generated assets it references. Independent of Unity's random
    /// local fileIDs, so two builds from the same code produce the same fingerprint (idempotency check).
    /// </summary>
    public static class SceneFingerprint
    {
        public sealed class Result
        {
            public string hash;
            public int gameObjects;
            public int components;
            public int assets;
        }

        static readonly StringBuilder s_AssetDump = new StringBuilder();

        /// <param name="settings">Render settings (<see cref="SettingsContext.Fingerprint"/>) hashed with the scene, or null.</param>
        public static Result Compute(Scene scene, string settings = null)
        {
            s_AssetDump.Clear();
            var sb = new StringBuilder(1 << 16);
            var assets = new SortedSet<string>(StringComparer.Ordinal);
            var r = new Result();

            foreach (var root in scene.GetRootGameObjects())
                Walk(root.transform, sb, assets, r);
            DumpRenderSettings(scene, sb, assets);

            // Generated assets referenced by the scene (and assets they reference, one level at a time).
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>(assets);
            var assetSb = new StringBuilder();
            var ordered = new SortedDictionary<string, string>(StringComparer.Ordinal);
            while (queue.Count > 0)
            {
                var path = queue.Dequeue();
                if (!visited.Add(path)) continue;
                var local = new SortedSet<string>(StringComparer.Ordinal);
                ordered[path] = HashAsset(path, local);
                foreach (var p in local) if (!visited.Contains(p)) queue.Enqueue(p);
            }
            foreach (var kv in ordered) assetSb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
            r.assets = ordered.Count;

            var text = sb.ToString() + "\n--assets--\n" + assetSb;
            if (settings != null) text += "\n--settings--\n" + settings;
            r.hash = Sha1(text);
            // Kept for diffing when two builds disagree: Library/Harness/fingerprint.txt (+ per-asset dumps). The last
            // different dump is kept as fingerprint.prev.txt, so a changed fingerprint can be diffed after the fact.
            try
            {
                var file = HarnessPaths.Combine(HarnessPaths.StateDir, "fingerprint.txt");
                var dump = text + "\n--asset dumps--\n" + s_AssetDump;
                if (File.Exists(file) && File.ReadAllText(file) != dump)
                    File.Copy(file, HarnessPaths.Combine(HarnessPaths.StateDir, "fingerprint.prev.txt"), true);
                File.WriteAllText(file, dump);
            }
            catch { }
            s_AssetDump.Clear();
            return r;
        }

        /// <summary>
        /// Fingerprint of a scene the harness did not build (an attached project): the scene file and every asset it
        /// depends on, by Unity's import dependency hash (source bytes + import settings). The loaded scene is not walked:
        /// [ExecuteAlways] scripts change it in edit mode (e.g. Cinemachine drives the camera's field of view, UI Toolkit
        /// adds hidden renderers), so it differs from loop to loop although nothing changed.
        /// </summary>
        public static Result ComputeFromAssets(string scenePath)
        {
            var r = new Result();
            var deps = AssetDatabase.GetDependencies(scenePath, true);
            Array.Sort(deps, StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (var p in deps) sb.Append(p).Append('=').Append(AssetDatabase.GetAssetDependencyHash(p).ToString()).Append('\n');
            r.assets = deps.Length;
            r.hash = Sha1(sb.ToString());
            var scene = SceneManager.GetSceneByPath(scenePath);
            if (scene.IsValid() && scene.isLoaded)
                foreach (var root in scene.GetRootGameObjects()) r.gameObjects += root.GetComponentsInChildren<Transform>(true).Length;
            try
            {
                var file = HarnessPaths.Combine(HarnessPaths.StateDir, "fingerprint.txt");
                var dump = "scene assets of " + scenePath + "\n" + sb;
                if (File.Exists(file) && File.ReadAllText(file) != dump)
                    File.Copy(file, HarnessPaths.Combine(HarnessPaths.StateDir, "fingerprint.prev.txt"), true);
                File.WriteAllText(file, dump);
            }
            catch { }
            return r;
        }

        /// <summary>
        /// The scene's render settings (fog, ambient, sky, reflection) and lighting data, which live outside its GameObjects.
        /// GPU-baked lighting (the reflection cubemap, the ambient probe computed from it) is referenced, not hashed: it can
        /// differ in the last bits between GPUs, and the golden images compare how it looks.
        /// </summary>
        static void DumpRenderSettings(Scene scene, StringBuilder sb, SortedSet<string> assets)
        {
            if (SceneManager.GetActiveScene() != scene) return;   // RenderSettings are the active scene's
            void Line(string key, string value) => sb.Append("  ").Append(key).Append('=').Append(value).Append('\n');
            sb.Append("RenderSettings\n");
            Line("fog", RenderSettings.fog ? "1" : "0");
            Line("fogMode", RenderSettings.fogMode.ToString());
            Line("fogColor", RenderSettings.fogColor.ToString("R"));
            Line("fogDensity", RenderSettings.fogDensity.ToString("R"));
            Line("fogStartDistance", RenderSettings.fogStartDistance.ToString("R"));
            Line("fogEndDistance", RenderSettings.fogEndDistance.ToString("R"));
            Line("ambientMode", RenderSettings.ambientMode.ToString());
            Line("ambientSkyColor", RenderSettings.ambientSkyColor.ToString("R"));
            Line("ambientEquatorColor", RenderSettings.ambientEquatorColor.ToString("R"));
            Line("ambientGroundColor", RenderSettings.ambientGroundColor.ToString("R"));
            Line("ambientIntensity", RenderSettings.ambientIntensity.ToString("R"));
            Line("subtractiveShadowColor", RenderSettings.subtractiveShadowColor.ToString("R"));
            Line("skybox", RefId(RenderSettings.skybox, assets));
            Line("sun", RenderSettings.sun == null ? "null" : "go:" + PathOf(RenderSettings.sun.transform));
            Line("defaultReflectionMode", RenderSettings.defaultReflectionMode.ToString());
            Line("defaultReflectionResolution", RenderSettings.defaultReflectionResolution.ToString());
            Line("reflectionIntensity", RenderSettings.reflectionIntensity.ToString("R"));
            Line("reflectionBounces", RenderSettings.reflectionBounces.ToString());
            Line("customReflectionTexture", RefOnly(RenderSettings.customReflectionTexture));
            Line("lightingDataAsset", RefOnly(Lightmapping.GetLightingDataAssetForScene(scene)));
        }

        static string RefOnly(Object o) => o == null ? "null" : $"asset:{AssetDatabase.GetAssetPath(o)}:{o.GetType().Name}:{o.name}";

        static void Walk(Transform t, StringBuilder sb, SortedSet<string> assets, Result r)
        {
            var go = t.gameObject;
            r.gameObjects++;
            sb.Append("GO ").Append(PathOf(t)).Append('\n');
            Dump(go, sb, assets);
            var comps = go.GetComponents<Component>();
            foreach (var c in comps)
            {
                if (c == null) { sb.Append("  <missing script>\n"); continue; }
                r.components++;
                sb.Append("  C ").Append(c.GetType().FullName).Append('\n');
                Dump(c, sb, assets);
            }
            for (var i = 0; i < t.childCount; i++) Walk(t.GetChild(i), sb, assets, r);
        }

        static void Dump(Object o, StringBuilder sb, SortedSet<string> assets)
        {
            using (var so = new SerializedObject(o))
            {
                var it = so.GetIterator();
                var enter = true;
                while (it.Next(enter))
                {
                    // Only descend into containers: an ObjectReference's children are its (random) m_FileID/m_PathID.
                    enter = it.propertyType == SerializedPropertyType.Generic;
                    switch (it.propertyType)
                    {
                        case SerializedPropertyType.ObjectReference:
                            sb.Append("    ").Append(it.propertyPath).Append('=').Append(RefId(it.objectReferenceValue, assets)).Append('\n');
                            break;
                        case SerializedPropertyType.Generic:
                            break; // container; children follow
                        case SerializedPropertyType.ManagedReference:
                            sb.Append("    ").Append(it.propertyPath).Append("=managed:").Append(it.managedReferenceFullTypename).Append('\n');
                            break;
                        default:
                            var v = Value(it);
                            if (v != null) sb.Append("    ").Append(it.propertyPath).Append('=').Append(v).Append('\n');
                            break;
                    }
                }
            }
        }

        internal static string Value(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer: return p.longValue.ToString();
                case SerializedPropertyType.Boolean: return p.boolValue ? "1" : "0";
                case SerializedPropertyType.Float: return p.doubleValue.ToString("R");
                case SerializedPropertyType.String: return p.stringValue;
                case SerializedPropertyType.Color: return p.colorValue.ToString("R");
                case SerializedPropertyType.LayerMask: return p.intValue.ToString();
                case SerializedPropertyType.Enum: return p.intValue.ToString();
                case SerializedPropertyType.Vector2: return p.vector2Value.ToString("R");
                case SerializedPropertyType.Vector3: return p.vector3Value.ToString("R");
                case SerializedPropertyType.Vector4: return p.vector4Value.ToString("R");
                case SerializedPropertyType.Quaternion: return p.quaternionValue.ToString("R");
                case SerializedPropertyType.Rect: return p.rectValue.ToString("R");
                case SerializedPropertyType.Bounds: return p.boundsValue.ToString("R");
                case SerializedPropertyType.ArraySize: return p.intValue.ToString();
                case SerializedPropertyType.Character: return p.intValue.ToString();
                case SerializedPropertyType.AnimationCurve: return p.animationCurveValue != null ? p.animationCurveValue.length.ToString() : "0";
                case SerializedPropertyType.Hash128: return p.hash128Value.ToString();
                case SerializedPropertyType.Vector2Int: return p.vector2IntValue.ToString();
                case SerializedPropertyType.Vector3Int: return p.vector3IntValue.ToString();
                default: return null;
            }
        }

        static string RefId(Object o, SortedSet<string> assets)
        {
            if (o == null) return "null";
            if (EditorUtility.IsPersistent(o))
            {
                var path = AssetDatabase.GetAssetPath(o);
                if (path.StartsWith(HarnessPaths.GeneratedRoot + "/", StringComparison.Ordinal)) assets.Add(path);
                return $"asset:{path}:{o.GetType().Name}:{o.name}";
            }
            if (o is GameObject g) return "go:" + PathOf(g.transform);
            if (o is Component c)
            {
                var same = c.GetComponents(c.GetType());
                return $"comp:{PathOf(c.transform)}:{c.GetType().Name}#{Array.IndexOf(same, c)}";
            }
            return "obj:" + o.GetType().Name + ":" + o.name;
        }

        static string PathOf(Transform t)
        {
            var s = t.name + "#" + t.GetSiblingIndex();
            while (t.parent != null) { t = t.parent; s = t.name + "#" + t.GetSiblingIndex() + "/" + s; }
            return s;
        }

        static string HashAsset(string path, SortedSet<string> referenced)
        {
            var main = AssetDatabase.LoadMainAssetAtPath(path);
            if (main is Texture2D || main is Mesh)
            {
                // Bulk data: hash the bytes on disk instead of walking thousands of serialized elements.
                var full = HarnessPaths.Combine(HarnessPaths.ProjectRoot, path);
                if (main is Mesh mesh)
                {
                    var sbm = new StringBuilder();
                    sbm.Append(mesh.vertexCount).Append('|').Append(mesh.bounds.ToString("R")).Append('|');
                    foreach (var v in mesh.vertices) sbm.Append(v.x.ToString("R")).Append(',').Append(v.y.ToString("R")).Append(',').Append(v.z.ToString("R")).Append(';');
                    foreach (var i in mesh.triangles) sbm.Append(i).Append(',');
                    return Sha1(sbm.ToString());
                }
                return File.Exists(full) ? Sha1(File.ReadAllBytes(full)) + "|" + ImporterSummary(path) : "missing";
            }
            // One dump per object, sorted: LoadAllAssetsAtPath lists sub-objects in local fileID order, which depends on the
            // asset's history (a freshly created .mat lists URP's hidden AssetVersion before the Material, an older one after it).
            var dumps = new List<string>();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o == null) continue;
                var sb = new StringBuilder();
                sb.Append("O ").Append(o.GetType().FullName).Append(':').Append(o.name).Append('\n');
                Dump(o, sb, referenced);
                dumps.Add(sb.ToString());
            }
            dumps.Sort(StringComparer.Ordinal);
            var text = string.Concat(dumps);
            s_AssetDump.Append("== ").Append(path).Append('\n').Append(text);
            return Sha1(text);
        }

        static string ImporterSummary(string path)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            return imp == null ? "" : $"{imp.textureType}/{imp.sRGBTexture}/{imp.wrapMode}/{imp.filterMode}/{imp.mipmapEnabled}";
        }

        static string Sha1(string s) => Sha1(Encoding.UTF8.GetBytes(s));

        static string Sha1(byte[] bytes)
        {
            using (var sha = SHA1.Create())
            {
                var h = sha.ComputeHash(bytes);
                var sb = new StringBuilder(40);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
