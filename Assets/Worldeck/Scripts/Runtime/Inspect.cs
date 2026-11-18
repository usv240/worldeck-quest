using System;
using UnityEngine;
using Worldeck.Core;

namespace Worldeck
{
    /// <summary>
    /// Bring a thing you are holding close to look at it: it lights up as a hologram of itself, a scan sweeps up and
    /// down through it, and a card beside it says what it is, how big it is in life, how big it is here, and one
    /// thing worth knowing. Move it away (or let go) and the card goes. Life size and a flick to throw a thing away
    /// are in Maker.
    /// </summary>
    public sealed class Inspect : MonoBehaviour
    {
        public Maker Maker;
        public Transform Head;
        public Font Font;
        public Material Panel, GlowFlat, Holo;
        [Tooltip("Held this close to the eyes (metres), in front of them, a thing is being looked at.")]
        public float Near = 0.6f;
        [Tooltip("Held close this long (seconds) before the card shows, so passing a thing by your face doesn't.")]
        public float Settle = 0.35f;

        public Maker.Thing Looking { get; private set; }
        /// <summary>"See where it lives" was touched: the thing, and the place it lives.</summary>
        public event Action<Maker.Thing, string> GoHome;
        /// <summary>The button under the card, while there is one (for tests).</summary>
        public PokeButton HomeButton => home;
        /// <summary>The card's last line while the opening teaches it ("Touch See where it lives below."); "" otherwise.</summary>
        [System.NonSerialized] public string FirstHint = "";
        /// <summary>True when the player plays without hands (none seen, or Hands-free on): looking offers the way home.</summary>
        public System.Func<bool> HandsFree;
        Maker.Thing gazeT;
        float gazeFor;
        PokeButton home, ask;
        Maker.Thing homeFor;
        float homeUntil;
        /// <summary>A suggested question was touched: the thing, and the question.</summary>
        public event Action<Maker.Thing, string> Asked;
        /// <summary>The suggested-question button while the card shows (for tests).</summary>
        public PokeButton AskButton => ask;
        public string CardText => card != null ? card.Current : "";
        /// <summary>How many times something was looked at (for tests and the log).</summary>
        public int Looks { get; private set; }

        static readonly Color ScanColor = new Color(1f, 0.78f, 0.45f, 1f) * 0.5f;
        Caption card;
        GameObject scan, trace;
        Maker.Thing candidate;
        float closeFor, awayFor, began;

        void Update()
        {
            if (Maker == null || Head == null) return;
            // Let go of it, and the way home stays beside it a while (for good while the opening teaches it).
            if (Looking == null && home != null)
            {
                if (homeFor == null || homeFor.T == null || !Maker.Things.Contains(homeFor) || Time.unscaledTime > homeUntil)
                { Util.Kill(home.gameObject); home = null; homeFor = null; }
                else PlaceBeside(home, homeFor);
            }
            if (Looking == null && home == null && HandsFree != null && HandsFree()) GazeOffer();
            var t = HeldClose();
            if (t != null && t == candidate) closeFor += Time.unscaledDeltaTime;
            else { candidate = t; closeFor = 0f; }
            if (Looking == null)
            {
                if (candidate != null && closeFor >= Settle) Begin(candidate);
                return;
            }
            bool still = Looking.T != null && Maker.IsHeld(Looking) && Vector3.Distance(Maker.Centre(Looking), Head.position) < Near + 0.2f;
            awayFor = still ? 0f : awayFor + Time.unscaledDeltaTime;
            if (Looking.T == null || awayFor > 0.4f) { End(); return; }
            Follow();
        }

        void OnDisable() { if (Looking != null) End(); }

        /// <summary>
        /// Without hands, an animal can't be lifted to the face: looked at for a moment, it offers "See where it lives"
        /// beside it, pressed by looking (Nov 17: a player agent with no hands stopped at the first animal).
        /// </summary>
        void GazeOffer()
        {
            if (Panel == null || Font == null) return;
            Maker.Thing best = null;
            float bestA = 12f;
            foreach (var th in Maker.Things)
            {
                if (th == null || th.T == null || !LifeSize.IsCreature(th.Kind) || Wildlife.HomeOf(th.Kind).Length == 0) continue;
                Vector3 to = Maker.Centre(th) - Head.position;
                if (to.magnitude > 2.5f) continue;
                float a = Vector3.Angle(Head.forward, to);
                if (a < bestA) { bestA = a; best = th; }
            }
            if (best != gazeT) { gazeT = best; gazeFor = 0f; }
            if (gazeT == null) return;
            gazeFor += Time.unscaledDeltaTime;
            if (gazeFor < 1.2f) return;
            var thing = gazeT;
            string place = Wildlife.HomeOf(thing.Kind);
            gazeT = null;
            home = PokeButton.Make("inspect home", transform, Panel, Font, "See where it lives", new Vector2(0.13f, 0.038f), 13f);
            home.Highlight = true;
            home.Pressed += () => { Trace.Info("Inspect", "see where it lives", "kind", thing.Kind, "place", place, "how", "hands-free"); GoHome?.Invoke(thing, place); };
            homeFor = thing;
            homeUntil = Time.unscaledTime + 60f;
            PlaceBeside(home, thing);
            Trace.Info("Inspect", "offered by looking", "kind", thing.Kind);
        }

