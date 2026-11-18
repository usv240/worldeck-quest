using System;
using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The buttons that are always there inside a world: Worlds (the map), Next world, My room.
    /// A low bar in front of you, below your line of sight, that follows you (it re-centres when you turn away or
    /// move), so there is always an obvious way on and a way back. Poke, ray, look-and-pinch or eyes-only.
    /// </summary>
    public sealed class Dock : MonoBehaviour
    {
        public Transform Head;
        public Material Panel;
        public Font Font;
        public bool Show;
        [Tooltip("Shift the bar to the side (while the console is on the table).")]
        public bool Aside;
        /// <summary>At camp: the middle of the table's near edge and the direction from it towards you. Null when away.</summary>
        public Vector3? Edge;
        public Vector3 EdgeToYou = Vector3.back;
        /// <summary>One line above the bar: what to do next ("" hides it).</summary>
        public string Next = "";
        /// <summary>The one bright button above the bar for the next step ("" hides it).</summary>
        public string ActionLabel = "";
        /// <summary>Only "My room" shows (a story is running: Worlds and Next would end it).</summary>
        public bool Locked;
        /// <summary>A hand is busy carrying something: presses are ignored (the dock is on the way to the table).</summary>
        public bool Busy;
        Caption nextLine;
        PokeButton action;
        string shownNext = "", shownAction = "";
        public event Action<string> Pressed;

        /// <summary>
        /// One press at a time: a hand sweeping across the dock touches several buttons in a quarter second (seen on
        /// the headset: Next, My room and the action all at once). After any press, the whole dock waits 0.4 s.
        /// </summary>
        public const float Calm = 0.4f;
        float lastPress = -10f;
        bool Accept()
        {
            if (Busy) return false;
            float now = Time.unscaledTime;
            if (now - lastPress < Calm) return false;
            lastPress = now;
            return true;
        }

        Transform root;
        readonly List<PokeButton> buttons = new List<PokeButton>();
        float shown, offSince = -1f;
        bool placed, wasEdge;

        // (No "Previous": it was pressed by accident on the headset (Nov 12) and it leads away from the story; the map has every world.)
        static readonly (string id, string label, string hint)[] Items =
        {
            ("worlds", "Worlds", "All the worlds"), ("next", "Next", "The next world"), ("room", "My room", "End this world, back to your room")
        };

        void Build()
        {
            root = new GameObject("Dock bar").transform;
            root.SetParent(transform, false);
            for (int i = 0; i < Items.Length; i++)
            {
                var it = Items[i];
                var b = PokeButton.Make("dock " + it.id, root, Panel, Font, it.label, new Vector2(0.098f, 0.05f), 13f);
                b.transform.localPosition = new Vector3((i - (Items.Length - 1) * 0.5f) * 0.108f, 0f, 0f);
                b.Hint = it.hint;
                string id = it.id;
                b.Pressed += () => { if (!Accept()) { Trace.Info("Dock", "ignored: too soon after another", "id", id); return; } Trace.Info("Dock", "pressed", "id", id); Pressed?.Invoke(id); };
                buttons.Add(b);
            }
            nextLine = Caption.Make("Dock next", root, Font, Panel, Head, 13f, 0f);
            nextLine.Fixed = true;
            nextLine.FaceHead = false;
            nextLine.LineChars = 34;
            nextLine.transform.localPosition = new Vector3(0f, 0.135f, 0f);
            action = PokeButton.Make("dock do", root, Panel, Font, "", new Vector2(0.2f, 0.052f), 14f);
            action.transform.localPosition = new Vector3(0f, 0.064f, 0f);
            action.Highlight = true;
            action.Pressed += () => { if (!Accept()) { Trace.Info("Dock", "ignored: too soon after another", "id", "do"); return; } Trace.Info("Dock", "pressed", "id", "do", "label", shownAction); Pressed?.Invoke("do"); };
            action.gameObject.SetActive(false);
            root.gameObject.SetActive(false);
        }

        void Place()
        {
            if (Edge.HasValue)
            {
                Vector3 e = Edge.Value + EdgeToYou * 0.045f + Vector3.down * 0.035f;
                root.position = e;
                root.rotation = Quaternion.LookRotation(-EdgeToYou, Vector3.up) * Quaternion.Euler(48f, 0f, 0f);
                placed = true;
                return;
            }
            Vector3 f = Head.forward; f.y = 0f;
            f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            if (Aside) f = Quaternion.Euler(0f, 48f, 0f) * f;
            Vector3 p = Head.position + f * 0.42f + Vector3.down * 0.36f;
            root.position = p;
            root.rotation = Quaternion.LookRotation(p - (Head.position + Vector3.down * 0.05f), Vector3.up);
            placed = true;
        }

        void LateUpdate()
        {
            if (Head == null || Font == null) return;
            if (root == null) Build();
            shown = Mathf.MoveTowards(shown, Show ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            bool on = shown > 0.02f;
            if (root.gameObject.activeSelf != on) { root.gameObject.SetActive(on); if (on) Place(); }
            if (!on) { placed = false; return; }
            root.localScale = Vector3.one * Util.Pop(shown);
            for (int i = 0; i < buttons.Count && i < Items.Length; i++)
                if (buttons[i] != null && Items[i].id != "room" && buttons[i].gameObject.activeSelf == Locked) buttons[i].gameObject.SetActive(!Locked);
            if (Next != shownNext) { shownNext = Next; nextLine.Say(Next, 0f, new Color(1f, 0.9f, 0.7f)); }
            if (ActionLabel != shownAction)
            {
                shownAction = ActionLabel;
                action.gameObject.SetActive(ActionLabel.Length > 0);
                if (ActionLabel.Length > 0) action.SetText(ActionLabel, 14f);
            }
            // Follow: if the bar is out of place (you turned, moved or jumped) for half a second, bring it back.
            Vector3 to = root.position - Head.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z), hf = new Vector3(Head.forward.x, 0f, Head.forward.z);
            float want = Aside ? 48f : 0f;
            float angle = Mathf.Abs(Mathf.DeltaAngle(Vector3.SignedAngle(hf, flat, Vector3.up), want));
            if (Edge.HasValue != wasEdge) { wasEdge = Edge.HasValue; Place(); }
            if (Edge.HasValue) return;
            bool off = !placed || flat.magnitude > 0.7f || flat.magnitude < 0.25f || angle > 55f || Mathf.Abs(to.y + 0.36f) > 0.25f;
            bool busy = false;
            foreach (var b in buttons) if (b != null && b.Hovered) busy = true;
            if (off && !busy) { if (offSince < 0f) offSince = Time.unscaledTime; } else offSince = -1f;
            if (offSince > 0f && Time.unscaledTime - offSince > (flat.magnitude > 1.5f ? 0.05f : 0.5f)) { Place(); offSince = -1f; }
        }
    }
}
