using UnityEngine;

namespace Harness.Procedural
{
    /// <summary>
    /// Deterministic, allocation-free, stateless noise (same seed → same values on every run and machine).
    /// Gradient (Perlin-style) noise in [-1, 1], fBm, ridged and domain-warped variants, plus hashing.
    /// </summary>
    public static class Noise
    {
        // ---- Hashing -----------------------------------------------------------------------------

        public static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352dU;
            x ^= x >> 15; x *= 0x846ca68bU;
            x ^= x >> 16;
            return x;
        }

        public static uint Hash(int x, int y, int seed) =>
            Hash((uint)x * 0x8da6b343U ^ Hash((uint)y * 0xd8163841U ^ Hash((uint)seed * 0xcb1ab31fU)));

        public static uint Hash(int x, int y, int z, int seed) =>
            Hash((uint)z * 0x165667b1U ^ Hash(x, y, seed));

        /// <summary>Uniform float in [0, 1) from integer coordinates.</summary>
        public static float Value01(int x, int y, int seed) => (Hash(x, y, seed) & 0xFFFFFF) / 16777216f;

        /// <summary>Deterministic pseudo-random stream (for scattering objects in builders).</summary>
        public struct Rng
        {
            uint m_State;
            public Rng(int seed) { m_State = Hash((uint)seed ^ 0x9E3779B9U) | 1U; }
            public uint NextUInt() { m_State = Hash(m_State + 0x9E3779B9U); return m_State; }
            public float Next01() => (NextUInt() & 0xFFFFFF) / 16777216f;
            public float Range(float min, float max) => min + (max - min) * Next01();
            public int Range(int minInclusive, int maxExclusive) => minInclusive + (int)(NextUInt() % (uint)Mathf.Max(1, maxExclusive - minInclusive));
            public Vector2 InsideUnitCircle() { var a = Next01() * Mathf.PI * 2f; var r = Mathf.Sqrt(Next01()); return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
            public Vector3 OnUnitSphere() { var z = Range(-1f, 1f); var a = Next01() * Mathf.PI * 2f; var r = Mathf.Sqrt(1f - z * z); return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z); }
        }

        // ---- Gradient noise ------------------------------------------------------------------------

        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        // 32 unit gradients around the circle (immutable lookup table; avoids cos/sin per sample).
        static readonly float[] s_GradX = BuildGrad(true);
        static readonly float[] s_GradY = BuildGrad(false);

        static float[] BuildGrad(bool x)
        {
            var g = new float[32];
            for (var i = 0; i < 32; i++) { var a = (i + 0.5f) / 32f * Mathf.PI * 2f; g[i] = x ? Mathf.Cos(a) : Mathf.Sin(a); }
            return g;
        }

        static float Grad2(int ix, int iy, int seed, float dx, float dy)
        {
            var h = (int)(Hash(ix, iy, seed) & 31);
            return s_GradX[h] * dx + s_GradY[h] * dy;
        }

        /// <summary>2D gradient noise, range ≈ [-1, 1].</summary>
        public static float Perlin(float x, float y, int seed = 0)
        {
            var ix = Mathf.FloorToInt(x); var iy = Mathf.FloorToInt(y);
            var fx = x - ix; var fy = y - iy;
            var u = Fade(fx); var v = Fade(fy);
            var n00 = Grad2(ix, iy, seed, fx, fy);
            var n10 = Grad2(ix + 1, iy, seed, fx - 1f, fy);
            var n01 = Grad2(ix, iy + 1, seed, fx, fy - 1f);
            var n11 = Grad2(ix + 1, iy + 1, seed, fx - 1f, fy - 1f);
            return Mathf.Lerp(Mathf.Lerp(n00, n10, u), Mathf.Lerp(n01, n11, u), v) * 1.4142135f;
        }

        static float Grad3(int ix, int iy, int iz, int seed, float dx, float dy, float dz)
        {
            // 12 cube-edge gradients.
            switch (Hash(ix, iy, iz, seed) % 12)
            {
                case 0: return dx + dy; case 1: return -dx + dy; case 2: return dx - dy; case 3: return -dx - dy;
                case 4: return dx + dz; case 5: return -dx + dz; case 6: return dx - dz; case 7: return -dx - dz;
                case 8: return dy + dz; case 9: return -dy + dz; case 10: return dy - dz; default: return -dy - dz;
            }
        }

