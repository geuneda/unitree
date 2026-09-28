using UnityEngine;

namespace Harness.Procedural
{
    /// <summary>
    /// Shader-style math for procedural code. NOTE: Mathf.SmoothStep(from, to, t) is NOT GLSL/HLSL smoothstep —
    /// it interpolates between from and to. Use <see cref="Smoothstep"/> for smoothstep(edge0, edge1, x).
    /// </summary>
    public static class PMath
    {
        /// <summary>GLSL/HLSL smoothstep: 0 at x ≤ edge0, 1 at x ≥ edge1, Hermite in between.</summary>
        public static float Smoothstep(float edge0, float edge1, float x)
        {
            var t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        public static float Saturate(float x) => Mathf.Clamp01(x);

        /// <summary>Linear remap of x from [a0, a1] to [b0, b1] (unclamped).</summary>
        public static float Remap(float x, float a0, float a1, float b0, float b1) => b0 + (x - a0) * (b1 - b0) / (a1 - a0);

        public static Color Mix(Color a, Color b, float t) => Color.LerpUnclamped(a, b, Mathf.Clamp01(t));
    }
}
