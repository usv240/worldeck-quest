using System;
using System.Collections;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Mara's story around the storm (<see cref="Story"/>): she calls, you answer and choose how she flies in, the
    /// storm is her approach (your furniture is her signal), and the ending is hers: landed, landed rough, or
    /// lost (and Rewind brings her back). She appears as a small hologram on your table and speaks in captions
    /// with radio sounds. The Director owns the storm; this owns her, her words and the choices.
    /// </summary>
    public sealed class Mission : MonoBehaviour
    {
        public enum Step { Idle, Calling, Answer, Pace, Storm, Ending, Farewell, Done }

        public Transform Head;
        public Emergency Emergency;
        public Guide Guide;
        public Material Holo, Glow, Panel, World;
        [Tooltip("Glow with no halo falloff, for solid shapes (runway lights, her ship). The halo glow fades them to a tenth.")]
        public Material GlowFlat;
        Material Solid => GlowFlat != null ? GlowFlat : Glow;
        [Tooltip("Mara's figure (Worldeck/Figure with her colour map); none: she is built in code.")]
        public Material Figure;
        public Font Font;

        public Step Now { get; private set; } = Step.Idle;
        public int Chapter { get; private set; }
        public Landing Result { get; private set; }
        public string PaceId { get; private set; } = "";
        /// <summary>How the last chapter went, set before <see cref="Begin"/> (she remembers it).</summary>
        public Landing LastLanding;
        public string LastPace = "", LastHeld = "";
        /// <summary>Set before <see cref="Begin"/>: a piece of your furniture still dark from her last storm ("shelf"),
        /// or lit again by this world's task.</summary>
        public string DarkNow = "", Relit = "";
        /// <summary>At the end of her storm: the keys of the pieces it was aimed at, and of those left dark.</summary>
        public List<string> EndDefended, EndDark;
        bool saidBig;
        /// <summary>A piece that never went dark in this chapter's storm ("sofa"), or "".</summary>
        public string Held { get; private set; } = "";
        /// <summary>The chapter's title card while it shows.</summary>
        public Caption TitleWords { get; private set; }
        public HoloPerson Mara { get; private set; }
        public Caption Words { get; private set; }
        public bool Active => Now != Step.Idle && Now != Step.Done;
        /// <summary>Her approach has begun (storm, landing, goodbye): leaving now would cut her off.</summary>
        public bool Committed => Now == Step.Storm || Now == Step.Ending || Now == Step.Farewell;
        public bool AwaitingChoice => Now == Step.Answer || Now == Step.Pace || Now == Step.Farewell;
        public float Signal { get; private set; } = 1f;
        public string LastLine { get; private set; } = "";

        /// <summary>The player chose how she flies in: start the storm at this pace.</summary>
        public event Action<Pace> Launch;
        /// <summary>The chapter is over (with how it ended and the goodbye chosen).</summary>
        public event Action<int, Landing, string> Finished;
        /// <summary>Her signal was lost (every defended piece broken): Rewind brings her back.</summary>
        public event Action<int> LostHer;
        /// <summary>The player asked her to call later: she has gone quiet, waiting for the button on the table.</summary>
        public event Action<int> Postponed;

        Choice[] offered;
        bool askedWho, saidHalfway, scanned;
        int blocks, hits;
        Transform root;
        AudioSource radio, hiss, voice;
        readonly float[] voiceSamples = new float[256];
        bool voicePaused;
        static AudioClip chirp, noise;
        Coroutine talk;
        static readonly Color Voice = new Color(0.62f, 0.9f, 1f, 1f);
        static float Vol => Sfx.Instance != null ? Sfx.Instance.Master : 1f;

        // ---------- start and stop ----------

        public void Begin(int chapter, RoomModel room)
        {
            Stop();
            Chapter = Mathf.Max(0, chapter);
            scanned = room != null && room.Scanned;
            table = room != null ? room.Table : null;
            WantsPalm = false; LandedInHand = false;
            askedWho = false; saidHalfway = false; blocks = 0; hits = 0; Signal = 1f; Result = Landing.None; PaceId = ""; Held = ""; saidBig = false; EndDefended = null; EndDark = null;
            root = new GameObject("Mission").transform;
            root.SetParent(transform, false);
            root.position = Spot(room);
            Mara = HoloPerson.Make("Mara", root, Holo, Glow, Figure != null ? 0.34f : 0.42f, Figure, Solid);
            Mara.Head = Head;
            Words = Caption.Make("Mara says", root, Font, Panel, Head, 15f, 24f);
            Words.Priority = 9;
            Words.transform.localPosition = Vector3.up * 0.5f;
            MakeSounds();
            if (Emergency != null)
            {
                Emergency.Struck += OnStruck;
                Emergency.Ended += OnEnded;
                Emergency.Rewound += OnRewound;
                Emergency.Incoming += OnIncoming;
            }
            Trace.Info("Mission", "begun", "chapter", Chapter, "title", Story.Title(Chapter), "scanned", scanned);
            Now = Step.Calling;
            talk = StartCoroutine(Call());
        }

        /// <summary>A faint, broken call from your table while you are still building: the story starts here.</summary>
        public void Tease(RoomModel room)
        {
            if (Active) return;
            if (teaseRoot != null) Util.Kill(teaseRoot.gameObject);
            teaseRoot = new GameObject("Faint call").transform;
            teaseRoot.SetParent(transform, false);
            teaseRoot.position = Spot(room);
            var c = Caption.Make("Faint call", teaseRoot, Font, Panel, Head, 13f, 22f);
            c.Priority = 4;
            c.transform.localPosition = Vector3.up * 0.28f;
            c.Say(Story.Faint, 5f, new Color(1f, 0.8f, 0.55f, 1f));
            if (chirp == null) chirp = Chirp();
            if (noise == null) noise = Static(1.2f, "static");
            var a = teaseRoot.gameObject.AddComponent<AudioSource>();
            a.spatialBlend = 0.8f; a.minDistance = 0.4f;
            a.PlayOneShot(noise, 0.5f * Vol);
            a.PlayOneShot(chirp, 0.6f * Vol);
            // Her voice, far away and breaking up.
            var faint = RadioVoice.Make(Story.Faint, 0.25f);
            if (faint != null) { a.clip = faint; a.volume = 0.6f * Vol; a.PlayDelayed(0.5f); }
            Destroy(teaseRoot.gameObject, 6f);
            Trace.Info("Mission", "faint call");
        }

        Transform teaseRoot;
        readonly List<Transform> lights = new List<Transform>();
        public int RunwayLightCount => lights.Count;
        readonly List<LineRenderer> links = new List<LineRenderer>();
        readonly float[] wasBroken = new float[8];

        /// <summary>A warm light on each piece of your furniture the storm is aimed at, linked to her beacon: her runway.</summary>
        void RunwayLights()
        {
            ClearLights();
            if (Emergency == null || root == null || Glow == null) return;
            int count = Emergency.Defended > 0 ? Emergency.Defended : (table != null ? 1 : 0);   // no scan: one light on the table
            for (int k = 0; k < count; k++)
            {
                var mb = new MeshBuilder();
                mb.Ico(new Vector3(0f, 0.6f, 0f), Vector3.one * 0.055f, new Color(1f, 0.82f, 0.5f), null, 0f, 1f);
                mb.Cylinder(Vector3.zero, 0.035f, 0.02f, 0.6f, 10, new Color(1f, 0.7f, 0.38f) * 0.85f, false, false, 1f);
                // Runway edge lights: a row of warm bulbs along the top edges of the piece, so your sofa reads as
                // her runway at a glance (the pillar and its halo mark it from across the room).
                var piece = Emergency.DefendedPiece(k);
                if (piece != null) EdgeLights(mb, piece.Size, piece.Rotation);
                else if (table != null) EdgeLights(mb, new Vector3(0.5f, 0f, 0.35f), Quaternion.identity);
                var l = Util.MeshObject("Runway light " + k, root, mb.ToMesh("runway light"), Solid).transform;
                l.position = Emergency.Defended > 0 ? Emergency.DefendedTop(k) : table.TopCenter + Vector3.ProjectOnPlane(table.TopCenter - root.position, Vector3.up).normalized * 0.15f;
                if (Emergency.Defended > 0) { var p = l.position; p.y = Emergency.SeenTopY(k); l.position = p; }
                var halo = Effects.Halo("Runway glow", l, Glow, 0.26f, new Color(1f, 0.7f, 0.35f) * 0.55f);
                halo.transform.localPosition = Vector3.up * 0.6f;
                lights.Add(l);
                var lr = new GameObject("Link " + k).AddComponent<LineRenderer>();
                lr.transform.SetParent(root, false);
                lr.sharedMaterial = Solid;
                lr.positionCount = 2;
                lr.widthMultiplier = 0.02f;
                lr.startColor = new Color(1f, 0.75f, 0.4f, 0.75f); lr.endColor = new Color(1f, 0.75f, 0.4f, 0.3f);
                lr.useWorldSpace = true;
                links.Add(lr);
            }
            // A piece still dark from her last storm starts with its light out (and no flare for it).
            for (int k = 0; k < wasBroken.Length; k++) wasBroken[k] = Emergency.Defended > 0 ? Emergency.PieceBreak(k) : 0f;
        }

        /// <summary>Bulbs every 12 cm along the edges of a top <paramref name="size"/> wide, turned by <paramref name="turn"/>, with a faint line between them.</summary>
        public static int EdgeLights(MeshBuilder mb, Vector3 size, Quaternion turn)
        {
            float hx = Mathf.Max(0.1f, size.x * 0.5f - 0.02f), hz = Mathf.Max(0.1f, size.z * 0.5f - 0.02f);
            var bulb = new Color(1f, 0.86f, 0.55f) * 0.72f;
            var line = new Color(1f, 0.66f, 0.32f) * 0.7f;
            var corners = new[] { new Vector3(-hx, 0f, -hz), new Vector3(hx, 0f, -hz), new Vector3(hx, 0f, hz), new Vector3(-hx, 0f, hz) };
            int bulbs = 0;
            mb.Push(Vector3.up * 0.035f, turn, Vector3.one);
            for (int e = 0; e < 4; e++)
            {
                Vector3 a = corners[e], b = corners[(e + 1) % 4];
                float len = Vector3.Distance(a, b);
                int n = Mathf.Max(1, Mathf.RoundToInt(len / 0.12f));
                for (int i = 0; i < n; i++)
                {
                    mb.Ico(Vector3.Lerp(a, b, i / (float)n), Vector3.one * 0.017f, bulb, null, 0f, 1f);
                    bulbs++;
                }
                // The strip between the bulbs.
                Vector3 mid = (a + b) * 0.5f;
                bool alongX = Mathf.Abs(b.x - a.x) > Mathf.Abs(b.z - a.z);
                mb.Box(mid, alongX ? new Vector3(len, 0.01f, 0.016f) : new Vector3(0.016f, 0.01f, len), line, null, 1f);
            }
            mb.Pop();
            return bulbs;
        }

        void ClearLights()
        {
            foreach (var l in lights) if (l != null) Util.Kill(l.gameObject);
            foreach (var l in links) if (l != null) Util.Kill(l.gameObject);
            lights.Clear(); links.Clear();
            for (int i = 0; i < wasBroken.Length; i++) wasBroken[i] = 0f;
        }

        void DrawLights()
        {
            if (lights.Count == 0 || Emergency == null) return;
            Vector3 beacon = root != null ? root.position + Vector3.up * 0.02f : Vector3.zero;
            float now = Time.unscaledTime;
            for (int k = 0; k < lights.Count; k++)
            {
                if (lights[k] == null) continue;
                float b = Emergency.Defended > 0 ? Emergency.PieceBreak(k) : 1f - Signal;
                if (k < wasBroken.Length && b >= 0.5f && wasBroken[k] < 0.5f)
                {
                    var flare = Effects.Burst(root, Glow, new Color(1f, 0.6f, 0.3f));
                    flare.transform.position = lights[k].position;
                    flare.Play();
                    Destroy(flare.gameObject, 2f);
                    Sfx.Instance?.At("extinguish", lights[k].position, 1f);
                }
                if (k < wasBroken.Length) wasBroken[k] = b;
                float pulse = 1f + 0.04f * Mathf.Sin(now * 4f + k * 1.3f);
                lights[k].localScale = Vector3.one * Mathf.Max(0.001f, (1f - b) * pulse);
                if (k < links.Count && links[k] != null)
                {
                    links[k].enabled = b < 0.5f && Now == Step.Storm;
                    links[k].SetPosition(0, lights[k].position + Vector3.up * 0.6f);
                    links[k].SetPosition(1, beacon);
                }
            }
        }

        public void Stop()
        {
            ClearLights();
            if (Gift != null) { Util.Kill(Gift.gameObject); Gift = null; }
            if (Emergency != null)
            {
                Emergency.Struck -= OnStruck;
                Emergency.Ended -= OnEnded;
                Emergency.Rewound -= OnRewound;
                Emergency.Incoming -= OnIncoming;
            }
            StopAllCoroutines();
            talk = null;
            WantsPalm = false;
            if (landingShip != null) { Util.Kill(landingShip.gameObject); landingShip = null; }
            ClearButtons();
            offered = null;
            if (root != null) Util.Kill(root.gameObject);
            root = null; Mara = null; Words = null; radio = null; hiss = null; voice = null;
            Now = Step.Idle;
        }

        /// <summary>Somewhere she must not stand (the world's build on your table: the fire).</summary>
        public Vector3? Avoid;
        RoomModel.Piece table;

        /// <summary>On the table, as far from the build as it allows and a little toward you; beside the table if it is too small; in front without one.</summary>
        Vector3 Spot(RoomModel room)
        {
            Vector3 head = Head != null ? Head.position : Vector3.up;
            Vector3 f = Head != null ? Head.forward : Vector3.forward; f.y = 0f;
            f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            var t = room != null ? room.Table : null;
            if (t != null)
            {
                Vector3 toYou = head - t.TopCenter; toYou.y = 0f;
                toYou = toYou.sqrMagnitude > 1e-4f ? toYou.normalized : -f;
                Vector3 side = Vector3.Cross(Vector3.up, -toYou);
                float half = Mathf.Min(t.Size.x, t.Size.z) * 0.5f;
                Vector3 best = Vector3.zero;
                float bestScore = float.MinValue;
                // Candidate spots on the table top, inset 9 cm from the edges.
                for (int ix = -2; ix <= 2; ix++)
                    for (int iz = -2; iz <= 2; iz++)
                    {
                        var local = new Vector3(ix * (t.Size.x * 0.5f - 0.09f) * 0.5f, t.Size.y, iz * (t.Size.z * 0.5f - 0.09f) * 0.5f);
                        Vector3 p = t.ToWorld(local);
                        float clear = Avoid.HasValue ? Vector2.Distance(new Vector2(p.x, p.z), new Vector2(Avoid.Value.x, Avoid.Value.z)) : 1f;
                        Vector3 d = p - head; d.y = 0f;
                        float ahead = Vector3.Dot(d.normalized, f);
                        float score = (clear < 0.3f ? -10f + clear : Mathf.Min(clear, 0.6f) * 2f) + ahead - 2.5f * Mathf.Abs(d.magnitude - 0.9f);
                        // Something of the room (its world skin can be taller than the real piece) between your eyes and her: not here.
                        if (Hidden(room, head, p + Vector3.up * 0.2f)) score -= 6f;
                        if (score > bestScore) { bestScore = score; best = p; }
                    }
                return best;
            }
            return head + f * 0.62f - right * 0.2f + Vector3.down * 0.5f;
        }

        /// <summary>True if a piece of the room (other than her table) stands between the eye and the point.</summary>
        public static bool Hidden(RoomModel room, Vector3 eye, Vector3 at)
        {
            if (room == null) return false;
            foreach (var piece in room.Pieces)
            {
                if (piece == null || piece == room.Table) continue;
                // The piece's box, a little taller and wider: the world's skin of it often is.
                Vector3 a = piece.ToLocal(eye), b = piece.ToLocal(at);
                Vector3 half = new Vector3(piece.Size.x * 0.5f + 0.08f, 0f, piece.Size.z * 0.5f + 0.08f);
                float top = piece.Size.y * 1.35f + 0.1f;
                if (SegmentHitsBox(a, b, new Vector3(-half.x, -0.05f, -half.z), new Vector3(half.x, top, half.z))) return true;
            }
            return false;
        }

        static bool SegmentHitsBox(Vector3 a, Vector3 b, Vector3 min, Vector3 max)
        {
            float t0 = 0f, t1 = 1f;
            Vector3 d = b - a;
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(d[i]) < 1e-6f) { if (a[i] < min[i] || a[i] > max[i]) return false; continue; }
                float inv = 1f / d[i], u = (min[i] - a[i]) * inv, v = (max[i] - a[i]) * inv;
                if (u > v) { float x = u; u = v; v = x; }
                t0 = Mathf.Max(t0, u); t1 = Mathf.Min(t1, v);
                if (t0 > t1) return false;
            }
            return true;
        }

        // ---------- talking ----------

        /// <summary>A line from her: the caption, and her voice saying <paramref name="spoken"/> (the same words
        /// without the static letters). Returns how long she talks.</summary>
        float Say(string text, float seconds, string spoken = null)
        {
            LastLine = text;
            float talk = 0f;
            AudioClip clip = null;
            if (voice != null)
            {
                clip = RadioVoice.Make(spoken ?? text, Now == Step.Storm ? Signal : 1f);
                talk = clip != null ? clip.length + 0.14f : 0f;
            }
            if (Words != null) Words.Say(Story.Pilot.ToUpperInvariant() + "\n" + Story.Fit(text), seconds > 0f ? Mathf.Max(seconds, talk + 0.6f) : 0f, Voice);
            if (Mara != null) Mara.Talking = true;
            if (radio != null && chirp != null) radio.PlayOneShot(chirp, 0.45f * Vol);
            if (voice != null)
            {
                voice.Stop();
                voice.clip = clip;
                voice.volume = 1f;
                voice.PlayDelayed(0.14f);
            }
            Trace.Info("Mission", "line", "chapter", Chapter, "step", Now.ToString(), "voice", RadioVoice.HasRecording(spoken ?? text) ? "recorded" : "murmur");
            return talk;
        }

        IEnumerator Lines(Line[] lines)
        {
            foreach (var l in lines)
            {
                float talk = Say(l.Text, l.Seconds + 0.5f);
                yield return new WaitForSeconds(Mathf.Max(Mathf.Max(l.Seconds, talk + 0.35f), Caption.ReadSeconds(l.Text)));
            }
            if (Mara != null) Mara.Talking = false;
        }

        readonly List<PokeButton> buttons = new List<PokeButton>();
        Transform buttonRow;

        void Offer(Step step, Choice[] choices)
        {
            Now = step;
            offered = choices;
            // Her question stays up while you choose.
            if (Words != null && LastLine.Length > 0) Words.Say(Story.Pilot.ToUpperInvariant() + "\n" + Story.Fit(LastLine), 0f, Voice);
            ClearButtons();
            if (Head == null || Panel == null || Font == null) return;
            buttonRow = new GameObject("Mara's choices").transform;
            buttonRow.SetParent(transform, false);
            Vector3 f = Head.forward; f.y = 0f; f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            // Toward her (so she and the choices are seen together), but never far from where you are looking.
            if (root != null)
            {
                Vector3 toHer = root.position - Head.position; toHer.y = 0f;
                if (toHer.sqrMagnitude > 1e-4f) f = Vector3.RotateTowards(f, toHer.normalized, 25f * Mathf.Deg2Rad, 0f);
            }
            // Within easy reach of a seated arm, a little below the eyes, facing you.
            // Low and near, so they sit below her in view rather than over her.
            buttonRow.position = Head.position + f * 0.36f + Vector3.down * 0.3f;
            buttonRow.rotation = Quaternion.LookRotation(buttonRow.position - Head.position, Vector3.up);
            float w = 0.15f, h = 0.06f, gap = 0.02f;
            for (int i = 0; i < choices.Length; i++)
            {
                var c = choices[i];
                var b = PokeButton.Make("Choice " + c.Id, buttonRow, Panel, Font, c.Label, new Vector2(w, h), 18f);
                b.transform.localPosition = new Vector3((i - (choices.Length - 1) * 0.5f) * (w + gap), 0f, 0f);
                b.Highlight = i == 0;
                string id = c.Id;
                b.Pressed += () => Pick(id);
                buttons.Add(b);
            }
        }

        void ClearButtons()
        {
            if (buttonRow != null) Util.Kill(buttonRow.gameObject);
            buttonRow = null;
            buttons.Clear();
        }

        IEnumerator Call()
        {
            // The chapter's title first, over where she will appear: a judge sees there are three, and which this is.
            if (root != null && Font != null)
            {
                TitleWords = Caption.Make("Chapter title", root, Font, Panel, Head, 14f, 22f);
                TitleWords.Priority = 10;
                TitleWords.transform.localPosition = Vector3.up * 0.62f;
                TitleWords.Say(Story.TitleCard(Chapter), 3f, new Color(1f, 0.86f, 0.6f, 1f));
                Trace.Info("Mission", "title card", "chapter", Chapter, "title", Story.Title(Chapter));
            }
            if (radio != null && chirp != null) radio.PlayOneShot(chirp, 0.7f * Vol);
            yield return new WaitForSeconds(2.6f);   // the title has been read before she speaks
            Mara?.Appear();
            yield return new WaitForSeconds(0.9f);
            // And gone: her first words never share the space with it (it stays until it has been read).
            float titleUntil = Time.time + 3f;
            while (TitleWords != null && TitleWords.Showing && Time.time < titleUntil) yield return null;
            if (TitleWords != null && TitleWords.Showing) TitleWords.Clear();
            // She notices your furniture: lit again by what you gathered, or still dark (that takes the place of
            // remembering the piece that held, to keep her call short).
            var call = new List<Line>(Story.Call(Chapter, LastLanding, Relit.Length > 0 || DarkNow.Length > 0 ? "" : LastHeld));
            if (Relit.Length > 0) call.Insert(Mathf.Min(1, call.Count), new Line(Story.Relit(Relit)));
            else if (DarkNow.Length > 0) call.Insert(Mathf.Min(1, call.Count), new Line(Story.StillDark(DarkNow)));
            yield return Lines(call.ToArray());
            Offer(Step.Answer, Story.Answer(Chapter, askedWho));
        }

        IEnumerator AskPace()
        {
            var l = Story.AskPace(Chapter, LastPace);
            Say(l.Text, 0f);
            yield return new WaitForSeconds(0.3f);
            Offer(Step.Pace, Story.Paces(Chapter));
        }

        /// <summary>A choice, touched or said.</summary>
        public void Pick(string id)
        {
            if (!AwaitingChoice || offered == null || Array.FindIndex(offered, c => c.Id == id) < 0) return;
            ClearButtons();
            offered = null;
            Sfx.Instance?.Ui("ui_select", 0.7f);
            Trace.Info("Mission", "chose", "chapter", Chapter, "id", id);
            if (talk != null) StopCoroutine(talk);
            switch (Now)
            {
                case Step.Answer:
                    if (id == "m:later")
                    {
                        int ch = Chapter;
                        Now = Step.Calling;
                        talk = StartCoroutine(Then(Lines(new[] { Story.LaterLine }), () => { Mara?.Vanish(); StartCoroutine(Quiet(ch)); }));
                        break;
                    }
                    if (id == "m:who") { askedWho = true; Now = Step.Calling; talk = StartCoroutine(Then(Lines(Story.Who()), () => Offer(Step.Answer, Story.Answer(Chapter, true)))); }
                    else { Now = Step.Calling; talk = StartCoroutine(AskPace()); }
                    break;
                case Step.Pace:
                    PaceId = id;
                    Now = Step.Storm;
                    Say(Story.Go(id, scanned && Emergency != null).Text, 5f);
                    Launch?.Invoke(Story.PaceFor(id));
                    RunwayLights();
                    break;
                case Step.Farewell:
                    Now = Step.Ending;
                    var r = Story.Reply(Chapter, id);
                    if (id == "m:wave") Mara?.Wave(3f);
                    talk = StartCoroutine(Then(Lines(new[] { r }), () => StartCoroutine(Leave(id))));
                    break;
            }
        }

        /// <summary>The player's words, if they answer one of the offered choices ("slow", "let's go fast").</summary>
        public bool TryWords(string said)
        {
            if (!AwaitingChoice) return false;
            string id = Story.FromWords(said, offered);
            if (id == null) return false;
            Pick(id);
            return true;
        }

        /// <summary>Whether the words answer an offered choice (nothing is picked).</summary>
        public bool TryWordsProbe(string said) => AwaitingChoice && Story.FromWords(said, offered) != null;

        /// <summary>The player waved: during the goodbye that is the wave choice; otherwise she waves back.</summary>
        public void Waved()
        {
            if (Now == Step.Farewell) { Pick("m:wave"); return; }
            if (Active && Mara != null) Mara.Wave(2f);
        }

        IEnumerator Quiet(int ch)
        {
            yield return new WaitForSeconds(1f);
            Stop();
            Trace.Info("Mission", "postponed", "chapter", ch);
            Postponed?.Invoke(ch);
        }

        IEnumerator Then(IEnumerator first, Action after)
        {
            yield return first;
            after?.Invoke();
        }

        /// <summary>The keepsake she left on your table this visit (it stays until you leave the world).</summary>
        public Transform Gift { get; private set; }

        IEnumerator Leave(string farewell)
        {
            yield return new WaitForSeconds(1.0f);
            if ((Result == Landing.Clean || Result == Landing.Rough) && Chapter < Story.Chapters && root != null)
            {
                var l = Story.KeepsakeLine(Chapter);
                Say(l.Text, l.Seconds + 0.5f);
                if (Gift != null) Util.Kill(Gift.gameObject);
                Gift = Util.MeshObject("Mara's " + Story.Keepsake(Chapter), transform, Souvenirs.Keepsake(Chapter), World != null ? World : Glow).transform;
                Vector3 at = root.position + (Head != null ? Vector3.ProjectOnPlane(Head.position - root.position, Vector3.up).normalized * 0.1f : Vector3.zero);
                if (table != null)
                {
                    // Always on your table: the nearest point of its top to where she stood, 6 cm in from the edge.
                    var onTop = table.ToLocal(at);
                    onTop.x = Mathf.Clamp(onTop.x, -table.Size.x * 0.5f + 0.06f, table.Size.x * 0.5f - 0.06f);
                    onTop.z = Mathf.Clamp(onTop.z, -table.Size.z * 0.5f + 0.06f, table.Size.z * 0.5f - 0.06f);
                    onTop.y = table.Size.y;
                    at = table.ToWorld(onTop);
                    if (Avoid.HasValue && Vector2.Distance(new Vector2(at.x, at.z), new Vector2(Avoid.Value.x, Avoid.Value.z)) < 0.15f)
                        at += Vector3.ProjectOnPlane(at - Avoid.Value, Vector3.up).normalized * 0.12f;
                }
                Gift.position = at + Vector3.up * 0.002f;
                Gift.localScale = Vector3.one * 1.3f;
                var burst = Effects.Burst(transform, Glow, new Color(1f, 0.85f, 0.55f));
                burst.transform.position = Gift.position + Vector3.up * 0.03f;
                burst.Play();
                Destroy(burst.gameObject, 2f);
                Sfx.Instance?.At("souvenir_chime", Gift.position, 0.9f);
                yield return new WaitForSeconds(l.Seconds);
                // The question the story keeps open (the last chapter answers it).
                yield return Lines(Story.Hook(Chapter));
            }
            Mara?.Wave(2f);
            yield return new WaitForSeconds(1.8f);
            Mara?.Vanish();
            if (Words != null) Words.Clear();
            if (radio != null && chirp != null) radio.PlayOneShot(chirp, 0.4f * Vol);
            Now = Step.Done;
            Trace.Info("Mission", "finished", "chapter", Chapter, "landing", Result.ToString(), "farewell", farewell, "pace", PaceId);
            Finished?.Invoke(Chapter, Result, farewell);
            yield return new WaitForSecondsRealtime(1.5f);
            if (root != null && Now == Step.Done) { Util.Kill(root.gameObject); root = null; Mara = null; Words = null; }
        }

        // ---------- the storm, as her approach ----------

        void OnStruck(Strike s, StrikeResult r)
        {
            if (Now != Step.Storm || Emergency == null || Emergency.Sim == null) return;
            UpdateSignal();
            var st = Emergency.Sim.State;
            if (r == StrikeResult.Hit)
            {
                Mara?.Glitch(0.9f);
                hits++;
                if (hits == 1 && Emergency.Defended > 0) Say(Story.FirstBreak(Emergency.PieceLabel(s)), 4.2f);
                else Say(Story.Hurt(Emergency.PieceLabel(s), Signal), 3.2f, Story.HurtSpoken(Emergency.PieceLabel(s), Signal));
                if (hiss != null && noise != null) hiss.PlayOneShot(noise, 0.5f * Vol);
                return;
            }
            blocks++;
            if (!saidHalfway && st.Next >= Emergency.Sim.Strikes.Count / 2) { saidHalfway = true; Say(Story.Halfway(Chapter), 2.6f); return; }
            if (blocks % 2 == 1) Say(Story.Cheer(blocks / 2 + Chapter), 1.8f);
        }

        /// <summary>A ring has appeared: she calls out the big one.</summary>
        void OnIncoming(int i, int kind)
        {
            if (Now != Step.Storm || kind != Sim.Big || saidBig) return;
            saidBig = true;
            Say(Emergency != null && Emergency.Focused.Length > 0 ? Story.BigAgain(Emergency.Focused) : Story.BigWarning, 3.2f);
        }

        void UpdateSignal()
        {
            if (Emergency == null || Emergency.Sim == null) { Signal = 1f; return; }
            if (Emergency.Defended > 0) Signal = 1f - Emergency.BrokenPieces / (float)Emergency.Defended;
            else Signal = Mathf.Clamp01(1f - Emergency.Sim.State.Taken * 0.18f);
            if (Emergency.RoomBack) Signal = 0f;
            if (Mara != null) Mara.Signal = Mathf.Max(0.05f, Signal);
        }

        void OnEnded(int outcome)
        {
            if (Now != Step.Storm || Emergency == null || Emergency.Sim == null) return;
            UpdateSignal();
            var st = Emergency.Sim.State;
            Result = Story.LandingFor(Emergency.RoomBack, st.Taken);
            Held = Emergency.FirstSafe();
            Emergency.Keys(out EndDefended, out EndDark);
            Trace.Info("Mission", "landing", "chapter", Chapter, "landing", Result.ToString(), "blocked", st.Blocked, "taken", st.Taken, "pace", PaceId);
            if (talk != null) StopCoroutine(talk);
            talk = StartCoroutine(Ending(st.Blocked));
        }

        IEnumerator Ending(int blocked)
        {
            Now = Step.Ending;
            if (Result == Landing.Lost)
            {
                Say(Story.End(Chapter, Result, "", blocked)[0].Text, 3f);
                Mara?.Glitch(1.5f);
                yield return new WaitForSeconds(1.2f);
                Mara?.Vanish();
                if (hiss != null && noise != null) hiss.PlayOneShot(noise, 0.7f * Vol);
                yield return new WaitForSeconds(1.2f);
                Guide?.SetMood(Guide.Mood.Speaking);
                Guide?.Say(Story.LostHint, 0f);
                Now = Step.Storm;   // waiting for Rewind
                Trace.Info("Mission", "lost her", "chapter", Chapter, "pace", PaceId);
                LostHer?.Invoke(Chapter);
                yield break;
            }
            yield return new WaitForSeconds(0.4f);
            yield return Flyover();
            if (Mara != null) Mara.Signal = 1f;
            yield return Lines(Story.End(Chapter, Result, Emergency != null ? Emergency.SafePieces() : "", blocked));
            Offer(Step.Farewell, Story.Farewell(Chapter));
        }

        void OnRewound()
        {
            if (!Active) return;
            if (talk != null) StopCoroutine(talk);
            WantsPalm = false;
            if (landingShip != null) { Util.Kill(landingShip.gameObject); landingShip = null; }
            offered = null;
            ClearButtons();
            Now = Step.Storm;
            Result = Landing.None;
            saidHalfway = false;
            hits = 0;
            if (Mara != null) { Mara.Appear(); Mara.Signal = 1f; }
            UpdateSignal();
            Say(Story.Back, 3.5f);
            Trace.Info("Mission", "rewound", "chapter", Chapter);
        }

        /// <summary>Her ship comes down out of the sky toward your table: a bright point, a streak, a thump.</summary>
        IEnumerator Flyover()
        {
            if (Head == null || root == null || Glow == null) yield break;
            var mb = new MeshBuilder();
            mb.Ico(Vector3.zero, new Vector3(0.05f, 0.03f, 0.09f), new Color(1f, 0.9f, 0.7f), null, 0f, 1f);
            var ship = Util.MeshObject("Mara's ship", root, mb.ToMesh("ship"), Solid).transform;
            landingShip = ship;
            Vector3 f = Head.forward; f.y = 0f; f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            Vector3 from = Head.position + f * 9f + right * 4f + Vector3.up * 5f;
            Vector3 to = Head.position + f * 3.2f - right * 1.5f + Vector3.down * 0.6f;
            Sfx.Instance?.At("reveal_whoosh", Vector3.Lerp(from, to, 0.6f), 0.9f, 1.15f);
            float t0 = Time.time, dur = 2.2f;
            while (Time.time - t0 < dur && ship != null)
            {
                float k = Util.Ease((Time.time - t0) / dur);
                ship.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.6f;
                ship.rotation = Quaternion.LookRotation((to - from).normalized);
                yield return null;
            }
            if (ship != null) yield return Touchdown(ship);
            yield return new WaitForSecondsRealtime(0.5f);
        }

        /// <summary>Asking for your hand: true from her "hold out your hand" until she has landed (the menu waits).</summary>
        public bool WantsPalm { get; private set; }
        /// <summary>Her ship came down in your hand this chapter (for tests and the log).</summary>
        public bool LandedInHand { get; private set; }
        public const float PalmWait = 9f;

        /// <summary>
        /// Her ship comes in to an open hand held palm up (and rides on it a moment); with no hand held out in time, or
        /// no hands seen, it comes down by the table as before. Nothing is lost by not doing it.
        /// </summary>
        Transform landingShip;

        IEnumerator Touchdown(Transform ship)
        {
            landingShip = ship;
            LandedInHand = false;
            var hands = Emergency != null ? Emergency.Hands : null;
            bool anyHand = false;
            if (hands != null) for (int h = 0; h < hands.Count; h++) if (hands[h].Tracked && !hands[h].Controller) anyHand = true;
            if (anyHand)
            {
                WantsPalm = true;
                Say(Story.PalmAsk, 4f);
                Vector3 hover = ship.position;
                float t0 = Time.time, steadySince = -1f;
                int which = -1;
                Vector3 last = Vector3.zero;
                while (Time.time - t0 < PalmWait && ship != null)
                {
                    ship.position = hover + Vector3.up * Mathf.Sin((Time.time - t0) * 2f) * 0.05f;
                    which = OpenPalmUp(hands);
                    if (which >= 0)
                    {
                        Vector3 p = hands[which].Palm;
                        if (steadySince < 0f || (p - last).magnitude > 0.05f) { steadySince = Time.time; last = p; }
                        if (Time.time - steadySince > 0.4f) break;
                    }
                    else steadySince = -1f;
                    yield return null;
                }
                if (which >= 0 && ship != null)
                {
                    Vector3 from = ship.position;
                    float t1 = Time.time, dur = 1.4f;
                    Vector3 dst = hands[which].Palm + hands[which].PalmNormal.normalized * 0.035f;
                    while (Time.time - t1 < dur && ship != null)
                    {
                        var st = hands[which];
                        if (st.Tracked) dst = st.Palm + st.PalmNormal.normalized * 0.035f;   // lost a moment: where it was
                        float k = Util.Ease((Time.time - t1) / dur);
                        ship.position = Vector3.Lerp(from, dst, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.25f;
                        Vector3 dir = dst - from; dir.y = 0f;
                        if (dir.sqrMagnitude > 1e-4f) ship.rotation = Quaternion.LookRotation(dir.normalized);
                        yield return null;
                    }
                    if (ship != null && hands[which].Tracked)
                    {
                        LandedInHand = true;
                        Trace.Info("Mission", "landed in your hand", "chapter", Chapter, "hand", which);
                        var glow = Effects.Burst(root, Glow, new Color(1f, 0.85f, 0.6f));
                        glow.transform.position = ship.position;
                        glow.Play();
                        Destroy(glow.gameObject, 2f);
                        Sfx.Instance?.At("souvenir_chime", ship.position, 0.9f);
                        Say(Story.PalmLanded, 3f);
                        float t2 = Time.time;
                        while (Time.time - t2 < 2.6f && ship != null)
                        {
                            var st = hands[which];
                            if (st.Tracked) ship.position = st.Palm + st.PalmNormal.normalized * 0.035f;
                            ship.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin((Time.time - t2) * 6f));
                            yield return null;
                        }
                    }
                }
                else Trace.Info("Mission", "no hand held out: she lands by the table", "chapter", Chapter);
                WantsPalm = false;
            }
            if (ship != null)
            {
                var burst = Effects.Burst(root, Glow, new Color(1f, 0.85f, 0.6f));
                burst.transform.position = ship.position;
                burst.Play();
                Destroy(burst.gameObject, 2f);
                Sfx.Instance?.At("visitor_land", ship.position, LandedInHand ? 0.5f : 1f);
                Util.Kill(ship.gameObject);
            }
        }

        /// <summary>A hand held open, palm up (not a fist, not pinching, not a controller), or -1.</summary>
        static int OpenPalmUp(HandInput hands)
        {
            for (int h = 0; h < hands.Count; h++)
            {
                var s = hands[h];
                // Any open hand with its palm to the ceiling. Not s.PalmUp alone: a palm turned toward the face is Quest's
                // system gesture, and while it lasts PalmUp is held false (the worn retake: 3 landings, none in the hand).
                bool up = s.PalmUp || Vector3.Dot(s.PalmNormal, Vector3.up) > 0.55f;
                if (s.Tracked && !s.Controller && up && !s.Fist && !s.Pinching) return h;
            }
            return -1;
        }

        void Update()
        {
            if (Now == Step.Storm) UpdateSignal();
            DrawLights();
            if (Mara != null && voice != null)
            {
                float v = 0f;
                if (voice.isPlaying)
                {
                    voice.GetOutputData(voiceSamples, 0);
                    float sum = 0f;
                    foreach (var x in voiceSamples) sum += x * x;
                    v = Mathf.Sqrt(sum / voiceSamples.Length) * 4f;
                }
                Mara.Voice = v;
            }
            if (voice != null)
            {
                bool frozen = Time.timeScale <= 0.001f;
                if (frozen && voice.isPlaying) { voice.Pause(); voicePaused = true; }
                else if (!frozen && voicePaused) { voice.UnPause(); voicePaused = false; }
            }
            if (hiss != null) hiss.volume = Now == Step.Storm ? Mathf.Clamp01((1f - Signal) * 0.25f) * Vol : 0f;
            if (Mara != null && Words != null && !Words.Showing) Mara.Talking = false;
        }

        // ---------- radio sounds, made in code ----------

        void MakeSounds()
        {
            if (chirp == null) chirp = Chirp();
            if (noise == null) noise = Static(1.2f, "static");
            radio = root.gameObject.AddComponent<AudioSource>();
            radio.spatialBlend = 0.8f; radio.playOnAwake = false; radio.minDistance = 0.4f;
            voice = root.gameObject.AddComponent<AudioSource>();
            voice.spatialBlend = 0.45f; voice.playOnAwake = false; voice.minDistance = 2.5f; voice.maxDistance = 20f;
            voice.rolloffMode = AudioRolloffMode.Logarithmic; voice.priority = 0;
            hiss = root.gameObject.AddComponent<AudioSource>();
            hiss.spatialBlend = 0.8f; hiss.playOnAwake = false; hiss.loop = true; hiss.clip = Static(2f, "hiss"); hiss.volume = 0f; hiss.Play();
        }

        static AudioClip Chirp()
        {
            const int rate = 22050;
            int n = (int)(rate * 0.32f);
            var d = new float[n];
            var rng = new Rng(7);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float tone = t < 0.09f ? Mathf.Sin(2f * Mathf.PI * 1320f * t) : t < 0.18f ? Mathf.Sin(2f * Mathf.PI * 990f * t) : 0f;
                float crackle = (rng.Next01() * 2f - 1f) * Mathf.Exp(-t * 14f) * 0.5f;
                d[i] = (tone * 0.35f + crackle * 0.4f) * Mathf.Clamp01((0.32f - t) * 20f);
            }
            var c = AudioClip.Create("radio chirp", n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }

        static AudioClip Static(float seconds, string name)
        {
            const int rate = 22050;
            int n = (int)(rate * seconds);
            var d = new float[n];
            var rng = new Rng(name.Length * 13);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = rng.Next01() * 2f - 1f;
                lp += (w - lp) * 0.35f;
                float edge = Mathf.Clamp01(Mathf.Min(i, n - i) / (rate * 0.05f));
                d[i] = lp * 0.5f * edge;
            }
            var c = AudioClip.Create(name, n, 1, rate, false);
            c.SetData(d, 0);
            return c;
        }
    }
}
