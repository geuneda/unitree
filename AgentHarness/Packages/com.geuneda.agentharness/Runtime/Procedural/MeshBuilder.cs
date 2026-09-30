using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Harness.Procedural
{
    /// <summary>
    /// Accumulates vertices/triangles and produces a Mesh (normals, tangents, bounds, 32-bit indices
    /// when needed). Static factories cover the common primitives; everything is deterministic.
    /// </summary>
    public sealed partial class MeshBuilder
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> UV = new List<Vector2>();
        public readonly List<Color> Colors = new List<Color>();
        public readonly List<int> Triangles = new List<int>();

        public int VertexCount => Vertices.Count;

        public int AddVertex(Vector3 position, Vector2 uv, Vector3? normal = null, Color? color = null)
        {
            Vertices.Add(position);
            UV.Add(uv);
            if (normal.HasValue) Normals.Add(normal.Value);
            if (color.HasValue) Colors.Add(color.Value);
            return Vertices.Count - 1;
        }

        public void AddTriangle(int a, int b, int c) { Triangles.Add(a); Triangles.Add(b); Triangles.Add(c); }
        public void AddQuad(int a, int b, int c, int d) { AddTriangle(a, b, c); AddTriangle(a, c, d); }

        /// <summary>Append another builder's geometry, transformed.</summary>
        public void Append(MeshBuilder other, Matrix4x4 transform)
        {
            var offset = Vertices.Count;
            var normalMatrix = transform.inverse.transpose;
            for (var i = 0; i < other.Vertices.Count; i++)
            {
                Vertices.Add(transform.MultiplyPoint3x4(other.Vertices[i]));
                UV.Add(i < other.UV.Count ? other.UV[i] : Vector2.zero);
                if (other.Normals.Count == other.Vertices.Count) Normals.Add(normalMatrix.MultiplyVector(other.Normals[i]).normalized);
                if (other.Colors.Count == other.Vertices.Count) Colors.Add(other.Colors[i]);
            }
            foreach (var t in other.Triangles) Triangles.Add(t + offset);
        }

        public Mesh ToMesh(string name = "ProceduralMesh", bool recalcNormals = false)
        {
            var mesh = new Mesh { name = name };
            if (Vertices.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(Vertices);
            if (UV.Count == Vertices.Count) mesh.SetUVs(0, UV);
            if (Colors.Count == Vertices.Count) mesh.SetColors(Colors);
            mesh.SetTriangles(Triangles, 0, true);
            if (recalcNormals || Normals.Count != Vertices.Count) mesh.RecalculateNormals();
            else mesh.SetNormals(Normals);
            if (UV.Count == Vertices.Count) mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---- Factories -----------------------------------------------------------------------------

        /// <summary>
        /// XZ grid centered at the origin. <paramref name="height"/>(u, v) receives normalized [0,1] coordinates.
        /// Normals are computed from the height field by central differences (smooth, seam-free).
        /// </summary>
        public static MeshBuilder Grid(float sizeX, float sizeZ, int resX, int resZ, Func<float, float, float> height = null,
            Func<float, float, float, Color> color = null)
        {
            var b = new MeshBuilder();
            resX = Mathf.Max(1, resX); resZ = Mathf.Max(1, resZ);
            var h = new float[(resX + 1) * (resZ + 1)];
            for (var z = 0; z <= resZ; z++)
            for (var x = 0; x <= resX; x++)
                h[z * (resX + 1) + x] = height?.Invoke((float)x / resX, (float)z / resZ) ?? 0f;

            var dx = sizeX / resX; var dz = sizeZ / resZ;
            for (var z = 0; z <= resZ; z++)
            for (var x = 0; x <= resX; x++)
            {
                var u = (float)x / resX; var v = (float)z / resZ;
                var y = h[z * (resX + 1) + x];
                var hl = h[z * (resX + 1) + Mathf.Max(0, x - 1)];
                var hr = h[z * (resX + 1) + Mathf.Min(resX, x + 1)];
                var hd = h[Mathf.Max(0, z - 1) * (resX + 1) + x];
                var hu = h[Mathf.Min(resZ, z + 1) * (resX + 1) + x];
                var n = new Vector3((hl - hr) / (2f * dx), 1f, (hd - hu) / (2f * dz)).normalized;
                var pos = new Vector3((u - 0.5f) * sizeX, y, (v - 0.5f) * sizeZ);
                b.AddVertex(pos, new Vector2(u, v), n, color?.Invoke(u, v, y));
            }
            for (var z = 0; z < resZ; z++)
            for (var x = 0; x < resX; x++)
            {
                var i = z * (resX + 1) + x;
                b.AddQuad(i, i + resX + 1, i + resX + 2, i + 1);
            }
            return b;
        }

        public static MeshBuilder Sphere(float radius = 0.5f, int segments = 48, int rings = 24)
        {
            var b = new MeshBuilder();
            for (var r = 0; r <= rings; r++)
            {
                var v = (float)r / rings;
                var phi = v * Mathf.PI;
                for (var s = 0; s <= segments; s++)
                {
                    var u = (float)s / segments;
                    var theta = u * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
                    b.AddVertex(n * radius, new Vector2(u, 1f - v), n);
                }
            }
            for (var r = 0; r < rings; r++)
            for (var s = 0; s < segments; s++)
            {
                var i = r * (segments + 1) + s;
                b.AddQuad(i, i + 1, i + segments + 2, i + segments + 1);
            }
            return b;
        }

        public static MeshBuilder Torus(float majorRadius = 1f, float minorRadius = 0.3f, int majorSegments = 96, int minorSegments = 32)
        {
            var b = new MeshBuilder();
            for (var i = 0; i <= majorSegments; i++)
            {
                var u = (float)i / majorSegments;
                var a = u * Mathf.PI * 2f;
                var center = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * majorRadius;
                for (var j = 0; j <= minorSegments; j++)
                {
                    var v = (float)j / minorSegments;
                    var c = v * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Cos(a) * Mathf.Cos(c), Mathf.Sin(c), Mathf.Sin(a) * Mathf.Cos(c));
                    b.AddVertex(center + n * minorRadius, new Vector2(u, v), n);
                }
            }
            for (var i = 0; i < majorSegments; i++)
            for (var j = 0; j < minorSegments; j++)
            {
                var k = i * (minorSegments + 1) + j;
                b.AddQuad(k, k + 1, k + minorSegments + 2, k + minorSegments + 1);
            }
            return b;
        }

        /// <summary>Torus knot (p, q) — a good "hero" test object: curvature everywhere, self-shadowing.</summary>
        public static MeshBuilder TorusKnot(float radius = 1f, float tube = 0.28f, int p = 2, int q = 3, int tubularSegments = 256, int radialSegments = 24)
        {
            var b = new MeshBuilder();
            Vector3 Curve(float t)
            {
                var cu = Mathf.Cos(t); var su = Mathf.Sin(t);
                var quOverP = q / (float)p * t;
                var cs = Mathf.Cos(quOverP);
                return new Vector3(radius * (2f + cs) * 0.5f * cu, radius * Mathf.Sin(quOverP) * 0.5f, radius * (2f + cs) * 0.5f * su);
            }
            for (var i = 0; i <= tubularSegments; i++)
            {
                var u = (float)i / tubularSegments * p * Mathf.PI * 2f;
                var p1 = Curve(u);
                var p2 = Curve(u + 0.01f);
                var T = (p2 - p1).normalized;
                var N = (p2 + p1).normalized;
                var B = Vector3.Cross(T, N).normalized;
                N = Vector3.Cross(B, T).normalized;
                for (var j = 0; j <= radialSegments; j++)
                {
                    var v = (float)j / radialSegments * Mathf.PI * 2f;
                    var cx = -tube * Mathf.Cos(v); var cy = tube * Mathf.Sin(v);
                    var pos = p1 + cx * N + cy * B;
                    b.AddVertex(pos, new Vector2((float)i / tubularSegments, (float)j / radialSegments), (pos - p1).normalized);
                }
            }
            for (var i = 0; i < tubularSegments; i++)
            for (var j = 0; j < radialSegments; j++)
            {
                var a = (radialSegments + 1) * i + j;
                var c = (radialSegments + 1) * (i + 1) + j;
                b.AddQuad(a, c, c + 1, a + 1);
            }
            return b;
        }

        public static MeshBuilder Box(Vector3 size)
        {
            var b = new MeshBuilder();
            var h = size * 0.5f;
            void Face(Vector3 n, Vector3 up)
            {
                var right = Vector3.Cross(n, up); // chosen so the quad winds clockwise seen from outside (Unity front face)
                var c = Vector3.Scale(n, h);
                var r = Vector3.Scale(right, h); var u = Vector3.Scale(up, h);
                var i0 = b.AddVertex(c - r - u, new Vector2(0, 0), n);
                var i1 = b.AddVertex(c - r + u, new Vector2(0, 1), n);
                var i2 = b.AddVertex(c + r + u, new Vector2(1, 1), n);
                var i3 = b.AddVertex(c + r - u, new Vector2(1, 0), n);
                b.AddQuad(i0, i1, i2, i3);
            }
            Face(Vector3.forward, Vector3.up); Face(Vector3.back, Vector3.up);
            Face(Vector3.right, Vector3.up); Face(Vector3.left, Vector3.up);
            Face(Vector3.up, Vector3.back); Face(Vector3.down, Vector3.forward);
            return b;
        }
    }
}
