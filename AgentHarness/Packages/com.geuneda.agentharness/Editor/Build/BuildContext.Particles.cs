#if AGENTHARNESS_PARTICLES
using System;
using System.Collections.Generic;
using Harness.Procedural;
using UnityEngine;
using UnityEngine.Rendering;

namespace Harness.Editor
{
    /// <summary>How particles combine with what is behind them (URP particle shaders' Blending Mode).</summary>
    public enum ParticleBlend { Alpha = 0, Premultiply = 1, Additive = 2, Multiply = 3 }

    /// <summary>Settings for <see cref="BuildContext.ParticleMaterial"/> (URP Particles/Unlit, transparent). Unset = the default.</summary>
    public sealed class ParticleMaterialSettings
    {
        /// <summary>The particle's texture; null = a generated soft round dot (ParticleDot.png).</summary>
        public Texture Texture;
        /// <summary>Multiplies the texture and the particle color (HDR values glow with bloom).</summary>
        public Color Color = Color.white;
        public ParticleBlend Blend = ParticleBlend.Alpha;
        /// <summary>Fade out where particles meet opaque geometry (needs the depth texture, on in the sample's pipeline).</summary>
        public bool SoftParticles;
        public float SoftNear;
        public float SoftFar = 1f;
        public CullMode Cull = CullMode.Back;
    }

    /// <summary>
    /// Settings for <see cref="BuildContext.Particles"/>: the modules most effects use, with defaults that work. Ranges and curves
    /// are Unity's own types: a float converts to <c>ParticleSystem.MinMaxCurve</c>, <c>new MinMaxCurve(min, max)</c> is a random range;
    /// a Color or Gradient converts to <c>MinMaxGradient</c>. Anything else: change the returned ParticleSystem's modules.
    /// </summary>
    public sealed class ParticleSettings
    {
        // Main module
        public float Duration = 5f;
        public bool Loop = true;
        /// <summary>Start as if the system had already run one Duration (only when looping): the first frame is full.</summary>
        public bool Prewarm;
        /// <summary>Seconds a particle lives.</summary>
        public ParticleSystem.MinMaxCurve Lifetime = 2f;
        public ParticleSystem.MinMaxCurve Speed = 1f;
        public ParticleSystem.MinMaxCurve Size = 0.2f;
        /// <summary>Start rotation in degrees.</summary>
        public ParticleSystem.MinMaxCurve Rotation = 0f;
        /// <summary>Start color. Particle colors are 8-bit (0..1): for a glow, give the material an HDR color (<c>ParticleMaterialSettings.Color</c>).</summary>
        public ParticleSystem.MinMaxGradient Color = UnityEngine.Color.white;
        /// <summary>Multiplier of Physics.gravity (1 = falls like a rigidbody, negative rises).</summary>
        public float Gravity;
        /// <summary>World: emitted particles stay behind when the emitter moves (smoke, sparks). Local: they move with it.</summary>
        public ParticleSystemSimulationSpace Space = ParticleSystemSimulationSpace.Local;
        public int MaxParticles = 1000;

        // Emission
        /// <summary>Particles per second.</summary>
        public ParticleSystem.MinMaxCurve Rate = 10f;
        public readonly List<ParticleSystem.Burst> Bursts = new List<ParticleSystem.Burst>();
        /// <summary><paramref name="count"/> particles at once at <paramref name="time"/> s into each cycle.</summary>
        public void Burst(float time, short count, int cycles = 1, float interval = 0.01f) => Bursts.Add(new ParticleSystem.Burst(time, count, count, cycles, interval));

        // Shape
        public ParticleSystemShapeType Shape = ParticleSystemShapeType.Cone;
        /// <summary>Cone half angle in degrees.</summary>
        public float Angle = 25f;
        public float Radius = 1f;
        /// <summary>0 = emit from the edge (circle, sphere) only, 1 = from the whole volume.</summary>
        public float RadiusThickness = 1f;
        public float Arc = 360f;
        public Vector3 BoxSize = Vector3.one;
        /// <summary>The shape's rotation. The default turns cones and circles to emit up (+Y) like a new Particle System from the menu.</summary>
        public Vector3 ShapeRotation = new Vector3(-90f, 0f, 0f);
        public Vector3 ShapePosition;