        /// <summary>A held thing close to the eyes and in front of them, or null.</summary>
        Maker.Thing HeldClose()
        {
            for (int h = 0; h < 3; h++)
            {
                var t = Maker.HeldBy(h);
                if (t == null || t.T == null) continue;
                Vector3 to = Maker.Centre(t) - Head.position;
                if (to.magnitude > Near || Vector3.Angle(Head.forward, to) > 40f) continue;
                return t;
            }
            return null;
        }

        void Begin(Maker.Thing t)
        {
            Looking = t;
            Maker.Touch(t);
            awayFor = 0f;
            began = Time.unscaledTime;
            Looks++;
            if (card == null)
            {
                card = Caption.Make("Inspect card", transform, Font, Panel, Head, 12f, 22f);
                card.Fixed = true;
                card.LineChars = 30;
            }
            card.Loud = true;
            Caption.Hush = true;   // only the card while you look: Juniper's lines and hints wait
            cardBase = LifeSize.Card(t.Name, t.Kind, t.Size, Maker.Measure(LifeSize.Metres(t.Kind)));
            string place0 = Wildlife.HomeOf(t.Kind);
            bool firstCard = FirstHint.Length > 0 && place0.Length > 0;
            string shown = cardBase;
            if (firstCard) { var ls = cardBase.Split('\n'); if (ls.Length > 2) shown = ls[0] + "\n" + ls[1]; }
            card.Say(shown + "\n" + (FirstHint.Length > 0 && place0.Length > 0 ? FirstHint : "Ask Juniper anything about it."), 0f, CardColor);
            string place = Wildlife.HomeOf(t.Kind);
            if (home != null) { Util.Kill(home.gameObject); home = null; homeFor = null; }
            if (LifeSize.IsCreature(t.Kind) && Panel != null && Font != null && !firstCard)
            {
                string q = QuestionFor(t.Kind);
                var asked = t;
                ask = PokeButton.Make("inspect ask", transform, Panel, Font, q, new Vector2(0.13f, 0.038f), 13f);
                ask.Pressed += () => { Trace.Info("Inspect", "suggested question", "kind", asked.Kind); Asked?.Invoke(asked, q); };
            }
            if (place.Length > 0 && Panel != null && Font != null)
            {
                home = PokeButton.Make("inspect home", transform, Panel, Font, "See where it lives", new Vector2(0.13f, 0.038f), 13f);
                home.Highlight = true;
                var thing = t;
                home.Pressed += () => { Trace.Info("Inspect", "see where it lives", "kind", thing.Kind, "place", place); GoHome?.Invoke(thing, place); };
            }
            // The hologram of it: the same shape again, drawn in light over the model.
            var mf = t.T.GetComponent<MeshFilter>();
            if (Holo != null && mf != null && mf.sharedMesh != null)
            {
                trace = new GameObject("Inspect hologram");
                trace.transform.SetParent(t.T, false);
                trace.transform.localScale = Vector3.one * 1.02f;
                trace.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var r = trace.AddComponent<MeshRenderer>();
                r.sharedMaterial = Holo;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            if (GlowFlat != null) scan = Util.MeshObject("Inspect scan", transform, Ring(56, 0.9f, ScanColor), GlowFlat);
            Follow();
            Sfx.Instance?.At("ui_hover", Maker.Centre(t), 0.55f);
            Hints.Instance?.Learned("inspect");
            Trace.Info("Inspect", "looking", "name", t.Name, "kind", t.Kind, "life_m", LifeSize.Metres(t.Kind), "size_m", t.Size);
        }

        string cardBase = "";
        static readonly Color CardColor = new Color(1f, 0.93f, 0.8f);

        /// <summary>Juniper's answer to a question about the thing you are looking at, on its card. False if you aren't.</summary>
        public bool ShowAnswer(Maker.Thing t, string text)
        {
            if (t == null || Looking != t || card == null || string.IsNullOrEmpty(text)) return false;
            card.Say(cardBase + "\n\n" + text, 0f, CardColor);
            return true;
        }

        void End()
        {
            var was = Looking;
            if (Looking != null) Trace.Info("Inspect", "done", "name", Looking.Name, "seconds", Time.unscaledTime - began);
            Looking = null;
            Caption.Hush = false;
            awayFor = 0f;
            if (card != null) card.Clear();
            if (trace != null) Util.Kill(trace);
            if (scan != null) Util.Kill(scan);
            trace = scan = null;
            if (ask != null) Util.Kill(ask.gameObject);
            ask = null;
            if (home != null && was != null && was.T != null)
            {
                homeFor = was;
                homeUntil = FirstHint.Length > 0 ? float.MaxValue : Time.unscaledTime + 5f;
                PlaceBeside(home, was);
            }
            else
            {
                if (home != null) Util.Kill(home.gameObject);
                home = null;
            }
        }

        /// <summary>A button just beside a thing (on the table, say), facing you.</summary>
        void PlaceBeside(PokeButton b, Maker.Thing t)
        {
            if (Head == null || b == null || t == null || t.T == null) return;   // the scene is closing
            Vector3 right = Head.right; right.y = 0f;
            right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            Vector3 at = Maker.Centre(t) + right * (Mathf.Max(0.06f, t.Size * 0.5f) + 0.09f) + Vector3.up * 0.03f;
            b.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - Head.position, Vector3.up));
        }

