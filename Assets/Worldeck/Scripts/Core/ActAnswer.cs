using System;
using System.Collections.Generic;
using System.Globalization;

namespace Worldeck.Core
{
    /// <summary>One simple shape of a built thing, in the thing's unit box (x, z from -0.5 to 0.5; y from 0 to 1).</summary>
    public struct ActPart
    {
        public string Shape;                 // box, cylinder, cone, sphere
        public float X, Y, Z, Sx, Sy, Sz, Rx, Ry, Rz;
        public float R, G, B;                // colour, 0–1

        /// <summary>Reads "shape x y z sx sy sz rx ry rz #rrggbb"; everything is clamped again here.</summary>
        public static bool TryParse(string line, out ActPart p)
        {
            p = default;
            if (string.IsNullOrEmpty(line) || line.Length > 120) return false;
            var t = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (t.Length < 11) return false;
            if (t[0] != "box" && t[0] != "cylinder" && t[0] != "cone" && t[0] != "sphere") return false;
            var n = new float[9];
            for (int i = 0; i < 9; i++)
                if (!float.TryParse(t[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i]) || float.IsNaN(n[i]) || float.IsInfinity(n[i])) return false;
            string c = t[10];
            if (c.Length != 7 || c[0] != '#') return false;
            if (!int.TryParse(c.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb)) return false;
            float C(float v, float lo, float hi) => Math.Min(hi, Math.Max(lo, v));
            p = new ActPart
            {
                Shape = t[0],
                X = C(n[0], -0.75f, 0.75f), Y = C(n[1], -0.1f, 1.25f), Z = C(n[2], -0.75f, 0.75f),
                Sx = C(n[3], 0.01f, 1.5f), Sy = C(n[4], 0.01f, 1.5f), Sz = C(n[5], 0.01f, 1.5f),
                Rx = C(n[6], -360f, 360f), Ry = C(n[7], -360f, 360f), Rz = C(n[8], -360f, 360f),
                R = ((rgb >> 16) & 255) / 255f, G = ((rgb >> 8) & 255) / 255f, B = (rgb & 255) / 255f
            };
            return true;
        }
    }

    /// <summary>One thing Juniper was asked to do: change the world, make a thing, change a thing, or start an event.</summary>
    public sealed class ActItem
    {
        public string Do = "", Name = "", Kind = "", Place = "front", Target = "last", Event = "", Color = "";
        public readonly List<ActPart> Parts = new List<ActPart>();
        public int Count = 1;
        public float Size = 0.25f, Scale = 1f;
        public bool Remove;
        /// <summary>With Remove: every thing of that name ("remove the pumpkins"), not only the last one.</summary>
        public bool Every;
        /// <summary>"Make it life size": as big as it is in life (LifeSize), as far as the room allows.</summary>
        public bool LifeSize;
        /// <summary>1: start turning on its own, -1: stop.</summary>
        public int Spin;
        public Recipe Recipe;
    }

    /// <summary>
    /// The relay's answer to "say anything", read with nothing trusted: only the four known actions, every number
    /// clamped, every word the player will see checked. The app can only ever do what is in this list.
    /// </summary>
    public sealed class ActAnswer
    {
        public const int MaxActions = 4, MaxParts = 28, MaxCount = 6;
        public const float MinSize = 0.05f, MaxSize = 1.5f;
        public static readonly string[] Places = { "front", "table", "floor", "left", "right", "there", "sofa", "shelf", "bed" };
        public static readonly string[] Events = { "storm", "rewind", "music", "music_off", "freeze", "resume", "next_world", "my_room", "quiet", "mute", "sound_on", "louder", "tour" };

        public bool Declined;
        public string Say = "", Source = "", Error;
        /// <summary>The answer to a question about the thing held up ("how fast could it run?"), or "".</summary>
        public string Answer = "";
        public float Ms;
        /// <summary>The relay's request id (X-Request-Id), or "".</summary>
        public string Rid = "";
        public readonly List<ActItem> Actions = new List<ActItem>();

