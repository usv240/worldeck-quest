using System;
using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// "How it works": every way to use Worldeck on one card in front of you, one row per thing you can do,
    /// in plain words. Rows that can be shown have a "Show me" button: the coach's see-through hand acts it
    /// out (<see cref="ShowMe"/>). Reachable from the map, the menu, Juniper's choices, or by saying "help".
    /// </summary>
    public sealed class HelpCard : MonoBehaviour
    {
        public Transform Head;
        public Material Panel;
        public Font Font;
        public event Action Closed;
        /// <summary>A row's "Show me": "press", "menu", "move", "jump", "tap".</summary>
        public event Action<string> ShowMe;
        /// <summary>The real tabletop's height (null: none). "Got it" stays at least 3 cm above it.</summary>
        public Func<float?> TableTop;
        public bool Showing { get; private set; }
        public Vector3 Center => root != null ? root.position : Vector3.zero;
        public Vector3 OkButton => ok != null ? ok.transform.position : Center;

        Transform root;
        PokeButton ok;
        MaterialPropertyBlock block;

        /// <summary>(what it says, the demo its "Show me" plays or "" for none, voice only).</summary>
        public static readonly (string text, string demo, bool voice)[] Rows =
        {
            ("PRESS: touch it, or look at it and pinch.", "press", false),
            ("PICK UP: pinch a glowing piece, carry it, let go.", "move", false),
            ("BLOCK: hold your open palm in a glowing ring.", "block", false),
            ("MOVE: palm down; pinch jumps, hold glides.", "jump", false),
            ("WAVE: an open hand, held up: they wave back.", "", false),
            ("HOLD OUT your hand, palm up: her ship lands.", "", false),
            ("MENU: the button on your left wrist.", "menu", false),
            ("TAP your table: the world answers.", "tap", false),
            ("JUNIPER: touch it to see the worlds.", "", false),
            ("SAY \"Juniper\", then a wish: \"make it rain\".", "", true),
            ("STAND UP and your room comes back.", "", false),
        };

        /// <param name="overWorld">In a world, the card is drawn over everything (a tree or a fire never pokes through
        /// it). In your room it is drawn like the rest, so your real hands show in front of it (Nov 17: they vanished
        /// behind it, and no finger reached a button).</param>
        public void Show(bool voice, bool overWorld = true)
        {
            Hide();
            if (Head == null || Font == null) return;
            Showing = true;
            root = new GameObject("Help card").transform;
            root.SetParent(transform, false);
            Vector3 f = Head.forward; f.y = 0f;
            f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            // Within reach, like the map (Nov 17: at 0.58 m out its buttons were up to 0.72 m away, and no hand reached).
            root.position = Head.position + f * 0.44f + Vector3.down * 0.12f;
            root.rotation = Quaternion.LookRotation(root.position - Head.position, Vector3.up);

            var rows = new List<(string text, string demo)>();
            foreach (var r in Rows) if (voice || !r.voice) rows.Add((r.text, r.demo));
            const float rowH = 0.042f, width = 0.47f;
            float top = rows.Count * rowH * 0.5f;
            onTop = overWorld;
            Background(width + 0.03f, rows.Count * rowH + 0.12f, new Vector3(0f, 0.005f, 0.006f));

            var title = Caption.Make("Help title", root, Font, null, Head, 19f, 30f);
            title.Fixed = true;
            title.FaceHead = false;
            title.transform.localPosition = new Vector3(0f, top + 0.034f, 0f);
            title.Say("How it works", 0f, new Color(1f, 0.82f, 0.5f));

            for (int i = 0; i < rows.Count; i++)
            {
                float y = top - rowH * (i + 0.5f);
                var line = Caption.Make("Help row " + i, root, Font, null, Head, 12.5f, 0f);
                line.Fixed = true;
                line.FaceHead = false;
                line.LineChars = 60;
                line.Text.anchor = TextAnchor.MiddleLeft;
                line.Text.alignment = TextAlignment.Left;
                line.transform.localPosition = new Vector3(-width * 0.5f, y, 0f);
                line.Say(rows[i].text, 0f);
                if (rows[i].demo.Length == 0) continue;
                string demo = rows[i].demo;
                var b = PokeButton.Make("help show " + demo, root, Panel, Font, "Show me", new Vector2(0.085f, 0.03f), 11f);
                b.transform.localPosition = new Vector3(width * 0.5f - 0.045f, y, -0.002f);
                OnTop(b);
                b.Pressed += () => { Trace.Info("Help", "show me", "what", demo); ShowMe?.Invoke(demo); };
            }
            ok = PokeButton.Make("help ok", root, Panel, Font, "Got it", new Vector2(0.1f, 0.04f), 14f);
            ok.transform.localPosition = new Vector3(0f, -top - 0.038f, -0.002f);
            ok.Highlight = true;
            OnTop(ok);
            ok.Pressed += () => { Hide(); Closed?.Invoke(); };
            // Clear of a real desk: "Got it" at least 3 cm above its top (Nov 17: a desk 0.35 m below the eyes put it
            // just under the top, where no finger can poke it).
            float? desk = TableTop != null ? TableTop() : null;
            float lift = 0f;
            if (desk.HasValue)
            {
                lift = Mathf.Clamp(desk.Value + 0.03f - (ok.transform.position.y - ok.Size.y * 0.5f), 0f, 0.12f);
                if (lift > 0f) root.position += Vector3.up * lift;
            }
            Trace.Info("Help", "shown", "rows", rows.Count, "voice", voice, "over_world", overWorld, "lifted_cm", Mathf.RoundToInt(lift * 100f));
        }

        static Material backOnTop;
        static Material baseBack;
        static Material backInRoom;
        bool onTop = true;

        /// <summary>A button drawn over the world, like the card's words.</summary>
        void OnTop(PokeButton b)
        {
            if (!onTop) return;   // in the room: drawn like other buttons, behind your hands
            if (b.Back != null && Panel != null) b.Back.sharedMaterial = Caption.OnTopPanel(Panel);
            if (b.Label != null) b.Label.GetComponent<MeshRenderer>().sharedMaterial = Caption.OnTopText(Font);
        }

        void Background(float w, float h, Vector3 at)
        {
            // Behind the card's words and buttons, in front of the world: a fire on your table never pokes through.
            if (Panel != null && (backOnTop == null || baseBack != Panel))
            {
                baseBack = Panel;
                backOnTop = new Material(Panel) { name = "Help card back (on top)", renderQueue = 3990 };
                backOnTop.SetFloat("_ZTest", (float)UnityEngine.Rendering.CompareFunction.Always);
            }
            // In the room: depth-tested (your hands' mask hides it where they are), and drawn before its buttons.
            if (!onTop && Panel != null && (backInRoom == null || baseBack != Panel))
                backInRoom = new Material(Panel) { name = "Help card back (room)", renderQueue = Panel.renderQueue - 10 };
            var back = Util.MeshObject("Help back", root, Util.Quad(Color.white),
                                       !onTop && backInRoom != null ? backInRoom : backOnTop != null ? backOnTop : Panel);
            back.transform.localPosition = at;
            back.transform.localScale = new Vector3(w, h, 1f);
            var r = back.GetComponent<Renderer>();
            block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            block.SetColor("_Color", new Color(0.03f, 0.04f, 0.08f, 1f));
            block.SetVector("_Size", new Vector4(w, h, 0f, 0f));
            block.SetFloat("_Radius", 0.018f);
            r.SetPropertyBlock(block);
        }

        public void Hide()
        {
            if (root != null) Util.Kill(root.gameObject);
            root = null;
            ok = null;
            Showing = false;
        }
    }
}
