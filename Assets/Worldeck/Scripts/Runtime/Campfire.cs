using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The campfire world's own play, after the fire is lit (every judge's first world):
    ///   Roast: a marshmallow on a stick rests within reach. Pinch the stick, hold the marshmallow over the fire:
    ///   raw → golden (a chime, 3–6 s) → brown → it catches fire (8 s). Shake it to put it out. Bring it to your
    ///   mouth to eat it. A golden one earns the gold souvenir. Up to three marshmallows.
    ///   Stoke: push your palm down over the fire three times and a column of sparks rises (the finale).
    /// </summary>
    public sealed class Campfire : MonoBehaviour
    {
        public HandInput Hands;
        public Transform Head;
        public Material World, Glow;
        public WorldLook Look;
        public float GoldenAt = 3f, BrownAt = 6f, BurnAt = 8f;
        public int Marshmallows = 3;

        public bool Running { get; private set; }
        public int Eaten { get; private set; }
        public int Golden { get; private set; }
        public int Stokes { get; private set; }
        public float Heat { get; private set; }
        public bool Burning { get; private set; }
        public bool Held => heldBy >= 0;
        public event Action<string> Ate;      // "golden", "raw", "toasty", "burnt"
        public event Action Stoked;
        /// <summary>Set by the director while it waits for the stoke (shows "push down" hints and the count).</summary>
        public bool WantStoke;

        /// <summary>Where the stick's handle is (where to pinch).</summary>
        public Vector3 Handle => stick != null ? stick.position + stick.forward * 0.03f : home;
        public Vector3 FirePos => fire;
        public bool HeldBy(int h) => heldBy >= 0 && heldBy == h;
        public bool NearStick(Vector3 p, float r = 0.1f) => stick != null && DistanceToStick(p) < r;
        public int Pumps => pumps.Count;
        bool mallowOver;

        Transform stick, mallow;
        Renderer mallowR;
        MaterialPropertyBlock block;
        Vector3 fire, home, homeDir;
        int heldBy = -1;
        bool chimed;
        ParticleSystem mallowFlame;
        float nextSizzle, respawnAt = -1f;
        readonly float[] lastPalmY = new float[3];
        readonly List<float> pumps = new List<float>();
        float pumpArmed;
        Vector3 prevPinch;

        static readonly Color Raw = new Color(0.98f, 0.96f, 0.92f), Gold = new Color(0.95f, 0.72f, 0.32f),
            Brown = new Color(0.55f, 0.32f, 0.16f), Char = new Color(0.12f, 0.1f, 0.1f);

        /// <summary>Starts the play around a fire at <paramref name="firePos"/> (its rim), stick resting at <paramref name="rest"/>.</summary>
        public void Begin(Vector3 firePos, Vector3 rest)
        {
            Stop();
            fire = firePos;
            home = rest;
            Vector3 toFire = fire - rest; toFire.y = 0f;
            homeDir = toFire.sqrMagnitude > 1e-4f ? toFire.normalized : Vector3.forward;
            BuildStick();
            Eaten = Golden = Stokes = 0;
            Running = true;
            NewMallow();
        }

        public void Stop()
        {
            Running = false;
            heldBy = -1;
            if (stick != null) Util.Kill(stick.gameObject);
            stick = null;
        }

        void BuildStick()
        {
            var root = new GameObject("Marshmallow stick").transform;
            root.SetParent(transform, false);
            var mb = new MeshBuilder();
            // The stick lies along +Z from the handle (origin) to the tip (0.34 m).
            mb.Push(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one);
            mb.Cylinder(Vector3.zero, 0.007f, 0.004f, 0.34f, 6, new Color(0.55f, 0.36f, 0.2f), true, true);
            mb.Pop();
            mb.Push(new Vector3(0f, 0f, 0.02f), Quaternion.Euler(90f, 0f, 0f), Vector3.one);
            mb.Cylinder(Vector3.zero, 0.0095f, 0.0095f, 0.07f, 6, new Color(0.35f, 0.22f, 0.12f), true, true);   // grip wrap
            mb.Pop();
            Util.MeshObject("Stick", root, mb.ToMesh("stick"), World);
            var m = new MeshBuilder();
            m.Push(Vector3.zero, Quaternion.Euler(90f, 0f, 0f), Vector3.one);
            m.Cylinder(new Vector3(0f, -0.016f, 0f), 0.017f, 0.017f, 0.032f, 10, Color.white, true, true);
            m.Pop();
            var mg = Util.MeshObject("Marshmallow", root, m.ToMesh("marshmallow"), World);
            mg.transform.localPosition = new Vector3(0f, 0f, 0.31f);
            mallow = mg.transform;
            mallowR = mg.GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            Effects.Halo("Glow", root, Glow, 0.12f, new Color(1f, 0.7f, 0.35f) * 0.35f).transform.localPosition = new Vector3(0, 0, 0.05f);
            stick = root;
            Rest();
        }

        void Rest()
        {
            if (stick == null) return;
            stick.position = home;
            stick.rotation = Quaternion.LookRotation(homeDir, Vector3.up) * Quaternion.Euler(-12f, 0f, 0f);
        }

        void NewMallow()
        {
            Heat = 0f;
            chimed = false;
            Burning = false;
            if (mallowFlame != null) Util.Kill(mallowFlame.gameObject);
            if (mallow != null) { mallow.gameObject.SetActive(true); mallow.localScale = Vector3.one; }
            Tint();
            Sfx.Instance?.At("item_appear", mallow != null ? mallow.position : home, 0.6f);
        }

        void Tint()
        {
            if (mallowR == null) return;
            Color c = Heat < GoldenAt ? Color.Lerp(Raw, Gold, Mathf.Pow(Heat / GoldenAt, 2f) * 0.4f)
                    : Heat < BrownAt ? Color.Lerp(Gold, Brown, (Heat - GoldenAt) / (BrownAt - GoldenAt) * 0.35f)
                    : Heat < BurnAt ? Color.Lerp(Brown, Char, (Heat - BrownAt) / (BurnAt - BrownAt) * 0.6f)
                    : Char;
            mallowR.GetPropertyBlock(block);
            block.SetColor("_Tint", c);
            mallowR.SetPropertyBlock(block);
        }

        public string Doneness => Burning || Heat >= BurnAt ? "burnt" : Heat >= BrownAt ? "toasty" : Heat >= GoldenAt ? "golden" : "raw";

        void Update()
        {
            if (!Running || Hands == null) return;
            float dt = Time.deltaTime;
            if (respawnAt > 0f && Time.time >= respawnAt) { respawnAt = -1f; NewMallow(); }

            // Holding the stick: pinch near the handle (or anywhere along the near half).
            if (heldBy < 0)
            {
                for (int h = 0; h < Hands.Count; h++)
                {
                    var s = Hands[h];
                    if (!s.Tracked || !Hands.PinchDown(h)) continue;
                    if (DistanceToStick(s.Pinch) < (s.Controller ? 0.08f : 0.06f))
                    {
                        heldBy = h;
                        prevPinch = s.Pinch;
                        Trace.Info("Campfire", "stick picked up", "hand", h);
                        Hints.Instance?.Learned("stick");
                        Sfx.Instance?.At("grab", s.Pinch, 0.8f);
                        break;
                    }
                }
            }
            if (heldBy >= 0)
            {
                var s = Hands[heldBy];
                if (!s.Tracked || !s.Pinching) { heldBy = -1; Rest(); }
                else
                {
                    // The stick points away from you, from the pinch, a little down.
                    Vector3 away = s.Pinch - (Head != null ? Head.position : s.Pinch - Vector3.forward);
                    away.y = Mathf.Min(away.y, 0f) - 0.15f;
                    Vector3 dir = Vector3.Slerp(stick.forward, away.normalized, 1f - Mathf.Exp(-dt * 14f));
                    stick.position = s.Pinch;
                    stick.rotation = Quaternion.LookRotation(dir, Vector3.up);
                    float speed = (s.Pinch - prevPinch).magnitude / Mathf.Max(1e-4f, dt);
                    prevPinch = s.Pinch;
                    if (Burning && speed > 1.5f) Extinguish();
                }
            }

            // Roasting: the marshmallow within 18 cm sideways of the fire and 4–40 cm above it.
            if (mallow != null && mallow.gameObject.activeSelf)
            {
                Vector3 m = mallow.position, d = m - fire;
                bool over = new Vector2(d.x, d.z).magnitude < 0.18f && d.y > 0.04f && d.y < 0.4f;
                mallowOver = over;
                if (over && !Burning)
                {
                    Heat += dt;
                    if (Time.time > nextSizzle) { nextSizzle = Time.time + 1.2f; Sfx.Instance?.At("roast_sizzle", m, 0.35f); }
                    if (!chimed && Heat >= GoldenAt) { chimed = true; Sfx.Instance?.At("golden_chime", m, 0.8f); Hints.Instance?.Learned("roast"); }
                    if (Heat >= BurnAt) Ignite();
                }
                Tint();
                // Eating: the marshmallow near your mouth.
                if (Head != null && heldBy >= 0)
                {
                    Vector3 mouth = Head.position - Head.up * 0.08f + Head.forward * 0.03f;
                    if (Vector3.Distance(m, mouth) < 0.12f) Eat();
                }
            }

            Stoke(dt);
            AskHints();
        }

        /// <summary>One line at a time, right by the marshmallow or the fire, saying what to do now.</summary>
        void AskHints()
        {
            var hints = Hints.Instance;
            if (hints == null || Hands == null) return;
            bool mallowOn = mallow != null && mallow.gameObject.activeSelf;
            if (heldBy < 0 && mallowOn && stick != null && hints.Wants("stick", 2))
            {
                for (int h = 0; h < Hands.Count; h++)
                {
                    var s = Hands[h];
                    if (s.Tracked && DistanceToStick(s.Pinch) < 0.22f) { hints.Request("stick", Handle + Vector3.up * 0.06f, "Pinch the stick here", 2); break; }
                }
            }
            else if (heldBy >= 0 && mallowOn)
            {
                Vector3 at = mallow.position + Vector3.up * 0.07f;
                if (Burning) hints.Request("burning", at, "It's on fire! Shake it out.", 5);
                else if (mallowOver && Heat >= GoldenAt && Heat < BrownAt) hints.Request("golden", at, "Golden! Pull it out now.", 4);
                else if (!mallowOver && Heat >= GoldenAt && hints.Wants("eat", 2)) hints.Request("eat", at, "Bring it to your mouth to eat it", 3);
                else if (!mallowOver && Heat < GoldenAt && hints.Wants("roast", 2)) hints.Request("roast", at, "Hold it over the fire", 2);
            }
            if (WantStoke || pumps.Count > 0)
            {
                Vector3 at = fire + Vector3.up * 0.34f;
                if (pumps.Count > 0) hints.Request("stoke count", at, pumps.Count + " of 3", 4);
                else
                {
                    for (int h = 0; h < Hands.Count; h++)
                    {
                        var s = Hands[h];
                        if (!s.Tracked || s.Controller) continue;
                        Vector3 d = s.Palm - fire;
                        if (new Vector2(d.x, d.z).magnitude < 0.35f && d.y < 0.6f)
                        {
                            hints.Request("stoke", at, s.PalmNormal.y < -0.5f ? "Now push down, fast, three times" : "Turn your palm down", 2);
                            break;
                        }
                    }
                }
            }
        }

        float DistanceToStick(Vector3 p)
        {
            if (stick == null) return float.MaxValue;
            Vector3 a = stick.position, b = stick.position + stick.forward * 0.2f;
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }

        void Ignite()
        {
            Burning = true;
            Sfx.Instance?.At("marshmallow_ignite", mallow.position, 0.8f);
            mallowFlame = Effects.Flames(mallow, Glow, 0.012f);
            mallowFlame.transform.localPosition = Vector3.zero;
            var main = mallowFlame.main;
            main.startSizeMultiplier = 0.35f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            mallowFlame.Play();
        }

        void Extinguish()
        {
            Burning = false;
            Heat = Mathf.Max(Heat, BurnAt);
            Sfx.Instance?.At("extinguish", mallow.position, 0.8f);
            if (mallowFlame != null) Util.Kill(mallowFlame.gameObject);
            mallowFlame = null;
        }

        void Eat()
        {
            string how = Doneness;
            if (Burning) Extinguish();
            Sfx.Instance?.At("crunch", mallow.position, 0.9f);
            mallow.gameObject.SetActive(false);
            Eaten++;
            if (how == "golden") Golden++;
            Trace.Info("Campfire", "ate the marshmallow", "doneness", how, "heat_s", Heat, "eaten", Eaten);
            Hints.Instance?.Learned("eat");
            Ate?.Invoke(how);
            if (Eaten < Marshmallows) respawnAt = Time.time + 1.5f;
        }

        /// <summary>Palm-down pushes over the fire: three within 2.5 s send up the sparks.</summary>
        void Stoke(float dt)
        {
            for (int h = 0; h < Hands.Count; h++)
            {
                var s = Hands[h];
                if (!s.Tracked || s.Controller || h == heldBy) { lastPalmY[h] = 0f; continue; }
                Vector3 d = s.Palm - fire;
                bool above = new Vector2(d.x, d.z).magnitude < 0.28f && d.y > 0.06f && d.y < 0.5f;
                bool palmDown = s.PalmNormal.y < -0.5f;
                float vy = lastPalmY[h] == 0f ? 0f : (s.Palm.y - lastPalmY[h]) / Mathf.Max(1e-4f, dt);
                lastPalmY[h] = s.Palm.y;
                if (!above || !palmDown) continue;
                if (vy < -0.45f && Time.time > pumpArmed)
                {
                    pumpArmed = Time.time + 0.25f;
                    pumps.Add(Time.time);
                    pumps.RemoveAll(t => Time.time - t > 2.5f);
                    Sfx.Instance?.At("stoke_whoosh", fire, 0.6f, 0.9f + 0.1f * pumps.Count);
                    if (Look != null) Look.FireStrength = Mathf.Min(1.6f, Look.FireStrength + 0.25f);
                    if (pumps.Count >= 3)
                    {
                        pumps.Clear();
                        Stokes++;
                        Trace.Info("Campfire", "stoked: sparks rise", "stokes", Stokes);
                        Sfx.Instance?.At("sparks_rise", fire, 0.9f);
                        var col = Effects.Embers(transform, Glow, 0f);
                        col.transform.position = fire + Vector3.up * 0.05f;
                        var e = col.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, 50) });
                        var main = col.main; main.loop = false; main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.4f); main.startLifetime = 2.5f;
                        main.maxParticles = 60;
                        col.Play();
                        Destroy(col.gameObject, 4f);
                        Stoked?.Invoke();
                    }
                }
            }
        }
    }
}
