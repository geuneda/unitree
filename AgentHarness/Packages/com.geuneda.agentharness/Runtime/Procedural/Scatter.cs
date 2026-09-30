using System;
using System.Collections.Generic;
using UnityEngine;

namespace Harness.Procedural
{
    /// <summary>
    /// Placing many things without clumps or overlaps, deterministically from a seed. Filter the points yourself (height, slope,
    /// distance from the center), then bake the copies into one mesh (<see cref="MeshBuilder.Append"/>) or place objects.
    /// </summary>
    public static class Scatter
    {
        /// <summary>
        /// Poisson-disk points in <paramref name="area"/> (Bridson's algorithm): no two closer than <paramref name="minDistance"/>, no large
        /// gaps. The same seed gives the same points in the same order.
        /// </summary>
        public static List<Vector2> Poisson(Rect area, float minDistance, int seed, int maxPoints = int.MaxValue, int attempts = 30)
        {
            if (!(minDistance > 0f)) throw new ArgumentException("minDistance must be positive");
            var rng = new Noise.Rng(seed);
            var cell = minDistance / Mathf.Sqrt(2f);
            var gw = Mathf.Max(1, Mathf.CeilToInt(area.width / cell));
            var gh = Mathf.Max(1, Mathf.CeilToInt(area.height / cell));
            var grid = new int[gw * gh];
            for (var i = 0; i < grid.Length; i++) grid[i] = -1;
            var points = new List<Vector2>();
            var active = new List<int>();
            var r2 = minDistance * minDistance;

            bool Fits(Vector2 p)
            {
                if (!area.Contains(p)) return false;
                var gx = Mathf.Min(gw - 1, (int)((p.x - area.xMin) / cell));
                var gy = Mathf.Min(gh - 1, (int)((p.y - area.yMin) / cell));
                for (var y = Mathf.Max(0, gy - 2); y <= Mathf.Min(gh - 1, gy + 2); y++)
                for (var x = Mathf.Max(0, gx - 2); x <= Mathf.Min(gw - 1, gx + 2); x++)
                {
                    var j = grid[y * gw + x];
                    if (j >= 0 && (points[j] - p).sqrMagnitude < r2) return false;
                }
                return true;
            }

            void Add(Vector2 p)
            {
                var gx = Mathf.Min(gw - 1, (int)((p.x - area.xMin) / cell));
                var gy = Mathf.Min(gh - 1, (int)((p.y - area.yMin) / cell));
                grid[gy * gw + gx] = points.Count;
                active.Add(points.Count);
                points.Add(p);
            }

            Add(new Vector2(rng.Range(area.xMin, area.xMax), rng.Range(area.yMin, area.yMax)));
            while (active.Count > 0 && points.Count < maxPoints)
            {
                var ai = rng.Range(0, active.Count);
                var center = points[active[ai]];
                var placed = false;
                for (var k = 0; k < attempts && points.Count < maxPoints; k++)
                {
                    var angle = rng.Next01() * Mathf.PI * 2f;
                    var dist = minDistance * (1f + rng.Next01());
                    var p = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * dist;
                    if (!Fits(p)) continue;
                    Add(p);
                    placed = true;
                    break;
                }
                if (!placed) { active[ai] = active[active.Count - 1]; active.RemoveAt(active.Count - 1); }
            }
            return points;
        }
    }

    public sealed partial class MeshBuilder
    {
        /// <summary>
        /// A rock: an icosphere (<paramref name="subdivisions"/> 0-4) pushed in and out by 3D noise, squashed by <paramref name="scale"/>,
        /// with flat facets (each triangle its own normal). Each seed is a different rock.
        /// </summary>
        public static MeshBuilder Rock(int seed, float radius = 1f, int subdivisions = 2, float roughness = 0.35f, Vector3? scale = null)
        {
            var s = scale ?? Vector3.one;
            var ico = Icosphere(Mathf.Clamp(subdivisions, 0, 4));
            var shaped = new Vector3[ico.Vertices.Count];
            for (var i = 0; i < shaped.Length; i++)
            {
                var n = ico.Vertices[i];
                var bump = Noise.Perlin(n.x * 1.3f + 11f, n.y * 1.3f, n.z * 1.3f, seed) * 0.7f
                         + Noise.Perlin(n.x * 3.1f, n.y * 3.1f + 5f, n.z * 3.1f, seed + 1) * 0.3f;
                shaped[i] = Vector3.Scale(n * radius * (1f + roughness * bump), s);
            }
            // Flat shading: three fresh vertices per triangle with its face normal.
            var b = new MeshBuilder();
            for (var t = 0; t < ico.Triangles.Count; t += 3)
            {
                var a = shaped[ico.Triangles[t]]; var c = shaped[ico.Triangles[t + 1]]; var d = shaped[ico.Triangles[t + 2]];
                var n = Vector3.Cross(c - a, d - a).normalized;
                var i0 = b.AddVertex(a, BoxUv(a, n), n);
                var i1 = b.AddVertex(c, BoxUv(c, n), n);
                var i2 = b.AddVertex(d, BoxUv(d, n), n);
                b.AddTriangle(i0, i1, i2);
            }
            return b;
        }

        /// <summary>Unit icosphere (vertices on the sphere, normals = positions), <paramref name="subdivisions"/> times split.</summary>
        public static MeshBuilder Icosphere(int subdivisions = 2)
        {
            var t = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            for (var i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            var tris = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (var s = 0; s < subdivisions; s++)
            {
                var mid = new Dictionary<long, int>();
                int Mid(int a, int c)
                {
                    var key = a < c ? ((long)a << 32) | (uint)c : ((long)c << 32) | (uint)a;
                    if (mid.TryGetValue(key, out var m)) return m;
                    verts.Add(((verts[a] + verts[c]) * 0.5f).normalized);
                    return mid[key] = verts.Count - 1;
                }
                var next = new List<int>(tris.Count * 4);
                for (var i = 0; i < tris.Count; i += 3)
                {
                    int a = tris[i], c = tris[i + 1], d = tris[i + 2];
                    int ab = Mid(a, c), bc = Mid(c, d), ca = Mid(d, a);
                    next.AddRange(new[] { a, ab, ca, c, bc, ab, d, ca, bc, ab, bc, ca });
                }
                tris = next;
            }
            var b = new MeshBuilder();
            foreach (var v in verts) b.AddVertex(v, new Vector2(Mathf.Atan2(v.z, v.x) / (2f * Mathf.PI) + 0.5f, v.y * 0.5f + 0.5f), v);
            // The table's order faces outward in Unity (clockwise seen from outside, left-handed); measured, not assumed.
            for (var i = 0; i < tris.Count; i += 3) b.AddTriangle(tris[i], tris[i + 1], tris[i + 2]);
            return b;
        }
    }
}
