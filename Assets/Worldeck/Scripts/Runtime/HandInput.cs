using Oculus.Interaction.Input;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Both hands as the app needs them, whatever is driving them: tracked hands (Interaction SDK joints and
    /// pinch), controllers held instead (grip position, trigger = pinch, so nobody is stuck if they kept
    /// them), or a simulated hand in tests and the editor. One place, so every interaction treats them alike.
    /// </summary>
    public sealed class HandInput : MonoBehaviour
    {
        public Hand Left, Right;
        [Tooltip("The OVRHand of each side: only to see when Quest's own system gesture is in progress.")]
        public OVRHand LeftSystem, RightSystem;
        public Transform LeftController, RightController;
        public Transform Head;
        [Header("Tests and editor")]
        public Transform SimulatedHand;
        public bool SimulatedPinch;
        public bool SimulatedPalmUp;
        /// <summary>The simulated hand is closed in a whole-hand grip (not a pinch).</summary>
        public bool SimulatedFist;
        /// <summary>The simulated hand is held up, palm away from you (a wave).</summary>
        public bool SimulatedPalmOut;

        public struct State
        {
            public bool Tracked;
            /// <summary>True for a controller standing in for this hand.</summary>
            public bool Controller;
            /// <summary>Between thumb and index tips (where a pinch holds things).</summary>
            public Vector3 Pinch;
            public float PinchStrength;
            public bool Pinching;
            public Vector3 IndexTip;
            public Vector3 Palm;
            /// <summary>Palm normal (out of the palm), world.</summary>
            public Vector3 PalmNormal;
            /// <summary>Palm orientation: fingers along +Z, back of the hand +Y (the Interaction SDK convention).</summary>
            public Quaternion PalmRot;
            /// <summary>The wrist joint (the base of the hand).</summary>
            public Vector3 Wrist;
            /// <summary>Controllers: where the controller points (for its ray). Zero for hands.</summary>
            public Vector3 Aim;
            /// <summary>Palm facing up and towards the eyes, fingers open: the panel gesture.</summary>
            public bool PalmUp;
            /// <summary>Whole hand closed (a palm grab).</summary>
            public bool Fist;
            /// <summary>The middle and ring fingers are curled in (round a thing, or into a fist), whatever the index and thumb do.</summary>
            public bool Grip;
            /// <summary>How open those fingers are: fingertip-to-wrist over knuckle-to-wrist, about 1.9 straight, 1.6 relaxed, 1.3 round a ball, 0.9 a fist (0: not known).</summary>
            public float Curl;
            /// <summary>The index finger is out straight while the others are curled in: pointing or poking, not grabbing.</summary>
            public bool Pointing;
        }

        public const int SimulatedIndex = 2;
        readonly State[] states = new State[3];
        readonly bool[] wasPinching = new bool[3];
        readonly bool[] wasGrip = new bool[3];
        /// <summary>Fingers curled this far in start a whole-hand grip; it holds until they open past <see cref="GripOpen"/>.</summary>
        public const float GripClose = 1.4f, GripOpen = 1.55f;   // 1.62 was past a relaxed hand (1.6): a thing could stay stuck in it
        readonly float[] palmUpSince = new float[3];

        /// <summary>0 left, 1 right, 2 simulated.</summary>
        public State this[int i] => states[i];
        public int Count => 3;
        /// <summary>A pinch started this frame on hand i.</summary>
        public bool PinchDown(int i) => states[i].Pinching && !wasPinching[i];
        public bool PinchUp(int i) => !states[i].Pinching && wasPinching[i];
        /// <summary>Seconds the palm has been up (0 if not).</summary>
        public float PalmUpFor(int i) => states[i].PalmUp ? Time.unscaledTime - palmUpSince[i] : 0f;
        public bool AnyTracked => states[0].Tracked || states[1].Tracked || states[2].Tracked;
        public bool AnyHandTracked => (states[0].Tracked && !states[0].Controller) || (states[1].Tracked && !states[1].Controller);

        // Every real (not synthetic) Interaction SDK hand per side: the rig has several (data, filtered,
        // controller-driven), and the first one found was not always the one that moves (Nov 11 device run:
        // no hand tracked for a whole session). Each frame the first valid one is used; if none is, the plain
        // OVRHand + OVRSkeleton of the same side is read instead.
        readonly System.Collections.Generic.List<Hand> lefts = new System.Collections.Generic.List<Hand>();
        readonly System.Collections.Generic.List<Hand> rights = new System.Collections.Generic.List<Hand>();
        readonly string[] source = { "-", "-", "-" };
        public string Source(int i) => source[i];

        void Start()
        {
            foreach (var h in FindObjectsByType<Hand>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string type = h.GetType().Name;
                if (type.Contains("Synthetic")) continue;
                // A hand made from a prefab pose (Interaction SDK's touch-grab sphere map) is not a live hand: both
                // rigs carry one marked "right", and reading it froze the right hand (Nov 12 headset log).
                if (h.name.Contains("Sphere") || h.GetComponent("FromHandPrefabDataSource") != null) continue;
                if (h.Handedness == Handedness.Left) lefts.Add(h); else if (h.Handedness == Handedness.Right) rights.Add(h);
            }
            // Prefer the raw data hands (no filters, no controller-driven hands) first.
            System.Comparison<Hand> order = (a, b) => Rank(a).CompareTo(Rank(b));
            lefts.Sort(order); rights.Sort(order);
            if (Left != null && !lefts.Contains(Left)) lefts.Insert(0, Left);
            if (Right != null && !rights.Contains(Right)) rights.Insert(0, Right);
            Trace.Info("Hands", "sources", "left", Names(lefts), "right", Names(rights),
                       "ovr_left", LeftSystem != null, "ovr_right", RightSystem != null, "hand_tracking_enabled", OVRPlugin.GetHandTrackingEnabled());
        }

        static int Rank(Hand h)
        {
            string n = (h.name + " " + h.GetType().Name).ToLowerInvariant();
            if (n.Contains("controller")) return 5;
            if (n.Contains("filter") || n.Contains("history") || n.Contains("lastknown") || n.Contains("fixedscale")) return 3;
            if (n.Contains("comprehensive")) return 0;
            if (n.Contains("data")) return 0;
            return 1;
        }

        static string Names(System.Collections.Generic.List<Hand> hs)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var h in hs) sb.Append(h.name).Append(':').Append(h.GetType().Name).Append(h.isActiveAndEnabled ? "" : "(off)").Append(' ');
            return sb.ToString().Trim();
        }

        Hand Pick(System.Collections.Generic.List<Hand> hs)
        {
            foreach (var h in hs) if (h != null && h.isActiveAndEnabled && h.IsTrackedDataValid) return h;
            return null;
        }

        void Update() => Sample();

        /// <summary>Reads all hands now (also called by tests after moving the simulated hand).</summary>
        public void Sample()
        {
            for (int i = 0; i < 3; i++) { wasPinching[i] = states[i].Pinching; wasGrip[i] = states[i].Grip; }
            states[0] = Read(Pick(lefts), LeftSystem, LeftController, OVRInput.Controller.LTouch, 0);
            states[1] = Read(Pick(rights), RightSystem, RightController, OVRInput.Controller.RTouch, 1);
            for (int i = 0; i < 2; i++)
            {
                string now = states[i].Tracked ? source[i] : "-";
                if (now != lastSource[i]) { Trace.Info("Hands", (i == 0 ? "left" : "right") + " hand source", "now", now, "was", lastSource[i]); lastSource[i] = now; }
            }
            // Quest's system gesture (palm towards you, pinch for the Meta menu) is reserved: while it is in
            // progress that hand neither pinches nor opens the panel here (VRC.Quest.Input.8).
            if (LeftSystem != null && LeftSystem.IsSystemGestureInProgress) { states[0].Pinching = false; states[0].PalmUp = false; states[0].PinchStrength = 0f; }
            if (RightSystem != null && RightSystem.IsSystemGestureInProgress) { states[1].Pinching = false; states[1].PalmUp = false; states[1].PinchStrength = 0f; }
            states[2] = Simulated();
            for (int i = 0; i < 3; i++)
            {
                if (states[i].PalmUp && palmUpSince[i] <= 0f) palmUpSince[i] = Time.unscaledTime;
                if (!states[i].PalmUp) palmUpSince[i] = 0f;
            }
        }

        readonly string[] lastSource = { "-", "-", "-" };
        readonly bool[] palmChecked = new bool[2];
        readonly OVRSkeleton[] skeletons = new OVRSkeleton[2];
        readonly Transform[][] bones = new Transform[2][];   // index tip, thumb tip, palm

        State Read(Hand hand, OVRHand ovr, Transform controller, OVRInput.Controller which, int index)
        {
            var s = new State();
            if (hand != null)
            {
                source[index] = "isdk:" + hand.name;
                s.Tracked = true;
                bool thumb = hand.GetJointPose(HandJointId.HandThumbTip, out var t);
                bool tip = hand.GetJointPose(HandJointId.HandIndexTip, out var it);
                if (tip) s.IndexTip = it.position;
                s.Pinch = thumb && tip ? (t.position + it.position) * 0.5f : s.IndexTip;
                s.PinchStrength = hand.GetFingerPinchStrength(HandFinger.Index);
                // Hysteresis: start at 0.85, hold down to 0.55 (no flicker at the threshold).
                s.Pinching = wasPinching[index] ? s.PinchStrength > 0.55f : s.PinchStrength > 0.85f;
                if (hand.GetJointPose(HandJointId.HandPalm, out var p))
                {
                    s.Palm = p.position;
                    // OpenXR hand convention in the Interaction SDK: palm faces local -Y, fingers +Z.
                    s.PalmNormal = p.rotation * Vector3.down;
                    s.PalmRot = p.rotation;
                    s.Wrist = hand.GetJointPose(HandJointId.HandWristRoot, out var wr) ? wr.position : p.position - p.rotation * Vector3.forward * 0.06f;
                    // The palm's facing from the knuckles themselves (index and little knuckle around the wrist),
                    // so it doesn't depend on any joint's axis convention; the pose's own axis is the fallback.
                    if (hand.GetJointPose(HandJointId.HandIndex1, out var i1) && hand.GetJointPose(HandJointId.HandPinky1, out var p1))
                    {
                        Vector3 geo = Vector3.Cross(i1.position - s.Wrist, p1.position - s.Wrist);
                        if (geo.sqrMagnitude > 1e-8f)
                        {
                            geo = geo.normalized * (index == 0 ? 1f : -1f);
                            if (!palmChecked[index])
                            {
                                palmChecked[index] = true;
                                Trace.Info("Hands", "palm facing check", "side", index == 0 ? "left" : "right", "agree", Vector3.Dot(geo, s.PalmNormal));
                            }
                            s.PalmNormal = geo;
                        }
                    }
                    Vector3 toEye = Head != null ? (Head.position - p.position).normalized : Vector3.up;
                    // Palm facing the ceiling (not the face: facing the eyes is Quest's system gesture), fingers open.
                    s.PalmUp = Vector3.Dot(s.PalmNormal, Vector3.up) > 0.72f && Vector3.Dot(s.PalmNormal, toEye) > 0.1f &&
                               s.PinchStrength < 0.3f;
                }
                bool middle = hand.GetFingerIsPinching(HandFinger.Middle), ring = hand.GetFingerIsPinching(HandFinger.Ring);
                // A whole-hand grip: the middle and ring fingers curled in, as they are round an animal or in a fist.
                // People close their hand round a thing more often than they pinch it.
                s.Curl = 0f;
                if (hand.GetJointPose(HandJointId.HandWristRoot, out var w0) &&
                    hand.GetJointPose(HandJointId.HandMiddle1, out var m1) && hand.GetJointPose(HandJointId.HandMiddleTip, out var mt) &&
                    hand.GetJointPose(HandJointId.HandRing1, out var r1) && hand.GetJointPose(HandJointId.HandRingTip, out var rt))
                {
                    float mk = Vector3.Distance(m1.position, w0.position), rk = Vector3.Distance(r1.position, w0.position);
                    if (mk > 0.02f && rk > 0.02f)
                    {
                        float mc = Vector3.Distance(mt.position, w0.position) / mk, rc = Vector3.Distance(rt.position, w0.position) / rk;
                        s.Curl = (mc + rc) * 0.5f;
                        // Starts when both fingers are well curled in; holds on their average, so one finger's tracking
                        // noise near the line neither drops the thing nor keeps it stuck (a hand round a big animal is about 1.45).
                        s.Grip = wasGrip[index] ? s.Curl < GripOpen : mc < GripClose && rc < GripClose;
                        // A hand curled to point or poke (index out straight) is not a whole-hand grab.
                        if (tip && s.Grip && hand.GetJointPose(HandJointId.HandIndex1, out var x1))
                        {
                            float ik = Vector3.Distance(x1.position, w0.position);
                            if (ik > 0.02f) s.Pointing = Vector3.Distance(it.position, w0.position) / ik > 1.65f;
                        }
                    }
                }
                if (middle && ring) s.Grip = true;
                s.Fist = !s.Pinching && s.Grip;
                return s;
            }
            if (ovr != null && ovr.IsTracked && ReadOvr(ovr, index, ref s))
            {
                source[index] = "ovrhand";
                return s;
            }
            if (controller != null && OVRInput.IsControllerConnected(which) &&
                (OVRInput.GetControllerPositionTracked(which) || OVRInput.GetControllerOrientationTracked(which)))
            {
                s.Tracked = true;
                s.Controller = true;
                source[index] = "controller";
                // The controller's tip: a little forward of the grip, where a finger would be.
                Vector3 tipPos = controller.position + controller.forward * 0.06f;
                s.Pinch = tipPos; s.IndexTip = tipPos; s.Palm = controller.position;
                s.PalmNormal = controller.up;
                s.PalmRot = controller.rotation;
                s.Wrist = controller.position;
                s.Aim = controller.forward;
                float trigger = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, which);
                float grip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, which);
                s.PinchStrength = Mathf.Max(trigger, grip);
                s.Pinching = wasPinching[index] ? s.PinchStrength > 0.35f : s.PinchStrength > 0.6f;
                // The menu button opens the panel when holding controllers (palm-up isn't natural with them).
                s.PalmUp = OVRInput.Get(OVRInput.Button.Start, OVRInput.Controller.LTouch) && index == 0;
                return s;
            }
            return s;
        }

        /// <summary>The second source: Meta's own OVRHand and its skeleton (bone names differ between skeleton types).</summary>
        bool ReadOvr(OVRHand ovr, int index, ref State s)
        {
            if (skeletons[index] == null) skeletons[index] = ovr.GetComponent<OVRSkeleton>();
            var sk = skeletons[index];
            if (sk == null || !sk.IsInitialized || !sk.IsDataValid || sk.Bones == null) return false;
            if (bones[index] == null || bones[index][0] == null)
            {
                var b = new Transform[4];
                foreach (var bone in sk.Bones)
                {
                    if (bone == null || bone.Transform == null) continue;
                    string n = bone.Id.ToString();
                    if (n.EndsWith("IndexTip")) b[0] = bone.Transform;
                    else if (n.EndsWith("ThumbTip")) b[1] = bone.Transform;
                    else if (n.EndsWith("Palm") || (b[2] == null && n.EndsWith("WristRoot"))) b[2] = bone.Transform;
                    if (n.EndsWith("WristRoot")) b[3] = bone.Transform;
                }
                if (b[0] == null) return false;
                bones[index] = b;
                Trace.Info("Hands", "ovr skeleton bones found", "side", index == 0 ? "left" : "right", "thumb", b[1] != null, "palm", b[2] != null);
            }
            var t = bones[index];
            s.Tracked = true;
            s.IndexTip = t[0].position;
            s.Pinch = t[1] != null ? (t[0].position + t[1].position) * 0.5f : s.IndexTip;
            s.PinchStrength = ovr.GetFingerPinchStrength(OVRHand.HandFinger.Index);
            s.Pinching = wasPinching[index] ? s.PinchStrength > 0.55f : s.PinchStrength > 0.85f;
            if (t[2] != null)
            {
                s.Palm = t[2].position;
                s.Wrist = t.Length > 3 && t[3] != null ? t[3].position : t[2].position;
                // Palm normal: across the hand from the index tip and thumb (a cheap, side-aware estimate).
                Vector3 fingers = (t[0].position - t[2].position).normalized;
                Vector3 thumbSide = t[1] != null ? (t[1].position - t[2].position).normalized : Vector3.right;
                Vector3 n = Vector3.Cross(fingers, thumbSide).normalized * (index == 0 ? -1f : 1f);
                s.PalmNormal = n;
                if (fingers.sqrMagnitude > 1e-6f && n.sqrMagnitude > 1e-6f) s.PalmRot = Quaternion.LookRotation(fingers, -n);
                Vector3 toEye = Head != null ? (Head.position - s.Palm).normalized : Vector3.up;
                s.PalmUp = Vector3.Dot(n, Vector3.up) > 0.72f && Vector3.Dot(n, toEye) > 0.1f && s.PinchStrength < 0.3f;
            }
            return true;
        }

        State Simulated()
        {
            var s = new State();
            if (SimulatedHand == null || !SimulatedHand.gameObject.activeInHierarchy) return s;
            s.Tracked = true;
            s.Pinch = s.IndexTip = s.Palm = SimulatedHand.position;
            Vector3 outward = Head != null ? new Vector3(Head.forward.x, 0f, Head.forward.z).normalized : Vector3.forward;
            s.PalmNormal = SimulatedPalmUp ? Vector3.up : SimulatedPalmOut ? outward : Vector3.down;
            s.PalmRot = SimulatedHand.rotation;
            s.Wrist = SimulatedHand.position - SimulatedHand.forward * 0.06f;
            s.Pinching = SimulatedPinch;
            s.PinchStrength = SimulatedPinch ? 1f : 0f;
            s.PalmUp = SimulatedPalmUp;
            s.Grip = SimulatedFist;
            s.Curl = SimulatedFist ? 1.1f : 1.9f;
            s.Fist = SimulatedFist && !SimulatedPinch;
            return s;
        }
    }
}
