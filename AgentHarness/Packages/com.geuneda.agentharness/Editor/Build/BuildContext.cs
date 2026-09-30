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
    /// Assets go to &lt;generatedRoot&gt;/&lt;Module&gt;/ (ProjectSettings/AgentHarness.json; the sample: Assets/Generated) and are
    /// overwritten in place (GUIDs stay stable), so rebuilding is idempotent. Assets no step touched during a build are
    /// deleted afterwards.
    /// </summary>
    public sealed partial class BuildContext
    {
        public Scene Scene { get; }

        /// <summary>Module of the step currently running (its folder name under a module root, or its configured name).</summary>
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
        /// Harness.Runtime (deterministic compile: MVID changes iff code changes) + the source of the module's shaders and of the
        /// harness shader includes (a GPU bake's shader changed). Pass anything else the output depends on (e.g. a file hash) in
        /// <paramref name="inputs"/>. harness_build --no_cache ignores it.
        /// </summary>
        public bool CacheHit(string name, string[] relativeAssets, params object[] inputs)
        {
            var parts = new List<string> { Module, name, StepAssembly?.ManifestModule.ModuleVersionId.ToString(), typeof(ShotPreset).Assembly.ManifestModule.ModuleVersionId.ToString(), ShadersHash() };
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

        readonly Dictionary<string, string> m_ShadersHash = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Source hash of this module's shaders (.shader/.hlsl/.cginc/.compute) and the harness's Shaders/ includes, once per build.</summary>
        string ShadersHash()
        {
            if (m_ShadersHash.TryGetValue(Module ?? "", out var h)) return h;
            var folder = ModuleFolder();
            h = (folder == null ? "none" : SourcesHash(folder, ".shader", ".hlsl", ".cginc", ".compute")) + "+" + SourcesHash(HarnessConfig.PackageRoot + "/Shaders", ".hlsl");
            return m_ShadersHash[Module ?? ""] = h;
        }

        /// <summary>The folder of the module whose step is running (ProjectSettings/AgentHarness.json), or null.</summary>
        internal string ModuleFolder()
        {
            var config = HarnessPaths.Config;
            foreach (var m in config.modules) if (m.name == Module) return m.path;
            foreach (var root in config.moduleRoots) if (AssetDatabase.IsValidFolder(root + "/" + Module)) return root + "/" + Module;
            return null;
        }

        internal void CommitCache()
        {
            foreach (var kv in PendingCacheKeys) m_Cache.Set(kv.Key, kv.Value);
            m_Cache.Save();
        }

        public void Warn(string message) => Warnings.Add($"[{Module}] {message}");

        /// <summary>After every step ran (the scene is complete): checks that need all of it.</summary>
        internal void AfterSteps()
        {
#if AGENTHARNESS_ANIMATION
            CheckAnimations();
#endif
#if AGENTHARNESS_URP
            CheckDecals();
#endif
        }

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
            return SaveAssetAt(obj, AssetPath(relative));
        }

        T SaveAssetAt<T>(T obj, string path) where T : Object
        {
            EnsureFolder(Path.GetDirectoryName(path));
            TouchedAssets.Add(path);
            obj.name = Path.GetFileNameWithoutExtension(path);

            var existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null && existing.GetType() == obj.GetType())
            {
                // CopySerialized replaces a mesh's or cubemap's serialized data but not the data the engine draws from: the next
                // play mode and captures drew the previous geometry (the first loop after a change showed the old rocks; the
                // loop after it the new ones). Those go through their own API.
                if (obj is Mesh srcMesh) CopyMesh(srcMesh, (Mesh)(Object)existing);
                else if (obj is Cubemap srcCube && srcCube.isReadable) CopyCubemap(srcCube, (Cubemap)(Object)existing);
                else EditorUtility.CopySerialized(obj, existing);
                existing.name = obj.name;
                EditorUtility.SetDirty(existing);
                if (!EditorUtility.IsPersistent(obj)) Object.DestroyImmediate(obj);
                return (T)existing;
            }
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(obj, path);
            return obj;
        }

        /// <summary>Replace <paramref name="dst"/>'s geometry with <paramref name="src"/>'s through the Mesh API (so it is drawn at once).</summary>
        static void CopyMesh(Mesh src, Mesh dst)
        {
            dst.Clear();
            dst.indexFormat = src.indexFormat;
            dst.SetVertices(src.vertices);
            if (src.HasVertexAttribute(VertexAttribute.Normal)) dst.SetNormals(src.normals);
            if (src.HasVertexAttribute(VertexAttribute.Tangent)) dst.SetTangents(src.tangents);
            if (src.HasVertexAttribute(VertexAttribute.Color)) dst.SetColors(src.colors);
            var uvs = new List<Vector4>();
            for (var ch = 0; ch < 8; ch++)
            {
                if (!src.HasVertexAttribute(VertexAttribute.TexCoord0 + ch)) continue;
                src.GetUVs(ch, uvs);
                dst.SetUVs(ch, uvs);
            }
            dst.subMeshCount = src.subMeshCount;
            for (var s = 0; s < src.subMeshCount; s++) dst.SetIndices(src.GetIndices(s), src.GetTopology(s), s, false);
            dst.bounds = src.bounds;
        }

        /// <summary>Replace <paramref name="dst"/>'s pixels (every face and mip) with <paramref name="src"/>'s and upload them.</summary>
        static void CopyCubemap(Cubemap src, Cubemap dst)
        {
            if (dst.width != src.width || dst.format != src.format || dst.mipmapCount != src.mipmapCount || !dst.isReadable)
            {
                EditorUtility.CopySerialized(src, dst);   // a different layout: nothing to copy into
                return;
            }
            for (var face = 0; face < 6; face++)
            for (var mip = 0; mip < src.mipmapCount; mip++)
                dst.SetPixelData(src.GetPixelData<byte>(mip, (CubemapFace)face), mip, (CubemapFace)face);
            dst.Apply(false, false);
        }

        public Material Material(string name, string shaderName, Action<Material> setup = null)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new ArgumentException($"Shader '{shaderName}' not found (for material '{name}').");
            return Material(name, shader, setup);
        }

        /// <summary>
        /// A material from <paramref name="shader"/>: <paramref name="setup"/> sets properties, then the shader's own validation
        /// derives keywords, queue and blend state (<see cref="ValidateMaterial"/>). Properties the shader does not have, values the
        /// validation replaced and an emission color with emission off are reported in build warnings. URP Lit: <see cref="LitMaterial"/>.
        /// </summary>
        public Material Material(string name, Shader shader, Action<Material> setup = null)
        {
            var m = new Material(shader);
            var defaults = MaterialValues(m);
            setup?.Invoke(m);
            var set = MaterialValues(m);
            ValidateMaterial(m);
            CheckMaterial(name, m, defaults, set);
            return SaveAsset(m, name + ".mat");
        }

        /// <summary>
        /// Run the shader's ShaderGUI.ValidateMaterial (what the Inspector does on every change). URP Lit derives
        /// keywords (_NORMALMAP, _OCCLUSIONMAP, _ALPHATEST_ON, _SURFACE_TYPE_TRANSPARENT, ...), the queue, the RenderType tag,
        /// disabled passes and legacy _Color/_MainTex from its properties there.
        /// Without it the first build (URP's import postprocessor validates new .mat files) and later in-place
        /// overwrites (no postprocessor) produce different materials.
        /// </summary>
        public static void ValidateMaterial(Material m)
        {
            var editor = (MaterialEditor)UnityEditor.Editor.CreateEditor(m);
            try { editor.customShaderGUI?.ValidateMaterial(m); }
            finally { Object.DestroyImmediate(editor); }
        }

        public Mesh SaveMesh(Mesh mesh, string name) => SaveAsset(mesh, name + ".asset");

        /// <summary>
        /// Write <paramref name="tex"/> as a PNG (viewable by agents) and import it with matching settings.
        /// The in-memory texture is destroyed; use the returned asset.
        /// </summary>
        public Texture2D SaveTexture(Texture2D tex, string name, bool sRGB = true, bool normalMap = false) => SaveTexture(tex, name, sRGB, normalMap, null);

        /// <param name="gpuKey">A GPU bake's input hash (<see cref="BakeTexture"/>): kept in the importer's userData, where the fingerprint reads it instead of the PNG.</param>
        internal Texture2D SaveTexture(Texture2D tex, string name, bool sRGB, bool normalMap, string gpuKey)
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
            Set(imp.userData ?? "", gpuKey ?? "", v => imp.userData = v);
            if (dirty) imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

