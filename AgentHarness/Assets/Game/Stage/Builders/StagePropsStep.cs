using System.Collections.Generic;
using Harness.Editor;
using Harness.Procedural;
using UnityEngine;

namespace Game.Stage.Builders
{
    /// <summary>
    /// Props from the procedural library: standing stones in a circle around the plateau (SDF shapes meshed with surface nets),
    /// rocks scattered over the hills (Poisson disk, filtered by slope, one combined mesh), a stone arch (a tube along a spline)
    /// and a rune circle on the ground (a URP decal whose texture is baked on the GPU).
    /// </summary>
    public sealed class StagePropsStep : IBuildStep
    {
        public int Order => 20;

        const int StoneCount = 9;
        const float StoneRing = 8.6f;

        public void Build(BuildContext ctx)
        {
            var terrainSeed = ctx.Seed("terrain");   // the Stage module's terrain seed (StageTerrainStep)
            var seed = ctx.Seed("props");

            // Textures (GPU bakes; skipped when the shader and inputs are unchanged).
            var rockTex = ctx.BakeTexture("RockAlbedo", 256, 256, "Game/Stage/PropsBake", m => m.SetInteger("_Seed", seed), pass: 1);
            var stoneTex = ctx.BakeTexture("StoneAlbedo", 256, 256, "Game/Stage/PropsBake", m => m.SetInteger("_Seed", seed + 1), pass: 2);
            var runeTex = ctx.BakeTexture("Runes", 512, 512, "Game/Stage/PropsBake", m => m.SetInteger("_Seed", seed + 2), wrap: TextureWrapMode.Clamp, pass: 0);
            var rockMat = ctx.LitMaterial("Rock", m => { m.BaseMap = rockTex; m.Tiling = Vector2.one * 0.8f; m.Smoothness = 0.08f; });
            var stoneMat = ctx.LitMaterial("Stone", m => { m.BaseMap = stoneTex; m.Tiling = new Vector2(0.9f, 0.45f); m.Smoothness = 0.15f; });

            // Meshes (CPU; cached while this code and Harness.Runtime are the same).
            if (!ctx.CacheHit("props", new[] { "StandingStones.asset", "Rocks.asset", "Arch.asset" }, seed, terrainSeed, StoneCount, StoneRing))
            {
                ctx.SaveMesh(StandingStones(seed, terrainSeed).ToMesh("StandingStones"), "StandingStones");
                ctx.SaveMesh(Rocks(seed, terrainSeed).ToMesh("Rocks"), "Rocks");
                ctx.SaveMesh(Arch().ToMesh("Arch"), "Arch");
            }
            void Prop(string path, string mesh, Material mat)
            {
                var go = ctx.MeshObject(path, ctx.LoadAsset<Mesh>(mesh), mat);
                go.layer = StageProjectSettingsStep.Props;
                go.isStatic = true;
            }
            Prop("Props/StandingStones", "StandingStones.asset", stoneMat);
            Prop("Props/Rocks", "Rocks.asset", rockMat);
            Prop("Props/Arch", "Arch.asset", stoneMat);

            // Rune circle around the pedestal, projected down onto the plateau.
            var decal = ctx.Decal("Props/RuneCircle", ctx.DecalMaterial("Runes", runeTex), new Vector3(8f, 8f, 3f));
            decal.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        }

