using System.Collections;
using System.IO;
using System.Linq;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// Development builds only: plays Mara's whole story by itself, end to end, for a demo recording with nobody
    /// wearing the headset. Started by a file named demo.txt in the app's files folder (pushed with adb); the file is
    /// removed as it starts, so it runs once. A see-through hand (the coach's GhostHand) is drawn where the simulated
    /// hand is: it carries each glowing piece to the table, presses the choices with a fingertip, blocks the meteors
    /// with an open palm (it lets the first one aimed at the sofa through, in chapter 1), holds the big one until its
    /// ring fills, and waves goodbye. The player's progress is put back as it was at the end. Never in the judges'
    /// build (release builds return at once). The hand is a stand-in: footage of it is a scripted demo, not a player.
    /// </summary>
    public sealed class DemoPilot : MonoBehaviour
    {
        public Director Director;
        public bool Running { get; private set; }
        Transform hand;          // the simulated hand the app reads (input)
        GhostHand ghost;         // what is seen
        Vector3 ghostAt;
        Quaternion ghostRot = Quaternion.identity;
        GhostHand.Anchor ghostAnchor = GhostHand.Anchor.Palm;
        bool ghostOn;
        string victim = "";
        bool letOneThrough;

        IEnumerator Start()
        {
            if (!Debug.isDebugBuild || Director == null) yield break;
            string trigger = Path.Combine(Application.persistentDataPath, "demo.txt");
            if (!File.Exists(trigger)) yield break;
            string mode = "";
            try { mode = File.ReadAllText(trigger).Trim().ToLowerInvariant(); } catch { }
            try { File.Delete(trigger); } catch { }
            Running = true;
            var d = Director;
            Trace.Info("Demo", "started", "mode", mode.Length > 0 ? mode : "story");
            yield return new WaitForSecondsRealtime(6f);
            string kept = d.Progress.ToJsonString();
            hand = new GameObject("Demo palm").transform;
            Park();
            if (d.Hands != null) d.Hands.SimulatedHand = hand;
            if (d.Coach != null && d.Coach.Ghost != null) ghost = GhostHand.Make("Demo hand", transform, d.Coach.Ghost, false);
            if (ghost != null) { ghost.Alpha = 0.95f; ghost.gameObject.SetActive(false); }
            ghostAt = Rest();
            if (mode == "tour") { yield return Tour(); Finish(kept); yield break; }
            // A new player who has met the animals: Mara's first call comes as the first world's task is done.
            d.ResetProgress(new Progress { Onboarded = true, ExploreShown = true, VoiceOffered = true, MusicOn = true });
            yield return new WaitForSecondsRealtime(4f);

            // Chapter 1: the camp, the task, her call, one meteor through to the sofa.
            d.Open(Recipe.For("campfire", 3), "demo");
            yield return Until(() => d.State == Director.Mode.World && d.Gather != null && d.Gather.Running, 40f, "the camp's task");
            yield return new WaitForSecondsRealtime(4f);
            yield return GatherByHand();
            yield return Until(() => d.Mission != null && d.Mission.Active, 40f, "her first call");
            letOneThrough = true;
            yield return Chapter("m:slow");

            // The next world: the sofa stands dark until the pieces light it again.
            yield return Until(() => d.Dock.ActionLabel == "Next world", 30f, "the next world offered");
            yield return new WaitForSecondsRealtime(3f);
            yield return PokeDock();
            yield return Until(() => d.State == Director.Mode.World && d.Gather.Running, 40f, "the next world's task");
            yield return new WaitForSecondsRealtime(6f);
            yield return GatherByHand();
            yield return Until(() => d.Dock.ActionLabel == "Answer Mara", 40f, "lit again, and Answer Mara");
            yield return new WaitForSecondsRealtime(3f);

            // Chapter 2: she sees it.
            yield return PokeDock();
            yield return Until(() => d.Mission.Active, 10f, "her second call");
            letOneThrough = false;
            yield return Chapter("m:fast");

            // Chapter 3: the next world, her last call, the big meteor, then home.
            yield return Until(() => d.Dock.ActionLabel == "Next world", 30f, "the next world offered");
            yield return new WaitForSecondsRealtime(6f);   // (the place-to-try and voice choices show for a moment)
            d.Guide.ShowChoices(null, null);
            yield return PokeDock();
            yield return Until(() => d.State == Director.Mode.World && d.Gather.Running, 40f, "the third world's task");
            yield return new WaitForSecondsRealtime(3f);
            yield return GatherByHand();
            yield return Until(() => d.Dock.ActionLabel == "Answer Mara", 40f, "Answer Mara");
            yield return new WaitForSecondsRealtime(2f);
            yield return PokeDock();
            yield return Until(() => d.Mission.Active, 10f, "her last call");
            yield return Chapter("m:fast");
            yield return Until(() => d.Dock.ActionLabel == "My room", 30f, "My room offered");
            yield return new WaitForSecondsRealtime(4f);
            yield return PokeDock();
            yield return Until(() => d.State == Director.Mode.Room, 30f, "home");
            // The ending: keepsakes, her ship, Juniper's words (all of it, then a moment).
            yield return new WaitForSecondsRealtime(3f);
            yield return Until(() => !d.StoryEnding, 60f, "the ending told");
            yield return new WaitForSecondsRealtime(2f);

            Finish(kept);
        }

        void Finish(string kept)
        {
            var d = Director;
            ghostOn = false;
            if (d.Hands != null)
            {
                d.Hands.SimulatedPinch = false; d.Hands.SimulatedFist = false; d.Hands.SimulatedPalmOut = false;
                if (d.Hands.SimulatedHand == hand) d.Hands.SimulatedHand = null;
            }
            if (hand != null) Destroy(hand.gameObject);
            if (ghost != null) Destroy(ghost.gameObject);
            d.ResetProgress(Progress.Parse(kept, out _));
            Trace.Info("Demo", "done: progress put back");
            Running = false;
        }

        /// <summary>
        /// Everything else Worldeck does, in one run: the first five minutes (an animal on the table, held up to its
        /// card, taken home at real size), someone who lives in a world waving back, a thing asked for and held up,
        /// made life size, a real place with its landmark at night in the rain, a place typed on the keyboard, a jump
        /// out to the wonders, and home with the souvenirs on the real table. Words "said" are shown as heard.
        /// </summary>
        IEnumerator Tour()
        {
            var d = Director;
            d.ResetProgress(new Progress { MusicOn = true });   // someone new
            yield return new WaitForSecondsRealtime(4f);

            // 1. An animal on your table, held up close, then its home at real size.
            if (d.Guide != null && d.Guide.Button != null) yield return PokeAt(d.Guide.Button);
            yield return Until(() => PokeButton.Live.Any(b => b != null && b.Id == "Choice first:zebra" && b.isActiveAndEnabled), 10f, "three animals");
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Poke("Choice first:zebra");
            yield return Until(() => d.FirstAnimal != null, 8f, "a zebra on the table");
            yield return new WaitForSecondsRealtime(2.5f);
            var zebra = d.FirstAnimal;
            if (zebra != null)
            {
                yield return PinchAt(Maker.Centre(zebra));
                yield return Lift(d.Head.position + d.Head.forward * 0.3f, 1.2f);
                yield return Until(() => d.Inspect.Looking == zebra, 4f, "its card");
                yield return new WaitForSecondsRealtime(5f);
                yield return LetGo();
            }
            yield return Until(() => d.Inspect.HomeButton != null, 4f, "See where it lives");
            yield return new WaitForSecondsRealtime(1.5f);
            if (d.Inspect.HomeButton != null) yield return PokeAt(d.Inspect.HomeButton);
            yield return Until(() => d.State == Director.Mode.World, 30f, "its home");
            yield return new WaitForSecondsRealtime(9f);
            yield return Until(() => PokeButton.Live.Any(b => b != null && b.Id == "Choice first:stay" && b.isActiveAndEnabled), 15f, "Mara's first call offered");
            yield return new WaitForSecondsRealtime(3f);
            yield return Poke("Choice first:stay");
            yield return new WaitForSecondsRealtime(2f);
            d.Maker?.Clear();   // a recording headset can't step round a life-size zebra: clear the view for the next worlds

            // 2. Someone lives in every world: the pirate cove's captain says hello, and waves back.
            yield return Say("Juniper, a pirate cove");
            yield return Arrive();
            yield return Until(() => d.Local != null && d.Local.Greeted, 10f, "the captain's hello");
            yield return new WaitForSecondsRealtime(2f);
            yield return WaveTo(() => d.Local != null && d.Local.Waves > 0);
            yield return new WaitForSecondsRealtime(4f);

            // 3. Ask for a thing: it forms on your table; held up, its card says how big it is; then life size.
            yield return Say("make me a lion");
            yield return Until(() => d.Maker.Things.Count > 0, 10f, "a lion");
            yield return new WaitForSecondsRealtime(2f);
            var lion = d.Maker.Things.LastOrDefault();
            if (lion != null)
            {
                yield return PinchAt(Maker.Centre(lion));
                yield return Lift(d.Head.position + d.Head.forward * 0.35f, 1.2f);
                yield return Until(() => d.Inspect.Looking == lion, 4f, "the lion's card");
                yield return new WaitForSecondsRealtime(5f);
                yield return LetGo();
                yield return new WaitForSecondsRealtime(2.5f);
            }
            d.Maker?.Clear();   // keep the view clear for India's skyline

            // 4. Any real place, offline: India, its dome on the horizon; then night, then rain.
            yield return Say("take me to India");
            yield return Arrive();
            yield return new WaitForSecondsRealtime(4f);
            yield return Say("make it night");
            yield return new WaitForSecondsRealtime(5f);
            yield return Say("make it rain");
            yield return new WaitForSecondsRealtime(6f);

            // 5. Or type it: a keyboard within reach, suggestions as you type.
            d.OnPanelForTests("type");
            yield return new WaitForSecondsRealtime(1.5f);
            foreach (char ch in "volc") yield return Poke("type key " + ch, 0.35f);
            yield return new WaitForSecondsRealtime(1f);
            yield return Poke("type: a volcano at nightfall");
            yield return Arrive();
            yield return new WaitForSecondsRealtime(5f);

            // 6. The world beyond the table: beams over five wonders; a jump out, from your seat.
            d.OnPanelForTests("explore");
            yield return new WaitForSecondsRealtime(4f);
            for (int j = 0; j < 2; j++) { yield return Jump(); yield return new WaitForSecondsRealtime(4f); }
            d.Move?.Home(true, "demo");
            yield return new WaitForSecondsRealtime(3f);

            // 7. Home: the worlds' souvenirs stand on your real table.
            d.Close("demo");
            yield return Until(() => d.State == Director.Mode.Room, 30f, "home");
            yield return new WaitForSecondsRealtime(10f);
        }

        /// <summary>Into the new world, clear of its task (a tour, not a chore).</summary>
        IEnumerator Arrive()
        {
            var d = Director;
            yield return new WaitForSecondsRealtime(1.5f);
            yield return Until(() => d.State == Director.Mode.World && !d.Busy, 30f, "the world open");
            yield return new WaitForSecondsRealtime(3f);
            d.Gather?.Stop();
            d.Loose?.Stop();
        }

        /// <summary>Words said to Juniper: she listens, the words are shown as heard, and they happen.</summary>
        IEnumerator Say(string words)
        {
            var d = Director;
            d.Guide.SetMood(Guide.Mood.Listening);
            yield return new WaitForSecondsRealtime(1.2f);
            d.Guide.Words.Say("\u201C" + words + "\u201D", 0f, new Color(0.72f, 0.75f, 0.8f));
            yield return new WaitForSecondsRealtime(1.4f);
            Trace.Info("Demo", "said", "words", words);
            d.Handle(words.StartsWith("Juniper, ") ? words.Substring(9) : words, "voice-name");
        }

        IEnumerator PinchAt(Vector3 at)
        {
            var d = Director;
            ghostOn = true; ghostAnchor = GhostHand.Anchor.PinchPoint;
            if (ghost != null) { ghost.Point = 0f; ghost.Curl = 0.3f; ghost.Pinch = 0f; }
            if (d.Hands != null) d.Hands.SimulatedPinch = false;
            yield return Glide(at, 0.9f, true);
            if (ghost != null) ghost.Pinch = 1f;
            if (d.Hands != null) d.Hands.SimulatedPinch = true;
            yield return new WaitForSecondsRealtime(0.4f);
        }

        IEnumerator Lift(Vector3 to, float seconds) { yield return Glide(to, seconds, true); }

        IEnumerator LetGo()
        {
            var d = Director;
            if (d.Hands != null) d.Hands.SimulatedPinch = false;
            if (ghost != null) ghost.Pinch = 0f;
            yield return new WaitForSecondsRealtime(0.4f);
            yield return Glide(Rest(), 0.6f, false);
            Park(); ghostOn = false;
        }

        /// <summary>An open hand, palm out, side to side in front of you, until <paramref name="done"/> (or 3 s).</summary>
        IEnumerator WaveTo(System.Func<bool> done)
        {
            var d = Director;
            var head = d.Head;
            Vector3 f = head.forward; f.y = 0f; f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            Vector3 c = head.position + f * 0.4f + Vector3.down * 0.1f;
            ghostOn = true; ghostAnchor = GhostHand.Anchor.Palm;
            if (ghost != null) { ghost.Point = 0f; ghost.Curl = 0f; ghost.Pinch = 0f; }
            if (d.Hands != null) d.Hands.SimulatedPalmOut = true;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 3f && !done())
            {
                float ph = (Time.realtimeSinceStartup - t0) * 3f;
                float x = (Mathf.PingPong(ph * 2f, 1f) * 2f - 1f) * 0.12f;
                hand.position = ghostAt = c + right * x;
                ghostRot = Quaternion.LookRotation(Vector3.up, -f);
                yield return null;
            }
            if (d.Hands != null) d.Hands.SimulatedPalmOut = false;
            yield return new WaitForSecondsRealtime(1.2f);
            Park(); ghostOn = false;
        }

        /// <summary>Hand out, palm down, then a pinch: a jump to where the arc lands.</summary>
        IEnumerator Jump()
        {
            var d = Director;
            if (d.Move == null) yield break;
            Vector3 f = d.Head.forward; f.y = 0f; f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            Vector3 at = d.Head.position + f * 0.45f + Vector3.down * 0.28f;
            ghostOn = true; ghostAnchor = GhostHand.Anchor.PinchPoint;
            if (ghost != null) { ghost.Point = 0f; ghost.Curl = 0.2f; ghost.Pinch = 0f; }
            if (d.Hands != null) { d.Hands.SimulatedPalmUp = false; d.Hands.SimulatedPinch = false; }
            yield return Glide(at, 0.8f, true);
            ghostRot = Quaternion.LookRotation(f, Vector3.up);
            yield return Until(() => d.Move.Aiming(HandInput.SimulatedIndex), 3f, "aiming");
            yield return new WaitForSecondsRealtime(1.2f);
            if (ghost != null) ghost.Pinch = 1f;
            if (d.Hands != null) d.Hands.SimulatedPinch = true;
            yield return new WaitForSecondsRealtime(0.15f);
            if (d.Hands != null) d.Hands.SimulatedPinch = false;
            if (ghost != null) ghost.Pinch = 0f;
            Park(); ghostOn = false;
        }

        IEnumerator PokeAt(PokeButton b)
        {
            if (b == null) yield break;
            yield return PokeFace(b, 0.6f);
        }

        /// <summary>One chapter: answer, the pace, the storm (blocked, but for one meteor when asked), the goodbye.</summary>
        IEnumerator Chapter(string pace)
        {
            var d = Director;
            var m = d.Mission;
            yield return Until(() => m.Now == Mission.Step.Answer, 60f, "her first choice");
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Poke("Choice m:guide");
            yield return Until(() => m.Now == Mission.Step.Pace, 20f, "the pace");
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Poke("Choice " + pace);
            yield return Until(() => d.Emergency != null && d.Emergency.Running && d.Emergency.Sim != null, 10f, "her storm");
            var em = d.Emergency;
            // The piece that breaks is the one most in view (in a recorded run the break happened off camera).
            int victimPiece = -1, pieces = Mathf.Max(1, em.Defended);
            if (letOneThrough && em.Sim.Strikes.Count > 0)
            {
                float best = float.MaxValue;
                for (int i = 0; i < em.Sim.Strikes.Count; i++)
                {
                    float a = Vector3.Angle(d.Head.forward, em.TargetOf(i) - d.Head.position);
                    if (a < best) { best = a; victimPiece = i % pieces; }
                }
                victim = em.PieceLabel(em.Sim.Strikes[victimPiece]);
                Trace.Info("Demo", "a meteor will get through", "piece", victim, "index", victimPiece, "deg_off_view", best);
            }
            bool through = false;
            ghostOn = true; ghostAnchor = GhostHand.Anchor.Palm;
            if (ghost != null) { ghost.Curl = 0f; ghost.Pinch = 0f; ghost.Point = 0f; }
            Vector3 at = Rest();
            float until = Time.realtimeSinceStartup + 150f;
            while (em.Running && em.Sim.State.Phase < Sim.Aftermath && Time.realtimeSinceStartup < until)
            {
                int next = em.Sim.State.Next;
                bool spare = letOneThrough && !through && next < em.Sim.Strikes.Count && next % pieces == victimPiece;
                if (spare && em.Sim.State.Taken > 0) through = true;
                Vector3 want = spare ? Rest() : em.NextTarget;
                at = Vector3.MoveTowards(at, want, Time.unscaledDeltaTime * 1.6f);
                hand.position = at;
                ghostAt = at; ghostRot = PalmOut(at);
                yield return null;
            }
            yield return Glide(Rest(), 0.6f, true);
            Park(); ghostOn = false;
            // Her ship comes in to an open hand.
            yield return Until(() => m.WantsPalm || m.Now == Mission.Step.Farewell, 15f, "her ship asks for a hand");
            if (m.WantsPalm)
            {
                var head = d.Head;
                Vector3 f = head.forward; f.y = 0f; f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
                Vector3 palm = head.position + f * 0.36f + Vector3.down * 0.22f;
                ghostOn = true; ghostAnchor = GhostHand.Anchor.Palm;
                if (ghost != null) { ghost.Curl = 0.05f; ghost.Pinch = 0f; ghost.Point = 0f; }
                ghostAt = Rest();
                yield return Glide(palm, 0.8f, true);
                ghostRot = Quaternion.LookRotation(f, Vector3.down);
                if (d.Hands != null) d.Hands.SimulatedPalmUp = true;
                float hold = Time.realtimeSinceStartup + 14f;
                while (m.WantsPalm && Time.realtimeSinceStartup < hold) { ghostAt = hand.position = palm; ghostRot = Quaternion.LookRotation(f, Vector3.down); yield return null; }
                if (d.Hands != null) d.Hands.SimulatedPalmUp = false;
                yield return new WaitForSecondsRealtime(0.6f);
                Park(); ghostOn = false;
            }
            yield return Until(() => m.Now == Mission.Step.Farewell, 40f, "her goodbye");
            yield return new WaitForSecondsRealtime(2.5f);
            yield return Wave();
            m.Waved();
            yield return Until(() => m.Now == Mission.Step.Done || !m.Active, 60f, "she signs off");
            yield return new WaitForSecondsRealtime(2f);
        }

        /// <summary>Carries each glowing piece to the table with a closed hand (Juniper brings any left after a minute).</summary>
        IEnumerator GatherByHand()
        {
            var d = Director;
            var g = d.Gather;
            float until = Time.realtimeSinceStartup + 60f;
            var table = d.Rooms.Room.Table;
            ghostOn = true; ghostAnchor = GhostHand.Anchor.Palm;
            if (ghost != null) { ghost.Point = 0f; ghost.Pinch = 0f; ghost.Curl = 0.2f; }
            hand.position = ghostAt = Rest();
            while (g.Running && Time.realtimeSinceStartup < until)
            {
                var it = g.Items.Where(i => !i.Placed && i.HeldBy < 0 && i.Appear >= 0.6f)
                                .OrderBy(i => Vector3.Distance(i.transform.position, d.Head.position)).FirstOrDefault();
                if (it == null) { yield return null; continue; }
                if (d.Hands != null) d.Hands.SimulatedFist = false;
                yield return Glide(it.transform.position + Vector3.up * 0.01f, 0.8f, true);
                if (d.Hands != null) d.Hands.SimulatedFist = true;
                if (ghost != null) ghost.Curl = 1f;
                yield return new WaitForSecondsRealtime(0.3f);
                if (!g.Holding(HandInput.SimulatedIndex))
                {
                    if (d.Hands != null) d.Hands.SimulatedFist = false;
                    if (ghost != null) ghost.Curl = 0.2f;
                    yield return new WaitForSecondsRealtime(0.3f);
                    continue;
                }
                yield return Glide(table.TopCenter + Vector3.up * 0.12f, 1f, true);
                if (d.Hands != null) d.Hands.SimulatedFist = false;
                if (ghost != null) ghost.Curl = 0.2f;
                yield return new WaitForSecondsRealtime(0.5f);
                yield return Glide(Rest(), 0.5f, true);
            }
            if (d.Hands != null) d.Hands.SimulatedFist = false;
            Park(); ghostOn = false;
            if (g.Running) { Trace.Warn("Demo", "pieces left: Juniper brings them"); g.AutoPlace(); }
        }

        /// <summary>A fingertip presses the button (the press itself is the button's own, as for a poke).</summary>
        IEnumerator Poke(string id, float reach = 0.6f)
        {
            var b = PokeButton.Live.FirstOrDefault(x => x != null && x.Id == id && x.isActiveAndEnabled);
            if (b == null) { Trace.Warn("Demo", "no button", "id", id); yield break; }
            yield return PokeFace(b, reach);
        }

        IEnumerator PokeFace(PokeButton b, float reach)
        {
            var head = Director.Head.position;
            Vector3 face = b.transform.position, toward = (face - head).normalized;
            ghostOn = true; ghostAnchor = GhostHand.Anchor.IndexTip;
            if (ghost != null) { ghost.Point = 1f; ghost.Curl = 0.6f; ghost.Pinch = 0f; }
            ghostRot = Quaternion.LookRotation(toward, Vector3.up);
            ghostAt = face - toward * 0.25f + Vector3.down * 0.05f;
            yield return GlideGhost(face - toward * 0.06f, reach);
            yield return GlideGhost(face, 0.15f);
            if (b != null && b.isActiveAndEnabled) b.Press();
            yield return GlideGhost(face - toward * 0.12f, 0.25f);
            ghostOn = false;
            if (ghost != null) { ghost.Point = 0f; ghost.Curl = 0.2f; }
        }

        IEnumerator PokeDock()
        {
            var b = PokeButton.Live.FirstOrDefault(x => x != null && x.Id == "dock do" && x.isActiveAndEnabled);
            if (b != null) yield return Poke("dock do");
            else Director.OnDockForTests("do");
        }

        /// <summary>An open hand, palm out, side to side in front of you.</summary>
        IEnumerator Wave()
        {
            var head = Director.Head;
            Vector3 f = head.forward; f.y = 0f; f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            Vector3 c = head.position + f * 0.42f + Vector3.down * 0.05f + right * 0.12f;
            ghostOn = true; ghostAnchor = GhostHand.Anchor.Palm;
            if (ghost != null) { ghost.Point = 0f; ghost.Curl = 0f; ghost.Pinch = 0f; }
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 1.8f)
            {
                float s = Mathf.Sin((Time.realtimeSinceStartup - t0) * Mathf.PI * 3f);
                ghostAt = c + right * (0.09f * s);
                ghostRot = Quaternion.LookRotation(Vector3.up, -f) * Quaternion.Euler(0f, 0f, 0f) * Quaternion.AngleAxis(-15f * s, f);
                yield return null;
            }
            ghostOn = false;
        }

        /// <summary>Moves the seen hand (and, if <paramref name="input"/>, the hand the app reads) over a time.</summary>
        IEnumerator Glide(Vector3 to, float seconds, bool input)
        {
            Vector3 from = ghostAt;
            float t0 = Time.realtimeSinceStartup;
            while (true)
            {
                float k = Mathf.Clamp01((Time.realtimeSinceStartup - t0) / Mathf.Max(0.01f, seconds));
                float e = k * k * (3f - 2f * k);
                ghostAt = Vector3.Lerp(from, to, e);
                ghostRot = Reach(ghostAt);
                if (input) hand.position = ghostAt;
                if (k >= 1f) break;
                yield return null;
            }
        }

        IEnumerator GlideGhost(Vector3 to, float seconds)
        {
            Vector3 from = ghostAt;
            float t0 = Time.realtimeSinceStartup;
            while (true)
            {
                float k = Mathf.Clamp01((Time.realtimeSinceStartup - t0) / Mathf.Max(0.01f, seconds));
                ghostAt = Vector3.Lerp(from, to, k * k * (3f - 2f * k));
                if (k >= 1f) break;
                yield return null;
            }
        }

        void LateUpdate()
        {
            if (ghost == null) return;
            if (ghost.gameObject.activeSelf != ghostOn) ghost.gameObject.SetActive(ghostOn);
            if (ghostOn) ghost.PlaceBy(ghostAt, ghostRot, ghostAnchor);
            // One hand on show: the coach's own demonstrations wait while the demo hand plays.
            if (Running && Director != null && Director.Coach != null && Director.Coach.Playing != HandCoach.Demo.None) Director.Coach.Stop("demo");
        }

        /// <summary>Palm facing away from you, fingers up: the blocking hand.</summary>
        Quaternion PalmOut(Vector3 at)
        {
            Vector3 d = at - Director.Head.position; d.y = 0f;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
            return Quaternion.LookRotation(Vector3.up, -d);
        }

        /// <summary>Reaching out towards a point, palm down.</summary>
        Quaternion Reach(Vector3 at)
        {
            Vector3 d = at - Director.Head.position;
            d = d.sqrMagnitude > 1e-4f ? d.normalized : Vector3.forward;
            return Quaternion.LookRotation(d, Vector3.up);
        }

        /// <summary>Where the hand rests between moves: low, in front, a little to the right.</summary>
        Vector3 Rest()
        {
            var head = Director.Head;
            Vector3 f = head.forward; f.y = 0f; f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            return head.position + f * 0.28f + Vector3.down * 0.3f + Vector3.Cross(Vector3.up, f) * 0.14f;
        }

        void Park() { if (hand != null && Director != null && Director.Head != null) hand.position = Director.Head.position + Vector3.up * 3f; }

        static IEnumerator Until(System.Func<bool> ok, float seconds, string what)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (!ok() && Time.realtimeSinceStartup < end) yield return null;
            if (!ok()) Trace.Warn("Demo", "waited in vain", "for", what);
            else Trace.Info("Demo", "step", "now", what);
        }
    }
}
