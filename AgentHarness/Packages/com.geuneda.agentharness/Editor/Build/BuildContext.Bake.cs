using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Harness.Editor
{
    public sealed partial class BuildContext
    {
        internal const string GpuKeyPrefix = "harness-gpu:";

        /// <summary>
        /// Bake a texture on the GPU: pass <paramref name="pass"/> of <paramref name="shaderName"/> (a bake shader, see
        /// Packages/com.geuneda.agentharness/Shaders/HarnessBake.hlsl) draws every texel of a width x height target, and the result is
        /// saved as &lt;name&gt;.png like <see cref="SaveTexture"/>. <paramref name="setup"/> sets the material's properties (numbers,
        /// input textures - e.g. a height field from <see cref="FloatTexture"/>). Return linear colors from the shader; an sRGB bake
        /// stores them sRGB-encoded. When the shader source (with its includes), the properties, the input textures and the size are
        /// the same as for the PNG on disk, nothing is drawn (the PNG is kept).
        /// The build fingerprint hashes those inputs, not the PNG: the last bits of a GPU result can differ between GPUs and drivers
        /// (the golden images compare how it looks).
        /// </summary>
        public Texture2D BakeTexture(string name, int width, int height, string shaderName, Action<Material> setup = null,
            bool sRGB = true, bool normalMap = false, TextureWrapMode wrap = TextureWrapMode.Repeat, bool mipmaps = true, int pass = 0)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null) throw new ArgumentException($"BakeTexture '{name}': shader '{shaderName}' not found");
            if (width < 1 || height < 1 || width > 8192 || height > 8192) throw new ArgumentException($"BakeTexture '{name}': size {width}x{height} out of range");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            RenderTexture rt = null;
            CommandBuffer cmd = null;
            try
            {
                if (pass < 0 || pass >= material.passCount) throw new ArgumentException($"BakeTexture '{name}': shader '{shaderName}' has {material.passCount} pass(es), no pass {pass}");
                setup?.Invoke(material);
                material.SetVector("_BakeTexelSize", new Vector4(1f / width, 1f / height, width, height));
                if (!m_ShaderSources.TryGetValue(shader, out var source)) m_ShaderSources[shader] = source = ShaderSourceHash(shader);
                var key = BakeKey(shader, source, material, width, height, sRGB, normalMap, wrap, mipmaps, pass);
                var path = AssetPath(name + ".png");
                if (AssetImporter.GetAtPath(path) is TextureImporter existing && existing.userData == key
                    && File.Exists(HarnessPaths.Combine(HarnessPaths.ProjectRoot, path)))
                {
                    TouchedAssets.Add(path);
                    BakesSkipped++;
                    return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                }
                if (!SystemInfo.supportsAsyncGPUReadback) throw new InvalidOperationException("BakeTexture needs AsyncGPUReadback (this GPU/graphics API does not support it)");

                var linear = !sRGB || normalMap;
                rt = new RenderTexture(new RenderTextureDescriptor(width, height, RenderTextureFormat.ARGB32, 0)
                {
                    sRGB = !linear, msaaSamples = 1, useMipMap = false, autoGenerateMips = false,
                });
                rt.Create();
                cmd = new CommandBuffer { name = "HarnessBake " + name };
                cmd.SetRenderTarget(rt);
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.DrawProcedural(Matrix4x4.identity, material, pass, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(cmd);
                var request = AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32);
                request.WaitForCompletion();
                if (request.hasError) throw new InvalidOperationException($"BakeTexture '{name}': reading the result back from the GPU failed");
                var data = request.GetData<byte>().ToArray();
                if (SystemInfo.graphicsUVStartsAtTop) FlipRows(data, width * 4, height);
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipmaps, linear)
                {
                    name = name, wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = 4,
                };
                tex.SetPixelData(data, 0);
                tex.Apply(mipmaps, false);
                Bakes++;
                return SaveTexture(tex, name, sRGB, normalMap, key);
            }
            finally
            {
                cmd?.Release();
                if (rt != null) { rt.Release(); Object.DestroyImmediate(rt); }
                Object.DestroyImmediate(material);
            }
        }

        internal int Bakes, BakesSkipped;
        readonly Dictionary<Shader, string> m_ShaderSources = new Dictionary<Shader, string>();   // read once per build

        /// <summary>
        /// A float grid (e.g. <c>TextureBaker.SampleGrid</c> heights, row 0 = v 0) as an in-memory RFloat texture for a bake shader to
        /// sample (bilinear, clamped). Not saved; destroy it after the bakes that use it.
        /// </summary>
        public static Texture2D FloatTexture(float[] grid, int width, int height, string name = "FloatGrid")
        {
            if (grid == null || grid.Length != width * height) throw new ArgumentException("FloatTexture: grid must have width * height values");
            var tex = new Texture2D(width, height, TextureFormat.RFloat, false, true)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave,
            };
            tex.SetPixelData(grid, 0);
            tex.Apply(false, false);
            return tex;
        }

        /// <summary>
        /// Rows come back top first where the graphics API puts row 0 of a render target at the top (Direct3D, Metal, Vulkan):
        /// flip them so row 0 is uv.y = 0, Texture2D's bottom row, everywhere.
        /// </summary>
        static void FlipRows(byte[] data, int rowBytes, int rows)
        {
            var tmp = new byte[rowBytes];
            for (int top = 0, bottom = rows - 1; top < bottom; top++, bottom--)
            {
                Buffer.BlockCopy(data, top * rowBytes, tmp, 0, rowBytes);
                Buffer.BlockCopy(data, bottom * rowBytes, data, top * rowBytes, rowBytes);
                Buffer.BlockCopy(tmp, 0, data, bottom * rowBytes, rowBytes);
            }
        }

        /// <summary>What a bake depends on: the shader source and its includes, every property value, input textures' contents, the size, the pass.</summary>
        static string BakeKey(Shader shader, string source, Material material, int width, int height, bool sRGB, bool normalMap, TextureWrapMode wrap, bool mipmaps, int pass)
        {
            var sb = new StringBuilder();
            sb.Append(shader.name).Append('|').Append(source).Append('|').Append(pass);
            sb.Append('|').Append(width).Append('x').Append(height).Append('|').Append(sRGB).Append('|').Append(normalMap).Append('|').Append(wrap).Append('|').Append(mipmaps);
            var values = new SortedDictionary<string, string>(MaterialValues(material), StringComparer.Ordinal);
            foreach (var kv in values) sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value);
            foreach (var n in material.GetPropertyNames(MaterialPropertyType.Texture))
            {
                var t = material.GetTexture(n);
                if (t == null) continue;
                var path = AssetDatabase.GetAssetPath(t);
                sb.Append('|').Append(n).Append('#');
                if (!string.IsNullOrEmpty(path) && File.Exists(FileUtil.GetPhysicalPath(path))) sb.Append(Sha1(File.ReadAllBytes(FileUtil.GetPhysicalPath(path))));
                else if (t is Texture2D t2 && t2.isReadable) sb.Append(Sha1(t2.GetRawTextureData()));
                else sb.Append(t.name).Append(t.width).Append('x').Append(t.height);
            }
            return GpuKeyPrefix + Sha1(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        static readonly Regex s_Include = new Regex("#\\s*include\\s+\"([^\"]+)\"", RegexOptions.Compiled);

        /// <summary>
        /// Hash of a shader's source text and every file it includes (recursively), line endings normalized - the same on every
        /// machine and checkout (a git checkout may use CRLF). Built-in shaders hash their name.
        /// </summary>
        internal static string ShaderSourceHash(Shader shader)
        {
            var path = AssetDatabase.GetAssetPath(shader);
            if (string.IsNullOrEmpty(path) || path.StartsWith("Resources/", StringComparison.Ordinal) || path.StartsWith("Library/", StringComparison.Ordinal))
                return "builtin:" + shader.name;
            var sb = new StringBuilder();
            AppendSource(path, sb, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            return Sha1(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        /// <summary>Source hash of the files with these extensions under <paramref name="folder"/> (a module's shaders, for CacheHit).</summary>
        internal static string SourcesHash(string folder, params string[] extensions)
        {
            var physical = FileUtil.GetPhysicalPath(folder);
            if (!Directory.Exists(physical)) return "none";
            var files = new List<string>();
            foreach (var ext in extensions) files.AddRange(Directory.GetFiles(physical, "*" + ext, SearchOption.AllDirectories));
            files.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in files)
            {
                var rel = folder.TrimEnd('/') + "/" + f.Substring(physical.Length).TrimStart('\\', '/').Replace('\\', '/');
                AppendSource(rel, sb, seen);
            }
            return Sha1(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        static void AppendSource(string assetPath, StringBuilder sb, HashSet<string> seen)
        {
            if (!seen.Add(assetPath)) return;
            var physical = FileUtil.GetPhysicalPath(assetPath);
            if (!File.Exists(physical)) { sb.Append("missing:").Append(assetPath).Append('\n'); return; }
            var text = File.ReadAllText(physical).Replace("\r\n", "\n");
            sb.Append("== ").Append(assetPath).Append('\n').Append(text).Append('\n');
            foreach (Match m in s_Include.Matches(text))
            {
                var inc = m.Groups[1].Value.Replace('\\', '/');
                // Unity resolves "Assets/..." and "Packages/..." from the project, anything else next to the including file.
                var resolved = inc.StartsWith("Assets/", StringComparison.Ordinal) || inc.StartsWith("Packages/", StringComparison.Ordinal)
                    ? inc
                    : NormalizePath(Path.GetDirectoryName(assetPath)?.Replace('\\', '/') + "/" + inc);
                // The render pipelines' own includes change with the Unity version, which the fingerprint has anyway.
                if (resolved.StartsWith("Packages/com.unity.", StringComparison.Ordinal)) { sb.Append("unity:").Append(resolved).Append('\n'); continue; }
                AppendSource(resolved, sb, seen);
            }
        }

        static string NormalizePath(string path)
        {
            var parts = new List<string>();
            foreach (var p in path.Split('/'))
            {
                if (p == "." || p.Length == 0) continue;
                if (p == ".." && parts.Count > 0) parts.RemoveAt(parts.Count - 1);
                else parts.Add(p);
            }
            return string.Join("/", parts);
        }

        static string Sha1(byte[] bytes)
        {
            using (var sha = SHA1.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
