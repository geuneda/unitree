using Harness.Editor;
using Harness.Procedural;
using UnityEngine;

namespace Game.Smoke.Builders
{
    /// <summary>Hero object (torus knot, custom HLSL material) on a pedestal, plus a close-up shot.</summary>
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
            });
            var spinner = ctx.MeshObject("Spinner", knot, iridescent);
            spinner.transform.localPosition = new Vector3(0f, 3.4f, 0f);

            var pedestalMesh = ctx.SaveMesh(MeshBuilder.Box(new Vector3(3.2f, 2.4f, 3.2f)).ToMesh("Pedestal"), "Pedestal");
            var stone = ctx.Material("PedestalStone", "Universal Render Pipeline/Lit", m =>
            {
                m.SetColor("_BaseColor", new Color(0.18f, 0.18f, 0.2f));
                m.SetFloat("_Smoothness", 0.55f);
                m.SetFloat("_Metallic", 0.2f);
            });
            var pedestal = ctx.MeshObject("Pedestal", pedestalMesh, stone);
            pedestal.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            pedestal.AddComponent<BoxCollider>();

            ctx.UIDocument("HUD", "Assets/Game/Smoke/UI/SmokeHud.uxml");

            ctx.Shot("closeup", new Vector3(4.6f, 4.3f, -6.2f), new Vector3(0f, 3.2f, 0f), 40f);
        }
    }
}
