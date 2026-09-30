using Harness.Editor;
using Harness.Procedural;
using UnityEngine;

namespace Game.Smoke.Builders
{
    /// <summary>
    /// Motion that needs no module code: two glowing rings orbiting the knot (an AnimationClip from code, played through
    /// Playables) and embers rising from the pedestal (a ParticleSystem from code).
    /// </summary>
    public sealed class SmokeFxStep : IBuildStep
    {
        public int Order => 110;

        static readonly Color Cyan = new Color(0.15f, 0.85f, 1f) * 1.3f;
        static readonly Color Magenta = new Color(1f, 0.25f, 0.75f) * 1.3f;

        public void Build(BuildContext ctx)
        {
            // Halo: a gyroscope of two rings around the knot. The clip turns them; module code could switch clips (ClipPlayer.Play).
            var ringMesh = ctx.SaveMesh(MeshBuilder.Torus(majorRadius: 2.6f, minorRadius: 0.03f, majorSegments: 160, minorSegments: 12).ToMesh("HaloRing"), "HaloRing");
            var ringMat = ctx.LitMaterial("HaloRing", m =>
            {
                m.BaseColor = new Color(0.05f, 0.06f, 0.1f);
                m.Metallic = 0.6f;
                m.Smoothness = 0.8f;
                m.Emission = Cyan;
            });
            var halo = ctx.Create("Halo");
            halo.transform.localPosition = new Vector3(0f, 3.4f, 0f);
            ctx.MeshObject("Halo/RingA", ringMesh, ringMat, castShadows: false);
            ctx.MeshObject("Halo/RingB", ringMesh, ringMat, castShadows: false);

            var orbit = ctx.AnimationClip("HaloOrbit", c =>
            {
                c.Loop = true;
                c.Rotation("RingA", (0f, new Vector3(68f, 0f, 0f)), (4f, new Vector3(68f, 360f, 0f))).Linear();
                c.Rotation("RingB", (0f, new Vector3(-56f, 90f, 0f)), (4f, new Vector3(-56f, -270f, 0f))).Linear();
                c.Scale("", (0f, Vector3.one), (2f, Vector3.one * 1.06f), (4f, Vector3.one));
                c.Color("RingA", typeof(MeshRenderer), "material._EmissionColor", (0f, Cyan), (2f, Magenta), (4f, Cyan));
                c.Color("RingB", typeof(MeshRenderer), "material._EmissionColor", (0f, Magenta), (2f, Cyan), (4f, Magenta));
                c.Event(2f, "HaloHalfTurn");
            });
            ctx.Animate(halo, orbit);

            // Embers: additive sparks rising through the knot from a disc over the pedestal, already in the air at the first frame.
            ctx.Particles("Embers", p =>
            {
                p.Prewarm = true;
                p.Lifetime = new ParticleSystem.MinMaxCurve(2.5f, 4.5f);
                p.Speed = new ParticleSystem.MinMaxCurve(0.5f, 1.4f);
                p.Size = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
                p.Color = new Color(1f, 0.45f, 0.12f);   // particle colors are 8-bit: the glow comes from the material's HDR color
                p.Gravity = -0.05f;
                p.Space = ParticleSystemSimulationSpace.World;
                p.Rate = 40f;
                p.MaxParticles = 300;
                p.Angle = 10f;
                p.Radius = 1.9f;
                p.ColorOverLifetime = TextureBaker.Ramp((0f, new Color(1f, 0.9f, 0.5f, 0f)), (0.1f, new Color(1f, 0.8f, 0.4f, 1f)),
                    (0.6f, new Color(1f, 0.4f, 0.2f, 0.8f)), (1f, new Color(0.8f, 0.1f, 0.05f, 0f)));
                p.SizeOverLifetime = AnimationCurve.Linear(0f, 1f, 1f, 0.3f);
                p.NoiseStrength = 0.4f;
                p.NoiseFrequency = 0.6f;
                p.NoiseScrollSpeed = 0.3f;
                p.Material = ctx.ParticleMaterial("Ember", m => { m.Blend = ParticleBlend.Additive; m.Color = Color.white * 2f; });
            }).transform.localPosition = new Vector3(0f, 1.45f, 0f);
        }
    }
}