        /// <summary>3D gradient noise, range ≈ [-1, 1].</summary>
        public static float Perlin(float x, float y, float z, int seed = 0)
        {
            var ix = Mathf.FloorToInt(x); var iy = Mathf.FloorToInt(y); var iz = Mathf.FloorToInt(z);
            var fx = x - ix; var fy = y - iy; var fz = z - iz;
            var u = Fade(fx); var v = Fade(fy); var w = Fade(fz);
            var x00 = Mathf.Lerp(Grad3(ix, iy, iz, seed, fx, fy, fz), Grad3(ix + 1, iy, iz, seed, fx - 1, fy, fz), u);
            var x10 = Mathf.Lerp(Grad3(ix, iy + 1, iz, seed, fx, fy - 1, fz), Grad3(ix + 1, iy + 1, iz, seed, fx - 1, fy - 1, fz), u);
            var x01 = Mathf.Lerp(Grad3(ix, iy, iz + 1, seed, fx, fy, fz - 1), Grad3(ix + 1, iy, iz + 1, seed, fx - 1, fy, fz - 1), u);
            var x11 = Mathf.Lerp(Grad3(ix, iy + 1, iz + 1, seed, fx, fy - 1, fz - 1), Grad3(ix + 1, iy + 1, iz + 1, seed, fx - 1, fy - 1, fz - 1), u);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, v), Mathf.Lerp(x01, x11, v), w);
        }

        // ---- Fractals ------------------------------------------------------------------------------

        /// <summary>Fractal Brownian motion, normalized to ≈ [-1, 1].</summary>
        public static float Fbm(float x, float y, int octaves = 5, float lacunarity = 2f, float gain = 0.5f, int seed = 0)
        {
            float sum = 0, amp = 1, norm = 0, freq = 1;
            for (var i = 0; i < octaves; i++)
            {
                sum += Perlin(x * freq, y * freq, seed + i * 1013) * amp;
                norm += amp; amp *= gain; freq *= lacunarity;
            }
            return sum / norm;
        }

        /// <summary>Ridged multifractal in [0, 1] — sharp crests (mountain ridges).</summary>
        public static float Ridged(float x, float y, int octaves = 5, float lacunarity = 2f, float gain = 0.5f, int seed = 0)
        {
            float sum = 0, amp = 0.5f, norm = 0, freq = 1, prev = 1;
            for (var i = 0; i < octaves; i++)
            {
                var n = 1f - Mathf.Abs(Perlin(x * freq, y * freq, seed + i * 7919));
                n *= n;
                sum += n * amp * prev;
                norm += amp;
                prev = Mathf.Clamp01(n * 2f);
                amp *= gain; freq *= lacunarity;
            }
            return Mathf.Clamp01(sum / norm);
        }

        /// <summary>fBm sampled at a position displaced by another fBm (organic, swirly shapes).</summary>
        public static float Warped(float x, float y, float strength = 1.5f, int octaves = 5, int seed = 0)
        {
            var wx = Fbm(x + 5.2f, y + 1.3f, 4, 2f, 0.5f, seed + 17);
            var wy = Fbm(x - 3.7f, y + 8.1f, 4, 2f, 0.5f, seed + 31);
            return Fbm(x + wx * strength, y + wy * strength, octaves, 2f, 0.5f, seed);
        }

        /// <summary>Cellular / Worley F1 distance in [0, ~1].</summary>
        public static float Worley(float x, float y, int seed = 0)
        {
            var ix = Mathf.FloorToInt(x); var iy = Mathf.FloorToInt(y);
            var best = 8f;
            for (var oy = -1; oy <= 1; oy++)
            for (var ox = -1; ox <= 1; ox++)
            {
                var cx = ix + ox; var cy = iy + oy;
                var px = cx + Value01(cx, cy, seed);
                var py = cy + Value01(cx, cy, seed + 1);
                var dx = px - x; var dy = py - y;
                best = Mathf.Min(best, dx * dx + dy * dy);
            }
            return Mathf.Sqrt(best);
        }
    }
}
