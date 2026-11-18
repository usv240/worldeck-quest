using System;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The real room as the world builder needs it: floor and ceiling heights, the wall quads, every
    /// piece of furniture with its role and its exact box (centre, size, yaw), the table nearest the player
    /// and the seat they are on. Built from the Quest room scan (MRUK), or a plausible virtual room when
    /// there is no scan, so the app always works and says which it is.
    /// </summary>
    public sealed class RoomModel
    {
        public sealed class Piece
        {
            public MRUKAnchor Anchor;
            public Role Role;
            public string Label = "";
            /// <summary>Box centre, world space.</summary>
            public Vector3 Center;
            /// <summary>Box size: x across, y height, z depth (in the piece's own frame).</summary>
            public Vector3 Size;
            /// <summary>Yaw-only rotation of the box (its x across, y up, z depth).</summary>
            public Quaternion Rotation = Quaternion.identity;
            public bool Virtual;
            public float TopY => Center.y + Size.y * 0.5f;
            public float BottomY => Center.y - Size.y * 0.5f;
            public Vector3 Bottom => Center - Vector3.up * (Size.y * 0.5f);
            public Vector3 TopCenter => Center + Vector3.up * (Size.y * 0.5f);
            /// <summary>Stable key for saving things relative to this piece ("" for virtual pieces).</summary>
            public string Key = "";

            /// <summary>The point in local box coordinates (x across, y up from the bottom, z depth).</summary>
            public Vector3 ToLocal(Vector3 world) => Quaternion.Inverse(Rotation) * (world - Bottom);
            public Vector3 ToWorld(Vector3 local) => Bottom + Rotation * local;

            /// <summary>True if <paramref name="world"/> is inside the box grown by <paramref name="margin"/>.</summary>
            public bool Contains(Vector3 world, float margin = 0f)
            {
                var l = ToLocal(world);
                return Mathf.Abs(l.x) <= Size.x * 0.5f + margin && Mathf.Abs(l.z) <= Size.z * 0.5f + margin &&
                       l.y >= -margin && l.y <= Size.y + margin;
            }

            /// <summary>Distance from a world point to the box surface (0 inside).</summary>
            public float Distance(Vector3 world)
            {
                var l = ToLocal(world);
                float dx = Mathf.Max(0f, Mathf.Abs(l.x) - Size.x * 0.5f);
                float dz = Mathf.Max(0f, Mathf.Abs(l.z) - Size.z * 0.5f);
                float dy = l.y < 0f ? -l.y : Mathf.Max(0f, l.y - Size.y);
                return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }

        public struct Wall
        {
            public Vector3 A, B;      // bottom corners (world)
            public float Bottom, Top; // heights
            public Vector3 Normal;    // into the room
        }

        public float FloorY;
        public float CeilingY = 2.6f;
        public Bounds Bounds;
        public readonly List<Piece> Pieces = new List<Piece>();
        public readonly List<Wall> Walls = new List<Wall>();
        public Piece Table;
        /// <summary>The piece the player sits on, if the scan has one.</summary>
        public Piece Seat;
        /// <summary>Where the player's head was when the room was read (seated eye point).</summary>
        public Vector3 Head;
        public Vector3 Forward = Vector3.forward;
        /// <summary>"scan", or why not: "no scan", "no permission", "editor".</summary>
        public string Source = "virtual";
        public bool Scanned => Source == "scan";

        // ---------- from the scan ----------

        static readonly (MRUKAnchor.SceneLabels flag, string name)[] LabelNames =
        {
            (MRUKAnchor.SceneLabels.TABLE, "TABLE"), (MRUKAnchor.SceneLabels.COUCH, "COUCH"), (MRUKAnchor.SceneLabels.BED, "BED"),
            (MRUKAnchor.SceneLabels.STORAGE, "STORAGE"), (MRUKAnchor.SceneLabels.SCREEN, "SCREEN"), (MRUKAnchor.SceneLabels.LAMP, "LAMP"),
            (MRUKAnchor.SceneLabels.PLANT, "PLANT"), (MRUKAnchor.SceneLabels.OTHER, "OTHER"), (MRUKAnchor.SceneLabels.DOOR_FRAME, "DOOR_FRAME"),
            (MRUKAnchor.SceneLabels.WINDOW_FRAME, "WINDOW_FRAME")
        };

        public static List<string> Labels(MRUKAnchor a)
        {
            var list = new List<string>();
            foreach (var (flag, name) in LabelNames) if (a.HasAnyLabel(flag)) list.Add(name);
            return list;
        }

        public static RoomModel FromScan(MRUKRoom room, Vector3 head, Vector3 forward)
        {
            var m = new RoomModel { Source = "scan", Head = head, Forward = Flat(forward) };
            m.FloorY = room.FloorAnchor != null ? room.FloorAnchor.transform.position.y : 0f;
            m.CeilingY = room.CeilingAnchor != null ? room.CeilingAnchor.transform.position.y : m.FloorY + 2.6f;
            if (m.CeilingY - m.FloorY < 1.8f) m.CeilingY = m.FloorY + 2.4f;
            m.Bounds = room.GetRoomBounds();

            foreach (var w in room.WallAnchors)
            {
                if (w == null || !w.PlaneRect.HasValue) continue;
                var r = w.PlaneRect.Value;
                var t = w.transform;
                Vector3 a = t.TransformPoint(new Vector3(r.xMin, r.yMin, 0f));
                Vector3 b = t.TransformPoint(new Vector3(r.xMax, r.yMin, 0f));
                Vector3 top = t.TransformPoint(new Vector3(r.xMin, r.yMax, 0f));
                m.Walls.Add(new Wall { A = new Vector3(a.x, m.FloorY, a.z), B = new Vector3(b.x, m.FloorY, b.z),
                                       Bottom = Mathf.Min(a.y, m.FloorY), Top = Mathf.Max(top.y, m.CeilingY), Normal = Flat(t.forward) });
            }

            foreach (var a in room.Anchors)
            {
                if (a == null) continue;
                var labels = Labels(a);
                var role = Kit.RoleOf(labels);
                if (role == null) continue;
                Piece p = null;
                if (a.VolumeBounds.HasValue) p = FromVolume(a, a.VolumeBounds.Value);
                else if (a.PlaneRect.HasValue && (role == Role.Door || role == Role.Window)) p = FromFrame(a, a.PlaneRect.Value, m.FloorY, role.Value);
                if (p == null) continue;
                p.Role = role.Value;
                p.Label = labels.Count > 0 ? labels[0] : "";
                if (p.Role == Role.Clutter && p.Size.y > Kit.TallClutter) p.Role = Role.Tall;
                // Pieces that sit on another surface (a monitor on a desk) keep their real bottom.
                p.Key = Key(a);
                m.Pieces.Add(p);
            }
            m.Choose(head);
            return m;
        }

        static Piece FromVolume(MRUKAnchor a, Bounds b)
        {
            var t = a.transform;
            // MRUK volumes: anchor local +Z is world up, the pose sits on the top face, XY is the footprint.
            Vector3 right = Flat(t.right);
            if (right.sqrMagnitude < 1e-4f) right = Vector3.right;
            Vector3 fwd = Vector3.Cross(right, Vector3.up);
            var size = new Vector3(Mathf.Abs(b.size.x), Mathf.Abs(b.size.z), Mathf.Abs(b.size.y));
            if (size.x < 0.05f || size.y < 0.05f || size.z < 0.05f) return null;
            return new Piece
            {
                Anchor = a, Center = t.TransformPoint(b.center), Size = size,
                Rotation = Quaternion.LookRotation(fwd, Vector3.up)
            };
        }

        static Piece FromFrame(MRUKAnchor a, Rect r, float floorY, Role role)
        {
            var t = a.transform;
            Vector3 c = t.TransformPoint(new Vector3(r.center.x, r.center.y, 0f));
            Vector3 normal = Flat(t.forward);
            if (normal.sqrMagnitude < 1e-4f) return null;
            float h = r.height, w = r.width;
            if (role == Role.Door) { h = Mathf.Max(h, c.y + h * 0.5f - floorY); c.y = floorY + h * 0.5f; }
            return new Piece
            {
                Anchor = a, Center = c + normal * 0.04f, Size = new Vector3(w, h, 0.08f),
                Rotation = Quaternion.LookRotation(normal, Vector3.up)
            };
        }

        public static string Key(MRUKAnchor a)
        {
            if (a == null) return "";
            try { return a.Anchor.Uuid.ToString("N"); } catch { return a.name; }
        }

        // ---------- without a scan ----------

        /// <summary>
        /// A room for when there is no scan: a 4.6 m square around the player, 2.6 m high, and a virtual
        /// table in front at a comfortable height. Nothing in it is touchable, and the app says so.
        /// </summary>
        public static RoomModel Virtual(Vector3 head, Vector3 forward, float floorY, string why)
        {
            var m = new RoomModel { Source = why == "scan" ? "virtual" : why, Head = head, Forward = Flat(forward), FloorY = floorY, CeilingY = floorY + 2.6f };
            Vector3 f = m.Forward, right = Vector3.Cross(Vector3.up, f);
            Vector3 c = new Vector3(head.x, floorY, head.z);
            float half = 2.3f;
            Vector3[] corners =
            {
                c + (-right - f) * half, c + (right - f) * half, c + (right + f) * half, c + (-right + f) * half
            };
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = corners[i], b = corners[(i + 1) % 4];
                Vector3 n = (c - (a + b) * 0.5f); n.y = 0; n.Normalize();
                m.Walls.Add(new Wall { A = a, B = b, Bottom = floorY, Top = m.CeilingY, Normal = n });
            }
            m.Bounds = new Bounds(c + Vector3.up * 1.3f, new Vector3(half * 2f, 2.6f, half * 2f));
            float eye = head.y - floorY;
            float top = Mathf.Clamp(eye - 0.48f, 0.5f, 0.85f);
            var table = new Piece
            {
                Role = Role.Surface, Label = "TABLE", Virtual = true,
                Center = new Vector3(head.x, floorY + top * 0.5f, head.z) + f * 0.55f,
                Size = new Vector3(0.9f, top, 0.55f), Rotation = Quaternion.LookRotation(f, Vector3.up)
            };
            m.Pieces.Add(table);
            m.Table = table;
            return m;
        }

        // ---------- choices ----------

        Vector3? lastTable;

        /// <summary>Picks the table nearest the player (else a virtual one in front) and the seat they're on.</summary>
        public void Choose(Vector3 head)
        {
            Head = head;
            Table = null; Seat = null;
            float best = float.MaxValue;
            Vector3 fwd = Forward;
            int surfaces = 0; string nearestWhy = "";
            float nearestD = float.MaxValue;
            foreach (var p in Pieces)
            {
                if (p.Role != Role.Surface || p.Virtual) continue;
                surfaces++;
                // A table to work at seated: its top well below the eyes, its edge within reach, and in front of you
                // (the room sweep found nearest tables behind the player and counters at eye height).
                Vector3 atTop = new Vector3(head.x, p.TopY, head.z);
                float edge = p.Distance(atTop);
                Vector3 to = p.Center - head; to.y = 0f;
                float angle = to.sqrMagnitude > 1e-4f ? Vector3.Angle(fwd, to) : 0f;
                // "Too high" by the floor as well as the eyes: right after the headset comes back on the eyes can read
                // low for a moment (88 cm seen on Nov 12), and a real desk must not be thrown away for it.
                bool tooHigh = p.TopY > Mathf.Max(head.y - 0.25f, FloorY + 0.95f);
                string why = tooHigh ? "too high" : p.TopY < FloorY + 0.22f ? "too low" : edge > 0.75f ? "out of reach" : angle > 80f ? "behind" : "";
                if (edge < nearestD) { nearestD = edge; nearestWhy = $"{p.Label} {why} edge {edge:0.00} m angle {angle:0}° top {p.TopY - FloorY:0.00} m"; }
                if (why.Length > 0) continue;
                float area = p.Size.x * p.Size.z;
                float score = edge + angle / 180f * 0.6f - (area > 0.25f ? 0.05f : 0f) + (area < 0.15f ? 0.3f : 0f);
                // The table chosen before stays the table (the same place, a small bonus): no swapping to a stool.
                if (lastTable.HasValue && Vector3.Distance(p.Center, lastTable.Value) < 0.15f) score -= 0.35f;
                if (score < best) { best = score; Table = p; }
            }
            if (Table == null)
            {
                Table = FreeVirtualTable(head);
                Pieces.Add(Table);
                Trace.Info("Room", "no table in reach and in front: virtual table", "size", Table.Size, "free", Free(Table), "surfaces_seen", surfaces, "nearest_rejected", nearestWhy);
            }
            else
            {
                lastTable = Table.Center;
                Trace.Info("Room", "table chosen", "label", Table.Label, "size", Table.Size, "edge_m", Table.Distance(new Vector3(head.x, Table.TopY, head.z)), "top_over_floor_m", Table.TopY - FloorY);
            }
            foreach (var p in Pieces)
            {
                if (p.Role != Role.Seat && p.Role != Role.Clutter) continue;
                var l = p.ToLocal(head);
                bool over = Mathf.Abs(l.x) <= p.Size.x * 0.5f + 0.25f && Mathf.Abs(l.z) <= p.Size.z * 0.5f + 0.25f;
                float above = head.y - p.TopY;
                if (over && above > 0.35f && above < 1.05f) { Seat = p; break; }
            }
        }

        /// <summary>
        /// A virtual table in front of the player where nothing real is: tries straight ahead, then turning
        /// left and right, nearer and farther, then a smaller table; never overlapping real furniture or a wall.
        /// </summary>
        Piece FreeVirtualTable(Vector3 head)
        {
            float eye = head.y - FloorY;
            float top = Mathf.Clamp(eye - 0.48f, 0.45f, 0.85f);
            var sizes = new[] { new Vector3(0.8f, top, 0.5f), new Vector3(0.6f, top, 0.4f), new Vector3(0.45f, top, 0.35f), new Vector3(0.36f, top, 0.3f) };
            var angles = new[] { 0f, -25f, 25f, -50f, 50f, -75f, 75f };
            var dists = new[] { 0.55f, 0.48f, 0.65f, 0.42f };
            Piece fallback = null;
            foreach (var size in sizes)
                foreach (var a in angles)
                    foreach (var d in dists)
                    {
                        Vector3 dir = Quaternion.Euler(0f, a, 0f) * Forward;
                        float reach = d + size.z * 0.5f - 0.25f;   // its near edge about 25–40 cm in front
                        var c = new Vector3(head.x, FloorY + top * 0.5f, head.z) + dir * Mathf.Max(reach, size.z * 0.5f + 0.15f);
                        var t = new Piece { Role = Role.Surface, Label = "TABLE", Virtual = true, Center = c, Size = size, Rotation = Quaternion.LookRotation(dir, Vector3.up) };
                        fallback ??= t;
                        if (Free(t)) return t;
                    }
            return fallback;
        }

        /// <summary>True if a box overlaps no real furniture and stays 5 cm inside the walls.</summary>
        public bool Free(Piece box)
        {
            foreach (var p in Pieces)
            {
                if (p.Virtual || p.Role == Role.Door || p.Role == Role.Window) continue;
                if (box.BottomY > p.TopY + 0.02f || p.BottomY > box.TopY - 0.02f) continue;
                if (Overlap2D(box, p, 0.01f)) return false;
            }
            float hx = box.Size.x * 0.5f, hz = box.Size.z * 0.5f;
            foreach (var corner in new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz) })
            {
                Vector3 w = box.Center + box.Rotation * corner;
                if (DistanceToWalls(w) < 0.02f) return false;
                if (Bounds.size.sqrMagnitude > 0f && (w.x < Bounds.min.x || w.x > Bounds.max.x || w.z < Bounds.min.z || w.z > Bounds.max.z)) return false;
            }
            return true;
        }

        /// <summary>Separating-axis test of two yaw-rotated rectangles (their footprints), grown by <paramref name="margin"/>.</summary>
        static bool Overlap2D(Piece a, Piece b, float margin)
        {
            Vector2 ca = new Vector2(a.Center.x, a.Center.z), cb = new Vector2(b.Center.x, b.Center.z);
            Vector3 ar = a.Rotation * Vector3.right, af = a.Rotation * Vector3.forward, br = b.Rotation * Vector3.right, bf = b.Rotation * Vector3.forward;
            var axes = new[] { new Vector2(ar.x, ar.z), new Vector2(af.x, af.z), new Vector2(br.x, br.z), new Vector2(bf.x, bf.z) };
            float ahx = a.Size.x * 0.5f + margin, ahz = a.Size.z * 0.5f + margin, bhx = b.Size.x * 0.5f, bhz = b.Size.z * 0.5f;
            foreach (var axis in axes)
            {
                Vector2 n = axis.normalized;
                float ra = ahx * Mathf.Abs(Vector2.Dot(new Vector2(ar.x, ar.z), n)) + ahz * Mathf.Abs(Vector2.Dot(new Vector2(af.x, af.z), n));
                float rb = bhx * Mathf.Abs(Vector2.Dot(new Vector2(br.x, br.z), n)) + bhz * Mathf.Abs(Vector2.Dot(new Vector2(bf.x, bf.z), n));
                if (Mathf.Abs(Vector2.Dot(cb - ca, n)) > ra + rb) return false;
            }
            return true;
        }

        /// <summary>True if the point is inside any real piece of furniture (grown by margin).</summary>
        public bool InsideFurniture(Vector3 world, float margin = 0.03f)
        {
            foreach (var p in Pieces) if (!p.Virtual && p.Contains(world, margin)) return true;
            return false;
        }

        /// <summary>Horizontal distance from a point to the nearest wall line (∞ with no walls).</summary>
        public float DistanceToWalls(Vector3 world)
        {
            float best = float.PositiveInfinity;
            Vector2 p = new Vector2(world.x, world.z);
            foreach (var w in Walls)
            {
                Vector2 a = new Vector2(w.A.x, w.A.z), b = new Vector2(w.B.x, w.B.z);
                Vector2 ab = b - a;
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        /// <summary>True if the point is inside the walls (seen from above). True with no walls.</summary>
        public bool Encloses(Vector3 world)
        {
            if (Walls.Count < 3) return true;
            // A ray towards +x crosses the walls an odd number of times from inside.
            int crossings = 0;
            foreach (var w in Walls)
            {
                float az = w.A.z, bz = w.B.z;
                if ((az > world.z) == (bz > world.z)) continue;
                float x = w.A.x + (world.z - az) / (bz - az) * (w.B.x - w.A.x);
                if (x > world.x) crossings++;
            }
            return (crossings & 1) == 1;
        }

        /// <summary>True if the straight line from a to b (seen from above) crosses a wall, with a inside the room:
        /// b is on the other side of a wall. (From outside the walls, nothing is said.)</summary>
        public bool AcrossWall(Vector3 a, Vector3 b)
        {
            if (!Encloses(a)) return false;
            Vector2 p = new Vector2(a.x, a.z), r = new Vector2(b.x - a.x, b.z - a.z);
            foreach (var w in Walls)
            {
                Vector2 c = new Vector2(w.A.x, w.A.z), s = new Vector2(w.B.x - w.A.x, w.B.z - w.A.z);
                float den = r.x * s.y - r.y * s.x;
                if (Mathf.Abs(den) < 1e-9f) continue;
                Vector2 cp = c - p;
                float t = (cp.x * s.y - cp.y * s.x) / den, u = (cp.x * r.y - cp.y * r.x) / den;
                if (t >= 0f && t <= 1f && u >= 0f && u <= 1f) return true;
            }
            return false;
        }

        /// <summary>The radius (from the head, horizontally) that encloses the room's walls: where the open world begins.</summary>
        public float OuterRadius
        {
            get
            {
                float r = 2.5f;
                Vector2 h = new Vector2(Head.x, Head.z);
                foreach (var w in Walls)
                {
                    r = Mathf.Max(r, Vector2.Distance(h, new Vector2(w.A.x, w.A.z)));
                    r = Mathf.Max(r, Vector2.Distance(h, new Vector2(w.B.x, w.B.z)));
                }
                return Mathf.Min(r, 12f);
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }
    }
}