        // Over lifetime (unset = module off)
        /// <summary>Multiplies <see cref="Color"/> over each particle's life (alpha fades it): <c>TextureBaker.Ramp(...)</c>.</summary>
        public Gradient ColorOverLifetime;
        /// <summary>Multiplies <see cref="Size"/> over each particle's life (x = 0..1 of the life): <c>AnimationCurve.Linear(0, 1, 1, 0)</c> shrinks.</summary>
        public AnimationCurve SizeOverLifetime;
        /// <summary>Spin in degrees per second.</summary>
        public ParticleSystem.MinMaxCurve? RotationOverLifetime;
        /// <summary>Added velocity (units/s) in <see cref="VelocitySpace"/>; zero = off.</summary>
        public Vector3 Velocity;
        public ParticleSystemSimulationSpace VelocitySpace = ParticleSystemSimulationSpace.Local;
        /// <summary>Slows particles down (Limit Velocity over Lifetime's drag); 0 = off.</summary>
        public float Drag;
        /// <summary>Turbulence (Noise module) in units/s; 0 = off.</summary>
        public float NoiseStrength;
        public float NoiseFrequency = 0.5f;
        public float NoiseScrollSpeed;

        // Renderer
        /// <summary>null = the module's shared default (<c>ctx.ParticleMaterial("ParticleDefault")</c>: alpha-blended soft dot).</summary>
        public Material Material;
        public ParticleSystemRenderMode RenderMode = ParticleSystemRenderMode.Billboard;
        /// <summary>Stretched billboards: length relative to the size.</summary>
        public float LengthScale = 2f;
        /// <summary>Stretched billboards: extra length per unit of speed.</summary>
        public float VelocityScale;
        /// <summary>RenderMode Mesh: the mesh each particle draws.</summary>
        public Mesh Mesh;
        public bool CastShadows;
        public bool ReceiveShadows;
        public ParticleSystemSortMode SortMode = ParticleSystemSortMode.None;
    }

    public sealed partial class BuildContext
    {
        const string UrpParticlesUnlit = "Universal Render Pipeline/Particles/Unlit";

        /// <summary>
        /// A ParticleSystem at <paramref name="path"/> from <see cref="ParticleSettings"/>. It plays when the scene starts, with a fixed
        /// random seed from the module and path (the same particles every run and every build; a scenario with a fixed time step
        /// captures the same frame) and keeps simulating off screen (culling would make a later frame depend on what a camera saw).
        /// Edit-mode captures show no particles; the play-mode ones do.
        /// </summary>
        public ParticleSystem Particles(string path, Action<ParticleSettings> setup)
        {
            var s = new ParticleSettings();
            setup?.Invoke(s);
            var go = Create(path, typeof(ParticleSystem));
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);   // duration and seed can only change while stopped
            ps.useAutoRandomSeed = false;
            ps.randomSeed = unchecked((uint)Seed("particles/" + path));

            var main = ps.main;
            main.duration = s.Duration;
            main.loop = s.Loop;
            main.prewarm = s.Prewarm && s.Loop;
            main.startLifetime = s.Lifetime;
            main.startSpeed = s.Speed;
            main.startSize = s.Size;
            main.startRotation = Scaled(s.Rotation, Mathf.Deg2Rad);
            main.startColor = s.Color;
            main.gravityModifier = s.Gravity;
            main.simulationSpace = s.Space;
            main.maxParticles = s.MaxParticles;
            main.playOnAwake = true;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
            if (s.Prewarm && !s.Loop) Warn($"particles '{path}': Prewarm only works on a looping system");

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = s.Rate;
            emission.rateOverDistance = 0f;
            emission.SetBursts(s.Bursts.ToArray());

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = s.Shape;
            shape.angle = s.Angle;
            shape.radius = s.Radius;
            shape.radiusThickness = s.RadiusThickness;
            shape.arc = s.Arc;
            shape.scale = s.Shape == ParticleSystemShapeType.Box || s.Shape == ParticleSystemShapeType.BoxShell || s.Shape == ParticleSystemShapeType.BoxEdge ? s.BoxSize : Vector3.one;
            shape.rotation = s.ShapeRotation;
            shape.position = s.ShapePosition;

