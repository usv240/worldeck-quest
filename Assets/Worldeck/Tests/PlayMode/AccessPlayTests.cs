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
    /// Player agents for people who don't use their hands the usual way, in the real scene and a scanned sample room.
    /// The no-hands agents never call Press(): they only turn the head and let the eyes' dwell do it.
    /// </summary>
    public class AccessPlayTests
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
            Assert.IsEmpty(ours, "errors from our code: " + string.Join(" | ", ours.Take(5)));
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

        IEnumerator Until(System.Func<bool> ok, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!ok() && Time.realtimeSinceStartup < end) yield return null;
            Assert.IsTrue(ok(), "timed out waiting for " + what);
        }

        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        // ---------- the agent: a trail, soft failures, and a verdict at the end ----------

        readonly List<string> trail = new List<string>(), fails = new List<string>();
        string step = "start";
        float began;
        bool got, gathered;
        int blocked = -1, taken = -1;
        System.Random rnd = new System.Random(7);
        static float Now => Time.realtimeSinceStartup;

        void Begin(string who)
        {
            trail.Clear(); fails.Clear(); step = "start"; began = Now; gathered = false; blocked = taken = -1; rnd = new System.Random(7);
            Debug.Log("[Agent] persona: " + who);
        }

        void Step(string s) { step = s; trail.Add((Now - began).ToString("0") + "s " + s); Debug.Log("[Agent] step: " + s + " (state " + (d != null ? d.State.ToString() : "-") + ")"); }

        static string Buttons() => string.Join(", ", PokeButton.Live.Where(b => b != null && b.isActiveAndEnabled).Select(b => b.Id).Distinct().Take(30));

        void Fail(string why)
        {
            fails.Add("[" + step + "] " + why + " | state " + (d != null ? d.State.ToString() : "-") + " | Mara " + (d != null && d.Mission != null ? d.Mission.Now.ToString() : "-") +
                      " | buttons: " + Buttons() + " | trail: " + string.Join(" > ", trail.Skip(Mathf.Max(0, trail.Count - 25))));
            Debug.Log("[Agent] FAIL " + fails[fails.Count - 1]);
        }

        void Verdict()
        {
            Debug.Log("[Agent] finished after " + (Now - began).ToString("0") + " s, " + fails.Count + " failure(s), blocked " + blocked + ", taken " + taken + ". Trail: " + string.Join(" > ", trail));
            Assert.IsEmpty(fails, string.Join("\n", fails));
        }
        static bool Safe(System.Func<bool> f) { try { return f(); } catch (System.Exception) { return false; } }

        IEnumerator Wait(System.Func<bool> ok, float seconds, string what, System.Action each = null)
        {
            float end = Now + seconds;
            while (!Safe(ok) && Now < end) { each?.Invoke(); yield return null; }
            got = Safe(ok);
            if (got) Step(what); else Fail("not reached within " + seconds.ToString("0") + " s: " + what);
        }

        static PokeButton Find(string id) => PokeButton.Live.FirstOrDefault(b => b != null && b.isActiveAndEnabled && b.Id == id);
        static PokeButton FindStart(string prefix) => PokeButton.Live.FirstOrDefault(b => b != null && b.isActiveAndEnabled && b.Id.StartsWith(prefix));

        Vector3 Flat() { Vector3 f = d != null && d.Head != null ? Vector3.ProjectOnPlane(d.Head.forward, Vector3.up) : Vector3.forward; return f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward; }
        void Aim(Vector3 at) { if (d != null && d.Head != null && (at - d.Head.position).sqrMagnitude > 1e-6f) d.Head.rotation = Quaternion.LookRotation(at - d.Head.position, Vector3.up); }

        /// <summary>Eyes resting where no button is: up over the table, at the sky.</summary>
        void LookUp() { if (d != null && d.Head != null) Aim(d.Head.position + Flat() + Vector3.up * 0.8f); }

        /// <summary>Looks at a button until the eyes' dwell presses it, or it goes. Never calls Press().</summary>
        IEnumerator LookPress(PokeButton b, float seconds = 12f)
        {
            if (b == null || d.Pokes == null) yield break;
            string id = b.Id;
            int before = d.Pokes.Presses;
            Step("look at " + id);
            float end = Now + seconds;
            while (Now < end && b != null && b.isActiveAndEnabled && d.Pokes.Presses == before) { Aim(b.transform.position); yield return null; }
            bool pressed = d.Pokes.Presses > before;
            Debug.Log("[Agent] " + id + (pressed ? " pressed by " + d.Pokes.LastMethod : " not pressed (looked " + (Now - end + seconds).ToString("0.0") + " s)"));
            if (pressed && d.Pokes.LastMethod != "look") Fail(id + " was pressed by " + d.Pokes.LastMethod + ", not by the eyes");
            LookUp();
        }

        // ---------- one slow hand ----------

        Vector3 Rest() => d.Head.position + Flat() * 0.2f + Vector3.down * 0.4f + Vector3.Cross(Vector3.up, Flat()) * 0.15f;

        IEnumerator Glide(System.Func<Vector3> to, float seconds)
        {
            if (hand == null) yield break;
            Vector3 from = hand.position;
            float t0 = Now;
            while (Now - t0 < seconds) { float k = (Now - t0) / seconds; hand.position = Vector3.Lerp(from, to(), k * k * (3f - 2f * k)); yield return null; }
            hand.position = to();
        }

        IEnumerator Pause(string why)
        {
            float s = 8f + (float)rnd.NextDouble() * 7f;
            Step("pause " + s.ToString("0") + " s " + why);
            hands.SimulatedPinch = false;
            yield return Glide(Rest, 1.2f);
            yield return new WaitForSecondsRealtime(s);
        }

        /// <summary>A fingertip press, three times slower than usual: a long reach, a slow push in, a rest on it.</summary>
        IEnumerator SlowPoke(PokeButton b)
        {
            if (b == null || hand == null) yield break;
            string id = b.Id;
            int before = d.Pokes != null ? d.Pokes.Presses : 0;
            Step("slow poke " + id);
            System.Func<float, System.Func<Vector3>> front = k => () => b != null ? b.transform.position - b.transform.forward * k : hand.position;
            yield return Glide(front(0.09f), 1.8f);
            yield return Glide(front(-0.012f), 0.5f);
            yield return new WaitForSecondsRealtime(0.4f);
            bool pressed = d.Pokes != null && d.Pokes.Presses > before;
            yield return Glide(front(0.1f), 0.6f);
            yield return Glide(Rest, 1.2f);
            Debug.Log("[Agent] slow poke " + id + (pressed ? " pressed (" + d.Pokes.LastMethod + ")" : " did not press"));
        }

        IEnumerator Use(PokeButton b, bool eyes)
        {
            if (eyes) { yield return LookPress(b); yield break; }
            string id = b != null ? b.Id : "?";
            yield return Pause("before " + id);
            if (Find(id) == null) Step(id + " had gone after the pause"); else yield return SlowPoke(Find(id));
        }

        IEnumerator GatherByEyes()
        {
            var g = d.Gather;
            yield return Wait(() => g.Items.All(i => i == null || i.Appear >= 0.6f), 15f, "the pieces glow");
            if (!g.EyesOnly) Fail("Gather.EyesOnly is off: looking cannot bring the pieces");
            foreach (var it in g.Items.ToList())
            {
                if (it == null || it.Placed || !g.Running) continue;
                var piece = it;
                yield return Wait(() => piece == null || piece.Placed || piece.HeldBy >= 0 || !g.Running, 12f, "piece " + piece.Index + " came by looking",
                                  () => { if (piece != null) Aim(piece.transform.position); });
            }
            LookUp();
            yield return Wait(() => !g.Running || g.PlacedCount == g.Total, 20f, "every piece on the table by eye");
            gathered = got;
        }

        IEnumerator GatherSlowly()
        {
            var g = d.Gather;
            var table = d.Rooms != null && d.Rooms.Room != null ? d.Rooms.Room.Table : null;
            yield return Wait(() => g.Items.All(i => i == null || i.Appear >= 0.6f), 15f, "the pieces glow");
            int n = 0;
            float end = Now + 150f;
            while (g.Running && table != null && Now < end && n < 14)
            {
                var it = g.Items.FirstOrDefault(i => i != null && !i.Placed && i.HeldBy < 0 && i.Appear >= 0.6f);
                if (it == null) { yield return null; continue; }
                if (++n == 2) yield return Pause("mid-task, between pieces");
                if (it == null || it.Placed) continue;
                hands.SimulatedPinch = false;
                yield return Glide(() => it != null ? it.transform.position : hand.position, 1.5f);
                hands.SimulatedPinch = true;
                yield return Frames(4);
                if (!g.Holding(HandInput.SimulatedIndex))
                {
                    Fail("a slow pinch on piece " + (it != null ? it.Index : -1) + " did not pick it up");
                    hands.SimulatedPinch = false;
                    yield return Glide(Rest, 1f);
                    continue;
                }
                yield return Glide(() => table.TopCenter + Vector3.up * 0.12f, 1.5f);   // carried three times slower than usual
                yield return new WaitForSecondsRealtime(0.4f);
                hands.SimulatedPinch = false;
                yield return new WaitForSecondsRealtime(0.8f);
                yield return Glide(Rest, 1f);
            }
            yield return Wait(() => !g.Running || g.PlacedCount == g.Total, 30f, "every piece on the table, slowly");
            gathered = got;
        }

        IEnumerator OfferPalm()
        {
            Step("a slow open palm for her ship");
            Vector3 at = d.Head.position + Flat() * 0.36f + Vector3.down * 0.22f;
            yield return Glide(() => at, 2f);
            hands.SimulatedPalmUp = true;
            for (float end = Now + 14f; d.Mission != null && d.Mission.WantsPalm && Now < end;) yield return null;
            hands.SimulatedPalmUp = false;
            yield return Glide(Rest, 1f);
        }

        /// <summary>
        /// Plays on from wherever the game is: a task, Mara's choices, her storm, the call from an animal's home, the
        /// bright button. Done once a task is gathered and her first chapter is flown. Nothing moving for 60 s is a dead end.
        /// </summary>
        IEnumerator Drive(bool eyes, float until)
        {
            string key = "";
            float movedAt = Now;
            bool stormPaused = false;
            while (Now < until && !(gathered && d.Progress.MissionChapter >= 1 && (d.Mission == null || !d.Mission.Active)))
            {
                var m = d.Mission; var em = d.Emergency; var g = d.Gather;
                bool storm = em != null && em.Running && em.Sim != null && em.Sim.State.Phase < Sim.Aftermath;
                string k = d.State + " | Mara " + (m != null ? m.Now.ToString() : "-") + (g != null && g.Running ? " | gather " + g.PlacedCount + "/" + g.Total : "") +
                           (storm ? " | strike " + em.Sim.State.Next : "") + (d.Dock != null && !string.IsNullOrEmpty(d.Dock.ActionLabel) ? " | button " + d.Dock.ActionLabel : "");
                if (k != key) { key = k; movedAt = Now; Step(k); }
                if (Now - movedAt > 60f) { Fail("no way forward within 60 s"); yield break; }
                if (d.Busy) { yield return null; continue; }
                if (m != null && m.Active)
                {
                    if (storm && m.Now == Mission.Step.Storm)
                    {
                        blocked = em.Sim.State.Blocked; taken = em.Sim.State.Taken;
                        if (eyes) Aim(em.NextTarget);
                        else if (!stormPaused && em.Sim.State.Next == 3) { stormPaused = true; yield return Pause("mid-storm, the hand down"); continue; }
                        else if (hand != null) hand.position = Vector3.MoveTowards(hand.position, em.NextTarget, Time.unscaledDeltaTime * 0.7f);
                        yield return null;
                        continue;
                    }
                    if (em != null && em.Sim != null) { blocked = em.Sim.State.Blocked; taken = em.Sim.State.Taken; }
                    var choice = m.Now == Mission.Step.Answer ? Find("Choice m:guide") : m.Now == Mission.Step.Pace ? Find("Choice m:slow") :
                                 m.Now == Mission.Step.Farewell ? (Find("Choice m:wave") ?? FindStart("Choice m:")) : null;
                    if (choice == null && em != null && em.RewindShown) choice = Find("rewind");
                    if (choice != null) { yield return Use(choice, eyes); continue; }
                    if (!eyes && m.WantsPalm) { yield return OfferPalm(); continue; }
                    if (eyes) LookUp();
                    yield return null;
                    continue;
                }
                if (g != null && g.Running && !gathered) { yield return eyes ? GatherByEyes() : GatherSlowly(); continue; }
                var call = Find("Choice first:campfire");
                if (call != null) { yield return Use(call, eyes); continue; }
                string label = d.Dock != null ? d.Dock.ActionLabel : "";
                var bright = Find("dock do");
                if (d.State == Director.Mode.World && bright != null && ((label == "Next world" && !gathered) || (label == "Answer Mara" && d.Progress.MissionChapter < 1)))
                { yield return Use(bright, eyes); continue; }
                if (eyes) LookUp();
                yield return null;
            }
            if (!(gathered && d.Progress.MissionChapter >= 1))
                Fail("out of time: task gathered " + gathered + ", Mara's chapters flown " + d.Progress.MissionChapter);
        }

        // ---------- 1. no hands at all ----------

        [UnityTest, Timeout(300000)]
        public IEnumerator NoHandsAtAllPlaysTheOpeningByLooking()
        {
            Begin("no hands at all (limited mobility)");
            LookUp();
            d.ResetProgress(new Progress());
            // A hand 5 m away is still "tracked" (HandInput.Simulated): only an inactive hand is not seen at all.
            hand.position = d.Head.position + Vector3.up * 5f;
            hand.gameObject.SetActive(false);
            yield return Wait(() => !d.Hands.AnyTracked && d.Pokes != null && d.Pokes.EyesOnly, 15f, "eyes only: no hand seen");
            d.StartFirstRunForTests();
            yield return Wait(() => d.Welcoming && Find("Choice welcome:go") != null, 60f, "1. the welcome card");
            yield return LookPress(Find("Choice welcome:go"));
            yield return Wait(() => Find("Choice first:zebra") != null, 60f, "2. the first animals");
            yield return LookPress(Find("Choice first:zebra"));
            yield return Wait(() => d.FirstAnimal != null && d.FirstAnimal.T != null, 60f, "2. a zebra on the table, chosen by looking");
            // 3. "See where it lives" comes only once the animal has been held up close (Inspect): watch for it.
            System.Func<bool> home = () => d.Inspect != null && d.Inspect.HomeButton != null && d.Inspect.HomeButton.isActiveAndEnabled;
            yield return Wait(() => home() || d.State != Director.Mode.Room, 60f, "3. \"See where it lives\", or a way on",
                              () => { var a = d.FirstAnimal; if (a != null && a.T != null) Aim(Maker.Centre(a)); });
            if (got && !home()) Fail("a stray glance left the room before \"See where it lives\" was offered");
            if (d.State == Director.Mode.Room && home())
            {
                yield return LookPress(d.Inspect.HomeButton);
                yield return Wait(() => d.State == Director.Mode.World, 40f, "3. the zebra's home, by looking");
            }
            else if (d.State == Director.Mode.Room)
            {
                Step("why: \"See where it lives\" needs the animal held up to the face (Inspect); Juniper says: " + (d.Guide != null && d.Guide.Words != null ? d.Guide.Words.Current : "-"));
                Step("fallback: look at Juniper, as a no-hands player is told to");
                yield return LookPress(d.Guide != null ? d.Guide.Button : null);
                yield return Wait(() => d.State == Director.Mode.World, 40f, "a world, by looking at Juniper");
            }
            if (d.State == Director.Mode.World || d.Busy) yield return Drive(true, began + 280f);
            if (blocked < 1) Fail("5. no ring of her storm was shielded by looking (blocked " + blocked + ")");
            Verdict();
        }

        // ---------- 2. Eyes only, with a hand resting in view ----------

        [UnityTest, Timeout(300000)]
        public IEnumerator EyesOnlySettingWithAHandRestingInView()
        {
            Begin("hands-free setting, a hand resting in view");
            LookUp();
            d.ResetProgress(new Progress { Onboarded = true, LookToPress = true });
            hand.position = d.Head.position + Vector3.down * 0.45f + Flat() * 0.15f;   // on the lap, in view
            d.Open(Recipe.For("meadow", 3), "test");
            yield return Wait(() => d.State == Director.Mode.World, 25f, "the meadow");
            yield return Frames(3);
            if (!d.Hands.AnyTracked) Fail("the resting hand is not seen: this is not the persona");
            if (!Emergency.EyesToo) Fail("Emergency.EyesToo is off with Eyes only on");
            if (d.Pokes == null || !d.Pokes.AlwaysDwell) Fail("look-to-press is off while a hand is in view");
            yield return Wait(() => d.Gather != null && d.Gather.Running, 20f, "4. the gather task");
            if (d.Gather != null && !d.Gather.EyesOnly) Fail("Gather.EyesOnly is off with a hand in view and Eyes only on");
            yield return Drive(true, began + 280f);
            if (blocked < 1) Fail("5. no ring was shielded by looking with a hand in view (blocked " + blocked + ")");
            Verdict();
        }

        // ---------- 3. one hand, slow ----------
        [UnityTest, Timeout(300000)]
        public IEnumerator OneSlowHandWithLongPausesNeverHitsADeadEnd()
        {
            Begin("one hand only, slow, long pauses");
            d.ResetProgress(new Progress { Onboarded = true });
            hand.position = Rest();
            hands.SimulatedPinch = false; hands.SimulatedPalmUp = false;
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Wait(() => d.State == Director.Mode.World, 25f, "the campfire");
            yield return Wait(() => d.Gather != null && d.Gather.Running, 20f, "the gather task");
            yield return Drive(false, began + 285f);
            Debug.Log("[Agent] the Welcome card goes on by itself after 120 s, How it works after 240 s (Director.Welcome): not waited out here");
            Verdict();
        }

        // ---------- 4. no hands, wants the settings mid-game ----------
        [UnityTest, Timeout(300000)]
        public IEnumerator NoHandsReachesComfortAndAccessFromAWorld()
        {
            Begin("no hands, wants Comfort & access mid-game");
            LookUp();
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = 1 });
            hand.position = d.Head.position + Vector3.up * 5f;
            hand.gameObject.SetActive(false);
            d.Open(Recipe.For("meadow", 3), "test");
            yield return Wait(() => d.State == Director.Mode.World && !d.Busy, 25f, "a world");
            yield return Wait(() => d.Pokes != null && d.Pokes.EyesOnly, 15f, "eyes only");
            yield return Wait(() => Find("dock worlds") != null, 25f, "the dock in view");
            if (d.Gather != null) d.Gather.Stop();
            if (d.Loose != null) d.Loose.Stop();
            // Every way in that could lead to the settings, tried by looking (nothing that ends or changes the world).
            string[] ways = { "settings", "wrist menu", "Guide", "map help", "help ok", "dock worlds", "map close" };
            System.Func<PokeButton, int> rank = b => new[] { "setting", "comfort", "access", "menu" }.Any(w => b.Id.ToLowerInvariant().Contains(w)) ? 0 :
                                                     System.Array.IndexOf(ways, b.Id) < 0 ? -1 : System.Array.IndexOf(ways, b.Id) + 1;
            var tried = new List<string>();
            string mapOffers = "(map never shown)";
            bool comfort = false;
            float end = Now + 150f;
            while (Now < end)
            {
                comfort = d.Panel != null && d.Panel.Open && d.Panel.Current == HandPanel.Page.Comfort;
                if (comfort) break;
                if (d.Map != null && d.Map.Showing) mapOffers = string.Join(", ", PokeButton.Live.Where(b => b != null && b.isActiveAndEnabled && b.Id.StartsWith("map ")).Select(b => b.Id));
                var next = PokeButton.Live.Where(b => b != null && b.isActiveAndEnabled && !tried.Contains(b.Id) && rank(b) >= 0).OrderBy(rank).FirstOrDefault();
                if (next == null) break;
                tried.Add(next.Id);
                Debug.Log("[Agent] in view: " + Buttons());
                yield return LookPress(next);
                yield return new WaitForSecondsRealtime(2.5f);
            }
            Step(comfort ? "Comfort & access reached by looking" : "no way left to try");
            if (!comfort)
                Fail("Comfort & access is not reachable with no hands from a world (the menu needs palm-up or the left-wrist button). Tried by looking: " +
                     string.Join(", ", tried) + ". Juniper's map offered: " + mapOffers);
            Verdict();
        }
    }
}
