// Hand-written URP HLSL (no Shader Graph): vertex wobble, main-light diffuse + shadows, Blinn-Phong spec, reflections of the
// probe around it (Forward+ cluster loop), animated iridescent fresnel rim pushed into HDR so the Bloom override picks it up.
Shader "Game/Smoke/Iridescent"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.06, 0.07, 0.12, 1)
        _RimColorA ("Rim Color A", Color) = (0.15, 0.85, 1.0, 1)
        _RimColorB ("Rim Color B", Color) = (1.0, 0.25, 0.75, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.4
        _Emission ("Rim Emission (HDR)", Range(0, 20)) = 5
        _BandScale ("Band Scale", Float) = 5
        _BandSpeed ("Band Speed", Float) = 0.35
        _Gloss ("Gloss", Range(4, 256)) = 96
        _Wobble ("Vertex Wobble", Range(0, 0.2)) = 0.035
        _Smoothness ("Reflection Smoothness", Range(0, 1)) = 0.85
        _Reflectivity ("Reflectivity Facing", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _RimColorA;
            half4 _RimColorB;
            half _RimPower;
            half _Emission;
            float _BandScale;
            float _BandSpeed;
            half _Gloss;
            float _Wobble;
            half _Smoothness;
            half _Reflectivity;
        CBUFFER_END

        float3 WobbleOS(float3 positionOS, float3 normalOS)
        {
            float w = sin(positionOS.y * 9.0 + _Time.y * 2.0) * sin(positionOS.x * 7.0 - _Time.y * 1.3);
            return positionOS + normalOS * (w * _Wobble);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            // Reflection probes under Forward+ come through the cluster loop, whose keyword Unity 6.1 renamed (UNITY_VERSION: 6000.1.0 = 60010000).
            #if UNITY_VERSION >= 60010000
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #else
            #pragma multi_compile _ _FORWARD_PLUS
            #endif
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(WobbleOS(input.positionOS.xyz, input.normalOS));
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.uv = input.uv;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(GetWorldSpaceViewDir(i.positionWS));
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half atten = light.shadowAttenuation * light.distanceAttenuation;

                half ndl = saturate(dot(n, light.direction));
                half3 diffuse = _BaseColor.rgb * (light.color * ndl * atten + SampleSH(n));
                half3 h = normalize(light.direction + v);
                half spec = pow(saturate(dot(n, h)), _Gloss) * atten;

                half fresnel = pow(1.0h - saturate(dot(n, v)), _RimPower);
                float band = 0.5 + 0.5 * sin((i.uv.x * _BandScale + _Time.y * _BandSpeed) * 6.2831853);
                half3 rim = lerp(_RimColorA.rgb, _RimColorB.rgb, band);

                // The probe around the knot (or the sky outside it), Schlick's fresnel.
                half3 env = GlossyEnvironmentReflection(reflect(-v, n), i.positionWS, 1.0h - _Smoothness, 1.0h, GetNormalizedScreenSpaceUV(i.positionCS));
                half reflectance = _Reflectivity + (1.0h - _Reflectivity) * pow(1.0h - saturate(dot(n, v)), 5.0h);

                half3 color = diffuse + spec * light.color + env * reflectance + rim * fresnel * _Emission;
                color = MixFog(color, i.fogFactor);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex VertShadow
            #pragma fragment FragShadow
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings VertShadow(Attributes input)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(WobbleOS(input.positionOS.xyz, input.normalOS));
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 FragShadow(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex VertDepth
            #pragma fragment FragDepth

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings VertDepth(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(WobbleOS(input.positionOS.xyz, input.normalOS));
                return o;
            }

            half FragDepth(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex VertDN
            #pragma fragment FragDN
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings VertDN(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(WobbleOS(input.positionOS.xyz, input.normalOS));
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            half4 FragDN(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(n);
                float2 remapped = saturate(oct * 0.5 + 0.5);
                return half4(PackFloat2To888(remapped), 0.0);
            #else
                return half4(n, 0.0);
            #endif
            }
            ENDHLSL
        }
    }

    FallBack Off
}
