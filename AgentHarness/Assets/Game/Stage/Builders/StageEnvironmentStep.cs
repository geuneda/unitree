using Harness;
using Harness.Editor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Game.Stage.Builders
{
    /// <summary>Camera, sun, sky, ambient, fog and the global post-processing Volume.</summary>
    public sealed class StageEnvironmentStep : IBuildStep
    {
        public int Order => 0;

        public void Build(BuildContext ctx)
        {
            // Camera --------------------------------------------------------------------------------
            var camGo = ctx.Create("Main Camera", typeof(Camera), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(0f, 7f, -16f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 2.8f, 0f) - camGo.transform.position);
            var cam = camGo.GetComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 600f;
            cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.Skybox;
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;
            camData.renderShadows = true;
            camData.dithering = true;

            // Sun ------------------------------------------------------------------------------------
            var sunGo = ctx.Create("Sun", typeof(Light));
            sunGo.transform.rotation = Quaternion.Euler(32f, -38f, 0f);
            var sun = sunGo.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.93f, 0.82f);
            sun.intensity = 2.2f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;

            // Beacon (flashed by StageModule on SpinnerLap) --------------------------------------------
            var beaconGo = ctx.Create("Beacon", typeof(Light));
            beaconGo.transform.position = new Vector3(0f, 6.5f, 0f);
            var beacon = beaconGo.GetComponent<Light>();
            beacon.type = LightType.Point;
            beacon.color = new Color(0.35f, 0.8f, 1f);
            beacon.range = 18f;
            beacon.intensity = 3f;
            beacon.shadows = LightShadows.None;

            // Sky, ambient, fog -------------------------------------------------------------------------
            var sky = ctx.Material("Sky", "Skybox/Procedural", m =>
            {
                m.SetFloat("_SunDisk", 2f);          // high quality
                m.SetFloat("_SunSize", 0.035f);
                m.SetFloat("_SunSizeConvergence", 6f);
                m.SetFloat("_AtmosphereThickness", 0.85f);
                m.SetColor("_SkyTint", new Color(0.45f, 0.55f, 0.75f));
                // Below-horizon sky ≈ fog color, so the world edge dissolves instead of showing a dark band.
                m.SetColor("_GroundColor", new Color(0.55f, 0.6f, 0.66f));
                m.SetFloat("_Exposure", 1.15f);
            });
            RenderSettings.skybox = sky;
            RenderSettings.sun = sun;
            ctx.BakeSkyReflection();
            // Trilight ambient needs no baking (skybox ambient would require a lighting bake per build).
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.62f, 0.8f);
            RenderSettings.ambientEquatorColor = new Color(0.42f, 0.42f, 0.45f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.13f, 0.1f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.66f, 0.72f, 0.8f);
            RenderSettings.fogDensity = 0.0045f;

            // Post-processing ---------------------------------------------------------------------------
            var profile = ctx.VolumeProfile("PostFX", p =>
            {
                var tone = p.Add<Tonemapping>(true);
                tone.mode.value = TonemappingMode.ACES;

                var bloom = p.Add<Bloom>(true);
                bloom.threshold.value = 1.0f;
                bloom.intensity.value = 0.9f;
                bloom.scatter.value = 0.72f;
                bloom.highQualityFiltering.value = true;

                var color = p.Add<ColorAdjustments>(true);
                color.postExposure.value = 0.15f;
                color.contrast.value = 14f;
                color.saturation.value = 10f;
                color.colorFilter.value = Color.white;
                color.hueShift.value = 0f;

                var vignette = p.Add<Vignette>(true);
                vignette.intensity.value = 0.24f;
                vignette.smoothness.value = 0.45f;
                vignette.color.value = Color.black;
                vignette.center.value = new Vector2(0.5f, 0.5f);
                vignette.rounded.value = false;
            });
            var volGo = ctx.Create("PostFX", typeof(Volume));
            var volume = volGo.GetComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.sharedProfile = profile;
        }
    }
}
