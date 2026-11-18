using System.Collections;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// "Reading your room": before a world opens, each real piece of furniture shows what it is FOR as one
    /// glowing word over it ("sit", "build", "light", "pass"), one after another with a soft tick, then the
    /// words lift away as the pieces become the world's things. It makes the function mapping visible: the
    /// table becomes the fire ring *because* it is where you build, the sofa a bench *because* you sit on it.
    /// </summary>
    public sealed class RoomVerbs : MonoBehaviour
    {
        public Transform Head;
        public Material Panel;
        public Font Font;
        public float Stagger = 0.14f;

        readonly List<Caption> words = new List<Caption>();
        public int Shown { get; private set; }

        public static string Verb(Role role) => role switch
        {
            Role.Surface => "build",
            Role.Seat => "sit",
            Role.Tall => "lean",
            Role.Light => "light",
            Role.Plant => "grow",
            Role.Clutter => "rest",
            Role.Door => "pass",
            Role.Window => "look",
            _ => ""
        };

        /// <summary>Shows a verb over every real piece (nearest first), holds, then clears. Runs alongside the reveal.</summary>
        public IEnumerator Show(RoomModel room, float hold = 1.6f)
        {
            Clear();
            if (room == null || Head == null) yield break;
            var pieces = new List<RoomModel.Piece>();
            foreach (var p in room.Pieces) if (!p.Virtual && Verb(p.Role).Length > 0) pieces.Add(p);
            pieces.Sort((a, b) => Vector3.Distance(a.Center, Head.position).CompareTo(Vector3.Distance(b.Center, Head.position)));
            int n = 0;
            foreach (var p in pieces)
            {
                if (n >= 10) break;
                var c = Caption.Make("Verb " + p.Label, transform, Font, Panel, Head, 18f, 20f);
                c.transform.position = p.TopCenter + Vector3.up * (p.Role == Role.Door || p.Role == Role.Window ? -p.Size.y * 0.5f : 0.12f);
                c.Say(Verb(p.Role), 0f, new Color(1f, 0.82f, 0.5f, 0.98f));
                c.transform.localScale = Vector3.one * 0.6f;
                words.Add(c);
                StartCoroutine(Pop(c.transform));
                Sfx.Instance?.At("ui_hover", c.transform.position, 0.5f, 1f + 0.06f * n);
                n++;
                Shown = n;
                yield return new WaitForSeconds(Stagger);
            }
            Trace.Info("Room", "verbs shown", "count", n);
            yield return new WaitForSeconds(hold);
            // Lift and fade.
            float t = 0f;
            while (t < 0.6f)
            {
                t += Time.deltaTime;
                float k = t / 0.6f;
                foreach (var w in words)
                {
                    if (w == null) continue;
                    w.transform.position += Vector3.up * (Time.deltaTime * 0.12f);
                    w.transform.localScale = Vector3.one * Mathf.Lerp(1f, 0.7f, k);
                    if (w.Text != null) w.Text.color = new Color(1f, 0.82f, 0.5f, 1f - k);
                }
                yield return null;
            }
            Clear();
        }

        static IEnumerator Pop(Transform t)
        {
            float a = 0f;
            while (a < 0.35f && t != null)
            {
                a += Time.deltaTime;
                t.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, Util.Pop(a / 0.35f));
                yield return null;
            }
        }

        public void Clear()
        {
            foreach (var w in words) if (w != null) Util.Kill(w.gameObject);
            words.Clear();
        }
    }
}
