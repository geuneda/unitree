using Harness.Editor;
using Harness.Procedural;
using UnityEngine;

namespace Game.Stage.Builders
{
    /// <summary>Procedural terrain: ridged mountains around a flat central plateau, with a baked albedo + detail normal map.</summary>
    public sealed class StageTerrainStep : IBuildStep
    {
        public int Order => 10;

        const float Size = 320f;
        const int Resolution = 200;
        const int TextureSize = 512;

        public void Build(BuildContext ctx)
        {
            var seed = ctx.Seed("terrain");

            float Height(float u, float v)
            {
                var x = (u - 0.5f) * Size; var z = (v - 0.5f) * Size;
                var r = Mathf.Sqrt(x * x + z * z);
                var ring = PMath.Smoothstep(10f, 70f, r);
                var mountains = Noise.Ridged(u * 5f, v * 5f, 6, 2.05f, 0.5f, seed) * 42f;
                var hills = Noise.Fbm(u * 12f, v * 12f, 5, 2f, 0.5f, seed + 11) * 3.5f;
                return mountains * ring * ring + hills * (0.12f + 0.88f * ring) - 0.4f;
            }

            // The bake is ~1 s; skip it when this assembly, Harness.Runtime and the inputs are unchanged.
            if (!ctx.CacheHit("terrain", new[] { "TerrainMesh.asset", "TerrainAlbedo.png", "TerrainNormal.png" }, Size, Resolution, TextureSize, seed))
                Bake(ctx, Height, seed);
            var mesh = ctx.LoadAsset<Mesh>("TerrainMesh.asset");
            var albedo = ctx.LoadAsset<Texture2D>("TerrainAlbedo.png");
            var normal = ctx.LoadAsset<Texture2D>("TerrainNormal.png");

            var mat = ctx.LitMaterial("Terrain", m =>
            {
                m.BaseMap = albedo;
                m.NormalMap = normal;
                m.Smoothness = 0.12f;
            });

            var go = ctx.MeshObject("Terrain", mesh, mat);
            go.AddComponent<MeshCollider>().sharedMesh = mesh;
            go.isStatic = true;

            ctx.Shot("overview", new Vector3(0f, 30f, -40f), new Vector3(0f, 2f, 0f), 55f);
            ctx.Shot("horizon", new Vector3(-10f, 1.6f, -13f), new Vector3(0f, 3.4f, 0f), 50f);
        }

        static void Bake(BuildContext ctx, System.Func<float, float, float> Height, int seed)
        {
            ctx.SaveMesh(MeshBuilder.Grid(Size, Size, Resolution, Resolution, Height).ToMesh("Terrain"), "TerrainMesh");

            // Evaluate each field once on the texel grid; derive slope and normals from neighbours.
            const int N = TextureSize;
            var heights = TextureBaker.SampleGrid(N, N, Height);
            var variation = TextureBaker.SampleGrid(N, N, (u, v) => Noise.Fbm(u * 60f, v * 60f, 3, 2f, 0.5f, seed + 5) * 0.5f + 0.5f);
            var detail = TextureBaker.SampleGrid(N, N, (u, v) => Noise.Fbm(u * 140f, v * 140f, 4, 2f, 0.5f, seed + 23) * 0.3f);
            var texel = Size / N;

            var grass = new Color(0.16f, 0.25f, 0.08f);
            var dry = new Color(0.38f, 0.33f, 0.17f);
            var rock = new Color(0.30f, 0.27f, 0.24f);
            var snow = new Color(0.90f, 0.92f, 0.96f);
            var albedo = TextureBaker.Bake(N, N, (u, v) =>
            {
                var x = Mathf.Min(N - 1, (int)(u * N)); var y = Mathf.Min(N - 1, (int)(v * N));
                var h = heights[y * N + x];
                var dx = (TextureBaker.At(heights, N, N, x + 1, y) - TextureBaker.At(heights, N, N, x - 1, y)) / (2f * texel);
                var dz = (TextureBaker.At(heights, N, N, x, y + 1) - TextureBaker.At(heights, N, N, x, y - 1)) / (2f * texel);
                var slope = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dz * dz));
                var n = variation[y * N + x];
                var c = PMath.Mix(grass, dry, PMath.Smoothstep(0.4f, 0.8f, n));
                c = PMath.Mix(c, rock, PMath.Smoothstep(0.45f, 0.85f, slope));
                c = PMath.Mix(c, snow, PMath.Smoothstep(20f, 30f, h + n * 6f) * (1f - PMath.Smoothstep(0.6f, 1.2f, slope)));
                c *= 0.8f + 0.4f * n;
                c.a = 1f;
                return c;
            }, wrap: TextureWrapMode.Clamp, name: "TerrainAlbedo");
            ctx.SaveTexture(albedo, "TerrainAlbedo");

            var normal = TextureBaker.NormalMapFromGrid(detail, N, N, strength: 1f / (2f * texel), name: "TerrainNormal");
            ctx.SaveTexture(normal, "TerrainNormal", sRGB: false, normalMap: true);
        }
    }
}
