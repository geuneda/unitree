using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Harness.Editor
{
    /// <summary>
    /// Everything a build step needs to create scene content and persistent assets.
    /// Assets go to Assets/Generated/&lt;Module&gt;/ and are overwritten in place (GUIDs stay stable), so rebuilding is
    /// idempotent. Assets no step touched during a build are deleted afterwards.
    /// </summary>
    public sealed class BuildContext
    {
        public Scene Scene { get; }

        /// <summary>Module of the step currently running (folder name under Assets/Game).</summary>
        public string Module { get; internal set; }

        internal readonly HashSet<string> TouchedAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal readonly List<string> Warnings = new List<string>();
        internal int CreatedObjects;

        internal BuildContext(Scene scene, bool useCache) { Scene = scene; m_UseCache = useCache; m_Cache = BuildCache.Load(); }

        // ---- Build cache ---------------------------------------------------------------------------

        readonly bool m_UseCache;
        readonly BuildCache m_Cache;
        internal readonly Dictionary<string, string> PendingCacheKeys = new Dictionary<string, string>();
        internal System.Reflection.Assembly StepAssembly { get; set; }

        /// <summary>
        /// Skip expensive generation when nothing that could change its output changed. Returns true (and keeps the
        /// listed assets alive) when the assets exist and the key matches; otherwise generate and save them yourself.
        /// Key = <paramref name="name"/> + <paramref name="inputs"/> + compiled content of this step's assembly and of
        /// Harness.Runtime (deterministic compile: MVID changes iff code changes). Pass anything else the output depends
        /// on (e.g. a file hash) in <paramref name="inputs"/>. harness_build --no_cache ignores it.
        /// </summary>
        public bool CacheHit(string name, string[] relativeAssets, params object[] inputs)
        {
            var parts = new List<string> { Module, name, StepAssembly?.ManifestModule.ModuleVersionId.ToString(), typeof(ShotPreset).Assembly.ManifestModule.ModuleVersionId.ToString() };
            foreach (var i in inputs) parts.Add(Convert.ToString(i, System.Globalization.CultureInfo.InvariantCulture));
            var key = string.Join("|", parts);
            var cacheName = Module + "/" + name;
            var paths = Array.ConvertAll(relativeAssets, AssetPath);
            var allExist = Array.TrueForAll(paths, p => AssetDatabase.LoadMainAssetAtPath(p) != null);
            if (m_UseCache && allExist && m_Cache.Get(cacheName) == key)
            {
                foreach (var p in paths) TouchedAssets.Add(p);
                CacheHits++;
                return true;
            }
            PendingCacheKeys[cacheName] = key; // committed only if the whole build succeeds
            return false;
        }

        /// <summary>Load a generated asset of this module (Assets/Generated/&lt;Module&gt;/&lt;relative&gt;).</summary>
        public T LoadAsset<T>(string relative) where T : Object
        {
            var p = AssetPath(relative);
            var a = AssetDatabase.LoadAssetAtPath<T>(p);
            if (a == null) throw new InvalidOperationException($"Generated asset missing: {p}");
            TouchedAssets.Add(p);
            return a;
        }

        internal int CacheHits;

        internal void CommitCache()
        {
            foreach (var kv in PendingCacheKeys) m_Cache.Set(kv.Key, kv.Value);
            m_Cache.Save();
        }

        public void Warn(string message) => Warnings.Add($"[{Module}] {message}");

        /// <summary>Stable seed derived from the module name and a salt (never use UnityEngine.Random / System.Random without a seed).</summary>
        public int Seed(string salt = "")
        {
            unchecked
            {
                var h = 2166136261u;
                foreach (var ch in Module + "/" + salt) { h ^= ch; h *= 16777619u; }
                return (int)h;
            }
        }

        // ---- Scene objects -------------------------------------------------------------------------

        /// <summary>The module's scene root (a root GameObject named after the module). Created on first use.</summary>
        public GameObject Root(string module = null)
        {
            module = module ?? Module;
            foreach (var go in Scene.GetRootGameObjects())
                if (go.name == module) return go;
            var root = new GameObject(module);
            SceneManager.MoveGameObjectToScene(root, Scene);
            CreatedObjects++;
            return root;
        }

        /// <summary>Create (or return) a GameObject at "A/B/C" under the module root, adding missing components.</summary>
        public GameObject Create(string path, params Type[] components)
        {
            var parent = Root().transform;
            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            Transform t = parent;
            foreach (var part in parts)
            {
                var child = t.Find(part);
                if (child == null)
                {
                    child = new GameObject(part).transform;
                    child.SetParent(t, false);
                    CreatedObjects++;
                }
                t = child;
            }
            foreach (var c in components)
                if (t.GetComponent(c) == null) t.gameObject.AddComponent(c);
            return t.gameObject;
        }

        /// <summary>GameObject with MeshFilter + MeshRenderer.</summary>
        public GameObject MeshObject(string path, Mesh mesh, Material material, bool castShadows = true)
        {
            var go = Create(path, typeof(MeshFilter), typeof(MeshRenderer));
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A named capture pose (<see cref="ShotPreset"/>) under &lt;Module&gt;/Shots/.</summary>
        public ShotPreset Shot(string name, Vector3 position, Vector3 lookAt, float fov = 60f)
        {
            var go = Create("Shots/" + name, typeof(ShotPreset));
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation((lookAt - position).normalized, Vector3.up);
            var sp = go.GetComponent<ShotPreset>();
            sp.presetName = name;
            sp.fieldOfView = fov;
            return sp;
        }

        // ---- Persistent assets --------------------------------------------------------------------

        /// <summary>Assets/Generated/&lt;Module&gt;/&lt;relative&gt;.</summary>
        public string AssetPath(string relative) => $"{HarnessPaths.GeneratedRoot}/{Module}/{relative}".Replace('\\', '/');

        /// <summary>
        /// Persist <paramref name="obj"/> at Assets/Generated/&lt;Module&gt;/&lt;relative&gt; (".asset" if no extension).
        /// If an asset of the same type exists there, its contents are replaced in place and the existing object is returned.
        /// </summary>
        public T SaveAsset<T>(T obj, string relative) where T : Object
        {
            if (string.IsNullOrEmpty(Path.GetExtension(relative))) relative += ".asset";
            var path = AssetPath(relative);
            EnsureFolder(Path.GetDirectoryName(path));
            TouchedAssets.Add(path);
            obj.name = Path.GetFileNameWithoutExtension(path);

            var existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null && existing.GetType() == obj.GetType())
            {
                EditorUtility.CopySerialized(obj, existing);
                existing.name = obj.name;
                EditorUtility.SetDirty(existing);
                if (!EditorUtility.IsPersistent(obj)) Object.DestroyImmediate(obj);
                return (T)existing;
            }
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }

        public Material Material(string name, string shaderName, Action<Material> setup = null)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new ArgumentException($"Shader '{shaderName}' not found (for material '{name}').");
            return Material(name, shader, setup);
        }

        public Material Material(string name, Shader shader, Action<Material> setup = null)
        {
            var m = new Material(shader);
            setup?.Invoke(m);
            return SaveAsset(m, name + ".mat");
        }

        public Mesh SaveMesh(Mesh mesh, string name) => SaveAsset(mesh, name + ".asset");

        /// <summary>
        /// Write <paramref name="tex"/> as a PNG (viewable by agents) and import it with matching settings.
        /// The in-memory texture is destroyed; use the returned asset.
        /// </summary>
        public Texture2D SaveTexture(Texture2D tex, string name, bool sRGB = true, bool normalMap = false)
        {
            var path = AssetPath(name + ".png");
            EnsureFolder(Path.GetDirectoryName(path));
            TouchedAssets.Add(path);
            var bytes = tex.EncodeToPNG();
            var full = HarnessPaths.Combine(HarnessPaths.ProjectRoot, path);
            var wrap = tex.wrapMode; var filter = tex.filterMode; var mips = tex.mipmapCount > 1; var aniso = tex.anisoLevel;
            Object.DestroyImmediate(tex);

            var changed = !File.Exists(full) || !BytesEqual(File.ReadAllBytes(full), bytes);
            if (changed)
            {
                File.WriteAllBytes(full, bytes);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            }
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            if (imp == null) throw new InvalidOperationException($"Texture import failed for {path}");
            var dirty = false;
            void Set<TV>(TV current, TV wanted, Action<TV> apply) { if (!EqualityComparer<TV>.Default.Equals(current, wanted)) { apply(wanted); dirty = true; } }
            Set(imp.textureType, normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default, v => imp.textureType = v);
            Set(imp.sRGBTexture, sRGB && !normalMap, v => imp.sRGBTexture = v);
            Set(imp.wrapMode, wrap, v => imp.wrapMode = v);
            Set(imp.filterMode, filter, v => imp.filterMode = v);
            Set(imp.mipmapEnabled, mips, v => imp.mipmapEnabled = v);
            Set(imp.anisoLevel, aniso, v => imp.anisoLevel = v);
            Set(imp.textureCompression, TextureImporterCompression.CompressedHQ, v => imp.textureCompression = v);
            if (dirty) imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Create or rebuild a VolumeProfile asset. <paramref name="configure"/> adds overrides, e.g.
        /// <c>p.Add&lt;Bloom&gt;(true).intensity.value = 0.8f;</c> — components from previous builds are removed first.
        /// </summary>
        public VolumeProfile VolumeProfile(string name, Action<VolumeProfile> configure)
        {
            var path = AssetPath(name + ".asset");
            EnsureFolder(Path.GetDirectoryName(path));
            TouchedAssets.Add(path);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            foreach (var c in profile.components.ToArray())
            {
                if (c == null) continue;
                AssetDatabase.RemoveObjectFromAsset(c);
                Object.DestroyImmediate(c, true);
            }
            profile.components.Clear();
            configure(profile);
            foreach (var c in profile.components)
            {
                c.name = c.GetType().Name;
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            EditorUtility.SetDirty(profile);
            return profile;
        }

        /// <summary>
        /// Render the current RenderSettings.skybox into a cubemap asset and use it as the default reflection
        /// (no lighting bake needed). Call after the skybox is set.
        /// </summary>
        public Cubemap BakeSkyReflection(string name = "SkyReflection", int size = 128)
        {
            var go = new GameObject("[HarnessSkyBake]") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.cullingMask = 0;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.allowHDR = true;
                var cube = new Cubemap(size, TextureFormat.RGBAHalf, true);
                if (!cam.RenderToCubemap(cube))
                {
                    Warn("Camera.RenderToCubemap failed; default reflection left unset");
                    Object.DestroyImmediate(cube);
                    return null;
                }
                cube.Apply(true);
                cube = SaveAsset(cube, name + ".asset");
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
                RenderSettings.reflectionIntensity = 1f;
                return cube;
            }
            finally { Object.DestroyImmediate(go); }
        }

        public const string DefaultThemePath = "Assets/Harness/UI/DefaultRuntimeTheme.tss";

        /// <summary>
        /// UI Toolkit screen overlay from a UXML file (text-first UI). Creates a generated PanelSettings asset
        /// (default runtime theme, scale-with-screen 1920x1080) and a UIDocument at <paramref name="path"/>.
        /// </summary>
        public UnityEngine.UIElements.UIDocument UIDocument(string path, string uxmlPath, int sortingOrder = 0)
        {
            var uxml = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>(uxmlPath);
            if (uxml == null) throw new ArgumentException($"UXML not found: {uxmlPath}");
            var theme = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.ThemeStyleSheet>(DefaultThemePath);
            if (theme == null) throw new InvalidOperationException($"Theme not found: {DefaultThemePath}");

            var ps = ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
            ps.themeStyleSheet = theme;
            ps.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.match = 0.5f;
            ps.sortingOrder = sortingOrder;
            ps = SaveAsset(ps, Path.GetFileNameWithoutExtension(uxmlPath) + "Panel.asset");

            var go = Create(path, typeof(UnityEngine.UIElements.UIDocument));
            var doc = go.GetComponent<UnityEngine.UIElements.UIDocument>();
            doc.panelSettings = ps;
            doc.visualTreeAsset = uxml;
            return doc;
        }

        internal static void EnsureFolder(string folder)
        {
            folder = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(folder)) return;
            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }

        static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