            var col = ps.colorOverLifetime;
            col.enabled = s.ColorOverLifetime != null;
            if (s.ColorOverLifetime != null) col.color = s.ColorOverLifetime;

            var size = ps.sizeOverLifetime;
            size.enabled = s.SizeOverLifetime != null;
            if (s.SizeOverLifetime != null) size.size = new ParticleSystem.MinMaxCurve(1f, s.SizeOverLifetime);

            var rot = ps.rotationOverLifetime;
            rot.enabled = s.RotationOverLifetime.HasValue;
            if (s.RotationOverLifetime.HasValue) rot.z = Scaled(s.RotationOverLifetime.Value, Mathf.Deg2Rad);

            var vel = ps.velocityOverLifetime;
            vel.enabled = s.Velocity != Vector3.zero;
            if (vel.enabled)
            {
                vel.space = s.VelocitySpace;
                vel.x = s.Velocity.x; vel.y = s.Velocity.y; vel.z = s.Velocity.z;
            }

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = s.Drag > 0f;
            if (limit.enabled) limit.drag = s.Drag;

            var noise = ps.noise;
            noise.enabled = s.NoiseStrength > 0f;
            if (noise.enabled)
            {
                noise.strength = s.NoiseStrength;
                noise.frequency = s.NoiseFrequency;
                noise.scrollSpeed = s.NoiseScrollSpeed;
            }

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = s.RenderMode;
            r.lengthScale = s.LengthScale;
            r.velocityScale = s.VelocityScale;
            r.mesh = s.RenderMode == ParticleSystemRenderMode.Mesh ? s.Mesh : null;
            if (s.RenderMode == ParticleSystemRenderMode.Mesh && s.Mesh == null) Warn($"particles '{path}': RenderMode Mesh without a Mesh draws nothing");
            r.sharedMaterial = s.Material != null ? s.Material : ParticleMaterial("ParticleDefault");
            r.shadowCastingMode = s.CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.receiveShadows = s.ReceiveShadows;
            r.sortMode = s.SortMode;
            return ps;
        }

        /// <summary>
        /// A URP Particles/Unlit material (transparent; keywords and blend state follow from the settings, like <see cref="LitMaterial"/>).
        /// Without a texture it uses a generated soft round dot, so a particle is not a square.
        /// </summary>
        public Material ParticleMaterial(string name, Action<ParticleMaterialSettings> setup = null)
        {
            var s = new ParticleMaterialSettings();
            setup?.Invoke(s);
            var shader = Shader.Find(UrpParticlesUnlit);
            if (shader == null) throw new InvalidOperationException($"ParticleMaterial needs URP (shader '{UrpParticlesUnlit}' not found); use ctx.Material with the pipeline's particle shader");
            var texture = s.Texture != null ? s.Texture : ParticleDot();
            return Material(name, shader, m =>
            {
                m.SetTexture("_BaseMap", texture);
                m.SetColor("_BaseColor", s.Color);
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", (float)s.Blend);
                m.SetFloat("_Cull", (float)s.Cull);
                m.SetFloat("_SoftParticlesEnabled", s.SoftParticles ? 1f : 0f);
                if (s.SoftParticles)
                {
                    m.SetFloat("_SoftParticlesNearFadeDistance", s.SoftNear);
                    m.SetFloat("_SoftParticlesFarFadeDistance", s.SoftFar);
                }
            });
        }

        /// <summary>A white dot with a soft edge in alpha (64x64), this module's ParticleDot.png.</summary>
        Texture2D ParticleDot()
        {
            var tex = TextureBaker.Bake(64, 64, (u, v) =>
            {
                var d = Mathf.Clamp01(1f - new Vector2(u - 0.5f, v - 0.5f).magnitude * 2f);
                return new Color(1f, 1f, 1f, d * d * (3f - 2f * d));
            }, wrap: TextureWrapMode.Clamp, name: "ParticleDot");
            return SaveTexture(tex, "ParticleDot");
        }

        static ParticleSystem.MinMaxCurve Scaled(ParticleSystem.MinMaxCurve c, float k)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant: return c.constant * k;
                case ParticleSystemCurveMode.TwoConstants: return new ParticleSystem.MinMaxCurve(c.constantMin * k, c.constantMax * k);
                default: c.curveMultiplier *= k; return c;
            }
        }
    }
}
#endif