        /// <summary>The question an animal's card suggests.</summary>
        public static string QuestionFor(string kind) => Wildlife.HomeOf(kind) == "prehistoric" ? "What did it eat?" : "How fast can it run?";

        void Follow()
        {
            var t = Looking;
            if (t == null || t.T == null || Head == null) return;
            Vector3 c = Maker.Centre(t);
            Bounds b = t.R != null ? t.R.bounds : new Bounds(c, Vector3.one * t.Size);
            float reach = Mathf.Max(b.extents.x, b.extents.z);
            Vector3 right = Head.right; right.y = 0f;
            right = right.sqrMagnitude > 1e-4f ? right.normalized : Vector3.right;
            // The card sits beside it, on the side towards the middle of your view.
            float side = Vector3.Dot(c - Head.position, right) > 0.02f ? -1f : 1f;
            if (card != null)
            {
                // Beside it as you see it: past its edge by a few degrees, never off the side of your view.
                Vector3 to = c - Head.position;
                float dist = Mathf.Max(0.2f, to.magnitude);
                float ang = Mathf.Min(32f, Mathf.Atan2(reach, dist) * Mathf.Rad2Deg + 10f);
                Vector3 dir = Quaternion.AngleAxis(side * ang, Vector3.up) * (to / dist);   // +angle turns right
                card.transform.position = Head.position + dir * Mathf.Max(0.38f, dist) + Vector3.down * 0.02f;
                if (home != null)
                {
                    // Under the card, facing you, within reach of the free hand.
                    Vector3 at = card.transform.position + Vector3.down * 0.1f;
                    home.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - Head.position, Vector3.up));
                }
                if (ask != null)
                {
                    Vector3 at2 = card.transform.position + Vector3.down * (home != null ? 0.15f : 0.1f);
                    ask.transform.SetPositionAndRotation(at2, Quaternion.LookRotation(at2 - Head.position, Vector3.up));
                }
            }
            if (scan != null)
            {
                // Up and down through it, every second and a half.
                float k = Mathf.PingPong((Time.unscaledTime - began) / 0.75f, 1f);
                k = k * k * (3f - 2f * k);
                scan.transform.position = new Vector3(c.x, Mathf.Lerp(b.min.y, b.max.y, k), c.z);
                scan.transform.rotation = Quaternion.identity;
                scan.transform.localScale = Vector3.one * Mathf.Max(0.04f, reach * 1.05f);
            }
        }

        /// <summary>A flat ring of light, brightest at its rim, seen from above or below.</summary>
        static Mesh Ring(int n, float inner, Color c)
        {
            var v = new Vector3[n * 2];
            var col = new Color[n * 2];
            var tri = new int[n * 12];
            var dim = new Color(0f, 0f, 0f, 0f);
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2] = d * inner;
                v[i * 2 + 1] = d;
                col[i * 2] = dim;
                col[i * 2 + 1] = c;
                int j = (i + 1) % n, o = i * 12;
                tri[o] = i * 2; tri[o + 1] = j * 2; tri[o + 2] = i * 2 + 1;
                tri[o + 3] = j * 2; tri[o + 4] = j * 2 + 1; tri[o + 5] = i * 2 + 1;
                tri[o + 6] = i * 2; tri[o + 7] = i * 2 + 1; tri[o + 8] = j * 2;
                tri[o + 9] = j * 2; tri[o + 10] = i * 2 + 1; tri[o + 11] = j * 2 + 1;
            }
            var m = new Mesh { name = "Inspect ring", vertices = v, colors = col, triangles = tri };
            m.RecalculateBounds();
            return m;
        }
    }
}