        /// <param name="knownKind">True for a library object the app can build.</param>
        public static ActAnswer Read(string json, Func<string, bool> knownKind)
        {
            var a = new ActAnswer();
            var o = Json.FirstObject(json);
            if (o == null) { a.Error = "unreadable answer"; return a; }
            a.Declined = Json.Str(o, "safety", "ok") == "decline";
            a.Source = Json.Str(o, "source", "");
            string say = Json.Str(o, "say", "").Trim();
            a.Say = say.Length <= 90 && Words.IsClean(say) && say.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0 ? say : "";
            string answer = Json.Str(o, "answer", "").Trim();
            a.Answer = answer.Length <= 240 && Words.IsClean(answer) && answer.IndexOf("http", StringComparison.OrdinalIgnoreCase) < 0 && answer.IndexOf("www.", StringComparison.OrdinalIgnoreCase) < 0 ? answer : "";
            var list = Json.Arr(o, "actions");
            if (list == null || a.Declined) return a;
            foreach (var item in list)
            {
                if (a.Actions.Count >= MaxActions) break;
                if (!(item is Dictionary<string, object> d)) continue;
                string what = Json.Str(d, "do", "");
                var it = new ActItem { Do = what };
                if (what == "world")
                {
                    it.Recipe = Recipe.FromJson(Json.Obj(d, "recipe"));
                    if (it.Recipe == null) continue;
                    it.Recipe = it.Recipe.Clamp();
                }
                else if (what == "make")
                {
                    it.Name = CleanName(Json.Str(d, "name", ""));
                    string kind = Json.Str(d, "kind", "");
                    it.Kind = kind.Length > 0 && knownKind != null && knownKind(kind) ? kind : "";
                    // Shapes for something we have a real model of ("dog" built from boxes): the real model instead.
                    if (it.Kind.Length == 0 && it.Name.Length > 0)
                    {
                        string named = Wishes.KindOf(it.Name);
                        if (named.Length > 0 && knownKind != null && knownKind(named)) it.Kind = named;
                    }
                    if (it.Kind.Length == 0)
                    {
                        var parts = Json.Arr(d, "parts");
                        if (parts != null)
                            foreach (var p in parts)
                            {
                                if (it.Parts.Count >= MaxParts) break;
                                if (p is string s && ActPart.TryParse(s, out var part)) it.Parts.Add(part);
                            }
                        if (it.Parts.Count == 0) continue;
                    }
                    if (it.Name.Length == 0) it.Name = it.Kind.Length > 0 ? it.Kind.Replace('_', ' ') : "thing";
                    string place = Json.Str(d, "place", "front");
                    it.Place = Array.IndexOf(Places, place) >= 0 ? place : "front";
                    it.Count = (int)Math.Round(Math.Min(MaxCount, Math.Max(1, Json.Num(d, "count", 1))));
                    it.Size = (float)Math.Min(MaxSize, Math.Max(MinSize, Json.Num(d, "size", 0.25)));
                }
                else if (what == "change")
                {
                    string target = CleanName(Json.Str(d, "target", "last"));
                    it.Target = target.Length > 0 ? target : "last";
                    it.Remove = Json.Bool(d, "remove", false);
                    it.Scale = (float)Math.Min(4, Math.Max(0.25, Json.Num(d, "scale", 1)));
                    string color = Json.Str(d, "color", "");
                    it.Color = color.Length == 7 && color[0] == '#' && int.TryParse(color.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) ? color : "";
                    if (!it.Remove && Math.Abs(it.Scale - 1f) < 0.001f && it.Color.Length == 0) continue;
                }
                else if (what == "event")
                {
                    it.Event = Json.Str(d, "event", "");
                    if (Array.IndexOf(Events, it.Event) < 0) continue;
                }
                else continue;
                a.Actions.Add(it);
            }
            return a;
        }

        /// <summary>A thing's name: lower-case letters, digits and spaces, at most 24, and clean.</summary>
        public static string CleanName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new System.Text.StringBuilder();
            foreach (char c0 in s.ToLowerInvariant())
            {
                char c = (c0 >= 'a' && c0 <= 'z') || (c0 >= '0' && c0 <= '9') ? c0 : ' ';
                if (c == ' ' && (sb.Length == 0 || sb[sb.Length - 1] == ' ')) continue;
                sb.Append(c);
            }
            string n = sb.ToString().Trim();
            if (n.Length > 24) n = n.Substring(0, 24).Trim();
            return n.Length > 0 && Words.IsClean(n) ? n : "";
        }
    }
}
