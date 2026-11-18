using System;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// A small "Menu" button on the back of your left wrist, where a watch would be. It shows when you turn
    /// the back of your wrist towards your eyes (like checking the time) and opens the menu when you touch it
    /// with your other index finger, or look at it until the ring fills. Easier to discover than palm-up,
    /// which still works. Hidden while you hold something, while controllers stand in for hands, and when
    /// the owner says no (<see cref="Allowed"/>).
    /// </summary>
    public sealed class WristMenu : MonoBehaviour
    {
        public HandInput Hands;
        public Transform Head;
        public Material Panel;
        public Font Font;
        public bool Allowed = true;
        public Func<int, bool> Busy;
        public event Action Pressed;

        /// <summary>The button must have been showing this long before a touch opens the menu (seconds).</summary>
        public const float MinShown = 0.45f;
        /// <summary>Touches ignored because the button had only just appeared (for tests and the log).</summary>
        public int Ignored { get; private set; }
        public bool Visible { get; private set; }
        public PokeButton Button { get; private set; }
        public Vector3 Spot { get; private set; }
        float shown, visibleSince = -10f;
        int seen;
        bool facing;

        void Start()
        {
            if (Font == null || Panel == null) return;
            Button = PokeButton.Make("wrist menu", transform, Panel, Font, "Menu", new Vector2(0.048f, 0.032f), 13f);
            Button.Hint = "Opens the menu";
            Button.DwellScale = 1.5f;
            Button.Pressed += () =>
            {
                // A hand passing the wrist as the button appears is not a press: it has to have been seen first.
                if (Time.unscaledTime - visibleSince < MinShown) { Ignored++; Trace.Info("Wrist", "press ignored: the button had only just appeared"); return; }
                Trace.Info("Wrist", "menu button pressed");
                Pressed?.Invoke();
            };
            Button.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (Button == null || Hands == null || Head == null) return;
            var s = Hands[0];
            bool ok = Allowed && s.Tracked && !s.Controller && (Busy == null || !Busy(0));
            Vector3 back = -s.PalmNormal;
            // Along the hand from the wrist joint to the palm (independent of any joint's axis convention).
            Vector3 fingers = s.Palm - s.Wrist;
            if (fingers.sqrMagnitude < 1e-6f) fingers = s.PalmRot * Vector3.forward;
            fingers = Vector3.ProjectOnPlane(fingers, back).normalized;
            Vector3 wrist = s.Wrist + back * 0.022f + fingers * 0.004f;
            if (ok)
            {
                Vector3 toEye = Head.position - wrist;
                float dot = Vector3.Dot(back.normalized, toEye.normalized);
                facing = facing ? dot > 0.32f : dot > 0.6f;
                ok = toEye.magnitude < 0.75f && facing;
                if (!ok && Visible && Time.unscaledTime - visibleSince < 0.6f && s.Tracked) ok = true;
            }
            shown = Mathf.MoveTowards(shown, ok ? 1f : 0f, Time.unscaledDeltaTime * (ok ? 5f : 8f));
            bool visible = shown > 0.02f;
            if (visible != Visible)
            {
                Visible = visible;
                if (visible) visibleSince = Time.unscaledTime;
                if (visible && seen++ < 3) Trace.Info("Wrist", "menu button shown");
            }
            Button.gameObject.SetActive(visible);
            if (!visible) return;
            Spot = wrist + back * 0.004f;
            Button.transform.position = Spot;
            Vector3 up = Vector3.ProjectOnPlane(Head.up, back);
            if (up.sqrMagnitude < 1e-4f) up = fingers;
            Button.transform.rotation = Quaternion.LookRotation(-back, up);
            Button.transform.localScale = Vector3.one * Util.Pop(shown);
        }
    }
}
