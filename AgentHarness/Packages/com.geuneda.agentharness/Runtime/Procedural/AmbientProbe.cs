using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Harness.Procedural
{
    /// <summary>
    /// Diffuse ambient light from an environment cubemap without a lighting bake: L2 spherical harmonics in the form of
    /// <c>RenderSettings.ambientProbe</c> (Unity's basis 1, y, z, x, xy, yz, 3z²-1, xz, x²-y²). Each texel's radiance is projected
    /// with its solid angle and convolved with the cosine lobe, so a Lambert surface of albedo 1 shows (1/π)∫L·max(0,n·ω)dω;
    /// a uniform environment of radiance c gives the probe of Flat ambient c.
    /// </summary>
    public static class AmbientProbe
    {
        // Cosine-lobe convolution per band (Â_l/π) times the squared normalization of each real SH basis function.
        static readonly double[] s_Scale =
        {
            1.0 * 1.0 / (4 * Math.PI),
            2.0 / 3.0 * 3.0 / (4 * Math.PI), 2.0 / 3.0 * 3.0 / (4 * Math.PI), 2.0 / 3.0 * 3.0 / (4 * Math.PI),
            0.25 * 15.0 / (4 * Math.PI), 0.25 * 15.0 / (4 * Math.PI), 0.25 * 5.0 / (16 * Math.PI), 0.25 * 15.0 / (4 * Math.PI), 0.25 * 15.0 / (16 * Math.PI),
        };

        /// <summary>
        /// Project a readable cubemap (mip <paramref name="mip"/>). Throws when a texel is not a finite, non-negative radiance
        /// (an unfilled or garbage cubemap would otherwise light the scene black).
        /// </summary>
        public static SphericalHarmonicsL2 FromCubemap(Cubemap cube, int mip = 0)
        {
            if (cube == null) throw new ArgumentNullException(nameof(cube));
            if (!cube.isReadable) throw new ArgumentException($"cubemap '{cube.name}' is not readable");
            var size = Mathf.Max(1, cube.width >> mip);
            var sum = new double[3, 9];
            var basis = new double[9];
            double total = 0;
            var invalid = 0;
            for (var face = 0; face < 6; face++)
            {
                var px = cube.GetPixels((CubemapFace)face, mip);
                for (var y = 0; y < size; y++)
                {
                    var v = 2.0 * (y + 0.5) / size - 1.0;
                    for (var x = 0; x < size; x++)
                    {
                        var u = 2.0 * (x + 0.5) / size - 1.0;
                        var c = px[y * size + x];
                        if (!(c.r >= 0f && c.g >= 0f && c.b >= 0f) || float.IsInfinity(c.r) || float.IsInfinity(c.g) || float.IsInfinity(c.b)) { invalid++; continue; }
                        var d = Direction((CubemapFace)face, u, v);
                        var len2 = 1.0 + u * u + v * v;
                        var len = Math.Sqrt(len2);
                        var solidAngle = 4.0 / ((double)size * size) / (len2 * len);
                        Basis(d.x / len, d.y / len, d.z / len, basis);
                        for (var i = 0; i < 9; i++)
                        {
                            var w = basis[i] * solidAngle;
                            sum[0, i] += c.r * w;
                            sum[1, i] += c.g * w;
                            sum[2, i] += c.b * w;
                        }
                        total += solidAngle;
                    }
                }
            }
            if (invalid > 0) throw new InvalidOperationException($"cubemap '{cube.name}' has {invalid} texels that are not finite non-negative radiance (unfilled or garbage pixel data)");
            var norm = 4 * Math.PI / total;   // the texel solid angles sum to 4π up to discretization
            var sh = new SphericalHarmonicsL2();
            for (var ch = 0; ch < 3; ch++)
                for (var i = 0; i < 9; i++)
                    sh[ch, i] = (float)(sum[ch, i] * norm * s_Scale[i]);
            return sh;
        }

        /// <summary>
        /// Unnormalized direction of the texel at (u, v) in [-1, 1] on <paramref name="face"/>: u to the right, v down from the
        /// face's top row, as Cubemap.GetPixel(face, x, y) indexes it.
        /// </summary>
        public static Vector3 Direction(CubemapFace face, double u, double v)
        {
            switch (face)
            {
                case CubemapFace.PositiveX: return new Vector3(1f, (float)-v, (float)-u);
                case CubemapFace.NegativeX: return new Vector3(-1f, (float)-v, (float)u);
                case CubemapFace.PositiveY: return new Vector3((float)u, 1f, (float)v);
                case CubemapFace.NegativeY: return new Vector3((float)u, -1f, (float)-v);
                case CubemapFace.PositiveZ: return new Vector3((float)u, (float)-v, 1f);
                default: return new Vector3((float)-u, (float)-v, -1f);
            }
        }

        static void Basis(double x, double y, double z, double[] b)
        {
            b[0] = 1;
            b[1] = y;
            b[2] = z;
            b[3] = x;
            b[4] = x * y;
            b[5] = y * z;
            b[6] = 3 * z * z - 1;
            b[7] = x * z;
            b[8] = x * x - y * y;
        }
    }
}
