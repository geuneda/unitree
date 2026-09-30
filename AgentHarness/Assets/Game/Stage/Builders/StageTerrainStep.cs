using Harness.Editor;
using Harness.Procedural;
using UnityEngine;

namespace Game.Stage.Builders
{
    /// <summary>
    /// Procedural terrain: ridged mountains around a flat central plateau. The mesh comes from the height function on the CPU;
    /// its textures are baked on the GPU (TerrainBake.shader) from the same heights: a 1024² albedo and normal map over the
    /// whole terrain, plus a 512² tiling detail albedo and normal (URP Lit detail maps) for close-ups.
    /// </summary>
    public sealed class StageTerrainStep : IBuildStep
    {
        public int Order => 10;

        public const float Size = 320f;
        const int Resolution = 200;
        const int HeightField = 257;
        const int TextureSize = 1024;
        const int DetailSize = 512;
        const float DetailTiling = 80f;   // one detail tile per 4 m

        /// <summary>Terrain height at (u, v) in [0, 1] over the terrain (world y). Props place themselves on it.</summary>
        public static float Height(float u, float v, int seed)
        {
            var x = (u - 0.5f) * Size; var z = (v - 0.5f) * Size;
            var r = Mathf.Sqrt(x * x + z * z);
            var ring = PMath.Smoothstep(10f, 70f, r);
            var mountains = Noise.Ridged(u * 5f, v * 5f, 6, 2.05f, 0.5f, seed) * 42f;
            var hills = Noise.Fbm(u * 12f, v * 12f, 5, 2f, 0.5f, seed + 11) * 3.5f;
            return mountains * ring * ring + hills * (0.12f + 0.88f * ring) - 0.4f;
        }

        /// <summary>World-space height at (x, z).</summary>
        public static float HeightAt(float x, float z, int seed) => Height(x / Size + 0.5f, z / Size + 0.5f, seed);

        public void Build(BuildContext ctx)
        {
            var seed = ctx.Seed("terrain");

            // The CPU part is the mesh and the height field the GPU bakes read; cached while this code and the inputs are the same.
            if (!ctx.CacheHit("terrain", new[] { "TerrainMesh.asset", "TerrainAlbedo.png", "TerrainNormal.png", "TerrainDetail.png", "TerrainDetailNormal.png" },
                    Size, Resolution, HeightField, TextureSize, DetailSize, seed))
                Bake(ctx, seed);
            var mesh = ctx.LoadAsset<Mesh>("TerrainMesh.asset");

            var mat = ctx.LitMaterial("Terrain", m =>
            {
                m.BaseMap = ctx.LoadAsset<Texture2D>("TerrainAlbedo.png");
                m.NormalMap = ctx.LoadAsset<Texture2D>("TerrainNormal.png");
                m.DetailAlbedoMap = ctx.LoadAsset<Texture2D>("TerrainDetail.png");
                m.DetailNormalMap = ctx.LoadAsset<Texture2D>("TerrainDetailNormal.png");
                m.DetailTiling = new Vector2(DetailTiling, DetailTiling);
                m.DetailNormalScale = 0.8f;
                m.Smoothness = 0.12f;
            });

            var go = ctx.MeshObject("Terrain", mesh, mat);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;

            ctx.Shot("overview", new Vector3(0f, 30f, -40f), new Vector3(0f, 2f, 0f), 55f);
            ctx.Shot("horizon", new Vector3(-10f, 1.6f, -13f), new Vector3(0f, 3.4f, 0f), 50f);
        }

        static void Bake(BuildContext ctx, int seed)
        {
            ctx.SaveMesh(MeshBuilder.Grid(Size, Size, Resolution, Resolution, (u, v) => Height(u, v, seed)).ToMesh("Terrain"), "TerrainMesh");
            // Heights at texel centers of a 257² grid for the GPU (bilinear in between); the albedo's slope and snow line come from it.
            var heights = BuildContext.FloatTexture(TextureBaker.SampleGrid(HeightField, HeightField, (u, v) => Height(u, v, seed)), HeightField, HeightField, "TerrainHeights");
            try
            {
                void Inputs(Material m) { m.SetTexture("_HeightMap", heights); m.SetFloat("_Size", Size); m.SetInteger("_Seed", seed); }
                ctx.BakeTexture("TerrainAlbedo", TextureSize, TextureSize, "Game/Stage/TerrainBake", Inputs, wrap: TextureWrapMode.Clamp, pass: 0);
                ctx.BakeTexture("TerrainNormal", TextureSize, TextureSize, "Game/Stage/TerrainBake", Inputs, normalMap: true, wrap: TextureWrapMode.Clamp, pass: 1);
                ctx.BakeTexture("TerrainDetail", DetailSize, DetailSize, "Game/Stage/TerrainBake", Inputs, sRGB: false, pass: 2);
                ctx.BakeTexture("TerrainDetailNormal", DetailSize, DetailSize, "Game/Stage/TerrainBake", Inputs, normalMap: true, pass: 3);
            }
            finally { Object.DestroyImmediate(heights); }
        }
    }
}
