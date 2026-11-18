using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// "Everything you touch is real": when a fingertip meets a piece of furniture (now the world's log, altar
    /// or console), a ring of light spreads from the touch and it makes a soft sound. The real surface stops
    /// the hand; the world answers it.
    /// </summary>
    public sealed class TouchRipples : MonoBehaviour
    {
        public HandInput Hands;
        public RoomSkins Skins;
        public Material Glow;
        public float Contact = 0.014f;
        public float Release = 0.03f;
        public float MinInterval = 0.22f;

        [Tooltip("The current world (set by the Director): taps on the table answer in its own way.")]
        public string Biome = "";
        [Tooltip("Downward speed (m/s) that turns a touch on a table into a tap.")]
        public float TapSpeed = 0.25f;

        readonly bool[] touching = new bool[3];
        readonly float[] last = new float[3];
        readonly Vector3[] prevTip = new Vector3[3];
        readonly float[] downSpeed = new float[3];
        readonly List<Ring> rings = new List<Ring>();
        Mesh ringMesh;
        int drum;
        public int Count { get; private set; }
        public int Taps { get; private set; }
        /// <summary>Raised on a tap on the table (the world's own reaction can join in).</summary>
        public event System.Action<Vector3> Tapped;

        sealed class Ring { public Transform T; public Renderer R; public float Age; public MaterialPropertyBlock Block; }

        void Update()
        {
            if (Hands == null || Skins == null) return;
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            for (int h = 0; h < Hands.Count; h++)
            {
                var s = Hands[h];
                if (!s.Tracked) { touching[h] = false; prevTip[h] = Vector3.zero; continue; }
                // Downward fingertip speed, smoothed over a few frames (hand tracking is jittery).
                float v = prevTip[h] == Vector3.zero ? 0f : (prevTip[h].y - s.IndexTip.y) / dt;
                downSpeed[h] = Mathf.Lerp(downSpeed[h], v, 0.5f);
                prevTip[h] = s.IndexTip;
                var skin = Skins.Touching(s.IndexTip, touching[h] ? Release : Contact, out bool top);
                bool now = skin != null;
                if (now && !touching[h] && Time.time - last[h] > MinInterval)
                {
                    last[h] = Time.time;
                    Vector3 normal = top ? Vector3.up : Side(skin.Piece, s.IndexTip);
                    Spawn(s.IndexTip, normal);
                    bool tap = top && skin.Piece.Role == Worldeck.Core.Role.Surface && downSpeed[h] > TapSpeed;
                    if (tap) Tap(s.IndexTip);
                    else
                    {
                        float pitch = skin.Piece.Role == Worldeck.Core.Role.Surface ? 1f : skin.Piece.Role == Worldeck.Core.Role.Seat ? 0.8f : 1.2f;
                        Sfx.Instance?.At("touch", s.IndexTip, 0.7f, pitch * Random.Range(0.94f, 1.06f));
                    }
                    Count++;
                }
                touching[h] = now;
            }
            for (int i = rings.Count - 1; i >= 0; i--)
            {
                var r = rings[i];
                r.Age += Time.deltaTime;
                float k = r.Age / 0.7f;
                if (k >= 1f) { r.T.gameObject.SetActive(false); continue; }
                r.T.localScale = Vector3.one * Mathf.Lerp(0.02f, 0.16f, Mathf.Sqrt(k));
                r.R.GetPropertyBlock(r.Block);
                r.Block.SetColor("_Tint", new Color(1f, 0.85f, 0.6f, 1f - k));
                r.R.SetPropertyBlock(r.Block);
            }
        }

        /// <summary>A tap on the table: the world answers in its own voice (sparks, sand, snow, a drum, bubbles…).</summary>
        public void Tap(Vector3 at)
        {
            Taps++;
            string clip = "tap_" + Biome;
            if (Biome == "city") clip = (drum++ & 1) == 0 ? "tap_city_1" : "tap_city_2";
            if (Sfx.Instance != null && Sfx.Instance.Has(clip)) Sfx.Instance.At(clip, at, 0.85f, Random.Range(0.96f, 1.04f));
            else Sfx.Instance?.At("touch", at, 0.8f, 1.1f);
            if (Glow != null)
            {
                var burst = Effects.Burst(transform, Glow, TapColor(Biome));
                burst.transform.position = at + Vector3.up * 0.01f;
                var main = burst.main;
                main.gravityModifier = Biome == "reef" ? -0.08f : Biome == "campfire" ? -0.15f : 0.2f;
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, Biome == "campfire" ? 0.7f : 0.4f);
                var shape = burst.shape; shape.shapeType = ParticleSystemShapeType.Hemisphere; shape.radius = 0.02f;
                burst.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
                burst.Play();
                Destroy(burst.gameObject, 2f);
            }
            Tapped?.Invoke(at);
        }

        static Color TapColor(string biome) => biome switch
        {
            "campfire" => new Color(1f, 0.62f, 0.25f),
            "beach" => new Color(0.95f, 0.82f, 0.6f),
            "desert" => new Color(1f, 0.85f, 0.55f),
            "snow" => new Color(0.92f, 0.96f, 1f),
            "space" => new Color(0.31f, 0.89f, 1f),
            "reef" => new Color(0.66f, 1f, 0.94f),
            "city" => new Color(1f, 0.37f, 0.64f),
            "meadow" => new Color(1f, 0.85f, 0.95f),
            _ => new Color(1f, 0.9f, 0.7f)
        };

        static Vector3 Side(RoomModel.Piece p, Vector3 tip)
        {
            var l = p.ToLocal(tip);
            float dx = Mathf.Abs(l.x) / Mathf.Max(0.01f, p.Size.x * 0.5f), dz = Mathf.Abs(l.z) / Mathf.Max(0.01f, p.Size.z * 0.5f);
            Vector3 n = dx > dz ? new Vector3(Mathf.Sign(l.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(l.z));
            return p.Rotation * n;
        }

        void Spawn(Vector3 at, Vector3 normal)
        {
            Ring r = rings.Find(x => !x.T.gameObject.activeSelf);
            if (r == null)
            {
                if (ringMesh == null) ringMesh = MakeRing();
                var go = Util.MeshObject("Ripple", transform, ringMesh, Glow);
                r = new Ring { T = go.transform, R = go.GetComponent<Renderer>(), Block = new MaterialPropertyBlock() };
                rings.Add(r);
            }
            r.Age = 0f;
            r.T.gameObject.SetActive(true);
            r.T.position = at + normal * 0.002f;
            r.T.rotation = Quaternion.FromToRotation(Vector3.up, normal);
        }

        static Mesh MakeRing()
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var t = new List<int>(); var uv = new List<Vector2>();
            int seg = 32;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * 0.42f); v.Add(d * 0.5f);
                c.Add(new Color(1f, 0.8f, 0.5f, 1f)); c.Add(new Color(1f, 0.8f, 0.5f, 0.3f));
                uv.Add(new Vector2(0.5f, 0.5f)); uv.Add(new Vector2(0.5f, 0.5f));
                if (i < seg) { int k = i * 2; t.AddRange(new[] { k, k + 1, k + 3, k, k + 3, k + 2 }); }
            }
            var m = new Mesh { name = "ripple" };
            m.SetVertices(v); m.SetColors(c); m.SetUVs(0, uv); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
