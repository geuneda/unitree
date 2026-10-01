using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Harness.Editor
{
    /// <summary>
    /// The harness sky (<see cref="BuildContext.Sky"/>). Colors are set like Material.SetColor (the values the Inspector shows);
    /// the sky is HDR, so the sun's disk and the clouds near it go above 1 and bloom.
    /// </summary>
    public sealed class SkySettings
    {
        public Color Zenith = new Color(0.2f, 0.3f, 0.53f);
        public Color Horizon = new Color(0.78f, 0.88f, 0.97f);
        /// <summary>Below the horizon (the world's edge; close to the fog color dissolves it).</summary>
        public Color Ground = new Color(0.58f, 0.61f, 0.61f);
        /// <summary>How fast the horizon haze thins out with height (higher = a thinner band).</summary>
        public float HorizonFalloff = 6f;
        /// <summary>Null: the sun light's color.</summary>
        public Color? SunColor;
        /// <summary>Angular radius of the sun's disk, degrees.</summary>
        public float SunSize = 1f;
        public float SunIntensity = 40f;
        public float SunGlow = 0.6f;
        /// <summary>0 = clear sky, 1 = overcast.</summary>
        public float CloudCoverage = 0.45f;
        /// <summary>Higher = harder cloud edges and more opaque centers.</summary>
        public float CloudSharpness = 5f;
        /// <summary>Noise cells per sky unit: higher = smaller clouds.</summary>
        public float CloudScale = 2f;
        public float CloudOpacity = 0.95f;
        public Color CloudColor = Color.white;
        public Color CloudShadow = new Color(0.68f, 0.72f, 0.8f);
        /// <summary>Cloud drift in sky units per second of game time (SkyClock).</summary>
        public Vector2 Wind = new Vector2(0.012f, 0.005f);
        public int CloudOctaves = 6;
        /// <summary>Null: ctx.Seed("sky").</summary>
        public int? Seed;
        public float Exposure = 1f;
    }

    public sealed partial class BuildContext
    {
        public const string SkyShader = "Harness/Sky";
        const string CubeFilterShader = "Hidden/Harness/CubeFilter";

        /// <summary>
        /// The harness sky as the scene's skybox (Shaders/HarnessSky.shader): a horizon-to-zenith gradient, the sun's disk and glow in
        /// the direction <paramref name="sun"/> shines from, and fBm clouds (the harness noise) drifting with game time - a
        /// <see cref="SkyClock"/> on &lt;Module&gt;/Sky drives them, so a fixed-step scenario sees the same clouds at the same t.
        /// Sets RenderSettings.skybox and .sun. Call <see cref="BakeSkyReflection"/> after it for reflections and ambient light.
        /// </summary>
        public Material Sky(Light sun, Action<SkySettings> setup = null)
        {
            var s = new SkySettings();
            setup?.Invoke(s);
            var sunColor = s.SunColor ?? (sun != null ? sun.color : new Color(1f, 0.93f, 0.82f));
            var toSun = sun != null ? -sun.transform.forward : Vector3.up;
            var seed = s.Seed ?? Seed("sky");
            var material = Material("Sky", SkyShader, m =>
            {
                m.SetColor("_ZenithColor", s.Zenith);
                m.SetColor("_HorizonColor", s.Horizon);
                m.SetColor("_GroundColor", s.Ground);
                m.SetFloat("_HorizonFalloff", s.HorizonFalloff);
                m.SetVector("_SunDirection", toSun);
                m.SetColor("_SunColor", sunColor);
                m.SetFloat("_SunSize", s.SunSize);
                m.SetFloat("_SunIntensity", s.SunIntensity);
                m.SetFloat("_SunGlow", s.SunGlow);
                m.SetFloat("_CloudCoverage", Mathf.Clamp01(s.CloudCoverage));
                m.SetFloat("_CloudSharpness", s.CloudSharpness);
                m.SetFloat("_CloudScale", s.CloudScale);
                m.SetFloat("_CloudOpacity", Mathf.Clamp01(s.CloudOpacity));
                m.SetColor("_CloudColor", s.CloudColor);
                m.SetColor("_CloudShadowColor", s.CloudShadow);
                m.SetVector("_CloudWind", s.Wind);
                m.SetInteger("_CloudOctaves", Mathf.Clamp(s.CloudOctaves, 1, 10));
                m.SetInteger("_CloudSeed", seed);
                m.SetFloat("_Exposure", s.Exposure);
            });
            RenderSettings.skybox = material;
            if (sun != null) RenderSettings.sun = sun;
            Create("Sky", typeof(SkyClock));
            return material;
        }

        /// <summary>
        /// Render the current RenderSettings.skybox into a cubemap asset and use it as the default reflection (no lighting bake
        /// needed). Call after the skybox is set. The mips are prefiltered for glossy reflections (GGX, what a lighting bake's
        /// convolution does); the asset is rewritten only when its pixels changed.
        /// </summary>
        public Cubemap BakeSkyReflection(string name = "SkyReflection", int size = 128)
        {
            var data = RenderEnvironment("the sky", Vector3.zero, size, 0, 0.3f, 1000f, CameraClearFlags.Skybox, Color.black, staticOnly: false);
            if (data == null) return null;
            var cube = WriteCubemap(AssetPath(name + ".asset"), size, data);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
            RenderSettings.customReflectionTexture = cube;
            RenderSettings.reflectionIntensity = 1f;
            return cube;
        }

        sealed class PendingProbe
        {
            public ReflectionProbe probe;
            public string path, asset, module;
        }

        readonly List<PendingProbe> m_Probes = new List<PendingProbe>();
        internal readonly List<string> CubemapsWritten = new List<string>();
        internal int ProbesRendered;

        /// <summary>
        /// A reflection probe at &lt;Module&gt;/<paramref name="path"/> that holds the finished scene seen from where it stands
        /// (Custom mode, no lighting bake): after every build step ran and the scene was saved with its lighting, the build renders
        /// the scene from the probe's position into a <paramref name="resolution"/>² cube - the renderers marked Reflection Probe
        /// Static (Unity's rule for baked probes: what moves, like a spinning hero object or particles, would reflect where it was at
        /// build time; GameObjectUtility.SetStaticEditorFlags or go.isStatic), with the sky, lights, decals and fog - prefilters its
        /// mips (GGX) and stores it at Reflection_&lt;path&gt;.asset. Other probes are off during that render (one bounce: what it
        /// shows reflects the sky). <paramref name="size"/> is the box that uses it (box projection on); move the probe after this call.
        /// The cubemap is a GPU result: the build fingerprint has its reference, not its pixels.
        /// </summary>
        public ReflectionProbe ReflectionProbe(string path, Vector3 size, int resolution = 128)
        {
            if (resolution < 16 || resolution > 2048 || (resolution & (resolution - 1)) != 0)
                throw new ArgumentException($"ReflectionProbe '{path}': resolution {resolution} must be a power of two from 16 to 2048");
            var go = Create(path, typeof(ReflectionProbe));
            var probe = go.GetComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Custom;
            probe.size = size;
            probe.center = Vector3.zero;
            probe.resolution = resolution;
            probe.hdr = true;
            probe.boxProjection = true;
            var asset = AssetPath("Reflection_" + path.Replace('/', '_') + ".asset");
            // The scene references the asset when it is saved; its pixels come after (RenderReflectionProbes).
            probe.customBakedTexture = PlaceholderCubemap(asset, resolution);
            m_Probes.Add(new PendingProbe { probe = probe, path = path, asset = asset, module = Module });
            return probe;
        }

        /// <summary>After the scene is saved with its lighting data: render every <see cref="ReflectionProbe"/> into its cubemap.</summary>
        internal void RenderReflectionProbes()
        {
            foreach (var p in m_Probes)
            {
                Module = p.module;
                if (p.probe == null) continue;
                var clear = p.probe.clearFlags == ReflectionProbeClearFlags.Skybox ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
                var data = RenderEnvironment("reflection probe " + p.path, p.probe.transform.position, p.probe.resolution, p.probe.cullingMask,
                    p.probe.nearClipPlane, p.probe.farClipPlane, clear, p.probe.backgroundColor, staticOnly: true);
                if (data == null) continue;
                var cube = WriteCubemap(p.asset, p.probe.resolution, data);
                AssetDatabase.SaveAssetIfDirty(cube);
                if (p.probe.customBakedTexture != cube) p.probe.customBakedTexture = cube;
                ProbesRendered++;
            }
        }

        Cubemap PlaceholderCubemap(string path, int size)
        {
            EnsureFolder(Path.GetDirectoryName(path));
            TouchedAssets.Add(path);
            var existing = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
            if (existing != null && existing.width == size && existing.format == TextureFormat.RGBAHalf && existing.mipmapCount == MipCount(size) && existing.isReadable)
                return existing;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            var cube = new Cubemap(size, TextureFormat.RGBAHalf, true) { name = Path.GetFileNameWithoutExtension(path) };
            cube.Apply(false, false);
            AssetDatabase.CreateAsset(cube, path);
            return cube;
        }

        static int MipCount(int size)
        {
            var n = 1;
            while ((size >>= 1) > 0) n++;
            return n;
        }

        /// <summary>
        /// The cubemap asset at <paramref name="path"/> with these pixels (RGBAHalf, index mip * 6 + face). Written only when they
        /// differ from the asset's (same machine, same scene = the same pixels: loops after the first leave the file alone).
        /// </summary>
        Cubemap WriteCubemap(string path, int size, byte[][] data)
        {
            var cube = PlaceholderCubemap(path, size);
            var same = true;
            for (var i = 0; i < data.Length && same; i++)
                same = cube.GetPixelData<byte>(i / 6, (CubemapFace)(i % 6)).AsReadOnlySpan().SequenceEqual(data[i]);
            if (same) return cube;
            for (var i = 0; i < data.Length; i++) cube.SetPixelData(data[i], i / 6, (CubemapFace)(i % 6));
            cube.Apply(false, false);
            EditorUtility.SetDirty(cube);
            CubemapsWritten.Add(path);
            return cube;
        }

        /// <summary>
        /// Render the scene from <paramref name="position"/> into a cube (Camera.RenderToCubemap into a cube render texture - the
        /// Cubemap overload leaves the CPU pixels unfilled in 6.6 and sRGB-encoded in 6.3, P-4) twice, then prefilter mips 1+ on the
        /// GPU (Hidden/Harness/CubeFilter) and read every face and mip back. <paramref name="staticOnly"/>: only Reflection Probe Static
        /// renderers, reflection probes off. Clouds at time 0 (SkyClock). The same pixels whatever the Editor did before (loops leave
        /// the asset alone). Null with a warning when the GPU cannot.
        /// </summary>
        byte[][] RenderEnvironment(string what, Vector3 position, int size, int cullingMask, float near, float far, CameraClearFlags clear, Color background, bool staticOnly)
        {
            if (!SystemInfo.supportsAsyncGPUReadback)
            {
                Warn($"rendering {what} into a cubemap needs AsyncGPUReadback (not on this GPU/graphics API); left unset");
                return null;
            }
            var filterShader = Shader.Find(CubeFilterShader);
            if (filterShader == null) throw new InvalidOperationException($"shader {CubeFilterShader} not found (Packages/{HarnessConfig.PackageName}/Shaders)");
            Shader.SetGlobalFloat(SkyClock.TimeId, 0f);
            var go = new GameObject("[HarnessCubeRender]") { hideFlags = HideFlags.HideAndDontSave };
            var hidden = new List<Renderer>();
            var probesOff = new List<ReflectionProbe>();
            var mips = MipCount(size);
            var desc = new RenderTextureDescriptor(size, size, RenderTextureFormat.ARGBHalf, 0)
            {
                dimension = TextureDimension.Cube, useMipMap = true, autoGenerateMips = false, msaaSamples = 1,
            };
            RenderTexture src = null, dst = null;
            Material filter = null;
            CommandBuffer cmd = null;
            try
            {
                if (staticOnly)
                {
                    foreach (var r in UnityCompat.FindObjects<Renderer>(FindObjectsInactive.Exclude))
                    {
                        if (r.forceRenderingOff || (GameObjectUtility.GetStaticEditorFlags(r.gameObject) & StaticEditorFlags.ReflectionProbeStatic) != 0) continue;
                        r.forceRenderingOff = true;
                        hidden.Add(r);
                    }
                    foreach (var p in UnityCompat.FindObjects<ReflectionProbe>(FindObjectsInactive.Exclude))
                    {
                        if (!p.enabled) continue;
                        p.enabled = false;
                        probesOff.Add(p);
                    }
                    // Culling sees the probes as they were when the Editor frame began - in a build, the previous scene's probe with
                    // the cubemap it is about to replace (each build reflected the last one's result until it settled, 2-3 builds).
                    UnityEngine.ReflectionProbe.UpdateCachedState();
                }
                var cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.transform.position = position;
                cam.cullingMask = cullingMask;
                cam.clearFlags = clear;
                cam.backgroundColor = background;
                cam.nearClipPlane = near;
                cam.farClipPlane = far;
                cam.allowHDR = true;
                cam.allowMSAA = false;
                src = new RenderTexture(desc);
                src.Create();
                if (!cam.RenderToCubemap(src))
                {
                    Warn($"rendering {what} into a cubemap failed; left unset");
                    return null;
                }
                // Draw again, keeping the second: the first render of a camera picks up state of the camera rendered before it (a
                // different near/far plane moved the sky's sun-disk edge by a few half-float steps - the cube then depended on what
                // the Editor drew last), and the Editor without a window draws some meshes wrong the first time (ROADMAP O-11).
                cam.RenderToCubemap(src);
                src.GenerateMips();

                dst = new RenderTexture(desc);
                dst.Create();
                filter = new Material(filterShader) { hideFlags = HideFlags.HideAndDontSave };
                cmd = new CommandBuffer { name = "HarnessCubeFilter" };
                var props = new MaterialPropertyBlock();
                props.SetTexture("_Source", src);
                props.SetFloat("_SourceSize", size);
                props.SetFloat("_SourceMips", mips);
                for (var mip = 1; mip < mips; mip++)
                {
                    props.SetFloat("_Size", Mathf.Max(1, size >> mip));
                    var roughness = PerceptualRoughnessOfMip(mip);
                    props.SetFloat("_Alpha", roughness * roughness);
                    for (var face = 0; face < 6; face++)
                    {
                        props.SetInteger("_Face", face);
                        cmd.SetRenderTarget(dst, mip, (CubemapFace)face);
                        cmd.DrawProcedural(Matrix4x4.identity, filter, 0, MeshTopology.Triangles, 3, 1, props);
                    }
                }
                Graphics.ExecuteCommandBuffer(cmd);

                var requests = new AsyncGPUReadbackRequest[mips * 6];
                for (var mip = 0; mip < mips; mip++)
                {
                    var w = Mathf.Max(1, size >> mip);
                    for (var face = 0; face < 6; face++)
                        requests[mip * 6 + face] = AsyncGPUReadback.Request(mip == 0 ? src : dst, mip, 0, w, 0, w, face, 1, TextureFormat.RGBAHalf);
                }
                var data = new byte[requests.Length][];
                for (var i = 0; i < requests.Length; i++)
                {
                    requests[i].WaitForCompletion();
                    if (requests[i].hasError)
                    {
                        Warn($"reading {what} back from the GPU failed; left unset");
                        return null;
                    }
                    data[i] = requests[i].GetData<byte>().ToArray();
                }
                return data;
            }
            finally
            {
                foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
                foreach (var p in probesOff) if (p != null) p.enabled = true;
                if (staticOnly) UnityEngine.ReflectionProbe.UpdateCachedState();
                cmd?.Release();
                if (filter != null) Object.DestroyImmediate(filter);
                if (src != null) { src.Release(); Object.DestroyImmediate(src); }
                if (dst != null) { dst.Release(); Object.DestroyImmediate(dst); }
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>
        /// The perceptual roughness URP reads from mip <paramref name="mip"/> of a reflection cubemap: the inverse of
        /// PerceptualRoughnessToMipmapLevel (mip = r (1.7 - 0.7 r) * 6), 1 past the sixth mip.
        /// </summary>
        internal static float PerceptualRoughnessOfMip(int mip)
        {
            if (mip >= 6) return 1f;
            return (1.7f - Mathf.Sqrt(2.89f - 2.8f * mip / 6f)) / 1.4f;
        }
    }
}
