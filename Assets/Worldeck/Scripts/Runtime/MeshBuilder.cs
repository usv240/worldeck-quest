using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Worldeck
{
    /// <summary>
    /// Builds flat-shaded low-poly meshes in code: every face has its own vertices, so the World shader
    /// shades it in one tone (the "diorama" look). Vertex colour = albedo; uv0.x = emissive (0–1);
    /// uv0.y = wind sway weight (0 at the base, 1 at the tip). A transform stack places parts.
    /// Optional fake ambient occlusion darkens vertices near a ground height (art direction rule 3).
    /// </summary>
    public sealed class MeshBuilder
    {
        readonly List<Vector3> pos = new List<Vector3>(1024);
        readonly List<Vector3> nrm = new List<Vector3>(1024);
        readonly List<Color32> col = new List<Color32>(1024);
        readonly List<Vector2> uv = new List<Vector2>(1024);
        readonly List<int> tri = new List<int>(2048);
        readonly Stack<Matrix4x4> stack = new Stack<Matrix4x4>();
        Matrix4x4 m = Matrix4x4.identity;

        /// <summary>World-space (after the stack) height of the ground for fake AO; NaN = off.</summary>
        public float AoBaseY = float.NaN;
        public float AoHeight = 0.15f;
        public float AoStrength = 0.25f;

        public int VertexCount => pos.Count;
        public int TriangleCount => tri.Count / 3;
        /// <summary>Vertex positions so far (tests check that a skin stays inside its furniture's box).</summary>
        public IReadOnlyList<Vector3> Positions => pos;
        /// <summary>The bounds of everything built so far (empty bounds at the origin when nothing is built).</summary>
        public Bounds Bounds
        {
            get
            {
                if (pos.Count == 0) return new Bounds(Vector3.zero, Vector3.zero);
                var b = new Bounds(pos[0], Vector3.zero);
                for (int i = 1; i < pos.Count; i++) b.Encapsulate(pos[i]);
                return b;
            }
        }

        public void Push(Matrix4x4 local) { stack.Push(m); m = m * local; }
        public void Push(Vector3 t, Quaternion r, Vector3 s) => Push(Matrix4x4.TRS(t, r, s));
        public void Push(Vector3 t) => Push(Matrix4x4.Translate(t));
        public void Pop() { if (stack.Count > 0) m = stack.Pop(); }
        public Matrix4x4 Current => m;

        public void Clear()
        {
            pos.Clear(); nrm.Clear(); col.Clear(); uv.Clear(); tri.Clear(); stack.Clear();
            m = Matrix4x4.identity;
        }

        // ---------- faces ----------

        public void Tri(Vector3 a, Vector3 b, Vector3 c, Color color, float emissive = 0f, float windA = 0f, float windB = 0f, float windC = 0f)
        {
            a = m.MultiplyPoint3x4(a); b = m.MultiplyPoint3x4(b); c = m.MultiplyPoint3x4(c);
            Vector3 n = Vector3.Cross(b - a, c - a);
            float len = n.magnitude;
            if (len < 1e-10f) return;
            n /= len;
            int i = pos.Count;
            pos.Add(a); pos.Add(b); pos.Add(c);
            nrm.Add(n); nrm.Add(n); nrm.Add(n);
            col.Add(Ao(color, a.y)); col.Add(Ao(color, b.y)); col.Add(Ao(color, c.y));
            uv.Add(new Vector2(emissive, windA)); uv.Add(new Vector2(emissive, windB)); uv.Add(new Vector2(emissive, windC));
            tri.Add(i); tri.Add(i + 1); tri.Add(i + 2);
        }

        /// <summary>Quad a-b-c-d, counter-clockwise seen from the front (Unity: clockwise = front; we wind for Unity).</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, float emissive = 0f, float windLow = 0f, float windHigh = 0f)
        {
            Tri(a, b, c, color, emissive, windLow, windHigh, windHigh);
            Tri(a, c, d, color, emissive, windLow, windHigh, windLow);
        }

        Color32 Ao(Color c, float y)
        {
            if (!float.IsNaN(AoBaseY) && AoHeight > 0f)
            {
                float k = Mathf.Clamp01((y - AoBaseY) / AoHeight);
                float f = Mathf.Lerp(1f - AoStrength, 1f, k);
                c = new Color(c.r * f, c.g * f, c.b * f, c.a);
            }
            return c;
        }

        // ---------- primitives (local space of the current transform) ----------

        /// <summary>Axis-aligned box centred at <paramref name="c"/>. <paramref name="top"/> colours the top face (default = colour).</summary>
        public void Box(Vector3 c, Vector3 size, Color color, Color? top = null, float emissive = 0f, bool bottom = false)
        {
            Vector3 h = size * 0.5f;
            Vector3 p000 = c + new Vector3(-h.x, -h.y, -h.z), p100 = c + new Vector3(h.x, -h.y, -h.z);
            Vector3 p010 = c + new Vector3(-h.x, h.y, -h.z), p110 = c + new Vector3(h.x, h.y, -h.z);
            Vector3 p001 = c + new Vector3(-h.x, -h.y, h.z), p101 = c + new Vector3(h.x, -h.y, h.z);
            Vector3 p011 = c + new Vector3(-h.x, h.y, h.z), p111 = c + new Vector3(h.x, h.y, h.z);
            Color tc = top ?? color;
            Quad(p010, p011, p111, p110, tc, emissive);          // top (+Y)
            Quad(p000, p010, p110, p100, color, emissive);       // front (-Z)
            Quad(p101, p111, p011, p001, color, emissive);       // back (+Z)
            Quad(p001, p011, p010, p000, color, emissive);       // left (-X)
            Quad(p100, p110, p111, p101, color, emissive);       // right (+X)
            if (bottom) Quad(p000, p100, p101, p001, color, emissive);
        }

        /// <summary>
        /// A tapered prism (cylinder with <paramref name="sides"/> faces) from <paramref name="baseCenter"/> up
        /// <paramref name="height"/>. rTop 0 makes a cone. Wind weight runs from windBase to windTop.
        /// </summary>
        public void Cylinder(Vector3 baseCenter, float rBottom, float rTop, float height, int sides, Color color,
                             bool capTop = true, bool capBottom = false, float emissive = 0f, float windBase = 0f, float windTop = 0f,
                             float angleOffset = 0f, Color? capColor = null)
        {
            sides = Mathf.Clamp(sides, 3, 32);
            Vector3 topC = baseCenter + Vector3.up * height;
            for (int i = 0; i < sides; i++)
            {
                float a0 = angleOffset + i * Mathf.PI * 2f / sides, a1 = angleOffset + (i + 1) * Mathf.PI * 2f / sides;
                Vector3 d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)), d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                Vector3 b0 = baseCenter + d0 * rBottom, b1 = baseCenter + d1 * rBottom;
                Vector3 t0 = topC + d0 * rTop, t1 = topC + d1 * rTop;
                // Unity's front faces wind clockwise; these give outward normals (checked by hand, Nov 11).
                if (rTop > 1e-5f)
                {
                    Quad(b0, t0, t1, b1, color, emissive, windBase, windTop);
                    if (capTop) Tri(topC, t1, t0, capColor ?? color, emissive, windTop, windTop, windTop);
                }
                else Tri(b0, topC, b1, color, emissive, windBase, windTop, windBase);
                if (capBottom && rBottom > 1e-5f) Tri(baseCenter, b0, b1, capColor ?? color, emissive, windBase, windBase, windBase);
            }
        }

        /// <summary>A cylinder lying along local X (a log), centred at <paramref name="c"/>.</summary>
        public void Log(Vector3 c, float length, float radius, int sides, Color bark, Color cut)
        {
            Push(c, Quaternion.Euler(0, 0, 90), Vector3.one);
            Cylinder(new Vector3(0, -length * 0.5f, 0), radius, radius, length, sides, bark, true, true, 0, 0, 0, 0, cut);
            Pop();
        }

        static readonly Vector3[] IcoV =
        {
            new Vector3(-1, 1.618f, 0), new Vector3(1, 1.618f, 0), new Vector3(-1, -1.618f, 0), new Vector3(1, -1.618f, 0),
            new Vector3(0, -1, 1.618f), new Vector3(0, 1, 1.618f), new Vector3(0, -1, -1.618f), new Vector3(0, 1, -1.618f),
            new Vector3(1.618f, 0, -1), new Vector3(1.618f, 0, 1), new Vector3(-1.618f, 0, -1), new Vector3(-1.618f, 0, 1)
        };
        static readonly int[] IcoT =
        {
            0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
            3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
        };

        /// <summary>A jittered icosahedron (rock, bush, snowball, foliage blob): 20 faces.</summary>
        public void Ico(Vector3 c, Vector3 radius, Color color, Worldeck.Core.Rng rng = null, float jitter = 0.18f,
                        float emissive = 0f, float windBase = 0f, float windTop = 0f, float flattenBottom = 0f)
        {
            var v = new Vector3[12];
            float norm = 1f / Mathf.Sqrt(1f + 1.618f * 1.618f);
            for (int i = 0; i < 12; i++)
            {
                Vector3 p = IcoV[i] * norm;
                if (rng != null && jitter > 0f) p *= 1f + (rng.Next01() * 2f - 1f) * jitter;
                if (flattenBottom > 0f && p.y < -flattenBottom) p.y = -flattenBottom;
                v[i] = c + Vector3.Scale(p, radius);
            }
            float y0 = c.y - radius.y, y1 = c.y + radius.y;
            for (int i = 0; i < IcoT.Length; i += 3)
            {
                Vector3 a = v[IcoT[i]], b = v[IcoT[i + 1]], d = v[IcoT[i + 2]];   // outward in Unity's winding
                float W(Vector3 p) => Mathf.Lerp(windBase, windTop, Mathf.InverseLerp(y0, y1, p.y));
                Tri(a, b, d, color, emissive, W(a), W(b), W(d));
            }
        }

        /// <summary>A flat disc facing +Y (or -Y when <paramref name="down"/>).</summary>
        public void Disc(Vector3 c, float radius, int sides, Color color, float emissive = 0f, bool down = false)
        {
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = c + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radius;
                Vector3 p1 = c + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radius;
                if (down) Tri(c, p0, p1, color, emissive); else Tri(c, p1, p0, color, emissive);
            }
        }

        /// <summary>A flat blade/leaf quad strip from <paramref name="root"/> along <paramref name="dir"/>, sagging under gravity.</summary>
        public void Frond(Vector3 root, Vector3 dir, float length, float width, int segments, float sag, Color color, float windRoot = 0.4f, float windTip = 1f)
        {
            dir.Normalize();
            Vector3 side = Vector3.Cross(dir, Vector3.up);
            if (side.sqrMagnitude < 1e-4f) side = Vector3.right;
            side.Normalize();
            Vector3 prev = root;
            float prevW = width * 0.3f;
            for (int s = 1; s <= segments; s++)
            {
                float t = s / (float)segments;
                Vector3 p = root + dir * (length * t) + Vector3.down * (sag * t * t);
                float w = width * Mathf.Sin(Mathf.PI * Mathf.Min(1f, t * 1.1f)) + 0.002f;
                float wa = Mathf.Lerp(windRoot, windTip, (s - 1f) / segments), wb = Mathf.Lerp(windRoot, windTip, t);
                Vector3 a = prev - side * prevW * 0.5f, b = prev + side * prevW * 0.5f, c = p + side * w * 0.5f, d = p - side * w * 0.5f;
                Quad(a, d, c, b, color, 0, wa, wb);
                Quad(b, c, d, a, color, 0, wa, wb);   // both sides
                prev = p; prevW = w;
            }
        }

        // ---------- output ----------

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (pos.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(pos);
            mesh.SetNormals(nrm);
            mesh.SetColors(col);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(tri, 0, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Appends another builder's geometry (already in world space) to this one.</summary>
        public void Append(MeshBuilder other)
        {
            int baseIndex = pos.Count;
            pos.AddRange(other.pos); nrm.AddRange(other.nrm); col.AddRange(other.col); uv.AddRange(other.uv);
            foreach (int i in other.tri) tri.Add(i + baseIndex);
        }
    }
}
