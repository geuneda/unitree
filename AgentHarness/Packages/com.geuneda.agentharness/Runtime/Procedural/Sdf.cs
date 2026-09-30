using System;
using UnityEngine;

namespace Harness.Procedural
{
    /// <summary>
    /// Signed distance functions (negative inside, distance in world units outside) and their combinations. Compose them in a
    /// lambda and mesh it with <see cref="MeshBuilder.FromSdf"/>:
    /// <c>p => Sdf.SmoothUnion(Sdf.Sphere(p, 1f), Sdf.Box(p - new Vector3(0, 1, 0), new Vector3(0.4f, 1f, 0.4f)), 0.3f)</c>.
    /// Shapes are centered at the origin; move a shape by passing <c>p - center</c>, rotate by passing <c>Quaternion.Inverse(rot) * p</c>.
    /// </summary>
    public static class Sdf
    {
        public static float Sphere(Vector3 p, float radius) => p.magnitude - radius;

        /// <summary>Box with half extents <paramref name="half"/>.</summary>
        public static float Box(Vector3 p, Vector3 half)
        {
            var q = new Vector3(Mathf.Abs(p.x) - half.x, Mathf.Abs(p.y) - half.y, Mathf.Abs(p.z) - half.z);
            var outside = new Vector3(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f), Mathf.Max(q.z, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
        }

        /// <summary>Box with its edges rounded by <paramref name="radius"/> (the outer size stays <paramref name="half"/>).</summary>
        public static float RoundBox(Vector3 p, Vector3 half, float radius) =>
            Box(p, half - Vector3.one * radius) - radius;

        /// <summary>Segment a-b with radius.</summary>
        public static float Capsule(Vector3 p, Vector3 a, Vector3 b, float radius)
        {
            var pa = p - a; var ba = b - a;
            var h = Mathf.Clamp01(Vector3.Dot(pa, ba) / Vector3.Dot(ba, ba));
            return (pa - ba * h).magnitude - radius;
        }

        /// <summary>Torus in the XZ plane.</summary>
        public static float Torus(Vector3 p, float majorRadius, float minorRadius)
        {
            var q = new Vector2(new Vector2(p.x, p.z).magnitude - majorRadius, p.y);
            return q.magnitude - minorRadius;
        }

        /// <summary>Cylinder along Y.</summary>
        public static float Cylinder(Vector3 p, float radius, float halfHeight)
        {
            var d = new Vector2(new Vector2(p.x, p.z).magnitude - radius, Mathf.Abs(p.y) - halfHeight);
            return Mathf.Min(Mathf.Max(d.x, d.y), 0f) + new Vector2(Mathf.Max(d.x, 0f), Mathf.Max(d.y, 0f)).magnitude;
        }

        /// <summary>Half-space below the plane through the origin with <paramref name="normal"/> (unit length).</summary>
        public static float Plane(Vector3 p, Vector3 normal) => Vector3.Dot(p, normal);

        public static float Union(float a, float b) => Mathf.Min(a, b);

        /// <summary><paramref name="a"/> with <paramref name="b"/> carved out.</summary>
        public static float Subtract(float a, float b) => Mathf.Max(a, -b);

        public static float Intersect(float a, float b) => Mathf.Max(a, b);

        /// <summary>Union blended over <paramref name="k"/> units (a fillet where the shapes meet).</summary>
        public static float SmoothUnion(float a, float b, float k)
        {
            var h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        public static float SmoothSubtract(float a, float b, float k)
        {
            var h = Mathf.Clamp01(0.5f - 0.5f * (a + b) / k);
            return Mathf.Lerp(a, -b, h) + k * h * (1f - h);
        }

        public static float SmoothIntersect(float a, float b, float k)
        {
            var h = Mathf.Clamp01(0.5f - 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) + k * h * (1f - h);
        }

        /// <summary>Surface normal direction (normalized gradient) by central differences.</summary>
        public static Vector3 Normal(Func<Vector3, float> sdf, Vector3 p, float eps = 1e-3f)
        {
            var dx = sdf(p + new Vector3(eps, 0f, 0f)) - sdf(p - new Vector3(eps, 0f, 0f));
            var dy = sdf(p + new Vector3(0f, eps, 0f)) - sdf(p - new Vector3(0f, eps, 0f));
            var dz = sdf(p + new Vector3(0f, 0f, eps)) - sdf(p - new Vector3(0f, 0f, eps));
            var g = new Vector3(dx, dy, dz);
            return g.sqrMagnitude > 1e-20f ? g.normalized : Vector3.up;
        }
    }

    public sealed partial class MeshBuilder
    {
        /// <summary>
        /// The surface of <paramref name="sdf"/> inside <paramref name="bounds"/> as a smooth mesh (surface nets: one vertex per cell the
        /// surface crosses, moved onto the surface, a quad per crossed grid edge). <paramref name="cellSize"/> is the detail: features
        /// smaller than about two cells disappear. Normals come from the SDF; UVs are box-projected (<paramref name="uvScale"/> per unit).
        /// Keep the shape inside the bounds (a surface cut by them stays open).
        /// </summary>
        public static MeshBuilder FromSdf(Func<Vector3, float> sdf, Bounds bounds, float cellSize, float uvScale = 1f)
        {
            if (sdf == null) throw new ArgumentNullException(nameof(sdf));
            if (!(cellSize > 0f)) throw new ArgumentException("cellSize must be positive");
            var min = bounds.min;
            var nx = Mathf.Max(2, Mathf.CeilToInt(bounds.size.x / cellSize) + 1);
            var ny = Mathf.Max(2, Mathf.CeilToInt(bounds.size.y / cellSize) + 1);
            var nz = Mathf.Max(2, Mathf.CeilToInt(bounds.size.z / cellSize) + 1);
            if ((long)nx * ny * nz > 16_000_000) throw new ArgumentException($"FromSdf: {nx}x{ny}x{nz} samples - use a larger cellSize or smaller bounds");

            int Idx(int x, int y, int z) => (z * ny + y) * nx + x;
            Vector3 Corner(int x, int y, int z) => min + new Vector3(x, y, z) * cellSize;
            var d = new float[nx * ny * nz];
            for (var z = 0; z < nz; z++)
            for (var y = 0; y < ny; y++)
            for (var x = 0; x < nx; x++)
                d[Idx(x, y, z)] = sdf(Corner(x, y, z));

            var b = new MeshBuilder();
            var cx = nx - 1; var cy = ny - 1; var cz = nz - 1;
            var cellVertex = new int[cx * cy * cz];
            int Cell(int x, int y, int z) => (z * cy + y) * cx + x;
            var eps = cellSize * 0.05f;

            // One vertex per cell with a sign change: the mean of its edge crossings, then one step onto the surface.
            for (var z = 0; z < cz; z++)
            for (var y = 0; y < cy; y++)
            for (var x = 0; x < cx; x++)
            {
                var ci = Cell(x, y, z);
                cellVertex[ci] = -1;
                var inside = 0;
                for (var c = 0; c < 8; c++) if (d[Idx(x + (c & 1), y + ((c >> 1) & 1), z + ((c >> 2) & 1))] < 0f) inside++;
                if (inside == 0 || inside == 8) continue;
                var sum = Vector3.zero; var count = 0;
                for (var c = 0; c < 8; c++)
                for (var axis = 0; axis < 3; axis++)
                {
                    if ((c >> axis & 1) != 0) continue;   // each of the 12 edges once: from the corner with a 0 bit on that axis
                    var c2 = c | (1 << axis);
                    var a0 = new Vector3Int(c & 1, (c >> 1) & 1, (c >> 2) & 1);
                    var a1 = new Vector3Int(c2 & 1, (c2 >> 1) & 1, (c2 >> 2) & 1);
                    var v0 = d[Idx(x + a0.x, y + a0.y, z + a0.z)];
                    var v1 = d[Idx(x + a1.x, y + a1.y, z + a1.z)];
                    if ((v0 < 0f) == (v1 < 0f)) continue;
                    var t = v0 / (v0 - v1);
                    sum += Vector3.Lerp(a0, a1, t);
                    count++;
                }
                var p = Corner(x, y, z) + sum / count * cellSize;
                var n = Sdf.Normal(sdf, p, eps);
                p -= n * sdf(p);
                n = Sdf.Normal(sdf, p, eps);
                cellVertex[ci] = b.AddVertex(p, BoxUv(p, n, uvScale), n);
            }

            // A quad per grid edge the surface crosses, joining the four cells around it, facing outward (toward positive distance).
            for (var z = 0; z < nz; z++)
            for (var y = 0; y < ny; y++)
            for (var x = 0; x < nx; x++)
            for (var axis = 0; axis < 3; axis++)
            {
                var ex = x + (axis == 0 ? 1 : 0); var ey = y + (axis == 1 ? 1 : 0); var ez = z + (axis == 2 ? 1 : 0);
                if (ex >= nx || ey >= ny || ez >= nz) continue;
                var v0 = d[Idx(x, y, z)]; var v1 = d[Idx(ex, ey, ez)];
                if ((v0 < 0f) == (v1 < 0f)) continue;
                // The two axes around the edge and the four cells that share it.
                var u = (axis + 1) % 3; var w = (axis + 2) % 3;
                var o = new Vector3Int(x, y, z);
                var quad = new int[4];
                var ok = true;
                for (var k = 0; k < 4 && ok; k++)
                {
                    var cc = o;
                    if ((k == 0 || k == 3)) cc[u] -= 1;
                    if (k < 2) cc[w] -= 1;
                    if (cc.x < 0 || cc.y < 0 || cc.z < 0 || cc.x >= cx || cc.y >= cy || cc.z >= cz) { ok = false; break; }
                    quad[k] = cellVertex[Cell(cc.x, cc.y, cc.z)];
                    if (quad[k] < 0) ok = false;
                }
                if (!ok) continue;
                // Outward = from negative to positive along the edge; flip the winding to face it.
                var outward = (v0 < 0f ? 1f : -1f) * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
                var pa = b.Vertices[quad[0]]; var pb = b.Vertices[quad[1]]; var pc = b.Vertices[quad[2]];
                if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), outward) >= 0f) b.AddQuad(quad[0], quad[1], quad[2], quad[3]);
                else b.AddQuad(quad[0], quad[3], quad[2], quad[1]);
            }
            return b;
        }

        /// <summary>UV projected along the dominant axis of the normal (for textures on shapes without a natural unwrap).</summary>
        public static Vector2 BoxUv(Vector3 p, Vector3 n, float scale = 1f)
        {
            var a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            var uv = a.x >= a.y && a.x >= a.z ? new Vector2(p.z, p.y) : a.y >= a.z ? new Vector2(p.x, p.z) : new Vector2(p.x, p.y);
            return uv * scale;
        }
    }
}
