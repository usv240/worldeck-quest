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
    /// <summary>A player agent: Mara's story from chapter 1, with one real-life mishap per case. Every case also checks: no
    /// error from our code, a next step or a way forward within 60 s of game time, never Changing for over 20 s.</summary>
    public class StoryMishapPlayTests
    {
        const string Scene = "Assets/Worldeck/Scenes/Worldeck.unity";
        Director d;
        HandInput hands;
        Transform hand;
        readonly List<string> ours = new List<string>();

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
            if (hands != null) { hands.SimulatedPalmUp = false; hands.SimulatedPinch = false; }
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
            changingSince = -1f; lastSig = ""; lastForward = Time.time;
        }

        IEnumerator Until(System.Func<bool> ok, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!ok() && Time.realtimeSinceStartup < end) { Tick(); yield return null; }
            Assert.IsTrue(ok(), "timed out waiting for " + what);
        }

        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        // ---------- the watchdog, run every frame the agent waits ----------

        float changingSince = -1f, lastForward;
        string lastSig = "";

        static PokeButton Live(string id) => PokeButton.Live.FirstOrDefault(b => b != null && b.Id == id && b.isActiveAndEnabled);

        /// <summary>An active choice, dock or Rewind button, or Mara on the line ("dock room" only leaves: not counted).</summary>
        bool WayForward() =>
            (d.Mission != null && d.Mission.Active) ||
            PokeButton.Live.Any(b => b != null && b.isActiveAndEnabled && b.Id != null && b.Id != "dock room" &&
                                     (b.Id.StartsWith("Choice ") || b.Id.StartsWith("dock ") || b.Id == "rewind"));

        string Where()
        {
            var m = d.Mission; var em = d.Emergency;
            return d.State + " | mission " + (m != null ? m.Now.ToString() : "-") +
                   " | storm " + (em != null && em.Sim != null ? em.Sim.State.Phase + ":" + em.Sim.State.Next : "-") +
                   " | gather " + (d.Gather != null ? d.Gather.PlacedCount + "/" + d.Gather.Total + (d.Gather.Running ? " running" : "") : "-");
        }

        void Tick()
        {
            if (d == null) return;
            float now = Time.unscaledTime;
            if (d.State != Director.Mode.Changing) changingSince = -1f;
            else if (changingSince < 0f) changingSince = now;
            if (changingSince >= 0f) Assert.Less(now - changingSince, 20f, "stuck in Changing for over 20 s: " + Where());
            string sig = Where();
            if (sig != lastSig || WayForward()) { lastSig = sig; lastForward = Time.time; }
            Assert.Less(Time.time - lastForward, 60f, "no next step and no way forward for 60 s of game time at " + sig);
        }

        IEnumerator Idle(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) { Tick(); yield return null; }
        }

        // ---------- playing the story ----------

        /// <summary>Where people really let go over the table (FlowTests.RealReleases).</summary>
        static readonly Vector3[] Releases = { new Vector3(-0.401f, 0.463f, 0.577f), new Vector3(-0.338f, 0.471f, 0.372f),
            new Vector3(-0.246f, 0.354f, 0.395f), new Vector3(-0.268f, 0.399f, 0.528f), new Vector3(-0.153f, 0.402f, 0.272f) };

        IEnumerator CarryOne(GatherItem it, int n)
        {
            if (it == null || it.Placed || d.Rooms == null || d.Rooms.Room == null) yield break;
            var table = d.Rooms.Room.Table;
            hand.position = it.transform.position;
            hands.SimulatedPinch = false;
            yield return Frames(2);
            hands.SimulatedPinch = true;
            yield return Frames(3);
            if (!d.Gather.Holding(HandInput.SimulatedIndex)) { Debug.Log("[Agent] missed the pinch on " + it.name); hands.SimulatedPinch = false; yield break; }
            var r = Releases[n % Releases.Length];
            Vector3 to = table.ToWorld(new Vector3(r.x, table.Size.y + r.y, r.z)), from = hand.position;
            float t0 = Time.time;
            while (Time.time - t0 < 0.5f) { hand.position = Vector3.Lerp(from, to, (Time.time - t0) / 0.4f); Tick(); yield return null; }
            hand.position = to;
            yield return new WaitForSeconds(0.15f);
            hands.SimulatedPinch = false;
            yield return Idle(0.6f);
        }

        IEnumerator OpenWorld(string biome)
        {
            hand.position = d.Head.position + Vector3.up * 3f;
            Debug.Log("[Agent] opening " + biome);
            d.Open(Recipe.For(biome, 3), "agent");
            yield return Until(() => d.State == Director.Mode.World && d.Current != null && d.Current.Biome == biome, 25f, "the " + biome);
            yield return Until(() => d.Gather != null && d.Gather.Running && d.Gather.Items.Count > 0 && d.Gather.Items.All(i => i != null && i.Appear >= 0.6f), 20f, "pieces to gather");
        }

        IEnumerator GatherAll()
        {
            Debug.Log("[Agent] gathering " + d.Gather.Total + " pieces");
            int n = 0;
            foreach (var it in d.Gather.Items.ToList()) yield return CarryOne(it, n++);
            float end = Time.realtimeSinceStartup + 10f;
            while (d.Gather.Running && Time.realtimeSinceStartup < end) { Tick(); yield return null; }
            if (d.Gather.Running) { Debug.Log("[Agent] some pieces did not count: the rest are placed for me"); d.Gather.AutoPlace(); }
            yield return Until(() => !d.Gather.Running && d.Gather.PlacedCount == d.Gather.Total, 30f, "the build done");
            hand.position = d.Head.position + Vector3.up * 3f;
        }

        /// <summary>Presses the bright dock button; if the dock ignored the touch, presses it the way the tests do.</summary>
        IEnumerator PressDo(System.Func<bool> happened)
        {
            var b = Live("dock do");
            Debug.Log("[Agent] pressing the dock's bright button: " + (d.Dock != null ? d.Dock.ActionLabel : "?"));
            if (b != null) b.Press();
            yield return Idle(1f);
            if (!happened()) { Debug.Log("[Agent] the dock ignored it: OnDockForTests(do)"); d.OnDockForTests("do"); }
        }

        IEnumerator AnswerCall()
        {
            var m = d.Mission;
            Assert.NotNull(m, "Mara is in the scene");
            yield return Until(() => m.Active || (Live("dock do") != null && d.Dock != null && d.Dock.ActionLabel.Contains("nswer")), 45f, "Mara's call, or the button that answers her");
            if (!m.Active) yield return PressDo(() => m.Active);
            yield return Until(() => m.Now == Mission.Step.Answer, 40f, "her call, then the first choice");
            var guide = Live("Choice m:guide");
            Assert.NotNull(guide, "I'll guide you");
            Debug.Log("[Agent] answering Mara: " + guide.Id);
            guide.Press();
            yield return Until(() => m.Now == Mission.Step.Pace, 15f, "how she should fly in");
            Debug.Log("[Agent] saying: let's take it slow");
            d.Handle("let's take it slow", "agent");
            yield return Until(() => m.Now == Mission.Step.Storm && d.Emergency != null && d.Emergency.Running && d.Emergency.Sim != null, 5f, "her storm");
        }

        /// <summary>Plays the storm out: a hand in every ring (or none), sped up.</summary>
        IEnumerator PlayStorm(bool block, float speed, System.Action each)
        {
            var em = d.Emergency;
            Debug.Log("[Agent] storm, " + (block ? "blocking every ring" : "hands down") + ", x" + speed);
            if (!block) hand.position = d.Head.position + Vector3.up * 3f;
            Time.timeScale = speed;
            try
            {
                yield return Until(() => { if (block && em.Sim != null) hand.position = em.NextTarget; each?.Invoke(); return em.Sim == null || em.Sim.State.Phase >= Sim.Aftermath; },
                                   150f, "the storm to pass");
            }
            finally { Time.timeScale = 1f; }
        }

        IEnumerator PressRewind()
        {
            var em = d.Emergency;
            var rw = Live("rewind");
            Assert.NotNull(rw, "Rewind is there to press");
            Debug.Log("[Agent] pressing Rewind");
            rw.Press();
            yield return Until(() => em.Rewinding, 3f, "Rewind to start");
            yield return Until(() => !em.Rewinding && em.Sim != null && em.Sim.State.Phase < Sim.Aftermath, 10f, "the rewind, and the storm again");
        }

        /// <summary>Her landing (a Rewind if she was lost), the goodbye, and her sign-off.</summary>
        IEnumerator Land(System.Action each)
        {
            var m = d.Mission; var em = d.Emergency;
            for (int tries = 0; tries < 3; tries++)
            {
                yield return Until(() =>
                {
                    each?.Invoke();
                    return m.Now == Mission.Step.Farewell || !m.Active ||
                           (m.Now == Mission.Step.Storm && em.Sim != null && em.Sim.State.Phase >= Sim.Aftermath && em.RewindShown);
                }, 60f, "her landing, or Rewind");
                if (m.Now != Mission.Step.Storm) break;
                Debug.Log("[Agent] Mara is lost");
                yield return PressRewind();
                yield return PlayStorm(true, 4f, each);
            }
            Assert.AreEqual(Mission.Step.Farewell, m.Now, "her goodbye: " + Where());
            hand.position = d.Head.position + Vector3.up * 3f;
            var bye = Live("Choice m:wave") ?? Live("Choice m:bye");
            Debug.Log("[Agent] goodbye: " + (bye != null ? bye.Id : "a wave"));
            if (bye != null) bye.Press(); else m.Waved();
            yield return Until(() => m.Now == Mission.Step.Done, 30f, "she signs off");
        }

        IEnumerator ToMidStorm()
        {
            var em = d.Emergency;
            yield return Until(() => { if (em.Sim != null) hand.position = em.NextTarget;
                                       return em.Sim == null || (em.Sim.State.Phase == Sim.Storm && em.Sim.State.Next >= 1) || em.Sim.State.Phase >= Sim.Aftermath; }, 90f, "mid-storm");
            Assert.AreEqual(Sim.Storm, em.Sim.State.Phase, "still mid-storm");
        }

        // ---------- the mishaps ----------

        [UnityTest, Timeout(300000)]
        public IEnumerator StandsUpDuringTheStorm()
        {
            d.ResetProgress(new Progress { Onboarded = true });
            yield return OpenWorld("campfire");
            yield return GatherAll();
            yield return AnswerCall();
            yield return ToMidStorm();
            var em = d.Emergency;
            Assert.NotNull(d.Watch, "the safety watch");
            Vector3 seated = d.Head.position;
            Debug.Log("[Agent] standing up mid-storm (0.4 m, 6 s)");
            d.Head.position = seated + Vector3.up * 0.4f;
            hand.position = seated + Vector3.down * 0.6f;   // hands drop as you get up
            yield return Until(() => d.Watch.Unsafe, 3f, "standing noticed");
            float last = em.Sim.State.T, crept = 0f, start = Time.realtimeSinceStartup;
            bool faded = false;
            while (Time.realtimeSinceStartup - start < 6f)
            {
                float t = em.Sim != null ? em.Sim.State.T : last;
                if (d.Watch.Unsafe && t > last) crept += t - last;
                last = t;
                faded |= d.FadeQuad != null && d.FadeQuad.enabled;
                Tick();
                yield return null;
            }
            Debug.Log("[Agent] sitting back down; the storm clock moved " + crept.ToString("0.00") + " s while faded");
            d.Head.position = seated;
            yield return Until(() => !d.Watch.Unsafe, 3f, "seated again");
            Assert.IsTrue(faded, "the world faded to the room while standing");
            yield return PlayStorm(true, 4f, null);
            yield return Land(null);
            Assert.AreEqual(1, d.Progress.MissionChapter, "her first chapter is done");
            Assert.Less(crept, 0.05f, "the storm should hold while the screen is faded, but Sim.State.T advanced " + crept.ToString("0.00") + " s while d.Watch.Unsafe");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator LetsEveryMeteorThroughThenRewinds()
        {
            d.ResetProgress(new Progress { Onboarded = true });
            yield return OpenWorld("campfire");
            yield return GatherAll();
            yield return AnswerCall();
            var m = d.Mission; var em = d.Emergency;
            Assert.Greater(em.Defended, 0, "the sample room has furniture to defend");
            yield return PlayStorm(false, 6f, null);
            Assert.AreEqual(0, em.Sim.State.Blocked, "with no hand up, nothing is blocked");
            yield return Until(() => m.Now == Mission.Step.Storm && m.Result == Landing.Lost && em.RewindShown, 20f, "Mara lost, and Rewind offered");
            Assert.IsTrue(em.RoomBack, "every piece broke: your room is back");
            Assert.AreEqual(0, d.Progress.MissionChapter, "a lost chapter is not done");
            yield return Idle(3f);   // the player stares at the empty table a moment
            yield return PressRewind();
            Assert.AreEqual(0, em.Sim.State.Taken, "rewound to before the first miss");
            yield return PlayStorm(true, 4f, null);
            yield return Land(null);
            Assert.AreEqual(1, d.Progress.MissionChapter, "rewound and finished: her first chapter is done");
            Assert.AreNotEqual(Landing.Lost, m.Result, "she landed this time");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator HoldsThePalmUpEarlyThroughTheLanding()
        {
            d.ResetProgress(new Progress { Onboarded = true });
            yield return OpenWorld("campfire");
            yield return GatherAll();
            yield return AnswerCall();
            var m = d.Mission; var em = d.Emergency;
            Assert.NotNull(d.Panel, "the palm menu");
            string opened = "";
            System.Action watch = () =>
            {
                if (opened.Length == 0 && d.Panel != null && d.Panel.Open)
                {
                    opened = "mission " + m.Now + ", storm phase " + (em.Sim != null ? em.Sim.State.Phase : -1) + ", WantsPalm " + m.WantsPalm;
                    Debug.Log("[Agent] the menu opened over her landing: " + opened);
                }
            };
            Debug.Log("[Agent] palm up from the start of the storm, before she asks");
            hands.SimulatedPalmUp = true;
            yield return PlayStorm(true, 4f, watch);
            hand.position = d.Head.position + d.Head.forward * 0.36f + Vector3.down * 0.22f;
            yield return Until(() => { watch(); return m.WantsPalm || m.Now == Mission.Step.Farewell || !m.Active; }, 30f, "she asks for your hand");
            yield return Until(() => { watch(); return m.LandedInHand || !m.WantsPalm; }, 12f, "her ship in your hand");
            bool landed = m.LandedInHand;
            yield return Until(() => { watch(); return !m.WantsPalm; }, 8f, "she leaves your hand");
            float end = Time.realtimeSinceStartup + 1f;
            while (Time.realtimeSinceStartup < end) { watch(); Tick(); yield return null; }
            Assert.IsEmpty(opened, "the menu opened over her landing (palm held up early): " + opened);
            hands.SimulatedPalmUp = false;
            Assert.IsTrue(landed, "her ship came down in the hand held up early");
            yield return Land(watch);
            Assert.AreEqual(1, d.Progress.MissionChapter, "her first chapter is done");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator SaysTakeMeHomeMidStormThenAnswersAgain()
        {
            d.ResetProgress(new Progress { Onboarded = true });
            yield return OpenWorld("campfire");
            yield return GatherAll();
            yield return AnswerCall();
            yield return ToMidStorm();
            var m = d.Mission; var em = d.Emergency;
            Debug.Log("[Agent] saying: take me home");
            d.Handle("take me home", "voice");
            yield return Until(() => d.State == Director.Mode.Room, 25f, "your room");
            Assert.IsFalse(m.Active, "no call left running in the room");
            Assert.IsFalse(em.Running, "the storm ended with the world");
            Assert.AreEqual(0, d.Progress.MissionChapter, "her first chapter is still to play");
            yield return Idle(2f);
            yield return OpenWorld("meadow");
            yield return GatherAll();
            yield return AnswerCall();
            Assert.AreEqual(0, m.Chapter, "her first chapter again, from her call");
            Assert.AreEqual(0, em.Sim.State.Taken, "a fresh storm, nothing carried over");
            yield return PlayStorm(true, 4f, null);
            yield return Land(null);
            Assert.AreEqual(1, d.Progress.MissionChapter, "her first chapter is done");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator MenuMidGatherThenAnotherWorldWhileAPieceIsDark()
        {
            d.ResetProgress(new Progress { Onboarded = true });
            yield return OpenWorld("campfire");
            yield return GatherAll();
            yield return AnswerCall();
            yield return PlayStorm(true, 4f, null);   // her first storm lets its first meteor through: a piece goes dark
            yield return Land(null);
            Assert.AreEqual(1, d.Progress.MissionChapter, "her first chapter is done");
            if (d.Progress.DarkPieces.Count == 0)
            {
                Debug.Log("[Agent] nothing went dark: darkening a piece by hand");
                var p = d.Rooms.Room.Pieces.FirstOrDefault(x => !x.Virtual && x != d.Rooms.Room.Table && !string.IsNullOrEmpty(x.Key));
                Assert.NotNull(p, "a scanned piece with a key");
                d.Progress.DarkPieces.Add(p.Key);
            }
            var dark = d.Progress.DarkPieces.ToList();
            Debug.Log("[Agent] dark: " + string.Join(", ", dark));
            // Her next chapter: the next world, and its task.
            yield return Until(() => Live("dock do") != null && !d.Mission.Active && d.Dock.ActionLabel == "Next world", 40f, "Next world, after her chapter");
            string was = d.Current.Biome;
            yield return PressDo(() => d.State != Director.Mode.World || d.Current.Biome != was);
            yield return Until(() => d.State == Director.Mode.World && d.Current.Biome != was && d.Gather.Running &&
                                     d.Gather.Items.Count > 1 && d.Gather.Items.All(i => i != null && i.Appear >= 0.6f), 45f, "the next world's task");
            string first = d.Current.Biome;
            yield return CarryOne(d.Gather.Items.FirstOrDefault(i => i != null && !i.Placed), 0);
            yield return Idle(1.5f);   // past "only just put it down"
            Assert.IsTrue(d.Gather.Running, "still mid-gather");
            // Build 73: the menu opens from the wrist button only (palm-up toward the face is Quest's own gesture).
            Debug.Log("[Agent] the wrist menu, mid-gather");
            d.Panel.Show(HandPanel.Page.Main);
            yield return Until(() => d.Panel != null && d.Panel.Open, 5f, "the menu, mid-gather");
            var worlds = Live("map");
            Assert.NotNull(worlds, "Worlds on the menu");
            worlds.Press();
            yield return Until(() => d.Map != null && d.Map.Showing, 3f, "the map");
            yield return Idle(1f);   // the map ignores presses as it appears
            string to = Featured.Ids.FirstOrDefault(id => id != first);
            var tile = Live("map world:" + to);
            Assert.NotNull(tile, "the " + to + " tile on the map");
            Debug.Log("[Agent] changing world from the map to " + to + " with a piece still dark");
            tile.Press();
            yield return Until(() => d.State == Director.Mode.World && d.Current != null && d.Current.Biome == to, 30f, "the " + to);
            foreach (var k in dark) Assert.Contains(k, d.Progress.DarkPieces, "left mid-task: the dark piece stays dark, not lit and not forgotten");
            Assert.IsFalse(d.Panel.Open, "no menu left open over the new world");
            yield return Until(() => d.Gather.Running || WayForward(), 25f, "a way forward in the new world");
            yield return Idle(5f);
        }
    }
}
