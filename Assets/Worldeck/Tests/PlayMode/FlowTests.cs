using System.Collections;
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
    /// The whole experience in the real scene, with a simulated hand: first launch in a scanned living room,
    /// touch the light, the room opens into the campfire world, gather the kindling onto the real table, the
    /// fire roars, the palm panel, a new world by words, back to the room, the souvenir is on the table and
    /// opens its world again. Plus the safety fade and saved worlds.
    /// </summary>
    public class FlowTests
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

        /// <summary>
        /// Where people really let go (headset, Nov 12): across the table from the middle, and how far above its top.
        /// Every one of these missed in that build; every one must count now.
        /// </summary>
        static readonly Vector3[] RealReleases =
        {
            new Vector3(-0.401f, 0.463f, 0.577f), new Vector3(-0.825f, 0.353f, 0.29f), new Vector3(-0.815f, 0.349f, 0.228f),
            new Vector3(-0.338f, 0.471f, 0.372f), new Vector3(-0.246f, 0.354f, 0.395f), new Vector3(-0.268f, 0.399f, 0.528f),
            new Vector3(-0.28f, 0.453f, 0.568f), new Vector3(-0.153f, 0.402f, 0.272f), new Vector3(-0.291f, 0.351f, 0.454f),
            new Vector3(-0.143f, 0.279f, 0.431f), new Vector3(-0.346f, 0.43f, 0.598f), new Vector3(-0.169f, 0.154f, 0.462f),
        };
        int release;

        IEnumerator GatherAll()
        {
            yield return Until(() => d.Gather.Running && d.Gather.Items.All(i => i.Appear >= 0.6f), 15f, "gather pieces");
            var table = d.Rooms.Room.Table;
            foreach (var it in d.Gather.Items.ToList())
            {
                hand.position = it.transform.position;
                hands.SimulatedPinch = false;
                yield return Frames(2);
                hands.SimulatedPinch = true;
                yield return Frames(3);
                Assert.IsTrue(d.Gather.Holding(HandInput.SimulatedIndex), "picked up");
                // Carry it to where someone really let go, and let go there.
                var r = RealReleases[release++ % RealReleases.Length];
                Vector3 to = table.ToWorld(new Vector3(r.x, table.Size.y + r.y, r.z));
                Assert.IsTrue(d.Gather.OverTable(to), "a real release counts: " + r);
                // Carry it over about half a second of real time (a held piece follows the hand at headset frame rates).
                Vector3 from = hand.position;
                float t0 = Time.time;
                while (Time.time - t0 < 0.5f) { hand.position = Vector3.Lerp(from, to, (Time.time - t0) / 0.4f); yield return null; }
                hand.position = to;
                yield return new WaitForSeconds(0.15f);
                hands.SimulatedPinch = false;
                yield return new WaitForSeconds(0.6f);
            }
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator FirstLaunchToSouvenirAndBack()
        {
            Assert.AreEqual(Director.Mode.Room, d.State);
            Assert.IsFalse(d.Progress.Onboarded);
            // Touch the light: an animal to pick. Touch it again: straight to the campfire.
            d.Guide.Button.Press();
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice first:zebra"), 5f, "the first animals");
            yield return new WaitForSecondsRealtime(0.5f);
            d.Guide.Button.Press();
            yield return Until(() => d.State == Director.Mode.World, 25f, "the first world");
            Assert.AreEqual("campfire", d.Current.Biome, "the first world is the campfire");
            Assert.NotNull(d.Builder.Root);
            Assert.Greater(d.Skins.All.Count, 0, "furniture turned into the world's things");
            Assert.NotNull(d.Skins.TableSkin, "the table has a skin");
            Assert.Greater(d.Shell.Radius, d.Shell.FullRadius, "the walls have gone");
            Assert.Less(d.LastWordsToWorldMs, 8000f);

            yield return GatherAll();
            yield return Until(() => !d.Gather.Running && d.Gather.PlacedCount == d.Gather.Total, 10f, "the build to finish");
            Assert.AreEqual(d.FirstTaskItems, d.Gather.Total);
            Assert.IsTrue(d.Site.Done);
            Assert.AreEqual(1, d.Progress.Built["campfire"]);
            Assert.AreEqual(1, d.Progress.Souvenirs.Count, "a souvenir is earned");

            // A palm up is not the menu any more (it sat next to Quest's own gesture); the wrist button is.
            yield return new WaitForSeconds(5f);
            hand.position = d.Head.position + d.Head.forward * 0.3f - Vector3.up * 0.2f;
            hands.SimulatedPalmUp = true;
            yield return new WaitForSecondsRealtime(1f);
            Assert.IsFalse(d.Panel.Open, "palm up opens nothing");
            hands.SimulatedPalmUp = false;
            d.Panel.Show(HandPanel.Page.Main);   // as the wrist button does
            yield return Until(() => d.Panel.Open, 5f, "the panel");
            yield return Frames(5);
            Assert.IsTrue(d.Progress.Onboarded, "first-time guidance is done once the panel was found");

            // A new world by words, then an edit.
            d.Handle("take me to a snowy cabin at night", "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Biome == "snow", 25f, "the snow world");
            Assert.IsTrue(d.Current.IsNight);
            d.Handle("add northern lights", "test");
            yield return Frames(3);
            Assert.IsTrue(d.Current.Extras.Contains(Extras.Aurora));
            d.Handle("undo", "test");
            yield return Frames(3);
            Assert.IsFalse(d.Current.Extras.Contains(Extras.Aurora), "undo takes the lights away");
            d.Handle("save this as winter night", "test");
            Assert.AreEqual(1, d.Progress.Programs.Count);
            Assert.AreEqual("Winter night", d.Progress.Programs[0].Name);

            // Take me home: the room comes back with the souvenir on the table.
            var end = d.Panel;
            d.Handle("take me home", "test");
            yield return Until(() => d.State == Director.Mode.Room, 10f, "the room");
            Assert.IsNull(d.Builder.Root, "the world is gone");
            Assert.Less(d.Shell.Radius, 0f, "the walls are whole");
            Assert.AreEqual(1, d.Shelf.Count, "the souvenir stands on the table");

            // Saved for next time.
            var saved = Progress.Parse(System.IO.File.ReadAllText(System.IO.Path.Combine(Application.persistentDataPath, "worldeck-progress.json")), out bool damaged);
            Assert.IsFalse(damaged);
            Assert.AreEqual(1, saved.Souvenirs.Count);
            Assert.AreEqual(1, saved.Programs.Count);
            Assert.IsNotEmpty(saved.Souvenirs[0].Anchor, "remembered against the scanned table");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator StandingUpBringsTheRoomBack()
        {
            d.ResetProgress(new Progress { Onboarded = true });
            d.Open(Recipe.For("meadow", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "the meadow");
            yield return new WaitForSeconds(2f);
            Assert.IsFalse(d.Watch.Unsafe);
            Vector3 seated = d.Head.position;
            d.Head.position = seated + Vector3.up * 0.4f;
            yield return Until(() => d.Watch.Unsafe, 3f, "standing noticed");
            yield return new WaitForSeconds(1.2f);
            Assert.IsTrue(d.FadeQuad.enabled, "the world fades to the room");
            d.Head.position = seated;
            yield return Until(() => !d.Watch.Unsafe, 3f, "seated again");
            yield return new WaitForSeconds(1.2f);
            Assert.IsFalse(d.FadeQuad.enabled, "the world is back");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator StandingUpMidStormHoldsTheStorm()
        {
            // Meteors must never land while the world is faded to your room (Nov 17 review).
            d.ResetProgress(new Progress { Onboarded = true });
            d.Open(Recipe.For("meadow", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "the meadow");
            yield return new WaitForSeconds(2f);
            d.Emergency.Begin(d.Rooms.Room, d.Current, 7, Pace.Slow, d.Skins);
            yield return Until(() => d.Emergency.Running && d.Emergency.Sim.State.T > 1f, 10f, "the storm under way");
            Vector3 seated = d.Head.position;
            d.Head.position = seated + Vector3.up * 0.4f;
            yield return Until(() => d.Watch.Unsafe, 3f, "standing noticed");
            yield return new WaitForSeconds(1.5f);
            float t0 = d.Emergency.Sim.State.T;
            yield return new WaitForSeconds(3f);
            Assert.AreEqual(t0, d.Emergency.Sim.State.T, 0.05f, "the storm waits while you stand");
            d.Head.position = seated;
            yield return Until(() => !d.Watch.Unsafe, 3f, "seated again");
            yield return new WaitForSeconds(2f);
            Assert.Greater(d.Emergency.Sim.State.T, t0 + 0.5f, "and goes on once you sit");
            d.Emergency.Stop();
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator TheMapOffersComfortAndAccess()
        {
            d.ResetProgress(new Progress { Onboarded = true });
            yield return new WaitForSeconds(1f);
            d.OnPanelForTests("worlds");
            yield return null;
            var b = PokeButton.Live.FirstOrDefault(x => x.Id == "map settings" && x.isActiveAndEnabled);
            if (b == null) { d.Map.Show(d.Progress, "", false, "", false); yield return null; b = PokeButton.Live.FirstOrDefault(x => x.Id == "map settings" && x.isActiveAndEnabled); }
            Assert.NotNull(b, "Comfort & access on the map");
            yield return new WaitForSecondsRealtime(1f);   // the map ignores presses for 0.8 s as it appears
            b.Press();
            yield return Until(() => d.Panel.Open, 5f, "the Comfort & access page");
            d.Panel.Hide();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TheSticksMoveAndTurnYou()
        {
            d.ResetProgress(new Progress { Onboarded = true });
            d.Open(Recipe.For("meadow", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "the meadow");
            yield return Until(() => d.Gather.Running, 15f, "the task");
            d.Gather.Stop();
            Assert.AreEqual(0, d.Wonders.All.Count, "no beams until you ask to explore");
            d.OnPanelForTests("explore");
            yield return Until(() => d.Wonders.All.Count > 0, 10f, "the land opened");
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Move.SimulateSticks = true;
            Vector3 before = d.Head.position;
            Vector3 f = d.Head.forward; f.y = 0f; f.Normalize();
            d.Move.SimLeftStick = new Vector2(0f, 1f);
            yield return new WaitForSecondsRealtime(1.5f);
            d.Move.SimLeftStick = Vector2.zero;
            Vector3 moved = d.Head.position - before; moved.y = 0f;
            Assert.That(moved.magnitude, Is.InRange(2.6f, 4.4f), "left stick forward glides at a steady 2.4 m/s");
            Assert.Greater(Vector3.Dot(moved.normalized, f), 0.95f, "in the direction you face");
            Assert.IsTrue(d.Move.Away);
            float yaw = d.Head.eulerAngles.y;
            d.Move.SimRightStick = new Vector2(1f, 0f);
            yield return Frames(3);
            d.Move.SimRightStick = Vector2.zero;
            yield return Frames(2);
            Assert.AreEqual(30f, Mathf.Abs(Mathf.DeltaAngle(yaw, d.Head.eulerAngles.y)), 1f, "right stick turns one 30 degree step");
            Assert.AreEqual(1, d.Move.Turns);
            // Right stick up aims; letting go jumps.
            Vector3 at = d.Head.position;
            d.Move.SimRightStick = new Vector2(0f, 1f);
            yield return new WaitForSecondsRealtime(0.5f);
            d.Move.SimRightStick = Vector2.zero;
            yield return Frames(3);
            Assert.AreEqual(1, d.Move.Jumps, "right stick up, then let go: a jump");
            Assert.Greater(Vector3.Distance(at, d.Head.position), 1f);
            d.Move.SimulateSticks = false;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator JumpFindAWonderAndComeBackToCamp()
        {
            d.ResetProgress(new Progress { Onboarded = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "the campfire");
            yield return Until(() => d.Gather.Running, 15f, "the task");
            d.Gather.Stop();
            d.Loose.Stop();
            // Reaching out palm-down does nothing until you choose to explore (no accidental jumps during a task).
            Vector3 f0 = d.Head.forward; f0.y = 0f; f0.Normalize();
            hand.position = d.Head.position + f0 * 0.45f + Vector3.down * 0.28f;
            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(d.Move.Aiming(HandInput.SimulatedIndex), "no jump aim before Explore");
            d.OnPanelForTests("explore");
            yield return Until(() => d.Wonders.All.Count == 5, 10f, "five wonders");
            Assert.IsFalse(d.Move.Away);
            // Hold the hand out, palm down, pointing ahead: an arc. Pinch: jump.
            Vector3 f = d.Head.forward; f.y = 0f; f.Normalize();
            hand.position = d.Head.position + f * 0.45f + Vector3.down * 0.28f;
            hands.SimulatedPalmUp = false;
            hands.SimulatedPinch = false;
            yield return Until(() => d.Move.Aiming(HandInput.SimulatedIndex), 3f, "aiming");
            Vector3 before = d.Head.position;
            hands.SimulatedPinch = true;
            yield return Frames(3);
            hands.SimulatedPinch = false;
            yield return Frames(2);   // a quick pinch jumps as it is let go
            Assert.IsTrue(d.Move.Away, "jumped");
            float jumped = new Vector2(d.Head.position.x - before.x, d.Head.position.z - before.z).magnitude;
            Assert.That(jumped, Is.InRange(d.Move.MinJump, d.Move.MaxJump), "a jump of a sensible length");
            // Go to a wonder (as if walking there): it is found and remembered.
            hand.position = d.Head.position + Vector3.up * 3f;   // the hand out of the way
            var wonder = d.Wonders.All[0];
            Vector3 to = wonder.At - d.Head.position; to.y = 0f;
            d.Move.Rig.position += to;
            yield return Until(() => wonder.Found, 3f, "the wonder found");
            Assert.Contains("campfire/" + wonder.Name, d.Progress.Wonders);
            // The wonder sent a gift to the table; touching it changes the world.
            Assert.AreEqual(1, d.Gifts.Waiting, "a gift is waiting on the table");
            var gift = PokeButton.Live.First(x => x.Id.StartsWith("gift "));
            Assert.Less(Vector3.Distance(gift.transform.position, d.Rooms.Room.Table.TopCenter), 1.2f, "on the table");
            bool hadLanterns = d.Current.Extras.Contains(Extras.Lanterns);
            gift.Press();
            yield return Frames(3);
            Assert.AreEqual(0, d.Gifts.Waiting, "the gift was used");
            Assert.IsTrue(hadLanterns || d.Current.Extras.Contains(Extras.Lanterns), "the lantern lit the world");
            Assert.AreEqual(1, d.Wonders.FoundCount);
            // The terrain carries you: the floor follows the ground there.
            yield return new WaitForSeconds(0.5f);
            Assert.IsFalse(float.IsNaN(d.Move.Rig.position.y));
            // Back to camp from the menu: the real table lines up again.
            d.Move.Home(true, "test");
            yield return Frames(2);
            Assert.IsFalse(d.Move.Away, "home");
            Assert.Less(d.Move.FromCamp, 1.5f);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator PinchAndHoldGlidesWithAHand()
        {
            // With hands only, moving as freely as with the stick: hand out, palm down, pinch and hold.
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = 1 });
            d.Open(Recipe.For("meadow", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            yield return Until(() => d.Gather.Running, 15f, "the task");
            d.Gather.Stop(); d.Loose.Stop();
            d.OnPanelForTests("explore");
            yield return Until(() => d.Wonders.All.Count > 0, 10f, "exploring");
            Vector3 f = d.Head.forward; f.y = 0f; f.Normalize();
            hand.position = d.Head.position + f * 0.45f + Vector3.down * 0.28f;
            hands.SimulatedPalmUp = false; hands.SimulatedPinch = false;
            yield return Until(() => d.Move.Aiming(HandInput.SimulatedIndex), 3f, "aiming");
            Vector3 before = d.Head.position;
            float glided = d.Move.Glided;
            hands.SimulatedPinch = true;
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 1.6f) { hand.position = d.Head.position + f * 0.45f + Vector3.down * 0.28f; yield return null; }
            Assert.IsTrue(d.Move.HandGliding, "held: gliding");
            float moved = new Vector2(d.Head.position.x - before.x, d.Head.position.z - before.z).magnitude;
            Assert.Greater(moved, 1.5f, "it went where the hand points");
            Assert.Greater(d.Move.Glided, glided + 1.5f, "gliding, not jumping");
            Assert.Greater(Vector3.Dot(new Vector3(d.Head.position.x - before.x, 0f, d.Head.position.z - before.z).normalized, f), 0.8f, "forward");
            hands.SimulatedPinch = false;
            yield return Frames(3);
            Assert.IsFalse(d.Move.HandGliding, "let go: it stops");
            Vector3 stopped = d.Head.position;
            yield return new WaitForSecondsRealtime(0.5f);
            Assert.Less(Vector3.Distance(stopped, d.Head.position), 0.05f, "and stays");
            hand.position = d.Head.position + Vector3.up * 3f;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator EveryWorldOpensAndChanges()
        {
            d.ResetProgress(new Progress { Onboarded = true });
            d.Open(Recipe.For(Kit.Biomes[0].Id, 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "first world");
            foreach (var b in Kit.Biomes.Skip(1))
            {
                d.Open(Recipe.For(b.Id, 5), "test");
                yield return Until(() => d.State == Director.Mode.World && d.Current.Biome == b.Id, 25f, b.Id);
                Assert.NotNull(d.Builder.Root, b.Id);
                Assert.AreEqual(0f, d.Look.Veil, 0.001f, "the fog clears in " + b.Id);
                yield return new WaitForSeconds(0.3f);
            }
            d.Close("test");
            yield return Until(() => d.State == Director.Mode.Room, 10f, "room");
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator EveryWorldHasItsFiveWonders()
        {
            // Nov 15 on the headset: the beach is an island and only one of its five wonders fitted on land, while
            // Juniper said "Five wonders". Every world once, and the beach with several seeds (its island differs).
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            var worlds = Kit.Biomes.Select(b => (b.Id, 7)).Concat(new[] { 1, 2, 3, 4, 5, 6 }.Select(s => ("beach", s)));
            foreach (var (id, seed) in worlds)
            {
                var b = Kit.Biomes.First(x => x.Id == id);
                d.Open(Recipe.For(b.Id, seed), "test");
                yield return Until(() => d.State == Director.Mode.World && d.Current.Biome == b.Id, 25f, b.Id);
                d.Gather.Stop();
                d.Loose.Stop();
                d.OnPanelForTests("explore");
                yield return Until(() => d.Wonders.All.Count > 0, 10f, "wonders in " + b.Id);
                Assert.AreEqual(5, d.Wonders.All.Count, "five wonders in " + b.Id);
                float floor = d.Builder.GroundAt(d.Rooms.Room.Head.x, d.Rooms.Room.Head.z);
                foreach (var w in d.Wonders.All)
                {
                    if (b.Id != "reef") Assert.Greater(w.At.y, floor - 0.16f, w.Name + " is on land in " + b.Id);
                    Vector3 flat = w.At - d.Rooms.Room.Head; flat.y = 0f;
                    Assert.Greater(flat.magnitude, 7f, w.Name + " is out past your room in " + b.Id);
                    foreach (var o in d.Wonders.All)
                    {
                        if (o == w) continue;
                        Vector3 g = o.At - w.At; g.y = 0f;
                        Assert.GreaterOrEqual(g.magnitude, 5.99f, w.Name + " and " + o.Name + " are apart in " + b.Id + " " + seed);
                    }
                }
                StringAssert.Contains("Five wonders", d.Guide.Words.Current, "and Juniper's count is right");
            }
            d.Close("test");
            yield return Until(() => d.State == Director.Mode.Room, 25f, "room");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator SayAnythingMakesMovesChangesAndClearsThings()
        {
            d.ResetProgress(new Progress { Onboarded = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "the campfire");
            Assert.NotNull(d.Maker, "the maker is in the scene");
            try
            {
                // A library thing needs no helper and no network.
                ActAi.Canned = null;
                d.Handle("make me a lantern", "test");
                Assert.AreEqual(1, d.Maker.Things.Count, "a lantern, made by the app itself");
                var lantern = d.Maker.Things[0];
                yield return new WaitForSecondsRealtime(0.9f);
                Assert.Greater(lantern.T.localScale.x, 0.01f, "it popped up to its size");
                Assert.Less(Vector3.Distance(lantern.T.position, d.Head.position), 1.2f, "within reach, in front of you");
                float before = lantern.Size;
                d.Handle("bigger", "test");
                Assert.Greater(lantern.Size, before, "bigger");

                // Anything else is built by Juniper's helper from simple shapes.
                ActAi.Canned = said => "{\"safety\":\"ok\",\"say\":\"Two rubber ducks.\",\"source\":\"model\",\"actions\":[{\"do\":\"make\",\"name\":\"rubber duck\",\"kind\":null," +
                    "\"parts\":[\"sphere 0 0.3 0 0.7 0.55 0.5 0 0 0 #f5c518\",\"sphere 0.25 0.7 0 0.35 0.35 0.35 0 0 0 #f5c518\",\"cone 0.47 0.68 0 0.12 0.2 0.12 0 0 -90 #ef8a2b\"]," +
                    "\"place\":\"table\",\"count\":2,\"size\":0.2}]}";
                d.Handle("make me two rubber ducks on the table", "test");
                yield return Until(() => d.Maker.Things.Count == 3, 5f, "two ducks from the helper");
                Assert.IsFalse(d.Maker.Working, "the shimmer stops once the answer is in");
                var duck = d.Maker.Things[2];
                Assert.AreEqual("rubber duck", duck.Name);
                Assert.AreEqual(d.Rooms.Room.Table.TopY, duck.T.position.y, 0.02f, "it stands on your real table");
                yield return new WaitForSecondsRealtime(0.9f);

                // Pinch it and carry it.
                d.Gather.Stop();
                d.Loose.Stop();
                yield return Pinch(duck.T.position + Vector3.up * (duck.Size * 0.4f));
                Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "picked up the duck");
                Vector3 to = hand.position + Vector3.up * 0.2f;
                hand.position = to;
                yield return new WaitForSecondsRealtime(0.4f);
                Assert.Less(Vector3.Distance(duck.T.position + Vector3.up * (duck.Size * 0.4f), to), 0.05f, "it follows the hand");
                hands.SimulatedPinch = false;
                yield return Frames(3);
                Assert.IsFalse(d.Maker.Holding(HandInput.SimulatedIndex), "let go");

                d.Handle("remove the rubber duck", "test");
                Assert.AreEqual(2, d.Maker.Things.Count, "one duck removed");

                // The helper can't be reached: nothing breaks, nothing is made.
                ActAi.Canned = said => null;
                d.Handle("make me a typewriter", "test");
                yield return Frames(5);
                Assert.AreEqual(2, d.Maker.Things.Count);
                Assert.IsFalse(d.Maker.Working);

                // More than twelve: the oldest go.
                ActAi.Canned = null;
                for (int i = 0; i < 5; i++) d.Handle("make me three shells", "test");
                Assert.AreEqual(Maker.MaxThings, d.Maker.Things.Count, "never more than twelve things");
            }
            finally { ActAi.Canned = null; }
            d.Close("test");
            yield return Until(() => d.State == Director.Mode.Room, 25f, "the room again");
            Assert.AreEqual(0, d.Maker.Things.Count, "your things go when the world closes");
        }

        [UnityTest, Timeout(200000)]
        public IEnumerator FurnitureSheLostStaysDarkUntilYouLightItAgain()
        {
            // After her first chapter, every scanned piece of this room is remembered dark.
            var p = new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, SeenBreak = true, MissionChapter = 1 };
            p.RememberChapter(0, Landing.Rough, "m:slow", "");
            d.ResetProgress(p);
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return Until(() => d.State == Director.Mode.Room, 10f, "your room");
            // (Not the work table: the storm never aims at it, so it is never dark.)
            foreach (var piece in d.Rooms.Room.Pieces) if (!piece.Virtual && piece != d.Rooms.Room.Table && !string.IsNullOrEmpty(piece.Key)) d.Progress.DarkPieces.Add(piece.Key);
            Assert.Greater(d.Progress.DarkPieces.Count, 1, "the sample room is scanned, with keys for its furniture");
            // Straight to her storm: what is dark starts dark, but one light always stays on.
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            var m = d.Mission;
            d.StartMission("test");
            Assert.IsNotEmpty(m.DarkNow, "she knows a piece is dark");
            yield return Until(() => m.LastLine.Contains("still dark"), 20f, "she notices");
            yield return Until(() => m.Now == Mission.Step.Answer, 30f, "her first choice");
            PokeButton.Live.First(b => b.Id == "Choice m:guide").Press();
            yield return Until(() => m.Now == Mission.Step.Pace, 10f, "the pace");
            d.Handle("let's take it slow", "test");
            var em = d.Emergency;
            yield return Frames(3);
            Assert.IsFalse(em.Sim.Strikes.Any(s => s.Kind == Sim.Big), "the big one waits for her last chapter");
            int dark = Enumerable.Range(0, em.Defended).Count(k => em.PieceBreak(k) >= 0.99f);
            Assert.GreaterOrEqual(dark, 1, "the dark piece starts dark");
            Assert.Less(dark, em.Defended, "one light always stays on for her");
            Assert.Less(m.Signal, 1f, "her signal is weaker for it");
            // Her storm, played out: every meteor blocked. What was aimed at and stayed whole is lit; what was dark
            // stays dark (blocking doesn't light it), and that is saved.
            Time.timeScale = 4f;
            yield return Until(() => { hand.position = em.NextTarget; return em.Sim.State.Phase >= Sim.Aftermath; }, 90f, "the storm");
            Time.timeScale = 1f;
            yield return Until(() => m.Now == Mission.Step.Farewell, 30f, "her landing, then the goodbye");
            m.Waved();
            yield return Until(() => m.Now == Mission.Step.Done, 20f, "she signs off");
            Assert.AreEqual(2, d.Progress.MissionChapter, "her second chapter is done");
            Assert.IsNotEmpty(m.EndDark, "the piece that started dark ended dark");
            foreach (var k in m.EndDark) Assert.Contains(k, d.Progress.DarkPieces, "what ended dark is remembered");
            foreach (var k in m.EndDefended) if (!m.EndDark.Contains(k)) Assert.IsFalse(d.Progress.DarkPieces.Contains(k), "what held is lit");
            var saved = Progress.Parse(d.Progress.ToJsonString(), out _);
            foreach (var k in m.EndDark) Assert.Contains(k, saved.DarkPieces, "and it is saved");
            // A quiet moment after her chapter: name a place, with voice offered alongside (review 30, 31).
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice voice:on" && b.isActiveAndEnabled), 25f, "voice offered after her chapter");
            Assert.IsTrue(PokeButton.Live.Any(b => b.Id == "Choice say:a volcano at night"), "next to a place to try");
            d.Guide.ShowChoices(null, null);
            // The next world: the task lights it again, and she sees it.
            d.Close("test");
            yield return Until(() => d.State == Director.Mode.Room, 25f, "home");
            d.Open(Recipe.For("campfire", 4), "test");
            yield return Until(() => d.State == Director.Mode.World && d.Gather.Running, 25f, "the task");
            yield return Frames(3);
            StringAssert.Contains("light your", d.Dock.Next, "the task says what it is for");
            d.Gather.AutoPlace();
            yield return Until(() => !d.Gather.Running && d.Gather.PlacedCount == d.Gather.Total, 30f, "the task done");
            yield return Frames(2);
            StringAssert.Contains("lit again", d.Guide.Words.Current, "Juniper says so");
            Assert.AreEqual(0, d.Progress.DarkPieces.Count(k => d.Rooms.Room.Pieces.Any(x => x.Key == k)), "nothing in this room is dark now");
            // Her line is read in full: no "Look up!" and no roast over it (review 30: it was cut at 5 s), and the
            // bright button answers her.
            yield return new WaitForSecondsRealtime(5.6f);
            StringAssert.Contains("lit again", d.Guide.Words.Current, "still there to read");
            Assert.IsFalse(d.Campfire != null && d.Campfire.Running, "no roast on the visit that lit it");
            Assert.AreEqual("Answer Mara", d.Dock.ActionLabel, "the bright button she names");
            // And nothing else starts: the world beyond waits until her story is told. (Explore would have opened
            // about 19 s after the relight: review 31.)
            yield return new WaitForSecondsRealtime(15f);
            Assert.IsTrue(d.Wonders == null || d.Wonders.All.Count == 0, "no wonders open by themselves mid-story");
            Assert.AreEqual("Answer Mara", d.Dock.ActionLabel, "still the one next step");
            d.StartMission("test");
            yield return Until(() => m.LastLine.Contains("lit again"), 20f, "and she sees it");
            m.Stop();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator EyesOnlyGathersWithAHandRestingInView()
        {
            // For someone who can't reach or pinch: Eyes only, a hand resting in view (as on a lap), and looking alone
            // brings every piece to the table.
            d.ResetProgress(new Progress { Onboarded = true, LookToPress = true });
            d.Open(Recipe.For("meadow", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "the meadow");
            yield return Until(() => d.Gather.Running && d.Gather.Items.All(i => i.Appear >= 0.6f), 15f, "the pieces");
            hand.position = d.Head.position + Vector3.down * 0.45f + d.Head.forward * 0.15f;   // resting, in view
            yield return Frames(3);
            Assert.IsTrue(d.Hands.AnyTracked, "a hand is in view");
            Assert.IsTrue(d.Gather.EyesOnly && Emergency.EyesToo, "Eyes only reaches the pieces and the storm");
            Vector3 eye = d.Head.position;
            foreach (var it in d.Gather.Items.ToList())
            {
                if (it == null || it.Placed) continue;
                d.Head.rotation = Quaternion.LookRotation(it.transform.position - eye);
                yield return Until(() => it == null || it.Placed || it.HeldBy >= 0 || !d.Gather.Running, 12f, "a piece looked at");
            }
            yield return Until(() => !d.Gather.Running || d.Gather.PlacedCount == d.Gather.Total, 20f, "every piece on the table by eye");
            d.ResetProgress(new Progress { Onboarded = true });
            yield return null;
            Assert.IsFalse(Emergency.EyesToo, "off again with the setting");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TheFirstScreenOffersComfortAndAccess()
        {
            d.ResetProgress(new Progress());
            hand.position = d.Head.position + Vector3.down * 0.2f + d.Head.forward * 0.35f;
            d.StartFirstRunForTests();
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice welcome:calm" && b.isActiveAndEnabled), 20f, "the light choice first");
            StringAssert.Contains("flash", d.Guide.Words.Current, "it says the storms flash");
            yield return new WaitForSecondsRealtime(0.8f);
            PokeButton.Live.First(b => b.Id == "Choice welcome:calm").Press();
            yield return Until(() => d.Progress.CalmEffects, 3f, "Calm effects chosen");
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice welcome:access" && b.isActiveAndEnabled), 20f, "Comfort & access on the welcome");
            PokeButton.Live.First(b => b.Id == "Choice welcome:access").Press();
            yield return Until(() => d.Panel.Open, 5f, "the Comfort & access page");
            Assert.IsTrue(PokeButton.Live.Any(b => b.isActiveAndEnabled && b.Id == "set:look"), "Hands-free offered (its label may wrap)");
            d.Panel.Hide();
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice first:zebra" && b.isActiveAndEnabled), 10f, "then the animals");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator HowItWorksSitsAboveALowDeskAndAloneAnswers()
        {
            // Nov 17: a desk 0.35 m below the eyes and 0.35 m away put "Got it" under its top. The card lifts clear.
            var t = d.Rooms.Room.Table;
            Vector3 toTable = t.TopCenter - d.Head.position; toTable.y = 0f;
            d.Head.position = new Vector3(d.Head.position.x, t.TopY + 0.35f, d.Head.position.z) + toTable.normalized * 0.10f;
            yield return null;
            d.Help.Show(false, false);
            yield return null;
            var ok = PokeButton.Live.First(b => b.Id == "help ok" && b.isActiveAndEnabled);
            Assert.GreaterOrEqual(ok.transform.position.y - ok.Size.y * 0.5f, t.TopY + 0.025f, "Got it clear of the desk");
            foreach (var b in PokeButton.Live.Where(b => b.Id.StartsWith("help ") && b.isActiveAndEnabled))
                Assert.LessOrEqual(Vector3.Distance(b.transform.position, d.Head.position), 0.58f, "within reach: " + b.Id);
            // While it is up only its buttons answer (Nov 17: a controller's ray opened a world through it).
            Assert.IsTrue(d.Pokes.Allowed(ok), "its own button");
            var other = PokeButton.Live.FirstOrDefault(b => b != null && !b.Id.StartsWith("help ") && b.isActiveAndEnabled);
            if (other != null) Assert.IsFalse(d.Pokes.Allowed(other), "not " + other.Id);
            d.Help.Hide();
            if (other != null) Assert.IsTrue(d.Pokes.Allowed(other), "and everything again once it is closed");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ATouchOnJuniperDuringTheLightChoiceLeavesNoDeadCard()
        {
            // Review 42: touching Juniper while the light choice was up brought the animals, then a welcome card with
            // buttons that did nothing. Now the welcome steps aside.
            d.ResetProgress(new Progress());
            hand.position = d.Head.position + Vector3.down * 0.2f + d.Head.forward * 0.35f;
            d.StartFirstRunForTests();
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice welcome:fx" && b.isActiveAndEnabled), 20f, "the light choice");
            var g = PokeButton.Live.FirstOrDefault(b => b.Id == "Guide" && b.isActiveAndEnabled);
            Assert.NotNull(g, "Juniper can be touched");
            g.Press();
            yield return Until(() => !d.Welcoming, 30f, "the welcome steps aside");
            yield return new WaitForSecondsRealtime(1f);
            Assert.IsFalse(PokeButton.Live.Any(b => b.isActiveAndEnabled && (b.Id == "Choice welcome:go" || b.Id == "Choice welcome:show")), "no dead welcome buttons");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AWelcomeCardOnceThenTheAnimals()
        {
            // First launch: a card says what Worldeck is; "Show me how" opens How it works; closing it, the animals.
            d.ResetProgress(new Progress());
            hand.position = d.Head.position + Vector3.down * 0.2f + d.Head.forward * 0.35f;   // hands in view
            d.StartFirstRunForTests();
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice welcome:fx" && b.isActiveAndEnabled), 20f, "the light choice");
            yield return new WaitForSecondsRealtime(0.8f);
            PokeButton.Live.First(b => b.Id == "Choice welcome:fx").Press();
            yield return Until(() => d.Welcoming && PokeButton.Live.Any(b => b.Id == "Choice welcome:show" && b.isActiveAndEnabled), 20f, "the welcome card");
            Assert.IsFalse(d.Progress.CalmEffects, "full effects kept");
            StringAssert.Contains("Welcome to Worldeck", d.Guide.Words.Current);
            yield return new WaitForSecondsRealtime(8f);
            Assert.IsTrue(d.Welcoming, "it stays until pressed");
            PokeButton.Live.First(b => b.Id == "Choice welcome:show").Press();
            yield return Until(() => d.Help.Showing, 5f, "How it works");
            // Every button on it within a hand's reach, like the map's (Nov 17: none was, and a controller came out).
            var helpButtons = PokeButton.Live.Where(b => b.Id.StartsWith("help ") && b.isActiveAndEnabled).ToList();
            Assert.GreaterOrEqual(helpButtons.Count, 6, "Show me buttons and Got it");
            foreach (var b in helpButtons)
                Assert.LessOrEqual(Vector3.Distance(b.transform.position, d.Head.position), 0.56f, "within reach: " + b.Id);
            Assert.IsTrue(d.Progress.WelcomeSeen, "seen: never again");
            d.Help.Hide();
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice first:zebra" && b.isActiveAndEnabled), 10f, "then the animals");
            Assert.IsFalse(d.Welcoming);
            Assert.IsTrue(Progress.Parse(d.Progress.ToJsonString(), out _).WelcomeSeen, "and saved");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator SomeoneAsksAFavourAndRemembersYou()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Handle("a pirate cove", "test");
            yield return Until(() => d.State == Director.Mode.World && d.Local != null, 25f, "the pirate cove, and its captain");
            d.Gather.Stop();
            yield return Until(() => d.Local.Greeted, 10f, "his hello");
            d.OnLocalForTests("favour");
            Assert.AreEqual("parrot", d.FavourWanted, "he asks for a parrot");
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice say:make me a parrot" && b.isActiveAndEnabled), 5f, "a button to make it");
            PokeButton.Live.First(b => b.Id == "Choice say:make me a parrot").Press();
            yield return Until(() => d.Progress.FavourDone("pirate_cove"), 10f, "the favour done");
            StringAssert.Contains("Ahoy, matey", d.LocalSaid, "he thanks you");
            // Next time, he remembers.
            d.Close("test");
            yield return Until(() => d.State == Director.Mode.Room, 25f, "home");
            d.Handle("a pirate cove", "test");
            yield return Until(() => d.State == Director.Mode.World && d.Local != null && d.Local.Greeted, 30f, "back, and his hello");
            StringAssert.Contains("You're back", d.LocalSaid, "he remembers you");
            StringAssert.Contains("parrot", d.LocalSaid, "and the parrot");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator WordsNeverSitOnOtherWords()
        {
            // Nov 16 on the headset: a local's hello on top of Juniper's line and the task's line.
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, SeenBreak = true, MissionChapter = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World && d.Gather.Running, 25f, "a world with its task");
            // Three people talking at the same spot in front of you: the words make room for each other.
            Vector3 at = d.Head.position + d.Head.forward * 0.8f + Vector3.up * 0.05f;
            var a = Caption.Make("Test words A", null, d.Guide.Font, d.Guide.Panel, d.Head, 15f, 26f);
            var b = Caption.Make("Test words B", null, d.Guide.Font, d.Guide.Panel, d.Head, 15f, 26f);
            var c = Caption.Make("Test words C", null, d.Guide.Font, d.Guide.Panel, d.Head, 15f, 26f);
            a.Priority = 9; b.Priority = 5; c.Priority = 3;
            try
            {
                a.transform.position = b.transform.position = c.transform.position = at;
                a.Say("The one who matters most keeps her place, right here.", 0f);
                b.Say("Someone who lives here says hello at the same spot.", 0f);
                c.Say("And a hint, also right there.", 0f);
                yield return new WaitForSecondsRealtime(1.2f);
                // (Read as the layout left the last frame: words are moved last in each frame.)
                Assert.AreEqual(0, CaptionLayout.LastOverlaps, "no words on top of other words");
                Assert.AreEqual(0, CaptionLayout.Unresolved);
                Assert.Less(Mathf.Abs(a.LayoutShift), 0.0001f, "the most important stays where it was put");
                Assert.Greater(Mathf.Abs(b.LayoutShift), 0.005f, "the others moved aside");
                // When the first one goes, the others come back to where they were put.
                a.Clear(); c.Clear();
                yield return new WaitForSecondsRealtime(1.2f);
                Assert.Less(Mathf.Abs(b.LayoutShift), 0.001f, "back in its place once the way is clear");
            }
            finally { Object.Destroy(a.gameObject); Object.Destroy(b.gameObject); Object.Destroy(c.gameObject); }
            // The real thing: Mara's call (her title, her words) with Juniper and the task's line on show.
            d.StartMission("test");
            float until = Time.unscaledTime + 14f;
            int worst = 0;
            string which = "";
            while (Time.unscaledTime < until)
            {
                yield return new WaitForSecondsRealtime(0.5f);
                if (CaptionLayout.LastOverlaps > worst) { worst = CaptionLayout.LastOverlaps; which = CaptionLayout.LastOverlapNames; }
            }
            Assert.AreEqual(0, worst, "nothing overlapped during her call: " + which);
            d.Mission.Stop();
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator AreYouThereGetsAnAnswer()
        {
            // Nov 16 on the headset: three words with nothing to act on got a list of worlds, and felt like not listening.
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = 1 });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.Handle("are you there", "voice");
            yield return Frames(2);
            StringAssert.Contains("I can hear you", d.Guide.Words.Current, "she answers");
            Assert.IsTrue(PokeButton.Live.Any(b => b.Id == "Choice say:make it rain" && b.isActiveAndEnabled), "and shows what works, to touch");
            Assert.IsFalse(d.Map.Showing, "not a list of worlds");
            PokeButton.Live.First(b => b.Id == "Choice say:make it rain").Press();
            yield return Frames(3);
            Assert.AreEqual(Director.Mode.World, d.State, "the same world, now raining");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator AScanFoundInAWorldIsUsedBeforeHerChapter()
        {
            // Nov 15, build 51: launched while the headset slept, the scan was found only once the player went home,
            // so her chapter in between defended none of their furniture.
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, SeenBreak = true, MissionChapter = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return Until(() => d.State == Director.Mode.Room, 10f, "your room");
            var scan = MRUK.Instance.GetCurrentRoom();
            d.Rooms.NoScanForTests();
            Assert.IsFalse(d.Rooms.Room.Scanned, "no scan yet");
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.Gather.Stop();
            // The scan turns up while you are in the world: it is held, the world is not changed under you.
            d.Rooms.FoundInWorldForTests(scan);
            yield return Frames(5);
            Assert.IsFalse(d.Rooms.Room.Scanned, "not swapped in under the world");
            // Her call: the world is rebuilt round your furniture first, then she calls.
            d.StartMission("test");
            yield return Until(() => d.Mission.Active, 30f, "her call, in the rebuilt world");
            Assert.IsTrue(d.Rooms.Room.Scanned, "your furniture is in it");
            Assert.IsFalse(d.Rooms.HasHeld);
            Assert.Greater(d.Skins.All.Count, 1, "the world is built round your real furniture");
            d.Mission.Stop();
            d.Close("test");
            yield return Until(() => d.State == Director.Mode.Room, 25f, "home");
            // Found while a world is opening: that world is built round it (review 32: it was held back instead).
            d.Rooms.NoScanForTests();
            yield return Frames(2);
            // (Held as the world starts to open, as when a look for it finishes while the opening waits for it.)
            d.Rooms.FoundInWorldForTests(scan);
            d.Open(Recipe.For("meadow", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "the world");
            Assert.IsTrue(d.Rooms.Room.Scanned, "the world opened round your furniture");
            Assert.IsFalse(d.Rooms.HasHeld, "nothing left held");
            d.Close("test");
            yield return Until(() => d.State == Director.Mode.Room, 25f, "home again");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator SoftMusicPlaysAndCanBeTurnedOff()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, SeenBreak = true, MissionChapter = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return Until(() => d.State == Director.Mode.Room, 10f, "your room");
            var sfx = Sfx.Instance;
            Assert.NotNull(sfx);
            yield return Until(() => sfx.MusicName == Director.CalmMusic && sfx.MusicLevel > Director.CalmMusicVolume * 0.9f, 6f, "soft music in your room, faded in");
            Assert.LessOrEqual(sfx.MusicLevel, Director.CalmMusicVolume + 0.001f, "and soft");
            // In a world it carries on; Mara's radio and her storm come first.
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            Assert.AreEqual(Director.CalmMusic, sfx.MusicName, "the same calm piece in a world");
            // One thing in front (Nov 15 on the headset: waves, birds and music together were too much): under the
            // music, the world's own sound is a faint layer.
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.Greater(sfx.BedLevel, 0f, "the world's own sound is there");
            Assert.LessOrEqual(sfx.BedLevel, sfx.BedVolume * Director.BedUnderMusic + 0.001f, "faint, under the music");
            d.StartMission("test");
            yield return Until(() => sfx.MusicLevel < 0.1f, 6f, "it dips under her radio");
            Assert.Greater(sfx.MusicLevel, 0f, "dips, not stops");
            d.Mission.Stop();
            // Settings: off, and remembered.
            d.OnPanelForTests("set:music");
            yield return Until(() => !sfx.MusicPlaying, 6f, "it fades out");
            yield return Until(() => sfx.BedLevel >= sfx.BedVolume * 0.99f, 4f, "with the music off, the world's own sound comes back");
            Assert.IsFalse(d.Progress.MusicOn, "off is remembered");
            Assert.IsFalse(Progress.Parse(d.Progress.ToJsonString(), out _).MusicOn, "and saved");
            d.Handle("play some music", "test");
            yield return Until(() => sfx.MusicPlaying, 4f, "and back by voice");
            Assert.IsTrue(d.Progress.MusicOn);
            Assert.AreNotEqual(Director.CalmMusic, sfx.MusicName, "in a world, asked for: that world's own tune");
            // The city's ambience is jazz already: the calm music steps aside there (no two tunes at once).
            d.Open(Recipe.For("city", 3), "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Biome == "city", 25f, "the city");
            Assert.IsTrue(sfx.BedIsMusic, "the city's own sound is music");
            yield return Until(() => sfx.MusicLevel < 0.01f, 8f, "so the background music fades away");
            Assert.Greater(sfx.BedLevel, sfx.BedVolume * 0.9f, "and the city's jazz plays as it is");
            Assert.AreEqual(1, sfx.BedSources, "from one source: four copies a quarter apart were four chords at once");
            // Asked for, a tune plays in the city too ("Here's a tune" was said over silence), and the jazz makes way.
            d.Handle("play some music", "test");
            yield return Until(() => sfx.MusicLevel > 0.2f, 6f, "a tune, asked for");
            Assert.Less(sfx.BedLevel, sfx.BedVolume * 0.25f, "the city's own jazz makes way");
            // The jukebox (a loop that is music) steps back under Mara's radio (review 32: it played on at full).
            sfx.Loop("jukebox_music", d.Rooms.Room.Table.TopCenter, 0.55f);
            yield return Frames(2);
            Assert.Greater(sfx.LoopLevel, 0.5f, "the jukebox plays");
            d.StartMission("test");
            yield return Until(() => sfx.LoopLevel < 0.2f, 6f, "it steps back under her radio");
            d.Mission.Stop();
            yield return Until(() => sfx.LoopLevel > 0.5f, 6f, "and comes back after");
            sfx.Loop(null, Vector3.zero, 0f);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator TheExtrasWaitForHerFirstChapter()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            yield return Frames(3);
            d.Panel.Show(HandPanel.Page.Main);
            yield return Frames(2);
            Assert.IsTrue(PokeButton.Live.Any(b => b.Id == "map" && b.isActiveAndEnabled), "Worlds is there");
            Assert.IsFalse(PokeButton.Live.Any(b => (b.Id == "tour" || b.Id == "explore") && b.isActiveAndEnabled), "no Tour or Explore before her first chapter");
            d.Progress.MissionChapter = 1;
            d.Panel.Refresh();
            yield return Frames(2);
            Assert.IsTrue(PokeButton.Live.Any(b => b.Id == "tour" && b.isActiveAndEnabled), "after it, they're back");
            d.Panel.Hide();
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator HerStoryShowsItsChaptersAndEndsInYourRoom()
        {
            // After her first chapter: a rough landing, the long way, and the shelf held.
            var p = new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, SeenBreak = true, MissionChapter = 1 };
            p.RememberChapter(0, Landing.Rough, "m:slow", "shelf");
            d.ResetProgress(p);
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return Until(() => d.State == Director.Mode.Room, 10f, "your room");
            yield return Frames(3);
            d.Shelf.Show(d.Progress, d.Rooms.Room);
            Assert.AreEqual(2, d.Shelf.EmptyPlaces, "two empty places on your table for the keepsakes to come");
            // Her second call: the title card first, then she remembers.
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            var m = d.Mission;
            d.StartMission("test");
            yield return Until(() => m.TitleWords != null && m.TitleWords.Showing, 3f, "the chapter's title");
            StringAssert.Contains("CHAPTER 2 OF 3", m.TitleWords.Current);
            Assert.IsFalse(m.Words != null && m.Words.Showing, "the title is read before she speaks");
            yield return Until(() => m.LastLine.Contains("cracked"), 15f, "she remembers the rough landing");
            yield return Until(() => m.Now == Mission.Step.Answer, 25f, "then the first choice");
            m.Stop();
            // Her last chapter, flown for real: the storm, the landing, the goodbye (review 31: the dock was tested
            // only after a stopped call, not in the storm's aftermath).
            d.Progress.MissionChapter = Story.Chapters - 1;
            d.Progress.RememberChapter(1, Landing.Clean, "m:fast", "shelf");
            d.Progress.StoryEndSeen = false;
            d.Progress.LastFlight = "";
            d.StartMission("test");
            yield return Until(() => m.Now == Mission.Step.Answer, 40f, "her last call");
            PokeButton.Live.First(b => b.Id == "Choice m:guide").Press();
            yield return Until(() => m.Now == Mission.Step.Pace, 10f, "the pace");
            d.Handle("let's take it slow", "test");
            var em = d.Emergency;
            Time.timeScale = 4f;
            yield return Until(() => { hand.position = em.NextTarget; return em.Sim.State.Phase >= Sim.Aftermath; }, 120f, "her last storm");
            Time.timeScale = 1f;
            yield return Until(() => m.Now == Mission.Step.Farewell, 30f, "her landing, then the goodbye");
            m.Waved();
            // (Her last goodbye is longer: her reply, the keepsake, and the story's last words.)
            yield return Until(() => m.Now == Mission.Step.Done, 45f, "she signs off");
            Assert.AreEqual(Story.Chapters, d.Progress.MissionChapter, "her story is told");
            // After her last chapter the bright button goes home, where the story ends (review 30: it said Next world,
            // and Explore opened by itself), and nothing else opens meanwhile.
            yield return new WaitForSecondsRealtime(9f);
            Assert.AreEqual("My room", d.Dock.ActionLabel, "the one next step is home");
            StringAssert.Contains("ends in your room", d.Dock.Next, "and the line says why");
            Assert.IsTrue(d.Wonders == null || d.Wonders.All.Count == 0, "the world beyond waits until the ending is seen");
            d.Progress.RememberChapter(2, Landing.Clean, "m:slow", "shelf");
            d.OnDockForTests("do");
            yield return Until(() => d.State == Director.Mode.Room, 25f, "home");
            Assert.IsTrue(d.Progress.StoryEndSeen, "the ending is shown once");
            Assert.AreEqual(0, d.Shelf.EmptyPlaces, "all three keepsakes are on the table");
            yield return Until(() => d.Guide.Words.Current.Contains("never went dark"), 25f, "Juniper tells you what you did");
            StringAssert.Contains("shelf", d.Guide.Words.Current, "naming the piece that held every time");
            // Nothing comes up over the ending (Nov 16, a recorded run: the map opened over it 9 s in).
            Assert.IsTrue(d.StoryEnding, "the ending is still playing");
            yield return new WaitForSecondsRealtime(6f);
            Assert.IsFalse(d.Map.Showing, "no map over her story's last words");
            StringAssert.Contains("never went dark", d.Guide.Words.Current, "they are read in full");
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator MaraCallsYouAnswerChooseAndGuideHerDown()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, SeenBreak = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            hand.position = d.Head.position + Vector3.up * 3f;
            var m = d.Mission;
            Assert.NotNull(m, "the mission is in the scene");
            d.StartMission("test");
            Assert.IsTrue(m.Active, "Mara is calling");
            yield return Until(() => m.Now == Mission.Step.Answer, 25f, "her call, then the first choice");
            Assert.NotNull(m.Mara);
            Assert.Greater(m.Mara.transform.localScale.y, 0.5f, "she has appeared");
            Assert.IsTrue(m.Mara.IsFigure, "Mara is the animated figure, not shapes built in code");
            Assert.IsNotEmpty(m.Mara.Playing, "and she is animated");
            // Her weak signal never strobes, on the figure players actually see. Sampled frame by frame through what the
            // storm does to her: a glitch, a hit that drops her signal in one step, and the signal coming back. Neither
            // how see-through she is nor her outline ever jumps. (A coroutine reads the values the previous frame drew,
            // so each change is divided by that frame's time.)
            {
                var marS = m.Mara.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(x => x != null && x.enabled);
                Assert.NotNull(marS, "her figure is a skinned mesh, with an outline to watch");
                var rpb = new MaterialPropertyBlock();
                m.Mara.Signal = 0.3f;
                m.Mara.Glitch(0.9f);
                float lastA = -1f, lastRim = -1f, alphaRate = 0f, rimRate = 0f, dtPrev = -1f, t0 = Time.unscaledTime, lowest = 1f;
                bool hit = false, back = false;
                while (Time.unscaledTime - t0 < 3f)
                {
                    yield return null;
                    float t = Time.unscaledTime - t0;
                    if (!hit && t > 1f) { hit = true; m.Mara.Signal = 0.05f; }   // a meteor through the room: one step down
                    if (!back && t > 2f) { back = true; m.Mara.Signal = 1f; }    // the room comes back
                    marS.GetPropertyBlock(rpb);
                    float a = rpb.GetFloat("_Alpha"), rim = rpb.GetFloat("_Rim");
                    lowest = Mathf.Min(lowest, a);
                    if (lastA >= 0f && dtPrev > 0f)
                    {
                        float span = Mathf.Max(dtPrev, 1e-3f);
                        alphaRate = Mathf.Max(alphaRate, Mathf.Abs(a - lastA) / span);
                        rimRate = Mathf.Max(rimRate, Mathf.Abs(rim - lastRim) / span);
                    }
                    lastA = a; lastRim = rim; dtPrev = Time.unscaledDeltaTime;
                }
                m.Mara.Signal = 1f;
                Assert.Less(lowest, 0.6f, "a weak signal does show: she fades");
                Assert.Less(alphaRate, 5f, "Mara never flickers: how see-through she is changes over half a second at least, never in one frame");
                Assert.Less(rimRate, 6.5f, "her outline brightens and fades with a hit or a glitch, never in a single frame");
            }
            var tbl = d.Rooms.Room.Table;
            var onTable = tbl.ToLocal(m.Mara.transform.position);
            Assert.IsTrue(Mathf.Abs(onTable.x) <= tbl.Size.x * 0.5f && Mathf.Abs(onTable.z) <= tbl.Size.z * 0.5f, "she stands on your real table");
            Assert.AreEqual(tbl.TopY, m.Mara.transform.position.y, 0.02f, "on its top, not in the air");
            Vector3 flatToHer = m.Mara.transform.position - d.Head.position; flatToHer.y = 0f;
            Assert.Greater(flatToHer.magnitude, 0.55f, "far enough to see her whole, not in your lap");
            var choice = PokeButton.Live.First(b => b.Id == "Choice m:who");
            float downToChoice = Vector3.Angle(Vector3.ProjectOnPlane(choice.transform.position - d.Head.position, Vector3.up), choice.transform.position - d.Head.position);
            float downToHer = Vector3.Angle(Vector3.ProjectOnPlane(m.Mara.transform.position - d.Head.position, Vector3.up), m.Mara.transform.position + Vector3.up * m.Mara.Height * 0.5f - d.Head.position);
            Assert.Greater(downToChoice, downToHer, "her choices sit below her in view, not over her");
            Assert.IsTrue(PokeButton.Live.Any(b => b.Id == "dock room" && b.isActiveAndEnabled), "My room is always there");
            Assert.IsFalse(PokeButton.Live.Any(b => b.Id == "dock next" && b.isActiveAndEnabled), "Next world is locked from her first word");
            Vector3 site = d.Site.transform.position, her = m.Mara.transform.position;
            Assert.Greater(new Vector2(her.x - site.x, her.z - site.z).magnitude, 0.25f, "not standing in the fire");
            var pick = PokeButton.Live.First(b => b.Id == "Choice m:who");
            Assert.Less(Vector3.Distance(pick.transform.position, d.Head.position), 0.6f, "her choices are within reach");
            Assert.GreaterOrEqual(pick.Size.x, 0.12f, "and big enough to read and touch");
            StringAssert.Contains("MARA", m.Words.Current, "her words carry her name");
            Assert.IsTrue(PokeButton.Live.Any(b => b.Id == "Choice m:who"), "a choice to touch");

            // Ask who she is, by voice; then answer by touch.
            d.Handle("who are you", "test");
            yield return Until(() => m.Now == Mission.Step.Answer, 25f, "her answer, then the choice again");
            var guide = PokeButton.Live.FirstOrDefault(b => b.Id == "Choice m:guide");
            Assert.NotNull(guide, "I'll guide you");
            guide.Press();
            yield return Until(() => m.Now == Mission.Step.Pace, 10f, "how she should fly in");

            // The pace, by voice: the slow approach is a longer, gentler storm.
            d.Handle("let's take it slow", "test");
            Assert.AreEqual(Mission.Step.Storm, m.Now);
            var em = d.Emergency;
            Assert.IsTrue(em.Running, "the storm is her approach");
            Assert.AreEqual(Pace.Slow.Count, em.Sim.Strikes.Count, "the pace chosen is the storm played");
            Assert.IsFalse(d.Panel.Open, "no palm menu during the storm");
            yield return Frames(3);
            Assert.IsFalse(PokeButton.Live.Any(b => b.Id == "dock next" && b.isActiveAndEnabled), "Next world is locked while she is coming in");
            d.Handle("take me to the beach", "test");
            Assert.AreEqual("campfire", d.Current.Biome, "another world waits until she is down");
            yield return Frames(3);
            Assert.Greater(em.Defended, 0, "the sample room has furniture in front: something is defended");
            // Furniture only: never a door or a window (a door frame went dark as "furniture" on Nov 16: review 36).
            for (int k = 0; k < em.Defended; k++)
            {
                var piece = em.DefendedPiece(k);
                Assert.IsFalse(piece.Role == Role.Door || piece.Role == Role.Window, "the storm is never aimed at a door or a window: " + piece.Label);
            }
            Assert.AreEqual(em.Defended, m.RunwayLightCount, "a runway light on every defended piece");
            foreach (var r in m.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name.StartsWith("Runway light"))
                    Assert.AreEqual(0f, r.sharedMaterial.GetFloat("_Radial"), "runway lights are solid glow, not a halo that fades them out");

            // Block every meteor: she lands.
            Time.timeScale = 4f;
            yield return Until(() => { hand.position = em.NextTarget; return em.Sim.State.Phase >= Sim.Aftermath; }, 90f, "the storm");
            Time.timeScale = 1f;
            Assert.AreEqual(0, em.Sim.State.Taken, "every meteor blocked");
            Assert.AreEqual(1f, m.Signal, 0.001f, "her signal stayed clear");
            // Her ship comes in to your hand: hold it out, palm up, and it lands there.
            yield return Until(() => m.WantsPalm, 12f, "she asks for your hand");
            Assert.IsFalse(d.Panel.Open, "a palm up is for her ship now, not the menu");
            hand.position = d.Head.position + d.Head.forward * 0.36f + Vector3.down * 0.22f;
            hands.SimulatedPalmUp = true;
            yield return Until(() => m.LandedInHand, 8f, "her ship in your hand");
            // The palm stays up after she has gone from it: still no menu (review 37: it opened over her goodbye).
            yield return Until(() => !m.WantsPalm, 8f, "she leaves your hand");
            yield return new WaitForSecondsRealtime(1f);
            Assert.IsFalse(d.Panel.Open, "and the menu stayed shut");
            hands.SimulatedPalmUp = false;
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return Until(() => m.Now == Mission.Step.Farewell, 30f, "her landing, then the goodbye");
            Assert.AreEqual(Landing.Clean, m.Result);
            m.Waved();
            yield return Until(() => m.Now == Mission.Step.Done, 20f, "she signs off");
            Assert.AreEqual(1, d.Progress.MissionChapter, "the next chapter is unlocked");
            Assert.NotNull(m.Gift, "she left her flight patch on your table");
            Assert.IsTrue(d.Rooms.Room.Table.Contains(m.Gift.position - Vector3.up * 0.005f, 0.06f), "on your real table");
            // (Not a fixed wait: after a slow frame her goodbye and a fixed wait can end on the same frame.)
            yield return Until(() => m.Mara == null, 8f, "she is gone from the table");
            // After her goodbye, one next step: her next chapter is in your next world (the world beyond opens by
            // itself only once her story is told).
            yield return new WaitForSecondsRealtime(8f);
            Assert.AreEqual("Next world", d.Dock.ActionLabel, "one bright next step after her chapter");
            StringAssert.Contains("next world", d.Dock.Next, "and the line above it says why (not \"The storm has passed\")");
            Assert.IsTrue(d.Wonders == null || d.Wonders.All.Count == 0, "the world beyond does not open by itself mid-story");

            // Arriving in the next world: no bright step until its task is set (review 32: the last world's "Next world"
            // showed for up to 4 s, right where the finger had just pressed, and skipped the relight).
            d.OnDockForTests("do");
            yield return Until(() => d.State == Director.Mode.Changing, 5f, "on to the next world");
            bool stale = false;
            while (d.State != Director.Mode.World) yield return null;
            float arrived = Time.unscaledTime;
            while (!d.Gather.Running && Time.unscaledTime - arrived < 5f)
            {
                if (d.Dock.ActionLabel.Length > 0 || PokeButton.Live.Any(b => (b.Id == "dock do" || b.Id == "dock next") && b.isActiveAndEnabled)) stale = true;
                yield return null;
            }
            Assert.IsFalse(stale, "no dock and no bright step before this world's task is set");
            Assert.IsTrue(d.Gather.Running, "this world's task");

            // Leaving the world mid-call leaves nothing behind.
            d.StartMission("test");
            yield return Frames(3);
            d.Close("test");
            yield return Until(() => d.State == Director.Mode.Room, 25f, "the room");
            Assert.IsFalse(m.Active, "no call left running in the room");
        }

        IEnumerator Pinch(Vector3 at)
        {
            hand.position = at;
            hands.SimulatedPinch = false;
            yield return Frames(2);
            hands.SimulatedPinch = true;
            yield return Frames(3);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator LooseThingsCanBeMovedPutDownAndTossed()
        {
            d.ResetProgress(new Progress { Onboarded = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "the campfire");
            yield return Until(() => d.Loose.Things.Count > 0, 10f, "things to play with on the table");
            d.Gather.Stop();   // the task's pieces aside: only the loose things are within reach
            var table = d.Rooms.Room.Table;
            var t = d.Loose.Things[0];
            Vector3 home = t.T.position;
            Assert.AreEqual(table.TopY + t.Radius, home.y, 0.01f, "rests on the table top");

            // Pick it up, carry it slowly above the table, hold still, let go: it drops onto the table.
            yield return Pinch(t.T.position);
            Assert.IsTrue(d.Loose.Holding(HandInput.SimulatedIndex), "picked up");
            Vector3 from = hand.position, to = table.ToWorld(new Vector3(table.Size.x * 0.2f, table.Size.y + 0.2f, 0f));
            float t0 = Time.time;
            while (Time.time - t0 < 0.6f) { hand.position = Vector3.Lerp(from, to, (Time.time - t0) / 0.5f); yield return null; }
            hand.position = to;
            yield return new WaitForSeconds(0.3f);
            hands.SimulatedPinch = false;
            yield return Frames(2);
            Assert.IsFalse(d.Loose.Holding(HandInput.SimulatedIndex), "let go");
            Assert.AreEqual(0, d.Loose.Throws, "a gentle let-go is not a throw");
            yield return Until(() => t.Resting, 4f, "it lands and rests");
            Assert.AreEqual(table.TopY + t.Radius, t.T.position.y, 0.02f, "it landed on the table");
            Assert.IsTrue(table.Contains(t.T.position - Vector3.up * t.Radius, 0.03f) || table.Contains(t.T.position, 0.03f), "on the table, where it was let go");

            // Toss it: a quick flick away and up.
            yield return Pinch(t.T.position);
            Assert.IsTrue(d.Loose.Holding(HandInput.SimulatedIndex), "picked up again");
            Vector3 away = d.Rooms.Room.Table.TopCenter - d.Head.position; away.y = 0f; away = away.normalized;
            Vector3 start = hand.position;
            for (int i = 1; i <= 6; i++) { hand.position = start + (away * 0.05f + Vector3.up * 0.03f) * i; yield return new WaitForSeconds(0.02f); }
            hands.SimulatedPinch = false;
            yield return Frames(2);
            Assert.AreEqual(1, d.Loose.Throws, "a flick is a throw");
            yield return Until(() => t.Resting || t.ReturnAt > 0f, 8f, "the tossed thing lands (or floats home)");
            Assert.IsFalse(float.IsNaN(t.T.position.x));
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator FrozenWorldsStillChangeAndTheMapCanEnd()
        {
            d.ResetProgress(new Progress { Onboarded = true });
            d.Undo();   // in the room: nothing happens
            Assert.AreEqual(Director.Mode.Room, d.State, "undo in the room does nothing");
            d.Open(Recipe.For("snow", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "the snow world");
            // Frozen, then another world: the change must not hang with time stopped.
            d.SetFrozen(true, "test");
            Assert.AreEqual(0f, Time.timeScale);
            d.Open(Recipe.For("beach", 3), "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Biome == "beach", 25f, "the beach, from a frozen world");
            Assert.IsFalse(d.Frozen, "changing world resumes time");
            Assert.AreEqual(1f, Time.timeScale);
            // Frozen again: the map offers Resume and My room (the way out without hands).
            d.SetFrozen(true, "test");
            d.Map.Show(d.Progress, "", true, "", true);
            yield return new WaitForSecondsRealtime(1.0f);   // the map ignores presses as it appears (and time is frozen)
            var resume = PokeButton.Live.FirstOrDefault(b => b.Id == "map resume");
            var end = PokeButton.Live.FirstOrDefault(b => b.Id == "map end");
            Assert.NotNull(resume, "Resume on the map while frozen");
            Assert.NotNull(end, "My room on the map in a world");
            resume.Press();
            yield return Frames(2);
            Assert.IsFalse(d.Frozen, "resumed from the map");
            d.Map.Show(d.Progress, "", true, "", false);
            yield return new WaitForSecondsRealtime(1.0f);
            PokeButton.Live.First(b => b.Id == "map end").Press();
            yield return Until(() => d.State == Director.Mode.Room, 15f, "the room, from the map's My room");
            Assert.AreEqual(0f, d.Look.Veil, 0.001f, "no fog left in the room");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator TheStormIsBlockedByHandAndRewindsToADifferentEnding()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            yield return Until(() => d.Gather.Running, 15f, "the task");
            hand.position = d.Head.position + Vector3.up * 3f;
            d.StartStorm("test");
            var em = d.Emergency;
            Assert.IsTrue(em.Running, "the storm started");
            Assert.IsTrue(em.Teach, "a first storm lets its first meteor through, so everyone sees their furniture break once");
            Assert.IsNull(PokeButton.Live.FirstOrDefault(b => b.Id == "power shields"), "no console: hands only");
            Assert.That(Vector3.Distance(em.NextTarget, d.Head.position), Is.InRange(0.3f, 0.7f), "the ring is within a seated arm's reach");
            Assert.IsFalse(em.RewindShown, "no Rewind before anything has got through");
            // You can see the meteor coming: in the last three seconds it is within 35 degrees of the ring, as seen from your head.
            yield return Until(() => em.Sim.NextStrikeIn < 4.5f, 30f, "the first meteor to come near");
            float worst = 0f;
            while (em.Sim.NextStrikeIn > 0.3f && em.Sim.State.Next == 0)
            {
                worst = Mathf.Max(worst, Vector3.Angle(em.NextTarget - d.Head.position, em.NextRock - d.Head.position));
                yield return null;
            }
            Assert.Less(worst, 35f, "the meteor is in view near its ring, not overhead, all the way in");
            // Hands away, at 6x speed: every meteor gets through.
            Time.timeScale = 6f;
            yield return Until(() => em.Sim.State.Phase >= Sim.Aftermath, 60f, "the storm to pass");
            Time.timeScale = 1f;
            int first = em.Sim.State.Taken;
            Assert.AreEqual(0, em.Sim.State.Blocked, "with no hand up, nothing is blocked");
            Assert.GreaterOrEqual(first, 2, "at least two got through");
            Assert.IsTrue(em.RewindShown, "Rewind is offered once something got through");
            yield return Frames(3);
            if (em.Defended > 0)
            {
                Assert.Greater(em.BrokenPieces, 0, "furniture that took a hit shows the real piece again");
                Assert.IsTrue(em.RoomBack, "with every piece broken, your real room is back");
                Assert.IsTrue(em.RewindShown, "and Rewind is there to win the world back");
                Assert.Less(first, em.Sim.Strikes.Count, "the storm ends when the room is lost");
                yield return new WaitForSecondsRealtime(3f);
                Assert.AreEqual(first, em.Sim.State.Taken, "nothing more falls after the storm has won");
                Assert.IsFalse(em.AnyRingShown, "no catch-ring is left lit after the storm has ended");
                yield return new WaitForSecondsRealtime(1.5f);
                Assert.Less(d.Shell.Radius, 1f, "the walls have returned");
            }
            Assert.Greater(d.Progress.StormRuns, 0, "the result is remembered");
            // Rewind to the start, and this time hold a palm in each ring.
            Assert.IsTrue(em.DoRewind(600f));
            yield return Until(() => !em.Rewinding, 5f, "the rewind");
            Assert.Less(em.Sim.State.T, 2f);
            Assert.AreEqual(0, em.Sim.State.Taken, "nothing has hit yet");
            Assert.IsFalse(em.Teach, "after a rewind every meteor can be blocked");
            Assert.IsTrue(d.Progress.SeenBreak, "the break has been seen once and is remembered");
            yield return Frames(3);
            Assert.AreEqual(0, em.BrokenPieces, "rewinding makes the furniture whole again");
            yield return new WaitForSecondsRealtime(1.5f);
            Assert.Greater(d.Shell.Radius, d.Shell.FullRadius, "and the world is back around you");
            Time.timeScale = 4f;
            yield return Until(() => { hand.position = em.NextTarget; return em.Sim.State.Phase >= Sim.Aftermath; }, 60f, "the storm again");
            Time.timeScale = 1f;
            Assert.AreEqual(0, em.Sim.State.Taken, "a palm in every ring blocks every meteor");
            Assert.AreEqual(Sim.Untouched, em.Sim.State.Outcome);
            yield return Frames(3);
            Assert.IsFalse(em.RewindShown, "nothing to rewind after a perfect run");
            hand.position = d.Head.position + Vector3.up * 3f;
            // By voice too, and it all goes away with the world.
            d.Handle("rewind", "test");
            yield return Until(() => em.Rewinding || em.Sim.State.Phase < Sim.Aftermath, 3f, "rewind by words");
            yield return Until(() => !em.Rewinding, 5f, "the rewind to finish");
            d.Handle("take me home", "test");
            yield return Until(() => d.State == Director.Mode.Room, 15f, "the room");
            Assert.IsFalse(em.Running, "the storm ends with the world");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TypingOnTheKeyboardOpensAPlace()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.OnPanelForTests("type");
            yield return Frames(2);
            Assert.IsTrue(d.Type.Open, "Type... opens the keyboard");
            foreach (var ch in "volc")
            {
                var key = PokeButton.Live.FirstOrDefault(b => b.Id == "type key " + ch);
                Assert.NotNull(key, "a key for " + ch);
                Assert.Less(Vector3.Distance(key.transform.position, d.Head.position), 0.7f, "within reach");
                key.Press();
            }
            Assert.AreEqual("volc", d.Type.Text);
            var chip = PokeButton.Live.FirstOrDefault(b => b.Id == "type: a volcano at nightfall");
            Assert.NotNull(chip, "the suggestion is on the keyboard, to touch while typing");
            chip.Press();
            Assert.IsFalse(d.Type.Open, "the keyboard closes");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Look != null && d.Current.Look.Name == "Volcano at nightfall", 25f, "the volcano");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator LabelsStayInsideTheirButtonsWhicheverWayThePanelFaces()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var holder = new GameObject("test labels").transform;
            holder.SetPositionAndRotation(d.Head.position + d.Head.forward * 0.5f, Quaternion.Euler(0f, 75f, 20f));
            var size = new Vector2(0.14f, 0.04f);
            float full = size.y * 0.42f / (0.1f * 72f);
            foreach (var text in new[] { "Mara calls: when I'm ready", "Use my furniture", "a volcano at nightfall with shooting stars", "Go" })
            {
                var b = PokeButton.Make("test fit", holder, d.GlowFlat, font, text, size);
                yield return null;
                var r = b.Label.GetComponent<MeshRenderer>();
                Assert.LessOrEqual(r.localBounds.size.x, size.x * 0.93f, text + ": inside, side to side");
                Assert.LessOrEqual(r.localBounds.size.y, size.y * 0.9f, text + ": inside, top to bottom");
                Assert.GreaterOrEqual(b.Label.characterSize, full * 0.55f, text + ": still big enough to read");
                Object.Destroy(b.gameObject);
            }
            Object.Destroy(holder.gameObject);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator APokeComesFromTheFrontNotFromTheSide()
        {
            int pressed = 0;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var b = PokeButton.Make("test side", null, d.GlowFlat, font, "", new Vector2(0.06f, 0.04f));
            Vector3 at = d.Head.position + d.Head.forward * 0.4f;
            b.transform.SetPositionAndRotation(at, Quaternion.LookRotation(at - d.Head.position, Vector3.up));
            b.Pressed += () => pressed++;
            Vector3 toUser = -b.transform.forward, side = b.transform.right;
            // A hand carrying something slides across, level with the face of the button.
            hand.position = at + side * 0.15f;
            yield return Frames(3);
            for (int i = 0; i <= 10; i++) { hand.position = at + side * (0.15f - i * 0.015f); yield return null; }
            yield return Frames(3);
            Assert.AreEqual(0, pressed, "sliding onto a button from the side is not a press");
            hand.position = at + side * 0.3f;
            yield return Frames(3);
            // A real poke: from in front, towards the button.
            hand.position = at + toUser * 0.04f;
            yield return Frames(3);
            hand.position = at - toUser * 0.005f;
            yield return Frames(3);
            Assert.AreEqual(1, pressed, "a poke from the front presses");
            Object.Destroy(b.gameObject);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator AfterHerFirstCallMaraWaitsToBeAnswered()
        {
            // You have met her (chapter one is done). In the next world she does not ring: no crackle while you
            // build, no call when the build is done. The bright button answers her when you want her.
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Open(Recipe.For("meadow", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            yield return Until(() => d.Gather.Running, 15f, "the task");
            Time.timeScale = 8f;
            try
            {
                yield return Until(() => !d.Gather.Running && d.Gather.PlacedCount == d.Gather.Total, 40f, "the build done");
                Assert.IsNull(GameObject.Find("Faint call"), "no crackle from the table while you build");
                yield return Until(() => d.Dock.ActionLabel == "Answer Mara", 40f, "the button that answers her");
                Assert.IsFalse(d.Mission.Active, "she has not rung");
                yield return new WaitForSeconds(30f);
                Assert.IsFalse(d.Mission.Active, "and she does not ring later either");
                Assert.AreEqual("Answer Mara", d.Dock.ActionLabel);
            }
            finally { Time.timeScale = 1f; }
            d.OnDockForTests("do");
            yield return Until(() => d.Mission.Active, 10f, "her call, when you ask for it");
            Assert.AreEqual(1, d.Mission.Chapter, "the next chapter of her story");
            d.Mission.Stop();
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AWholeHandGripHoldsItAndLosingSightOfTheHandDoesNotDropIt()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            hands.SimulatedPinch = false; hands.SimulatedFist = false;
            yield return Until(() => d.State == Director.Mode.Room, 10f, "your room");
            d.Handle("make me a fox on the table", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a fox");
            var fox = d.Maker.Things[0];
            yield return new WaitForSeconds(1.2f);
            try
            {
                // The whole hand closes round it: no pinch.
                hand.position = fox.T.position + Vector3.up * (fox.Size * 0.3f);
                yield return Frames(3);
                hands.SimulatedFist = true;
                yield return Frames(3);
                Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "a whole-hand grip picks it up");
                Assert.AreEqual(1, d.Maker.Gripped);
                Vector3 up = d.Head.position + d.Head.forward * 0.45f + Vector3.down * 0.15f;
                for (int i = 1; i <= 20; i++) { hand.position = Vector3.Lerp(hand.position, up, 0.25f); yield return null; }
                yield return Frames(10);
                Assert.Less(Vector3.Distance(fox.T.position, up), 0.25f, "it came with the hand");
                // The hand leaves the cameras' view for a moment: still held, and it has not fallen.
                hand.gameObject.SetActive(false);
                yield return new WaitForSecondsRealtime(0.3f);
                Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "losing sight of the hand for a moment does not drop it");
                Assert.Less(Vector3.Distance(fox.T.position, up), 0.25f, "it stays where it was held");
                hand.gameObject.SetActive(true);
                yield return Frames(3);
                Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "still held when the hand is seen again");
                // Out of sight for longer than the grace: let go.
                hand.gameObject.SetActive(false);
                yield return new WaitForSecondsRealtime(Maker.LostGrace + 0.3f);
                Assert.IsFalse(d.Maker.Holding(HandInput.SimulatedIndex), "a hand gone for good lets go");
                hand.gameObject.SetActive(true);
                // Opening the hand lets go.
                hands.SimulatedFist = false;
                hand.position = d.Head.position + Vector3.up * 3f;
                yield return new WaitForSeconds(1.5f);   // let go at your face, it glides back to the table first
                hand.position = fox.T.position + Vector3.up * (fox.Size * 0.3f);
                yield return Frames(3);
                hands.SimulatedFist = true;
                yield return Frames(3);
                Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "picked up again");
                hands.SimulatedFist = false;
                yield return Frames(3);
                Assert.IsFalse(d.Maker.Holding(HandInput.SimulatedIndex), "opening the hand lets go");
                // A hand that closes a moment before it reaches the fox (people close as they reach) still takes it.
                yield return new WaitForSeconds(0.6f);
                Vector3 onFox = fox.T.position + Vector3.up * (fox.Size * 0.3f);
                hand.position = onFox + Vector3.up * 0.3f;
                yield return Frames(3);
                hands.SimulatedFist = true;
                yield return Frames(2);
                Assert.IsFalse(d.Maker.Holding(HandInput.SimulatedIndex), "closed in the air: nothing yet");
                hand.position = onFox;
                yield return Frames(3);
                Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "a hand that closed just before reaching it takes it");
                hands.SimulatedFist = false;
                yield return new WaitForSeconds(0.6f);
                Assert.IsFalse(d.Maker.Holding(HandInput.SimulatedIndex));
                // A fist made long before is not a grab: it passes through.
                onFox = fox.T.position + Vector3.up * (fox.Size * 0.3f);
                hand.position = onFox + Vector3.up * 0.3f;
                yield return Frames(3);
                hands.SimulatedFist = true;
                yield return new WaitForSecondsRealtime(Maker.FistReach + 0.3f);
                hand.position = onFox;
                yield return Frames(3);
                Assert.IsFalse(d.Maker.Holding(HandInput.SimulatedIndex), "a fist made long before passes through");
                hands.SimulatedFist = false;
                yield return Frames(3);
            }
            finally { hands.SimulatedFist = false; hand.gameObject.SetActive(true); }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TreesAndFurnitureCastShadowsOnTheGround()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Open(Recipe.For("meadow", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            var sun = Object.FindFirstObjectByType<SunShadows>();
            Assert.NotNull(sun, "the shadows are in the scene");
            if (!sun.Supported) Assert.Ignore("this graphics device cannot do the shadow texture");
            yield return Until(() => sun.Renders > 0, 10f, "the shadows drawn");
            yield return new WaitForSeconds(1f);
            Assert.Greater(Shader.GetGlobalFloat("_SwShadowOn"), 0.5f, "the ground reads them");
            Assert.Greater(sun.Casters, 3, "trees, rocks and furniture are in it");
            float cover = sun.Coverage();
            Assert.Greater(cover, 0.002f, "something stands between the sun and the ground");
            Assert.Less(cover, 0.8f, "and the world is not all shadow");
            Assert.GreaterOrEqual(sun.Sun.y, Mathf.Sin(SunShadows.LowestSun * Mathf.Deg2Rad) - 0.001f, "never from a sun too low to bear");
            // The far corner of what it covers is where the matrix says: a point on the ground under you is in the middle.
            var m = SunShadows.LightMatrix(sun.Sun, new Vector3(1f, 0f, 2f), out float range);
            Vector3 mid = m.MultiplyPoint3x4(new Vector3(1f, 0f, 2f));
            Assert.AreEqual(0.5f, mid.x, 1e-3f); Assert.AreEqual(0.5f, mid.y, 1e-3f); Assert.AreEqual(0.5f, mid.z, 1e-3f);
            Vector3 towardsSun = m.MultiplyPoint3x4(new Vector3(1f, 0f, 2f) + sun.Sun * 5f);
            Assert.Less(towardsSun.z, 0.5f, "nearer the sun is a smaller distance");
            Assert.AreEqual(0.5f, towardsSun.x, 1e-3f, "and on the same ray");
            Assert.AreEqual(0.5f, towardsSun.y, 1e-3f, "and on the same ray");
            Assert.Greater(range, 10f);
            // Back in your room there is no world to shade.
            d.Close("test");
            yield return Until(() => d.State == Director.Mode.Room, 15f, "your room");
            yield return Frames(3);
            Assert.Less(Shader.GetGlobalFloat("_SwShadowOn"), 0.5f, "no shadows over your real room");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator WaveAndWhoeverLivesThereWavesBack()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = Story.Chapters, LastFlight = System.DateTime.UtcNow.ToString("yyyy-MM-dd"), FlightStreak = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            hands.SimulatedPinch = false;
            d.Handle("a pirate cove", "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Look != null, 25f, "the pirate cove");
            d.Gather.Stop();
            yield return Until(() => d.Local != null, 5f, "someone's in it");
            var who = d.Local;
            var before = d.Head.rotation;
            try
            {
                // Looking away from them, a wave is only the guide's.
                d.Head.rotation = Quaternion.LookRotation(-(who.transform.position - d.Head.position).normalized);
                yield return Frames(3);
                Vector3 foot = who.transform.position;
                d.Head.rotation = Quaternion.LookRotation((who.transform.position + Vector3.up * 1f - d.Head.position).normalized);
                yield return Frames(2);
                // A hand moving side to side with its palm down (reaching over the table) is not a wave.
                Assert.IsTrue(who.InView(50f), "they are in front of you");
                float t0 = Time.unscaledTime;
                int guideWaves = d.Guide.Waves;
                while (Time.unscaledTime - t0 < 1.2f)
                {
                    float ph = (Time.unscaledTime - t0) * 3f;
                    hand.position = d.Head.TransformPoint(new Vector3((Mathf.PingPong(ph * 2f, 1f) * 2f - 1f) * 0.12f, -0.1f, 0.4f));
                    yield return new WaitForSecondsRealtime(0.011f);
                }
                Assert.AreEqual(0, who.Waves, "palm down: not a wave");
                Assert.AreEqual(guideWaves, d.Guide.Waves, "palm down: not a wave to the guide either");
                // Wave: the open hand, palm out, goes side to side in front of you.
                hands.SimulatedPalmOut = true;
                t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < 3f && who.Waves == 0)
                {
                    // A triangle sweep, 12 cm each way, three times a second: fast enough to read at any frame rate.
                    float ph = (Time.unscaledTime - t0) * 3f;
                    float x = (Mathf.PingPong(ph * 2f, 1f) * 2f - 1f) * 0.12f;
                    hand.position = d.Head.TransformPoint(new Vector3(x, -0.1f, 0.4f));
                    yield return new WaitForSecondsRealtime(0.011f);
                }
                hands.SimulatedPalmOut = false;
                Assert.AreEqual(1, who.Waves, "they wave back");
                Assert.IsTrue(who.Greeted, "and that is a hello");
                StringAssert.Contains("Captain Brine", d.LocalSaid);
                float rolled = 0f, hopped = 0f, nodded = 0f;
                float until = Time.unscaledTime + Local.WaveSeconds + 0.3f;
                while (Time.unscaledTime < until)
                {
                    rolled = Mathf.Max(rolled, Mathf.Abs(who.Roll));
                    nodded = Mathf.Max(nodded, Mathf.Abs(who.Pitch));
                    hopped = Mathf.Max(hopped, who.transform.position.y - foot.y);
                    yield return null;
                }
                Assert.Greater(rolled, 5f, "rocking from foot to foot");
                Assert.Greater(hopped, 0.02f, "with a little hop");
                Assert.Greater(nodded, 4f, "a nod as they speak");
                yield return Frames(3);
                Assert.IsFalse(who.Waving);
                Assert.AreEqual(foot.x, who.transform.position.x, 1e-3f, "back where they stood");
                Assert.AreEqual(foot.y, who.transform.position.y, 1e-3f, "feet on the ground again");
                Assert.AreEqual(foot.z, who.transform.position.z, 1e-3f);
                Assert.AreEqual(0f, who.Roll, 1e-3f, "upright again");
                // Their words stay in view when they stand well off to one side (look-3: Wren's ran off the edge).
                Assert.IsNotEmpty(d.LocalSaid, "still speaking");
                Vector3 toWho = who.transform.position - d.Head.position; toWho.y = 0f;
                d.Head.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(55f, Vector3.up) * toWho.normalized);
                yield return Frames(2);
                Vector3 cap = d.LocalCaptionAt - d.Head.position; cap.y = 0f;
                Vector3 fw = d.Head.forward; fw.y = 0f;
                Assert.LessOrEqual(Vector3.Angle(fw, cap), Director.LocalCaptionMaxDeg + 1f, "their words stay near the middle of your view");
                // Head down at the table (review 31): the words come down too, never far above where you look.
                d.Head.rotation = Quaternion.LookRotation(toWho.normalized * Mathf.Cos(50f * Mathf.Deg2Rad) + Vector3.down * Mathf.Sin(50f * Mathf.Deg2Rad));
                yield return Frames(2);
                Vector3 c2 = d.LocalCaptionAt - d.Head.position;
                float above = Vector3.SignedAngle(d.Head.forward, c2, d.Head.right);   // negative: above where you look
                Assert.LessOrEqual(-above, Director.LocalCaptionUpDeg + 12f, "not far above where you look");
                Assert.Greater(d.LocalCaptionAt.y, d.Rooms.Room.Table.TopY, "and never down in the table");
                // Behind you: it stays on one side as the head shakes a little (it flipped every frame).
                d.Head.rotation = Quaternion.LookRotation(-toWho.normalized);
                yield return Frames(2);
                Vector3 a = d.LocalCaptionAt;
                d.Head.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(3f, Vector3.up) * -toWho.normalized);
                yield return Frames(2);
                Vector3 b2 = d.LocalCaptionAt;
                d.Head.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(-3f, Vector3.up) * -toWho.normalized);
                yield return Frames(2);
                Vector3 c3 = d.LocalCaptionAt;
                Assert.Less(Vector3.Distance(a, c3), 0.6f, "no jump from one side to the other");
                Assert.Less(Vector3.Distance(b2, c3), 0.6f, "no jump from one side to the other");
            }
            finally { d.Head.rotation = before; hand.position = d.Head.position + Vector3.up * 3f; }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TheMenuKeepsOutOfTheWayOfWorkInHand()
        {
            // From a headset session: the menu was open over the table, and a hand putting a piece down pressed
            // "Leave this world". Leaving while that piece was still gliding to the table threw.
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            hands.SimulatedPinch = false; hands.SimulatedPalmUp = false;
            d.Open(Recipe.For("space", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            yield return Until(() => d.Gather.Running && d.Gather.Items.Count > 0 && d.Gather.Items.All(i => i.Appear >= 0.6f), 15f, "pieces to carry");
            d.Panel.Show(HandPanel.Page.Main);
            yield return Frames(2);
            Assert.IsTrue(d.Panel.Open, "the menu is open");
            var piece = d.Gather.Items.First(i => !i.Placed);
            yield return Pinch(piece.transform.position);
            Assert.IsTrue(d.Gather.Holding(HandInput.SimulatedIndex), "a piece in hand");
            yield return Frames(3);
            Assert.IsFalse(d.Panel.Open, "picking something up closes the menu");
            // A palm turned up while carrying does not open it, nor for a moment after putting the piece down.
            hands.SimulatedPalmUp = true;
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.IsFalse(d.Panel.Open, "no menu under a hand that is carrying");
            hands.SimulatedPalmUp = false;
            // Over the table and let go: it glides to its place. Leave the world while it is still on its way.
            hand.position = d.Rooms.Room.Table.TopCenter + Vector3.up * 0.12f;
            yield return Frames(3);
            hands.SimulatedPinch = false;
            yield return Frames(2);
            d.Close("test");
            yield return Until(() => d.State == Director.Mode.Room, 25f, "your room, and nothing thrown on the way");
            hand.position = d.Head.position + Vector3.up * 3f;
            // The wrist button: a touch the instant it appears is ignored.
            Assert.Greater(WristMenu.MinShown, 0.3f);
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator TakeMeToEgyptAndThePyramidsAreThere()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = Story.Chapters, LastFlight = System.DateTime.UtcNow.ToString("yyyy-MM-dd"), FlightStreak = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Handle("take me to Egypt", "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Look != null, 25f, "Egypt");
            Assert.AreEqual("Egypt at golden hour", d.Current.Look.Name);
            var mark = d.Builder.Landmark;
            Assert.NotNull(mark, "the pyramids stand there");
            var room = d.Rooms.Room;
            Vector3 to = mark.position - room.Head; to.y = 0f;
            Assert.Greater(to.magnitude, room.OuterRadius + 60f, "far off, past where your walls were");
            Assert.Less(Vector3.Angle(room.Forward, to), 50f, "in front of you");
            Assert.IsTrue(d.Builder.ClearView(to.normalized) || !new[] { 16f, -16f, 0f, 32f, -32f, 48f, -48f }.Any(a => d.Builder.ClearView(Quaternion.AngleAxis(a, Vector3.up) * Flat(room.Forward).normalized)),
                "none of your furniture stands in front of it (unless it stands in every direction)");
            Assert.AreEqual(0.5f, mark.GetComponent<Renderer>().sharedMaterial.GetFloat("_FogScale"), 1e-3f, "it stands out of the haze");
            // Nothing tall was scattered between your seat and them.
            Vector3 toMark = Flat(mark.position - room.Head).normalized;
            Assert.IsTrue(d.Builder.InLandmarkView(room.Head + toMark * (room.OuterRadius + 12f)), "the view to them is kept open");
            Assert.IsFalse(d.Builder.InLandmarkView(room.Head - toMark * (room.OuterRadius + 12f)), "only that view");
            var b = mark.GetComponent<Renderer>().bounds;
            Assert.Greater(b.size.y, 60f, "and they are big");
            Assert.Less(b.min.y, room.FloorY + 4f, "standing on the ground, not in the air");
            // Nothing else was scattered into them.
            foreach (var w in d.Builder.Root.GetComponentsInChildren<Wild>())
                Assert.Greater(Vector3.Distance(Flat(w.transform.position), Flat(mark.position)), 40f, "no animal stands inside the pyramids");
            yield return Until(() => d.Local != null, 5f, "someone lives there");
            Assert.AreEqual("Zahra", d.Local.Def.Name, "the well keeper of the oasis it is made from");
            // A change Claude makes to where you are keeps where you are (it once turned India into a plain meadow at night).
            var night = Recipe.For(d.Current.Biome, d.Current.Seed);
            night.Hour = 22f;
            d.OnActForTests(new ActAnswer { Source = "test", Actions = { new ActItem { Do = "world", Recipe = night } } }, "make it moody");
            yield return Until(() => d.State == Director.Mode.World && Mathf.Abs(d.Current.Hour - 22f) < 0.6f, 15f, "night falls");
            Assert.IsNotNull(d.Current.Look, "still the place");
            Assert.AreEqual("pyramids", d.Current.Look.Landmark, "with its landmark");
            Assert.IsNotNull(d.Builder.Landmark, "which still stands there");
            // Every landmark builds, with something to see.
            foreach (var kind in Atlas.Landmarks)
            {
                var mb = new MeshBuilder();
                Landmarks.Build(mb, kind, Color.gray, new Rng(1));
                Assert.Greater(mb.TriangleCount, 10, kind);
                Assert.Less(mb.TriangleCount, 6000, kind + " is light");
                Assert.Greater(mb.Bounds.size.y, 5f, kind + " stands up");
                Assert.That(mb.Bounds.min.y, Is.InRange(-3f, 0.5f), kind + " stands on the ground (splayed feet may be sunk a little, never hanging)");
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AWholeHandPicksUpAPieceToo()
        {
            // From a headset session: the player closed a whole hand round the glowing pieces (it worked on a made
            // astronaut) and several were never picked up: pieces took only a pinch.
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            hands.SimulatedPinch = false; hands.SimulatedFist = false; hands.SimulatedPalmUp = false;
            d.Open(Recipe.For("space", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            yield return Until(() => d.Gather.Running && d.Gather.Items.Count > 0 && d.Gather.Items.All(i => i.Appear >= 0.6f), 15f, "pieces to carry");
            var piece = d.Gather.Items.First(i => !i.Placed);
            try
            {
                // The hand closes on its way, a moment before it reaches the piece: it is taken all the same.
                hand.position = d.Head.position + Vector3.up * 0.6f;
                yield return Frames(3);
                hands.SimulatedFist = true;
                yield return Frames(2);
                Assert.IsFalse(d.Gather.Holding(HandInput.SimulatedIndex), "closed in the air: nothing yet");
                // 11 cm off: a fist is bigger than a pinch (Nov 15 on the headset, hands closed 11 and 15 cm off and missed).
                hand.position = piece.transform.position + Vector3.right * 0.11f;
                yield return Frames(3);
                Assert.IsTrue(d.Gather.Holding(HandInput.SimulatedIndex), "a whole hand closing round a piece picks it up");
                Assert.AreEqual(1, d.Gather.Gripped);
                // Over the table, open the hand: it goes to its place.
                int placed = d.Gather.PlacedCount;
                Assert.AreEqual(0, d.Gather.Came, "nothing has had to come to the hand yet");
                hand.position = d.Rooms.Room.Table.TopCenter + Vector3.up * 0.12f;
                yield return Frames(5);
                Assert.IsTrue(d.Gather.Holding(HandInput.SimulatedIndex), "still held while the hand stays closed");
                hands.SimulatedFist = false;
                yield return Until(() => d.Gather.PlacedCount > placed, 5f, "the piece placed");
            }
            finally { hands.SimulatedFist = false; hand.position = d.Head.position + Vector3.up * 3f; }
            // A piece out of reach: close a hand twice short of it, and it comes to that hand.
            var farPiece = d.Gather.Items.FirstOrDefault(i => !i.Placed && i.HeldBy < 0);
            if (farPiece != null)
            {
                try
                {
                    Vector3 from = farPiece.transform.position + (d.Head.position - farPiece.transform.position).normalized * 0.25f;
                    hand.position = from;
                    yield return Frames(3);
                    hands.SimulatedFist = true;
                    yield return new WaitForSecondsRealtime(0.25f);
                    hands.SimulatedFist = false;
                    yield return new WaitForSecondsRealtime(0.85f);
                    Assert.AreEqual(0, d.Gather.Came, "one miss is only a miss");
                    // The second time the hand stays closed: the piece comes over and that hand has it.
                    hands.SimulatedFist = true;
                    yield return new WaitForSecondsRealtime(0.9f);
                    Assert.AreEqual(1, d.Gather.Came, "closed on twice from 25 cm: it comes over");
                    Assert.IsTrue(d.Gather.Holding(HandInput.SimulatedIndex), "and the hand, still closed, has it");
                }
                finally { hands.SimulatedFist = false; hand.position = d.Head.position + Vector3.up * 3f; }
            }
            d.Close("test");
            yield return Until(() => d.State == Director.Mode.Room, 25f, "your room");
        }

        [Test]
        public void NothingFlashes()
        {
            // Mara's weak signal: a slow swell, never a strobe. Sampled at 90 frames a second for 30 s of a bad signal,
            // no frame-to-frame jump is visible, and the brightness turns round at most about once a second.
            bool calm = Effects.Calm;
            try
            {
                Effects.Calm = false;
                float last = HoloPerson.SignalDim(0f, 0.7f, false), lowest = 1f, biggestStep = 0f;
                int turns = 0, dir = 0;
                for (int k = 1; k <= 90 * 30; k++)
                {
                    float now = k / 90f, v = HoloPerson.SignalDim(now, 0.7f, k % 400 < 40);
                    biggestStep = Mathf.Max(biggestStep, Mathf.Abs(v - last));
                    lowest = Mathf.Min(lowest, v);
                    int d = v > last + 1e-4f ? 1 : v < last - 1e-4f ? -1 : 0;
                    if (d != 0 && dir != 0 && d != dir) turns++;
                    if (d != 0) dir = d;
                    last = v;
                }
                Assert.Less(biggestStep, 0.03f, "no visible jump between two frames");
                Assert.GreaterOrEqual(lowest, 0.59f, "never darker than 60%");
                Assert.LessOrEqual(turns / 30f, 3f, "it turns round at most three times a second");
                Effects.Calm = true;
                Assert.AreEqual(1f, HoloPerson.SignalDim(3f, 0.9f, true), "steady under Calm effects");
            }
            finally { Effects.Calm = calm; }
        }

        [UnityTest, Timeout(90000)]
        public IEnumerator AGlowingPieceNeverHidesAChoice()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = Story.Chapters, LastFlight = System.DateTime.UtcNow.ToString("yyyy-MM-dd"), FlightStreak = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            yield return Until(() => d.Gather.Running && d.Gather.Items.All(i => i.Appear >= 0.6f), 15f, "the pieces");
            // Where a row about someone straight ahead appears; then a piece put right in front of its middle button.
            Vector3 eye = d.Head.position;
            Vector3 ahead = new Vector3(d.Head.forward.x, 0f, d.Head.forward.z).normalized;
            string[] labels = { "Who are you?", "What is this place?", "What can I do here?" }, ids = { "test:who", "test:here", "test:tip" };
            d.Guide.ShowChoices(labels, ids, eye + ahead * 3f);
            yield return Frames(3);
            Vector3 mid = PokeButton.Live.First(b => b.Id == "Choice test:here").transform.position;
            d.Guide.ShowChoices(null, null);
            var piece = d.Gather.Items.First(i => !i.Placed && i.HeldBy < 0);
            piece.Home = eye + (mid - eye) * 0.8f;
            piece.transform.position = piece.Home;
            yield return Frames(2);
            d.Guide.ShowChoices(labels, ids, eye + ahead * 3f);
            yield return new WaitForSeconds(1f);
            try
            {
                var row = PokeButton.Live.Where(b => b.Id.StartsWith("Choice test:")).ToList();
                Assert.AreEqual(3, row.Count, "the row is on show");
                foreach (var b in row)
                {
                    // Seen from the eyes, the piece is not over any of the buttons.
                    Vector3 toB = b.transform.position - eye;
                    Vector3 toP = piece.transform.position - eye;
                    float z = Vector3.Dot(toP, toB.normalized);
                    Vector3 onRow = eye + toP * (toB.magnitude / Mathf.Max(0.05f, z));
                    Assert.IsFalse(b.Over(onRow, piece.Radius * 0.5f), "the piece no longer hides \"" + b.Id + "\"");
                }
                Vector3 flat = piece.Home - eye; flat.y = 0f;
                Assert.LessOrEqual(flat.magnitude, 0.6f, "and it is still within reach");
                Assert.Less(piece.Home.y, eye.y - 0.14f, "below the eyes");
            }
            finally { d.Guide.ShowChoices(null, null); }
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator AFingerRestingOnAChoicePressesItOnce()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = Story.Chapters, LastFlight = System.DateTime.UtcNow.ToString("yyyy-MM-dd"), FlightStreak = 1 });
            yield return Until(() => d.State == Director.Mode.Room, 10f, "your room");
            hands.SimulatedPinch = false; hands.SimulatedFist = false; hands.SimulatedPalmUp = false;
            hand.position = d.Head.position + Vector3.up * 3f;
            int pressed = 0;
            System.Action<string> count = id => { if (id.StartsWith("test:")) pressed++; };
            d.Guide.Chose += count;
            try
            {
                string[] labels = { "First", "Second" }, ids = { "test:a", "test:b" };
                d.Guide.ShowChoices(labels, ids);
                yield return Frames(3);
                Vector3 at = PokeButton.Live.First(b => b.Id == "Choice test:a").transform.position;
                // Choices that appear under a fingertip already resting there are not pressed by it.
                d.Guide.ShowChoices(null, null);
                hand.position = at;
                yield return Frames(2);
                d.Guide.ShowChoices(labels, ids);
                yield return Frames(2);
                var again = PokeButton.Live.First(b => b.Id == "Choice test:a");
                Assert.IsTrue(again.Over(hand.position) && Mathf.Abs(again.Local(hand.position).z) < 0.006f, "the fingertip is on the new button");
                yield return new WaitForSecondsRealtime(1.2f);
                Assert.AreEqual(0, pressed, "a finger that was already there presses nothing");
                // Away, back from below, and resting on it: one press.
                at = again.transform.position;
                hand.position = at + Vector3.down * 0.12f;
                yield return Frames(3);
                hand.position = at;
                yield return new WaitForSecondsRealtime(0.5f);
                Assert.AreEqual(1, pressed, "resting on it presses it, once");
                // The same choices come back under the finger (a local's questions do, after each answer): no second press.
                d.Guide.ShowChoices(labels, ids);
                yield return new WaitForSecondsRealtime(1.2f);
                Assert.AreEqual(1, pressed, "a finger left there does not press again");
            }
            finally { d.Guide.Chose -= count; d.Guide.ShowChoices(null, null); hand.position = d.Head.position + Vector3.up * 3f; }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator MaraCanWaitWhileYouExplore()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.Gather.Stop();
            d.StartMission("test");
            var m = d.Mission;
            yield return Until(() => m.Now == Mission.Step.Answer, 25f, "her call");
            Assert.IsTrue(PokeButton.Live.Any(b => b.Id == "Choice m:later"), "Call me later is a choice");
            PokeButton.Live.First(b => b.Id == "Choice m:later").Press();
            yield return Until(() => !m.Active, 20f, "she goes quiet");
            yield return Frames(3);
            Assert.AreEqual("Answer Mara", d.Dock.ActionLabel, "the dock answers her when you're ready");
            // With the map up, the world waits (Nov 15 on the headset: Explore and its jump coach opened over the map).
            d.OnDockForTests("worlds");
            yield return Frames(2);
            Assert.IsTrue(d.Map.Showing, "the map is up");
            yield return new WaitForSecondsRealtime(8f);
            Assert.IsTrue(d.Wonders == null || d.Wonders.All.Count == 0, "nothing opens over the map");
            d.Map.Hide();
            yield return Until(() => d.Wonders != null && d.Wonders.All.Count > 0, 15f, "the world opens for exploring");
            d.OnDockForTests("do");
            yield return Until(() => m.Active, 10f, "her call again");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AMadeThingTurnsWithTheHand()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.Gather.Stop();
            d.Handle("make me a lantern", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a lantern");
            var t = d.Maker.Things[0];
            yield return new WaitForSeconds(0.8f);
            hand.rotation = Quaternion.identity;
            yield return Pinch(t.T.position + Vector3.up * (t.Size * 0.4f));
            Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "picked up");
            float yaw0 = t.T.eulerAngles.y;
            for (int i = 1; i <= 30; i++) { hand.rotation = Quaternion.Euler(0f, i * 3f, 0f); yield return null; }
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(90f, Mathf.DeltaAngle(yaw0, t.T.eulerAngles.y), 8f, "it turned with the hand");
            hands.SimulatedPinch = false;
            yield return Frames(3);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AThingHeldCloseIsLookedAtMadeLifeSizeAndFlickedAway()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.Gather.Stop();
            d.Handle("make me a lion", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a lion");
            var t = d.Maker.Things[0];
            Assert.AreEqual("lion", t.Kind);
            yield return new WaitForSeconds(0.8f);
            hand.rotation = Quaternion.identity;
            yield return Pinch(t.T.position + Vector3.up * (t.Size * 0.4f));
            Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "picked up");
            // Brought up to the eyes, it is looked at: a card says what it is and how big it is in life.
            Vector3 close = d.Head.position + d.Head.forward * 0.35f;
            for (int i = 0; i < 20; i++) { hand.position = Vector3.Lerp(hand.position, close, 0.25f); yield return null; }
            yield return Until(() => d.Inspect.Looking == t, 3f, "looking at it");
            StringAssert.Contains("in life", d.Inspect.CardText);
            hands.SimulatedPinch = false;
            yield return new WaitForSeconds(0.8f);
            Assert.IsNull(d.Inspect.Looking, "let go, and the card goes");
            Assert.AreEqual(1, d.Maker.Things.Count, "letting go gently keeps it");
            // As big as a real lion, on the floor in front.
            d.Handle("make it life size", "test");
            Assert.AreEqual(2.5f, t.Size, 0.01f, "a lion is 2.5 m in life");
            Assert.Greater(Vector3.Distance(Flat(t.T.position), Flat(d.Head.position)), 0.7f, "not standing in you");
            yield return new WaitForSeconds(0.8f);
            // A flick throws it away.
            yield return Pinch(t.T.position + Vector3.up * (t.Size * 0.4f));
            Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "picked up again");
            Vector3 flick = d.Head.right;
            for (int i = 0; i < 8; i++) { hand.position += flick * 0.24f; yield return null; }
            hands.SimulatedPinch = false;
            hand.position += flick * 0.24f;
            yield return null;
            yield return Until(() => d.Maker.Things.Count == 0, 2f, "thrown away");
            Assert.AreEqual(1, d.Maker.Tossed);
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        static readonly string[] MonkeySays =
        {
            "make me a lion", "a volcano", "make it rain", "remove it", "bigger", "make it life size", "take me on a tour",
            "peace and quiet", "louder", "spin it", "how fast can it run?", "hi, deer", "take me where it lives", "undo",
            "freeze", "resume", "Juniper, on Mars", "put a t rex on the table", "remove all the lions", "go back",
            "add fireflies", "darker", "a haunted forest at night", "generate a dog", "make me two penguins", "mute",
            "sound on", "save this as monkey world", "surprise me", "today's world", "help", "remove everything"
        };

        [UnityTest, Timeout(600000)]
        public IEnumerator RandomPressesWordsAndPinchesNeverBreakIt()
        {
            // A first-time player who does everything at once, for two and a half minutes. Any error from our code
            // fails the test (see Done); afterwards, the way home must still work.
            d.ResetProgress(new Progress());
            var rnd = new System.Random(20261113);
            int presses = 0, words = 0, pinches = 0;
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 150f)
            {
                double roll = rnd.NextDouble();
                if (roll < 0.25) { d.Handle(MonkeySays[rnd.Next(MonkeySays.Length)], "monkey"); words++; }
                else if (roll < 0.4 && d.Maker.Things.Count > 0)
                {
                    var th = d.Maker.Things[rnd.Next(d.Maker.Things.Count)];
                    if (th.T != null) { hand.position = Maker.Centre(th) + Random.insideUnitSphere * 0.05f; hands.SimulatedPinch = rnd.NextDouble() < 0.6; pinches++; }
                }
                else
                {
                    // Not Quest's own room setup, nor the microphone: those leave the app in the editor.
                    var live = PokeButton.Live.Where(b => b != null && b.isActiveAndEnabled && !b.Id.Contains("scan") && !b.Id.Contains("voice") && !b.Id.StartsWith("test")).ToList();
                    if (live.Count > 0) { live[rnd.Next(live.Count)].Press(); presses++; }
                }
                if (rnd.NextDouble() < 0.1) { hands.SimulatedPinch = false; hand.position = d.Head.position + Vector3.up * 3f; }
                yield return new WaitForSecondsRealtime(0.25f + (float)rnd.NextDouble() * 0.35f);
            }
            hands.SimulatedPinch = false;
            hand.position = d.Head.position + Vector3.up * 3f;
            Time.timeScale = 1f;
            if (d.Mission.Active) d.Mission.Stop();
            float h0 = Time.unscaledTime, asked = -10f;
            while (d.State != Director.Mode.Room && Time.unscaledTime - h0 < 60f)
            {
                if (Time.unscaledTime - asked > 4f && !d.Busy) { asked = Time.unscaledTime; d.Handle("take me home", "monkey"); }
                yield return null;
            }
            Assert.AreEqual(Director.Mode.Room, d.State, "home again after " + presses + " presses, " + words + " sentences, " + pinches + " pinches");
            Assert.Greater(presses + words, 150, "it really was busy");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AMadeThingBesideTheKindlingIsStillYours()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            yield return Until(() => d.Gather.Running && d.Gather.Items.Count > 0, 15f, "the kindling");
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Handle("make me a fox", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a fox");
            var fox = d.Maker.Things[0];
            yield return new WaitForSeconds(1f);
            // The fox right beside a piece of kindling, closer to the hand than the kindling is.
            var piece = d.Gather.Items[0].transform.position;
            Vector3 side = d.Head.right; side.y = 0f; side.Normalize();
            fox.T.position = piece + side * 0.1f - Vector3.up * fox.Size * 0.4f;
            yield return null;
            hand.rotation = Quaternion.identity;
            yield return Pinch(Maker.Centre(fox));
            Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "the made thing nearest the hand is picked up");
            Assert.IsFalse(d.Gather.Holding(HandInput.SimulatedIndex), "not the kindling");
            hands.SimulatedPinch = false;
            yield return Frames(3);
        }

        [UnityTest, Timeout(240000)]
        public IEnumerator TheFirstFiveMinutesAnAnimalThenItsHomeThenMara()
        {
            // A first-time player, presses only: an animal on the table, held up, taken home at real size, then Mara.
            d.ResetProgress(new Progress());
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return Until(() => d.State == Director.Mode.Room, 10f, "your room");
            float t0 = Time.unscaledTime;
            d.Guide.Button.Press();
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice first:zebra"), 5f, "three animals to pick");
            PokeButton.Live.First(b => b.Id == "Choice first:zebra").Press();
            yield return Until(() => d.FirstAnimal != null, 5f, "a zebra");
            var zebra = d.FirstAnimal;
            Assert.AreEqual("zebra", zebra.Kind);
            Assert.AreEqual(d.Rooms.Room.Table.TopY, zebra.T.position.y, 0.05f, "on your table");
            yield return new WaitForSeconds(1f);
            hand.rotation = Quaternion.identity;
            yield return Pinch(Maker.Centre(zebra));
            Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "picked up");
            Vector3 close = d.Head.position + d.Head.forward * 0.3f;
            float h0 = Time.unscaledTime;
            while (Time.unscaledTime - h0 < 0.8f) { hand.position = Vector3.Lerp(hand.position, close, 0.15f); yield return null; }
            yield return Until(() => d.Inspect.Looking == zebra && d.Inspect.HomeButton != null, 3f, "its card, with the way home");
            StringAssert.Contains("See where it lives", d.Inspect.CardText, "the card says what to do");
            // Let go first: the way home stays beside it.
            hands.SimulatedPinch = false;
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return new WaitForSeconds(1.5f);
            Assert.IsNull(d.Inspect.Looking, "let go");
            Assert.NotNull(d.Inspect.HomeButton, "the way home stays after you let go");
            d.Inspect.HomeButton.Press();
            var savanna = Places.Get("savanna");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Look != null && d.Current.Look.Name == savanna.Look.Name, 25f, "its home");
            hands.SimulatedPinch = false;
            yield return Until(() => zebra.Size > 2f, 5f, "at its real size");
            Assert.Less(Time.unscaledTime - t0, 90f, "the first payoff in under 90 seconds");
            Assert.IsFalse(d.Gather.Running, "no task on the way");
            // Then Mara: her call, and the campfire with all its first-time help.
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice first:campfire"), 15f, "Mara calling");
            PokeButton.Live.First(b => b.Id == "Choice first:campfire").Press();
            yield return Until(() => d.State == Director.Mode.World && d.Current.Biome == "campfire", 25f, "the campfire");
            yield return Until(() => d.Mission.Active, 15f, "Mara calls as soon as you arrive");
            Assert.IsFalse(d.Gather.Running, "no chore before her");
            Assert.AreEqual(0, d.Maker.Things.Count, "the zebra stayed home");
            d.Mission.Stop();
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator AfterHerStoryMaraFliesEveryDay()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = Story.Chapters });
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice flight:today"), 5f, "today's flight offered in your room");
            PokeButton.Live.First(b => b.Id == "Choice flight:today").Press();
            yield return Until(() => d.State == Director.Mode.World, 25f, "today's world");
            yield return Until(() => d.Mission.Active, 15f, "Mara calls as soon as you arrive");
            Assert.IsFalse(d.Gather.Running, "no task first");
            d.Mission.Stop();
        }

        [UnityTest, Timeout(30000)]
        public IEnumerator EveryLineStaysLongEnoughToRead()
        {
            yield return null;
            var go = new GameObject("read test");
            try
            {
                var c = Caption.Make("Read test", go.transform, d.Guide.Font, d.Guide.Panel, d.Head);
                string line = "No room scan, so your table is her runway tonight. Menu, Use my furniture, to bring in the rest.";
                c.Say(line, 5f);
                Assert.GreaterOrEqual(c.HideAt - Time.unscaledTime, 1.5f + 0.32f * line.Split(' ').Length - 0.05f, "a long line stays until it can be read");
                c.Say("Okay.", 3f);
                Assert.AreEqual(3f, c.HideAt - Time.unscaledTime, 0.05f, "a short line keeps its own time");
                Assert.IsTrue(c.Fresh, "a line just shown is still being read");
                c.Say("Stays.", 0f);
                Assert.Less(c.HideAt, 0f, "a line without a time stays");
            }
            finally { Object.Destroy(go); }
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator ALocalsQuestionsMakeWayForTheCard()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = Story.Chapters, LastFlight = System.DateTime.UtcNow.ToString("yyyy-MM-dd"), FlightStreak = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Handle("a pirate cove", "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Look != null, 25f, "the pirate cove");
            yield return Until(() => d.Local != null, 5f, "someone's in it");
            var who = d.Local;
            yield return Until(() => who.Greeted, 8f, "a hello");
            // While the pieces float around you, his questions wait (a hand reaching for a piece pressed them).
            yield return Until(() => d.Gather.Running, 10f, "pieces to gather");
            var rot0 = d.Head.rotation;
            d.Head.rotation = Quaternion.LookRotation((who.transform.position + Vector3.up * 1f - d.Head.position).normalized);
            float g0 = Time.unscaledTime;
            while (Time.unscaledTime - g0 < 2f)
            {
                Assert.IsFalse(PokeButton.Live.Any(b => b != null && b.Id.StartsWith("Choice local:")), "no questions among the pieces you are gathering");
                yield return null;
            }
            d.Gather.Stop();
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice local:who"), 5f, "his questions, once the pieces are done");
            d.Head.rotation = rot0;
            d.Guide.ShowChoices(null, null);
            d.Handle("make me a fox", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a fox");
            var fox = d.Maker.Things[0];
            yield return new WaitForSeconds(1f);
            // Look at the captain: his three questions.
            d.Head.rotation = Quaternion.LookRotation((who.transform.position + Vector3.up * 1f - d.Head.position).normalized);
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice local:who"), 5f, "his questions");
            // Pick the fox up and hold it to your face, with him behind it: its card, and only its card.
            hand.rotation = Quaternion.identity;
            yield return Pinch(Maker.Centre(fox));
            Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "picked up");
            Vector3 close = d.Head.position + d.Head.forward * 0.3f;
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.8f) { hand.position = Vector3.Lerp(hand.position, close, 0.15f); yield return null; }
            yield return Until(() => d.Inspect.Looking == fox, 3f, "its card");
            yield return Frames(2);
            Assert.IsFalse(PokeButton.Live.Any(b => b != null && b.Id.StartsWith("Choice local:")), "his questions make way for the card: one row at a time");
            yield return new WaitForSeconds(1.5f);
            Assert.IsTrue(who.Watched, "still looking his way");
            Assert.IsFalse(PokeButton.Live.Any(b => b != null && b.Id.StartsWith("Choice local:")), "and stay away while you hold it up");
            hands.SimulatedPinch = false;
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator HoldItUpThenSeeWhereItLives()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return Until(() => d.State == Director.Mode.Room, 10f, "your room");
            // Your real furniture is in front of what you make, in your room.
            yield return Frames(3);
            Assert.Greater(d.Furniture.Count, 0, "the scanned furniture is drawn into depth");
            Assert.IsTrue(d.Furniture.Showing, "in your room");
            d.Handle("make me a zebra", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a zebra");
            var zebra = d.Maker.Things[0];
            yield return new WaitForSeconds(1f);
            hand.rotation = Quaternion.identity;
            yield return Pinch(Maker.Centre(zebra));
            Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "picked up");
            Vector3 close = d.Head.position + d.Head.forward * 0.3f;
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.8f) { hand.position = Vector3.Lerp(hand.position, close, 0.15f); yield return null; }
            yield return Until(() => d.Inspect.Looking == zebra && d.Inspect.HomeButton != null, 3f, "looking at it, with a way home");
            d.Inspect.HomeButton.Press();
            var savanna = Places.Get("savanna");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Look != null && d.Current.Look.Name == savanna.Look.Name, 25f, "the savanna");
            hands.SimulatedPinch = false;
            yield return Until(() => zebra.Size > 2f, 5f, "the zebra at its real size");
            Assert.IsFalse(d.Gather.Running, "no task on a home visit");
            Assert.IsFalse(d.Furniture.Showing, "in a world the furniture has its own skins");
            Assert.GreaterOrEqual(d.Builder.Root.GetComponentsInChildren<Wild>().Count(w => w.Kind == "zebra"), 3, "among its herd");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ALifeSizeThingDoesNotTakeEveryPinch()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            yield return Until(() => d.Gather.Running && d.Gather.Items.Count > 0, 15f, "the kindling");
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Handle("put a t rex on the table", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a T. rex");
            d.Handle("make it life size", "test");
            yield return new WaitForSeconds(1f);
            var piece = d.Gather.Items.First(it => !it.Placed);
            hand.rotation = Quaternion.identity;
            yield return Pinch(piece.transform.position);
            Assert.IsTrue(d.Gather.Holding(HandInput.SimulatedIndex), "the kindling beside a life-size T. rex is what you pick up");
            Assert.IsFalse(d.Maker.Holding(HandInput.SimulatedIndex), "not the T. rex");
            hands.SimulatedPinch = false;
            yield return Frames(3);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator PointAndPinchPullsAThingToYourHand()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.Gather.Stop();
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Handle("make me a fox", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a fox");
            var fox = d.Maker.Things[0];
            yield return new WaitForSeconds(1f);
            // Out of reach, about 2 m ahead; the hand held out towards it, a little off the line.
            Vector3 f = d.Head.forward; f.y = 0f; f.Normalize();
            fox.T.position = d.Head.position + f * 2f + Vector3.down * 0.6f;
            yield return null;
            Vector3 shoulder = d.Head.position + Vector3.down * 0.2f;
            Vector3 toFox = (Maker.Centre(fox) - shoulder).normalized;
            hand.rotation = Quaternion.identity;
            yield return Pinch(shoulder + Quaternion.AngleAxis(10f, Vector3.up) * toFox * 0.45f);
            Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "pointed at and pinched: it comes to the hand");
            yield return new WaitForSeconds(0.5f);
            Assert.Less(Vector3.Distance(Maker.Centre(fox), hand.position), 0.4f, "in the hand");
            hands.SimulatedPinch = false;
            yield return Frames(3);
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator ThingsStandOnTheTableNeverInItAndReachingNeverJumps()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.Gather.Stop();
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Handle("make me a deer", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a deer");
            var deer = d.Maker.Things[0];
            yield return new WaitForSeconds(1.2f);
            var table = d.Rooms.Room.Table;
            float top = table.TopY;
            // Pushed down into the table and let go (tilted, too): it comes up to stand on top, upright.
            hand.rotation = Quaternion.identity;
            yield return Pinch(Maker.Centre(deer));
            Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "picked up");
            Vector3 side = table.Rotation * (table.Size.x >= table.Size.z ? Vector3.right : Vector3.forward);
            Vector3 into = table.TopCenter + side * (Mathf.Max(table.Size.x, table.Size.z) * 0.5f - 0.12f) + Vector3.up * 0.02f;
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.8f) { hand.position = Vector3.Lerp(hand.position, into, 0.2f); hand.rotation = Quaternion.Euler(35f, 20f, 25f); yield return null; }
            yield return new WaitForSeconds(0.4f);
            Assert.Less(deer.R.bounds.min.y, top - 0.03f, "its feet are inside the table while you hold it there");
            hands.SimulatedPinch = false;
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(top, deer.R.bounds.min.y, 0.02f, "let go, it stands on the table");
            Assert.Less(Vector3.Angle(deer.T.up, Vector3.up), 3f, "upright");
            Assert.Greater(Vector3.Distance(Flat(deer.T.position), Flat(table.TopCenter)), Mathf.Min(0.19f, Mathf.Max(table.Size.x, table.Size.z) * 0.5f - 0.14f), "beside the fire, not in it");
            // Exploring: a hand reaching for it, palm down, pinching just short of it, does not jump you away.
            d.OnPanelForTests("explore");
            yield return Frames(3);
            Vector3 before = d.Move.Rig.position;
            hands.SimulatedPalmUp = false;
            Vector3 beside = d.Head.right; beside.y = 0f;
            hand.position = Maker.Centre(deer) + beside.normalized * 0.24f;
            yield return new WaitForSeconds(0.9f);
            hands.SimulatedPinch = true;
            yield return Frames(4);
            hands.SimulatedPinch = false;
            yield return Frames(4);
            Assert.Less(Vector3.Distance(d.Move.Rig.position, before), 0.01f, "reaching for a thing is not a jump");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TouchingAMadeThingMakesItHop()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.Gather.Stop();
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Handle("make me a fox", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a fox");
            var fox = d.Maker.Things[0];
            yield return new WaitForSeconds(1f);
            int before = d.Maker.Touches;
            // Reaching in to pick it up is not a touch.
            hand.rotation = Quaternion.identity;
            yield return Pinch(Maker.Centre(fox));
            Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "picked up");
            Assert.AreEqual(before, d.Maker.Touches, "a reach that closes into a pinch is a grab, not a touch");
            hands.SimulatedPinch = false;
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return new WaitForSeconds(1.5f);
            hand.position = Maker.Centre(fox);
            yield return new WaitForSeconds(Maker.TouchDelay * 0.5f);
            Assert.AreEqual(before, d.Maker.Touches, "a fingertip only just arrived is not a touch yet (it may be closing to pick it up)");
            yield return Until(() => d.Maker.Touches > before, 2f, "a fingertip that rests on it is a touch");
            Assert.AreEqual(before + 1, d.Maker.Touches, "a fingertip touch");
            Assert.NotNull(fox.T.GetComponent<Hop>(), "it hops");
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return new WaitForSeconds(0.8f);
            Assert.IsNull(fox.T.GetComponent<Hop>(), "and lands");
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator SayHiToADeer()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.Gather.Stop();
            yield return Frames(3);
            var deer = d.Builder.Root.GetComponentsInChildren<Wild>().FirstOrDefault(w => w.Kind == "deer");
            Assert.NotNull(deer, "deer live at the forest's edge");
            Assert.AreSame(deer.GetType(), d.AnimalAddressed("Oh, hi, deer. Hi.", null)?.GetType(), "a greeting to the deer is to a deer");
            d.Handle("Oh, hi, deer. Hi.", "test");
            yield return Frames(2);
            Assert.IsTrue(d.Builder.Root.GetComponentsInChildren<Wild>().Any(w => w.Kind == "deer" && w.Watching), "a deer looks up at you");
            Assert.IsNull(d.AnimalAddressed("make it rain", null), "the weather is not for the deer");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AskEmberAboutTheThingYouHold()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.Gather.Stop();
            hand.position = d.Head.position + Vector3.up * 3f;
            ActAi.Canned = said => said.Contains("fast")
                ? "{\"safety\":\"ok\",\"say\":\"Good question.\",\"answer\":\"Scientists think about 20 km an hour.\",\"source\":\"model\",\"actions\":[]}"
                : null;
            try
            {
                d.Handle("put a t rex on the table", "test");
                yield return Until(() => d.Maker.Things.Count > 0, 10f, "a T. rex");
                var rex = d.Maker.Things[0];
                yield return new WaitForSeconds(1f);
                hand.rotation = Quaternion.identity;
                yield return Pinch(Maker.Centre(rex));
                Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "picked up");
                Vector3 close = d.Head.position + d.Head.forward * 0.3f;
                float t0 = Time.unscaledTime;
                while (Time.unscaledTime - t0 < 0.8f) { hand.position = Vector3.Lerp(hand.position, close, 0.15f); yield return null; }
                yield return Until(() => d.Inspect.Looking == rex, 3f, "looking at it");
                StringAssert.Contains("Ask Juniper", d.Inspect.CardText);
                // A suggested question on the card: offline (no canned answer for it), the card's own fact.
                Assert.NotNull(d.Inspect.AskButton, "a question to touch");
                d.Inspect.AskButton.Press();
                yield return Until(() => d.Inspect.CardText.Contains("bite"), 5f, "an answer on its card");
                d.Handle("how fast could it run?", "test");
                yield return Until(() => d.Inspect.CardText.Contains("20 km"), 5f, "the answer on its card");
                Assert.AreEqual(1, d.Maker.Things.Count, "a question makes nothing");
                hands.SimulatedPinch = false;
                yield return new WaitForSeconds(0.5f);
            }
            finally { ActAi.Canned = null; }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ItMeansTheThingYouHeldAndItGoesBackFromYourFace()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.Gather.Stop();
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Handle("put a t rex on the table", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a T. rex");
            d.Handle("make me a fox on the table", "test");
            yield return Until(() => d.Maker.Things.Count > 1, 10f, "a fox");
            var rex = d.Maker.Things[0];
            var fox = d.Maker.Things[1];
            yield return new WaitForSeconds(1.2f);
            // Both on the table top, beside its middle (the fire), not in it and not in each other.
            var table = d.Rooms.Room.Table;
            foreach (var x in new[] { rex, fox })
                Assert.Greater(Vector3.Distance(Flat(x.T.position), Flat(table.TopCenter)), 0.15f, x.Name + " stands beside the fire");
            Assert.Greater(Vector3.Distance(Flat(rex.T.position), Flat(fox.T.position)), (rex.Size + fox.Size) * 0.4f, "side by side");
            // Hold the T. rex up to your face and let it go: it goes back to the table.
            Vector3 home = rex.T.position;
            hand.rotation = Quaternion.identity;
            yield return Pinch(Maker.Centre(rex));
            Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "picked up");
            Vector3 close = d.Head.position + d.Head.forward * 0.3f;
            float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 0.8f) { hand.position = Vector3.Lerp(hand.position, close, 0.15f); yield return null; }
            yield return new WaitForSeconds(0.4f);
            hands.SimulatedPinch = false;
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(2, d.Maker.Things.Count, "not thrown");
            Assert.Less(Vector3.Distance(rex.T.position, home), 0.03f, "back where it was");
            // "It" is the T. rex you held, not the fox made last.
            float foxSize = fox.Size;
            d.Handle("make it life size", "test");
            Assert.AreEqual(12f, rex.Size, 0.1f, "the T. rex at its real size, in a world");
            Assert.AreEqual(foxSize, fox.Size, 0.001f, "the fox unchanged");
            Assert.Greater(Vector3.Distance(Flat(rex.T.position), Flat(d.Head.position)), d.Rooms.Room.OuterRadius, "out past your walls");
            Assert.Greater(d.Maker.SofasShown, 1, "measured out in your own sofas, beside it");
        }

        /// <summary>Raises the hand <paramref name="metres"/> over <paramref name="seconds"/>, at a hand's speed whatever the frame rate.</summary>
        IEnumerator Lift(float metres, float seconds)
        {
            float start = hand.position.y, t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < seconds)
            {
                hand.position = new Vector3(hand.position.x, start + metres * (Time.unscaledTime - t0) / seconds, hand.position.z);
                yield return null;
            }
            hand.position = new Vector3(hand.position.x, start + metres, hand.position.z);
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator OneFingertipPressesOneKey()
        {
            int a = 0, b = 0;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Vector3 at = d.Head.position + d.Head.forward * 0.4f;
            var rot = Quaternion.LookRotation(at - d.Head.position, Vector3.up);
            var ka = PokeButton.Make("test key a", null, d.GlowFlat, font, "y", new Vector2(0.03f, 0.03f));
            var kb = PokeButton.Make("test key b", null, d.GlowFlat, font, "u", new Vector2(0.03f, 0.03f));
            Vector3 side = rot * Vector3.right;
            ka.transform.SetPositionAndRotation(at - side * 0.0145f, rot);
            kb.transform.SetPositionAndRotation(at + side * 0.0145f, rot);
            ka.Pressed += () => a++;
            kb.Pressed += () => b++;
            Vector3 toUser = -ka.transform.forward;
            // A fingertip comes straight in, a little to the left of the gap between the keys.
            hand.position = at - side * 0.003f + toUser * 0.04f;
            yield return Frames(3);
            hand.position = at - side * 0.003f - toUser * 0.005f;
            yield return Frames(3);
            Assert.AreEqual(1, a + b, "one press, not two");
            Assert.AreEqual(1, a, "the key it is most squarely on");
            Object.Destroy(ka.gameObject);
            Object.Destroy(kb.gameObject);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TheMenuWordsNeverSitOnItsButtons()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.Gather.Stop();
            hand.position = d.Head.position + Vector3.up * 3f;
            foreach (var page in new[] { HandPanel.Page.Main, HandPanel.Page.Say, HandPanel.Page.Comfort, HandPanel.Page.Codes })
            {
                d.Panel.Show(page);
                yield return Frames(3);
                float top = float.MinValue, bottom = float.MaxValue;
                foreach (var btn in d.Panel.Buttons)
                {
                    if (btn == null || !btn.isActiveAndEnabled) continue;
                    float y = btn.transform.localPosition.y;
                    top = Mathf.Max(top, y + btn.Size.y * 0.5f);
                    bottom = Mathf.Min(bottom, y - btn.Size.y * 0.5f);
                }
                Assert.Greater(top, float.MinValue, page + " has buttons");
                Assert.LessOrEqual(Mathf.RoundToInt((top - bottom + 0.012f) / 0.056f), HandPanel.MaxRows + 1, page + ": four rows at most");
                Assert.GreaterOrEqual(d.Panel.TitleLine.transform.localPosition.y, top, page + ": the title is above the buttons");
                Assert.LessOrEqual(d.Panel.FooterLine.transform.localPosition.y, bottom, page + ": the footer is below them");
            }
            d.Panel.Hide();
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator SayAWorldSomeonesInIt()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = Story.Chapters, LastFlight = System.DateTime.UtcNow.ToString("yyyy-MM-dd"), FlightStreak = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Handle("a pirate cove", "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Look != null, 25f, "the pirate cove");
            // The shells first (a local's questions wait while there are pieces to gather): set them aside.
            yield return Until(() => d.Gather.Running, 10f, "the shells");
            d.Gather.Stop();
            yield return Until(() => d.Local != null, 5f, "someone's in it");
            var who = d.Local;
            Assert.AreEqual("Captain Brine", who.Def.Name);
            Assert.Greater(Vector3.Distance(Flat(who.transform.position), Flat(d.Head.position)), d.Rooms.Room.OuterRadius, "past where your walls were");
            Assert.Less(Vector3.Distance(Flat(who.transform.position), Flat(d.Head.position)), d.Rooms.Room.OuterRadius + 4f, "near enough to meet");
            var r = who.GetComponent<Renderer>();
            Assert.AreEqual(Locals.Height(who.Def), r.bounds.size.y, 0.2f, "small folk: eye to eye with you seated");
            Assert.AreEqual(Locals.People, ModelKit.Models("person").Count, "every one of the people is in the kit");
            Assert.AreEqual(12, who.Def.Seed, "the captain is the bearded one with the eye patch");
            // Nothing in the room stands between you and them.
            Vector3 eye = d.Rooms.Room.Head, face = who.transform.position + Vector3.up * 1.05f;
            for (int i = 1; i <= 10; i++)
                foreach (var piece in d.Rooms.Room.Pieces)
                    Assert.IsTrue(piece.Virtual || piece.Distance(Vector3.Lerp(eye, face, i / 11f)) >= 0.05f, "a clear line of sight to them past the " + piece.Label);
            // The bar of buttons is within a seated arm's reach.
            foreach (var bar in PokeButton.Live.Where(b => b.Id.StartsWith("dock ") && b.gameObject.activeInHierarchy))
                Assert.Less(Vector3.Distance(Flat(bar.transform.position), Flat(d.Head.position)), 0.75f, bar.Id + " is within reach");
            // They say hello first, a few seconds after you arrive, without being stared at.
            yield return Until(() => who.Greeted, 8f, "a hello without being looked at");
            StringAssert.Contains("Ahoy", d.LocalSaid);
            // Look at them: they say hello, and three questions appear within reach.
            var before = d.Head.rotation;
            d.Head.rotation = Quaternion.LookRotation((who.transform.position + Vector3.up * 1f - d.Head.position).normalized);
            yield return Until(() => who.Greeted, 5f, "a hello");
            StringAssert.Contains("Captain Brine", d.LocalSaid);
            StringAssert.Contains("Ahoy", d.LocalSaid);
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "Choice local:who"), 5f, "questions to ask");
            yield return Frames(2);
            var asks = PokeButton.Live.Where(b => b.Id.StartsWith("Choice local:")).ToList();
            Assert.AreEqual(3, asks.Count, "three questions");
            foreach (var ask in asks)
            {
                Assert.Less(Vector3.Distance(Flat(ask.transform.position), Flat(d.Head.position)), 0.7f, ask.Id + " is within an arm's reach");
                Assert.Less(ask.transform.position.y, d.Head.position.y - 0.1f, ask.Id + " is below your line of sight to them");
                Assert.Less(Vector3.Angle(d.Head.forward, ask.transform.position - d.Head.position), 48f, ask.Id + " is in view while you look at them");
                if (d.Rooms.Room.Table != null) Assert.Greater(ask.transform.position.y, d.Rooms.Room.Table.TopCenter.y + 0.05f, ask.Id + " is above the table");
                foreach (var dockButton in PokeButton.Live.Where(b => b.Id.StartsWith("dock ") && b.gameObject.activeInHierarchy))
                    Assert.Greater(Vector3.Distance(ask.transform.position, dockButton.transform.position), 0.07f, ask.Id + " is clear of the " + dockButton.Id + " button");
            }
            // They do not hop about as your head sways, and a fingertip cannot press one the moment it appears.
            var whoAsk = PokeButton.Live.First(b => b.Id == "Choice local:who");
            Vector3 askAt = whoAsk.transform.position;
            d.Head.position += d.Head.right * 0.05f;
            yield return Frames(3);
            Assert.Less(Vector3.Distance(askAt, whoAsk.transform.position), 0.005f, "the row stays put when your head sways 5 cm");
            d.Head.position -= d.Head.right * 0.05f;
            yield return new WaitForSecondsRealtime(Guide.ChoiceArm + 0.1f);
            Assert.IsTrue(whoAsk.Armed, "armed half a second later");
            PokeButton.Live.First(b => b.Id == "Choice local:who").Press();
            yield return Frames(2);
            StringAssert.Contains("Retired", d.LocalSaid, "who he is");
            // By voice, while you look at them; anything else is answered by Claude, as them.
            d.Handle("what is this place?", "test");
            yield return Frames(2);
            StringAssert.Contains("ship", d.LocalSaid);
            // "Juniper, what can I do here?" while you look at them is to Juniper (speech arrives with her name taken
            // off, heard as "voice-name"): her help card, and the captain stays quiet. Without her name, it is to him.
            string saidBefore = d.LocalSaid;
            d.Handle("what can I do here?", "voice-name");
            yield return Frames(2);
            Assert.IsTrue(d.Help != null && d.Help.Showing, "said to Juniper by name: her help card");
            Assert.AreEqual(saidBefore, d.LocalSaid, "and the captain does not answer it");
            d.Help.Hide();
            yield return Frames(2);
            d.Handle("what can I do here?", "voice");
            yield return Frames(2);
            Assert.AreNotEqual(saidBefore, d.LocalSaid, "said without her name while you look at him: the captain answers");
            Assert.IsFalse(d.Help.Showing, "not the help card");
            ActAi.Canned = said => said.Contains("treasure") ? "{\"safety\":\"ok\",\"say\":\"Ha!\",\"answer\":\"Found it twice, lost it three times.\",\"source\":\"model\",\"actions\":[]}" : null;
            try
            {
                d.Handle("did you ever find treasure?", "test");
                yield return Until(() => d.LocalSaid.Contains("lost it three times"), 5f, "Claude answers as the captain");
            }
            finally { ActAi.Canned = null; }
            // Their tip works when you say it.
            d.Head.rotation = before;
            d.Handle(Locals.TipPhrase(who.Def), "test");
            yield return Until(() => d.Current.Extras.Contains(Extras.Lanterns), 10f, "the cove lights up");
            Assert.AreEqual("Pirate cove at sunset", d.Current.Look != null ? d.Current.Look.Name : "", "and you are still in the cove");
            // While Mara's call is on, they step out of sight.
            d.StartMission("test");
            yield return Until(() => d.Mission.Active, 10f, "Mara's call");
            yield return Frames(3);
            Assert.IsFalse(who != null && who.gameObject.activeSelf, "the local steps aside for Mara");
            d.Mission.Stop();
        }

        [UnityTest, Timeout(150000)]
        public IEnumerator TheSavannaIsAlive()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            var savanna = Places.Get("savanna");
            d.Open(savanna.Recipe(3), "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Look != null && d.Current.Look.Name == savanna.Look.Name, 25f, "the savanna");
            d.Gather.Stop();
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return Frames(3);
            var animals = Object.FindObjectsByType<Wild>(FindObjectsSortMode.None);
            Assert.GreaterOrEqual(animals.Length, 4, "animals live there");
            foreach (var a in animals)
            {
                var r = a.GetComponent<Renderer>();
                float largest = Mathf.Max(r.bounds.size.x, Mathf.Max(r.bounds.size.y, r.bounds.size.z));
                Assert.Greater(largest, LifeSize.Metres(a.Kind) * 0.6f, a.Kind + " at its real size");
                Assert.Greater(Vector3.Distance(Flat(a.transform.position), Flat(d.Head.position)), 6f, a.Kind + " out in the world, not in your room");
            }
            // Watched for a moment, one turns to look at you.
            var one = animals[0];
            var before = d.Head.rotation;
            d.Head.rotation = Quaternion.LookRotation((one.transform.position + Vector3.up * one.Size * 0.3f - d.Head.position).normalized);
            yield return Until(() => one.Watching, 5f, "it notices you");
            yield return new WaitForSeconds(6.5f);
            Vector3 back = d.Head.position - one.transform.position; back.y = 0f;
            Vector3 face = one.transform.forward; face.y = 0f;
            Assert.Less(Vector3.Angle(face, back), 30f, "it turned to look at you");
            d.Head.rotation = before;
            d.Open(Recipe.For("space", 3), "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Biome == "space", 25f, "space");
            yield return Frames(3);
            Assert.AreEqual(0, Object.FindObjectsByType<Wild>(FindObjectsSortMode.None).Length, "nothing lives in orbit");
            // And the volcano place has its volcano.
            var volcano = Places.Get("volcano");
            d.Open(volcano.Recipe(3), "test");
            yield return Until(() => d.State == Director.Mode.World && d.Current.Look != null && d.Current.Look.Name == volcano.Look.Name, 25f, "the volcano");
            Assert.NotNull(d.Builder.Root.Find("Volcano"), "a volcano stands there");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AMadeAnimalNoticesYou()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.Gather.Stop();
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Handle("make me a cat", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a cat");
            var t = d.Maker.Things[0];
            Assert.NotNull(t.T.GetComponent<Alive>(), "a cat is alive");
            yield return new WaitForSeconds(0.8f);
            // Turned away, it turns back to you while you look at it.
            Vector3 to = d.Head.position - t.T.position; to.y = 0f;
            t.T.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(90f, Vector3.up) * to, Vector3.up);
            yield return new WaitForSeconds(3f);
            Vector3 face = t.T.forward; face.y = 0f;
            Assert.Less(Vector3.Angle(face, to), 15f, "it faces you");
            d.Handle("make me a lantern", "test");
            yield return Until(() => d.Maker.Things.Count > 1, 10f, "a lantern");
            Assert.IsNull(d.Maker.Things[1].T.GetComponent<Alive>(), "a lantern is not");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator OnALongTableItStandsWhereYouCanReachIt()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return Until(() => d.State == Director.Mode.Room, 10f, "your room");
            var room = d.Rooms.Room;
            var table = room.Table;
            Vector3 size0 = table.Size, centre0 = table.Center;
            Vector3 head0 = d.Head.position; Quaternion look0 = d.Head.rotation;
            try
            {
                // A dining table 2.4 m long, and you at one end of it.
                table.Size = new Vector3(2.4f, size0.y, 0.9f);
                Vector3 end = table.TopCenter + table.Rotation * (Vector3.right * 1.65f) + Vector3.up * 0.45f;
                d.Head.SetPositionAndRotation(end, Quaternion.LookRotation(table.Rotation * Vector3.left, Vector3.up));
                foreach (int n in new[] { 1, 2 })
                {
                    d.Maker.Make(new ActItem { Do = "make", Kind = "fox", Name = "fox", Place = "table", Count = 1, Size = Wishes.SizeOf("fox") }, room, Recipe.For("campfire", 1));
                    Assert.AreEqual(n, d.Maker.Things.Count);
                    var fox = d.Maker.Things[n - 1];
                    Vector3 flat = fox.T.position - end; flat.y = 0f;
                    Assert.LessOrEqual(flat.magnitude, n == 1 ? Maker.HandyReach + 0.01f : 1f, $"fox {n} is within reach from the end of the table (the first within easy reach, the next an arm's stretch): " + string.Join(" ", d.Maker.Things.Select(x => (Quaternion.Inverse(table.Rotation) * (x.T.position - table.TopCenter)).ToString("0.00"))));
                    Assert.AreEqual(table.TopY, fox.T.position.y, 0.03f, "on the table top");
                    Vector3 local = Quaternion.Inverse(table.Rotation) * (fox.T.position - table.TopCenter);
                    Assert.LessOrEqual(Mathf.Abs(local.x), 1.2f, "on the table");
                    Assert.LessOrEqual(Mathf.Abs(local.z), 0.45f, "on the table");
                    for (int k = 0; k < n - 1; k++)
                    {
                        Vector3 gap = d.Maker.Things[k].T.position - fox.T.position; gap.y = 0f;
                        Assert.Greater(gap.magnitude, 0.1f, "not on top of another fox");
                    }
                }
            }
            finally
            {
                table.Size = size0; table.Center = centre0;
                d.Head.SetPositionAndRotation(head0, look0);
                d.Maker.Clear();
            }
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator ASmallAnimalWalksToYourOpenHand()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = 1 });
            hand.position = d.Head.position + Vector3.up * 3f;
            hands.SimulatedPinch = false;
            yield return Until(() => d.State == Director.Mode.Room, 10f, "your room");
            d.Handle("make me a fox on the table", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a fox");
            var fox = d.Maker.Things[0];
            var alive = fox.T.GetComponent<Alive>();
            yield return new WaitForSeconds(1.5f);
            var table = d.Rooms.Room.Table;
            Vector3 from = fox.T.position;
            // An open hand laid on the table, a little way from it.
            Vector3 dir = Vector3.zero;
            for (int k = 0; k < 8 && dir == Vector3.zero; k++)
            {
                var c = Quaternion.Euler(0f, k * 45f, 0f) * Vector3.forward;
                if (d.Maker.SameGround(from, from + c * 0.15f) && d.Maker.SameGround(from, from + c * 0.3f)) dir = c;
            }
            Assert.AreNotEqual(Vector3.zero, dir, "room on the table beside it");
            Vector3 palm = from + dir * 0.34f + Vector3.up * 0.03f;
            hand.position = palm;
            Assert.IsNull(d.Maker.Offered(fox), "a hand that has only just arrived is not an offer yet");
            yield return Until(() => alive.Coming, 5f, "it sets off towards the hand");
            // Its legs step as it comes: the low corners of the mesh are marked as legs, the body is not.
            var gait = fox.T.GetComponent<Gait>();
            Assert.NotNull(gait, "a fox has legs to walk on");
            var legs = new System.Collections.Generic.List<Vector2>();
            var foxMesh = fox.T.GetComponent<MeshFilter>().sharedMesh;
            foxMesh.GetUVs(1, legs);
            var corners = foxMesh.vertices;
            Assert.AreEqual(corners.Length, legs.Count, "every corner is marked");
            float top = foxMesh.bounds.max.y, low = foxMesh.bounds.min.y;
            int feet = 0, pairA = 0, pairB = 0;
            for (int i = 0; i < corners.Length; i++)
            {
                if (corners[i].y > low + (top - low) * 0.5f) Assert.AreEqual(0f, legs[i].x, 1e-4f, "nothing above its legs swings");
                if (legs[i].x > 0.5f) { feet++; if (legs[i].y > 0.25f) pairA++; else pairB++; }
            }
            Assert.Greater(feet, 8, "it has feet");
            Assert.Greater(pairA, 0, "and they step in two pairs"); Assert.Greater(pairB, 0);
            yield return Until(() => gait.Swing > fox.Size * 0.03f, 3f, "its legs swing as it walks");
            Assert.Greater(gait.Stride, 0f, "and it is somewhere in its stride");
            yield return Until(() => d.Maker.Came > 0, 10f, "it reaches the hand");
            Vector3 gap = palm - fox.T.position; gap.y = 0f;
            Assert.LessOrEqual(gap.magnitude, fox.Size * 0.5f + 0.08f, "beside the hand");
            Assert.Greater(Vector3.Distance(from, fox.T.position), 0.08f, "it walked");
            Assert.Less(Vector3.Angle(fox.T.forward, gap), 35f, "facing the hand");
            yield return new WaitForSeconds(0.6f);   // the greeting hop
            Assert.AreEqual(table.TopY, fox.R.bounds.min.y, 0.03f, "still standing on the table");
            Assert.AreEqual(1, alive.Arrivals, "one greeting, not one a frame");
            yield return Until(() => gait.Swing <= 1e-5f, 3f, "standing again, its legs are still");
            Assert.IsFalse(Gait.HasLegs("fish"), "a fish has none");
            Assert.IsFalse(Gait.HasLegs("lantern"), "nor a lantern");
            // A hand closing to pinch is a grab, not an offer; and a hand taken away calls nobody.
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return Frames(3);
            Assert.IsNull(d.Maker.Offered(fox), "no hand, no call");
            Assert.IsFalse(alive.Coming);
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AnAnimalOnYourTablePottersAboutWhileYouLookAway()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MissionChapter = 1, LastFlight = System.DateTime.UtcNow.ToString("yyyy-MM-dd"), FlightStreak = 3 });
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return Until(() => d.State == Director.Mode.Room, 10f, "your room");
            yield return Frames(3);
            Assert.AreEqual(3, Object.FindFirstObjectByType<Souvenirs>().Beacons, "a beacon on your table for each day in a row");
            d.Handle("make me a fox on the table", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a fox");
            var fox = d.Maker.Things[0];
            var alive = fox.T.GetComponent<Alive>();
            yield return new WaitForSeconds(1.5f);
            var table = d.Rooms.Room.Table;
            Quaternion look = d.Head.rotation;
            d.Head.rotation = Quaternion.LookRotation(-d.Head.forward);   // look away
            yield return Until(() => alive.Walks > 0, 15f, "it potters about");
            yield return new WaitForSeconds(3f);
            Vector3 local = Quaternion.Inverse(table.Rotation) * (fox.T.position - table.TopCenter);
            Assert.LessOrEqual(Mathf.Abs(local.x), table.Size.x * 0.5f, "still on the table");
            Assert.LessOrEqual(Mathf.Abs(local.z), table.Size.z * 0.5f, "still on the table");
            Assert.AreEqual(table.TopY, fox.R.bounds.min.y, 0.03f, "standing on it");
            d.Head.rotation = look;   // look back: it stops
            yield return Frames(3);
            Assert.IsFalse(alive.Walking, "it stops when you look at it");
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator AMadeThingSitsOnTheTableWithItsShadow()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            d.Gather.Stop();
            d.Handle("make me a dog on the table", "test");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a dog");
            var t = d.Maker.Things[0];
            float top = d.Rooms.Room.Table.TopY;
            yield return new WaitForSeconds(1.2f);
            Assert.AreEqual(top, t.R.bounds.min.y, 0.03f, "it stands on the table");
            Assert.NotNull(t.Shade, "it has a shadow");
            Assert.IsTrue(t.Shade.enabled, "the shadow shows");
            Assert.AreEqual(top, t.Shade.transform.position.y, 0.01f, "on the table top");
            // Picked up and let go a hand's width above the table, it settles back onto it.
            hand.rotation = Quaternion.identity;
            yield return Pinch(t.T.position + Vector3.up * (t.Size * 0.4f));
            Assert.IsTrue(d.Maker.Holding(HandInput.SimulatedIndex), "picked up");
            yield return Lift(0.12f, 0.6f);
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(t.R.bounds.min.y, top + 0.06f, "lifted");
            hands.SimulatedPinch = false;
            yield return new WaitForSeconds(1.5f);
            Assert.AreEqual(top, t.R.bounds.min.y, 0.03f, "back on the table");
            // Held up high and let go, it floats where it was left.
            yield return Pinch(t.T.position + Vector3.up * (t.Size * 0.4f));
            yield return Lift(0.45f, 1.2f);
            yield return new WaitForSeconds(0.3f);   // held still a moment: letting go is not a flick
            hands.SimulatedPinch = false;
            yield return new WaitForSeconds(1f);
            Assert.AreEqual(1, d.Maker.Things.Count, "still there: a slow lift is not a throw");
            Assert.Greater(t.R.bounds.min.y, top + 0.3f, "high up, it stays");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator ProgressSurvivesALaunch()
        {
            // What the player earned is on disk; a new launch reads it back, and nothing writes over it first.
            d.ResetProgress(new Progress { Onboarded = true, MissionChapter = 2 });
            string path = System.IO.Path.Combine(Application.persistentDataPath, "worldeck-progress.json");
            StringAssert.Contains("\"mission_chapter\":2", System.IO.File.ReadAllText(path));
            var fresh = new GameObject("Second launch").AddComponent<Director>();
            Assert.IsFalse(fresh.ProgressLoaded);
            fresh.SaveProgress();   // something on the first frame asks to save before Start
            StringAssert.Contains("\"mission_chapter\":2", System.IO.File.ReadAllText(path), "an early save did not wipe the player's progress");
            Object.Destroy(fresh.gameObject);
            yield return null;
        }

        [UnityTest, Timeout(120000)]
        public IEnumerator TheDockAlwaysOffersNextWorldsAndMyRoom()
        {
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true });
            d.Open(Recipe.For("campfire", 3), "test");
            yield return Until(() => d.State == Director.Mode.World, 25f, "a world");
            hand.position = d.Head.position + Vector3.up * 3f;
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "dock next" && b.isActiveAndEnabled), 8f, "the dock");
            // (It comes with the task, and takes presses once a carrying hand could no longer be passing over it.)
            yield return Until(() => !d.Dock.Busy, 3f, "the dock ready");
            var next = PokeButton.Live.First(b => b.Id == "dock next");
            Assert.Less(Vector3.Distance(next.transform.position, d.Head.position), 0.85f, "within reach");
            Assert.Less(next.transform.position.y, d.Head.position.y - 0.2f, "below the line of sight");
            Assert.Less(Vector3.Distance(next.transform.position, d.Rooms.Room.Table.TopCenter), 0.9f, "on the near edge of the table");
            next.Press();
            // A hand sweeping on across the dock touches My room too: that second touch is ignored.
            PokeButton.Live.First(b => b.Id == "dock room").Press();
            yield return Until(() => d.State == Director.Mode.World && d.Current.Biome == Featured.Ids[1], 25f, "the next world, not my room");
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "dock worlds" && b.isActiveAndEnabled), 8f, "the dock again");
            yield return Until(() => !d.Dock.Busy, 3f, "the dock ready");
            Assert.IsFalse(PokeButton.Live.Any(b => b.Id == "dock prev" && b.isActiveAndEnabled), "no Previous: it led away from the story");
            PokeButton.Live.First(b => b.Id == "dock worlds").Press();
            yield return Frames(3);
            Assert.IsTrue(d.Map.Showing, "Worlds opens the map");
            // There is always a next step: with nothing found yet, the line points at the beam; after that, the storm.
            Assert.IsNotEmpty(d.Dock.Next, "the dock always says what to do next");
            d.Map.Hide();
            yield return new WaitForSecondsRealtime(Dock.Calm + 0.1f);   // a person's next press, not the same sweep
            yield return Until(() => PokeButton.Live.Any(b => b.Id == "dock room" && b.isActiveAndEnabled), 8f, "the dock");
            PokeButton.Live.First(b => b.Id == "dock room").Press();
            yield return Until(() => d.State == Director.Mode.Room, 15f, "my room");
            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(PokeButton.Live.Any(b => b.Id == "dock next" && b.isActiveAndEnabled), "no dock in the room");
        }

        [UnityTest, Timeout(180000)]
        public IEnumerator TheStormButtonComesEvenIfNothingIsRoastedOrPickedUp()
        {
            // A first-time player who does nothing at all: the pieces are brought over, the roast is skipped,
            // and the bright button to the storm still appears.
            d.ResetProgress(new Progress());
            hand.position = d.Head.position + Vector3.up * 3f;
            d.Guide.Button.Press();
            yield return new WaitForSecondsRealtime(0.5f);
            if (d.State == Director.Mode.Room) d.Guide.Button.Press();   // past the animal choice
            yield return Until(() => d.State == Director.Mode.World, 25f, "the first world");
            yield return Until(() => d.Gather.Running, 15f, "the task");
            Time.timeScale = 8f;
            yield return Until(() => !d.Gather.Running && d.Gather.PlacedCount == d.Gather.Total, 30f, "Juniper to bring the pieces over");
            yield return Until(() => d.Mission.Active || d.Dock.ActionLabel == "Answer the call", 40f, "Mara's call, with no roast");
            Time.timeScale = 1f;
            Assert.IsTrue(d.Mission.Active || PokeButton.Live.Any(b => b.Id == "dock do" && b.isActiveAndEnabled), "the story goes on by itself, or the bright button is there to press");
        }

        [UnityTest, Timeout(60000)]
        public IEnumerator TheCoachActsOutEveryGesture()
        {
            var go = new GameObject("Test coach");
            var c = go.AddComponent<HandCoach>();
            c.Head = d.Head; c.Hands = d.Hands; c.Ghost = d.Coach.Ghost; c.Panel = d.Coach.Panel; c.Font = d.Coach.Font;
            hand.position = d.Head.position + Vector3.up * 2f;   // the simulated hand out of the way (a near hand fades the ghost)
            yield return null;
            var table = d.Rooms.Room.Table;
            Vector3 a = table.TopCenter + Vector3.up * 0.1f, b = a + d.Head.right * 0.2f;
            foreach (HandCoach.Demo demo in System.Enum.GetValues(typeof(HandCoach.Demo)))
            {
                if (demo == HandCoach.Demo.None) continue;
                c.Show(demo, a, b, "test", GhostHand.Capsule(0f, 0.02f, 0.02f), 1f, 2);
                float until = Time.realtimeSinceStartup + 1f;
                bool seen = false;
                while (Time.realtimeSinceStartup < until)
                {
                    yield return null;
                    foreach (var g in go.GetComponentsInChildren<GhostHand>())
                    {
                        Assert.IsFalse(float.IsNaN(g.transform.position.x), demo + ": no NaN");
                        if (g.Alpha > 0.5f) seen = true;
                    }
                }
                Assert.AreEqual(demo, c.Playing, demo + " still playing");
                Assert.IsTrue(seen, demo + ": the hand was shown");
                Assert.Less(Vector3.Distance(go.GetComponentInChildren<GhostHand>().transform.position, a), 0.6f, demo + ": shown where asked");
            }
            c.Stop("test");
            Assert.AreEqual(HandCoach.Demo.None, c.Playing);
            Object.Destroy(go);
        }
    }
}
