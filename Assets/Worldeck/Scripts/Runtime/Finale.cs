using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The payoff after a world's build: the thing you made appears across the sky as a constellation, star
    /// by star, each with a rising note, joined by soft lines; it holds a while and fades. World-sized, so the
    /// small act on your real table answers at the scale of the whole world (research G: a large reveal).
    /// </summary>
    public sealed class Finale : MonoBehaviour
    {
        public Transform Head;
        public Material Glow, GlowFlat;
        public float Distance = 60f, Elevation = 32f, Size = 20f, Hold = 22f;
        public bool Showing { get; private set; }
        public int Stars { get; private set; }

        readonly List<GameObject> made = new List<GameObject>();
        Coroutine run;
        float fade = 1f;

        // Shapes on a unit square (x, y in -1…1), drawn as one line through the points in order.
        static readonly Dictionary<string, Vector2[]> Shapes = new Dictionary<string, Vector2[]>
        {
            { "lantern", new[] { new Vector2(0, 1), new Vector2(0, 0.7f), new Vector2(-0.45f, 0.45f), new Vector2(-0.5f, -0.3f), new Vector2(0, -0.75f), new Vector2(0.5f, -0.3f), new Vector2(0.45f, 0.45f), new Vector2(0, 0.7f) } },
            { "sandcastle", new[] { new Vector2(-1, -0.8f), new Vector2(-1, 0.2f), new Vector2(-0.6f, 0.2f), new Vector2(-0.6f, -0.1f), new Vector2(-0.2f, -0.1f), new Vector2(-0.2f, 0.7f), new Vector2(0, 1), new Vector2(0.2f, 0.7f), new Vector2(0.2f, -0.1f), new Vector2(0.6f, -0.1f), new Vector2(0.6f, 0.2f), new Vector2(1, 0.2f), new Vector2(1, -0.8f) } },
            { "sun_crystal", new[] { new Vector2(0, 1), new Vector2(0.55f, 0.25f), new Vector2(0.4f, -0.7f), new Vector2(0, -1), new Vector2(-0.4f, -0.7f), new Vector2(-0.55f, 0.25f), new Vector2(0, 1) } },
            { "snowman", new[] { new Vector2(0, 1), new Vector2(0.28f, 0.72f), new Vector2(0, 0.45f), new Vector2(-0.28f, 0.72f), new Vector2(0, 1), new Vector2(0, 0.45f), new Vector2(0.45f, 0.05f), new Vector2(0, -0.3f), new Vector2(0.6f, -0.75f), new Vector2(0, -1), new Vector2(-0.6f, -0.75f), new Vector2(0, -0.3f), new Vector2(-0.45f, 0.05f), new Vector2(0, 0.45f) } },
            { "star_globe", new[] { new Vector2(0, 1), new Vector2(0.7f, 0.7f), new Vector2(1, 0), new Vector2(0.7f, -0.7f), new Vector2(0, -1), new Vector2(-0.7f, -0.7f), new Vector2(-1, 0), new Vector2(-0.7f, 0.7f), new Vector2(0, 1) } },
            { "pearl_shell", new[] { new Vector2(0, -0.9f), new Vector2(-0.9f, 0.1f), new Vector2(-0.5f, 0.8f), new Vector2(0, 1), new Vector2(0.5f, 0.8f), new Vector2(0.9f, 0.1f), new Vector2(0, -0.9f), new Vector2(0, 1) } },
            { "jukebox", new[] { new Vector2(-0.7f, -1), new Vector2(-0.7f, 0.4f), new Vector2(-0.4f, 0.85f), new Vector2(0, 1), new Vector2(0.4f, 0.85f), new Vector2(0.7f, 0.4f), new Vector2(0.7f, -1), new Vector2(-0.7f, -1) } },
            { "bonsai", new[] { new Vector2(0, -1), new Vector2(0, -0.2f), new Vector2(-0.6f, 0.3f), new Vector2(-0.3f, 0.8f), new Vector2(0, 0.5f), new Vector2(0.3f, 0.95f), new Vector2(0.7f, 0.4f), new Vector2(0, -0.2f) } },
        };

        public void Play(string souvenir, Color color, Vector3 forward)
        {
            Clear();
            run = StartCoroutine(Show(souvenir, color, forward));
        }

        public void Clear()
        {
            if (run != null) StopCoroutine(run);
            run = null;
            foreach (var g in made) if (g != null) Util.Kill(g);
            made.Clear();
            Showing = false;
            Stars = 0;
        }

        IEnumerator Show(string souvenir, Color color, Vector3 forward)
        {
            if (Head == null) yield break;
            Showing = true;
            fade = 1f;
            Trace.Info("Finale", "constellation", "shape", souvenir);
            if (!Shapes.TryGetValue(souvenir ?? "", out var pts)) pts = Shapes["star_globe"];
            forward.y = 0f;
            forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            Vector3 dir = Quaternion.AngleAxis(-Elevation, Vector3.Cross(Vector3.up, forward)) * forward;
            Vector3 center = Head.position + dir * Distance;
            Vector3 right = Vector3.Cross(Vector3.up, dir).normalized;
            Vector3 up = Vector3.Cross(dir, right).normalized;
            var world = new List<Vector3>();
            var seen = new List<Vector3>();
            foreach (var p in pts) world.Add(center + (right * p.x + up * p.y) * (Size * 0.5f));

            Sfx.Instance?.Ui("constellation", 0.9f);
            var line = new GameObject("Constellation lines").AddComponent<LineRenderer>();
            line.transform.SetParent(transform, false);
            made.Add(line.gameObject);
            line.useWorldSpace = true;
            line.sharedMaterial = GlowFlat;
            line.widthMultiplier = 0.12f;
            line.startColor = line.endColor = new Color(color.r, color.g, color.b, 1f) * 0.35f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.positionCount = 0;

            for (int i = 0; i < world.Count; i++)
            {
                Vector3 p = world[i];
                bool repeat = seen.Exists(q => (q - p).sqrMagnitude < 0.01f);
                if (!repeat)
                {
                    seen.Add(p);
                    var star = Effects.Halo("Star", transform, Glow, 2.4f, Color.Lerp(color, Color.white, 0.55f));
                    star.transform.position = p;
                    made.Add(star);
                    Stars++;
                    Sfx.Instance?.Ui("place_" + Mathf.Clamp(1 + Stars % 6, 1, 6), 0.45f);
                    StartCoroutine(Twinkle(star.transform, 2.4f));
                }
                line.positionCount = i + 1;
                line.SetPosition(i, p);
                yield return new WaitForSeconds(0.28f);
            }
            yield return new WaitForSeconds(Hold);
            // Fade.
            float t = 0f;
            Color c0 = line.startColor;
            while (t < 2f)
            {
                t += Time.deltaTime;
                float k = 1f - t / 2f;
                line.startColor = line.endColor = c0 * k;
                fade = k;
                yield return null;
            }
            Clear();
        }

        IEnumerator Twinkle(Transform t, float size)
        {
            float a = 0f;
            while (t != null)
            {
                a += Time.deltaTime;
                float pop = a < 0.4f ? Util.Pop(a / 0.4f) : 1f;
                t.localScale = Vector3.one * size * pop * fade * (1f + 0.08f * Mathf.Sin(a * 3.1f + t.position.x));
                yield return null;
            }
        }
    }
}
