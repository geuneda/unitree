// GPU bakes for the Stage terrain (StageTerrainStep via ctx.BakeTexture). Pass 0: albedo from the height field (the same
// heights the mesh is built from) and the same variation noise the CPU bake used; pass 1: the detail normal map.
// Pass 2 / 3: a tiling detail albedo (linear, 0.5 = no change) and detail normal for close-ups (URP Lit detail maps).
Shader "Game/Stage/TerrainBake"
{
    Properties
    {
        _HeightMap ("Height field (RFloat, world units)", 2D) = "black" {}
        _Size ("Terrain size (m)", Float) = 320
        _Seed ("Seed", Integer) = 0
    }
    SubShader
    {
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        #include "Packages/com.geuneda.agentharness/Shaders/HarnessBake.hlsl"
        Texture2D<float> _HeightMap;
        SamplerState sampler_linear_clamp;
        float4 _HeightMap_TexelSize;
        float _Size;
        int _Seed;

        float Height(float2 uv) { return _HeightMap.SampleLevel(sampler_linear_clamp, uv, 0); }
        float Smooth(float e0, float e1, float x) { float t = saturate((x - e0) / (e1 - e0)); return t * t * (3.0 - 2.0 * t); }
        ENDHLSL

        Pass
        {
            Name "Albedo"
            HLSLPROGRAM
            #pragma vertex BakeVert
            #pragma fragment Frag
            float4 Frag(BakeVaryings i) : SV_Target
            {
                // Slope from the height field (world units), one output texel apart.
                float2 d = _BakeTexelSize.xy;
                float texel = _Size * d.x;
                float h = Height(i.uv);
                float dx = (Height(i.uv + float2(d.x, 0)) - Height(i.uv - float2(d.x, 0))) / (2.0 * texel);
                float dz = (Height(i.uv + float2(0, d.y)) - Height(i.uv - float2(0, d.y))) / (2.0 * texel);
                float slope = saturate(sqrt(dx * dx + dz * dz));
                float n = Noise_Fbm(i.uv * 60.0, 3, 2.0, 0.5, _Seed + 5) * 0.5 + 0.5;
                // The CPU bake's colors and mixing, in sRGB numbers; the target stores the linear result sRGB-encoded.
                float3 grass = float3(0.16, 0.25, 0.08), dry = float3(0.38, 0.33, 0.17);
                float3 rock = float3(0.30, 0.27, 0.24), snow = float3(0.90, 0.92, 0.96);
                float3 c = lerp(grass, dry, Smooth(0.4, 0.8, n));
                c = lerp(c, rock, Smooth(0.45, 0.85, slope));
                c = lerp(c, snow, Smooth(20.0, 30.0, h + n * 6.0) * (1.0 - Smooth(0.6, 1.2, slope)));
                c *= 0.8 + 0.4 * n;
                return float4(SrgbToLinear(saturate(c)), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Normal"
            HLSLPROGRAM
            #pragma vertex BakeVert
            #pragma fragment Frag
            float Detail(float2 uv) { return Noise_Fbm(uv * 140.0, 4, 2.0, 0.5, _Seed + 23) * 0.3; }
            float4 Frag(BakeVaryings i) : SV_Target
            {
                float2 d = _BakeTexelSize.xy;
                float texel = _Size * d.x;
                float dhdx = (Detail(i.uv + float2(d.x, 0)) - Detail(i.uv - float2(d.x, 0))) / (2.0 * texel);
                float dhdy = (Detail(i.uv + float2(0, d.y)) - Detail(i.uv - float2(0, d.y))) / (2.0 * texel);
                return EncodeNormal(dhdx, dhdy, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DetailAlbedo"
            HLSLPROGRAM
            #pragma vertex BakeVert
            #pragma fragment Frag
            float4 Frag(BakeVaryings i) : SV_Target
            {
                // Grain and pebbles around 0.5 (URP multiplies the albedo by 2 x this), seamless across the tile.
                float grain = Noise_FbmTiled(i.uv, 16, 4, 0.55, _Seed + 41);
                float pebbles = 1.0 - saturate(Noise_WorleyTiled(i.uv * 24.0, 24, _Seed + 43) * 2.2);
                float v = 0.5 + grain * 0.14 + pebbles * 0.08;
                return float4(v, v, v, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DetailNormal"
            HLSLPROGRAM
            #pragma vertex BakeVert
            #pragma fragment Frag
            float Bumps(float2 uv) { return Noise_FbmTiled(uv, 16, 4, 0.55, _Seed + 41); }
            float4 Frag(BakeVaryings i) : SV_Target
            {
                float2 d = _BakeTexelSize.xy;
                float dhdx = (Bumps(i.uv + float2(d.x, 0)) - Bumps(i.uv - float2(d.x, 0))) / (2.0 * d.x);
                float dhdy = (Bumps(i.uv + float2(0, d.y)) - Bumps(i.uv - float2(0, d.y))) / (2.0 * d.y);
                return EncodeNormal(dhdx, dhdy, 0.012);
            }
            ENDHLSL
        }
    }
}
