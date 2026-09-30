using Harness.Editor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Game.Stage.Builders
{
    /// <summary>
    /// Rendering as code: a PC and a Mobile URP asset for the quality levels of the same names (PC is also the project default),
    /// the quality levels themselves and the color space. Anything not set here is URP's default for a new asset; a quality level
    /// field not set here is what ProjectSettings/QualitySettings.asset has.
    /// </summary>
    public sealed class StageRenderSettingsStep : ISettingsStep
    {
        public int Order => 0;

        public void Apply(SettingsContext ctx)
        {
            var pc = ctx.UniversalPipeline("PC", rp =>
            {
                rp.supportsCameraDepthTexture = true;
                rp.supportsCameraOpaqueTexture = true;
                rp.shadowDistance = 50f;
                rp.shadowCascadeCount = 4;
                rp.cascade4Split = new Vector3(0.123f, 0.2926f, 0.536f);
                rp.cascadeBorder = 0.1078f;
                rp.shadowDepthBias = 0.1f;
                rp.shadowNormalBias = 0.5f;
                // No public setters for these.
                SettingsContext.Set(rp, "m_SoftShadowsSupported", true);
                SettingsContext.Set(rp, "m_SoftShadowQuality", SoftShadowQuality.High);
                SettingsContext.Set(rp, "m_AdditionalLightShadowsSupported", true);
                SettingsContext.Set(rp, "m_ReflectionProbeBlending", true);
                SettingsContext.Set(rp, "m_ReflectionProbeBoxProjection", true);
                SettingsContext.Set(rp, "m_SupportsLightLayers", true);
            }, renderer =>
            {
                renderer.renderingMode = RenderingMode.ForwardPlus;
                renderer.useNativeRenderPass = true;
                renderer.copyDepthMode = CopyDepthMode.AfterOpaques;
                renderer.intermediateTextureMode = IntermediateTextureMode.Auto;
                SettingsContext.AddRendererFeature<ScreenSpaceAmbientOcclusion>(renderer, ssao =>
                {
                    SettingsContext.Set(ssao, "m_Settings.Intensity", 0.4f);
                    SettingsContext.Set(ssao, "m_Settings.Radius", 0.3f);
                });
                SettingsContext.AddRendererFeature<DecalRendererFeature>(renderer);   // ctx.Decal (the rune circle, StagePropsStep)
            });

            // The URP template's mobile values: lower resolution, one hard-shadow cascade, no SSAO.
            var mobile = ctx.UniversalPipeline("Mobile", rp =>
            {
                rp.renderScale = 0.8f;
                rp.mainLightShadowmapResolution = 1024;
                SettingsContext.Set(rp, "m_OpaqueDownsampling", Downsampling.None);
                SettingsContext.Set(rp, "m_ReflectionProbeBlending", true);
                SettingsContext.Set(rp, "m_ReflectionProbeBoxProjection", true);
                SettingsContext.Set(rp, "m_SupportsLightLayers", true);
                SettingsContext.Set(rp, "m_AdditionalLightsCookieResolution", LightCookieResolution._1024);
                SettingsContext.Set(rp, "m_AdditionalLightsCookieFormat", LightCookieFormat.GrayscaleHigh);
                SettingsContext.Set(rp, "m_UseFastSRGBLinearConversion", true);
            }, renderer =>
            {
                renderer.useNativeRenderPass = true;
                renderer.shadowTransparentReceive = false;
                renderer.copyDepthMode = CopyDepthMode.AfterOpaques;
                renderer.intermediateTextureMode = IntermediateTextureMode.Auto;
            });

            ctx.UsePipeline(pc);
            ctx.QualityLevels(
                new QualityLevelValues("Mobile")
                {
                    Pipeline = mobile,
                    DefaultFor = new[] { "Android", "iPhone", "WebGL" },
                    ExcludedPlatforms = new[] { "Standalone" },
                    VSyncCount = 0,
                    LodBias = 1f,
                    AnisotropicTextures = AnisotropicFiltering.Enable,
                    SkinWeights = SkinWeights.TwoBones,
                    RealtimeReflectionProbes = false,
                },
                new QualityLevelValues("PC")
                {
                    Pipeline = pc,
                    DefaultFor = new[] { "Standalone" },
                    ExcludedPlatforms = new[] { "Android", "iPhone" },
                    VSyncCount = 0,   // frame times unpaced (the loop's fps, tools/player.ps1)
                    LodBias = 2f,
                    AnisotropicTextures = AnisotropicFiltering.ForceEnable,
                    SkinWeights = SkinWeights.FourBones,
                    RealtimeReflectionProbes = false,
                });
            ctx.Player(p => p.ColorSpace = ColorSpace.Linear);
        }
    }
}
