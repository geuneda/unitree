using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
#if AGENTHARNESS_URP
using UnityEngine.Rendering.Universal;
#endif
using Object = UnityEngine.Object;

namespace Harness.Editor
{
    /// <summary>
    /// What an <see cref="ISettingsStep"/> uses to write project settings as code. Render pipeline assets go to
    /// &lt;generatedRoot&gt;/&lt;Module&gt;/ with a GUID derived from that path; each is built from a fresh instance and written only
    /// when its serialized content differs from the file (in place, so the GUID and the ProjectSettings that reference it stay).
    /// Settings in ProjectSettings/ (quality levels, player, time, physics, layers, tags): SettingsContext.Project.cs.
    /// </summary>
    public sealed partial class SettingsContext
    {
        /// <summary>Module of the step currently running.</summary>
        public string Module { get; internal set; }

        internal readonly List<string> Assets = new List<string>();     // every settings asset a step produced, in order
        internal readonly List<string> Written = new List<string>();    // created or rewritten by this run
        internal readonly List<string> Assigned = new List<string>();   // pipeline assignments this run changed
        internal readonly List<string> Warnings = new List<string>();

        public void Warn(string message) => Warnings.Add($"[{Module}] {message}");

        /// <summary>&lt;generatedRoot&gt;/&lt;Module&gt;/&lt;relative&gt;.</summary>
        public string AssetPath(string relative) => $"{HarnessPaths.GeneratedRoot}/{Module}/{relative}".Replace('\\', '/');

        // ---- Generic -------------------------------------------------------------------------------

        /// <summary>
        /// A settings asset of type <typeparamref name="T"/> at &lt;generatedRoot&gt;/&lt;Module&gt;/&lt;relative&gt; (".asset" if no extension):
        /// <paramref name="configure"/> sets up a fresh instance; objects it references that are not assets yet (e.g. renderer
        /// features) become sub-assets. Returns the asset on disk.
        /// </summary>
        public T Asset<T>(string relative, Action<T> configure = null) where T : ScriptableObject
        {
            var fresh = ScriptableObject.CreateInstance<T>();
            configure?.Invoke(fresh);
            return Save(fresh, relative);
        }

