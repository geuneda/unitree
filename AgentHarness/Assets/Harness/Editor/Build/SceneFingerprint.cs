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

        public static Result Compute(Scene scene)
        {
            s_AssetDump.Clear();
            var sb = new StringBuilder(1 << 16);
            var assets = new SortedSet<string>(StringComparer.Ordinal);
            var r = new Result();

            foreach (var root in scene.GetRootGameObjects())
                Walk(root.transform, sb, assets, r);

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
            r.hash = Sha1(text);
            // Kept for diffing when two builds disagree: Library/Harness/fingerprint.txt (+ per-asset dumps).
            try { File.WriteAllText(HarnessPaths.Combine(HarnessPaths.StateDir, "fingerprint.txt"), text + "\n--asset dumps--\n" + s_AssetDump); } catch { }
            s_AssetDump.Clear();
            return r;
        }

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

        static string Value(SerializedProperty p)
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
            var sb = new StringBuilder();
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            {
                if (o == null) continue;
                sb.Append("O ").Append(o.GetType().FullName).Append(':').Append(o.name).Append('\n');
                Dump(o, sb, referenced);
            }
            s_AssetDump.Append("== ").Append(path).Append('\n').Append(sb);
            return Sha1(sb.ToString());
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
