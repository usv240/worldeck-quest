using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Keeps the player safe and the app honest. Safety: while a world is open, standing up (head 30 cm above
    /// where it settled), moving 60 cm sideways, or losing head tracking fades the world back to the real room
    /// until you're seated again. Health: head tracking, hands, frame rate (a world that can't hold its frame
    /// rate gets lighter: fewer props and particles), reported to the Health registry and the log.
    /// </summary>
    public sealed class Watch : MonoBehaviour
    {
        public Transform Head;
        public HandInput Hands;
        [Tooltip("The camera rig: the safety check works in tracking space, so moving through a world never looks like standing up.")]
        public Transform Rig;
        [Tooltip("Worlds you can walk in: standing and walking are fine; only real walls and tall furniture nearby (and lost tracking) bring the room back.")]
        public bool AllowWalking;
        public float NearWall = 0.45f;
        public float StandRise = 0.30f;
        public float Wander = 0.6f;

        public bool Unsafe { get; private set; }
        public string Why { get; private set; } = "";
        public bool Armed { get; private set; }
        /// <summary>Raised when the world should get lighter (sustained low frame rate).</summary>
        public System.Action Lighten;

        Vector3 seat;
        float settleUntil, baseHeight;
        float headLostSince = -1f, slowSince = -1f, lastLighten = -100f;
        readonly float[] recent = new float[90];
        int ri;
        bool handsSeen;
        float lastResume = -10f;

        void OnApplicationPause(bool paused) { lastResume = Time.unscaledTime; if (!paused) Rebase(); }
        void OnApplicationFocus(bool focus) { lastResume = Time.unscaledTime; if (focus) Rebase(); }
        void OnEnable() { OVRManager.HMDMounted += Rebase; }
        void OnDisable() { OVRManager.HMDMounted -= Rebase; }

        /// <summary>Takes where the head is now as the seat again (after the headset was off, paused or refocused).</summary>
        Vector3 HeadLocal => Rig != null ? Rig.InverseTransformPoint(Head.position) : Head.position;
        readonly System.Collections.Generic.List<(Vector3 a, Vector3 b, Vector3 n)> walls = new System.Collections.Generic.List<(Vector3, Vector3, Vector3)>();
        readonly System.Collections.Generic.List<(Vector3 c, Vector3 half, Quaternion inv)> tall = new System.Collections.Generic.List<(Vector3, Vector3, Quaternion)>();

        /// <summary>The real room's walls and tall furniture, in tracking space (call with the rig at home).</summary>
        public void SetObstacles(RoomModel room)
        {
            walls.Clear(); tall.Clear();
            if (room == null || !room.Scanned) return;
            System.Func<Vector3, Vector3> L = p => Rig != null ? Rig.InverseTransformPoint(p) : p;
            Quaternion rigInv = Rig != null ? Quaternion.Inverse(Rig.rotation) : Quaternion.identity;
            foreach (var w in room.Walls) walls.Add((L(w.A), L(w.B), rigInv * w.Normal));
            foreach (var p in room.Pieces)
            {
                if (p.Virtual || p == room.Table || p == room.Seat || p.Size.y < 0.9f) continue;
                tall.Add((L(p.Center), p.Size * 0.5f, Quaternion.Inverse(rigInv * p.Rotation)));
            }
        }

        /// <summary>Distance from a tracking-space point to the nearest real wall or tall piece (horizontal).</summary>
        public float Nearest(Vector3 local)
        {
            float best = float.MaxValue;
            foreach (var w in walls)
            {
                Vector3 ab = w.b - w.a; ab.y = 0f;
                Vector3 ap = local - w.a; ap.y = 0f;
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, (ap - ab * t).magnitude);
            }
            foreach (var p in tall)
            {
                Vector3 q = p.inv * (local - p.c);
                float dx = Mathf.Max(0f, Mathf.Abs(q.x) - p.half.x), dz = Mathf.Max(0f, Mathf.Abs(q.z) - p.half.z);
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }
            return best;
        }

        public void Rebase()
        {
            if (!Armed || Head == null) return;
            seat = HeadLocal;
            // Never higher than a little above where the player sat when the world opened: re-seating while
            // standing must not switch the stand-up check off.
            baseHeight = Mathf.Min(HeadLocal.y, seatedY + StandRise * 0.5f);
            rebasedAt = Time.unscaledTime;
            settleUntil = Time.unscaledTime + 2f;
            Unsafe = false; Why = "";
            unsafeSince = -1f;
        }
        float unsafeSince = -1f, stillSince = -1f;
        float seatedY, rebasedAt;
        Vector3 lastHead;

        /// <summary>Starts watching from where the player sits now (they have 1.5 s to settle).</summary>
        public void Arm()
        {
            if (Head == null) return;
            seat = HeadLocal;
            baseHeight = seat.y;
            seatedY = seat.y;
            rebasedAt = Time.unscaledTime;
            settleUntil = Time.unscaledTime + 1.5f;
            Armed = true;
            Unsafe = false;
            Why = "";
        }

        public void Disarm() { Armed = false; Unsafe = false; Why = ""; }

        void Update()
        {
            if (Head == null) return;
            float now = Time.unscaledTime;

            // Head tracking.
            bool tracked = OVRManager.instance == null || OVRPlugin.GetNodePositionTracked(OVRPlugin.Node.Head) || Application.isEditor;
            if (!tracked) { if (headLostSince < 0f) headLostSince = now; }
            else headLostSince = -1f;
            bool headLost = headLostSince > 0f && now - headLostSince > 0.5f && now > 4f;   // tracking starts after launch
            Health.Set(Health.Head, headLost ? Health.Level.Fault : Health.Level.Ok, headLost ? "lost" : "tracked", now,
                       headLost ? "Head tracking lost. Your room is back." : "");

            // Hands.
            if (Hands != null)
            {
                bool any = Hands.AnyTracked;
                if (any) handsSeen = true;
                Health.Set(Health.Hands, any || !handsSeen ? Health.Level.Ok : Health.Level.Fallback,
                           any ? (Hands.AnyHandTracked ? "hands" : "controllers") : "none in view", now);
            }

            // Frame rate: mean over the last 90 frames against the display rate. Frames around a pause, a focus
            // change or a permission prompt are not the app's frame rate (Nov 11 device run: a false alarm).
            float frame = Time.unscaledDeltaTime;
            if (frame > 0.25f || now - lastResume < 3f) { for (int i = 0; i < recent.Length; i++) recent[i] = 1f / 72f; frame = 1f / 72f; }
            recent[ri] = frame;
            ri = (ri + 1) % recent.Length;
            float sum = 0f;
            foreach (var d in recent) sum += d;
            float fps = sum > 0f ? recent.Length / sum : 0f;
            float hz = OVRManager.display != null ? OVRManager.display.displayFrequency : 72f;
            bool slow = Time.frameCount > 200 && fps < hz * 0.82f && Time.timeScale > 0f && Application.isFocused;
            if (slow) { if (slowSince < 0f) slowSince = now; } else slowSince = -1f;
            bool sustained = slowSince > 0f && now - slowSince > 2.5f;
            // The detail changes only with the level (the live fps flipped 71/72 and wrote a record each time; it is in the heartbeat).
            Health.Set(Health.Frames, sustained ? Health.Level.Fallback : Health.Level.Ok, sustained ? $"under {hz * 0.82f:0} fps" : $"{hz:0} Hz", now);
            if (sustained && now - lastLighten > 8f) { lastLighten = now; Lighten?.Invoke(); }

            // Safety. Nothing counts while the headset is off the face or the head isn't tracked: those poses jump.
            if (!Armed) return;
            if (OVRManager.instance != null && !OVRManager.isHmdPresent) return;
            if (headLost) { stillSince = -1f; return; }
            Vector3 head = HeadLocal;
            if (now < settleUntil) { baseHeight = Mathf.Min(Mathf.Lerp(baseHeight, head.y, 0.1f), head.y + 0.05f); seat = Vector3.Lerp(seat, head, 0.1f); return; }
            Vector3 d3 = head - seat;
            float side = new Vector2(d3.x, d3.z).magnitude;
            bool stood = head.y - baseHeight > StandRise;
            bool wandered = side > Wander;
            float near = AllowWalking ? Nearest(head) : float.MaxValue;
            // Seated people sit close to walls all the time: a wall only counts once you're up and about.
            bool nearWall = near < NearWall && (stood || wandered);
            bool unsafeNow = AllowWalking ? (nearWall || headLost) : (stood || wandered || headLost);
            // Back to safe only when clearly seated again (hysteresis).
            bool clear = AllowWalking ? (near > NearWall + 0.2f || (!stood && !wandered)) : head.y - baseHeight < StandRise * 0.5f && side < Wander * 0.6f;
            if (Unsafe && !headLost && clear) { Unsafe = false; Why = ""; unsafeSince = -1f; }
            else if (!Unsafe && unsafeNow)
            {
                Unsafe = true;
                unsafeSince = now;
                Why = headLost ? "tracking" : nearWall ? "near a wall" : stood ? "stood up" : "moved";
                Debug.Log($"[Worldeck] safety: {Why} (head {Head.position.y - baseHeight:+0.00} m above the seat, {side:0.00} m aside)");
            }
            // A wrong baseline (the seat was recorded while the headset was being put on) would keep the world
            // away for good. If the head has been still for 3 s while "unsafe" for over 8 s, this is the seat.
            float moved = (head - lastHead).magnitude / Mathf.Max(1e-4f, Time.unscaledDeltaTime);
            lastHead = head;
            if (Unsafe && moved < 0.08f) { if (stillSince < 0f) stillSince = now; } else stillSince = -1f;
            // Only soon after arming or re-seating (when a wrong baseline is likely), never as a way to stay standing.
            if (Unsafe && unsafeSince > 0f && now - unsafeSince > 8f && stillSince > 0f && now - stillSince > 3f && now - rebasedAt < 25f && !AllowWalking)
            {
                Debug.Log("[Worldeck] safety: still for 3 s, taking this as the seat");
                Rebase();
            }
        }
    }
}