        /// <summary>
        /// Set a serialized field by its property path (as in the asset's YAML, e.g. "m_SoftShadowsSupported" or
        /// "m_Settings.Intensity") — for settings whose C# setter is internal. Throws when this Unity version has no such
        /// field or the value does not fit, so a renamed field fails the build instead of being ignored.
        /// </summary>
        public static void Set(Object target, string propertyPath, object value)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            using (var so = new SerializedObject(target))
            {
                Assign(Find(so, target, propertyPath), value, target, propertyPath);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static SerializedProperty Find(SerializedObject so, Object target, string propertyPath) =>
            so.FindProperty(propertyPath) ?? throw new ArgumentException($"{target.GetType().Name} has no serialized field '{propertyPath}' in Unity {Application.unityVersion}{Similar(so, propertyPath)}");

        /// <summary>Put <paramref name="value"/> into <paramref name="p"/> (not applied yet); throws when it does not fit.</summary>
        static void Assign(SerializedProperty p, object value, Object target, string propertyPath)
        {
            try
            {
                if (p.isArray && p.propertyType == SerializedPropertyType.Generic && p.arrayElementType == "string" && value is IEnumerable<string> items)
                {
                    var list = new List<string>(items);
                    p.arraySize = list.Count;
                    for (var i = 0; i < list.Count; i++) p.GetArrayElementAtIndex(i).stringValue = list[i];
                    return;
                }
                switch (p.propertyType)
                {
                    case SerializedPropertyType.Boolean: p.boolValue = Convert.ToBoolean(value); break;
                    case SerializedPropertyType.Integer: p.longValue = Convert.ToInt64(value); break;
                    case SerializedPropertyType.Enum: p.intValue = Convert.ToInt32(value); break;
                    case SerializedPropertyType.LayerMask: p.intValue = value is LayerMask m ? m.value : Convert.ToInt32(value); break;
                    case SerializedPropertyType.Float: p.doubleValue = Convert.ToDouble(value); break;
                    case SerializedPropertyType.String: p.stringValue = (string)value; break;
                    case SerializedPropertyType.Color: p.colorValue = (Color)value; break;
                    case SerializedPropertyType.Vector2: p.vector2Value = (Vector2)value; break;
                    case SerializedPropertyType.Vector3: p.vector3Value = (Vector3)value; break;
                    case SerializedPropertyType.Vector4: p.vector4Value = (Vector4)value; break;
                    case SerializedPropertyType.ObjectReference: p.objectReferenceValue = (Object)value; break;
                    default: throw new ArgumentException($"{target.GetType().Name}.{propertyPath} is a {p.propertyType}; Set supports numbers, bools, enums, strings, string arrays, colors, vectors and object references");
                }
            }
            catch (Exception e) when (e is InvalidCastException || e is FormatException || e is OverflowException)
            {
                throw new ArgumentException($"{target.GetType().Name}.{propertyPath} is a {p.propertyType}; cannot set it to {value ?? "null"} ({value?.GetType().Name})", e);
            }
        }

        /// <summary>Lower-case words of a field name ("m_SoftShadowQuality" -> soft, shadow, quality).</summary>
        static List<string> Words(string name)
        {
            var words = new List<string>();
            foreach (var w in System.Text.RegularExpressions.Regex.Split(name.Replace("m_", ""), "(?<=[a-z])(?=[A-Z])|[._ ]"))
                if (w.Length >= 3) words.Add(w.ToLowerInvariant());
            return words;
        }

        static string Similar(SerializedObject so, string wanted)
        {
            var words = Words(wanted);
            var hits = new List<string>();
            var it = so.GetIterator();
            while (it.Next(true) && hits.Count < 8)
            {
                var path = it.propertyPath.ToLowerInvariant();
                if (path.Contains("array.")) continue;
                if (words.Exists(w => path.Contains(w))) hits.Add(it.propertyPath);
            }
            return hits.Count == 0 ? "" : " (similar: " + string.Join(", ", hits) + ")";
        }

        /// <summary>
        /// Use <paramref name="pipeline"/> as the project's default render pipeline (Graphics settings), or, with
        /// <paramref name="qualityLevels"/>, as the override of those quality levels (by name; each must exist).
        /// </summary>
        public void UsePipeline(RenderPipelineAsset pipeline, params string[] qualityLevels)
        {
            if (qualityLevels == null || qualityLevels.Length == 0)
            {
                if (GraphicsSettings.defaultRenderPipeline != pipeline)
                {
                    GraphicsSettings.defaultRenderPipeline = pipeline;
                    Assigned.Add("GraphicsSettings.defaultRenderPipeline=" + Describe(pipeline));
                }
                return;
            }
            var qs = QualitySettingsAsset();
            using (var so = new SerializedObject(qs))
            {
                var levels = so.FindProperty("m_QualitySettings");
                var changed = false;
                foreach (var level in qualityLevels)
                {
                    var index = -1;
                    var names = new List<string>();
                    for (var i = 0; i < levels.arraySize; i++)
                    {
                        var n = levels.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue;
                        names.Add(n);
                        if (n == level) index = i;
                    }
                    if (index < 0) throw new ArgumentException($"no quality level '{level}' (levels: {string.Join(", ", names)})");
                    var p = levels.GetArrayElementAtIndex(index).FindPropertyRelative("customRenderPipeline");
                    if (p.objectReferenceValue == pipeline) continue;
                    p.objectReferenceValue = pipeline;
                    changed = true;
                    Assigned.Add($"QualitySettings[{level}].renderPipeline={Describe(pipeline)}");
                }
                if (changed) so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        internal static Object QualitySettingsAsset()
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset"))
                if (o != null) return o;
            throw new InvalidOperationException("ProjectSettings/QualitySettings.asset not found");
        }

        static string Describe(Object o) => o == null ? "none" : AssetDatabase.GetAssetPath(o);

        /// <summary>Which pipeline the Graphics settings and each quality level use (part of build.fingerprint).</summary>
        internal static string DescribePipelines()
        {
            var sb = new StringBuilder();
            sb.Append("graphics.defaultRenderPipeline=").Append(Describe(GraphicsSettings.defaultRenderPipeline)).Append('\n');
            var names = QualitySettings.names;
            for (var i = 0; i < names.Length; i++)
                sb.Append("quality[").Append(i).Append("]=").Append(names[i]).Append(':').Append(Describe(QualitySettings.GetRenderPipelineAssetAt(i))).Append('\n');
            return sb.ToString();
        }

#if AGENTHARNESS_URP
        // ---- URP -----------------------------------------------------------------------------------

        /// <summary>
        /// A URP asset &lt;name&gt;_RPAsset.asset with its Universal Renderer &lt;name&gt;_Renderer.asset, both starting from URP's
        /// defaults for a new asset (Assets &gt; Create &gt; Rendering &gt; URP Asset). Assign it with <see cref="UsePipeline"/>.
        /// Settings without a public setter: <see cref="Set"/> (e.g. <c>Set(rp, "m_SoftShadowsSupported", true)</c>).
        /// </summary>
        public UniversalRenderPipelineAsset UniversalPipeline(string name, Action<UniversalRenderPipelineAsset> configure = null, Action<UniversalRendererData> renderer = null)
        {
            var data = ScriptableObject.CreateInstance<UniversalRendererData>();
            // What URP's menu does for a new renderer (debug shaders and probe resources load in UniversalRendererData.OnEnable).
            data.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(UniversalRenderPipelineAsset.packagePath + "/Runtime/Data/PostProcessData.asset");
            renderer?.Invoke(data);
            var saved = Save(data, name + "_Renderer.asset");
            var rp = UniversalRenderPipelineAsset.Create(saved);
            configure?.Invoke(rp);
            return Save(rp, name + "_RPAsset.asset");
        }

        /// <summary>Add a renderer feature (a sub-asset of the renderer) and configure it; internal fields via <see cref="Set"/>.</summary>
        public static T AddRendererFeature<T>(ScriptableRendererData renderer, Action<T> configure = null) where T : ScriptableRendererFeature
        {
            var feature = ScriptableObject.CreateInstance<T>();
            feature.name = typeof(T).Name;
            // URP 17.6 hides feature sub-assets when it loads a renderer (and its menu does); do the same in every version, or
            // 6.6's copy on disk never matches a fresh one and gets rewritten every build.
            feature.hideFlags |= HideFlags.HideInHierarchy;
            configure?.Invoke(feature);
            renderer.rendererFeatures.Add(feature);
            return feature;
        }
#endif

        // ---- Writing -------------------------------------------------------------------------------

        /// <summary>
        /// Persist <paramref name="fresh"/> (destroyed afterwards) at &lt;generatedRoot&gt;/&lt;Module&gt;/&lt;relative&gt; and return the asset.
        /// Unchanged content is not written. A missing asset (or one with another GUID) is created with the path's GUID.
        /// </summary>
        internal T Save<T>(T fresh, string relative) where T : Object
        {
            if (string.IsNullOrEmpty(Path.GetExtension(relative))) relative += ".asset";
            var path = AssetPath(relative);
            var folder = Path.GetDirectoryName(path).Replace('\\', '/');
            BuildContext.EnsureFolder(folder);
            fresh.name = Path.GetFileNameWithoutExtension(path);
            if (Assets.Contains(path)) throw new InvalidOperationException($"{path} was already written by a settings step in this run");
            Assets.Add(path);
            var guid = FixedGuid(path);
            var subs = new List<Object>();
            CollectNew(fresh, fresh, subs);

            var existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null && existing.GetType() == fresh.GetType() && AssetDatabase.AssetPathToGUID(path) == guid)
            {
                if (Dump(existing) == Dump(fresh))
                {
                    Destroy(fresh, subs);
                    return (T)existing;
                }
                foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (o == null || o == existing || o is GameObject || o is Component) continue;
                    AssetDatabase.RemoveObjectFromAsset(o);
                    Object.DestroyImmediate(o, true);
                }
                EditorUtility.CopySerialized(fresh, existing);
                existing.name = fresh.name;
                foreach (var s in subs) AssetDatabase.AddObjectToAsset(s, existing);
                AfterSubAssets(existing);
                EditorUtility.SetDirty(existing);
                AssetDatabase.SaveAssetIfDirty(existing);
                Object.DestroyImmediate(fresh);
                Written.Add(path);
                return (T)existing;
            }

            // Create: let Unity serialize it at a temporary path, then move the file under the fixed GUID (AssetDatabase has
            // no API to choose a GUID; CreateAsset ignores a .meta written beforehand). Sub-asset file IDs stay as written.
            if (existing != null || File.Exists(Full(path))) AssetDatabase.DeleteAsset(path);
            // Same file name (CreateAsset names the object after its file) in a scratch folder.
            var tmpFolder = HarnessPaths.GeneratedRoot + "/__harness_tmp";
            AssetDatabase.DeleteAsset(tmpFolder);
            BuildContext.EnsureFolder(tmpFolder);
            var tmp = tmpFolder + "/" + Path.GetFileName(path);
            AssetDatabase.CreateAsset(fresh, tmp);
            foreach (var s in subs) AssetDatabase.AddObjectToAsset(s, fresh);
            AfterSubAssets(fresh);
            EditorUtility.SetDirty(fresh);
            AssetDatabase.SaveAssetIfDirty(fresh);
            var bytes = File.ReadAllBytes(Full(tmp));
            AssetDatabase.DeleteAsset(tmpFolder);
            File.WriteAllBytes(Full(path), bytes);
            File.WriteAllText(Full(path) + ".meta",
                "fileFormatVersion: 2\nguid: " + guid + "\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: 11400000\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var created = AssetDatabase.LoadMainAssetAtPath(path) as T;
            if (created == null) throw new InvalidOperationException($"settings asset {path} did not import");
            if (AssetDatabase.AssetPathToGUID(path) != guid) Warn($"{path} got GUID {AssetDatabase.AssetPathToGUID(path)}, not {guid}: ProjectSettings that reference it will differ between machines");
            Written.Add(path);
            return created;
        }

        /// <summary>Renderer data lists its features' local file IDs (URP re-links lost references with it); fill it once they are sub-assets.</summary>
        static void AfterSubAssets(Object asset)
        {
            using (var so = new SerializedObject(asset))
            {
                var list = so.FindProperty("m_RendererFeatures");
                var map = so.FindProperty("m_RendererFeatureMap");
                if (list == null || map == null || !list.isArray || !map.isArray) return;
                map.arraySize = list.arraySize;
                for (var i = 0; i < list.arraySize; i++)
                {
                    var f = list.GetArrayElementAtIndex(i).objectReferenceValue;
                    map.GetArrayElementAtIndex(i).longValue = f != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out _, out long id) ? id : 0;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static string Full(string assetPath) => HarnessPaths.Combine(HarnessPaths.ProjectRoot, assetPath);

        /// <summary>The GUID a settings asset gets: a hash of its path, the same on every machine.</summary>
        internal static string FixedGuid(string assetPath)
        {
            using (var md5 = MD5.Create())
            {
                var h = md5.ComputeHash(Encoding.UTF8.GetBytes("AgentHarness/settings/" + assetPath));
                var sb = new StringBuilder(32);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        static void Destroy(Object fresh, List<Object> subs)
        {
            foreach (var s in subs) Object.DestroyImmediate(s);
            Object.DestroyImmediate(fresh);
        }

        /// <summary>Objects <paramref name="o"/> references that are not assets yet (they become sub-assets).</summary>
        static void CollectNew(Object o, Object root, List<Object> found)
        {
            using (var so = new SerializedObject(o))
            {
                var it = so.GetIterator();
                var enter = true;
                while (it.Next(enter))
                {
                    enter = it.propertyType == SerializedPropertyType.Generic;
                    if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var r = it.objectReferenceValue;
                    if (r == null || r == root || EditorUtility.IsPersistent(r) || found.Contains(r)) continue;
                    found.Add(r);
                    CollectNew(r, root, found);
                }
            }
        }

        // Local file IDs of sub-assets: random per creation, not settings.
        static readonly string[] s_Ignored = { "m_RendererFeatureMap" };

        /// <summary>
        /// Serialized content of an asset and its sub-objects, independent of file IDs: a reference to another asset is its
        /// path, type and name; a sub-object (or a not-yet-saved object) is dumped in place. Same text = same settings.
        /// </summary>
        internal static string Dump(Object main)
        {
            var sb = new StringBuilder();
            var done = new HashSet<Object>();
            DumpObject(main, main, sb, done);
            return sb.ToString();
        }

        static void DumpObject(Object o, Object main, StringBuilder sb, HashSet<Object> done)
        {
            if (!done.Add(o)) return;
            sb.Append("O ").Append(o.GetType().FullName).Append(':').Append(o.name).Append('\n');
            var inline = new List<Object>();
            using (var so = new SerializedObject(o))
            {
                var it = so.GetIterator();
                var enter = true;
                while (it.Next(enter))
                {
                    enter = it.propertyType == SerializedPropertyType.Generic;
                    if (Array.Exists(s_Ignored, x => it.propertyPath.StartsWith(x, StringComparison.Ordinal))) { enter = false; continue; }
                    if (it.propertyType == SerializedPropertyType.Generic) continue;
                    string v;
                    if (it.propertyType == SerializedPropertyType.ObjectReference)
                    {
                        var r = it.objectReferenceValue;
                        if (r == null) v = "null";
                        else if (r != main && (!EditorUtility.IsPersistent(r) || (EditorUtility.IsPersistent(main) && AssetDatabase.GetAssetPath(r) == AssetDatabase.GetAssetPath(main))))
                        {
                            v = "sub:" + r.GetType().Name + ":" + r.name;
                            inline.Add(r);
                        }
                        else v = EditorUtility.IsPersistent(r) ? "asset:" + AssetDatabase.GetAssetPath(r) + ":" + r.GetType().Name + ":" + r.name : "obj:" + r.GetType().Name + ":" + r.name;
                    }
                    else if (it.propertyType == SerializedPropertyType.ManagedReference) v = "managed:" + it.managedReferenceFullTypename;
                    else v = SceneFingerprint.Value(it);
                    if (v != null) sb.Append("  ").Append(it.propertyPath).Append('=').Append(v).Append('\n');
                }
            }
            foreach (var r in inline) DumpObject(r, main, sb, done);
        }

        // ---- Running -------------------------------------------------------------------------------

        internal sealed class RunResult
        {
            public SettingsContext ctx;
            public List<(ISettingsStep step, HarnessBuild.StepInfo info)> steps;
            public bool failed;
            public bool skipped;   // steps exist but the project is not a harness project
            public string switched;   // "<before> -> <after>" when the active render pipeline changed (a domain reload was requested)
        }

        /// <summary>Run every settings step (harness projects only). Saves changed project settings.</summary>
        internal static RunResult Run(List<string> ignored)
        {
            var steps = HarnessBuild.DiscoverSteps<ISettingsStep>(s => s.Order, ignored);
            var result = new RunResult { ctx = new SettingsContext(), steps = steps };
            foreach (var (_, info) in steps) info.phase = "settings";
            if (steps.Count == 0) return result;
            if (!HarnessPaths.Config.IsHarnessProject)
            {
                result.skipped = true;
                result.ctx.Warnings.Add($"{steps.Count} settings step(s) not applied: \"setup\" is \"{HarnessPaths.Config.setup}\" in {HarnessConfig.FileName} (an attached project's render settings are its own)");
                return result;
            }
            var before = GraphicsSettings.currentRenderPipeline;
            var files = ProjectFileIds();
            foreach (var (step, info) in steps)
            {
                result.ctx.Module = info.module;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { step.Apply(result.ctx); }
                catch (Exception e)
                {
                    result.failed = true;
                    HarnessBuild.FillError(info, e);
                }
                info.ms = Math.Round(sw.Elapsed.TotalMilliseconds, 1);
            }
            // ProjectSettings the steps own: clear undeclared layers and tags, save (only when every step ran).
            if (!result.failed)
            {
                var (_, last) = steps[steps.Count - 1];
                try { result.ctx.FinishProject(); }
                catch (Exception e)
                {
                    result.failed = true;
                    HarnessBuild.FillError(last, e);
                }
            }
            if (result.ctx.Assigned.Count > 0) AssetDatabase.SaveAssets();
            // Remember what the owned settings are now and how each ProjectSettings file came to be (Library/Harness/project-settings.json).
            if (!result.failed)
            {
                try { result.ctx.SaveSnapshot(files); }
                catch (Exception e) { result.ctx.Warnings.Add($"[harness] could not save {SnapshotPath}: {e.Message}"); }
            }
            var after = GraphicsSettings.currentRenderPipeline;
            if (before != after)
            {
                // Editor state that depends on the pipeline is set up when the domain loads: in a session that started with
                // another pipeline (a new clone opens with Built-in until its first harness_setup), Unity 6.6 left new shader
                // variants out of the first frame they were drawn in (a stack overlay camera's quad). A reload fixes it; the
                // callers (uc.ps1, the loop) wait for it.
                result.switched = Describe(before) + " -> " + Describe(after);
                EditorUtility.RequestScriptReload();
            }
            return result;
        }

        /// <summary>Settings part of build.fingerprint: every settings asset's content and the pipeline assignments.</summary>
        internal string Fingerprint()
        {
            var sb = new StringBuilder();
            var paths = new List<string>(Assets);
            paths.Sort(StringComparer.Ordinal);
            foreach (var p in paths)
            {
                var a = AssetDatabase.LoadMainAssetAtPath(p);
                sb.Append("== ").Append(p).Append('\n').Append(a == null ? "missing\n" : Dump(a));
            }
            sb.Append(DescribePipelines());
            ProjectFingerprint(sb);
            return sb.ToString();
        }
    }
}
