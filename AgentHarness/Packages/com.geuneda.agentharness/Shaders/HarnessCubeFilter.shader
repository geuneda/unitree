// Prefilters an environment cubemap for glossy reflections (ctx.BakeSkyReflection, ctx.ReflectionProbe): mip m of the output
// holds the source convolved with the GGX lobe of the roughness URP reads from that mip (PerceptualRoughnessToMipmapLevel,
// 6 steps), as a lighting bake's convolution does. A fixed Hammersley set (the same result on every run) with filtered
// importance sampling from the source's box-filtered mips (Colbert & Krivanek): few samples, no sparkles from the sun.
// One draw per face and mip into that face of a cube render target; the fragment's row is the face's memory row (row 0 = the
// face's top, as Cubemap.GetPixel and AmbientProbe.Direction index it).
Shader "Hidden/Harness/CubeFilter"
{
    SubShader
    {
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            TextureCube<float4> _Source;
            SamplerState sampler_trilinear_clamp;
            int _Face;
            float _Size;         // output face size (texels)
            float _SourceSize;   // source mip 0 face size
            float _SourceMips;
            float _Alpha;        // GGX alpha = perceptual roughness squared

            #define FILTER_PI 3.14159265359
            #define FILTER_SAMPLES 256u

            float4 Vert(uint id : SV_VertexID) : SV_POSITION
            {
                float2 uv = float2((id << 1) & 2, id & 2);
                return float4(uv * 2.0 - 1.0, 0.0, 1.0);
            }

            // Direction of (u, v) in [-1, 1] on a face: u to the right, v down from the top row (AmbientProbe.Direction).
            float3 FaceDirection(int face, float u, float v)
            {
                if (face == 0) return float3(1.0, -v, -u);
                if (face == 1) return float3(-1.0, -v, u);
                if (face == 2) return float3(u, 1.0, v);
                if (face == 3) return float3(u, -1.0, -v);
                if (face == 4) return float3(u, -v, 1.0);
                return float3(-u, -v, -1.0);
            }

            float RadicalInverse(uint b)
            {
                b = (b << 16u) | (b >> 16u);
                b = ((b & 0x55555555u) << 1u) | ((b & 0xAAAAAAAAu) >> 1u);
                b = ((b & 0x33333333u) << 2u) | ((b & 0xCCCCCCCCu) >> 2u);
                b = ((b & 0x0F0F0F0Fu) << 4u) | ((b & 0xF0F0F0F0u) >> 4u);
                b = ((b & 0x00FF00FFu) << 8u) | ((b & 0xFF00FF00u) >> 8u);
                return b * 2.3283064365386963e-10;
            }

            float4 Frag(float4 pos : SV_POSITION) : SV_Target
            {
                float3 n = normalize(FaceDirection(_Face, pos.x / _Size * 2.0 - 1.0, pos.y / _Size * 2.0 - 1.0));
                float3 up = abs(n.y) < 0.999 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
                float3 t = normalize(cross(up, n));
                float3 b = cross(n, t);
                float a2 = max(_Alpha * _Alpha, 1e-8);
                float texelSolidAngle = 4.0 * FILTER_PI / (6.0 * _SourceSize * _SourceSize);
                float3 sum = 0.0;
                float weight = 0.0;
                [loop] for (uint i = 0u; i < FILTER_SAMPLES; i++)
                {
                    // Half vector from the GGX distribution around n; view = normal = reflection (split-sum).
                    float phi = 2.0 * FILTER_PI * (i + 0.5) / FILTER_SAMPLES;
                    float xi = RadicalInverse(i);
                    float cosTheta = sqrt((1.0 - xi) / (1.0 + (a2 - 1.0) * xi));
                    float sinTheta = sqrt(saturate(1.0 - cosTheta * cosTheta));
                    float3 h = t * (sinTheta * cos(phi)) + b * (sinTheta * sin(phi)) + n * cosTheta;
                    float3 l = 2.0 * dot(n, h) * h - n;
                    float nl = dot(n, l);
                    if (nl <= 0.0) continue;
                    float dd = cosTheta * cosTheta * (a2 - 1.0) + 1.0;
                    float pdf = a2 / (FILTER_PI * dd * dd) * 0.25;   // D(h) n.h / (4 v.h), v = n
                    float sampleSolidAngle = 1.0 / (FILTER_SAMPLES * pdf + 1e-6);
                    float lod = clamp(0.5 * log2(sampleSolidAngle / texelSolidAngle) + 1.0, 0.0, _SourceMips - 1.0);
                    sum += _Source.SampleLevel(sampler_trilinear_clamp, l, lod).rgb * nl;
                    weight += nl;
                }
                return float4(sum / max(weight, 1e-6), 1.0);
            }
            ENDHLSL
        }
    }
}
