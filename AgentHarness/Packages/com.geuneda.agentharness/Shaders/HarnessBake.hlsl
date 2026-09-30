#ifndef HARNESS_BAKE_INCLUDED
#define HARNESS_BAKE_INCLUDED

// Texture bakes on the GPU (ctx.BakeTexture): a full-screen triangle, uv (0, 0) = the texture's first texel (bottom left,
// like TextureBaker's (u, v)). Return linear colors as any shader does: an sRGB bake stores them sRGB-encoded in the PNG.
//
//   Shader "Game/Foo/MyBake" { Properties { _Scale ("Scale", Float) = 8 } SubShader { Pass {
//       HLSLPROGRAM
//       #pragma vertex BakeVert
//       #pragma fragment Frag
//       #include "Packages/com.geuneda.agentharness/Shaders/HarnessBake.hlsl"
//       float _Scale;
//       float4 Frag(BakeVaryings i) : SV_Target { float n = Noise_Fbm(i.uv * _Scale, 5, 2, 0.5, 7) * 0.5 + 0.5; return float4(n, n, n, 1); }
//       ENDHLSL
//   } } }

#include "Packages/com.geuneda.agentharness/Shaders/HarnessNoise.hlsl"

// (1 / width, 1 / height, width, height) of the texture being baked.
float4 _BakeTexelSize;

struct BakeVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
};

BakeVaryings BakeVert(uint id : SV_VertexID)
{
    BakeVaryings o;
    float2 uv = float2((id << 1) & 2, id & 2);
    o.positionCS = float4(uv * 2.0 - 1.0, 0.0, 1.0);
    o.uv = uv;
    return o;
}

// sRGB <-> linear for colors written as sRGB numbers (the ones a color picker or TextureBaker code uses).
float3 SrgbToLinear(float3 c) { return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4); }
float3 LinearToSrgb(float3 c) { return c <= 0.0031308 ? c * 12.92 : 1.055 * pow(c, 1.0 / 2.4) - 0.055; }

// A tangent-space normal (+Z up) from height differences to the right and up, encoded for a normal-map bake.
float4 EncodeNormal(float dhdx, float dhdy, float strength)
{
    float3 n = normalize(float3(-dhdx * strength, -dhdy * strength, 1.0));
    return float4(n * 0.5 + 0.5, 1.0);
}

#endif
