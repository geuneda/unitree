using System;
using UnityEngine;

namespace Harness.Procedural
{
    /// <summary>
    /// Bake functions into Texture2D (runtime or editor). In a Builder, persist the result with
    /// <c>ctx.SaveTexture(...)</c>; at runtime, use the texture directly.
    /// </summary>
    public static class TextureBaker
    {
        /// <summary>Evaluate <paramref name="f"/>(u, v) per texel (u, v in [0,1], texel centers).</summary>
        public static Texture2D Bake(int width, int height, Func<float, float, Color> f, bool linear = false,
            bool mipmaps = true, TextureWrapMode wrap = TextureWrapMode.Repeat, FilterMode filter = FilterMode.Trilinear,
            string name = "ProceduralTexture")
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, mipmaps, linear)
            {
                name = name,
                wrapMode = wrap,
                filterMode = filter,
                anisoLevel = 4,
            };
            var px = new Color32[width * height];
            for (var y = 0; y < height; y++)
            {
                var v = (y + 0.5f) / height;
                for (var x = 0; x < width; x++)
                    px[y * width + x] = f((x + 0.5f) / width, v);
            }
            tex.SetPixels32(px);
            tex.Apply(mipmaps, false);
            return tex;
        }

        /// <summary>
        /// Sample <paramref name="f"/>(u, v) at texel centers into a row-major grid. Evaluate expensive functions once,
        /// then derive color/slope/normals from the grid instead of re-sampling neighbours per texel.
        /// </summary>
        public static float[] SampleGrid(int width, int height, Func<float, float, float> f)
        {
            var g = new float[width * height];
            for (var y = 0; y < height; y++)
            {
                var v = (y + 0.5f) / height;
                for (var x = 0; x < width; x++) g[y * width + x] = f((x + 0.5f) / width, v);
            }
            return g;
        }

        /// <summary>Grid lookup with clamping (or wrapping).</summary>
        public static float At(float[] grid, int width, int height, int x, int y, bool wrap = false)
        {
            if (wrap) { x = ((x % width) + width) % width; y = ((y % height) + height) % height; }
            else { x = Mathf.Clamp(x, 0, width - 1); y = Mathf.Clamp(y, 0, height - 1); }
            return grid[y * width + x];
        }

        /// <summary>Tangent-space normal map from a height grid; <paramref name="strength"/> scales the per-texel height delta.</summary>
        public static Texture2D NormalMapFromGrid(float[] heights, int width, int height, float strength, bool wrap = false, string name = "ProceduralNormal")
        {
            return Bake(width, height, (u, v) =>
            {
                var x = Mathf.Min(width - 1, (int)(u * width)); var y = Mathf.Min(height - 1, (int)(v * height));
                var dx = At(heights, width, height, x - 1, y, wrap) - At(heights, width, height, x + 1, y, wrap);
                var dy = At(heights, width, height, x, y - 1, wrap) - At(heights, width, height, x, y + 1, wrap);
                var n = new Vector3(dx * strength, dy * strength, 1f).normalized;
                return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }, linear: true, wrap: wrap ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, name: name);
        }

        /// <summary>Tangent-space normal map from a height function (linear texture).</summary>
        public static Texture2D NormalMap(int width, int height, Func<float, float, float> heightFn, float strength = 2f, string name = "ProceduralNormal")
        {
            var du = 1f / width; var dv = 1f / height;
            return Bake(width, height, (u, v) =>
            {
                var hl = heightFn(u - du, v); var hr = heightFn(u + du, v);
                var hd = heightFn(u, v - dv); var hu = heightFn(u, v + dv);
                var n = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1f).normalized;
                return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }, linear: true, name: name);
        }

        public static Texture2D Checker(int size, int cells, Color a, Color b, string name = "Checker") =>
            Bake(size, size, (u, v) => ((Mathf.FloorToInt(u * cells) + Mathf.FloorToInt(v * cells)) & 1) == 0 ? a : b,
                filter: FilterMode.Point, name: name);

        /// <summary>Tileable fBm noise (sampled on a torus so it wraps seamlessly).</summary>
        public static Texture2D TileableNoise(int size, float frequency, int seed, Gradient ramp = null, string name = "Noise")
        {
            return Bake(size, size, (u, v) =>
            {
                var n = TileableFbm(u, v, frequency, seed) * 0.5f + 0.5f;
                return ramp != null ? ramp.Evaluate(n) : new Color(n, n, n, 1f);
            }, name: name);
        }

        /// <summary>Seamless 2D fBm: maps (u,v) onto a 4D torus via two 3D samples.</summary>
        public static float TileableFbm(float u, float v, float frequency, int seed, int octaves = 5)
        {
            float sum = 0, amp = 1, norm = 0, freq = frequency;
            for (var i = 0; i < octaves; i++)
            {
                var a = u * Mathf.PI * 2f; var b = v * Mathf.PI * 2f;
                var r = freq / (Mathf.PI * 2f);
                var n = Noise.Perlin(Mathf.Cos(a) * r, Mathf.Sin(a) * r, Mathf.Cos(b) * r + Mathf.Sin(b) * r * 0.5f, seed + i * 101);
                sum += n * amp; norm += amp; amp *= 0.5f; freq *= 2f;
            }
            return sum / norm;
        }

        /// <summary>Gradient from (t, color) keys ??convenience for ramps in code.</summary>
        public static Gradient Ramp(params (float t, Color c)[] keys)
        {
            var g = new Gradient();
            var ck = new GradientColorKey[keys.Length];
            var ak = new GradientAlphaKey[keys.Length];
            for (var i = 0; i < keys.Length; i++) { ck[i] = new GradientColorKey(keys[i].c, keys[i].t); ak[i] = new GradientAlphaKey(keys[i].c.a, keys[i].t); }
            g.SetKeys(ck, ak);
            return g;
        }
    }
}
