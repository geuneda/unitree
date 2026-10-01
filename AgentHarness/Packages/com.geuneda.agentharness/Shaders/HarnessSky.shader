// The harness sky (ctx.Sky): a gradient from the horizon to the zenith, the sun's disk and glow, and a layer of fBm clouds
// (HarnessNoise.hlsl - the noise of Harness.Procedural.Noise) that drifts with game time. The time is the global
// _HarnessSkyTime: SkyClock sets it from the game clock during play and back to 0 when play stops, so edit-mode captures and
// the cubemaps a build renders (BakeSkyReflection, ReflectionProbe) see the clouds where they are at time 0, and a scenario
// with a fixed time step sees the same clouds at the same t. Pipeline independent (a skybox material: RenderSettings.skybox).
Shader "Harness/Sky"
{
    Properties
    {
        _ZenithColor ("Zenith", Color) = (0.2, 0.3, 0.53, 1)
        _HorizonColor ("Horizon", Color) = (0.78, 0.88, 0.97, 1)
        _GroundColor ("Below the horizon", Color) = (0.58, 0.61, 0.61, 1)
        _HorizonFalloff ("Horizon falloff (higher = thinner haze band)", Float) = 6
        _SunDirection ("Direction to the sun (world)", Vector) = (0, 1, 0, 0)
        _SunColor ("Sun color", Color) = (1, 0.93, 0.82, 1)
        _SunSize ("Sun angular radius (degrees)", Float) = 1
        _SunIntensity ("Sun disk intensity", Float) = 40
        _SunGlow ("Sun glow", Float) = 0.6
        _CloudCoverage ("Cloud coverage", Range(0, 1)) = 0.45
        _CloudSharpness ("Cloud edge sharpness", Float) = 5
        _CloudScale ("Cloud scale (noise cells per sky unit)", Float) = 2
        _CloudOpacity ("Cloud opacity", Range(0, 1)) = 0.95
        _CloudColor ("Cloud lit color", Color) = (1, 1, 1, 1)
        _CloudShadowColor ("Cloud shade color", Color) = (0.68, 0.72, 0.8, 1)
        _CloudWind ("Wind (sky units per second of game time)", Vector) = (0.012, 0.005, 0, 0)
        _CloudOctaves ("Cloud octaves", Integer) = 6
        _CloudSeed ("Cloud seed", Integer) = 1
        _Exposure ("Exposure", Float) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"
            #include "Packages/com.geuneda.agentharness/Shaders/HarnessNoise.hlsl"

            half4 _ZenithColor, _HorizonColor, _GroundColor, _SunColor, _CloudColor, _CloudShadowColor;
            float _HorizonFalloff, _SunSize, _SunIntensity, _SunGlow;
            float _CloudCoverage, _CloudSharpness, _CloudScale, _CloudOpacity, _Exposure;
            float4 _SunDirection, _CloudWind;
            int _CloudOctaves, _CloudSeed;
            float _HarnessSkyTime;   // global (SkyClock): game time during play, 0 otherwise

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = mul((float3x3)unity_ObjectToWorld, v.vertex.xyz);
                return o;
            }

            // fBm of the harness gradient noise whose octaves fade out once a lattice cell is under ~2 pixels: no shimmer near the
            // horizon, and a small cubemap face gets the large shapes only. footprint = noise units per pixel.
            float CloudNoise(float2 p, float footprint)
            {
                float sum = 0.0, amp = 1.0, norm = 0.0, freq = 1.0;
                [loop] for (int i = 0; i < min(_CloudOctaves, 10); i++)
                {
                    float fade = saturate(1.5 - 2.0 * footprint * freq);
                    sum += Noise_Perlin(p * freq, _CloudSeed + i * 1013) * amp * fade;
                    norm += amp; amp *= 0.5; freq *= 2.0;
                }
                return sum / max(norm, 1e-4);
            }

            float Density(float n) { return saturate((n * 0.5 + 0.5 - (1.0 - _CloudCoverage)) * _CloudSharpness); }

            half4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 s = normalize(_SunDirection.xyz);
                float mu = dot(d, s);

                // Sky: the horizon haze thins out exponentially with height; below the horizon it turns into the ground color.
                float haze = exp(-max(d.y, 0.0) * _HorizonFalloff);
                float3 sky = lerp(_ZenithColor.rgb, _HorizonColor.rgb, haze);
                float below = smoothstep(0.0, 0.06, -d.y);
                sky = lerp(sky, _GroundColor.rgb, below);
                float glow = pow(saturate(mu), 8.0) * 0.25 + pow(saturate(mu), 96.0);
                sky += _SunColor.rgb * glow * _SunGlow * (1.0 - below);

                // The sun's disk, with a soft edge of a fifth of its radius.
                float r = radians(max(_SunSize, 0.01));
                float disk = smoothstep(cos(r * 1.2), cos(r * 0.8), mu) * (1.0 - below);

                // Clouds: noise on a layer above the camera (d.xz / d.y), bent down at the horizon so it does not stretch to infinity.
                float3 color = sky + _SunColor.rgb * disk * _SunIntensity;
                if (d.y > 0.0)
                {
                    float2 uv = d.xz / (d.y + 0.12) * _CloudScale + _CloudWind.xy * _HarnessSkyTime;
                    float footprint = max(fwidth(uv.x), fwidth(uv.y));
                    float density = Density(CloudNoise(uv, footprint));
                    // Light from the sun's side: denser cloud toward the sun shades this point, a sun-facing edge is lit.
                    float2 toSun = normalize(s.xz + float2(1e-4, 0.0)) * 0.12;
                    float towardSun = Density(CloudNoise(uv + toSun, footprint));
                    float lit = saturate(1.0 - towardSun * 0.7 + (density - towardSun) * 0.6);
                    float3 cloud = lerp(_CloudShadowColor.rgb, _CloudColor.rgb, lit) * lerp(1.0, _SunColor.rgb, 0.35);
                    cloud += _SunColor.rgb * pow(saturate(mu), 24.0) * (1.0 - density) * 2.0;   // silver lining near the sun
                    cloud = lerp(cloud, _HorizonColor.rgb, haze * 0.65);                       // far clouds fade into the haze
                    float a = density * _CloudOpacity * smoothstep(0.0, 0.12, d.y);
                    color = lerp(color, cloud, a);
                }
                return half4(color * _Exposure, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
