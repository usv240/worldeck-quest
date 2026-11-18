using System.Collections.Generic;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// No words over other words. After everything has placed its captions for the frame, every caption and button on
    /// show is measured as it falls on the view; a caption that overlaps one that matters more is moved up or down,
    /// just enough to clear it (eased, never by more than a third of the view). Fixed captions (the dock's line, a
    /// menu's title, a card beside its buttons) and buttons never move; the others keep clear of them. Seen on the
    /// headset on Nov 16: a local's hello on top of Juniper's line and the task's line.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class CaptionLayout : MonoBehaviour
    {
        public static CaptionLayout Instance { get; private set; }
        /// <summary>Overlaps that could not be cleared this frame (for tests and the log).</summary>
        public static int Unresolved { get; private set; }
        /// <summary>The most captions moved at once since start (for tests).</summary>
        public static int Moved { get; private set; }
        /// <summary>Pairs of words that still overlapped once laid out, last frame (for tests and the log).</summary>
        public static int LastOverlaps { get; private set; }
        readonly List<Rect> wordBoxes = new List<Rect>();
        readonly List<Caption> wordOwners = new List<Caption>();
        /// <summary>Which words overlapped last frame ("A / B; ..."), for tests and the log.</summary>
        public static string LastOverlapNames { get; private set; } = "";

        public const float Margin = 0.012f;      // of the view's height, between two boxes
        public const float MaxShift = 0.34f;     // never further than this from where its owner put it
        float unresolvedSince = -1f;

        struct Box { public Rect R; public float Z; }
        readonly List<Box> placed = new List<Box>();
        readonly List<Caption> order = new List<Caption>();

        public static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("Caption layout");
            Instance = go.AddComponent<CaptionLayout>();
        }

        void Awake() { if (Instance == null) Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null) return;
            placed.Clear();
            order.Clear();
            // Buttons on show are fixed: words keep clear of them.
            foreach (var b in PokeButton.Live)
            {
                if (b == null || !b.isActiveAndEnabled || b.Back == null || !b.Back.enabled) continue;
                var s = b.Size;
                if (ViewRect(cam, b.transform, s.x, s.y, out Rect br, out float bz)) placed.Add(new Box { R = br, Z = bz });
            }
            foreach (var c in Caption.All) if (c != null && c.OnShow) order.Add(c);
            // Fixed first, then the most important words; each later one keeps clear of all before it.
            order.Sort((a, b) => a.Fixed != b.Fixed ? (a.Fixed ? -1 : 1) : b.Priority.CompareTo(a.Priority));
            int unresolved = 0, moved = 0;
            wordBoxes.Clear(); wordOwners.Clear();
            foreach (var c in order)
            {
                if (!c.MeasurePanel(cam, out Rect r, out float z)) continue;
                if (c.Fixed) { placed.Add(new Box { R = r, Z = z }); wordBoxes.Add(r); wordOwners.Add(c); continue; }
                float shift = 0f;
                if (Hits(r, 0f))
                {
                    if (!Clear(r, out shift)) { unresolved++; shift = c.LayoutShift; }
                }
                // Eased: a quick slide, not a jump (and back when the way is clear).
                c.LayoutShift = Mathf.MoveTowards(c.LayoutShift, shift, Time.unscaledDeltaTime * 4f);
                if (Mathf.Abs(c.LayoutShift) > 0.001f) moved++;
                float perUnit = 2f * z * Mathf.Tan(0.5f * cam.fieldOfView * Mathf.Deg2Rad);
                c.ApplyLayout(cam.transform.up * (c.LayoutShift * perUnit));
                r.y += c.LayoutShift;
                placed.Add(new Box { R = r, Z = z });
                wordBoxes.Add(r); wordOwners.Add(c);
            }
            int overlaps = 0;
            string names = "";
            for (int i = 0; i < wordBoxes.Count; i++)
                for (int j = i + 1; j < wordBoxes.Count; j++)
                    if (wordBoxes[i].Overlaps(wordBoxes[j]))
                    {
                        overlaps++;
                        if (names.Length < 300) names += wordOwners[i].name + (wordOwners[i].Fixed ? "*" : "") + " / " + wordOwners[j].name + (wordOwners[j].Fixed ? "*" : "") + "; ";
                    }
            LastOverlaps = overlaps;
            LastOverlapNames = names;
            Unresolved = unresolved;
            Moved = Mathf.Max(Moved, moved);
            if (unresolved > 0)
            {
                if (unresolvedSince < 0f) unresolvedSince = Time.unscaledTime;
                else if (Time.unscaledTime - unresolvedSince > 0.6f && Trace.Every("caption-overlap", 30f))
                    Trace.Warn("Captions", "words overlap that could not be moved apart", "count", unresolved, "on_show", order.Count);
            }
            else unresolvedSince = -1f;
        }

        bool Hits(Rect r, float dy)
        {
            r.y += dy;
            foreach (var p in placed) if (Overlap(r, p.R)) return true;
            return false;
        }

        static bool Overlap(Rect a, Rect b) =>
            a.xMin < b.xMax + Margin && b.xMin < a.xMax + Margin && a.yMin < b.yMax + Margin && b.yMin < a.yMax + Margin;

        /// <summary>The smallest move up or down that clears every box placed so far.</summary>
        bool Clear(Rect r, out float best)
        {
            float found = 0f;
            float bestAbs = float.MaxValue;
            float outNow = OutOfView(r);
            void Try(float dy)
            {
                if (Mathf.Abs(dy) > MaxShift || Mathf.Abs(dy) >= bestAbs) return;
                var moved = r; moved.y += dy;
                // In view, or at least no further out of it than where its owner put it (words near the top edge
                // could never move before, and stayed on top of others: Mara's line under her chapter title).
                if (OutOfView(moved) > Mathf.Max(0f, outNow) + 0.0001f) return;
                if (!Hits(r, dy)) { found = dy; bestAbs = Mathf.Abs(dy); }
            }
            // Just clear of each box's edge, then a fine search both ways for anything those miss.
            foreach (var p in placed)
            {
                Try(p.R.yMax + Margin - r.yMin + 0.001f);
                Try(p.R.yMin - Margin - r.yMax - 0.001f);
            }
            for (float k = 0.01f; k <= MaxShift && bestAbs == float.MaxValue; k += 0.01f) { Try(-k); Try(k); }
            best = found;
            return bestAbs < float.MaxValue;
        }

        /// <summary>How far a box sticks out of the comfortable middle of the view (0 when inside).</summary>
        static float OutOfView(Rect b) => Mathf.Max(0f, 0.02f - b.yMin) + Mathf.Max(0f, b.yMax - 0.98f);

        /// <summary>Where a flat rectangle (centre, width, height in its own plane) falls on the view; false if behind.</summary>
        public static bool ViewRect(Camera cam, Transform t, float w, float h, out Rect rect, out float depth, Vector3 centre = default)
        {
            rect = default; depth = 0f;
            Vector3 a = cam.WorldToViewportPoint(t.TransformPoint(centre + new Vector3(-w * 0.5f, -h * 0.5f, 0f)));
            Vector3 b = cam.WorldToViewportPoint(t.TransformPoint(centre + new Vector3(w * 0.5f, h * 0.5f, 0f)));
            Vector3 c = cam.WorldToViewportPoint(t.TransformPoint(centre + new Vector3(-w * 0.5f, h * 0.5f, 0f)));
            Vector3 d = cam.WorldToViewportPoint(t.TransformPoint(centre + new Vector3(w * 0.5f, -h * 0.5f, 0f)));
            if (a.z <= 0.05f || b.z <= 0.05f || c.z <= 0.05f || d.z <= 0.05f) return false;
            float x0 = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x)), x1 = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
            float y0 = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y)), y1 = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y));
            rect = Rect.MinMaxRect(x0, y0, x1, y1); depth = (a.z + b.z) * 0.5f;
            return x1 > -0.2f && x0 < 1.2f && y1 > -0.2f && y0 < 1.2f;
        }

        /// <summary>Pairs of words on show that overlap now (for tests).</summary>
        public static int OverlapsNow()
        {
            var cam = Camera.main;
            if (cam == null) return 0;
            var rects = new List<Rect>();
            foreach (var c in Caption.All)
                if (c != null && c.OnShow && c.MeasurePanel(cam, out Rect r, out _)) rects.Add(r);
            int n = 0;
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                    if (rects[i].Overlaps(rects[j])) n++;
            return n;
        }
    }
}
