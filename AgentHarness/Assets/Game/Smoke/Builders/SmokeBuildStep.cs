using Harness.Editor;
using Harness.Procedural;
using UnityEngine;

namespace Game.Smoke.Builders
{
    /// <summary>Hero object (torus knot, custom HLSL material) on a pedestal, a reflection probe around them, plus a close-up shot.</summary>
    public sealed class SmokeBuildStep : IBuildStep
    {
        public int Order => 100;

        public void Build(BuildContext ctx)
        {
            var knot = MeshBuilder.TorusKnot(radius: 1.6f, tube: 0.42f, p: 2, q: 3, tubularSegments: 320, radialSegments: 32).ToMesh("SpinnerKnot");
            knot = ctx.SaveMesh(knot, "SpinnerKnot");
            var iridescent = ctx.Material("Iridescent", "Game/Smoke/Iridescent", m =>
            {
                m.SetColor("_BaseColor", new Color(0.05f, 0.06f, 0.11f));
                m.SetColor("_RimColorA", new Color(0.15f, 0.85f, 1f));
                m.SetColor("_RimColorB", new Color(1f, 0.25f, 0.75f));
                m.SetFloat("_Emission", 5f);
                m.SetFloat("_Smoothness", 0.9f);     // a polished surface: the probe's stones and arch show in it
                m.SetFloat("_Reflectivity", 0.4f);
            });
            var spinner = ctx.MeshObject("Spinner", knot, iridescent);
            spinner.transform.localPosition = new Vector3(0f, 3.4f, 0f);

            var pedestalMesh = ctx.SaveMesh(MeshBuilder.Box(new Vector3(3.2f, 2.4f, 3.2f)).ToMesh("Pedestal"), "Pedestal");
            var stone = ctx.Material("PedestalStone", "Universal Render Pipeline/Lit", m =>
            {
                // Polished steel: the stones, the arch and the sky show in its sides (the reflection probe below).
                m.SetColor("_BaseColor", new Color(0.78f, 0.79f, 0.83f));
                m.SetFloat("_Smoothness", 0.88f);
                m.SetFloat("_Metallic", 0.92f);
            });
            var pedestal = ctx.MeshObject("Pedestal", pedestalMesh, stone);
            pedestal.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            pedestal.AddComponent<BoxCollider>();
            pedestal.isStatic = true;   // drawn into the reflection probe (the spinning knot and the rings are not: they move)

            // The knot, the rings and the pedestal reflect the stone circle, the arch and the sky around them: a probe rendered from the
            // knot's center after the build, its box reaching the stones (box projection lines the reflections up with them).
            var probe = ctx.ReflectionProbe("Probe", new Vector3(19f, 12.4f, 19f), 256);
            probe.transform.localPosition = new Vector3(0f, 3.4f, 0f);
            probe.center = new Vector3(0f, 2.4f, 0f);   // the box stands on the plateau (y -0.4), not around the capture point

            ctx.UIDocument("HUD", "Assets/Game/Smoke/UI/SmokeHud.uxml");

            ctx.Shot("closeup", new Vector3(4.6f, 4.3f, -6.2f), new Vector3(0f, 3.2f, 0f), 40f);
        }
    }
}
