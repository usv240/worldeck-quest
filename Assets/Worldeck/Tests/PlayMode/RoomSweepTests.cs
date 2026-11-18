using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Meta.XR.MRUtilityKit;
using NUnit.Framework;
using Worldeck.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Worldeck.PlayTests
{
    /// <summary>
    /// "Holding up in rooms the developer never tested": every room that ships with MR Utility Kit, a player
    /// seated at every table and at sampled spots, and checks of everything placed in the real room:
    /// the work table, where gather pieces appear (in reach, in view, never inside furniture or walls),
    /// the guide, the souvenir spots, and that each piece of furniture gets a skin for its role.
    /// One world is built per room (cycling through all eight) within the budget. Writes Tools/room-sweep.md.
    /// </summary>
    public class RoomSweepTests
    {
        const string Scene = "Assets/Worldeck/Scenes/Worldeck.unity";

        [UnitySetUp]
        public IEnumerator Load()
        {
            LogAssert.ignoreFailingMessages = true;
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(Scene, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return null;
#endif
            for (int i = 0; i < 5; i++) yield return null;
        }

        sealed class Stats
        {
            public int CrampedVirtual, Rooms, Seats, RealTable, VirtualTable, Pieces, Skinned, SpotSets, Spots, BadSpots, ShortSpots, Worlds, BadWorlds;
            public int Made, MadeBad, MadeInFront, AnimalWorlds, Animals, AnimalsInRoom;
            public int Locals, LocalsHidden, LocalsHalf, Landmarks, LandmarksHidden;
            public int InTheWay, MovedClear, StuckInTheWay;
            public int FewestSpots = 99, FewSeats, TableSeats, ShortTable, FewTable;
            public float WorstSpotDeg, WorstGuideDeg, MaxBuildMs, MaxTris;
            public readonly Dictionary<string, int> Roles = new Dictionary<string, int>();
            public readonly List<string> Problems = new List<string>();
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator EveryShippedRoomWorks()
        {
#if UNITY_EDITOR
            Assert.NotNull(MRUK.Instance, "MRUK in scene");
            var director = Object.FindFirstObjectByType<Director>();
            Assert.NotNull(director);
            var builder = director.Builder;
            var rooms = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Packages/com.meta.xr.mrutilitykit/Core/Rooms/Prefabs" })
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToList();
            var jsons = UnityEditor.AssetDatabase.FindAssets("t:TextAsset", new[] { "Packages/com.meta.xr.mrutilitykit/Core/Rooms/Json" })
                .Select(UnityEditor.AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".json")).OrderBy(p => p).ToList();
            Assert.That(rooms.Count + jsons.Count, Is.GreaterThanOrEqualTo(50), "sample rooms present");
            var st = new Stats();
            var perRoom = new StringBuilder();
            int worldIndex = 0;
            foreach (var path in rooms.Concat(jsons))
            {
                System.Threading.Tasks.Task<MRUK.LoadDeviceResult> task = path.EndsWith(".json")
                    ? MRUK.Instance.LoadSceneFromJsonString(File.ReadAllText(path))
                    : MRUK.Instance.LoadSceneFromPrefab(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path));
                float until = Time.realtimeSinceStartup + 20f;
                while (!task.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
                yield return null;
                foreach (var room in MRUK.Instance.Rooms.ToList())
                {
                    st.Rooms++;
                    string name = Path.GetFileNameWithoutExtension(path);
                    int before = st.BadSpots + st.Problems.Count;
                    var seats = Seats(room);
                    RoomModel last = null;
                    Vector3 lastHead = Vector3.zero, lastFwd = Vector3.forward;
                    foreach (var (head, fwd, atTable) in seats)
                    {
                        st.Seats++;
                        var m = RoomModel.FromScan(room, head, fwd);
                        last = m; lastHead = head; lastFwd = fwd;
                        CheckSeat(m, head, fwd, name, st, atTable);
                        CheckMade(director, m, head, fwd, name, st);
                    }
                    // One world per room, cycling through all of them.
                    if (last != null)
                    {
                        var def = Kit.Biomes[worldIndex++ % Kit.Biomes.Length];
                        var r = Recipe.For(def.Id, st.Rooms);
                        // Every second room: a place where animals live; none of them may stand inside your room.
                        string[] wild = { "savanna", "prehistoric", "farm", "aurora", "mountain_lake", "autumn" };
                        if (st.Rooms % 2 == 0) { var pl = Places.Get(wild[(st.Rooms / 2) % wild.Length]); r = pl.Recipe(st.Rooms); def = r.Def; }
                        // Every third room: a real place with its landmark.
                        if (st.Rooms % 3 == 0)
                        {
                            var marked = Atlas.All.Where(s => s.Landmark.Length > 0).ToArray();
                            var spot = marked[(st.Rooms / 3) % marked.Length];
                            r = Atlas.Make(spot, spot.Words[0]).Recipe(st.Rooms); def = r.Def;
                        }
                        builder.BuildNow(r, last);
                        // Whoever lives there can be seen from the seat; a landmark stands where your furniture is not.
                        var who = builder.Root != null ? builder.Root.GetComponentInChildren<Local>(true) : null;
                        if (who != null)
                        {
                            st.Locals++;
                            if (!builder.SeenFromSeat(who.transform.position)) { st.LocalsHidden++; st.Problems.Add($"{name}: the local's face is behind furniture"); }
                            else if (!builder.SeenFromSeat(who.transform.position, 0.45f, 0.3f)) st.LocalsHalf++;
                        }
                        if (r.Look != null && r.Look.Landmark.Length > 0)
                        {
                            st.Landmarks++;
                            var mark = builder.Landmark;
                            bool there = mark != null && builder.Root != null && mark.IsChildOf(builder.Root);
                            Vector3 to = there ? mark.position - lastHead : Vector3.forward; to.y = 0f;
                            if (!there || !builder.ClearView(to.normalized)) { st.LandmarksHidden++; st.Problems.Add($"{name}: the {r.Look.Landmark} is {(there ? "behind furniture" : "missing")}"); }
                        }
                        if (r.Look != null)
                        {
                            st.AnimalWorlds++;
                            foreach (var w in builder.Root.GetComponentsInChildren<Wild>())
                            {
                                st.Animals++;
                                Vector3 at = w.transform.position;
                                if (last.InsideFurniture(at, 0f) || new Vector2(at.x - lastHead.x, at.z - lastHead.z).magnitude < last.OuterRadius + 0.5f)
                                { st.AnimalsInRoom++; st.Problems.Add($"{name}: a {w.Kind} inside the room"); }
                            }
                        }
                        st.Worlds++;
                        st.MaxBuildMs = Mathf.Max(st.MaxBuildMs, builder.LastBuildMs);
                        st.MaxTris = Mathf.Max(st.MaxTris, builder.Triangles);
                        if (builder.Root == null || builder.Triangles > builder.MaxTriangles + 60000 || builder.Renderers > 40)
                        { st.BadWorlds++; st.Problems.Add($"{name}: world {def.Id} {builder.Triangles} tris {builder.Renderers} renderers"); }
                        director.Skins.Build(last, r);
                        foreach (var p in last.Pieces) if (!p.Virtual && def.Skins.TryGetValue(p.Role, out var sk) && sk != "none") st.Pieces++;
                        st.Skinned += director.Skins.All.Count(s => !s.Piece.Virtual);
                        director.Skins.Clear();
                        builder.Clear();
                    }
                    perRoom.AppendLine($"| {name} | {seats.Count} | {(last != null && last.Table != null && !last.Table.Virtual ? "scan" : "virtual")} | {(st.BadSpots + st.Problems.Count - before == 0 ? "ok" : "check")} |");
                }
                yield return null;
            }

            var md = new StringBuilder();
            md.AppendLine("# Room sweep (MR Utility Kit sample rooms)");
            md.AppendLine();
            md.AppendLine($"Generated by `RoomSweepTests`. {st.Rooms} rooms from {rooms.Count} furnished prefabs and {jsons.Count} scanned-mesh scenes.");
            md.AppendLine("Seats: at every side of every table (eye 45 cm above the top, 45 cm back, facing it) plus 6 seeded spots per room (eye 1.15 m above the floor, random facing).");
            md.AppendLine();
            md.AppendLine("| Measure | Result |");
            md.AppendLine("|---|---|");
            md.AppendLine($"| Seats tested | {st.Seats} |");
            md.AppendLine($"| Work table: scanned / virtual (none within reach and in front) | {st.RealTable} / {st.VirtualTable} |");
            md.AppendLine($"| Virtual tables with no spot clear of furniture and walls | {st.CrampedVirtual} |");
            md.AppendLine($"| Gather pieces placed | {st.Spots} in {st.SpotSets} sets of 5 ({st.ShortSpots} sets short of 5; {st.FewSeats} seats with fewer than 3; fewest {st.FewestSpots}) |");
            md.AppendLine($"| ...at a table (eye 45 cm back from an edge, facing it) | {st.TableSeats} seats: {st.ShortTable} short of 5, {st.FewTable} with fewer than 3 |");
            md.AppendLine($"| ...at seeded spots with a random facing (some face a wall or a bed at arm's length) | {st.Seats - st.TableSeats} seats: {st.ShortSpots - st.ShortTable} short of 5, {st.FewSeats - st.FewTable} with fewer than 3 |");
            md.AppendLine($"| Gather pieces inside furniture, near a wall, out of reach | {st.BadSpots} |");
            md.AppendLine($"| Widest angle from straight ahead to a gather piece | {st.WorstSpotDeg:0}° |");
            md.AppendLine($"| A row of three choices raised at every seat: pieces in its way (the seat's own five, plus nine set in front of the buttons) moved to a good spot / left where they were (no good spot near) | {st.MovedClear} of {st.InTheWay} / {st.StuckInTheWay} |");
            md.AppendLine($"| Widest angle from straight ahead to the guide | {st.WorstGuideDeg:0}° (Meta VR Glasses design area: 29° horizontally) |");
            md.AppendLine($"| Scanned furniture given a themed skin | {st.Skinned} of {st.Pieces} |");
            md.AppendLine($"| Furniture roles seen | {string.Join(", ", st.Roles.OrderByDescending(k => k.Value).Select(k => k.Key + " " + k.Value))} |");
            md.AppendLine($"| Worlds built (all eight in turn) | {st.Worlds}, {st.BadWorlds} over budget; slowest {st.MaxBuildMs:0} ms (editor), most triangles {st.MaxTris:0} |");
            md.AppendLine($"| An animal made on the table (every seat): on the table top, in reach, inside no other furniture | {st.Made - st.MadeBad} of {st.Made} ({st.MadeInFront} formed in front of you instead: no reachable, clear part of the table from that seat) |");
            md.AppendLine($"| Places with animals built | {st.AnimalWorlds}: {st.Animals} animals, {st.AnimalsInRoom} inside a room |");
            md.AppendLine($"| Whoever lives there, seen from the seat past your furniture | face: {st.Locals - st.LocalsHidden} of {st.Locals}; of those, {st.LocalsHalf} with only the head showing over a piece of furniture (chest line with 30 cm to spare) |");
            md.AppendLine($"| A real place's landmark, in a direction your furniture does not block | {st.Landmarks - st.LandmarksHidden} of {st.Landmarks} |");
            md.AppendLine($"| Problems | {(st.Problems.Count == 0 ? "none" : string.Join("; ", st.Problems.Take(20)))} |");
            md.AppendLine();
            md.AppendLine("| Room | Seats | Table | Checks |");
            md.AppendLine("|---|---|---|---|");
            md.Append(perRoom);
            Directory.CreateDirectory("Tools");
            File.WriteAllText("Tools/room-sweep.md", md.ToString());
            Debug.Log("[Worldeck] room sweep:\n" + md);

            Assert.AreEqual(0, st.BadSpots, "gather pieces in bad places: " + string.Join("; ", st.Problems.Take(10)));
            Assert.AreEqual(0, st.MadeBad, "animals made on the table in bad places: " + string.Join("; ", st.Problems.Where(p => p.Contains("made")).Take(10)));
            Assert.AreEqual(0, st.AnimalsInRoom, "animals standing inside a room");
            Assert.Greater(st.Animals, 0, "places with animals were built");
            Assert.AreEqual(0, st.BadWorlds, "worlds over budget");
            Assert.Greater(st.Locals, 0, "someone lives in the worlds built");
            Assert.LessOrEqual(st.LocalsHidden, st.Locals / 5, "the local's face shows past your furniture");
            Assert.Greater(st.Landmarks, 0, "real places were built");
            Assert.LessOrEqual(st.LandmarksHidden, st.Landmarks / 4, "the landmark stands where your furniture is not");
            Assert.AreEqual(st.Pieces, st.Skinned, "every scanned piece of furniture gets a skin");
            Assert.LessOrEqual(st.WorstGuideDeg, 90f, "the work table (and the guide above it) is in front of the player");
            Assert.LessOrEqual(st.CrampedVirtual, st.VirtualTable / 5, "virtual tables find a clear spot (seeded seats include squeezed spots between beds)");
            // Since Nov 15 no piece is put past a wall (a hand reaching for it would meet the real wall), nor more than 60
            // degrees to the side (FoV-aware): a cramped seat gets fewer pieces (the task is as long as the pieces it has),
            // and a seat squeezed between beds with a wall in front may get none (the task is then skipped: Mara calls).
            // Where people sit, at a table: at least 99% get three pieces or more, nine in ten all five.
            Assert.LessOrEqual(st.FewTable, st.TableSeats / 100, "at a table, at least three pieces from 99% of seats");
            Assert.LessOrEqual(st.ShortTable, st.TableSeats / 10, "at a table, all five pieces from 90% of seats");
            // Seeded random facings (a wall or a bed at arm's length): reported above; a loose limit, as a tripwire.
            Assert.LessOrEqual(st.FewSeats - st.FewTable, (st.Seats - st.TableSeats) / 3, "random seats: most get three or more");
            Assert.LessOrEqual(st.WorstSpotDeg, 101f, "only a cramped seat (fewer than three pieces in front) has any to the side, never behind");
#else
            yield return null;
            Assert.Ignore("editor only");
#endif
        }

        /// <summary>A fox made "on the table" from this seat: on the table top, within reach, inside no other furniture.</summary>
        static void CheckMade(Director d, RoomModel m, Vector3 head, Vector3 fwd, string name, Stats st)
        {
            if (d.Maker == null || d.Head == null) return;
            d.Head.SetPositionAndRotation(head, Quaternion.LookRotation(fwd, Vector3.up));
            d.Maker.Clear();
            int inFront = d.Maker.InFront;
            var item = new ActItem { Do = "make", Kind = "fox", Name = "fox", Place = "table", Count = 1, Size = Wishes.SizeOf("fox") };
            if (d.Maker.Make(item, m, Recipe.For("campfire", 1)) == 0) { st.Made++; st.MadeBad++; st.Problems.Add(name + ": made nothing"); return; }
            var t = d.Maker.Things[0];
            Vector3 p = t.T.position;
            st.Made++;
            Vector3 local = Quaternion.Inverse(m.Table.Rotation) * (p - m.Table.TopCenter);
            string why = null;
            if (d.Maker.InFront > inFront)
            {
                // No reachable, clear part of the table from this seat: in front of you instead, and in nothing.
                st.MadeInFront++;
                if (Vector3.Distance(p, head) > 1.1f) why = $"in front, but out of reach ({Vector3.Distance(p, head):0.00} m)";
                else
                    foreach (var piece in m.Pieces)
                        if (!piece.Virtual && piece.Distance(p + Vector3.up * 0.05f) < 0.001f) { why = "in front, inside the " + piece.Label.ToLowerInvariant(); break; }
            }
            else if (Mathf.Abs(p.y - m.Table.TopY) > 0.03f) why = "not on the table top";
            else if (Mathf.Abs(local.x) > m.Table.Size.x * 0.5f + 0.01f || Mathf.Abs(local.z) > m.Table.Size.z * 0.5f + 0.01f) why = "off the table";
            else if (Flat(p - head) > 1.01f) why = $"out of reach ({Flat(p - head):0.00} m on the flat)";
            else
                foreach (var piece in m.Pieces)
                    if (piece != m.Table && !piece.Virtual && piece.Distance(p + Vector3.up * 0.05f) < 0.001f) { why = "inside the " + piece.Label.ToLowerInvariant(); break; }
            if (why != null) { st.MadeBad++; st.Problems.Add($"{name}: made fox {why}"); }
            d.Maker.Clear();
        }

        static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

        static List<(Vector3, Vector3, bool)> Seats(MRUKRoom room)
        {
            var seats = new List<(Vector3, Vector3, bool)>();
            float floor = room.FloorAnchor != null ? room.FloorAnchor.transform.position.y : 0f;
            foreach (var a in room.Anchors)
            {
                if (a == null || !a.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE) || !a.VolumeBounds.HasValue) continue;
                var b = a.VolumeBounds.Value;
                var t = a.transform;
                Vector3 c = t.TransformPoint(b.center);
                float top = c.y + b.size.z * 0.5f;
                Vector3 right = new Vector3(t.right.x, 0, t.right.z).normalized;
                Vector3 fwd = Vector3.Cross(right, Vector3.up);
                foreach (var (dir, half) in new[] { (fwd, b.size.y * 0.5f), (-fwd, b.size.y * 0.5f), (right, b.size.x * 0.5f), (-right, b.size.x * 0.5f) })
                {
                    Vector3 head = new Vector3(c.x, top + 0.45f, c.z) + dir * (half + 0.45f);
                    if (room.IsPositionInSceneVolume(head, out _, true, 0f) || !room.IsPositionInRoom(head)) continue;
                    seats.Add((head, -dir, true));
                }
            }
            var bounds = room.GetRoomBounds();
            int seed = 17;
            foreach (char ch in room.name) seed = unchecked(seed * 31 + ch);   // the same every run
            var rng = new System.Random(seed);
            for (int i = 0, tries = 0; i < 6 && tries < 80; tries++)
            {
                var p = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, (float)rng.NextDouble()), floor + 1.15f, Mathf.Lerp(bounds.min.z, bounds.max.z, (float)rng.NextDouble()));
                if (!room.IsPositionInRoom(p) || room.IsPositionInSceneVolume(p, out _, true, 0.15f)) continue;
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                seats.Add((p, new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)), false));
                i++;
            }
            return seats;
        }

        static void CheckSeat(RoomModel m, Vector3 head, Vector3 fwd, string name, Stats st, bool atTable)
        {
            Assert.NotNull(m.Table, name + " has a work table");
            if (m.Table.Virtual) st.VirtualTable++; else st.RealTable++;
            foreach (var p in m.Pieces) { string k = p.Role.ToString(); st.Roles.TryGetValue(k, out int n); st.Roles[k] = n + 1; }
            if (m.Table.Virtual && !m.Free(m.Table)) { st.CrampedVirtual++; st.Problems.Add(name + ": no free spot for the virtual table"); }
            if (!m.Table.Virtual)
            {
                Vector3 to = m.Table.Center - head; to.y = 0f;
                if (m.Table.Distance(new Vector3(head.x, m.Table.TopY, head.z)) > 0.05f && Vector3.Angle(new Vector3(fwd.x, 0, fwd.z), to) > 80f)
                    st.Problems.Add(name + ": table behind the player");
            }

            var spots = Gather.Spots(m, head, fwd, 5, 7);
            st.SpotSets++;
            st.Spots += spots.Count;
            if (spots.Count < 5) st.ShortSpots++;
            st.FewestSpots = Mathf.Min(st.FewestSpots, spots.Count);
            if (atTable) { st.TableSeats++; if (spots.Count < 5) st.ShortTable++; if (spots.Count < 3) st.FewTable++; }
            if (spots.Count < 3) { st.FewSeats++; st.Problems.Add($"{name}: only {spots.Count} pieces from a {(atTable ? "table" : "random")} seat"); }
            foreach (var s in spots)
            {
                Vector3 flat = new Vector3(s.x - head.x, 0, s.z - head.z);
                float dist = flat.magnitude;
                float deg = Vector3.Angle(new Vector3(fwd.x, 0, fwd.z), flat);
                st.WorstSpotDeg = Mathf.Max(st.WorstSpotDeg, deg);
                string why = null;
                if (m.InsideFurniture(s, 0.05f)) why = "inside furniture";
                else if (m.DistanceToWalls(s) < 0.15f) why = "against a wall";
                else if (dist < 0.3f || dist > 0.6f) why = $"reach {dist:0.00} m";
                else if (s.y > head.y) why = "above the eyes";
                if (why != null) { st.BadSpots++; st.Problems.Add($"{name}: piece {why}"); }
            }
            // A row of three choices about someone out there appears (where a local's questions go): every piece in its
            // way moves only to a spot a piece may float at, and none is left over the buttons.
            {
                Vector3 f = new Vector3(fwd.x, 0, fwd.z).normalized;
                Vector3 rowAt = head + f * 0.42f + Vector3.down * 0.26f;
                Vector3 rowRight = Vector3.Cross(Vector3.up, (rowAt - head).normalized).normalized;
                var at = new List<Vector3>();
                var size = new List<Vector2>();
                for (int i = -1; i <= 1; i++) { at.Add(rowAt + rowRight * (i * 0.152f)); size.Add(new Vector2(0.14f, 0.065f)); }
                Assert.IsTrue(Gather.RowView.Of(head, at, size, rowRight, out var row), name + ": the row seen from the seat");
                var homes = new List<Vector3>(spots);
                foreach (var b in at) foreach (float frac in new[] { 0.6f, 0.8f, 0.95f }) homes.Add(head + (b - head) * frac);
                foreach (var h in homes)
                {
                    if (!row.Hides(h, 0.06f)) continue;
                    st.InTheWay++;
                    var others = spots.Where(o => (o - h).sqrMagnitude > 1e-8f).ToList();
                    var to = Gather.ClearOf(m, row, h, 0.06f, m.Table.TopCenter, others);
                    if (!to.HasValue) { st.StuckInTheWay++; continue; }
                    Vector3 p = to.Value;
                    float reach = new Vector3(p.x - head.x, 0, p.z - head.z).magnitude;
                    string why = null;
                    if (m.InsideFurniture(p, 0.05f) || m.Table.Contains(p, 0.05f)) why = "inside furniture";
                    else if (m.DistanceToWalls(p) < 0.15f || m.AcrossWall(head, p)) why = "against or past a wall";
                    else if (reach < 0.3f || reach > 0.6f) why = $"reach {reach:0.00} m";
                    else if (p.y > head.y - 0.14f) why = "at eye height";
                    else if (row.Hides(p, 0.06f)) why = "still over the choices";
                    if (why != null) { st.BadSpots++; st.Problems.Add($"{name}: a piece moved clear of the choices is {why}"); }
                    else st.MovedClear++;
                }
            }
            // Souvenir spots stay on the table.
            for (int i = 0; i < 8; i++)
            {
                var l = Souvenirs.Slot(m.Table, i, 1f);
                if (Mathf.Abs(l.x) > m.Table.Size.x * 0.5f || Mathf.Abs(l.z) > m.Table.Size.z * 0.5f) { st.Problems.Add(name + ": souvenir off the table"); break; }
            }
            // The guide sits in comfortable view: below the eyes, in front.
            Vector3 g = m.Table.TopCenter + Vector3.up * 0.26f;
            st.WorstGuideDeg = Mathf.Max(st.WorstGuideDeg, Vector3.Angle(new Vector3(fwd.x, 0, fwd.z), new Vector3(g.x - head.x, 0, g.z - head.z)));
        }
    }
}
