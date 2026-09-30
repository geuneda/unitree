// GPU bakes for the Stage props (StagePropsStep via ctx.BakeTexture). Pass 0: the rune circle decal (color + coverage in
// alpha); pass 1: rock albedo; pass 2: standing stone albedo (lighter, with vertical streaks).
Shader "Game/Stage/PropsBake"
{
    Properties
    {
        _Seed ("Seed", Integer) = 0
        _RuneColor ("Rune color (sRGB numbers)", Color) = (0.25, 0.9, 1, 1)
    }
    SubShader
    {
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        #include "Packages/com.geuneda.agentharness/Shaders/HarnessBake.hlsl"
        int _Seed;
        float4 _RuneColor;

        float Line(float d, float width) { return saturate(1.0 - abs(d) / width); }
        ENDHLSL

        Pass
        {
            Name "Rune"
            HLSLPROGRAM
            #pragma vertex BakeVert
            #pragma fragment Frag
            float4 Frag(BakeVaryings i) : SV_Target
            {
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                float a = atan2(p.y, p.x) / 6.28318530718 + 0.5;
                float ink = max(max(Line(r - 0.93, 0.014), Line(r - 0.87, 0.006)), Line(r - 0.58, 0.008));
                // 24 glyphs between the rings, each a few strokes chosen by a hash of its sector.
                float sectors = 24.0;
                int s = (int)floor(a * sectors);
                float la = frac(a * sectors);
                float lr = (r - 0.64) / 0.19;
                if (lr > 0.0 && lr < 1.0 && la > 0.18 && la < 0.82)
                {
                    uint bits = Noise_Hash(s, 0, _Seed);
                    float x = (la - 0.18) / 0.64;
                    float w = 0.07;
                    if (bits & 1u) ink = max(ink, Line(x - 0.5, w));
                    if (bits & 2u) ink = max(ink, Line(lr - 0.25, w));
                    if (bits & 4u) ink = max(ink, Line(lr - 0.75, w));
                    if (bits & 8u) ink = max(ink, Line(x - lr, w));
                    if (bits & 16u) ink = max(ink, Line(x - (1.0 - lr), w));
                    if ((bits & 96u) == 0u) ink = max(ink, Line(length(float2(x, lr) - 0.5) - 0.28, w));
                }
                return float4(SrgbToLinear(_RuneColor.rgb), saturate(ink * 1.6));
            }
            ENDHLSL
        }

        Pass
        {
            Name "Rock"
            HLSLPROGRAM
            #pragma vertex BakeVert
            #pragma fragment Frag
            float4 Frag(BakeVaryings i) : SV_Target
            {
                float n = Noise_Fbm(i.uv * 6.0, 5, 2.0, 0.55, _Seed) * 0.5 + 0.5;
                float spots = 1.0 - saturate(Noise_Worley(i.uv * 9.0, _Seed + 3) * 2.5);
                float3 c = lerp(float3(0.27, 0.25, 0.23), float3(0.45, 0.42, 0.38), n);
                c = lerp(c, float3(0.30, 0.36, 0.18), spots * 0.6);   // lichen
                return float4(SrgbToLinear(c), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Stone"
            HLSLPROGRAM
            #pragma vertex BakeVert
            #pragma fragment Frag
            float4 Frag(BakeVaryings i) : SV_Target
            {
                float n = Noise_Fbm(i.uv * float2(4.0, 1.5), 5, 2.0, 0.5, _Seed) * 0.5 + 0.5;
                float streak = Noise_Fbm(float2(i.uv.x * 30.0, i.uv.y * 2.0), 3, 2.0, 0.5, _Seed + 9) * 0.5 + 0.5;
                float3 c = lerp(float3(0.42, 0.41, 0.40), float3(0.62, 0.60, 0.57), n) * (0.85 + 0.25 * streak);
                return float4(SrgbToLinear(saturate(c)), 1.0);
            }
            ENDHLSL
        }
    }
}
