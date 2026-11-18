using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Which button does what, on the controllers themselves: when controllers are picked up instead of hands,
    /// a label floats over each one for a few seconds ("Trigger: press and pick up", "Menu button: the menu").
    /// It comes back with the "How it works" card (<see cref="ShowNow"/>).
    /// </summary>
    public sealed class ControllerLabels : MonoBehaviour
    {
        public HandInput Hands;
        public Transform Head;
        public Font Font;
        public Material Panel;
        public float ShowSeconds = 14f;

        public const string LeftText = "Stick forward: go where you look\nStick left/right: turn\nMenu button: the menu";
        public const string RightText = "Stick up: aim a jump, let go\nB or Y: all the worlds\nSet it down to use hands";

        Caption left, right;
        float until = -1f;
        bool was;

        public bool Showing => (left != null && left.Showing) || (right != null && right.Showing);

        void Start()
        {
            if (Font == null) return;
            left = Caption.Make("Controller label L", transform, Font, Panel, Head, 12f, 20f);
            right = Caption.Make("Controller label R", transform, Font, Panel, Head, 12f, 20f);
            left.LineChars = right.LineChars = 28;
            left.Fixed = right.Fixed = true;
        }

        public void ShowNow(float seconds = 10f) => until = Time.unscaledTime + seconds;

        void LateUpdate()
        {
            if (Hands == null || left == null) return;
            bool any = (Hands[0].Tracked && Hands[0].Controller) || (Hands[1].Tracked && Hands[1].Controller);
            if (any && !was)
            {
                until = Mathf.Max(until, Time.unscaledTime + ShowSeconds);
                Trace.Info("Controllers", "in use: showing what the buttons do");
            }
            was = any;
            bool show = any && Time.unscaledTime < until;
            Place(left, 0, show, LeftText);
            Place(right, 1, show, RightText);
        }

        void Place(Caption c, int i, bool show, string text)
        {
            var s = Hands[i];
            if (!show || !s.Tracked || !s.Controller) { if (c.Showing) c.Clear(); return; }
            if (!c.Showing) c.Say(text, 0f);
            Vector3 toEye = Head != null ? (Head.position - s.Palm).normalized : Vector3.back;
            c.transform.position = s.Palm + Vector3.up * 0.075f + toEye * 0.02f;
        }
    }
}
