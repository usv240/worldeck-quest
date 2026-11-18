using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.MRUtilityKit;
using NUnit.Framework;
using Worldeck.Core;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Worldeck.PlayTests
{
    /// <summary>
    /// A player agent, the impatient first-timer ("masher"): presses whatever is on show, pinches, fists, waves, palm up
    /// mid-task, says nonsense, changes worlds, goes home; ~150 s per seed. Oracles in Check().
    /// </summary>
    public class MasherPlayTests
    {
        const string Scene = "Assets/Worldeck/Scenes/Worldeck.unity";
        Director d;
        HandInput hands;
        Transform hand;
        readonly System.Collections.Generic.List<string> ours = new System.Collections.Generic.List<string>();

        void OnLog(string message, string stack, LogType type)
        {
            if ((type == LogType.Error || type == LogType.Exception || type == LogType.Assert) && (stack ?? "").Contains("Worldeck."))
                ours.Add(message + " @ " + (stack ?? "").Split('\n').FirstOrDefault(l => l.Contains("Worldeck.")));
        }

        [UnityTearDown]
        public IEnumerator Done()
        {
            Application.logMessageReceived -= OnLog;
            Time.timeScale = 1f;
            ActAi.Canned = null;
            if (hands != null) { hands.SimulatedPinch = false; hands.SimulatedFist = false; hands.SimulatedPalmUp = false; hands.SimulatedPalmOut = false; }
            Assert.IsEmpty(ours, "errors from our code (seed " + seed + "): " + string.Join(" | ", ours.Take(5)) + "\nlast actions:\n  " + Trail());
            yield return null;
        }

        [UnitySetUp]
        public IEnumerator Load()
        {
            // Errors from the SDKs in the editor (no headset) are expected; errors from our own code are not.
            LogAssert.ignoreFailingMessages = true;
            ours.Clear();
            Application.logMessageReceived += OnLog;
            PlayerPrefs.DeleteAll();
            var progress = System.IO.Path.Combine(Application.persistentDataPath, "worldeck-progress.json");
            if (System.IO.File.Exists(progress)) System.IO.File.Delete(progress);
            if (System.IO.File.Exists(progress + ".tmp")) System.IO.File.Delete(progress + ".tmp");
#if UNITY_EDITOR
            yield return UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(Scene, new LoadSceneParameters(LoadSceneMode.Single));
#else
            yield return null;
#endif
            for (int i = 0; i < 5; i++) yield return null;
            d = Object.FindFirstObjectByType<Director>();
            Assert.NotNull(d, "director");
            // No headset in the editor: the test moves the head itself.
            var rig = Object.FindFirstObjectByType<OVRCameraRig>();
            if (rig != null) rig.enabled = false;
            hands = d.Hands;
            hand = new GameObject("Simulated hand").transform;
            hands.SimulatedHand = hand;
            // A real sample room, as if scanned.
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Packages/com.meta.xr.mrutilitykit/Core/Rooms/Prefabs/LivingRoom/LivingRoom00.prefab");
            var task = MRUK.Instance.LoadSceneFromPrefab(prefab);
            float until = Time.realtimeSinceStartup + 20f;
            while (!task.IsCompleted && Time.realtimeSinceStartup < until) yield return null;
#endif
            var room = MRUK.Instance.GetCurrentRoom();
            Assert.NotNull(room, "sample room loaded");
            // Sit at the room's first table, facing it.
            var table = room.Anchors.First(a => a.HasAnyLabel(MRUKAnchor.SceneLabels.TABLE) && a.VolumeBounds.HasValue);
            var b = table.VolumeBounds.Value;
            Vector3 c = table.transform.TransformPoint(b.center);
            float top = c.y + b.size.z * 0.5f;
            Vector3 right = new Vector3(table.transform.right.x, 0, table.transform.right.z).normalized;
            Vector3 back = Vector3.Cross(right, Vector3.up);
            Vector3 head = new Vector3(c.x, top + 0.45f, c.z) - back * (b.size.y * 0.5f + 0.45f);
            if (room.IsPositionInSceneVolume(head, true, 0f)) head = new Vector3(c.x, top + 0.45f, c.z) + back * (b.size.y * 0.5f + 0.45f);
            d.Head.position = head;
            d.Head.rotation = Quaternion.LookRotation(new Vector3(c.x - head.x, -0.4f, c.z - head.z));
            d.Rooms.UseRoom(room);
            yield return null;
            Assert.IsFalse(d.Rooms.Room.Table.Virtual, "a real table to work at");
        }

        // ---------------------------------------------------------------- the masher
        const float RunSeconds = 150f;
        // Re-reading the room in the editor would drop the sample room: not the game under test.
        static readonly string[] NeverPress = { "scan", "Choice scan:yes" };
        static readonly string[] Sayings =
        {
            "asdf banana", "uhhh", "what", "purple monkey dishwasher", "?!", "make it rain", "undo", "bigger", "help", "menu",
            "stop", "storm", "are you there", "make me a lantern", "save this as qwerty", "freeze", "resume", "louder",
            "no", "yes", "explore", "hello", "日本語 🐉", new string('z', 240), "take me home take me home",
        };
        static readonly string[] Worlds = { "the beach", "a snowy cabin at night", "a pirate cove", "a meadow", "a volcano at night", "the city", "a coral reef" };
        System.Random rng;
        int seed, capHits, reachHits;
        string fail;
        readonly List<string> trail = new List<string>();
        Quaternion seatRot; Vector3 lastHead; Director.Mode lastState;
        float runStart, stateAt, forwardAt, headAt, capAt, reachAt;

        [UnityTest, Timeout(300000)] public IEnumerator MasherSeed1729() { return Mash(1729); }
        [UnityTest, Timeout(300000)] public IEnumerator MasherSeed4242() { return Mash(4242); }
        [UnityTest, Timeout(300000)] public IEnumerator MasherSeed90210() { return Mash(90210); }

        IEnumerator Mash(int s)
        {
            seed = s; rng = new System.Random(s); fail = null; trail.Clear();
            ActAi.Canned = said => null;   // Juniper's helper "can't be reached": no network, the same answer every run
            d.ResetProgress(new Progress());
            seatRot = d.Head.rotation;
            hand.position = d.Head.position + Fwd() * 0.15f + Vector3.down * 0.45f;   // on the lap, in view
            d.StartFirstRunForTests();
            runStart = Time.unscaledTime;
            lastState = d.State; stateAt = forwardAt = headAt = runStart; capAt = reachAt = runStart + 1f; capHits = reachHits = 0; lastHead = d.Head.position;
            Act("a brand-new player, first run"); int steps = 0;
            for (; fail == null && Time.unscaledTime - runStart < RunSeconds; steps++)
            {
                yield return Step();
                if (fail == null) yield return Wait(R(0.1f, 1.2f));
            }
            hands.SimulatedPinch = hands.SimulatedFist = hands.SimulatedPalmUp = hands.SimulatedPalmOut = false;
            if (fail != null) Assert.Fail(fail);
            Debug.Log("[Agent] seed " + seed + ": " + steps + " actions in " + RunSeconds + " s, nothing broke. " + Describe());
        }

        IEnumerator Step()
        {
            double r = rng.NextDouble() * 100.0;
            if (r < 32) yield return PokeSomething(rng.NextDouble() < 0.3);
            else if (r < 40) yield return Grab(false);
            else if (r < 45) yield return Grab(true);
            else if (r < 50) yield return Wave();
            else if (r < 58) yield return PalmUp();
            else if (r < 70)
            {   // nonsense, commands and odd input, by voice or typed
                string w = Sayings[rng.Next(Sayings.Length)], how = rng.NextDouble() < 0.6 ? "voice" : "typed";
                Do("says \"" + Clip(w, 40) + "\" (" + how + ")", () => d.Handle(w, how));
            }
            else if (r < 76 && rng.NextDouble() < 0.6) { string w = Worlds[rng.Next(Worlds.Length)]; Do("asks for " + w, () => d.Handle("take me to " + w, "voice")); }
            else if (r < 76) { string id = rng.NextDouble() < 0.5 ? "next" : "worlds"; Do("dock " + id + " (hook)", () => d.OnDockForTests(id)); }
            else if (r < 80 && rng.NextDouble() < 0.6) { string home = rng.NextDouble() < 0.5 ? "take me home" : "go back to my room"; Do("says " + home, () => d.Handle(home, "voice")); }
            else if (r < 80) Do("dock room (hook)", () => d.OnDockForTests("room"));
            else if (r < 86) LookAround();
            else if (r < 88) yield return StandUp();
            else if (r < 95) { float w = R(1f, 4f); Act("waits " + w.ToString("F1") + " s"); yield return Wait(w); }
            else
            {
                int n = rng.Next(3, 7);
                Act("mashes " + n + " times");
                for (int i = 0; i < n && fail == null; i++) yield return rng.NextDouble() < 0.6 ? PokeSomething(false) : Grab(false, 0.08f);
            }
        }

        IEnumerator PokeSomething(bool twice)
        {
            var all = PokeButton.Live.Where(x => x != null && x.isActiveAndEnabled && !NeverPress.Contains(x.Id ?? ""))
                .OrderBy(x => x.Id ?? "", System.StringComparer.Ordinal).ToList();
            if (all.Count == 0) { Act("looks for a button: none"); yield return Wait(0.3f); yield break; }
            var obvious = all.Where(Main).ToList();
            var b = obvious.Count > 0 && rng.NextDouble() < 0.6 ? obvious[rng.Next(obvious.Count)] : all[rng.Next(all.Count)];
            string id = b.Id;
            float before = b.LastPress;
            if (rng.NextDouble() < 0.15)
            {
                // Straight Press(), but only where a real finger could press it (armed, and allowed past the help card).
                if (!b.Armed || (d.Pokes != null && d.Pokes.Allowed != null && !d.Pokes.Allowed(b))) { Act("taps " + id + ": not armed or not allowed"); yield break; }
                Do("presses " + id + (twice ? " twice" : ""), () => { b.Press(); if (twice && b != null) b.Press(); });
                yield break;
            }
            Act("pokes " + id + (twice ? " twice, fast" : ""));
            for (int k = 0; k < (twice ? 2 : 1) && fail == null; k++)
            {
                if (b == null || !b.isActiveAndEnabled) break;
                Vector3 n = b.transform.forward;   // buttons face the user along -Z: the front is -forward
                hand.position = b.transform.position - n * 0.035f; yield return Wait(0.07f);   // in front of it
                if (b == null || !b.isActiveAndEnabled) break;
                hand.position = b.transform.position + n * 0.004f; yield return Wait(0.12f);   // into the button: a press
                if (b == null) break;
                hand.position = b.transform.position - n * 0.07f; yield return Wait(twice ? 0.04f : 0.15f);
            }
            Act("  " + id + (b != null && b.LastPress > before ? ": pressed" : ": no press"));
        }

        IEnumerator Grab(bool fist, float maxHold = 1.2f)
        {
            Vector3 at = Spot(), to = rng.NextDouble() < 0.5 ? Spot() : at;
            Act((fist ? "fist" : "pinch") + " at " + Rel(at) + (to != at ? " dragging to " + Rel(to) : ""));
            hand.position = at; yield return Wait(0.05f);
            if (fist) hands.SimulatedFist = true; else hands.SimulatedPinch = true;
            float hold = R(0.05f, maxHold), t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < hold && fail == null) { hand.position = Vector3.Lerp(at, to, (Time.unscaledTime - t0) / hold); yield return Wait(0f, 1); }
            hands.SimulatedFist = false; hands.SimulatedPinch = false; yield return Wait(0.1f);
        }

        IEnumerator Wave()
        {
            Act("waves");
            Vector3 f = Fwd(), side = Vector3.Cross(Vector3.up, f), c = d.Head.position + f * 0.42f + Vector3.down * 0.05f;
            hands.SimulatedPalmOut = true; float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 1.6f && fail == null) { hand.position = c + side * (0.1f * Mathf.Sin((Time.unscaledTime - t0) * Mathf.PI * 3f)); yield return Wait(0f, 1); }
            hands.SimulatedPalmOut = false;
        }

        IEnumerator PalmUp()
        {
            Act("turns the palm up");
            hand.position = d.Head.position + d.Head.forward * 0.3f - Vector3.up * 0.2f; hands.SimulatedPalmUp = true;
            yield return Wait(R(0.4f, 2f));
            hands.SimulatedPalmUp = false; Act("  menu " + (d.Panel != null && d.Panel.Open ? "open" : "not open"));
        }

        void LookAround()
        {
            if (rng.NextDouble() < 0.3) { d.Head.rotation = seatRot; Act("looks back at the table"); }
            else
            {
                float yaw = R(-40f, 40f), pitch = R(-20f, 25f);
                d.Head.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * seatRot * Quaternion.AngleAxis(pitch, Vector3.right);
                Act("looks around (yaw " + yaw.ToString("F0") + ", pitch " + pitch.ToString("F0") + ")");
            }
            headAt = Time.unscaledTime;
        }

        IEnumerator StandUp()
        {
            Vector3 p = d.Head.position; float w = R(1f, 3f);
            Act("stands up for " + w.ToString("F1") + " s");
            d.Head.position = p + Vector3.up * 0.4f;
            yield return Wait(w);
            d.Head.position = p; Act("  sits down");
        }

        // ---------------------------------------------------------------- oracles (checked every frame of every wait)
        void Check()
        {
            if (fail != null || d == null || d.Head == null) return;
            float now = Time.unscaledTime;
            if (ours.Count > 0) { Fail("an error from our code: " + ours[0]); return; }
            var st = d.State;
            if (st != lastState) { Act("  (state " + lastState + " -> " + st + ")"); lastState = st; stateAt = now; }
            if (st == Director.Mode.Changing && now - stateAt > 20f) { Fail("still Changing after 20 s"); return; }
            if ((st == Director.Mode.Opening || st == Director.Mode.Closing) && now - stateAt > 30f) { Fail("still " + st + " after 30 s"); return; }
            if (WayForward()) forwardAt = now;
            else if (now - forwardAt > 30f) { Fail("stuck: no way forward for 30 s"); return; }
            if ((d.Head.position - lastHead).sqrMagnitude > 0.01f) headAt = now;   // jumped, glided or stood
            lastHead = d.Head.position;
            bool settled = now - stateAt > 2f;
            if (now >= capAt)
            {
                // Confirmed 0.35 s later (a caption still easing aside is not counted), and not just after an instant head turn.
                if (settled && now - headAt > 1f && CaptionLayout.LastOverlaps > 0)
                {
                    if (++capHits >= 2) { Fail("words on words: " + CaptionLayout.LastOverlaps + " (" + CaptionLayout.LastOverlapNames + ")"); return; }
                    capAt = now + 0.35f;
                }
                else { capHits = 0; capAt = now + 1f; }
            }
            if (now >= reachAt)
            {
                string far = settled && now - headAt > 2f && !(d.Watch != null && d.Watch.Unsafe) ? Far() : null;
                if (far == null) { reachHits = 0; reachAt = now + 1f; }
                else if (++reachHits >= 2) Fail("out of reach: " + far);
                else reachAt = now + 0.6f;   // the dock brings itself back within half a second
            }
        }

        bool WayForward()
        {
            if (d.Busy || d.Frozen) return true;   // frozen: "Palm up to resume"
            if ((d.Gather != null && d.Gather.Running) || (d.Mission != null && d.Mission.Active) ||
                (d.Panel != null && d.Panel.Open) || (d.Help != null && d.Help.Showing)) return true;
            foreach (var b in PokeButton.Live) if (b != null && b.isActiveAndEnabled && Main(b)) return true;
            // In the room the light itself is the way on ("Touch me, or look at me").
            return d.State == Director.Mode.Room && d.Guide != null && d.Guide.Button != null && d.Guide.Button.isActiveAndEnabled;
        }

        static bool Starts(PokeButton b, string p) => (b.Id ?? "").StartsWith(p, System.StringComparison.Ordinal);
        static bool Main(PokeButton b) => Starts(b, "Choice ") || Starts(b, "dock ") || Starts(b, "map ");

        string Far()
        {
            foreach (var b in PokeButton.Live)
                if (b != null && b.isActiveAndEnabled && (Starts(b, "Choice ") || Starts(b, "dock ")) && Vector3.Distance(b.transform.position, d.Head.position) > 0.75f)
                    return b.Id + " is " + Vector3.Distance(b.transform.position, d.Head.position).ToString("F2") + " m from the head";
            return null;
        }

        // ---------------------------------------------------------------- helpers
        IEnumerator Wait(float seconds, int minFrames = 2)
        {
            float end = Time.unscaledTime + seconds;
            for (int f = 0; f < minFrames || Time.unscaledTime < end; f++) { Check(); if (fail != null) yield break; yield return null; }
        }

        void Do(string what, System.Action a)
        {   // A throw from our code during an action fails with the trail, not as a bare exception.
            Act(what);
            try { a(); } catch (System.Exception e) { Fail("threw on '" + what + "': " + e.GetType().Name + ": " + e.Message); }
        }

        void Act(string what)
        {
            string line = "t=" + (Time.unscaledTime - runStart).ToString("F1") + " " + what + " [" + (d != null ? d.State.ToString() : "-") + "]";
            trail.Add(line); Debug.Log("[Agent] seed " + seed + " " + line);
        }

        void Fail(string why)
        {
            if (fail != null) return;
            fail = "seed " + seed + " at t=" + (Time.unscaledTime - runStart).ToString("F1") + ": " + why + "\n" + Describe() + "\nlast actions:\n  " + Trail();
            Debug.Log("[Agent] FAIL " + fail);
        }
        string Trail() => string.Join("\n  ", trail.Skip(Mathf.Max(0, trail.Count - 10)));

        string Describe()
        {
            if (d == null) return "no director";
            var ids = PokeButton.Live.Where(b => b != null && b.isActiveAndEnabled).Select(b => b.Id).Take(14);
            return "state=" + d.State + " world=" + (d.Current != null ? d.Current.Biome : "-") + " gather=" + (d.Gather != null && d.Gather.Running) +
                " mission=" + (d.Mission != null && d.Mission.Active) + " panel=" + (d.Panel != null && d.Panel.Open) + " help=" + (d.Help != null && d.Help.Showing) +
                " map=" + (d.Map != null && d.Map.Showing) + " frozen=" + d.Frozen + " welcoming=" + d.Welcoming +
                " says=\"" + Clip(d.Guide != null && d.Guide.Words != null ? d.Guide.Words.Current : "", 80) + "\" buttons=[" + string.Join(", ", ids) + "]";
        }

        Vector3 Fwd() { Vector3 f = d.Head.forward; f.y = 0f; return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward; }

        /// <summary>Somewhere a curious hand goes: over the table, or anywhere in front within reach.</summary>
        Vector3 Spot()
        {
            var t = d.Rooms != null && d.Rooms.Room != null ? d.Rooms.Room.Table : null;
            if (t != null && rng.NextDouble() < 0.35) return t.TopCenter + new Vector3(R(-0.4f, 0.4f), R(0.03f, 0.3f), R(-0.4f, 0.4f));
            Vector3 f = Fwd(), side = Vector3.Cross(Vector3.up, f);
            return d.Head.position + f * R(0.15f, 0.6f) + side * R(-0.35f, 0.35f) + Vector3.up * R(-0.5f, 0.15f);
        }

        string Rel(Vector3 p) { Vector3 v = p - d.Head.position; return "(" + v.x.ToString("F2") + ", " + v.y.ToString("F2") + ", " + v.z.ToString("F2") + ")"; }
        float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
        static string Clip(string s, int n) => s == null ? "" : s.Length <= n ? s : s.Substring(0, n) + "…";
    }
}
