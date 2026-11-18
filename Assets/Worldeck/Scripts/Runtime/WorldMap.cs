using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The world map: every world as a small glowing miniature on a tile, in an arc in front of you, with its
    /// name under it. Touch a tile with a fingertip, or look at it for a moment, to go there. Badges show
    /// today's world and the worlds you have a souvenir from. A row underneath holds the other ways in:
    /// describe a place, surprise me, saved worlds, a friend's code, and how it works.
    /// It is The library of worlds made physical, and the one place every option lives.
    /// </summary>
    public sealed class WorldMap : MonoBehaviour
    {
        public Transform Head;
        public Material World, Glow, Panel;
        public Font Font;
        public float Distance = 0.52f;
        public event Action<string> Chosen;

        public bool Showing { get; private set; }
        public string Highlighted { get; private set; } = "";
        readonly List<PokeButton> buttons = new List<PokeButton>();
        readonly List<Transform> minis = new List<Transform>();
        Transform root;
        Caption title;
        float shownAt;

        /// <summary>Shows the map in front of the head. <paramref name="highlight"/> pulses one world (the tutorial's pick).</summary>
        public void Show(Progress progress, string highlight = "", bool inWorld = false, string heading = "", bool frozen = false)
        {
            Hide();
            if (Head == null) return;
            Showing = true;
            shownAt = Time.unscaledTime;
            Highlighted = highlight ?? "";
            root = new GameObject("World map").transform;
            root.SetParent(transform, false);
            Vector3 f = Head.forward; f.y = 0f;
            f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            root.position = Head.position;
            root.rotation = Quaternion.LookRotation(f, Vector3.up);

            string daily = Recipe.Daily(DateTime.UtcNow).Biome;
            var biomes = Featured.Defs;
            // Two rows of four, 17° apart, inside the narrow design area (±26°), centred about 10° below the eyes.
            for (int i = 0; i < biomes.Length; i++)
            {
                var def = biomes[i];
                int row = i / 4, col = i % 4;
                float yaw = (col - (Mathf.Min(4, biomes.Length) - 1) * 0.5f) * 17f;
                float pitch = row == 0 ? 3f : 17f;
                var tile = Tile(def, yaw, pitch, progress, daily);
                if (def.Id == Highlighted) tile.Highlight = true;
            }
            // The other ways in.
            var extras = new List<(string label, string id)> { ("How it works", "help"), ("Comfort & access", "settings") };
            if (inWorld && frozen) extras.Add(("Resume", "resume"));
            if (inWorld) extras.Add(("My room", "end"));
            if (inWorld) extras.Add(("Close map", "close"));
            // One row of up to five; more go in two short rows (everything stays inside the narrow design area).
            int perRow = extras.Count <= 5 ? extras.Count : (extras.Count + 1) / 2;
            for (int i = 0; i < extras.Count; i++)
            {
                int row = i / perRow, col = i % perRow;
                int inRow = Mathf.Min(perRow, extras.Count - row * perRow);
                float yaw = (col - (inRow - 1) * 0.5f) * 11.5f;
                var b = PokeButton.Make("map " + extras[i].id, root, Panel, Font, extras[i].label, new Vector2(0.085f, 0.036f), 13f);
                Place(b.transform, yaw, 28f + row * 7f, Distance - 0.04f);
                string id = extras[i].id;
                b.Pressed += () => Pick(id);
                buttons.Add(b);
            }
            title = Caption.Make("Map title", root, Font, Panel, Head, 18f, 30f);
            title.Fixed = true;
            Place(title.transform, 0f, -10f, Distance + 0.02f);
            title.Say(heading.Length > 0 ? heading : "Where to? Touch a world, or look at it.", 0f);
            Trace.Info("Map", "shown", "highlight", Highlighted, "in_world", inWorld, "tiles", buttons.Count);
            Sfx.Instance?.Ui("item_appear", 0.6f);
        }

        PokeButton Tile(BiomeDef def, float yaw, float pitch, Progress progress, string daily)
        {
            var b = PokeButton.Make("map world:" + def.Id, root, Panel, Font, def.Short, new Vector2(0.11f, 0.105f), 14f);
            Place(b.transform, yaw, pitch, Distance);
            var pal = WorldColors.From(def.Palette);
            b.Idle = new Color(pal.Top.r * 0.6f + 0.04f, pal.Top.g * 0.6f + 0.05f, pal.Top.b * 0.6f + 0.09f, 0.9f);
            b.Accent = new Color(pal.Emissive.r, pal.Emissive.g, pal.Emissive.b, 0.95f);
            // The name sits at the bottom of the tile; the miniature stands above it.
            b.Label.transform.localPosition = new Vector3(0f, -0.037f, -0.003f);
            b.FitText(13f);
            var mb = new MeshBuilder();
            mb.AoBaseY = 0f; mb.AoHeight = 0.015f;
            Builds.Souvenir(mb, def.Souvenir, WorldColors.For(Recipe.For(def.Id)), new Rng(def.Id.GetHashCode()));
            var mini = Util.MeshObject("Mini " + def.Id, b.transform, mb.ToMesh("mini-" + def.Id), World);
            mini.transform.localPosition = new Vector3(0f, -0.022f, -0.03f);
            mini.transform.localRotation = Quaternion.Euler(-12f, 180f, 0f);
            mini.transform.localScale = Vector3.one * 0.55f;
            minis.Add(mini.transform);
            var halo = Effects.Halo("Mini glow", mini.transform, Glow, 0.11f, pal.Emissive * 0.25f);
            halo.transform.localPosition = Vector3.up * 0.04f;
            // Badges.
            bool today = def.Id == daily;
            bool kept = progress != null && progress.Souvenirs.Exists(s => s.Biome == def.Id);
            if (today || kept)
            {
                var badge = new GameObject("Badge").AddComponent<TextMesh>();
                badge.transform.SetParent(b.transform, false);
                badge.transform.localPosition = new Vector3(0f, 0.047f, -0.003f);
                badge.font = Font; badge.fontSize = 48; badge.characterSize = 0.0014f;   // about 13 dmm (it was 6.5)
                badge.anchor = TextAnchor.MiddleCenter; badge.alignment = TextAlignment.Center;
                badge.text = (today ? "Today" : "") + (today && kept ? " · " : "") + (kept ? "★ visited" : "");
                badge.color = today ? new Color(1f, 0.82f, 0.45f) : new Color(0.85f, 0.95f, 1f);
                badge.GetComponent<MeshRenderer>().sharedMaterial = Font.material;
            }
            string id = "world:" + def.Id;
            b.Pressed += () => Pick(id);
            buttons.Add(b);
            return b;
        }

        void Place(Transform t, float yawDeg, float pitchDownDeg, float dist)
        {
            Quaternion q = root.rotation * Quaternion.Euler(pitchDownDeg, yawDeg, 0f);
            Vector3 p = root.position + q * Vector3.forward * dist;
            t.position = p;
            t.rotation = Quaternion.LookRotation(p - Head.position, Vector3.up);
        }

        void Pick(string id)
        {
            if (Time.unscaledTime - shownAt < 0.8f) return;   // no accidental press as it appears
            Trace.Info("Map", "picked", "id", id);
            if (id == "close") { Hide(); return; }
            Hide();
            Chosen?.Invoke(id);
        }

        public void Hide()
        {
            if (root != null) Util.Kill(root.gameObject);
            root = null;
            buttons.Clear();
            minis.Clear();
            if (Showing) Trace.Info("Map", "hidden");
            Showing = false;
            Highlighted = "";
        }

        /// <summary>Where a world's tile is (for the coach to point at), or the map's centre.</summary>
        public Vector3 TilePos(string worldId)
        {
            foreach (var b in buttons) if (b != null && b.Id == "map world:" + worldId) return b.transform.position;
            return root != null ? root.position : Vector3.zero;
        }

        /// <summary>The position above the map's centre (where Juniper waits while the map is open).</summary>
        public Vector3 Top => root != null ? root.position + root.rotation * Quaternion.Euler(-6f, 0f, 0f) * Vector3.forward * (Distance + 0.02f) + Vector3.up * 0.05f : Vector3.zero;

        void Update()
        {
            if (!Showing) return;
            float t = Time.unscaledTime;
            for (int i = 0; i < minis.Count; i++)
                if (minis[i] != null) minis[i].localRotation = Quaternion.Euler(-12f, 180f + Mathf.Sin(t * 0.6f + i) * 25f, 0f);
        }
    }
}