        /// <summary>Rounded slabs with a notch and a noisy surface, three shapes around the circle, each standing on the terrain.</summary>
        static MeshBuilder StandingStones(int seed, int terrainSeed)
        {
            var all = new MeshBuilder();
            var shapes = new MeshBuilder[3];
            for (var s = 0; s < shapes.Length; s++)
            {
                var k = seed + s * 101;
                var half = new Vector3(0.55f, 1.55f + 0.25f * s, 0.32f);
                float Stone(Vector3 p)
                {
                    var slab = Sdf.RoundBox(p, half, 0.16f);
                    var notch = Sdf.Sphere(p - new Vector3(0.45f * (s - 1), half.y + 0.15f, 0f), 0.42f);
                    return Sdf.SmoothSubtract(slab, notch, 0.12f) + Noise.Perlin(p.x * 3f, p.y * 3f, p.z * 3f, k) * 0.05f;
                }
                shapes[s] = MeshBuilder.FromSdf(Stone, new Bounds(Vector3.zero, half * 2f + Vector3.one * 0.5f), 0.07f, uvScale: 0.5f);
            }
            var rng = new Noise.Rng(seed);
            for (var i = 0; i < StoneCount; i++)
            {
                var a = (i + 0.5f) / StoneCount * Mathf.PI * 2f + rng.Range(-0.08f, 0.08f);
                var pos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * StoneRing;
                var shape = shapes[i % shapes.Length];
                var baseY = StageTerrainStep.HeightAt(pos.x, pos.z, terrainSeed);
                pos.y = baseY + 1.35f;   // sunk a little into the ground
                var rot = Quaternion.Euler(rng.Range(-4f, 4f), -a * Mathf.Rad2Deg + 90f + rng.Range(-10f, 10f), rng.Range(-5f, 5f));
                all.Append(shape, Matrix4x4.TRS(pos, rot, Vector3.one * rng.Range(0.9f, 1.1f)));
            }
            return all;
        }

        /// <summary>
        /// Rocks on the hills: Poisson-disk points, kept where the ground is neither the plateau nor too steep. Rocks near the plateau
        /// get four times the facets of the far ones (the combined mesh is loaded and hashed every build).
        /// </summary>
        static MeshBuilder Rocks(int seed, int terrainSeed)
        {
            var near = new MeshBuilder[2];
            var far = new MeshBuilder[2];
            for (var i = 0; i < 2; i++)
            {
                near[i] = MeshBuilder.Rock(seed + i * 17, 1f, 2, 0.4f, new Vector3(1f, 0.7f, 0.9f));
                far[i] = MeshBuilder.Rock(seed + 100 + i * 17, 1f, 1, 0.4f, new Vector3(1f, 0.7f, 0.9f));
            }
            var all = new MeshBuilder();
            var rng = new Noise.Rng(seed + 5);
            var half = StageTerrainStep.Size * 0.45f;
            foreach (var p in Scatter.Poisson(new Rect(-half, -half, half * 2f, half * 2f), 5.5f, seed + 7))
            {
                var r = p.magnitude;
                if (r < 14f || r > 120f) continue;
                var h = StageTerrainStep.HeightAt(p.x, p.y, terrainSeed);
                var dx = StageTerrainStep.HeightAt(p.x + 1f, p.y, terrainSeed) - StageTerrainStep.HeightAt(p.x - 1f, p.y, terrainSeed);
                var dz = StageTerrainStep.HeightAt(p.x, p.y + 1f, terrainSeed) - StageTerrainStep.HeightAt(p.x, p.y - 1f, terrainSeed);
                if (Mathf.Sqrt(dx * dx + dz * dz) * 0.5f > 0.7f) continue;   // too steep to rest on
                var size = rng.Range(0.35f, 1.3f) * (1f + Mathf.Clamp01((r - 14f) / 60f));
                var rot = Quaternion.Euler(rng.Range(-15f, 15f), rng.Range(0f, 360f), rng.Range(-15f, 15f));
                var variants = r < 45f ? near : far;
                all.Append(variants[rng.Range(0, variants.Length)], Matrix4x4.TRS(new Vector3(p.x, h + size * 0.2f, p.y), rot, Vector3.one * size));
            }
            return all;
        }

        /// <summary>An arch behind the pedestal: a tube along a spline, thick at the feet, thin at the top.</summary>
        static MeshBuilder Arch()
        {
            var spline = new Spline(new List<Vector3>
            {
                new Vector3(-5.5f, -0.5f, 6.5f), new Vector3(-5f, 4f, 6.8f), new Vector3(-2.5f, 7.5f, 7f), new Vector3(0f, 8.4f, 7f),
                new Vector3(2.5f, 7.5f, 7f), new Vector3(5f, 4f, 6.8f), new Vector3(5.5f, -0.5f, 6.5f),
            });
            return MeshBuilder.Tube(spline, t => Mathf.Lerp(0.55f, 0.32f, Mathf.Sin(t * Mathf.PI)), 96, 16);
        }
    }
}
