using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The real room drawn as "see-through": walls, floor and ceiling (and, while a world opens, each piece of
    /// furniture) render a transparent pixel that shows the passthrough room and hides the world behind it.
    /// The reveal front (<see cref="Radius"/>) dissolves them outward from <see cref="Origin"/>; the furniture
    /// holders stay until each piece's skin has grown over it, so real furniture never vanishes early.
    /// </summary>
    public sealed class RoomShell : MonoBehaviour
    {
        public Material Cut;
        public Material CutHold;

        public Vector3 Origin;
        /// <summary>Metres the dissolve has travelled (negative: the room is whole).</summary>
        public float Radius = -1f;
        public float Edge = 0.05f;

        readonly Dictionary<RoomModel.Piece, GameObject> holders = new Dictionary<RoomModel.Piece, GameObject>();
        Transform shell;
        RoomModel room;

        static readonly int OriginId = Shader.PropertyToID("_SwRevealOrigin"), RadiusId = Shader.PropertyToID("_SwRevealRadius"),
            EdgeId = Shader.PropertyToID("_SwRevealEdge");

        /// <summary>The farthest any shell point is from <see cref="Origin"/> (where the dissolve is complete).</summary>
        public float FullRadius { get; private set; } = 6f;

        public void Build(RoomModel r)
        {
            room = r;
            if (shell != null) Util.Kill(shell.gameObject);
            holders.Clear();
            shell = new GameObject("Room shell").transform;
            shell.SetParent(transform, false);

            var mesh = new Mesh { name = "room-shell" };
            var v = new List<Vector3>();
            var t = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            foreach (var w in r.Walls)
            {
                // A little outside the real wall, so a hand on the wall is in front of it.
                Vector3 o = -w.Normal * 0.01f;
                Quad(new Vector3(w.A.x, w.Bottom - 0.05f, w.A.z) + o, new Vector3(w.A.x, w.Top + 0.05f, w.A.z) + o,
                     new Vector3(w.B.x, w.Top + 0.05f, w.B.z) + o, new Vector3(w.B.x, w.Bottom - 0.05f, w.B.z) + o);
            }
            // Floor and ceiling: a rectangle over the room's bounds (corners outside the walls are hidden by the walls).
            var b = r.Bounds;
            float pad = 0.5f;
            float x0 = b.min.x - pad, x1 = b.max.x + pad, z0 = b.min.z - pad, z1 = b.max.z + pad;
            float fy = r.FloorY, cy = r.CeilingY;
            Quad(new Vector3(x0, fy, z0), new Vector3(x0, fy, z1), new Vector3(x1, fy, z1), new Vector3(x1, fy, z0));
            Quad(new Vector3(x0, cy, z0), new Vector3(x1, cy, z0), new Vector3(x1, cy, z1), new Vector3(x0, cy, z1));
            mesh.SetVertices(v);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            Util.MeshObject("Walls floor ceiling", shell, mesh, Cut);

            FullRadius = 0f;
            foreach (var p in v) FullRadius = Mathf.Max(FullRadius, Vector3.Distance(Origin, p));
            FullRadius += 1f;

            foreach (var piece in r.Pieces)
            {
                if (piece.Virtual) continue;
                var go = Util.MeshObject("Hold " + piece.Label, shell, BoxMesh(), CutHold);
                go.transform.position = piece.Center;
                go.transform.rotation = piece.Rotation;
                go.transform.localScale = piece.Size + Vector3.one * 0.01f;
                holders[piece] = go;
            }
            Apply();
        }

        /// <summary>Re-measures the full radius for a new origin.</summary>
        public void SetOrigin(Vector3 origin)
        {
            Origin = origin;
            if (room == null) return;
            FullRadius = 1f;
            foreach (var w in room.Walls)
            {
                FullRadius = Mathf.Max(FullRadius, Vector3.Distance(origin, new Vector3(w.A.x, room.CeilingY, w.A.z)) + 1f);
                FullRadius = Mathf.Max(FullRadius, Vector3.Distance(origin, new Vector3(w.B.x, room.FloorY, w.B.z)) + 1f);
            }
            var b = room.Bounds;
            FullRadius = Mathf.Max(FullRadius, Vector3.Distance(origin, b.min) + 1f, Vector3.Distance(origin, b.max) + 1f);
        }

        /// <summary>Shows or hides the passthrough holder over one real piece of furniture.</summary>
        public void Hold(RoomModel.Piece p, bool on)
        {
            if (p != null && holders.TryGetValue(p, out var go) && go != null) go.SetActive(on);
        }

        public void HoldAll(bool on)
        {
            foreach (var kv in holders) if (kv.Value != null) kv.Value.SetActive(on);
        }

        /// <summary>
        /// Shrinks a holder towards its piece's floor point as the skin grows (<paramref name="keep"/> 1 = whole
        /// real piece showing, 0 = gone), so the real furniture seems to turn into its themed shape.
        /// </summary>
        public void HolderScale(RoomModel.Piece p, float keep)
        {
            if (p == null || !holders.TryGetValue(p, out var go) || go == null) return;
            keep = Mathf.Clamp01(keep);
            go.SetActive(keep > 0.01f);
            go.transform.position = p.Bottom + Vector3.up * (p.Size.y * keep * 0.5f);
            go.transform.localScale = p.Size * keep + Vector3.one * 0.01f;
        }

        void LateUpdate() => Apply();

        public void Apply()
        {
            Shader.SetGlobalVector(OriginId, Origin);
            Shader.SetGlobalFloat(RadiusId, Radius);
            Shader.SetGlobalFloat(EdgeId, Edge);
        }

        static Mesh box;
        static Mesh BoxMesh()
        {
            if (box != null) return box;
            var mb = new MeshBuilder();
            mb.Box(Vector3.zero, Vector3.one, Color.black, null, 0f, true);
            box = mb.ToMesh("unit-box");
            return box;
        }
    }
}
