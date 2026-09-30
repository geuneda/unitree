using System;
using System.Collections.Generic;
using UnityEngine;

namespace Harness.Procedural
{
    /// <summary>
    /// A smooth curve through control points (centripetal Catmull-Rom: no loops or cusps where points bunch up), open or closed.
    /// <see cref="Evaluate"/> takes t in [0, 1] over the whole curve by arc length, so equal steps of t are equal distances.
    /// Mesh it with <see cref="MeshBuilder.Tube"/>, or place objects along it (<see cref="Evaluate"/>, <see cref="Tangent"/>).
    /// </summary>
    public sealed class Spline
    {
        readonly Vector3[] m_Points;
        readonly bool m_Closed;
        readonly float[] m_Arc;      // cumulative length at each lookup sample
        const int SamplesPerSegment = 32;

        public IReadOnlyList<Vector3> Points => m_Points;
        public bool Closed => m_Closed;
        public float Length => m_Arc[m_Arc.Length - 1];
        int Segments => m_Closed ? m_Points.Length : m_Points.Length - 1;

        public Spline(IList<Vector3> points, bool closed = false)
        {
            if (points == null || points.Count < 2) throw new ArgumentException("a spline needs at least 2 points");
            m_Points = new Vector3[points.Count];
            points.CopyTo(m_Points, 0);
            m_Closed = closed;
            m_Arc = new float[Segments * SamplesPerSegment + 1];
            var prev = Raw(0f);
            for (var i = 1; i < m_Arc.Length; i++)
            {
                var p = Raw((float)i / (m_Arc.Length - 1));
                m_Arc[i] = m_Arc[i - 1] + (p - prev).magnitude;
                prev = p;
            }
        }

        /// <summary>Point at <paramref name="t"/> (0 = start, 1 = end) by arc length.</summary>
        public Vector3 Evaluate(float t) => Raw(ToParameter(t));

        /// <summary>Unit direction of travel at <paramref name="t"/>.</summary>
        public Vector3 Tangent(float t)
        {
            var s = ToParameter(t);
            var h = 0.5f / (Segments * SamplesPerSegment);
            var a = Raw(m_Closed ? Wrap(s - h) : Mathf.Max(0f, s - h));
            var b = Raw(m_Closed ? Wrap(s + h) : Mathf.Min(1f, s + h));
            var d = b - a;
            return d.sqrMagnitude > 1e-12f ? d.normalized : Vector3.forward;
        }

        static float Wrap(float s) => s - Mathf.Floor(s);

        float ToParameter(float t)
        {
            t = m_Closed ? Wrap(t) : Mathf.Clamp01(t);
            var target = t * Length;
            int lo = 0, hi = m_Arc.Length - 1;
            while (hi - lo > 1) { var mid = (lo + hi) / 2; if (m_Arc[mid] < target) lo = mid; else hi = mid; }
            var span = m_Arc[hi] - m_Arc[lo];
            var f = span > 1e-9f ? (target - m_Arc[lo]) / span : 0f;
            return (lo + f) / (m_Arc.Length - 1);
        }

        /// <summary>Point at curve parameter s in [0, 1] (uniform per segment).</summary>
        Vector3 Raw(float s)
        {
            var n = m_Points.Length;
            var seg = Mathf.Min(Mathf.FloorToInt(s * Segments), Segments - 1);
            var u = s * Segments - seg;
            Vector3 P(int i) => m_Closed ? m_Points[((i % n) + n) % n] : m_Points[Mathf.Clamp(i, 0, n - 1)];
            var p1 = P(seg); var p2 = P(seg + 1);
            // Open ends: mirror the neighbour so the curve starts and ends at the first and last point without overshoot.
            var p0 = m_Closed || seg > 0 ? P(seg - 1) : p1 + (p1 - p2);
            var p3 = m_Closed || seg + 2 < n ? P(seg + 2) : p2 + (p2 - p1);
            return CentripetalCatmullRom(p0, p1, p2, p3, u);
        }

        static Vector3 CentripetalCatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
        {
            float Knot(float ti, Vector3 a, Vector3 b) => ti + Mathf.Max(1e-4f, Mathf.Sqrt((b - a).magnitude));
            const float t0 = 0f;
            var t1 = Knot(t0, p0, p1); var t2 = Knot(t1, p1, p2); var t3 = Knot(t2, p2, p3);
            var t = Mathf.Lerp(t1, t2, u);
            var a1 = (t1 - t) / (t1 - t0) * p0 + (t - t0) / (t1 - t0) * p1;
            var a2 = (t2 - t) / (t2 - t1) * p1 + (t - t1) / (t2 - t1) * p2;
            var a3 = (t3 - t) / (t3 - t2) * p2 + (t - t2) / (t3 - t2) * p3;
            var b1 = (t2 - t) / (t2 - t0) * a1 + (t - t0) / (t2 - t0) * a2;
            var b2 = (t3 - t) / (t3 - t1) * a2 + (t - t1) / (t3 - t1) * a3;
            return (t2 - t) / (t2 - t1) * b1 + (t - t1) / (t2 - t1) * b2;
        }
    }

