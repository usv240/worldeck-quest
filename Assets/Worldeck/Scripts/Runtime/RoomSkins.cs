using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Turns each real piece of furniture into the world's version of it, fitted to the piece's exact box:
    /// chosen by what it is for (<see cref="Role"/>), built by <see cref="Skins"/>. Skins grow in one after
    /// another (nearest first) while the passthrough holder over the real piece shrinks away, and reverse
    /// when the world ends. The table's skin is where gathered pieces build the result.
    /// </summary>
    public sealed class RoomSkins : MonoBehaviour
    {
        public Material World;
        public RoomShell Shell;
        public float GrowSeconds = 0.8f;   // a light change over at least 0.8 s
        public float Stagger = 0.15f;

        public sealed class Skin
        {
            public RoomModel.Piece Piece;
            public string Id = "";
            public GameObject Go;
            public float Grow;      // 0 hidden … 1 full
            public float Target;
            public float Delay;
            public bool FlatTop;
        }

        public readonly List<Skin> All = new List<Skin>();
        public Skin TableSkin { get; private set; }
        public int Triangles { get; private set; }
        public bool Growing { get; private set; }
        public bool Shown => All.Count > 0 && All.TrueForAll(s => s.Grow >= 1f || s.Go == null);
        /// <summary>Raised when a skin first pops into view (for a sound).</summary>
        public System.Action<Skin> Popped;

        Transform root;

        public void Build(RoomModel room, Recipe r)
        {
            Clear();
            root = new GameObject("Skins: " + r.Biome).transform;
            root.SetParent(transform, false);
            var colors = WorldColors.For(r);
            var def = r.Def;
            Vector3 head = room.Head;
            var order = new List<RoomModel.Piece>(room.Pieces);
            order.Sort((a, b) => Vector3.Distance(a.Center, head).CompareTo(Vector3.Distance(b.Center, head)));
            int i = 0;
            int tris = 0;
            foreach (var p in order)
            {
                if (!def.Skins.TryGetValue(p.Role, out var id) || id == "none") continue;
                var rng = new Rng(r.Seed * 17 + (p.Key.Length > 0 ? p.Key.GetHashCode() : i * 31));
                GameObject go;
                int made;
                // A sofa's scanned box reaches the top of its backrest. Sitting at that height would put you inside
                // the log, so a tall seat becomes a seat at real seat height plus a back along the wall side.
                if (p.Role == Role.Seat && p.Size.y > 0.62f && p.Size.z > 0.5f)
                    go = SofaSkin(p, id, colors, rng, room, out made);
                else
                {
                    var mb = new MeshBuilder();
                    mb.AoBaseY = 0f;
                    mb.AoHeight = Mathf.Min(0.15f, p.Size.y * 0.3f);
                    if (!Skins.Build(mb, id, p.Size, colors, rng) || mb.TriangleCount == 0) continue;
                    made = mb.TriangleCount;
                    go = Util.MeshObject("Skin " + id, root, mb.ToMesh(id), World);
                }
                if (go == null) continue;
                tris += made;
                go.transform.position = p.Bottom;
                go.transform.rotation = p.Rotation;
                go.transform.localScale = new Vector3(1f, 0.001f, 1f);
                go.SetActive(false);
                var s = new Skin { Piece = p, Id = id, Go = go, Delay = i * Stagger, FlatTop = Skins.FlatTop(id) };
                All.Add(s);
                if (p == room.Table) TableSkin = s;
                i++;
            }
            Triangles = tris;
        }

        /// <summary>Seat height for a tall seat's box (sofas: about 45 cm, never above 52 % of the box).</summary>
        public static float SeatHeight(Vector3 size) => Mathf.Clamp(size.y * 0.52f, 0.36f, 0.5f);

        /// <summary>+1 if the piece's back (its +z side) is the side nearer a wall, else -1.</summary>
        public static float BackSide(RoomModel.Piece p, RoomModel room)
        {
            Vector3 plus = p.ToWorld(new Vector3(0, p.Size.y * 0.5f, p.Size.z * 0.5f));
            Vector3 minus = p.ToWorld(new Vector3(0, p.Size.y * 0.5f, -p.Size.z * 0.5f));
            float dp = room.DistanceToWalls(plus), dm = room.DistanceToWalls(minus);
            if (Mathf.Abs(dp - dm) > 0.05f) return dp < dm ? 1f : -1f;
            // No clear wall: the back faces away from the room's middle.
            Vector3 mid = room.Bounds.center;
            return Vector3.Distance(plus, mid) > Vector3.Distance(minus, mid) ? 1f : -1f;
        }

        GameObject SofaSkin(RoomModel.Piece p, string id, WorldColors colors, Rng rng, RoomModel room, out int tris)
        {
            int count = 0;
            float seatH = SeatHeight(p.Size);
            float backD = Mathf.Clamp(p.Size.z * 0.28f, 0.15f, 0.3f);
            float side = BackSide(p, room);
            var holder = new GameObject("Skin " + id + " (seat and back)");
            holder.transform.SetParent(root, false);
            void Part(string name, Vector3 size, float zOffset)
            {
                var mb = new MeshBuilder();
                mb.AoBaseY = 0f;
                mb.AoHeight = Mathf.Min(0.12f, size.y * 0.3f);
                if (!Skins.Build(mb, id, size, colors, rng) || mb.TriangleCount == 0) return;
                count += mb.TriangleCount;
                var go = Util.MeshObject(name, holder.transform, mb.ToMesh(id + "-" + name), World);
                go.transform.localPosition = new Vector3(0f, 0f, zOffset);
            }
            Part("seat", new Vector3(p.Size.x, seatH, p.Size.z - backD), -side * backD * 0.5f);
            Part("back", new Vector3(p.Size.x, p.Size.y, backD), side * (p.Size.z - backD) * 0.5f);
            tris = count;
            if (count == 0) { Util.Kill(holder); return null; }
            return holder;
        }

        /// <summary>One piece's skin goes (your real furniture shows, dark) or grows back (lit again).</summary>
        public bool Dim(RoomModel.Piece piece, bool dark)
        {
            foreach (var s in All)
            {
                if (s.Piece != piece || s.Go == null) continue;
                s.Target = dark ? 0f : 1f;
                s.Delay = 0f;
                Growing = true;
                Apply(s);   // already where it should be (put back whole by the storm's end): shown as it is now
                return true;
            }
            return false;
        }

        /// <summary>Starts growing every skin in (or shrinking them away with <paramref name="show"/> false).</summary>
        public void Show(bool show, bool instant = false)
        {
            float t = 0f;
            foreach (var s in All)
            {
                s.Target = show ? 1f : 0f;
                s.Delay = show ? t : 0f;
                t += Stagger;
                if (instant) s.Grow = s.Target;
                Apply(s);
            }
            Growing = !instant;
        }

        void Update()
        {
            if (!Growing) return;
            bool any = false;
            float dt = Time.deltaTime;
            foreach (var s in All)
            {
                if (s.Delay > 0f) { s.Delay -= dt; any = true; continue; }
                if (Mathf.Approximately(s.Grow, s.Target)) continue;
                bool wasHidden = s.Grow <= 0f;
                s.Grow = Mathf.MoveTowards(s.Grow, s.Target, dt / Mathf.Max(0.05f, GrowSeconds));
                if (wasHidden && s.Grow > 0f) Popped?.Invoke(s);
                Apply(s);
                any = true;
            }
            Growing = any;
        }

        void Apply(Skin s)
        {
            if (s.Go == null) return;
            float g = s.Target > 0.5f ? Util.Pop(s.Grow) : Util.Ease(s.Grow);
            s.Go.SetActive(s.Grow > 0.001f);
            float y = Mathf.Max(0.001f, g);
            float xz = Mathf.Lerp(0.6f, 1f, Mathf.Clamp01(g));
            s.Go.transform.localScale = new Vector3(xz, y, xz);
            if (Shell != null) Shell.HolderScale(s.Piece, 1f - Mathf.Clamp01(s.Grow * 1.15f));
        }

        public void Clear()
        {
            if (root != null) Util.Kill(root.gameObject);
            All.Clear();
            TableSkin = null;
            Growing = false;
        }

        /// <summary>The skin whose piece the point touches (within <paramref name="margin"/>), or null.</summary>
        public Skin Touching(Vector3 world, float margin, out bool onTop)
        {
            onTop = false;
            Skin best = null;
            float bestD = margin;
            foreach (var s in All)
            {
                if (s.Grow < 0.99f) continue;
                float d = s.Piece.Distance(world);
                if (d > bestD) continue;
                bestD = d;
                best = s;
                var l = s.Piece.ToLocal(world);
                onTop = l.y >= s.Piece.Size.y - margin * 1.5f;
            }
            return best;
        }
    }
}
