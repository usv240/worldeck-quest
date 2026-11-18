using System;
using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Presses buttons: each tracked fingertip (both hands, controller tips) entering a button's face, or a
    /// pinch while looking at a button. Ignores the hand that is holding something.
    /// </summary>
    public sealed class Pokes : MonoBehaviour
    {
        public HandInput Hands;
        public Transform Head;
        public Func<int, bool> Busy;   // hand index → holding something
        /// <summary>Which buttons may be pressed now (null: all). While How it works is up, only its own.</summary>
        public Func<PokeButton, bool> Allowed;
        public float GazeConeDeg = 6f;
        [Tooltip("Look at a button this long to press it (no hands needed). 0 turns it off.")]
        public float DwellSeconds = 1.4f;
        [Tooltip("Comfort & access: look at a button to press it even while hands are tracked.")]
        public bool AlwaysDwell;
        public Material Glow;
        PokeButton dwellOn;
        float dwellStart;
        GameObject dwellRing;
        public int Presses { get; private set; }
        /// <summary>No hands or controllers for 2 s: look-to-press is on (the eyes-only way to play).</summary>
        public bool EyesOnly { get; private set; }
        float handsSeenAt = -10f, wokeAt = -1f;
        /// <summary>Look-to-press waits this long after launch, and after the app comes back from a pause.</summary>
        public const float EyesDelay = 5f;
        /// <summary>Looking at a button starts to count only this long after it appeared.</summary>
        public const float FreshSeconds = 2f;
        /// <summary>A fingertip resting this long on a button that allows it (PokeButton.HoldPress) presses it.</summary>
        public const float RestSeconds = 0.22f;
        readonly Dictionary<PokeButton, Vector3>[] restPos = { new Dictionary<PokeButton, Vector3>(), new Dictionary<PokeButton, Vector3>(), new Dictionary<PokeButton, Vector3>(), new Dictionary<PokeButton, Vector3>() };
        readonly Dictionary<PokeButton, float>[] restAt = { new Dictionary<PokeButton, float>(), new Dictionary<PokeButton, float>(), new Dictionary<PokeButton, float>(), new Dictionary<PokeButton, float>() };
        void OnApplicationPause(bool paused) { if (!paused) wokeAt = Time.unscaledTime; }
        public string LastMethod { get; private set; } = "";

        readonly Dictionary<PokeButton, bool>[] inside = { new Dictionary<PokeButton, bool>(), new Dictionary<PokeButton, bool>(), new Dictionary<PokeButton, bool>() };
        readonly Dictionary<PokeButton, float>[] front = { new Dictionary<PokeButton, float>(), new Dictionary<PokeButton, float>(), new Dictionary<PokeButton, float>(), new Dictionary<PokeButton, float>() };
        // When each fingertip last pressed: one fingertip presses one button, and not another within 150 ms.
        readonly float[] lastPoke = { -10f, -10f, -10f, -10f };
        public const float PokeLockout = 0.15f;
        public PokeButton Gazed { get; private set; }
        readonly List<PokeButton> snapshot = new List<PokeButton>();

        /// <summary>
        /// Eyes-only pressing: a ring fills around the button you look at, and it presses when full. Works
        /// whether or not hands are tracked (and is the accessibility path for no hands at all).
        /// </summary>
        void Dwell()
        {
            if (DwellSeconds <= 0f || !(EyesOnly || AlwaysDwell))
            {
                dwellOn = null;
                if (dwellRing != null && dwellRing.activeSelf) dwellRing.SetActive(false);
                return;
            }
            if (Gazed != dwellOn) { dwellOn = Gazed; dwellStart = Time.unscaledTime; }
            if (dwellRing == null && Glow != null)
            {
                dwellRing = Util.MeshObject("Dwell ring", transform, RingMesh(), Glow);
                dwellRing.SetActive(false);
            }
            if (dwellOn == null || !dwellOn.isActiveAndEnabled) { if (dwellRing != null) dwellRing.SetActive(false); return; }
            // A button that has only just appeared is not pressed by the eyes that happened to be there (a question was
            // answered 1.4 s after it was asked, before it could be read).
            if (Time.unscaledTime - dwellOn.BornAt < FreshSeconds) dwellStart = Time.unscaledTime;
            float k = (Time.unscaledTime - dwellStart) / (DwellSeconds * Mathf.Max(0.2f, dwellOn.DwellScale));
            if (dwellRing != null)
            {
                dwellRing.SetActive(k > 0.15f);
                dwellRing.transform.position = dwellOn.transform.position - (dwellOn.transform.position - Head.position).normalized * 0.004f;
                dwellRing.transform.rotation = Quaternion.LookRotation(dwellOn.transform.position - Head.position, Vector3.up);
                float size = Mathf.Max(dwellOn.Size.x, dwellOn.Size.y) * 0.75f;
                dwellRing.transform.localScale = new Vector3(size, size, 1f) * Mathf.Lerp(1.4f, 1f, Mathf.Clamp01(k));
            }
            if (k >= 1f)
            {
                var b = dwellOn;
                dwellStart = Time.unscaledTime + 1.2f;   // don't press the same button again at once
                if (dwellRing != null) dwellRing.SetActive(false);
                Press(b, "look");
            }
        }

        void Press(PokeButton b, string how)
        {
            Presses++;
            LastMethod = how;
            Trace.Info("Hands", "pressed", "button", b.Id, "how", how);
            b.Press();
        }

        // ---------- controller rays ----------

        readonly GameObject[] rays = new GameObject[2];
        /// <summary>The button each controller's ray is on (null if none).</summary>
        public readonly PokeButton[] RayOn = new PokeButton[2];

        /// <summary>
        /// Controllers point and pull the trigger, as everywhere on Quest: a thin ray from each controller, the
        /// button it lands on lights up, the trigger presses it.
        /// </summary>
        void Rays(List<PokeButton> live)
        {
            for (int h = 0; h < 2; h++)
            {
                var s = Hands[h];
                RayOn[h] = null;
                bool on = s.Tracked && s.Controller && s.Aim.sqrMagnitude > 0.5f && (Busy == null || !Busy(h));
                if (rays[h] == null && Glow != null && on)
                {
                    rays[h] = Util.MeshObject("Controller ray " + h, transform, RayMesh(), Glow);
                }
                if (!on) { if (rays[h] != null) rays[h].SetActive(false); continue; }
                Vector3 o = s.Palm + s.Aim * 0.05f, dir = s.Aim.normalized;
                float best = 3f;
                PokeButton hit = null;
                foreach (var b in live)
                {
                    if (b == null || !b.isActiveAndEnabled) continue;
                    Vector3 n = -b.transform.forward;   // out of the face, towards the user
                    float denom = Vector3.Dot(dir, n);
                    if (denom > -1e-4f) continue;          // pointing away from the face
                    float t = Vector3.Dot(b.transform.position - o, n) / denom;
                    if (t <= 0f || t >= best) continue;
                    if (!b.Over(o + dir * t, 0.004f)) continue;
                    best = t; hit = b;
                }
                RayOn[h] = hit;
                if (rays[h] != null)
                {
                    rays[h].SetActive(true);
                    float len = hit != null ? best : 0.6f;
                    rays[h].transform.position = o;
                    rays[h].transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
                    rays[h].transform.localScale = new Vector3(1f, 1f, len);
                }
                if (hit == null) continue;
                hit.SetHover(true);
                hit.FingerHover = true;
                if (Hands.PinchDown(h)) Press(hit, "ray");
            }
        }

        static Mesh rayMesh;
        /// <summary>Two crossed thin ribbons along +Z, 1 m long, bright at the controller and fading out.</summary>
        static Mesh RayMesh()
        {
            if (rayMesh != null) return rayMesh;
            const float w = 0.0016f;
            var near = new Color(1f, 0.85f, 0.65f, 0.55f);
            var far = new Color(1f, 0.85f, 0.65f, 0.08f);
            var v = new List<Vector3>
            {
                new Vector3(-w, 0, 0), new Vector3(w, 0, 0), new Vector3(w, 0, 1), new Vector3(-w, 0, 1),
                new Vector3(0, -w, 0), new Vector3(0, w, 0), new Vector3(0, w, 1), new Vector3(0, -w, 1)
            };
            var c = new List<Color> { near, near, far, far, near, near, far, far };
            var uv = new List<Vector2>();
            for (int i = 0; i < 8; i++) uv.Add(new Vector2(0.5f, 0.5f));
            var t = new List<int> { 0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7 };
            rayMesh = new Mesh { name = "controller-ray" };
            rayMesh.SetVertices(v); rayMesh.SetColors(c); rayMesh.SetUVs(0, uv); rayMesh.SetTriangles(t, 0);
            rayMesh.RecalculateBounds();
            return rayMesh;
        }

        static Mesh ring;
        static Mesh RingMesh()
        {
            if (ring != null) return ring;
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>(); var uv = new List<Vector2>();
            int seg = 40;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(d * 0.46f); v.Add(d * 0.5f);
                c.Add(new Color(1f, 0.8f, 0.5f, 1f)); c.Add(new Color(1f, 0.8f, 0.5f, 1f));
                uv.Add(new Vector2(0.5f, 0.5f)); uv.Add(new Vector2(0.5f, 0.5f));
                if (i < seg) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 }); }
            }
            ring = new Mesh { name = "dwell-ring" };
            ring.SetVertices(v); ring.SetColors(c); ring.SetUVs(0, uv); ring.SetTriangles(t, 0);
            return ring;
        }

        void Update()
        {
            if (Hands == null) return;
            if (Hands.AnyTracked) handsSeenAt = Time.unscaledTime;
            // Not in the first seconds after launch or coming back: hands are not tracked yet then, and a glance pressed
            // a souvenir at 3.6 s, before the room's scan had loaded (seen on the headset).
            if (wokeAt < 0f) wokeAt = Time.unscaledTime;
            bool eyes = Time.unscaledTime - handsSeenAt > 2f && Time.unscaledTime - wokeAt > EyesDelay;
            if (eyes != EyesOnly) { EyesOnly = eyes; Trace.Info("Hands", eyes ? "eyes-only: look at a button to press it" : "hands back: look-to-press off"); }
            // A press can rebuild a panel (buttons enable and disable): work on a copy.
            snapshot.Clear();
            snapshot.AddRange(PokeButton.Live);
            if (Allowed != null) snapshot.RemoveAll(b => b != null && !Allowed(b));
            var live = snapshot;
            foreach (var b in live) if (b != null) { b.SetHover(false); b.FingerHover = false; }
            // Forget destroyed buttons now and then (rebuilt panels leave them behind).
            if (Time.frameCount % 300 == 0)
                for (int k = 0; k < inside.Length; k++)
                {
                    var dead = new List<PokeButton>();
                    foreach (var key in inside[k].Keys) if (key == null) dead.Add(key);
                    foreach (var key in dead) { inside[k].Remove(key); restAt[k].Remove(key); restPos[k].Remove(key); }
                    var gone = new List<PokeButton>();
                    foreach (var key in front[k].Keys) if (key == null) gone.Add(key);
                    foreach (var key in gone) front[k].Remove(key);
                }
            for (int h = 0; h < Hands.Count; h++)
            {
                var s = Hands[h];
                if (!s.Tracked || (Busy != null && Busy(h))) { inside[h].Clear(); continue; }
                PokeButton pick = null;
                float pickOff = float.MaxValue;
                foreach (var b in live)
                {
                    if (b == null || !b.isActiveAndEnabled) continue;
                    var l = b.Local(s.IndexTip);
                    bool over = b.Over(s.IndexTip);
                    if (over && l.z < 0.04f && l.z > -0.03f) { b.SetHover(true); b.FingerHover = true; }
                    bool inZone = over && l.z <= 0.006f && l.z > -0.035f;
                    // In front of the button, coming towards it (up to 6 cm away): the start of a real poke.
                    if (over && l.z > 0.006f && l.z < 0.06f) front[h][b] = Time.unscaledTime;
                    // First sight of this button for this hand: note where the finger is, press nothing. A finger
                    // already resting where a new button appears (a rebuilt panel, the next interview question,
                    // the keypad after a key) must move out and in again.
                    if (!inside[h].TryGetValue(b, out bool was)) { inside[h][b] = inZone; continue; }
                    if (inZone && !was)
                    {
                        // A press comes from the front. A finger that slid in sideways (a hand carrying a piece past
                        // the dock: "Previous" pressed by accident on Nov 12) does nothing.
                        bool fromFront = Hands[h].Controller || (front[h].TryGetValue(b, out float at) && Time.unscaledTime - at < 0.6f);
                        if (fromFront && !b.Armed)
                        {
                            if (Trace.Every("poke-unarmed-" + b.Id, 5f)) Trace.Info("Hands", "poke ignored: the button had just appeared, or a hand is carrying", "button", b.Id);
                        }
                        else if (fromFront)
                        {
                            // How far off its middle the fingertip is, in button sizes: the most squarely touched wins.
                            float off = new Vector2(l.x / Mathf.Max(0.005f, b.Size.x), l.y / Mathf.Max(0.005f, b.Size.y)).sqrMagnitude;
                            if (off < pickOff) { pickOff = off; pick = b; }
                        }
                        else if (Trace.Every("poke-side-" + b.Id, 5f)) Trace.Info("Hands", "slid onto a button: not a press", "button", b.Id);
                    }
                    if (b.HoldPress)
                    {
                        // Resting on it is a press too: it came from the side or from below, and stayed.
                        // Still, too: a hand passing across the row (carrying a piece, reaching past) is not resting on it.
                        if (!inZone) { restAt[h].Remove(b); restPos[h].Remove(b); }
                        // Armed only when the finger arrives on it, or moves on it: a finger that was already there when the
                        // button appeared (the questions rebuild under it after each answer) presses nothing.
                        else if (!was) { restAt[h][b] = Time.unscaledTime; restPos[h][b] = l; }
                        // Moving on it starts the wait again, unless it has pressed already (then only leaving it re-arms).
                        else if (restPos[h].TryGetValue(b, out Vector3 rp) && (l - rp).magnitude > 0.015f)
                        { restPos[h][b] = l; if (restAt[h].TryGetValue(b, out float s0) && s0 > 0f) restAt[h][b] = Time.unscaledTime; }
                        else if (restAt[h].TryGetValue(b, out float since) && since > 0f && Time.unscaledTime - since >= RestSeconds && b.Armed)
                        {
                            restAt[h][b] = -1f;
                            float offRest = new Vector2(l.x / Mathf.Max(0.005f, b.Size.x), l.y / Mathf.Max(0.005f, b.Size.y)).sqrMagnitude;
                            if (offRest < pickOff) { pickOff = offRest; pick = b; Trace.Info("Hands", "a resting fingertip pressed it", "button", b.Id); }
                        }
                    }
                    inside[h][b] = inZone;
                }
                // Two keys side by side under one fingertip pressed together on Nov 12 ("puppy" typed as "pyuppy").
                if (pick != null && h < lastPoke.Length && Time.unscaledTime - lastPoke[h] >= PokeLockout)
                {
                    lastPoke[h] = Time.unscaledTime;
                    Press(pick, Hands[h].Controller ? "controller" : "poke");
                }
            }
            Rays(live);
            // Look and pinch.
            Gazed = null;
            if (Head != null)
            {
                float best = Mathf.Cos(GazeConeDeg * Mathf.Deg2Rad);
                foreach (var b in live)
                {
                    if (b == null || !b.isActiveAndEnabled) continue;
                    Vector3 to = b.transform.position - Head.position;
                    float d = Vector3.Dot(to.normalized, Head.forward);
                    float allowance = Mathf.Cos(Mathf.Atan2(Mathf.Max(b.Size.x, b.Size.y) * 0.5f, to.magnitude) + GazeConeDeg * 0.5f * Mathf.Deg2Rad);
                    if (d > Mathf.Min(best, allowance)) { best = d; Gazed = b; }
                }
                Dwell();
                if (Gazed != null)
                {
                    for (int h = 0; h < Hands.Count; h++)
                    {
                        if (Busy != null && Busy(h)) continue;
                        var s = Hands[h];
                        if (s.Controller) continue;   // controllers press with their ray
                        // A pinch far from any button (not a poke): the hand is resting, the eyes choose.
                        if (Hands.PinchDown(h) && Vector3.Distance(s.Pinch, Gazed.transform.position) > 0.12f) { Press(Gazed, "look+pinch"); break; }
                    }
                    if (!Gazed.Hovered) Gazed.SetHover(true);
                }
            }
        }
    }
}