    public sealed partial class MeshBuilder
    {
        /// <summary>
        /// A tube around <paramref name="spline"/>: <paramref name="segments"/> rings along it (equal distances), each of
        /// <paramref name="radialSegments"/> vertices, oriented by rotation-minimizing frames (no sudden twists). <paramref name="radius"/>(t)
        /// sets the radius along it (t = 0..1). An open tube gets flat caps. U runs along the tube (0..1), V around it.
        /// </summary>
        public static MeshBuilder Tube(Spline spline, Func<float, float> radius, int segments = 64, int radialSegments = 12, bool caps = true)
        {
            if (spline == null) throw new ArgumentNullException(nameof(spline));
            radius = radius ?? (_ => 0.1f);
            segments = Mathf.Max(1, segments); radialSegments = Mathf.Max(3, radialSegments);
            var b = new MeshBuilder();
            var rings = spline.Closed ? segments : segments + 1;
            // Rotation-minimizing frames: carry the normal along by the rotation between consecutive tangents.
            var tangent = spline.Tangent(0f);
            var normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
            var frames = new (Vector3 p, Vector3 t, Vector3 n)[rings];
            for (var i = 0; i < rings; i++)
            {
                var t = (float)i / segments;
                var tan = spline.Tangent(t);
                if (i > 0) normal = (Quaternion.FromToRotation(frames[i - 1].t, tan) * normal).normalized;
                normal = (normal - tan * Vector3.Dot(normal, tan)).normalized;
                frames[i] = (spline.Evaluate(t), tan, normal);
            }
            // A closed tube: spread the twist left between the last and the first frame over the whole length.
            if (spline.Closed && rings > 1)
            {
                var carried = (Quaternion.FromToRotation(frames[rings - 1].t, frames[0].t) * frames[rings - 1].n).normalized;
                var twist = Vector3.SignedAngle(carried, frames[0].n, frames[0].t);
                for (var i = 0; i < rings; i++)
                    frames[i].n = Quaternion.AngleAxis(twist * i / rings, frames[i].t) * frames[i].n;
            }
            var ringCount = spline.Closed ? rings + 1 : rings;   // a closed tube repeats its first ring (U = 1) for the texture seam
            for (var i = 0; i < ringCount; i++)
            {
                var f = frames[i % rings];
                var t = (float)i / segments;
                var r = radius(Mathf.Min(1f, t));
                var bin = Vector3.Cross(f.t, f.n);
                for (var j = 0; j <= radialSegments; j++)
                {
                    var a = (float)j / radialSegments * Mathf.PI * 2f;
                    var dir = f.n * Mathf.Cos(a) + bin * Mathf.Sin(a);
                    b.AddVertex(f.p + dir * r, new Vector2(t, (float)j / radialSegments), dir);
                }
            }
            for (var i = 0; i < ringCount - 1; i++)
            for (var j = 0; j < radialSegments; j++)
            {
                var k = i * (radialSegments + 1) + j;
                b.AddQuad(k, k + 1, k + radialSegments + 2, k + radialSegments + 1);
            }
            if (caps && !spline.Closed)
            {
                Cap(b, frames[0].p, -frames[0].t, 0, radialSegments, reverse: true);
                Cap(b, frames[rings - 1].p, frames[rings - 1].t, (rings - 1) * (radialSegments + 1), radialSegments, reverse: false);
            }
            return b;
        }

        public static MeshBuilder Tube(Spline spline, float radius, int segments = 64, int radialSegments = 12, bool caps = true) =>
            Tube(spline, _ => radius, segments, radialSegments, caps);

        static void Cap(MeshBuilder b, Vector3 center, Vector3 normal, int ringStart, int radialSegments, bool reverse)
        {
            var c = b.AddVertex(center, new Vector2(0.5f, 0.5f), normal);
            var first = b.VertexCount;
            for (var j = 0; j <= radialSegments; j++) b.AddVertex(b.Vertices[ringStart + j], b.UV[ringStart + j], normal);
            for (var j = 0; j < radialSegments; j++)
            {
                if (reverse) b.AddTriangle(c, first + j + 1, first + j);
                else b.AddTriangle(c, first + j, first + j + 1);
            }
        }
    }
}
