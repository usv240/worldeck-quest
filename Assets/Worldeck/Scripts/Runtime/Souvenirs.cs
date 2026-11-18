using System;
using System.Collections;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The collection on your real table: one small souvenir from every world you finished, standing along
    /// the table's back edge and remembered against the scanned table, so it is there next time too. Touch
    /// one (or look and pinch) to go back to its world.
    /// </summary>
    public sealed class Souvenirs : MonoBehaviour
    {
        public Material World, Glow, Panel;
        public Font Font;
        public Transform Head;
        public event Action<Progress.Souvenir> Chosen;
        public event Action<Progress.Code> CodeChosen;

        readonly List<GameObject> shown = new List<GameObject>();
        readonly List<GameObject> keepsakes = new List<GameObject>();
        /// <summary>Empty places for Mara's keepsakes still to come (after her first call).</summary>
        public int EmptyPlaces { get; private set; }
        public int Count => shown.Count;
        /// <summary>Beacons on the table: one for each day in a row Mara landed.</summary>
        public int Beacons { get; private set; }

        /// <summary>Places every souvenir in <paramref name="p"/> for this room (and assigns new ones a spot).</summary>
        public void Show(Progress p, RoomModel room)
        {
            Clear();
            if (room == null || room.Table == null) return;
            var table = room.Table;
            float away = table.ToLocal(room.Head).z > 0f ? -1f : 1f;
            int slot = 0;
            foreach (var s in p.Souvenirs)
            {
                Vector3 local;
                bool sameTable = !table.Virtual && s.Anchor.Length > 0 && s.Anchor == table.Key;
                if (sameTable) local = new Vector3(s.X, s.Y, s.Z);
                else
                {
                    local = Slot(table, slot, away);
                    if (!table.Virtual) { s.Anchor = table.Key; s.X = local.x; s.Y = local.y; s.Z = local.z; }
                }
                slot++;
                // Saved on another table that isn't in this scan: put it on this one.
                if (!sameTable && s.Anchor.Length > 0 && s.Anchor != table.Key) { s.Anchor = table.Key; s.X = local.x; s.Y = local.y; s.Z = local.z; }
                var def = Kit.Get(s.Biome);
                if (def == null) continue;
                var r = Recipe.For(def.Id);
                var mb = new MeshBuilder();
                mb.AoBaseY = 0f; mb.AoHeight = 0.03f;
                var colors = WorldColors.For(r);
                if (s.Variant == "gold")
                {
                    // The gold souvenir: earned by the world's best play (a golden marshmallow…).
                    colors.Prop = colors.PropAccent = new Color(0.95f, 0.76f, 0.3f);
                    colors.Emissive = new Color(1f, 0.85f, 0.45f);
                }
                Builds.Souvenir(mb, s.Kind, colors, new Rng(s.Biome.GetHashCode()));
                var go = Util.MeshObject("Souvenir " + s.Kind, transform, mb.ToMesh(s.Kind), World);
                go.transform.position = table.ToWorld(local);
                go.transform.rotation = table.Rotation * Quaternion.Euler(0, s.Yaw + 180f, 0);
                var halo = Effects.Halo("Glow", go.transform, Glow, 0.09f, WorldColors.Hex(def.Palette.Emissive, Color.white) * 0.25f);
                halo.transform.localPosition = Vector3.up * 0.05f;
                var b = PokeButton.Make("Souvenir " + s.Biome, go.transform, Panel, Font, "", new Vector2(0.09f, 0.11f));
                b.Back.enabled = false;
                b.transform.localPosition = Vector3.up * 0.055f;
                b.gameObject.AddComponent<FaceHead>().Head = Head;
                var keep = s;
                b.Hint = "Touch to go back there";
                b.Pressed += () => Chosen?.Invoke(keep);
                shown.Add(go);
            }
            // Friends' codes: small glowing tokens along the near edge, each one a world that fits this room.
            int k = 0;
            foreach (var code in p.Codes)
            {
                var r = ShareCode.Decode(code.Text);
                if (r == null) continue;
                var mb = new MeshBuilder();
                var colors = WorldColors.For(r);
                mb.AoBaseY = 0f; mb.AoHeight = 0.02f;
                mb.Cylinder(Vector3.zero, 0.026f, 0.022f, 0.012f, 6, Color.Lerp(colors.Prop, colors.PropAccent, 0.5f), true, false, 0.15f, 0f, 0f, 0f, colors.Emissive);
                mb.Cylinder(new Vector3(0f, 0.012f, 0f), 0.014f, 0.006f, 0.02f, 6, colors.Emissive, true, false, 0.9f);
                var go = Util.MeshObject("Code token " + code.Text, transform, mb.ToMesh("token"), World);
                float x = ((k + 1) / 2) * 0.09f * ((k % 2 == 0) ? 1 : -1);
                var local = new Vector3(Mathf.Clamp(x, -table.Size.x * 0.5f + 0.05f, table.Size.x * 0.5f - 0.05f), table.Size.y + 0.001f, -away * (table.Size.z * 0.5f - 0.07f));
                go.transform.position = table.ToWorld(local);
                go.transform.rotation = table.Rotation;
                var halo = Effects.Halo("Glow", go.transform, Glow, 0.07f, WorldColors.Hex(r.Def.Palette.Emissive, Color.white) * 0.3f);
                halo.transform.localPosition = Vector3.up * 0.025f;
                var b = PokeButton.Make("Code " + code.Text, go.transform, Panel, Font, "", new Vector2(0.07f, 0.07f));
                b.Back.enabled = false;
                b.transform.localPosition = Vector3.up * 0.03f;
                b.gameObject.AddComponent<FaceHead>().Head = Head;
                var keep = code;
                b.Hint = "A friend's world: touch to go";
                b.Pressed += () => CodeChosen?.Invoke(keep);
                shown.Add(go);
                k++;
            }
            keepsakes.Clear();
            EmptyPlaces = 0;
            // Her three keepsakes in a row: those you have, and (once she has called) an empty place for each still
            // to come, so the story shows its length on your own table.
            int have = Mathf.Min(Story.Chapters, p.MissionChapter);
            int places = p.MissionChapter > 0 ? Story.Chapters : have;
            for (int ch = 0; ch < places; ch++)
            {
                var local = new Vector3(-table.Size.x * 0.5f + 0.07f + ch * 0.08f, table.Size.y + 0.001f, -away * (table.Size.z * 0.5f - 0.16f));
                local.x = Mathf.Min(local.x, table.Size.x * 0.5f - 0.05f);
                if (ch >= have)
                {
                    var mbe = new MeshBuilder();
                    mbe.Cylinder(Vector3.zero, 0.03f, 0.03f, 0.003f, 18, new Color(0.16f, 0.19f, 0.27f), true, true);
                    mbe.Cylinder(new Vector3(0f, 0.003f, 0f), 0.024f, 0.024f, 0.001f, 18, new Color(0.3f, 0.34f, 0.45f), true, false, 0.15f);
                    var empty = Util.MeshObject("Keepsake to come " + Story.Keepsake(ch), transform, mbe.ToMesh("keepsake place"), World);
                    empty.transform.position = table.ToWorld(local);
                    empty.transform.rotation = table.Rotation;
                    shown.Add(empty);
                    EmptyPlaces++;
                    continue;
                }
                var go = Util.MeshObject("Keepsake " + Story.Keepsake(ch), transform, Keepsake(ch), World);
                go.transform.position = table.ToWorld(local);
                go.transform.rotation = table.Rotation * Quaternion.Euler(0f, 180f + ch * 11f, 0f);
                var halo = Effects.Halo("Glow", go.transform, Glow, 0.06f, new Color(1f, 0.8f, 0.45f) * 0.25f);
                halo.transform.localPosition = Vector3.up * 0.02f;
                shown.Add(go);
                keepsakes.Add(go);
            }
            // A small beacon for each day in a row you've brought Mara down (up to seven): your table keeps count.
            Beacons = Mathf.Min(7, p.FlightStreakOn(DateTime.UtcNow));
            for (int i = 0; i < Beacons; i++)
            {
                var mb = new MeshBuilder();
                mb.Cylinder(Vector3.zero, 0.012f, 0.009f, 0.006f, 10, new Color(0.25f, 0.3f, 0.45f), true, true);
                mb.Cylinder(new Vector3(0f, 0.006f, 0f), 0.003f, 0.003f, 0.03f, 6, new Color(0.7f, 0.7f, 0.75f), true, false, 0.1f);
                mb.Ico(new Vector3(0f, 0.04f, 0f), Vector3.one * 0.012f, new Color(1f, 0.82f, 0.5f), null, 0f, 1f);
                var go = Util.MeshObject("Flight beacon " + (i + 1), transform, mb.ToMesh("beacon"), World);
                var local = new Vector3(table.Size.x * 0.5f - 0.05f - i * 0.04f, table.Size.y + 0.001f, -away * (table.Size.z * 0.5f - 0.16f));
                local.x = Mathf.Max(local.x, -table.Size.x * 0.5f + 0.05f);
                go.transform.position = table.ToWorld(local);
                go.transform.rotation = table.Rotation;
                var halo = Effects.Halo("Glow", go.transform, Glow, 0.05f, new Color(1f, 0.8f, 0.45f) * (i == Beacons - 1 ? 0.5f : 0.25f));
                halo.transform.localPosition = Vector3.up * 0.04f;
                shown.Add(go);
            }
            Trace.Info("Souvenirs", "shown on the table", "count", shown.Count, "codes", p.Codes.Count, "keepsakes", Mathf.Min(Story.Chapters, p.MissionChapter), "beacons", Beacons, "table", table.Virtual ? "virtual" : table.Label);
        }

        /// <summary>Mara's keepsake for a chapter, built at the origin standing on y = 0 (about 7 cm across).</summary>
        public static Mesh Keepsake(int chapter)
        {
            var mb = new MeshBuilder();
            var gold = new Color(0.95f, 0.72f, 0.32f);
            var glow = new Color(1f, 0.85f, 0.5f);
            switch (chapter)
            {
                case 0:   // a round flight patch with a star
                    mb.Cylinder(Vector3.zero, 0.034f, 0.034f, 0.006f, 18, new Color(0.25f, 0.3f, 0.45f), true, true);
                    mb.Cylinder(new Vector3(0f, 0.006f, 0f), 0.028f, 0.028f, 0.002f, 18, gold, true, false, 0.2f);
                    for (int i = 0; i < 5; i++)
                    {
                        mb.Push(new Vector3(0f, 0.009f, 0f), Quaternion.Euler(0f, i * 72f, 0f), Vector3.one);
                        mb.Box(new Vector3(0f, 0f, 0.008f), new Vector3(0.006f, 0.003f, 0.016f), glow, null, 0.9f);
                        mb.Pop();
                    }
                    break;
                case 1:   // pilot's wings
                    for (int side = -1; side <= 1; side += 2)
                        for (int f = 0; f < 3; f++)
                        {
                            mb.Push(new Vector3(side * (0.012f + f * 0.011f), 0.004f + f * 0.0015f, 0f), Quaternion.Euler(0f, 0f, side * -8f), Vector3.one);
                            mb.Box(Vector3.zero, new Vector3(0.012f, 0.004f, 0.016f - f * 0.004f), gold, null, 0.3f);
                            mb.Pop();
                        }
                    mb.Cylinder(Vector3.zero, 0.009f, 0.009f, 0.008f, 12, glow, true, true, 0.9f);
                    break;
                default:   // her pilot's badge: a gold star on a shield
                    mb.Box(new Vector3(0f, 0.003f, 0f), new Vector3(0.05f, 0.006f, 0.058f), new Color(0.25f, 0.3f, 0.45f), null, 0f, true);
                    for (int i = 0; i < 5; i++)
                    {
                        mb.Push(new Vector3(0f, 0.008f, 0f), Quaternion.Euler(0f, i * 72f, 0f), Vector3.one);
                        mb.Box(new Vector3(0f, 0f, 0.011f), new Vector3(0.009f, 0.004f, 0.022f), gold, null, 0.6f);
                        mb.Pop();
                    }
                    mb.Cylinder(new Vector3(0f, 0.006f, 0f), 0.008f, 0.008f, 0.005f, 10, glow, true, false, 1f);
                    break;
            }
            return mb.ToMesh("keepsake " + chapter);
        }

        /// <summary>Spots along the back of the table (away from the player), 11 cm apart, from the middle out.</summary>
        public static Vector3 Slot(RoomModel.Piece table, int i, float away = 1f)
        {
            float back = (table.Size.z * 0.5f - 0.07f) * Mathf.Sign(away);
            int side = (i % 2 == 0) ? 1 : -1;
            float x = ((i + 1) / 2) * 0.11f * side;
            x = Mathf.Clamp(x, -table.Size.x * 0.5f + 0.06f, table.Size.x * 0.5f - 0.06f);
            return new Vector3(x, table.Size.y + 0.001f, back);
        }

        public void SetVisible(bool on) { foreach (var g in shown) if (g != null) g.SetActive(on); }

        /// <summary>
        /// The end of Mara's story, in your room: her keepsakes light up one after another, and her ship crosses
        /// over your room, high up, and is gone. About five seconds. Calm effects: no sparks.
        /// </summary>
        public IEnumerator StoryEnd(Transform head)
        {
            foreach (var k in keepsakes)
            {
                if (k == null) continue;
                Sfx.Instance?.At("souvenir_chime", k.transform.position, 0.7f, 1f + 0.12f * keepsakes.IndexOf(k));
                if (!Effects.Calm)
                {
                    var burst = Effects.Burst(transform, Glow, new Color(1f, 0.85f, 0.55f));
                    burst.transform.position = k.transform.position + Vector3.up * 0.03f;
                    burst.Play();
                    Destroy(burst.gameObject, 2f);
                }
                var halo = Effects.Halo("Lit", k.transform, Glow, 0.11f, new Color(1f, 0.8f, 0.45f) * 0.45f);
                halo.transform.localPosition = Vector3.up * 0.025f;
                yield return new WaitForSecondsRealtime(0.6f);
            }
            if (head == null) yield break;
            // Her ship, crossing over the room from one side to the other, high and far enough to read as the sky.
            Vector3 f = head.forward; f.y = 0f; f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            Vector3 from = head.position + f * 2.6f - right * 3.5f + Vector3.up * 1.1f;
            Vector3 to = head.position + f * 2.2f + right * 3.5f + Vector3.up * 1.6f;
            var mb = new MeshBuilder();
            mb.Ico(Vector3.zero, new Vector3(0.07f, 0.04f, 0.13f), new Color(1f, 0.9f, 0.7f), null, 0f, 1f);
            var ship = Util.MeshObject("Mara's ship over the room", transform, mb.ToMesh("ship"), World).transform;
            Effects.Halo("Glow", ship, Glow, 0.5f, new Color(1f, 0.85f, 0.6f) * 0.6f);
            Sfx.Instance?.At("reveal_whoosh", Vector3.Lerp(from, to, 0.5f), 0.7f, 1.2f);
            float t0 = Time.unscaledTime, dur = 3.4f;
            while (Time.unscaledTime - t0 < dur && ship != null)
            {
                float k = (Time.unscaledTime - t0) / dur;
                ship.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.25f;
                ship.rotation = Quaternion.LookRotation((to - from).normalized);
                yield return null;
            }
            if (ship != null) Util.Kill(ship.gameObject);
        }

        public void Clear()
        {
            foreach (var g in shown) if (g != null) Util.Kill(g);
            shown.Clear();
        }
    }

    /// <summary>Keeps a transform turned towards the head (for invisible touch targets).</summary>
    public sealed class FaceHead : MonoBehaviour
    {
        public Transform Head;
        void LateUpdate()
        {
            if (Head == null) return;
            Vector3 to = transform.position - Head.position;
            if (to.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(to, Vector3.up);
        }
    }
}
