using System;
using System.Collections.Generic;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The controls outside the world: turn a palm up (or press the menu button on a controller) and a small
    /// panel appears in front of you, fixed in the room, 15° below your eyes, about 44 cm away. Press its
    /// buttons with the other hand's fingertip, or look and pinch. Everything voice can do is here too.
    /// </summary>
    public sealed class HandPanel : MonoBehaviour
    {
        public HandInput Hands;
        public Transform Head;
        public Material Panel;
        public Font Font;
        public float HoldToOpen = 0.3f;
        public float Distance = 0.44f;
        public const float PitchDown = 6f;

        public enum Page { Main, Worlds, Saved, Codes, Comfort, Running, Records, Say }

        /// <summary>The "What can I say?" page: one of each kind of thing, to say (or touch to try).</summary>
        public static readonly string[] SayExamples =
        {
            "take me to Egypt", "on Mars", "make it rain",
            "at sunset", "make me a fox", "put a t rex on the table",
            "make it life size", "take me on a tour", "peace and quiet"
        };
        string entry = "";
        public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        public Page Current { get; private set; } = Page.Main;
        public bool Open { get; private set; }
        public bool InWorld;
        public bool Frozen;
        public bool CanUndo;
        /// <summary>On-device voice exists here (the switch shows) and whether it is on.</summary>
        public bool VoiceSupported, VoiceOn;
        /// <summary>Away from the table: "Back to camp" comes first.</summary>
        public bool Away;
        public bool StepMove;
        /// <summary>An emergency is running (the menu offers Rewind) or can be started (Storm drill).</summary>
        public bool StormOn;
        public string Footer = "";
        public List<Progress.Saved> SavedList = new List<Progress.Saved>();
        /// <summary>The settings shown on the Comfort & access page.</summary>
        public Progress Settings;
        /// <summary>Before Mara's first chapter is done the extras wait (Tour, Explore, Records, New player): the first
        /// ten minutes have one path.</summary>
        bool Early => Settings != null && Settings.MissionChapter < 1;
        /// <summary>The text of the How it's running and Records pages.</summary>
        public Func<string> RunningText, RecordsText;
        /// <summary>Commands: "talk", "worlds", "undo", "freeze", "resume", "save", "end", "today", "surprise", "saved", "scan", "back", "world:&lt;id&gt;", "saved:&lt;index&gt;".</summary>
        public event Action<string> Command;
        public bool Suppressed;
        /// <summary>
        /// A hand is carrying something (or has only just put it down): the menu does not open, and an open one
        /// closes, so nothing on it is pressed by a hand on its way to the table.
        /// </summary>
        public Func<bool> Carrying;

        Transform frame;
        Caption footer, title;
        readonly List<PokeButton> buttons = new List<PokeButton>();
        float lastActive;
        bool rearm;   // a command closed the panel: the palm must go down before palm-up opens it again
        /// <summary>A palm held up for something else (her ship landing): it must go down before it opens the menu.</summary>
        public void Rearm() => rearm = true;
        float openedAt;

        void Start()
        {
            frame = new GameObject("Panel").transform;
            frame.SetParent(transform, false);
            title = Caption.Make("Panel title", frame, Font, Panel, Head, 15f, 24f);
            title.Fixed = true;
            title.FaceHead = false;
            title.transform.localPosition = new Vector3(0f, 0.072f, 0f);
            title.Text.anchor = TextAnchor.LowerCenter;   // grows upward, away from the buttons
            footer = Caption.Make("Panel footer", frame, Font, Panel, Head, 13f, 26f);
            footer.Fixed = true;
            footer.FaceHead = false;
            footer.transform.localPosition = new Vector3(0f, -0.082f, 0f);
            footer.Text.anchor = TextAnchor.UpperCenter;   // grows downward
            frame.gameObject.SetActive(false);
        }

        void Update()
        {
            if (Hands == null || Head == null || frame == null) return;
            bool palm = false;
            // Only a controller's menu button opens it this way. With hands, the menu is the wrist button: a palm up sat
            // next to Quest's own palm-toward-face gesture and the two were confused (Nov 17, worn).
            for (int h = 0; h < Hands.Count; h++) if (Hands.PalmUpFor(h) >= HoldToOpen && Hands[h].Controller) palm = true;
            if (!palm) rearm = false;
            bool carrying = Carrying != null && Carrying();
            if (carrying && Open) { Trace.Info("Panel", "closed: something is being carried"); Hide(); rearm = true; }
            if (!Open && palm && !Suppressed && !rearm && !carrying) Show(Page.Main);
            if (!Open) return;
            bool hover = false;
            foreach (var b in buttons) if (b != null && b.Hovered) hover = true;
            if (palm || hover) lastActive = Time.unscaledTime;
            Vector3 to = (frame.position - Head.position).normalized;
            bool looking = Vector3.Dot(to, Head.forward) > 0.5f;
            if (looking && Time.unscaledTime - openedAt < 2f) lastActive = Time.unscaledTime;
            bool reading = Current == Page.Codes || Current == Page.Comfort || Current == Page.Running || Current == Page.Records || Current == Page.Say;
            if (Time.unscaledTime - lastActive > (reading ? 20f : looking ? 6f : 2.5f)) Hide();
        }

        public void Show(Page page)
        {
            if (!Open)
            {
                Vector3 f = Head.forward; f.y = 0f;
                f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
                // Just below the line of sight (6°): read without looking down, reached without lifting the arm high.
                Vector3 pos = Head.position + f * Distance * Mathf.Cos(PitchDown * Mathf.Deg2Rad) - Vector3.up * Distance * Mathf.Sin(PitchDown * Mathf.Deg2Rad);
                frame.position = pos;
                frame.rotation = Quaternion.LookRotation(pos - Head.position, Vector3.up);
                openedAt = Time.unscaledTime;
                Sfx.Instance?.Ui("ui_hover", 0.6f);
            }
            if (!Open) Trace.Info("Panel", "opened", "page", page.ToString(), "in_world", InWorld);
            Open = true;
            lastActive = Time.unscaledTime;
            frame.gameObject.SetActive(true);
            Current = page;
            Build();
        }

        public void Hide()
        {
            if (Open) Trace.Info("Panel", "closed");
            Open = false;
            if (frame != null) frame.gameObject.SetActive(false);
        }

        public void Refresh() { if (Open) Build(); }

        void Build()
        {
            foreach (var b in buttons) if (b != null) Util.Kill(b.gameObject);
            buttons.Clear();
            var items = new List<(string label, string cmd, bool hi)>();
            switch (Current)
            {
                case Page.Main:
                    title.Say(InWorld ? "This world" : "Your room");
                    if (InWorld)
                    {
                        if (Away) items.Add(("Back to camp", "camp", true));
                        items.Add(("Worlds", "map", false));
                        if (!Away && !Early) items.Add(("Explore", "explore", false));
                        if (!Early) items.Add(("Tour", "tour", false));
                        items.Add(("Ask Juniper", "sayhelp", false));
                        items.Add(("Settings", "settings", false));
                        items.Add(("My room", "end", false));
                    }
                    else
                    {
                        items.Add(("Worlds", "map", true));
                        items.Add(("Ask Juniper", "sayhelp", false));
                        items.Add(("Settings", "settings", false));
                        items.Add(("Use my furniture", "scan", false));
                    }
                    break;
                case Page.Worlds:
                    title.Say("Where to?");
                    foreach (var b in Kit.Biomes) items.Add((b.Short, "world:" + b.Id, false));
                    items.Add(("Describe…", "describe", false));
                    items.Add(("Surprise me", "surprise", false));
                    items.Add(("Back", "back", false));
                    break;
                case Page.Saved:
                    title.Say(SavedList.Count == 0 ? "Nothing saved yet" : "Saved worlds");
                    for (int i = 0; i < SavedList.Count && i < 8; i++) items.Add((Short(SavedList[i].Name), "saved:" + i, false));
                    items.Add(("Back", "back", false));
                    break;
                case Page.Codes:
                    BuildKeypad();
                    footer.Say(Footer);
                    return;
                case Page.Comfort:
                {
                    title.Say("Comfort & access");
                    var p = Settings ?? new Progress();
                    items.Add((p.CaptionsLarge ? "Big text: on" : "Big text: off", "set:captions", p.CaptionsLarge));
                    items.Add((p.GentleStorm ? "Gentle storm: on" : "Gentle storm: off", "set:gentle", p.GentleStorm));
                    items.Add((p.CalmEffects ? "Calm effects: on" : "Calm effects: off", "set:calm", p.CalmEffects));
                    items.Add((p.LookToPress ? "Hands-free: on" : "Hands-free: off", "set:look", p.LookToPress));
                    items.Add(("Sound: " + (p.SoundLevel == 0 ? "quiet" : p.SoundLevel == 2 ? "loud" : "normal"), "set:sound", false));
                    items.Add((p.StepMove ? "Moving: steps" : "Moving: glide", "stepmove", p.StepMove));
                    items.Add((p.CallWhenReady ? "Mara calls: when I'm ready" : "Mara calls: the first time only", "set:call", p.CallWhenReady));
                    items.Add((p.MusicOn ? "Music: on" : "Music: off", "set:music", p.MusicOn));
                    items.Add((p.Muted ? "Sound: muted" : "Mute", "set:mute", p.Muted));
                    items.Add(("How it works", "help", false));
                    items.Add(("How it's running", "running", false));
                    if (!Early)
                    {
                        items.Add(("Records", "records", false));
                        items.Add(("New player", "set:newplayer", false));
                    }
                    items.Add(("Back", "back", false));
                    break;
                }
                case Page.Say:
                    title.Say(VoiceOn ? "Say \u201CJuniper,\u201D then any of these. Or touch one." : "Touch one to try it. Turn on Voice to say them.");
                    items.Add(("Type\u2026", "type", true));
                    if (VoiceSupported) items.Add((VoiceOn ? "Voice: on" : "Voice: off", "voice", VoiceOn));
                    foreach (var e in SayExamples) items.Add((e, "say:" + e, false));
                    items.Add(("Back", "back", false));
                    break;
                case Page.Running:
                    title.Say("How it's running");
                    items.Add(("Records", "records", false));
                    items.Add(("Back", "settings", false));
                    footer.Say(RunningText != null ? RunningText() : "");
                    Layout(items);
                    return;
                case Page.Records:
                    title.Say("Your records");
                    items.Add(("Back", "running", false));
                    footer.Say(RecordsText != null ? RecordsText() : "");
                    Layout(items);
                    return;
            }
            footer.Say(Footer);
            Layout(items);
        }

        void Layout(List<(string label, string cmd, bool hi)> items)
        {
            int cols = Current == Page.Main || Current == Page.Comfort ? 3 : 4;
            float w = Current == Page.Main || Current == Page.Comfort ? 0.09f : Current == Page.Say ? 0.078f : 0.066f, h = 0.044f, gap = 0.012f;
            // At most four rows: a taller grid runs into the words above and below it.
            if ((items.Count + cols - 1) / cols > MaxRows) { cols = (items.Count + MaxRows - 1) / MaxRows; w = Mathf.Min(w, 0.3f / cols - gap); }
            int rows = (items.Count + cols - 1) / cols;
            float half = (rows * h + (rows - 1) * gap) * 0.5f;
            PlaceWords(half, -half);
            for (int i = 0; i < items.Count; i++)
            {
                int r = i / cols, c = i % cols;
                int inRow = Mathf.Min(cols, items.Count - r * cols);
                float x = (c - (inRow - 1) * 0.5f) * (w + gap);
                float y = ((rows - 1) * 0.5f - r) * (h + gap);
                var it = items[i];
                var b = PokeButton.Make(it.cmd, frame, Panel, Font, it.label, new Vector2(w, h), 14f);
                b.transform.localPosition = new Vector3(x, y, 0f);
                b.Highlight = it.hi;
                b.Hint = HintFor(it.cmd);
                string cmd = it.cmd;
                b.Pressed += () => OnPress(cmd);
                buttons.Add(b);
            }
        }

        public const int MaxRows = 4;

        /// <summary>The title just above the buttons and the footer just below, however tall the grid is.</summary>
        void PlaceWords(float top, float bottom)
        {
            title.transform.localPosition = new Vector3(0f, top + 0.014f, 0f);
            footer.transform.localPosition = new Vector3(0f, bottom - 0.014f, 0f);
        }

        /// <summary>The panel's title and footer lines (for tests).</summary>
        public Caption TitleLine => title;
        public Caption FooterLine => footer;

        /// <summary>What a menu button does, in a few words (shown beside it on hover, the first few times).</summary>
        public static string HintFor(string cmd) => cmd switch
        {
            "talk" => "Say a place or a change",
            "map" => "All the worlds",
            "undo" => "Takes back the last change",
            "freeze" => "Stops time. Touch the rain!",
            "resume" => "Starts time again",
            "save" => "Keeps this world, with a share code",
            "help" => "Every way to play, with demos",
            "end" => "Back to your room",
            "today" => "One world a day, same for everyone",
            "surprise" => "A random world",
            "describe" => "Four questions, then your place",
            "saved" => "Worlds you kept",
            "codes" => "Type a friend's world code",
            "scan" => "Scan your room so your sofa and table join in",
            "close" => "Closes this menu",
            "tour" => "Sit back: Juniper takes you round this world",
            "set:mute" => "Every sound off; captions stay",
            "set:music" => "Soft music in the background",
            "settings" => "Comfort & access: text size, hands-free, gentler storm, sound",
            "set:captions" => "Larger captions",
            "set:gentle" => "Bigger catch zone, more forgiving timing",
            "set:calm" => "Fewer sparks and flashes",
            "set:newplayer" => "For someone new: clears progress, plays the introduction",
            "set:look" => "Turn your head to a button, piece or ring: no hands needed",
            "set:sound" => "Quiet, normal or loud",
            "set:call" => "Explore first: she waits for the button on your table",
            "sayhelp" => "Everything you can ask for, to say or touch",
            "running" => "Frame rate, voice, AI and more, live",
            "records" => "Your best storm, landings, worlds visited",
            "explore" => "Go beyond your table: jump to the beams of light",
            "camp" => "Back to your table",
            "storm" => "Block a meteor storm with your hands",
            "rewind" => "Back in time: catch the ones you missed",
            "stepmove" => "Steps: no sliding, easiest on the stomach",
            "voice" => "Say \"Juniper\" anytime. Stays on this headset",
            _ => ""
        };

        static string Short(string s) => s.Length <= 18 ? s : s.Substring(0, 17) + "…";

        /// <summary>
        /// A friend's share code, typed with a fingertip: 32 keys in 4 rows (the code's alphabet has no I, L, O
        /// or U), the entry shown above, dashes added as you go, and the code checked as soon as 11 characters are in.
        /// </summary>
        void BuildKeypad()
        {
            string shown = entry.Length == 0 ? "Type a friend's code" : Pretty(entry);
            title.Say(shown);
            float w = 0.034f, h = 0.034f, gap = 0.007f;
            int cols = 8;
            for (int i = 0; i < Alphabet.Length; i++)
            {
                int r = i / cols, c = i % cols;
                string ch = Alphabet[i].ToString();
                var b = PokeButton.Make("key " + ch, frame, Panel, Font, ch, new Vector2(w, h), 15f);
                b.transform.localPosition = new Vector3((c - 3.5f) * (w + gap), (1.5f - r) * (h + gap) - 0.004f, 0f);
                b.Pressed += () => Key(ch);
                buttons.Add(b);
            }
            float y = -2.5f * (h + gap) - 0.006f;
            PlaceWords(1.5f * (h + gap) - 0.004f + h * 0.5f, y - h * 0.5f);
            var back = PokeButton.Make("key back", frame, Panel, Font, "Delete", new Vector2(0.075f, h), 14f);
            back.transform.localPosition = new Vector3(-0.1f, y, 0f);
            back.Pressed += () => { if (entry.Length > 0) entry = entry.Substring(0, entry.Length - 1); Build(); };
            buttons.Add(back);
            var done = PokeButton.Make("key done", frame, Panel, Font, "Open", new Vector2(0.075f, h), 14f);
            done.transform.localPosition = new Vector3(0f, y, 0f);
            done.Highlight = entry.Length == 11;
            done.Pressed += () => Submit();
            buttons.Add(done);
            var exit = PokeButton.Make("key exit", frame, Panel, Font, "Back", new Vector2(0.075f, h), 14f);
            exit.transform.localPosition = new Vector3(0.1f, y, 0f);
            exit.Pressed += () => { entry = ""; Show(Page.Main); };
            buttons.Add(exit);
        }

        public static string Pretty(string code)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < code.Length; i++) { if (i == 4 || i == 8) sb.Append('-'); sb.Append(code[i]); }
            return sb.ToString();
        }

        void Key(string ch)
        {
            lastActive = Time.unscaledTime;
            if (entry.Length >= 11) return;
            entry += ch;
            Build();
            if (entry.Length == 11) Submit();
        }

        void Submit()
        {
            if (entry.Length < 11) return;
            string code = entry;
            entry = "";
            Hide();
            Command?.Invoke("code:" + code);
        }

        /// <summary>Opens the keypad (from the guide's choices or a spoken "enter a code").</summary>
        public void ShowCodes() { entry = ""; Show(Page.Codes); }

        void OnPress(string cmd)
        {
            lastActive = Time.unscaledTime;
            switch (cmd)
            {
                case "worlds": Show(Page.Worlds); return;
                case "saved": Show(Page.Saved); return;
                case "codes": ShowCodes(); return;
                case "back": Show(Page.Main); return;
                case "settings": Show(Page.Comfort); return;
                case "sayhelp": Show(Page.Say); return;
                case "running": Show(Page.Running); return;
                case "records": Show(Page.Records); return;
                case "close": rearm = true; Hide(); return;
            }
            if (cmd.StartsWith("set:", StringComparison.Ordinal) || (cmd == "stepmove" && Current == Page.Comfort))
            {
                Command?.Invoke(cmd);
                Show(Page.Comfort);
                return;
            }
            rearm = true;
            Hide();
            Command?.Invoke(cmd);
        }

        public IReadOnlyList<PokeButton> Buttons => buttons;
    }
}