#if AGENTHARNESS_RP_CORE
        /// <summary>
        /// Create or rebuild a VolumeProfile asset (needs a Scriptable Render Pipeline: com.unity.render-pipelines.core). <paramref name="configure"/> adds overrides, e.g.
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
#endif

        /// <summary>
        /// Render the current RenderSettings.skybox into a cubemap asset and use it as the default reflection
        /// (no lighting bake needed). Call after the skybox is set.
        /// </summary>
        public Cubemap BakeSkyReflection(string name = "SkyReflection", int size = 128)
        {
            var go = new GameObject("[HarnessSkyBake]") { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture rt = null;
            try
            {
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.cullingMask = 0;
                cam.clearFlags = CameraClearFlags.Skybox;
                cam.allowHDR = true;
                // Render into a cube render texture and read the faces back. Camera.RenderToCubemap(Cubemap) renders on the GPU,
                // but in Unity 6.6 it leaves the Cubemap's pixel data uninitialized - and that is what gets saved (P-4: the next
                // load reflected garbage, often negative, and every URP Lit surface went black).
                rt = new RenderTexture(new RenderTextureDescriptor(size, size, RenderTextureFormat.ARGBHalf, 0) { dimension = TextureDimension.Cube });
                if (!SystemInfo.supportsAsyncGPUReadback || !cam.RenderToCubemap(rt))
                {
                    Warn("rendering the sky into a cubemap failed; default reflection left unset");
                    return null;
                }
                var cube = new Cubemap(size, TextureFormat.RGBAHalf, true);
                for (var face = 0; face < 6; face++)
                {
                    var request = AsyncGPUReadback.Request(rt, 0, 0, size, 0, size, face, 1, TextureFormat.RGBAHalf);
                    request.WaitForCompletion();
                    if (request.hasError)
                    {
                        Warn("reading the sky cubemap back from the GPU failed; default reflection left unset");
                        Object.DestroyImmediate(cube);
                        return null;
                    }
                    cube.SetPixelData(request.GetData<byte>(), 0, (CubemapFace)face);
                }
                cube.Apply(true);
                cube = SaveAsset(cube, name + ".asset");
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cube;
                RenderSettings.reflectionIntensity = 1f;
                return cube;
            }
            finally
            {
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// Ambient light from the sky without a lighting bake (G4-2): projects <paramref name="sky"/> (e.g. BakeSkyReflection's
        /// cubemap) to L2 spherical harmonics (<see cref="Harness.Procedural.AmbientProbe"/>) and stores them as the scene's
        /// lighting data with ambient mode Skybox - what baking the environment lighting would store. <paramref name="intensity"/>
        /// scales the probe.
        /// </summary>
        public UnityEngine.Rendering.SphericalHarmonicsL2 SkyAmbient(Cubemap sky, float intensity = 1f)
        {
            var sh = Harness.Procedural.AmbientProbe.FromCubemap(sky) * intensity;
            // The lighting data is written once the scene is saved (SaveLightingData): made for an unsaved scene, it loads with
            // an "incompatible ... scene was not serialized" warning every time.
            m_Ambient = sh;
            m_AmbientPath = AssetPath("LightingData.asset");
            TouchedAssets.Add(m_AmbientPath);
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            return sh;
        }

        UnityEngine.Rendering.SphericalHarmonicsL2? m_Ambient;
        string m_AmbientPath;

        /// <summary>After the scene is saved: its lighting data with the <see cref="SkyAmbient"/> probe. True when the scene needs saving again.</summary>
        internal bool SaveLightingData()
        {
            if (m_Ambient == null) return false;
            var data = new LightingDataAsset(Scene);
            data.SetAmbientProbe(m_Ambient.Value);
            data = SaveAssetAt(data, m_AmbientPath);
            AssetDatabase.SaveAssetIfDirty(data);
            if (!HarnessPaths.Config.IsHarnessProject) HarnessBuild.MarkGenerated(m_AmbientPath);
            Lightmapping.SetLightingDataAssetForScene(Scene, data);
            return true;
        }

        public const string DefaultThemePath = HarnessConfig.PackageRoot + "/UI/DefaultRuntimeTheme.tss";

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

            var ps = CreatePanelSettings(theme);
            ps.themeStyleSheet = theme;
            ps.scaleMode = UnityEngine.UIElements.PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.match = 0.5f;
            // 6.6 starts a new PanelSettings at the DPI of the screen the Editor is on (144 on a 150% display, 96 headless):
            // the asset, and the fingerprint, would follow the display. Scale-with-screen-size does not use it.
            ps.referenceDpi = 96f;
            ps.fallbackDpi = 96f;
            ps.sortingOrder = sortingOrder;
            ps = SaveAsset(ps, Path.GetFileNameWithoutExtension(uxmlPath) + "Panel.asset");

            var go = Create(path, typeof(UnityEngine.UIElements.UIDocument));
            var doc = go.GetComponent<UnityEngine.UIElements.UIDocument>();
            doc.panelSettings = ps;
            doc.visualTreeAsset = uxml;
            return doc;
        }

        const string UnityDefaultThemeFolder = "Assets/UI Toolkit";

        /// <summary>
        /// A new PanelSettings in the Editor gets a theme from PanelSettings.GetOrCreateDefaultTheme, which writes
        /// Assets/UI Toolkit/UnityThemes/UnityDefaultRuntimeTheme.tss when no theme is under Assets/ (the harness theme is in
        /// its package). Point that hook at <paramref name="theme"/> while creating, and remove the file if Unity wrote it
        /// anyway (the hook is internal and may change), so building never adds an asset the project did not have.
        /// </summary>
        static UnityEngine.UIElements.PanelSettings CreatePanelSettings(UnityEngine.UIElements.ThemeStyleSheet theme)
        {
            var hadFolder = AssetDatabase.IsValidFolder(UnityDefaultThemeFolder);
            var hook = typeof(UnityEngine.UIElements.PanelSettings).GetField("GetOrCreateDefaultTheme",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            var previous = hook != null && hook.FieldType == typeof(Func<UnityEngine.UIElements.ThemeStyleSheet>) ? hook.GetValue(null) : null;
            try
            {
                if (previous != null) hook.SetValue(null, (Func<UnityEngine.UIElements.ThemeStyleSheet>)(() => theme));
                return ScriptableObject.CreateInstance<UnityEngine.UIElements.PanelSettings>();
            }
            finally
            {
                if (previous != null) hook.SetValue(null, previous);
                if (!hadFolder && AssetDatabase.IsValidFolder(UnityDefaultThemeFolder)) AssetDatabase.DeleteAsset(UnityDefaultThemeFolder);
            }
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
