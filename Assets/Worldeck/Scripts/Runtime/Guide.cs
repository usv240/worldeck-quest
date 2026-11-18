using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Juniper, the guide: a small warm light with a face, above your table. It is the room listening, and a
    /// little character: it blinks, watches your nearest hand, squashes and giggles when poked, waves back when
    /// you wave, "says" its captions in chirps (no voice acting), and flies to a piece when you seem stuck.
    /// Touch it (or look at it and pinch) to talk. It swells with your voice while listening and circles while
    /// it thinks. It is the visible face of the AI, and works without it (choices instead of speech).
    /// </summary>
    public sealed class Guide : MonoBehaviour
    {
        public enum Mood { Idle, Listening, Thinking, Speaking, Hidden }

        public Transform Head;
        public HandInput Hands;
        public Material World, Glow, Panel;
        public Font Font;
        public PokeButton Button { get; private set; }
        public Caption Words { get; private set; }
        public Mood State { get; private set; } = Mood.Idle;
        /// <summary>0..1 microphone level while listening.</summary>
        public float Level;
        public event Action Tapped;
        GameObject ear;
        /// <summary>On-device voice is listening for "Juniper": a small blue light beside it, always visible while on.</summary>
        public void SetEars(bool on) { if (ear != null && ear.activeSelf != on) ear.SetActive(on); }
        public event Action<string> Chose;
        public event Action Waved;
        public int Waves { get; private set; }
        public int Pokes { get; private set; }

        GameObject core, halo, ring;
        Transform body, face, eyeL, eyeR;
        readonly List<GameObject> motes = new List<GameObject>();
        readonly List<PokeButton> chips = new List<PokeButton>();
        Transform chipRow;
        Vector3 home, flyTarget;
        float flyUntil = -1f;
        float breathe, squash, wiggle, nextBlink, blinkUntil;
        Color tint = new Color(1f, 0.75f, 0.45f);
        Coroutine chirping;

        public void Make()
        {
            body = new GameObject("Body").transform;
            body.SetParent(transform, false);
            var mb = new MeshBuilder();
            mb.Ico(Vector3.zero, Vector3.one * 0.026f, new Color(1f, 0.86f, 0.62f), new Worldeck.Core.Rng(3), 0.06f, 1f);
            core = Util.MeshObject("Core", body, mb.ToMesh("ember"), World);
            face = new GameObject("Face").transform;
            face.SetParent(body, false);
            eyeL = Eye("Eye L", -0.0095f);
            eyeR = Eye("Eye R", 0.0095f);
            halo = Effects.Halo("Halo", transform, Glow, 0.13f, tint * 0.7f);
            ring = Effects.Halo("Ring", transform, Glow, 0.18f, new Color(0.6f, 0.85f, 1f) * 0.5f);
            ear = Effects.Halo("Ear", transform, Glow, 0.022f, new Color(0.55f, 0.8f, 1f) * 0.9f);
            ear.transform.localPosition = new Vector3(0.034f, 0.03f, 0f);
            ear.SetActive(false);
            ring.SetActive(false);
            for (int i = 0; i < 3; i++)
            {
                var m = Effects.Halo("Mote " + i, transform, Glow, 0.025f, tint);
                m.SetActive(false);
                motes.Add(m);
            }
            Button = PokeButton.Make("Guide", transform, Panel, Font, "", new Vector2(0.09f, 0.09f));
            Button.Back.enabled = false;
            Button.Pressed += () =>
            {
                Pokes++;
                squash = 1f;
                Sfx.Instance?.At("ember_giggle", transform.position, 0.8f);
                Tapped?.Invoke();
            };
            Words = Caption.Make("Guide words", transform, Font, Panel, Head, 17f, 26f);
            Words.Priority = 7;
            Words.transform.localPosition = new Vector3(0f, 0.095f, 0f);
            chipRow = new GameObject("Choices").transform;
            chipRow.SetParent(transform, false);
            chipRow.localPosition = new Vector3(0f, -0.08f, 0f);
            nextBlink = Time.unscaledTime + 2f;
        }

        Transform Eye(string name, float x)
        {
            var mb = new MeshBuilder();
            mb.Ico(Vector3.zero, new Vector3(0.0042f, 0.006f, 0.003f), new Color(0.12f, 0.08f, 0.1f), null, 0f, 0f);
            var go = Util.MeshObject(name, face, mb.ToMesh("eye"), World);
            go.transform.localPosition = new Vector3(x, 0.004f, 0.0245f);
            return go.transform;
        }

        /// <summary>Puts the guide above the back of the table (or in front when there is no table).</summary>
        public void Place(Vector3 at)
        {
            spotMode = 0;   // choices on show find their place again
            home = at;
            transform.position = at;
            flyUntil = -1f;
        }

        /// <summary>Flies over to <paramref name="target"/> for a moment (a hint), then home.</summary>
        public void Point(Vector3 target, float seconds = 2.2f)
        {
            flyTarget = target + Vector3.up * 0.1f;
            flyUntil = Time.unscaledTime + seconds;
        }

        public void SetMood(Mood m)
        {
            if (m != State && Trace.Every("guide-mood-" + m, 0.5f)) Trace.Info("Guide", "mood", "to", m.ToString());
            State = m;
            bool on = m != Mood.Hidden;
            body.gameObject.SetActive(on);
            halo.SetActive(on);
            Button.gameObject.SetActive(on);
            ring.SetActive(m == Mood.Listening);
            foreach (var x in motes) x.SetActive(m == Mood.Thinking);
            if (!on) { Words.Clear(); ShowChoices(null, null); }
        }

        /// <summary>Shows a line and "says" it in chirps, one per syllable.</summary>
        public void Say(string line, float seconds = 5f)
        {
            Words.Say(line, seconds);
            if (chirping != null) StopCoroutine(chirping);
            if (State != Mood.Hidden && !string.IsNullOrEmpty(line) && isActiveAndEnabled) chirping = StartCoroutine(Chirp(line));
        }

        IEnumerator Chirp(string line)
        {
            int n = Mathf.Clamp(Syllables(line), 1, 10);
            var rng = new Worldeck.Core.Rng(line.GetHashCode());
            bool question = line.TrimEnd().EndsWith("?");
            for (int i = 0; i < n; i++)
            {
                float pitch = 1f + 0.12f * Mathf.Sin(i * 1.7f) + (question && i == n - 1 ? 0.18f : 0f);
                Sfx.Instance?.At("ember_chirp_" + (1 + rng.Range(0, 6)), transform.position, 0.45f, pitch);
                squash = Mathf.Max(squash, 0.25f);
                yield return new WaitForSecondsRealtime(rng.Range(0.1f, 0.15f));
            }
            chirping = null;
        }

        public static int Syllables(string s)
        {
            int n = 0;
            bool inVowel = false;
            foreach (char ch in s.ToLowerInvariant())
            {
                bool v = "aeiouy".IndexOf(ch) >= 0;
                if (v && !inVowel) n++;
                inVowel = v;
            }
            return n;
        }

        /// <summary>Further than this from you (on the flat), the guide's choices come to you instead.</summary>
        public const float ChoiceReach = 0.6f;
        /// <summary>The row of choices under Juniper is never further from you than this (on the flat).</summary>
        public const float RowReach = 0.44f;
        /// <summary>How far from you (on the flat) Juniper may be placed.</summary>
        public const float MaxReach = 0.52f;
        /// <summary>Where choices go when the guide is out of reach: over the table's near edge (null: no table by you).</summary>
        public Vector3? ChoicesAt;
        /// <summary>True while a fingertip must not press a choice (a hand is carrying something past them).</summary>
        public Func<bool> ChoicesBlocked;
        /// <summary>A choice does not take a fingertip for this long after it appears (a hand was already on its way somewhere).</summary>
        public const float ChoiceArm = 0.5f;
        Vector3? nearAt, toward;
        Vector3 nearFrom;
        int spotMode;   // 0 not chosen yet, 1 under the guide, 2 and 3 a fixed place (nearAt)

        /// <summary>Where the row of choices is: under the guide, or within your reach when the guide is not.</summary>
        public Vector3 ChoiceSpot()
        {
            Vector3 own = transform.position + Vector3.down * 0.095f;
            if (Head == null || chips.Count == 0) return own;
            // Chosen once when the choices appear, and again only when you have moved: a row that hopped between two
            // places as your head swayed would dodge the finger coming for it.
            // Under Juniper, but within an easy reach: she may be half a metre off, the row comes a little nearer.
            Vector3 flat = own - Head.position; flat.y = 0f;
            if (flat.magnitude > RowReach) own -= flat.normalized * (flat.magnitude - RowReach);
            if (spotMode == 0 || Vector3.Distance(nearFrom, Head.position) > 0.3f)
            {
                nearFrom = Head.position;
                Vector3 edge = ChoicesAt.HasValue ? ChoicesAt.Value - Head.position : Vector3.zero; edge.y = 0f;
                if (toward.HasValue)
                {
                    // About someone out there: on the line towards them, a little below your eyes, so they are in view.
                    Vector3 t = toward.Value - Head.position; t.y = 0f;
                    spotMode = 3;
                    nearAt = Head.position + (t.sqrMagnitude > 1e-4f ? t.normalized : flat.normalized) * 0.42f + Vector3.down * 0.26f;
                }
                else if (flat.magnitude <= ChoiceReach) spotMode = 1;
                else if (ChoicesAt.HasValue && edge.magnitude <= ChoiceReach) { spotMode = 2; nearAt = ChoicesAt.Value; }
                else { spotMode = 3; nearAt = Head.position + flat.normalized * 0.42f + Vector3.down * 0.26f; }
                // Never down in the table.
                if (spotMode == 3 && ChoicesAt.HasValue && edge.magnitude < 1.2f)
                    nearAt = new Vector3(nearAt.Value.x, Mathf.Max(nearAt.Value.y, ChoicesAt.Value.y - 0.1f), nearAt.Value.z);
            }
            return spotMode == 1 ? own : nearAt.Value;
        }

        /// <summary>A choice you cannot reach is no choice: the row is under the guide, or comes to you (also while the guide is hidden).</summary>
        void PlaceChoices()
        {
            if (Head == null || chipRow == null || chips.Count == 0) return;
            Vector3 at = ChoiceSpot();
            // In a fixed place the row also keeps its facing (from where you were when it was placed): turning to
            // follow a swaying head swings the outer buttons.
            Vector3 to = at - (spotMode == 1 ? Head.position : nearFrom);
            chipRow.position = at;
            if (to.sqrMagnitude > 1e-4f) chipRow.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        }

        /// <summary>Shows up to three touchable choices: label shown, id returned through <see cref="Chose"/>.</summary>
        public void ShowChoices(string[] labels, string[] ids, Vector3? towards = null)
        {
            foreach (var c in chips) if (c != null) Util.Kill(c.gameObject);
            chips.Clear();
            nearAt = null;
            spotMode = 0;
            toward = towards;
            if (labels == null || labels.Length == 0) return;
            // Up to eight choices: rows of up to four, centred (buttons 40 mm+ apart: Meta's hand UI sizes).
            int n = Mathf.Min(8, labels.Length);
            int perRow = n <= 3 ? n : n <= 4 ? 4 : n <= 6 ? 3 : 4;
            // One, two or three choices are the big ones (the first thing a new player must touch): 16 x 7 cm, 14 x 6.5 for three.
            float w = n > 3 ? 0.075f : n == 3 ? 0.14f : 0.16f, h = n > 3 ? 0.05f : n == 3 ? 0.065f : 0.07f, gap = 0.012f;
            float size = n > 3 ? 14f : n == 3 ? 15f : 16f;
            for (int i = 0; i < n; i++)
            {
                int row = i / perRow, col = i % perRow;
                int inRow = Mathf.Min(perRow, n - row * perRow);
                float start = -(inRow - 1) * (w + gap) * 0.5f;
                string id = ids != null && i < ids.Length ? ids[i] : labels[i];
                var b = PokeButton.Make("Choice " + id, chipRow, Panel, Font, labels[i], new Vector2(w, h), size);
                b.transform.localPosition = new Vector3(start + col * (w + gap), -row * (h + gap), 0f);
                b.ArmedAt = Time.unscaledTime + ChoiceArm;
                b.HoldPress = id != "new:yes";   // clearing a player's progress takes a real press
                b.Blocked = () => ChoicesBlocked != null && ChoicesBlocked();
                b.Pressed += () => { ShowChoices(null, null); Chose?.Invoke(id); };
                chips.Add(b);
            }
        }

        public bool HasChoices => chips.Count > 0;
        /// <summary>The choices on show (empty when none).</summary>
        public IReadOnlyList<PokeButton> ChoiceButtons => chips;

        // ---------- waving ----------

        readonly float[] lastX = new float[3];
        readonly int[] lastDir = new int[3];
        readonly float[] turnStart = new float[3];
        readonly int[] turns = new int[3];
        float lastWave = -10f;

        /// <summary>A hand moving side to side (three turns, each ≥ 5 cm, within 1.4 s), in view, open: a wave.</summary>
        void DetectWave()
        {
            if (Hands == null || Head == null) return;
            for (int h = 0; h < Hands.Count; h++)
            {
                var s = Hands[h];
                // A wave is an open hand, held up, palm towards whoever it is for: a finger poking a lantern on the
                // table also went side to side, and was answered with a wave (seen on the headset).
                // Open: the fingers out straight (a pointing finger has the others curled in). Palm out or sideways,
                // never down: people wave goodbye either way, and nobody was told which.
                bool open = s.Controller || s.Curl <= 0f || s.Curl > 1.45f;   // a relaxed open hand is about 1.6: 1.6 itself was too strict
                if (!s.Tracked || s.Pinching || s.Grip || !open) { turns[h] = 0; continue; }
                Vector3 local = Head.InverseTransformPoint(s.Palm);
                Vector3 away = s.Palm - Head.position; away.y = 0f;
                bool palmOut = s.Controller || (away.sqrMagnitude > 1e-4f && Vector3.Dot(s.PalmNormal, away.normalized) > -0.1f && s.PalmNormal.y > -0.6f);
                if (local.z < 0.15f || local.y < -0.3f || !palmOut) { turns[h] = 0; continue; }
                float x = local.x, d = x - lastX[h];
                int dir = Mathf.Abs(d) < 0.004f ? 0 : (d > 0 ? 1 : -1);
                if (dir != 0 && dir != lastDir[h])
                {
                    if (lastDir[h] != 0 && Mathf.Abs(x - turnStartX[h]) > 0.05f)
                    {
                        if (turns[h] == 0) turnStart[h] = Time.unscaledTime;
                        turns[h]++;
                    }
                    turnStartX[h] = x;
                    lastDir[h] = dir;
                }
                if (turns[h] > 0 && Time.unscaledTime - turnStart[h] > 1.4f) turns[h] = 0;
                if (turns[h] >= 3 && Time.unscaledTime - lastWave > 6f)
                {
                    turns[h] = 0;
                    lastWave = Time.unscaledTime;
                    Waves++;
                    wiggle = 1f;
                    Trace.Info("Guide", "player waved", "count", Waves);
                    Sfx.Instance?.At("ember_wave", transform.position, 0.8f);
                    Waved?.Invoke();
                }
                lastX[h] = x;
            }
        }

        readonly float[] turnStartX = new float[3];

        // ---------- per frame ----------

        void LateUpdate()
        {
            if (core == null) return;
            // Out of sight, a wave is still a wave (to Mara, or to whoever lives here).
            if (State == Mood.Hidden) { DetectWave(); PlaceChoices(); return; }
            breathe += Time.unscaledDeltaTime;
            float dt = Time.unscaledDeltaTime;
            Vector3 target = home + Vector3.up * (0.008f * Mathf.Sin(breathe * 1.3f));
            if (flyUntil > Time.unscaledTime) target = flyTarget + Vector3.up * (0.01f * Mathf.Sin(breathe * 5f));
            transform.position = Vector3.Lerp(transform.position, target, 1f - Mathf.Exp(-dt * (flyUntil > Time.unscaledTime ? 4f : 6f)));

            // Look at the nearest hand within reach, else at you.
            Vector3 look = Head != null ? Head.position : transform.position + Vector3.back;
            if (Hands != null)
            {
                float best = 0.45f;
                for (int h = 0; h < Hands.Count; h++)
                {
                    var hs = Hands[h];
                    if (!hs.Tracked) continue;
                    float d = Vector3.Distance(hs.IndexTip, transform.position);
                    if (d < best) { best = d; look = hs.IndexTip; }
                }
            }
            Vector3 dir = look - transform.position;
            if (dir.sqrMagnitude > 1e-5f) face.rotation = Quaternion.Slerp(face.rotation, Quaternion.LookRotation(dir.normalized, Vector3.up), 1f - Mathf.Exp(-dt * 10f));

            // Blink every 3–6 s.
            if (Time.unscaledTime > nextBlink) { blinkUntil = Time.unscaledTime + 0.12f; nextBlink = Time.unscaledTime + UnityEngine.Random.Range(3f, 6f); }
            float eyeY = Time.unscaledTime < blinkUntil ? 0.15f : 1f;
            eyeL.localScale = eyeR.localScale = new Vector3(1f, eyeY, 1f);

            DetectWave();
            squash = Mathf.MoveTowards(squash, 0f, dt * 3f);
            wiggle = Mathf.MoveTowards(wiggle, 0f, dt * 1.2f);
            float sq = Util.Pop(1f - squash);
            body.localScale = new Vector3(1f + 0.2f * (1f - sq), 1f - 0.25f * (1f - sq), 1f + 0.2f * (1f - sq));
            body.localRotation = Quaternion.Euler(0f, 0f, wiggle > 0f ? Mathf.Sin(breathe * 18f) * 22f * wiggle : 0f);

            if (Head != null)
            {
                Vector3 to = transform.position - Head.position;
                if (to.sqrMagnitude > 1e-4f)
                {
                    var rot = Quaternion.LookRotation(to.normalized, Vector3.up);
                    Button.transform.rotation = rot;
                }
            }
            PlaceChoices();
            float s = 1f;
            switch (State)
            {
                case Mood.Idle: s = 1f + 0.08f * Mathf.Sin(breathe * 1.25f); break;
                case Mood.Listening: s = 1.3f + 0.5f * Level; break;
                case Mood.Thinking: s = 1.05f; break;
                case Mood.Speaking: s = 1.1f + 0.05f * Mathf.Sin(breathe * 6f); break;
            }
            if (Button.Hovered) s *= 1.12f;
            halo.transform.localScale = Vector3.one * 0.13f * s;
            core.transform.localScale = Vector3.one * Mathf.Lerp(1f, s, 0.4f);
            if (ring.activeSelf) ring.transform.localScale = Vector3.one * (0.17f + 0.08f * Level);
            for (int i = 0; i < motes.Count; i++)
            {
                if (!motes[i].activeSelf) continue;
                float a = breathe * 3f + i * Mathf.PI * 2f / 3f;
                motes[i].transform.localPosition = new Vector3(Mathf.Cos(a) * 0.045f, Mathf.Sin(a * 0.7f) * 0.012f, Mathf.Sin(a) * 0.045f);
            }
        }
    }
}
