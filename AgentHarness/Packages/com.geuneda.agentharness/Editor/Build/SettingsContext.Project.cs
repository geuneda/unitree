using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Harness.Editor
{
    public sealed partial class SettingsContext
    {
        // ---- Project settings (G1-5) ----------------------------------------------------------------
        //
        // ProjectSettings/ cannot be generated (the Editor needs it to start), so a settings step owns values in it instead: every
        // build writes what the code says where it differs and reports it, and warns when the value it found is not the one the
        // previous build left (changed in the Project Settings window or the YAML: drift). Values the code does not name stay as
        // committed. What the last build left: Library/Harness/project-settings.json (machine-local).

        sealed class Owned { public string wanted, actual, module; }

        readonly Dictionary<string, Owned> m_Owned = new Dictionary<string, Owned>(StringComparer.Ordinal);
        internal readonly List<string> ProjectChanged = new List<string>();   // "key: before -> after" written by this run
        internal readonly List<string> ProjectDrift = new List<string>();     // keys that were changed outside the code
        readonly HashSet<Object> m_ToSave = new HashSet<Object>();
        Dictionary<string, string> m_Last;
        List<(int index, string name, string module)> m_Layers;   // null: no step declares layers (they are the project's)
        List<string> m_Tags;
        string m_QualityModule;

        internal int ProjectOwned => m_Owned.Count;

        /// <summary>Player settings the code owns (only the fields it sets).</summary>
        public void Player(Action<PlayerValues> configure)
        {
            var v = new PlayerValues();
            configure(v);
            var ps = SettingsObject("ProjectSettings");
            if (v.ColorSpace.HasValue) Api("Player.colorSpace", v.ColorSpace.Value, () => PlayerSettings.colorSpace, x => PlayerSettings.colorSpace = x, ps);
            if (v.CompanyName != null) Api("Player.companyName", v.CompanyName, () => PlayerSettings.companyName, x => PlayerSettings.companyName = x, ps);
            if (v.ProductName != null) Api("Player.productName", v.ProductName, () => PlayerSettings.productName, x => PlayerSettings.productName = x, ps);
            if (v.DefaultScreenWidth.HasValue) Api("Player.defaultScreenWidth", v.DefaultScreenWidth.Value, () => PlayerSettings.defaultScreenWidth, x => PlayerSettings.defaultScreenWidth = x, ps);
            if (v.DefaultScreenHeight.HasValue) Api("Player.defaultScreenHeight", v.DefaultScreenHeight.Value, () => PlayerSettings.defaultScreenHeight, x => PlayerSettings.defaultScreenHeight = x, ps);
            if (v.FullScreenMode.HasValue) Api("Player.fullScreenMode", v.FullScreenMode.Value, () => PlayerSettings.fullScreenMode, x => PlayerSettings.fullScreenMode = x, ps);
            if (v.ResizableWindow.HasValue) Api("Player.resizableWindow", v.ResizableWindow.Value, () => PlayerSettings.resizableWindow, x => PlayerSettings.resizableWindow = x, ps);
            if (v.DefaultOrientation.HasValue) Api("Player.defaultOrientation", v.DefaultOrientation.Value, () => PlayerSettings.defaultInterfaceOrientation, x => PlayerSettings.defaultInterfaceOrientation = x, ps);
            foreach (var e in v.Extra) Serialized("Player." + e.Key, ps, e.Key, e.Value);
        }

        /// <summary>Time settings the code owns (only the fields it sets).</summary>
        public void Time(Action<TimeValues> configure)
        {
            var v = new TimeValues();
            configure(v);
            var tm = SettingsObject("TimeManager");
            // The Time API: Unity 6.3 serializes the fixed timestep as a fraction (the YAML of older versions has a float).
            if (v.FixedTimestep.HasValue) Api("Time.fixedTimestep", v.FixedTimestep.Value, () => UnityEngine.Time.fixedDeltaTime, x => UnityEngine.Time.fixedDeltaTime = x, tm);
            if (v.MaximumAllowedTimestep.HasValue) Api("Time.maximumAllowedTimestep", v.MaximumAllowedTimestep.Value, () => UnityEngine.Time.maximumDeltaTime, x => UnityEngine.Time.maximumDeltaTime = x, tm);
            if (v.MaximumParticleTimestep.HasValue) Api("Time.maximumParticleTimestep", v.MaximumParticleTimestep.Value, () => UnityEngine.Time.maximumParticleDeltaTime, x => UnityEngine.Time.maximumParticleDeltaTime = x, tm);
            if (v.TimeScale.HasValue) Api("Time.timeScale", v.TimeScale.Value, () => UnityEngine.Time.timeScale, x => UnityEngine.Time.timeScale = x, tm);
            foreach (var e in v.Extra) Serialized("Time." + e.Key, tm, e.Key, e.Value);
        }

#if AGENTHARNESS_PHYSICS
        /// <summary>3D physics settings the code owns (only the fields it sets; the collision matrix once it names a pair).</summary>
        public void Physics(Action<PhysicsValues> configure)
        {
            var v = new PhysicsValues();
            configure(v);
            var dm = SettingsObject("DynamicsManager");
            if (v.Gravity.HasValue) Api("Physics.gravity", v.Gravity.Value, () => UnityEngine.Physics.gravity, x => UnityEngine.Physics.gravity = x, dm);
            if (v.DefaultSolverIterations.HasValue) Api("Physics.defaultSolverIterations", v.DefaultSolverIterations.Value, () => UnityEngine.Physics.defaultSolverIterations, x => UnityEngine.Physics.defaultSolverIterations = x, dm);
            if (v.DefaultSolverVelocityIterations.HasValue) Api("Physics.defaultSolverVelocityIterations", v.DefaultSolverVelocityIterations.Value, () => UnityEngine.Physics.defaultSolverVelocityIterations, x => UnityEngine.Physics.defaultSolverVelocityIterations = x, dm);
            if (v.BounceThreshold.HasValue) Api("Physics.bounceThreshold", v.BounceThreshold.Value, () => UnityEngine.Physics.bounceThreshold, x => UnityEngine.Physics.bounceThreshold = x, dm);
            if (v.SleepThreshold.HasValue) Api("Physics.sleepThreshold", v.SleepThreshold.Value, () => UnityEngine.Physics.sleepThreshold, x => UnityEngine.Physics.sleepThreshold = x, dm);
            if (v.DefaultContactOffset.HasValue) Api("Physics.defaultContactOffset", v.DefaultContactOffset.Value, () => UnityEngine.Physics.defaultContactOffset, x => UnityEngine.Physics.defaultContactOffset = x, dm);
            if (v.QueriesHitTriggers.HasValue) Api("Physics.queriesHitTriggers", v.QueriesHitTriggers.Value, () => UnityEngine.Physics.queriesHitTriggers, x => UnityEngine.Physics.queriesHitTriggers = x, dm);
            if (v.QueriesHitBackfaces.HasValue) Api("Physics.queriesHitBackfaces", v.QueriesHitBackfaces.Value, () => UnityEngine.Physics.queriesHitBackfaces, x => UnityEngine.Physics.queriesHitBackfaces = x, dm);
            if (v.SimulationMode.HasValue) Api("Physics.simulationMode", v.SimulationMode.Value, () => UnityEngine.Physics.simulationMode, x => UnityEngine.Physics.simulationMode = x, dm);
            if (v.Ignored != null)
            {
                var pairs = new List<(int, int)>();
                foreach (var (a, b) in v.Ignored)
                {
                    int i = LayerIndex(a), j = LayerIndex(b);
                    var pair = i <= j ? (i, j) : (j, i);
                    if (!pairs.Contains(pair)) pairs.Add(pair);
                }
                pairs.Sort();
                Own("Physics.ignoreCollision", IgnoredText(pairs), () => IgnoredText(IgnoredPairs()), () =>
                {
                    for (var i = 0; i < 32; i++)
                        for (var j = i; j < 32; j++)
                        {
                            var ignore = pairs.Contains((i, j));
                            if (UnityEngine.Physics.GetIgnoreLayerCollision(i, j) != ignore) UnityEngine.Physics.IgnoreLayerCollision(i, j, ignore);
                        }
                }, dm);
            }
            foreach (var e in v.Extra) Serialized("Physics." + e.Key, dm, e.Key, e.Value);
        }

        static List<(int, int)> IgnoredPairs()
        {
            var pairs = new List<(int, int)>();
            for (var i = 0; i < 32; i++)
                for (var j = i; j < 32; j++)
                    if (UnityEngine.Physics.GetIgnoreLayerCollision(i, j)) pairs.Add((i, j));
            return pairs;
        }

        static string IgnoredText(List<(int a, int b)> pairs)
        {
            var items = new List<string>();
            foreach (var (a, b) in pairs) items.Add(LayerLabel(a) + "/" + LayerLabel(b));
            return Format(items);
        }

        static int LayerIndex(string name)
        {
            var i = LayerMask.NameToLayer(name);
            if (i < 0) throw new ArgumentException($"no layer '{name}' (declare it with ctx.Layer before the physics settings)");
            return i;
        }

        static string LayerLabel(int i)
        {
            var n = LayerMask.LayerToName(i);
            return string.IsNullOrEmpty(n) ? i.ToString(CultureInfo.InvariantCulture) : n;
        }
#endif

        static readonly string[] s_BuiltinLayers = { "Default", "TransparentFX", "Ignore Raycast", null, "Water", "UI" };   // 3 is a user layer
        static readonly string[] s_BuiltinTags = { "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController" };

        /// <summary>
        /// User layer <paramref name="index"/> (3, 6-31) is <paramref name="name"/>; returns the index. Once a step declares a layer,
        /// the user layers are exactly the declared ones (the others are cleared after the last settings step). Build steps run after
        /// every settings step: LayerMask.NameToLayer / GetMask see the layers there.
        /// </summary>
        public int Layer(int index, string name)
        {
            if (index < 0 || index > 31) throw new ArgumentOutOfRangeException(nameof(index), index, "layers are 0-31; user layers are 3 and 6-31");
            if (index < s_BuiltinLayers.Length && s_BuiltinLayers[index] != null) throw new ArgumentException($"layer {index} is Unity's '{s_BuiltinLayers[index]}'; user layers are 3 and 6-31");
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("a layer needs a name");
            if (Array.IndexOf(s_BuiltinLayers, name) >= 0) throw new ArgumentException($"'{name}' is a built-in layer");
            m_Layers ??= new List<(int, string, string)>();
            foreach (var (i, n, m) in m_Layers)
            {
                if (i == index && n == name) return index;
                if (i == index) throw new InvalidOperationException($"layer {index} is already '{n}' (module {m})");
                if (n == name) throw new InvalidOperationException($"layer '{name}' is already layer {i} (module {m})");
            }
            m_Layers.Add((index, name, Module));
            Serialized($"Layer[{index}]", SettingsObject("TagManager"), $"layers.Array.data[{index}]", name);
            return index;
        }

        /// <summary>
        /// A tag (the built-in ones need no declaration). Once a step declares a tag, the tags are exactly the declared ones, in
        /// order (applied after the last settings step, before the build steps).
        /// </summary>
        public void Tag(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("a tag needs a name");
            m_Tags ??= new List<string>();
            if (Array.IndexOf(s_BuiltinTags, name) < 0 && !m_Tags.Contains(name)) m_Tags.Add(name);
        }

        /// <summary>
        /// The quality levels, exactly these in this order (matched by name; a new level starts as a copy of the one before it) with
        /// the values each sets. One settings step declares the whole list. The Editor then plays with the active platform's
        /// default level (<see cref="QualityLevelValues.DefaultFor"/>), as after switching the platform.
        /// </summary>
        public void QualityLevels(params QualityLevelValues[] levels)
        {
            if (levels == null || levels.Length == 0) throw new ArgumentException("QualityLevels needs at least one level");
            if (m_QualityModule != null) throw new InvalidOperationException($"the quality levels were already declared by module {m_QualityModule}: one settings step declares the whole list");
            var names = new List<string>();
            foreach (var l in levels)
            {
                if (l == null || string.IsNullOrWhiteSpace(l.Name)) throw new ArgumentException("a quality level needs a name");
                if (names.Contains(l.Name)) throw new ArgumentException($"quality level '{l.Name}' is declared twice");
                names.Add(l.Name);
            }
            m_QualityModule = Module;
            var qs = QualitySettingsAsset();
            Own("Quality.levels", Format(names), () => Format(LevelNames(qs)), () => SetLevels(qs, names), qs);

            var platforms = PlatformNames(qs);
            for (var i = 0; i < levels.Length; i++)
            {
                var l = levels[i];
                var at = $"m_QualitySettings.Array.data[{i}]";
                void Field(string field, object value) => Serialized($"Quality[{l.Name}].{field}", qs, at + "." + field, value,
                    so => $"quality level '{l.Name}' has no field '{field}' in Unity {Application.unityVersion}{SimilarChild(so, at, field)}");
                if (l.Pipeline != null)
                {
                    var changes = ProjectChanged.Count;
                    Field("customRenderPipeline", l.Pipeline);
                    if (ProjectChanged.Count > changes) Assigned.Add($"QualitySettings[{l.Name}].renderPipeline={Describe(l.Pipeline)}");
                }
                if (l.ExcludedPlatforms != null) Field("excludedTargetPlatforms", KnownPlatforms(platforms, l.ExcludedPlatforms));
                if (l.VSyncCount.HasValue) Field("vSyncCount", l.VSyncCount.Value);
                if (l.LodBias.HasValue) Field("lodBias", l.LodBias.Value);
                if (l.MaximumLodLevel.HasValue) Field("maximumLODLevel", l.MaximumLodLevel.Value);
                if (l.AnisotropicTextures.HasValue) Field("anisotropicTextures", l.AnisotropicTextures.Value);
                if (l.GlobalTextureMipmapLimit.HasValue) Field("globalTextureMipmapLimit", l.GlobalTextureMipmapLimit.Value);
                if (l.SkinWeights.HasValue) Field("skinWeights", l.SkinWeights.Value);
                if (l.RealtimeReflectionProbes.HasValue) Field("realtimeReflectionProbes", l.RealtimeReflectionProbes.Value);
                if (l.BillboardsFaceCameraPosition.HasValue) Field("billboardsFaceCameraPosition", l.BillboardsFaceCameraPosition.Value);
                if (l.SoftParticles.HasValue) Field("softParticles", l.SoftParticles.Value);
                if (l.ParticleRaycastBudget.HasValue) Field("particleRaycastBudget", l.ParticleRaycastBudget.Value);
                if (l.StreamingMipmaps.HasValue) Field("streamingMipmapsActive", l.StreamingMipmaps.Value);
                foreach (var e in l.Extra) Field(e.Key, e.Value);
                if (l.DefaultFor == null) continue;
                foreach (var platform in KnownPlatforms(platforms, l.DefaultFor))
                {
                    var index = i;
                    Own($"Quality.default[{platform}]", l.Name, () => DefaultLevel(qs, platform), () => SetDefaultLevel(qs, platform, index), qs);
                }
            }

            var active = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)).TargetName;
            for (var i = 0; i < levels.Length; i++)
            {
                if (levels[i].DefaultFor == null || Array.IndexOf(levels[i].DefaultFor, active) < 0) continue;
                var index = i;
                Own("Quality.editor", levels[i].Name, CurrentLevelName, () => QualitySettings.SetQualityLevel(index, true), qs);
                break;
            }
        }

        /// <summary>
        /// Any field of ProjectSettings/&lt;file&gt;.asset by its serialized path, e.g.
        /// <c>ctx.ProjectSetting("TagManager", "m_RenderingLayers.Array.data[1]", "Glow")</c>. The typed helpers come first.
        /// </summary>
        public void ProjectSetting(string file, string propertyPath, object value) => Serialized($"{file}:{propertyPath}", SettingsObject(file), propertyPath, value);

        // ---- Owning a value ---------------------------------------------------------------------------

        /// <summary>
        /// Every project setting goes through here: read it, write it when it is not <paramref name="wanted"/>, report the change,
        /// and warn when the value found is not the one the previous build left. Values compare as text (numbers within 1e-6).
        /// </summary>
        void Own(string key, string wanted, Func<string> read, Action write, Object owner, string fix = "Change it in the settings step")
        {
            if (m_Owned.TryGetValue(key, out var prev))
            {
                if (!Same(prev.wanted, wanted)) throw new InvalidOperationException($"project setting {key} is already set to {Show(prev.wanted)} by module {prev.module}: one settings step owns it");
                return;
            }
            var owned = new Owned { wanted = wanted, module = Module };
            m_Owned[key] = owned;
            var before = read();
            var actual = before;
            if (!Same(before, wanted))
            {
                write();
                actual = read();
                if (!Same(actual, before))
                {
                    if (owner != null) m_ToSave.Add(owner);
                    ProjectChanged.Add($"{key}: {Show(before)} -> {Show(actual)}");
                    if (Last.TryGetValue(key, out var last) && !Same(last, before))
                    {
                        ProjectDrift.Add(key);
                        Warn($"project setting {key} was {Show(before)}, not {Show(last)} as the previous build left it: it was changed outside the code " +
                             $"(Project Settings window or ProjectSettings YAML) and is {Show(actual)} again. {fix}");
                    }
                }
            }
            if (!Same(actual, wanted)) Warn($"project setting {key}: the code sets {Show(wanted)}, Unity keeps {Show(actual)}");
            owned.actual = actual;
        }

        void Api<T>(string key, T want, Func<T> get, Action<T> set, Object owner) => Own(key, Format(want), () => Format(get()), () => set(want), owner);

        /// <summary>
        /// A field of a ProjectSettings object by its serialized path, compared as that field holds it (enum = int, float).
        /// <paramref name="missing"/>: the error when this Unity version has no such field.
        /// </summary>
        void Serialized(string key, Object target, string path, object value, Func<SerializedObject, string> missing = null, string fix = "Change it in the settings step")
        {
            string wanted;
            using (var scratch = new SerializedObject(target))
            {
                var p = scratch.FindProperty(path)
                        ?? throw new ArgumentException(missing != null ? missing(scratch) : $"{target.GetType().Name} has no serialized field '{path}' in Unity {Application.unityVersion}{Similar(scratch, path)}");
                Assign(p, value, target, path);
                wanted = Read(p);
            }
            Own(key, wanted, () =>
            {
                using (var so = new SerializedObject(target)) return Read(so.FindProperty(path));
            }, () =>
            {
                using (var so = new SerializedObject(target))
                {
                    Assign(so.FindProperty(path), value, target, path);
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
            }, target, fix);
        }

        static string Read(SerializedProperty p)
        {
            if (p.isArray && p.propertyType == SerializedPropertyType.Generic && p.arrayElementType == "string")
            {
                var items = new List<string>();
                for (var i = 0; i < p.arraySize; i++) items.Add(p.GetArrayElementAtIndex(i).stringValue);
                return Format(items);
            }
            switch (p.propertyType)
            {
                case SerializedPropertyType.Boolean: return Format(p.boolValue);
                case SerializedPropertyType.Float: return Format(p.floatValue);
                case SerializedPropertyType.ObjectReference: return Format(p.objectReferenceValue);
                case SerializedPropertyType.Vector2: return Format(p.vector2Value);
                case SerializedPropertyType.Vector3: return Format(p.vector3Value);
                default: return SceneFingerprint.Value(p) ?? p.propertyType.ToString();
            }
        }

        internal static string Format(object v)
        {
            switch (v)
            {
                case null: return "null";
                case string s: return s;
                case float f: return f.ToString("R", CultureInfo.InvariantCulture);
                case double d: return ((float)d).ToString("R", CultureInfo.InvariantCulture);
                case bool b: return b ? "true" : "false";
                case Vector2 v2: return "(" + Format(v2.x) + ", " + Format(v2.y) + ")";
                case Vector3 v3: return "(" + Format(v3.x) + ", " + Format(v3.y) + ", " + Format(v3.z) + ")";
                case Object o: return o == null ? "null" : AssetOrName(o);
                case IEnumerable<string> items: return "[" + string.Join(", ", items) + "]";
                case IFormattable f: return f.ToString(null, CultureInfo.InvariantCulture);
                default: return v.ToString();
            }
        }

        static string AssetOrName(Object o)
        {
            var path = AssetDatabase.GetAssetPath(o);
            return string.IsNullOrEmpty(path) ? o.name : path;
        }

        static string Show(string v) => v == "" ? "\"\"" : v;

        static readonly Regex s_Number = new Regex(@"-?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?");

        /// <summary>The same text, or the same text around numbers within 1e-6 (relative): a float read back is not the literal.</summary>
        static bool Same(string a, string b)
        {
            if (a == b) return true;
            if (a == null || b == null) return false;
            var ma = s_Number.Matches(a);
            var mb = s_Number.Matches(b);
            if (ma.Count == 0 || ma.Count != mb.Count || s_Number.Replace(a, "#") != s_Number.Replace(b, "#")) return false;
            for (var i = 0; i < ma.Count; i++)
            {
                if (!double.TryParse(ma[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ||
                    !double.TryParse(mb[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var y)) return false;
                if (Math.Abs(x - y) > 1e-6 * Math.Max(1.0, Math.Max(Math.Abs(x), Math.Abs(y)))) return false;
            }
            return true;
        }

        static Object SettingsObject(string file)
        {
            var path = "ProjectSettings/" + file + ".asset";
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o != null) return o;
            var files = new List<string>();
            foreach (var f in Directory.GetFiles(HarnessPaths.Combine(HarnessPaths.ProjectRoot, "ProjectSettings"), "*.asset")) files.Add(Path.GetFileNameWithoutExtension(f));
            throw new ArgumentException($"no {path} (files: {string.Join(", ", files)})");
        }

        /// <summary>Fields of the array element at <paramref name="at"/> whose names share a word with <paramref name="wanted"/>.</summary>
        static string SimilarChild(SerializedObject so, string at, string wanted)
        {
            var element = so.FindProperty(at);
            if (element == null) return "";
            var words = Words(wanted);
            var hits = new List<string>();
            var it = element.Copy();
            var end = element.GetEndProperty();
            var enter = true;
            while (it.Next(enter) && !SerializedProperty.EqualContents(it, end))
            {
                enter = false;
                var name = it.name.ToLowerInvariant();
                if (words.Exists(w => name.Contains(w))) hits.Add(it.name);
            }
            return hits.Count == 0 ? "" : " (similar: " + string.Join(", ", hits) + ")";
        }

        // ---- Quality levels ---------------------------------------------------------------------------

        static List<string> LevelNames(Object qs)
        {
            using (var so = new SerializedObject(qs)) return LevelNames(so.FindProperty("m_QualitySettings"));
        }

        static List<string> LevelNames(SerializedProperty list)
        {
            var names = new List<string>();
            for (var i = 0; i < list.arraySize; i++) names.Add(list.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue);
            return names;
        }

        /// <summary>Reorder, add and remove levels to get <paramref name="names"/>; the Editor's level and per-platform defaults follow their level by name.</summary>
        static void SetLevels(Object qs, List<string> names)
        {
            using (var so = new SerializedObject(qs))
            {
                var list = so.FindProperty("m_QualitySettings");
                var before = LevelNames(list);
                string NameAt(int i) => i >= 0 && i < before.Count ? before[i] : null;
                var current = so.FindProperty("m_CurrentQuality");
                var currentName = NameAt(current.intValue);
                var map = so.FindProperty("m_PerPlatformDefaultQuality");
                var defaults = new List<string>();
                for (var k = 0; k < map.arraySize; k++) defaults.Add(NameAt(map.GetArrayElementAtIndex(k).FindPropertyRelative("second").intValue));

                for (var i = 0; i < names.Count; i++)
                {
                    var j = -1;
                    for (var k = i; k < list.arraySize && j < 0; k++)
                        if (list.GetArrayElementAtIndex(k).FindPropertyRelative("name").stringValue == names[i]) j = k;
                    if (j > i) list.MoveArrayElement(j, i);
                    else if (j < 0)
                    {
                        // A copy of the level before it (the element at 'from' is duplicated into 'from' + 1).
                        list.InsertArrayElementAtIndex(Math.Max(0, i - 1));
                        list.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue = names[i];
                    }
                }
                while (list.arraySize > names.Count) list.DeleteArrayElementAtIndex(list.arraySize - 1);
                current.intValue = Math.Max(0, names.IndexOf(currentName));
                for (var k = 0; k < map.arraySize; k++) map.GetArrayElementAtIndex(k).FindPropertyRelative("second").intValue = Math.Max(0, names.IndexOf(defaults[k]));
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        /// <summary>The platform names the quality settings know (the keys of their per-platform default levels).</summary>
        static List<string> PlatformNames(Object qs)
        {
            var names = new List<string>();
            using (var so = new SerializedObject(qs))
            {
                var map = so.FindProperty("m_PerPlatformDefaultQuality");
                for (var k = 0; k < map.arraySize; k++) names.Add(map.GetArrayElementAtIndex(k).FindPropertyRelative("first").stringValue);
            }
            return names;
        }

        static string[] KnownPlatforms(List<string> known, string[] platforms)
        {
            foreach (var p in platforms)
                if (!known.Contains(p)) throw new ArgumentException($"no platform '{p}' in the quality settings (platforms: {string.Join(", ", known)})");
            return platforms;
        }

        static SerializedProperty DefaultEntry(SerializedObject so, string platform)
        {
            var map = so.FindProperty("m_PerPlatformDefaultQuality");
            for (var k = 0; k < map.arraySize; k++)
            {
                var e = map.GetArrayElementAtIndex(k);
                if (e.FindPropertyRelative("first").stringValue == platform) return e.FindPropertyRelative("second");
            }
            return null;
        }

        static string DefaultLevel(Object qs, string platform)
        {
            using (var so = new SerializedObject(qs))
            {
                var e = DefaultEntry(so, platform);
                if (e == null) return "none";
                var names = LevelNames(so.FindProperty("m_QualitySettings"));
                return e.intValue >= 0 && e.intValue < names.Count ? names[e.intValue] : e.intValue.ToString(CultureInfo.InvariantCulture);
            }
        }

        static void SetDefaultLevel(Object qs, string platform, int index)
        {
            using (var so = new SerializedObject(qs))
            {
                DefaultEntry(so, platform).intValue = index;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        static string CurrentLevelName()
        {
            var i = QualitySettings.GetQualityLevel();
            var names = QualitySettings.names;
            return i >= 0 && i < names.Length ? names[i] : i.ToString(CultureInfo.InvariantCulture);
        }

        // ---- After the steps ----------------------------------------------------------------------------

        [Serializable]
        sealed class Snapshot
        {
            public List<string> keys = new List<string>();
            public List<string> values = new List<string>();
            public List<FileRun> files = new List<FileRun>();
        }

        /// <summary>The contents of a ProjectSettings file from which only builds led to it (git blob ids, the current one last).</summary>
        [Serializable]
        sealed class FileRun
        {
            public string path;
            public List<string> ids = new List<string>();
        }

        static string SnapshotPath => HarnessPaths.Combine(HarnessPaths.StateDir, "project-settings.json");

        static Snapshot ReadSnapshot()
        {
            try { return File.Exists(SnapshotPath) ? JsonUtility.FromJson<Snapshot>(File.ReadAllText(SnapshotPath)) : null; }
            catch (Exception) { return null; }
        }

        /// <summary>What the previous build left in each owned setting.</summary>
        Dictionary<string, string> Last
        {
            get
            {
                if (m_Last != null) return m_Last;
                m_Last = new Dictionary<string, string>(StringComparer.Ordinal);
                var s = ReadSnapshot();
                for (var i = 0; s != null && i < s.keys.Count && i < s.values.Count; i++) m_Last[s.keys[i]] = s.values[i];
                return m_Last;
            }
        }

        /// <summary>
        /// After every settings step succeeded: clear the layers and tags nobody declared and save the settings this run changed.
        /// </summary>
        internal void FinishProject()
        {
            Module = "harness";
            if (m_Layers != null)
            {
                var tm = SettingsObject("TagManager");
                for (var i = 3; i < 32; i++)
                {
                    if ((i < s_BuiltinLayers.Length && s_BuiltinLayers[i] != null) || m_Layers.Exists(l => l.index == i)) continue;
                    Serialized($"Layer[{i}]", tm, $"layers.Array.data[{i}]", "", fix: $"The user layers are the ones settings steps declare: to keep one, ctx.Layer({i}, \"<name>\")");
                }
            }
            if (m_Tags != null) Serialized("Tags", SettingsObject("TagManager"), "tags", m_Tags, fix: "The tags are the ones settings steps declare (ctx.Tag)");
            if (m_ToSave.Count > 0)
            {
                foreach (var o in m_ToSave) EditorUtility.SetDirty(o);
                AssetDatabase.SaveAssets();
            }
        }

        // ---- Which file contents are the settings steps' output (G5-6) --------------------------------------------------
        //
        // A ProjectSettings file holds the settings of every module's step, and parallel worktrees land into one Editor tree. So
        // that tools/land.ps1 can tell what the settings steps wrote on top of the committed file (the loop after a merge writes it
        // again from the merged code) from somebody's edit, each run records per ProjectSettings/*.asset the contents from which
        // only builds led to the file as it is. Changed any other way - the YAML edited, a settings window's unsaved change that
        // this run's save would write - the record starts again from the file as the run found it.

        const int MaxRun = 64;

        // Blob ids by path for a file length and write time (opening ~25 files twice per build cost ~5 ms).
        static readonly Dictionary<string, (long length, long ticks, string id)> s_Ids = new Dictionary<string, (long, long, string)>(StringComparer.Ordinal);

        /// <summary>Git blob id of each ProjectSettings/*.asset now; null when the Editor holds unsaved changes to it.</summary>
        internal static Dictionary<string, string> ProjectFileIds()
        {
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var f in new DirectoryInfo(HarnessPaths.Combine(HarnessPaths.ProjectRoot, "ProjectSettings")).GetFiles("*.asset"))
            {
                var path = "ProjectSettings/" + f.Name;
                if (Unsaved(path)) { ids[path] = null; continue; }
                var ticks = f.LastWriteTimeUtc.Ticks;
                if (!s_Ids.TryGetValue(path, out var c) || c.length != f.Length || c.ticks != ticks)
                    s_Ids[path] = c = (f.Length, ticks, BlobId(File.ReadAllBytes(f.FullName)));
                ids[path] = c.id;
            }
            return ids;
        }

        static bool Unsaved(string path)
        {
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o != null && EditorUtility.IsDirty(o)) return true;
            return false;
        }

        /// <summary>The id git gives the file's content: text (no NUL in the first 8000 bytes) with LF line ends, as text=auto stores it.</summary>
        static string BlobId(byte[] bytes)
        {
            if (Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8000)) < 0 && Array.IndexOf(bytes, (byte)'\r') >= 0)
            {
                var lf = new List<byte>(bytes.Length);
                for (var i = 0; i < bytes.Length; i++)
                    if (bytes[i] != '\r' || i + 1 >= bytes.Length || bytes[i + 1] != '\n') lf.Add(bytes[i]);
                bytes = lf.ToArray();
            }
            using (var sha = SHA1.Create())
            {
                var header = Encoding.ASCII.GetBytes("blob " + bytes.Length.ToString(CultureInfo.InvariantCulture) + "\0");
                sha.TransformBlock(header, 0, header.Length, null, 0);
                sha.TransformFinalBlock(bytes, 0, bytes.Length);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// After every settings step succeeded and the settings were saved: remember what each owned setting is now (the next build
        /// tells a change outside the code from a change of the code) and, per ProjectSettings file, the contents from which only
        /// builds led to it. <paramref name="before"/>: <see cref="ProjectFileIds"/> before the first step.
        /// </summary>
        internal void SaveSnapshot(Dictionary<string, string> before)
        {
            var previous = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var last = ReadSnapshot();
            if (last?.files != null)
                foreach (var r in last.files)
                    if (r?.path != null && r.ids != null && r.ids.Count > 0) previous[r.path] = r.ids;

            var snapshot = new Snapshot();
            var keys = new List<string>(m_Owned.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys)
            {
                snapshot.keys.Add(k);
                snapshot.values.Add(m_Owned[k].actual);
            }
            var now = ProjectFileIds();
            var paths = new List<string>(now.Keys);
            paths.Sort(StringComparer.Ordinal);
            foreach (var path in paths)
            {
                var id = now[path];
                if (id == null) continue;   // unsaved changes after the save: nothing to tell
                before.TryGetValue(path, out var start);
                previous.TryGetValue(path, out var run);
                var ids = new List<string>();
                // The file was as the previous run left it, or this run made it that again (it undid whatever changed it in
                // between): the earlier contents still led here only through builds, and so did the one this run started from.
                if (run != null && (start == run[run.Count - 1] || id == run[run.Count - 1])) ids.AddRange(run);
                if (start != null && !ids.Contains(start)) ids.Add(start);
                ids.Remove(id);
                ids.Add(id);
                if (ids.Count > MaxRun) ids.RemoveRange(0, ids.Count - MaxRun);
                snapshot.files.Add(new FileRun { path = path, ids = ids });
            }
            Directory.CreateDirectory(HarnessPaths.StateDir);
            HarnessPaths.WriteStateFile(SnapshotPath, JsonUtility.ToJson(snapshot, true));
        }

        /// <summary>Owned project settings for build.fingerprint ("key=value", sorted).</summary>
        void ProjectFingerprint(StringBuilder sb)
        {
            if (m_Owned.Count == 0) return;
            sb.Append("--project--\n");
            var keys = new List<string>(m_Owned.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var k in keys) sb.Append(k).Append('=').Append(m_Owned[k].actual).Append('\n');
        }
    }
}
