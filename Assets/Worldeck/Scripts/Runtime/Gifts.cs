using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// What exploring brings home: each wonder you find sends a gift to your table (a lantern, a crystal, a star,
    /// a seed, a cloud). Back at camp, touch a gift and it changes the world (lanterns light the night, the crystal
    /// brings the northern lights, the star starts shooting stars, the seed makes flowers, the cloud brings rain
    /// or clears it). Gifts wait on the near-left edge of the table, glowing, until used.
    /// </summary>
    public sealed class Gifts : MonoBehaviour
    {
        public Material World, Glow, Panel;
        public Font Font;
        public Transform Head;
        /// <summary>A gift was touched: the words to act on ("add lanterns").</summary>
        public event Action<string, string> Used;

        /// <summary>The gift each wonder (by its order in the world) gives: name, prop kind, what touching it says.</summary>
        public static readonly (string name, string kind, string phrase)[] Table =
        {
            ("a lantern", "lantern", "add lanterns"),
            ("a crystal", "crystal_cluster", "add northern lights"),
            ("a fallen star", "sun_shard", "add shooting stars"),
            ("a seed", "seed", "add flowers"),
            ("a little cloud", "cloud", "add clouds"),
        };

        readonly List<GameObject> shown = new List<GameObject>();
        Transform root;
        RoomModel room;
        Recipe recipe;
        public int Waiting => shown.Count;

        public void Begin(RoomModel roomModel, Recipe r)
        {
            Clear();
            room = roomModel;
            recipe = r;
            root = new GameObject("Gifts").transform;
            root.SetParent(transform, false);
        }

        public void Clear()
        {
            shown.Clear();
            if (root != null) Util.Kill(root.gameObject);
            root = null;
        }

        /// <summary>Puts gift <paramref name="index"/> on the table. Returns its name ("a lantern").</summary>
        public string Give(int index)
        {
            if (root == null || room == null || room.Table == null || recipe == null) return "";
            var g = Table[((index % Table.Length) + Table.Length) % Table.Length];
            var t = room.Table;
            // Along the near-left edge, 10 cm apart.
            Vector3 headLocal = t.ToLocal(room.Head);
            float nearZ = Mathf.Sign(headLocal.z == 0f ? -1f : headLocal.z) * (t.Size.z * 0.5f - 0.06f);
            float x = Mathf.Clamp(-t.Size.x * 0.5f + 0.07f + shown.Count * 0.1f, -t.Size.x * 0.5f + 0.05f, t.Size.x * 0.5f - 0.05f);
            Vector3 at = t.ToWorld(new Vector3(x, t.Size.y + 0.045f, nearZ));
            var go = new GameObject("Gift " + g.kind);
            go.transform.SetParent(root, false);
            go.transform.position = at;
            var mb = new MeshBuilder();
            Props.Build(mb, g.kind, WorldColors.For(recipe), new Rng(index + 11));
            var b = mb.Bounds;
            float scale = 0.07f / Mathf.Max(0.001f, Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
            var body = Util.MeshObject("Body", go.transform, mb.ToMesh("gift-" + g.kind), World);
            body.transform.localScale = Vector3.one * scale;
            body.transform.localPosition = new Vector3(-b.center.x, -b.center.y, -b.center.z) * scale;
            Effects.Halo("Glow", go.transform, Glow, 0.2f, new Color(1f, 0.85f, 0.55f) * 0.6f);
            var btn = PokeButton.Make("gift " + index, go.transform, Panel, Font, "", new Vector2(0.09f, 0.09f));
            btn.transform.rotation = Quaternion.LookRotation(at - (Head != null ? Head.position : room.Head), Vector3.up);
            btn.Idle = new Color(0f, 0f, 0f, 0.02f);
            btn.Hint = "Touch to use " + g.name;
            string phrase = g.phrase, name = g.name;
            btn.Pressed += () =>
            {
                Trace.Info("Gifts", "used", "gift", name, "phrase", phrase);
                shown.Remove(go);
                var burst = Effects.Burst(root, Glow, Color.white);
                burst.transform.position = go.transform.position;
                burst.Play();
                Util.Kill(go);
                Used?.Invoke(name, phrase);
            };
            shown.Add(go);
            Trace.Info("Gifts", "on the table", "gift", g.name, "waiting", shown.Count);
            return g.name;
        }

        void Update()
        {
            foreach (var g in shown)
                if (g != null && g.transform.childCount > 0) g.transform.GetChild(0).Rotate(0f, 30f * Time.unscaledDeltaTime, 0f, Space.World);
        }
    }
}
