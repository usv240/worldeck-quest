using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Worldeck.Core;
using UnityEngine;

namespace Worldeck
{
    /// <summary>
    /// The whole experience, start to end. In your room (passthrough) a small light waits above your table.
    /// Touch it, say or pick a world, and the room opens into it: a spark hits the wall in front of you, the
    /// walls dissolve outward, your furniture turns into the world's version of itself, and the table becomes
    /// where things are built. Gather the pieces that appear around you and bring them to the table to finish
    /// the world's build; change the world as you go; go home and your room comes back with a
    /// souvenir on the table. Everything voice does, the palm panel does too.
    /// </summary>
    public sealed class Director : MonoBehaviour
    {
        public enum Mode { Room, Opening, World, Changing, Closing }

        [Header("Parts")]
        public Transform Head;
        public RoomSource Rooms;
        public WorldBuilder Builder;
        public WorldLook Look;
        public RoomShell Shell;
        public RoomSkins Skins;
        public BuildSite Site;
        public Gather Gather;
        public HandInput Hands;
        public Pokes Pokes;
        public HandPanel Panel;
        public Guide Guide;
        public MicCapture Mic;
        public Souvenirs Shelf;
        public Watch Watch;
        public PlayLog Log;
        public Renderer FadeQuad;
        public Material GlowFlat, Glow;
        public OVRPassthroughLayer Passthrough;
        public TouchRipples Ripples;
        public Campfire Campfire;
        public PassthroughLight Light;
        public FrozenTouch Freeze;
        public RoomVerbs Verbs;
        public WorldMap Map;
        public HelpCard Help;
        public HandCoach Coach;
        public Hints Hints;
        public WristMenu Wrist;
        public Loose Loose;
        /// <summary>"Say anything": the things you ask for.</summary>
        public Maker Maker;
        public Inspect Inspect;
        public FurnitureMask Furniture;
        /// <summary>The Last Signal: Mara and her story around the storm.</summary>
        public Mission Mission;
        public TypeBox Type;
        public ControllerLabels Pads;
        public Ears Ears;
        public Gifts Gifts;
        public Dock Dock;
        public Emergency Emergency;
        public HandLook HandLook;
        public Locomotion Move;
        public Wonders Wonders;
        public Finale Finale;

        [Header("Feel")]
        public float RevealSeconds = 1.9f;
        public float CloseSeconds = 1.6f;
        public int FirstTaskItems = 3;
        public int TaskItems = 5;

        public Mode State { get; private set; } = Mode.Room;
        public Recipe Current { get; private set; }
        public Progress Progress { get; private set; } = new Progress();
        public bool Frozen { get; private set; }
        public bool Busy => State == Mode.Opening || State == Mode.Changing || State == Mode.Closing;
        // A hand holds a piece, a thing, the stick, or put one down less than a second ago: no menu opens under it.
        float carriedAt = -10f;
        bool CarryingNow => Time.unscaledTime - carriedAt < 1f;
        /// <summary>Meteors are coming (or just stopped): open palms block, so the palm menu stays shut.</summary>
        public bool StormOn => Emergency != null && Emergency.Running && Emergency.Sim != null && Emergency.Sim.State.Phase < Sim.Aftermath;
        public float LastWordsToWorldMs { get; private set; }
        public string LastHow { get; private set; } = "";
        public event Action<Mode> Changed;

        readonly List<Recipe> undo = new List<Recipe>();
        bool taskDoneThisVisit;
        Progress.Souvenir pendingSouvenir;
        float askedAt = -1f;
        bool listening;
        LineRenderer tableTrace;
        GameObject spark;
        float fadeKeep = 1f, fadeTarget = 1f;
        MaterialPropertyBlock fadeBlock;
        string progressPath;
        bool firstWorld;
        // "Rest an open hand near it": said once a session, with the first small animal after the first thing.
        bool taughtCome;
        Coroutine running;
        float lastStatusLine = -10f;
        static readonly int RoomGlowId = Shader.PropertyToID("_SwRoomGlow");
        float lastTaskMove;
        Interview interview;

        // ---------- lifecycle ----------

        void Start()
        {
            AppConfig.Load();
            Trace.State = () => State.ToString();
            Trace.Info("App", "start", "version", Application.version, "build", Application.buildGUID, "unity", Application.unityVersion, "device", SystemInfo.deviceModel,
                       "gpu", SystemInfo.graphicsDeviceName, "voice", AppConfig.HasVoice, "ai", AppConfig.HasAi, "kit", Kit.Version,
                       "hz", OVRManager.display != null ? OVRManager.display.displayFrequency : 0f);
            progressPath = Path.Combine(Application.persistentDataPath, "worldeck-progress.json");
            LoadProgress();
            Progress.Launches++;
            SaveProgress();
            foreach (var a in Records.Earned(Progress)) MetaPlatform.Achieve(a);
            if (Progress.BestBlocked > 0) MetaPlatform.Post(MetaPlatform.MostBlocked, Progress.BestBlocked);
            Log?.Add("settings", new Dictionary<string, object>
            {
                { "big_text", Progress.CaptionsLarge }, { "gentle_storm", Progress.GentleStorm }, { "calm", Progress.CalmEffects },
                { "look_to_press", Progress.LookToPress }, { "steps", Progress.StepMove }, { "sound", Progress.SoundLevel }, { "muted", Progress.Muted },
                { "call_when_ready", Progress.CallWhenReady }, { "voice", Progress.VoiceOn }
            });
            Trace.Info("App", "progress", "launch", Progress.Launches, "onboarded", Progress.Onboarded, "souvenirs", Progress.Souvenirs.Count,
                       "programs", Progress.Programs.Count, "daily_streak", Progress.DailyStreak, "last", Progress.Last != null ? Progress.Last.Line() : "-");

            Look.ShowRoom(true);
            if (Rooms != null) Rooms.Changed += OnRoom;
            if (Panel != null) Panel.Command += OnPanel;
            if (Guide != null)
            {
                Guide.Make();
                Guide.Tapped += OnGuideTapped;
                Guide.Chose += OnChoice;
            }
            if (Type != null) Type.Submitted += (text, how) => Handle(text, how);
            // Rewind is for a storm that went wrong: a lost signal or a rough landing, never after a clean one.
            if (Emergency != null) Emergency.RewindAllowed = () => Mission == null || !Mission.Active || Mission.Now == Mission.Step.Storm ||
                                                                   (Mission.Now == Mission.Step.Ending && Mission.Result != Landing.Clean && !Mission.WantsPalm);
            if (Shelf != null) Shelf.Chosen += s => { if (State == Mode.Room) Open(Recipe.For(s.Biome, NewSeed()), "souvenir"); };
            if (Shelf != null) Shelf.CodeChosen += c =>
            {
                if (State != Mode.Room) return;
                var r = ShareCode.Decode(c.Text);
                if (r == null) return;
                c.Visits++;
                r.Title = "From a friend: " + r.Def.Name;
                Trace.Info("Director", "friend's code opened", "code", c.Text, "world", r.Line());
                Guide.Say("A friend's world, in your room.", 3f);
                Open(r, "code");
            };
            if (Gather != null)
            {
                Gather.Completed += OnTaskDone;
                Gather.Grabbed += _ => { lastTaskMove = Time.time; if (coachFor == "gather") StopCoach("grabbed"); if (firstWorld) Guide.Say("Now put it on the glowing ring\non your table.", 4f); };
                Gather.Placed += _ => lastTaskMove = Time.time;
            }
            if (Campfire != null) Campfire.Ate += OnAte;
            if (Map != null) Map.Chosen += OnMap;
            if (Help != null) Help.Closed += () => { Guide.SetMood(Guide.Mood.Idle); if (State == Mode.Room) ShowMap(""); };
            if (Guide != null) Guide.Waved += () => { if (Mission != null && Mission.Active) { Mission.Waved(); return; } if (WaveToLocal()) return; if ((State == Mode.World || State == Mode.Room) && Guide.State != Guide.Mood.Hidden) Guide.Say("Hi!", 2f); };
            if (Mic != null)
            {
                Mic.Failed += why => { listening = false; Guide.SetMood(Guide.Mood.Idle); Guide.Say("I couldn't hear that (" + why + ").", 4f); ShowChoices("Where to?"); };
            }
            if (Furniture != null) { Furniture.Room = () => Rooms != null ? Rooms.Room : null; Furniture.Active = () => State == Mode.Room; }
            if (Pokes != null) Pokes.Busy = h => (Move != null && Move.Aiming(h)) || (Gather != null && (Gather.Holding(h) || Gather.Near(h))) ||
                                                 (Loose != null && (Loose.Holding(h) || Loose.Near(h))) ||
                                                 (Campfire != null && Campfire.HeldBy(h));
            if (Loose != null) Loose.Blocked = h => (Gather != null && (Gather.Holding(h) || Gather.Near(h))) ||
                                                    (Campfire != null && (Campfire.HeldBy(h) || Campfire.NearStick(Hands[h].Pinch)));
            if (Maker != null)
            {
                // A pinch goes to whatever is nearest the hand: a made thing beside the kindling is still yours to take
                // (Nov 13: the right hand, busy with the kindling, could not pick up what it had made).
                Maker.Blocked = h => (Gather != null && (Gather.Holding(h) || (Gather.Near(h) && Gather.Nearest(h) < Maker.Nearest(h)))) ||
                                     (Loose != null && (Loose.Holding(h) || (Loose.Near(h) && Loose.Nearest(h) < Maker.Nearest(h)))) ||
                                     (Campfire != null && Campfire.HeldBy(h));
                if (Gather != null) Gather.Blocked = h => Maker.Holding(h) || Maker.Nearest(h) <= Gather.Nearest(h);
                var looseBlocked = Loose != null ? Loose.Blocked : null;
                if (Loose != null) Loose.Blocked = h => Maker.Holding(h) || Maker.Nearest(h) <= Loose.Nearest(h) || (looseBlocked != null && looseBlocked(h));
                // Seen on the headset: a pinch 15 cm short of a seed pulled the astronaut over instead, and the seed was never had.
                Maker.FarBlocked = h => Gather != null && Gather.Running && Gather.Nearest(h) < 0.5f;   // the pieces' own reach for "come to my hand"
                Maker.InWorld = () => State == Mode.World || State == Mode.Changing;
                if (Inspect != null) Inspect.GoHome += (t, place) => GoWhereItLives(t, place, "button");
                if (Inspect != null) Inspect.Asked += (t, q) => AskAbout(q, t, "card");
                var pokesBusy = Pokes != null ? Pokes.Busy : null;
                if (Pokes != null) Pokes.Busy = h => Maker.Holding(h) || Maker.Near(h) || (pokesBusy != null && pokesBusy(h));
            }
            if (Wrist != null)
            {
                Wrist.Busy = h => (Gather != null && Gather.Holding(h)) || (Loose != null && Loose.Holding(h)) || (Campfire != null && Campfire.HeldBy(h));
                Wrist.Pressed += OnWristMenu;
            }
            if (Help != null) Help.ShowMe += ShowMe;
            if (Inspect != null) Inspect.HandsFree = () => (Hands != null && !Hands.AnyTracked) || Progress.LookToPress;
            if (Help != null) Help.TableTop = () => Rooms != null && Rooms.Room != null && Rooms.Room.Table != null ? Rooms.Room.Table.TopY : (float?)null;
            if (Pokes != null) Pokes.Allowed = b => (Help == null || !Help.Showing || b.Id.StartsWith("help ", StringComparison.Ordinal)) &&
                                                   // Juniper isn't pressed by a glance before the welcome (hands not yet seen at launch).
                                                   !(b.Id == "Guide" && !Progress.WelcomeSeen && Pokes.EyesOnly);
            if (Coach != null) Coach.OverCards = () => Help != null && Help.Showing && State != Mode.Room;
            if (Gifts != null) Gifts.Used += (name, phrase) => { Guide.SetMood(Guide.Mood.Speaking); Handle(phrase, "gift"); Log?.Add("gift", new Dictionary<string, object> { { "gift", name } }); };
            if (Dock != null) Dock.Pressed += OnDock;
            if (Mission != null)
            {
                Mission.Launch += pace => StartStorm("mission", pace);
                Mission.Finished += OnMissionDone;
                Mission.Postponed += ch =>
                {
                    stormThisWorld = false;
                    callWaiting = true;
                    Log?.Add("mission_later", new Dictionary<string, object> { { "chapter", ch } });
                    Guide.SetMood(Guide.Mood.Speaking);
                    Guide.Say("She'll wait. The world is yours:\nthe button on your table answers her.", 6f);
                    StartCoroutine(InviteOut(visit));
                };
                if (Mission.Emergency == null) Mission.Emergency = Emergency;   // the storm is her approach: she must hear it
                Mission.LostHer += ch => { if (sessionMission == Landing.None) sessionMission = Landing.Lost; sessionChapter = ch; Log?.Add("mission_lost", new Dictionary<string, object> { { "chapter", ch } }); };
            }
            if (Emergency != null)
            {
                Emergency.Phase += OnStormPhase;
                Emergency.Struck += OnStormStrike;
                Emergency.Broke += s => { Guide.SetMood(Guide.Mood.Speaking); Trace.Info("Emergency", "station hit", "station", s); };
                Emergency.Repaired += s => Guide.Say("Fixed: the " + Sim.StationName(s) + ".", 2.5f);
                Emergency.Ended += OnStormEnded;
                Emergency.Rewound += () => { saidHit.Clear(); if (Mission != null && Mission.Active) return; Guide.SetMood(Guide.Mood.Speaking); Guide.Say("Back in time. What will you do differently?", 5f); };
            }
            if (Move != null)
            {
                // A pinch means one thing: on (or pointing at) something you made, it is that thing, not a jump.
                Move.Busy = h => (Gather != null && (Gather.Holding(h) || Gather.Near(h))) || (Loose != null && (Loose.Holding(h) || Loose.Near(h))) ||
                                 (Campfire != null && (Campfire.HeldBy(h) || (Campfire.Running && Vector3.Distance(Hands[h].Palm, Campfire.FirePos) < 0.45f))) ||
                                 (Maker != null && (Maker.Holding(h) || Maker.Near(h) || Maker.Pointed(h) != null || Maker.CloseTo(h, 0.3f) || Maker.Offering(h) || Time.unscaledTime - Maker.LastHandled < 1.5f));
                Move.Moved += how => { if (coachFor == "jump" && how != "turn") StopCoach("moved"); };
                Move.CameHome += () => { if (State == Mode.World) Guide.Say("Back at camp.", 2f); };
            }
            if (Wonders != null) { Wonders.Remember(Progress.Wonders); Wonders.Discovered += OnWonder; }
            if (Ears != null && Ears.Supported && UnityEngine.Debug.isDebugBuild && !Progress.VoiceOn) Ears.Warm();
            if (Ears != null)
            {
                AppConfig.OnDevice = Ears.Supported;
                Ears.HeardPhrase += OnHeard;
                Ears.ClearCommand = IsClearCommand;
                Ears.Woke += OnWoke;
                Ears.Changed += s => { if (s == Ears.Status.Failed) Health.Set(Health.Voice, Health.Level.Fallback, Ears.Error, Time.realtimeSinceStartup, "Voice is off. Touch to choose instead."); };
                // Voice the player turned on stays on across launches (if the permission is still there).
                if (Ears.Supported && Progress.VoiceOn && MicCapture.HasPermission) Ears.TurnOn();
            }
            if (Guide != null && Guide.Button != null) Guide.Button.Hint = "Touch me for the map";
            if (Watch != null) Watch.Lighten = () =>
            {
                Log?.Add("lighten", null);
                float now = UnityEngine.XR.XRSettings.eyeTextureResolutionScale;
                if (now > 1.01f) UnityEngine.XR.XRSettings.eyeTextureResolutionScale = Mathf.Max(1f, now - 0.125f);
                Trace.Warn("Perf", "frame rate low for 2.5 s", "resolution_scale_now", UnityEngine.XR.XRSettings.eyeTextureResolutionScale);
            };
            if (Skins != null) Skins.Popped = s => Sfx.Instance?.At("touch", s.Piece.TopCenter, 0.6f, UnityEngine.Random.Range(0.85f, 1.15f));
            OVRManager.HMDUnmounted += OnUnmounted;
            OVRManager.HMDMounted += OnMounted;
            // The Quest menu opening (input focus lost): freeze the world and stop listening (VRC.Quest.Input.4).
            OVRManager.InputFocusLost += OnFocusLost;
            OVRManager.InputFocusAcquired += OnFocusAcquired;
            if (OVRManager.display != null) { try { OVRManager.display.displayFrequency = 72f; } catch { } }
            try
            {
                UnityEngine.XR.XRSettings.eyeTextureResolutionScale = 1.0f;
                if (OVRManager.instance != null) OVRManager.instance.sharpenType = OVRPlugin.LayerSharpenType.Quality;
                Trace.Info("App", "picture", "resolution_scale", UnityEngine.XR.XRSettings.eyeTextureResolutionScale, "sharpen", "quality");
            }
            catch (Exception e) { Trace.Warn("App", "picture settings failed", "why", e.Message); }
            OVRManager.foveatedRenderingLevel = OVRManager.FoveatedRenderingLevel.High;
            OVRManager.useDynamicFoveatedRendering = true;

            MakeTrace();
            StartCoroutine(Prewarm());
            if (!Progress.Onboarded) StartCoroutine(FirstRun());
            if (Rooms != null && Rooms.Room != null) OnRoom(Rooms.Room);
            RoomGreeting();
        }

        void OnDestroy()
        {
            OVRManager.HMDUnmounted -= OnUnmounted;
            OVRManager.HMDMounted -= OnMounted;
            OVRManager.InputFocusLost -= OnFocusLost;
            OVRManager.InputFocusAcquired -= OnFocusAcquired;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) { SaveProgress(); Log?.Flush(); if (Mic != null) Mic.Cancel(); listening = false; }
        }

        void OnFocusLost()
        {
            if (Mic != null) Mic.Cancel();
            listening = false;
            if (Ears != null) { Ears.Muted = true; Ears.StopListening(); }
            // Quest's own keyboard takes input focus while you type: that is not leaving the app, so nothing freezes.
            if (Type != null && Type.Open) { Trace.Info("Type", "keyboard has input focus: world keeps running"); return; }
            if (State == Mode.World && !Frozen) SetFrozen(true, "system menu");
            if (Panel != null) Panel.Hide();
        }

        void OnFocusAcquired()
        {
            if (Frozen && frozenBy == "system menu") SetFrozen(false, "");
            if (Ears != null) Ears.Muted = false;
            roomIdleSince = Time.unscaledTime;
            if (State == Mode.Room) StartCoroutine(RefitRoomSoon("focus"));
        }

        /// <summary>
        /// After the headset is put on, Quest recentres or a Quest flow returns, the head is somewhere new: the
        /// room (if it's the virtual one) and Juniper are laid out again around where the head really is.
        /// </summary>
        IEnumerator RefitRoomSoon(string why)
        {
            yield return new WaitForSecondsRealtime(0.6f);
            if (State != Mode.Room || Rooms == null || Rooms.Room == null) yield break;
            if (!Rooms.Room.Scanned) { Rooms.LookSoon(); lookAgainUntil = Time.unscaledTime + 30f; }
            Rooms.Recentre();
            PlaceGuide(Rooms.Room);
            Trace.Info("Guide", "placed again", "why", why, "room", Rooms.Room.Source);
        }

        float roomIdleSince, guideOffSince = -1f, noHandsSince = -1f, lookAgainUntil = -1f;
        bool idleChoicesShown, saidNoHands;

        /// <summary>
        /// In your room, before a world: Juniper must be where you can see and reach it. If it has been out of view
        /// or out of reach for 2 s it glides back in front of you. After 9 s with nothing touched, its choices
        /// appear under it. With no hands seen for 6 s it says so, and how to choose with your eyes.
        /// </summary>
        void RoomWatch()
        {
            if (State != Mode.Room || Guide == null || Head == null || Busy) { guideOffSince = -1f; return; }
            Vector3 to = Guide.transform.position - Head.position;
            float dist = to.magnitude, angle = Vector3.Angle(Head.forward, to);
            bool bad = dist > 1.0f || dist < 0.25f || angle > 50f || to.y < -0.75f || to.y > 0.1f;
            if ((Map != null && Map.Showing) || (Help != null && Help.Showing)) bad = false;   // it waits above them on purpose
            if (bad) { if (guideOffSince < 0f) guideOffSince = Time.unscaledTime; } else guideOffSince = -1f;
            if (guideOffSince > 0f && Time.unscaledTime - guideOffSince > 2f)
            {
                guideOffSince = -1f;
                Vector3 f = Flat(Head.forward);
                Vector3 p = Head.position + f * 0.5f + Vector3.down * 0.22f;
                if (Rooms != null && Rooms.Room != null && Rooms.Room.Scanned && Rooms.Room.Table != null && !Rooms.Room.Table.Virtual)
                {
                    // A real table in front stays the anchor; otherwise float in front of you.
                    Vector3 t = Rooms.Room.Table.TopCenter + Vector3.up * 0.26f;
                    if (Vector3.Distance(t, Head.position) < 1.0f && Vector3.Angle(Head.forward, t - Head.position) < 50f) p = t;
                }
                // Within reach here too (PlaceGuide's rule): over a real table she went back to its centre, up to a metre off.
                Vector3 back = p - Head.position; back.y = 0f;
                if (back.magnitude > Guide.MaxReach) p -= back.normalized * (back.magnitude - Guide.MaxReach);
                Guide.Place(p);
                Trace.Info("Guide", "brought back into view", "dist_was_m", dist, "angle_was", angle, "to", p);
            }
            var hs = Hands;
            bool anyHands = hs != null && hs.AnyTracked;
            if (anyHands) { noHandsSince = -1f; saidNoHands = false; }
            else if (noHandsSince < 0f) noHandsSince = Time.unscaledTime;
            if (!anyHands && !saidNoHands && !storyEnding && noHandsSince > 0f && Time.unscaledTime - noHandsSince > 6f && Application.isFocused)
            {
                saidNoHands = true;
                Trace.Warn("Hands", "no hands seen for 6 s");
                Guide.Say("I can't see your hands.\nHold them up, or just look at me for a moment.", 0f);
            }
            if (!idleChoicesShown && !inFirstRun && !storyEnding && firstAnimal == null && Time.unscaledTime - roomIdleSince > 9f && !Guide.HasChoices && Application.isFocused &&
                (Map == null || !Map.Showing) && (Help == null || !Help.Showing))
            {
                idleChoicesShown = true;
                Trace.Info("Guide", "idle: map offered");
                ShowMap("");
            }
        }

        // The proximity sensor flickers while a headset is adjusted (Nov 11 run: "off" every few seconds), so the
        // world only freezes once it has been off the face for a moment.
        Coroutine unmountWait;
        void OnUnmounted()
        {
            Mic?.Cancel(); listening = false;
            if (Ears != null) { Ears.Muted = true; Ears.StopListening(); }
            if (unmountWait == null && isActiveAndEnabled) unmountWait = StartCoroutine(UnmountSoon());
        }
        IEnumerator UnmountSoon()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            unmountWait = null;
            if (OVRManager.instance != null && !OVRManager.isHmdPresent && State == Mode.World && !Frozen) SetFrozen(true, "headset off");
        }
        void OnMounted()
        {
            roomIdleSince = Time.unscaledTime;
            if (State == Mode.Room) StartCoroutine(RefitRoomSoon("mounted"));
            if (unmountWait != null) { StopCoroutine(unmountWait); unmountWait = null; }
            if (Frozen && frozenBy == "headset off") SetFrozen(false, "");
            if (Ears != null) Ears.Muted = false;
            Watch?.Rebase();
        }

        void OnRoom(RoomModel room)
        {
            if (State != Mode.Room || room == null) return;
            PlaceGuide(room);
            if (Shelf != null) { Shelf.Show(Progress, room); SaveProgress(); }
            Shell.SetOrigin(RevealOrigin(room));
            Shell.Build(room);
            Shell.Radius = -1f;
            if (!room.Scanned && !invitedScan && Rooms != null && !Rooms.Loading && Progress.Onboarded)
            {
                invitedScan = true;
                StartCoroutine(InviteScanSoon());
            }
            Log?.Add("room", new Dictionary<string, object>
            {
                { "source", room.Source }, { "walls", room.Walls.Count }, { "pieces", room.Pieces.Count },
                { "table", room.Table != null && !room.Table.Virtual }, { "seat", room.Seat != null ? room.Seat.Label : "" }
            });
        }

        void PlaceGuide(RoomModel room)
        {
            if (Guide == null || room.Table == null) return;
            roomIdleSince = Time.unscaledTime;
            idleChoicesShown = false;
            var t = room.Table;
            Vector3 toHead = room.Head - t.TopCenter; toHead.y = 0f;
            Vector3 back = toHead.sqrMagnitude > 1e-4f ? -toHead.normalized : room.Forward;
            float depth = Mathf.Min(t.Size.x, t.Size.z) * 0.25f;
            Vector3 p = t.TopCenter + back * depth + Vector3.up * 0.26f;
            // Keep it in a comfortable view: never above the eyes, never right in the face.
            p.y = Mathf.Min(p.y, room.Head.y - 0.12f);
            if (Vector3.Distance(p, room.Head) < 0.38f) p = room.Head + (p - room.Head).normalized * 0.38f;
            // Juniper is a button too: in your room, never further than a seated arm reaches (she sat 0.72 m off over a
            // virtual table, 0.93 m over a real one). In worlds she keeps her place above the table, clear of the pieces.
            Vector3 reach = p - room.Head; reach.y = 0f;
            if (reach.magnitude > Guide.MaxReach) p -= reach.normalized * (reach.magnitude - Guide.MaxReach);
            Guide.Place(p);
            Guide.SetMood(Guide.Mood.Idle);
            Trace.Info("Guide", "placed", "at", p, "head_dist_m", Vector3.Distance(p, room.Head), "table", room.Table.Virtual ? "virtual" : room.Table.Label);
        }

        /// <summary>
        /// Draws every material once, tiny, while you are still in your room, so the GPU has every shader
        /// ready before the first reveal (first-run shader stutters are a known retention killer on Quest).
        /// </summary>
        IEnumerator Prewarm()
        {
            yield return null;
            if (Head == null || Builder == null) yield break;
            var root = new GameObject("Prewarm").transform;
            root.SetParent(Head, false);
            root.localPosition = new Vector3(0f, 0f, 0.6f);
            root.localScale = Vector3.one * 0.0005f;
            var mb = new MeshBuilder();
            mb.Box(Vector3.zero, Vector3.one, Color.gray);
            var mesh = mb.ToMesh("prewarm");
            foreach (var m in new[] { Builder.World, Builder.Silhouette, Builder.Sky })
                if (m != null) Util.MeshObject("Warm " + m.name, root, mesh, m);
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Util.Kill(sphere.GetComponent<Collider>());
            sphere.transform.SetParent(root, false);
            sphere.GetComponent<Renderer>().sharedMaterial = Builder.Sky;
            foreach (var m in new[] { Builder.Glow, Builder.GlowFlat })
                if (m != null)
                {
                    Effects.Halo("Warm halo", root, m, 1f, Color.black);
                    var ps = Effects.Burst(root, m, Color.black);
                    ps.Play();
                }
            for (int i = 0; i < 12; i++) yield return null;
            Util.Kill(root.gameObject);
        }

        static readonly Dictionary<string, string> FurnitureWords = new Dictionary<string, string>
        {
            { "COUCH", "sofa" }, { "BED", "bed" }, { "STORAGE", "shelf" }, { "SCREEN", "screen" }, { "LAMP", "lamp" },
            { "PLANT", "plant" }, { "DOOR_FRAME", "door" }, { "OTHER", "box" }, { "TABLE", "table" }
        };

        /// <summary>"Your sofa is a log bench now.": the guide names one of your own pieces of furniture.</summary>
        string RoomLine()
        {
            if (Skins == null || Rooms == null || Rooms.Room == null) return "";
            RoomSkins.Skin best = null;
            float bestD = float.MaxValue;
            foreach (var sk in Skins.All)
            {
                if (sk.Piece.Virtual || sk.Piece == Rooms.Room.Table || !FurnitureWords.ContainsKey(sk.Piece.Label)) continue;
                float d = Vector3.Distance(sk.Piece.Center, Head.position);
                if (sk.Piece.Role == Role.Seat) d -= 1f;   // a sofa first: it is the one people sit on
                if (d < bestD) { bestD = d; best = sk; }
            }
            if (best == null) return "";
            return "Your " + FurnitureWords[best.Piece.Label] + " is a " + best.Id.Replace('_', ' ') + " now.";
        }

        /// <summary>A world that suits the hour on your headset (a proactive pick for the choices).</summary>
        static string SuitsNow()
        {
            int h = DateTime.Now.Hour;
            if (h >= 21 || h < 5) return "campfire";
            if (h < 9) return "desert";
            if (h < 12) return "meadow";
            if (h < 19) return "beach";
            return "city";
        }

        bool greetedOnce;

        void RoomGreeting()
        {
            bool first = !greetedOnce;
            try { Greet(); } finally { greetedOnce = true; }
            if (first) Trace.Info("App", "greeting", "chapter", Progress.MissionChapter);
        }

        void Greet()
        {

            if (Guide == null) return;
            if (!Progress.Onboarded)
                Guide.Say("Stay seated. Touch me, or look at me,\nto see the worlds.", 0f);
            else if (!greetedOnce && Progress.MissionChapter > 0 && Progress.MissionChapter < Story.Chapters)
            {
                // Back for another session, with her story unfinished: she has been trying to reach you.
                Guide.Say(Story.WhileAway(Progress.MissionChapter) + "\nTouch me, or look at me, to pick a world.", 0f);
                Guide.SetMood(Guide.Mood.Speaking);
            }
            else if (Progress.MissionChapter >= Story.Chapters)
            {
                // Her story is told: she flies every day, over today's world. A reason to come back tomorrow.
                var today = Recipe.Daily(DateTime.UtcNow);
                bool flown = Progress.FlownOn(DateTime.UtcNow);
                Guide.SetMood(Guide.Mood.Speaking);
                Guide.Say(Story.TodaysFlight(today.Def.Name.ToLowerInvariant(), Progress.FlightStreakOn(DateTime.UtcNow), flown, Story.Cargo(DateTime.UtcNow)), 0f);
                if (!flown) Guide.ShowChoices(new[] { "Light her way", "Not now" }, new[] { "flight:today", "flight:no" });
            }
            else
            {
                var d = Recipe.Daily(DateTime.UtcNow);
                string streak = Progress.DailyStreak > 1 ? $" · {Progress.DailyStreak} days in a row" : "";
string next = Progress.DailyDoneOn(DateTime.UtcNow) ? "Tomorrow: " + Recipe.Daily(DateTime.UtcNow.AddDays(1)).Def.Name : "Today: " + d.Def.Name;
                Guide.Say("Touch me, or look at me, for the map.\n" + next + streak + "\n" + Progress.Souvenirs.Count + " of " + Kit.Biomes.Length + " souvenirs", 0f);
            }
        }

        // ---------- per frame ----------

        void Update()
        {
            TickLocal();
            // In a world the middle of the table is the world's own build (the fire): made things stand beside it.
            if (Maker != null) Maker.TableKeepOut = State == Mode.World || State == Mode.Changing ? 0.2f : 0f;
            // Safety fade: the world goes back to the real room while you're standing or moving about.
            bool unsafeNow = Watch != null && Watch.Unsafe && (State == Mode.World || State == Mode.Changing);
            fadeTarget = unsafeNow ? 0f : 1f;
            // Meteors must never land while you can't see them: the storm waits until you sit back down.
            if (Emergency != null) Emergency.Held = unsafeNow || fadeKeep < 0.5f;
            if (unsafeNow && Guide != null && Time.unscaledTime - lastStatusLine > 6f)
            {
                lastStatusLine = Time.unscaledTime;
                Log?.Add("safety", new Dictionary<string, object> { { "why", Watch.Why } });
                Trace.Warn("Watch", "safety fade", "why", Watch.Why);
                Guide.Say(Watch.Why == "tracking" ? "Lost tracking. Your room is back for now." : "Your room is back while you move.\nSit down to return.", 4f);
            }
            fadeKeep = Mathf.MoveTowards(fadeKeep, fadeTarget, Time.unscaledDeltaTime / 0.8f);
            if (FadeQuad != null)
            {
                FadeQuad.enabled = fadeKeep < 0.999f;
                fadeBlock ??= new MaterialPropertyBlock();
                FadeQuad.GetPropertyBlock(fadeBlock);
                fadeBlock.SetFloat("_Keep", fadeKeep);
                FadeQuad.SetPropertyBlock(fadeBlock);
            }
            if (Guide != null && Mic != null && listening) Guide.Level = Mathf.MoveTowards(Guide.Level, Mic.Level, Time.unscaledDeltaTime * 2.5f);
            RoomWatch();
            CoachWatch();
            Nets();
            if (Ears != null)
            {
                Guide?.SetEars(Ears.On && Progress.VoiceOn);
                if (listening && Ears.On && !Ears.Listening && (Mic == null || !Mic.Listening)) { listening = false; Guide?.SetMood(Guide.Mood.Idle); }
                if (listening && Ears.Listening && Guide != null) Guide.Level = Mathf.MoveTowards(Guide.Level, Ears.Level, Time.unscaledDeltaTime * 2.5f);
            }
            if (Panel != null) { Panel.VoiceSupported = Ears != null && Ears.Supported; Panel.VoiceOn = Progress.VoiceOn; Panel.Away = Move != null && Move.Away; Panel.StepMove = Progress.StepMove; }
            if (Move != null) Move.StepMove = Progress.StepMove;
            ApplySettings();
            BackgroundMusic();
            VoiceTip();
            if (Move != null)
                Move.Allowed = exploring && State == Mode.World && !Frozen && (Panel == null || !Panel.Open) && (Map == null || !Map.Showing) &&
                               (Help == null || !Help.Showing) && (Gather == null || !Gather.Running) && (Emergency == null || !Emergency.Running) &&
                               (Campfire == null || !Campfire.Held);
            if (Watch != null) Watch.AllowWalking = false;   // seated: standing up or walking off brings your room back
            // Touch-to-talk borrowed the microphone: give it back when voice is off.
            if (Ears != null && !Progress.VoiceOn && Ears.On && !Ears.Listening && !listening) Ears.TurnOff();
            // Scanned rooms: MRUK's world lock pins the rig and would undo every move, so it is off inside worlds.
            if (Meta.XR.MRUtilityKit.MRUK.Instance != null) Meta.XR.MRUtilityKit.MRUK.Instance.EnableWorldLock = !(State == Mode.World || State == Mode.Changing);
            // Glowing hands once the walls have gone; real hands while the real room shows (also during the safety fade).
            if (HandLook != null) HandLook.InWorld = (State == Mode.World || State == Mode.Changing) && (Watch == null || !Watch.Unsafe);
            if (Panel != null) Panel.StormOn = Emergency != null && Emergency.Running;
            if (Dock != null)
            {
                // Not until this world's task is set: arriving, the last world's step (and the spot where the finger had
                // just pressed Next world) would carry over (review 32).
                Dock.Show = State == Mode.World && !Busy && taskVisit == visit && (Panel == null || !Panel.Open) && (Map == null || !Map.Showing) && (Help == null || !Help.Showing);
                Dock.Aside = false;
                var dockRoom = Rooms != null ? Rooms.Room : null;
                if (dockRoom != null && dockRoom.Table != null && (Move == null || !Move.Away))
                {
                    var tb = dockRoom.Table;
                    Vector3 toYou = dockRoom.Head - tb.TopCenter; toYou.y = 0f;
                    toYou = toYou.sqrMagnitude > 1e-4f ? toYou.normalized : -dockRoom.Forward;
                    Vector3 lt = Quaternion.Inverse(tb.Rotation) * toYou;
                    float half = Mathf.Abs(lt.x) > Mathf.Abs(lt.z) ? tb.Size.x * 0.5f : tb.Size.z * 0.5f;
                    Dock.Edge = tb.TopCenter + toYou * half;
                    Dock.EdgeToYou = toYou;
                    // A table edge beyond a seated reach is no place for buttons: the bar comes to you instead. (Kept
                    // a little longer than it is taken up, so a swaying head does not move it back and forth.)
                    Vector3 edgeFlat = Dock.Edge.Value - (Head != null ? Head.position : dockRoom.Head); edgeFlat.y = 0f;
                    // Nor is an edge in your lap (a virtual table's is 0.23 m ahead and half a metre down: hands resting there pressed it).
                    float edgeBelow = (Head != null ? Head.position.y : dockRoom.Head.y) - Dock.Edge.Value.y;
                    dockAtEdge = edgeFlat.magnitude < (dockAtEdge ? 0.58f : 0.5f) && edgeFlat.magnitude > 0.3f && edgeBelow < 0.47f;
                    // Measured and logged, not acted on: the bar stays at the table's near edge, as in every worn session.
                    // (Floating it in front of you put it in the play area, and it could hop as you leaned in.)
                    if (!dockAtEdge && Trace.Every("dock-edge-far", 30f)) Trace.Info("Dock", "table edge is out of easy reach", "flat_m", edgeFlat.magnitude, "below_eyes_m", edgeBelow);
                    // Nov 17: 0.58 m away in a worn session, and a controller came out. The buttons come in to 0.5 m.
                    Vector3 seatFlat = Dock.Edge.Value - dockRoom.Head; seatFlat.y = 0f;
                    if (seatFlat.magnitude > 0.5f && seatFlat.magnitude < 0.9f && edgeBelow < 0.47f)
                        Dock.Edge = Dock.Edge.Value + toYou * (seatFlat.magnitude - 0.5f);
                }
                else Dock.Edge = null;
                // Choices the guide offers from out of reach come here: over the table's near edge, above the dock's line.
                // On your side of the edge, not over the table: a hand reaching for something on it does not pass through them.
                if (Guide != null)
                {
                    Guide.ChoicesAt = Dock.Edge.HasValue ? Dock.Edge.Value + Dock.EdgeToYou * 0.04f + Vector3.up * 0.2f : (Vector3?)null;
                    if (Guide.ChoicesBlocked == null) Guide.ChoicesBlocked = () => CarryingNow || (Dock != null && Dock.Busy);
                }
                Journey();
            }
            // Controllers: B or Y opens the worlds map from anywhere.
            if ((OVRInput.GetDown(OVRInput.Button.Two) || OVRInput.GetDown(OVRInput.Button.Four)) && !Busy && (State == Mode.World || State == Mode.Room))
            {
                if (Map != null && Map.Showing) Map.Hide(); else ShowMap("");
            }
            // The sharper picture is asked for once the headset's display is running (asked earlier, it was ignored).
            if (!pictureSet && Time.realtimeSinceStartup > 4f && UnityEngine.XR.XRSettings.isDeviceActive)
            {
                pictureSet = true;
                UnityEngine.XR.XRSettings.eyeTextureResolutionScale = 1.0f;
                Trace.Info("App", "picture set", "resolution_scale", UnityEngine.XR.XRSettings.eyeTextureResolutionScale);
            }
            if (Hints != null) Hints.Quiet = (Coach != null && Coach.Playing != HandCoach.Demo.None) || (Map != null && Map.Showing) ||
                                             (Panel != null && Panel.Open) || (Help != null && Help.Showing);
            // Juniper's words wait while the menu is open (they sat on its title and footer); they show again when it closes.
            bool mapUp = Map != null && Map.Showing;
            if (Guide != null && Guide.Words != null) Guide.Words.Muted = (Panel != null && Panel.Open) || mapUp || (Help != null && Help.Showing);
            if (localCaption != null) localCaption.Muted = mapUp || (Panel != null && Panel.Open);
            if (Guide != null && Guide.Button != null) Guide.Button.Hint = State == Mode.Room ? "Touch me for the map" : "";
            FollowWhileAway();
            if (Gather != null) Gather.EyesOnly = (Hands != null && !Hands.AnyTracked) || Progress.LookToPress;
            if (Rooms != null)
            {
                // In a world it looks too (a found scan is held, never swapped in under a world), but not while
                // Mara's call or her storm is on.
                Rooms.LookQuietly = (State == Mode.Room && (Map == null || !Map.Showing || Time.realtimeSinceStartup < 30f || Time.unscaledTime < lookAgainUntil)) ||
                                    (State == Mode.World && !StormOn && (Mission == null || !Mission.Active) && Time.unscaledTime - worldSince < 180f);
                Rooms.HoldFound = State != Mode.Room;
                if (State == Mode.Room && !Busy && Rooms.HasHeld) Rooms.ApplyHeld();
            }
            bool inHand = (Gather != null && Gather.AnyHeld) || (Maker != null && (Maker.Holding(0) || Maker.Holding(1) || Maker.Holding(2))) ||
                          (Loose != null && (Loose.Holding(0) || Loose.Holding(1))) || (Campfire != null && (Campfire.HeldBy(0) || Campfire.HeldBy(1)));
            if (inHand) carriedAt = Time.unscaledTime;
            if (Wrist != null) Wrist.Allowed = !Busy && !StormOn && (Panel == null || !Panel.Open) && !CarryingNow && (Mission == null || !Mission.Active || !Mission.WantsPalm);
            // Finding the palm panel in the first world finishes the introduction.
            if (!Progress.Onboarded && State == Mode.World && Panel != null && Panel.Open && taskDoneThisVisit)
            {
                Progress.Onboarded = true;
                SaveProgress();
            }
            // A row of choices appearing (someone who lives here, Mara, Juniper) is never hidden behind a glowing piece.
            if (State == Mode.World && Gather != null && Gather.Running && Guide != null && Guide.HasChoices && Head != null)
                Gather.KeepClearOf(Head.position, Guide.ChoiceButtons);
            // Stuck? Juniper flies to the nearest piece still waiting (every 15 s without progress).
            if (State == Mode.World && Gather != null && Gather.Running && Time.time - lastTaskMove > 15f && Guide != null && (Move == null || !Move.Away))
            {
                lastTaskMove = Time.time;
                GatherItem next = null;
                float best = float.MaxValue;
                foreach (var it in Gather.Items)
                {
                    if (it.Placed || it.HeldBy >= 0) continue;
                    float d = Vector3.Distance(it.transform.position, Head.position);
                    if (d < best) { best = d; next = it; }
                }
                if (next != null)
                {
                    Guide.Point(next.transform.position);
                    Guide.Say("Here's one!", 2.5f);
                    if (Coach != null && Coach.Playing == HandCoach.Demo.None && !Gather.AnyHeld) CoachCarry(next, null);
                    Log?.Add("hint", new Dictionary<string, object> { { "biome", Current != null ? Current.Biome : "" } });
                }
            }
            if (Panel != null)
            {
                Panel.InWorld = State == Mode.World;
                Panel.Frozen = Frozen;
                Panel.CanUndo = undo.Count > 0;
                bool wantsPalm = Mission != null && Mission.Active && (Mission.WantsPalm || Mission.Now == Mission.Step.Ending);
                Panel.Suppressed = Busy || StormOn || wantsPalm || (Help != null && Help.Showing) || (Map != null && Map.Showing);   // a palm up is for her ship then
                // ...and when she has gone from your hand, the palm that held her doesn't open the menu (review 37).
                if (wantsPalmWas && !wantsPalm) Panel.Rearm();
                wantsPalmWas = wantsPalm;
                if (Panel.Carrying == null) Panel.Carrying = () => CarryingNow;
                if ((StormOn || wantsPalm) && Panel.Open) Panel.Hide();
            }
#if UNITY_EDITOR
            EditorKeys();
#endif
        }

        // ---------- opening a world ----------

        public void Open(Recipe r, string how)
        {
            if (r == null) return;
            if (listening && Mic != null) { Mic.Cancel(); listening = false; Guide?.SetMood(Guide.Mood.Idle); }
            interview = null;
            r = r.Copy().Clamp();
            LastHow = how;
            if (State == Mode.Room) Go(OpenFromRoom(r, how));
            else if (State == Mode.World)
            {
                if (Current != null && r.Biome == Current.Biome && r.Seed == Current.Seed && Mathf.Abs(r.Density - Current.Density) < 0.01f) { Edit(r, how); return; }
                Go(ChangeWorld(r, how));
            }
        }

        void Go(IEnumerator routine)
        {
            if (running != null) StopCoroutine(running);
            running = StartCoroutine(routine);
        }

        IEnumerator OpenFromRoom(Recipe r, string how)
        {
            Map?.Hide(); Help?.Hide();
            visit++;
            sessionWonders = 0; sessionStorm = 0; sessionWorlds = 0;
            SetState(Mode.Opening);
            Panel?.Hide();
            Guide.ShowChoices(null, null);
            float t0 = askedAt > 0f ? askedAt : Time.unscaledTime;
            float wait = Time.unscaledTime + 12f;
            if (Rooms != null && Rooms.Loading) Guide.Say("Reading your room…", 0f);
            var openTimer = Trace.Phase("Reveal", "open " + r.Line());
            float waitStart = Time.unscaledTime;
            while (Rooms != null && Rooms.Loading && Time.unscaledTime < wait) yield return null;
            if (Time.unscaledTime - waitStart > 0.1f) Trace.Info("Reveal", "waited for the room", "ms", Math.Round((Time.unscaledTime - waitStart) * 1000f), "loaded", Rooms == null || !Rooms.Loading);
            // A scan that arrived while we waited is used for this world (it was held, as in any world: review 32).
            if (Rooms != null && Rooms.HasHeld) Rooms.ApplyHeld();
            // Scanned or not, the room is laid out around where the head really is now.
            if (Rooms != null) Rooms.Recentre();
            var room = Rooms.Room;
            Look.Facing = room.Forward;
            Shell.SetOrigin(RevealOrigin(room));
            Shell.Build(room);
            Shell.Radius = -1f;
            Shell.HoldAll(true);

            // Build behind the room (the walls hide it) over a few frames.
            Guide.SetMood(Guide.Mood.Thinking);
            Trace.Info("Reveal", "room for this world", "source", room.Source, "walls", room.Walls.Count, "pieces", room.Pieces.Count,
                       "table", room.Table != null ? (room.Table.Virtual ? "virtual" : room.Table.Label) : "none", "table_top_m", room.Table != null ? room.Table.TopY - room.FloorY : 0f,
                       "floor_y", room.FloorY, "head_over_floor_m", Head.position.y - room.FloorY, "origin", Shell.Origin);
            yield return Builder.BuildRoutine(r, room);
            Builder.SetVisible(true);
            Skins.Build(room, r);
            Trace.Info("Skins", "built", "count", Skins.All.Count, "triangles", Skins.Triangles, "table_skin", Skins.TableSkin != null ? Skins.TableSkin.Id : "none");
            MakeSite(r, room);
            Look.Show(r, snap: true);
            Look.Veil = 0f;
            undo.Clear();
            Current = r;
            if (Ripples != null) Ripples.Biome = r.Biome;
            Progress.Visited(r.Biome);
            NoteVisit(r);
            Progress.Last = r.Copy();
            SaveProgress();
            firstWorld = !Progress.Onboarded && !homeVisit;
            Guide.SetMood(Guide.Mood.Hidden);
            if (Shelf != null) Shelf.SetVisible(false);

            // Choreography (research D §3): trace, spark, walls dissolve, furniture turns, the table wakes.
            float now = 0f;
            Light?.For(r);
            if (Verbs != null && room.Scanned) StartCoroutine(Verbs.Show(room, RevealSeconds * 0.5f));
            TraceTable(room, true);
            Sfx.Instance?.Ui("ack", 0.8f);
            Vector3 from = room.Table.TopCenter + Vector3.up * 0.05f, to = Shell.Origin;
            spark = Effects.Halo("Spark", transform, GlowFlat, 0.08f, new Color(1f, 0.75f, 0.4f));
            spark.transform.position = from;
            Sfx.Instance?.At("reveal_whoosh", from, 0.8f);
            while (now < 0.9f)
            {
                now += Time.deltaTime;
                float k = Util.Ease(now / 0.9f);
                Shader.SetGlobalFloat(RoomGlowId, k);
                if (Light != null) Light.Amount = 0.5f * k;
                spark.transform.position = Vector3.Lerp(from, to, k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * 0.15f;
                yield return null;
            }
            Util.Kill(spark);
            LastWordsToWorldMs = (Time.unscaledTime - t0) * 1000f;
            Log?.StartVisit(r, how, LastWordsToWorldMs, Builder != null ? Builder.Triangles : -1, Builder != null ? Builder.Renderers : -1, Builder != null ? Builder.LastBuildMs : -1f);
            Sfx.Instance?.At("reveal_thump", to, 0.7f);
            Sfx.Instance?.Bed(r.Def.Ambience, room.Head);
            float full = Shell.FullRadius, tr = 0f;
            bool grown = false;
            while (tr < RevealSeconds)
            {
                tr += Time.deltaTime;
                float k = Util.Ease(tr / RevealSeconds);
                Shell.Radius = k * full;
                if (Light != null) Light.Amount = 0.5f + 0.5f * k;
                if (!grown && tr > RevealSeconds * 0.45f) { Skins.Show(true); grown = true; }
                yield return null;
            }
            if (!grown) Skins.Show(true);
            Shell.Radius = full + 2f;
            Shader.SetGlobalFloat(RoomGlowId, 0f);
            if (Light != null) Light.Amount = 1f;
            TraceTable(room, false);
            while (Skins.Growing) yield return null;
            Shell.HoldAll(false);
            WakeTable(r);
            Frozen = false;
            openTimer.Dispose();
            SetState(Mode.World);
            if (Watch != null) Watch.Arm();
            askedAt = -1f;

            // A moment to look around before anything is asked; the guide notices your furniture.
            yield return new WaitForSeconds(1.2f);
            string roomLine = RoomLine();
            if (roomLine.Length > 0 && Guide != null)
            {
                Guide.Place(TaskCaptionSpot());
                Guide.SetMood(Guide.Mood.Speaking);
                Guide.Say(roomLine, 3f);
                yield return new WaitForSeconds(2.6f);
            }
            else yield return new WaitForSeconds(firstWorld ? 1.3f : 0.3f);
            StartTask(r);
        }

        IEnumerator ChangeWorld(Recipe r, string how, bool pushUndo = true)
        {
            Map?.Hide(); Help?.Hide();
            if (Frozen) SetFrozen(false, "");
            CleanTransition();
            visit++;
            Emergency?.Stop();
            Gifts?.Clear();
            Mission?.Stop();
            Move?.Home(true, "world changing");
            Wonders?.Clear();
            SetState(Mode.Changing);
            firstWorld = !Progress.Onboarded && !homeVisit;
            Panel?.Hide();
            float t0 = askedAt > 0f ? askedAt : Time.unscaledTime;
            Gather.Stop();
            Campfire?.Stop();
            Loose?.Stop();
            StopCoach("world changing");
            Finale?.Clear();
            Sfx.Instance?.Ui("ack", 0.8f);
            // Fog thickens in this world's colour, the next world is built behind it, the fog clears in the next one's.
            float v = 0f;
            while (v < 1f) { v = Mathf.Min(1f, v + Time.deltaTime / 0.8f); Look.Veil = Util.Ease(v); yield return null; }
            // A scan found while in the last world: this one is built round your furniture.
            if (Rooms.HasHeld)
            {
                Rooms.ApplyHeld();
                Rooms.Recentre();
                // The walls and the passthrough holders over each real piece, for the room as scanned (the world is
                // already open: the walls stay dissolved).
                Shell.SetOrigin(RevealOrigin(Rooms.Room));
                Shell.Build(Rooms.Room);
                Shell.Radius = Shell.FullRadius + 2f;
                Shell.HoldAll(false);
            }
            var room = Rooms.Room;
            if (pushUndo && Current != null) undo.Add(Current);
            Trim();
            yield return Builder.BuildRoutine(r, room);
            Skins.Build(room, r);
            Skins.Show(true, instant: true);
            MakeSite(r, room);
            // Not snapped: the new world's light and fog blend in over 1.4 s while the fog clears (snapping changed the
            // whole view's colour in one frame, up to 17 times brighter from a night world to a day one).
            Look.Show(r);
            Light?.For(r);
            Current = r;
            if (Ripples != null) Ripples.Biome = r.Biome;
            Progress.Visited(r.Biome);
            NoteVisit(r);
            Progress.Last = r.Copy();
            LastWordsToWorldMs = (Time.unscaledTime - t0) * 1000f;
            Log?.StartVisit(r, how, LastWordsToWorldMs, Builder != null ? Builder.Triangles : -1, Builder != null ? Builder.Renderers : -1, Builder != null ? Builder.LastBuildMs : -1f);
            Sfx.Instance?.Bed(r.Def.Ambience, room.Head);
            while (v > 0f) { v = Mathf.Max(0f, v - Time.deltaTime / 1.2f); Look.Veil = Util.Ease(v); yield return null; }
            Look.Veil = 0f;
            WakeTable(r);
            SetState(Mode.World);
            askedAt = -1f;
            yield return new WaitForSeconds(1.2f);
            StartTask(r);
        }

        void MakeSite(Recipe r, RoomModel room)
        {
            if (room.Table == null)
            {
                Debug.LogWarning($"[Worldeck] no work table in the {room.Source} room ({room.Pieces.Count} pieces): choosing again");
                room.Choose(Head.position);
            }
            var t = room.Table;
            Site.transform.position = t.TopCenter + Vector3.up * 0.002f;
            Site.transform.rotation = t.Rotation;
            float foot = Mathf.Clamp(Mathf.Min(t.Size.x, t.Size.z) * 0.42f, 0.22f, 0.42f);
            Site.Make(r, foot);
            Site.gameObject.SetActive(true);
            taskDoneThisVisit = false;
        }

        void WakeTable(Recipe r)
        {
            if (r.Def.Build == "fire")
            {
                Sfx.Instance?.At("fire_ignite", Site.transform.position, 0.9f);
                Sfx.Instance?.Loop("fire_loop", Site.transform.position, 0.18f);
            }
            else Sfx.Instance?.Loop(null, Vector3.zero, 0f);
        }

        bool homeVisit, flightVisit;

        // ---------- the opening, animal first ----------

        public static readonly (string Kind, string Label)[] FirstAnimals = { ("t_rex", "A T. rex"), ("zebra", "A zebra"), ("fox", "A fox") };
        bool firstAnimalOffered;
        Maker.Thing firstAnimal;
        /// <summary>The animal made in the opening (null before, or once it has been taken home).</summary>
        public Maker.Thing FirstAnimal => firstAnimal;

        void OfferFirstAnimal()
        {
            firstAnimalOffered = true;
            Map?.Hide();
            Guide.SetMood(Guide.Mood.Speaking);
            Guide.Say("Let's start with someone small.\nPick one: it appears on your table.", 0f);
            var labels = new string[FirstAnimals.Length];
            var ids = new string[FirstAnimals.Length];
            for (int i = 0; i < FirstAnimals.Length; i++) { labels[i] = FirstAnimals[i].Label; ids[i] = "first:" + FirstAnimals[i].Kind; }
            Guide.ShowChoices(labels, ids);
            Trace.Info("Tutorial", "step", "n", 2, "what", "first animal");
            Log?.Add("tutorial", new Dictionary<string, object> { { "step", "first animal offered" } });
        }

        void FirstChoice(string what)
        {
            Guide.ShowChoices(null, null);
            if (what == "campfire")
            {
                // From an animal's home to Mara: the animal stays where it lives, and she calls as soon as you arrive.
                Maker?.Clear();
                ClearPedestal();
                if (Inspect != null) Inspect.FirstHint = "";
                flightVisit = true;
                Open(Recipe.For("campfire", 1), "first");
                return;
            }
            if (what == "stay") { Guide.Say("Stay as long as you like.\nThe bright button on your table answers Mara.", 5f); return; }
            if (Maker == null || !MadeKinds.Has(what) || State != Mode.Room) return;
            var item = new ActItem { Do = "make", Kind = what, Name = what == "t_rex" ? "t rex" : what.Replace('_', ' '), Place = "table", Count = 1, Size = Wishes.SizeOf(what) };
            if (Maker.Make(item, Rooms != null ? Rooms.Room : null, Recipe.For(Kit.Biomes[0].Id, 1)) == 0) return;
            firstAnimal = Maker.Last();
            if (Inspect != null) Inspect.FirstHint = "Touch \u201CSee where it lives\u201D below.";
            Log?.Add("made", new Dictionary<string, object> { { "kind", what }, { "count", 1 }, { "by", "first" } });
            Trace.Info("Tutorial", "step", "n", 3, "what", "hold it up", "kind", what);
            Guide.SetMood(Guide.Mood.Speaking);
            var table = Rooms != null && Rooms.Room != null ? Rooms.Room.Table : null;
            if (table != null && table.Virtual && GlowFlat != null)
            {
                // No scan: a soft glow shows where the imagined table is, so the animal stands on something.
                ClearPedestal();
                pedestal = Util.MeshObject("First table glow", transform, Util.Quad(new Color(1f, 0.78f, 0.45f) * 0.22f), GlowFlat);
                pedestal.transform.SetPositionAndRotation(table.TopCenter + Vector3.up * 0.004f, table.Rotation * Quaternion.Euler(90f, 0f, 0f));
                pedestal.transform.localScale = new Vector3(table.Size.x, table.Size.z, 1f);
            }
            Guide.Say(table != null && table.Virtual ? "It's on the table beside me.\nPinch it or grab it,\nthen bring it close to your face." : "Pinch it or grab it,\nthen bring it close to your face.", 0f);
            StartCoroutine(CoachFirstAnimal(firstAnimal));
        }

        GameObject pedestal;
        void ClearPedestal() { if (pedestal != null) Util.Kill(pedestal); pedestal = null; }

        /// <summary>
        /// The see-through hand, all through the opening: pinch and lift while the animal sits there; touch "See where
        /// it lives" once it's been looked at and let go; nothing while you hold it up. Until the animal goes home.
        /// </summary>
        IEnumerator CoachFirstAnimal(Maker.Thing t)
        {
            yield return new WaitForSecondsRealtime(1.2f);
            float t0 = Time.unscaledTime, lastShow = -10f;
            bool reminded = false;
            while (t != null && t.T != null && firstAnimal == t && State == Mode.Room)
            {
                bool looking = Inspect != null && Inspect.Looking == t;
                bool held = Maker.IsHeld(t);
                bool waiting = !looking && Inspect != null && Inspect.HomeButton != null;
                if (looking || held) { if (Coach != null && Coach.Playing != HandCoach.Demo.None && coachFor == "animal") StopCoach("held"); }
                else if (Coach != null && Coach.Playing == HandCoach.Demo.None && Head != null && Time.unscaledTime - lastShow > 6f)
                {
                    lastShow = Time.unscaledTime;
                    coachFor = "animal";
                    if (waiting) Coach.Show(HandCoach.Demo.Poke, Inspect.HomeButton.transform.position, default, null, null, 1f, 2);
                    else Coach.Show(HandCoach.Demo.Carry, Maker.Centre(t), Head.position + Head.forward * 0.32f + Vector3.down * 0.05f, null, null, 1f, 2);
                }
                if (!reminded && Time.unscaledTime - t0 > 10f && !held && !looking)
                {
                    reminded = true;
                    Guide.Say(waiting ? "Touch \u201CSee where it lives\u201D beside it."
                              : Hands != null && !Hands.AnyTracked ? "Look at it for a moment, then at\n\u201CSee where it lives\u201D."
                              : "Pinch it, or close your whole hand on it,\nthen lift it close to your face.", 0f);
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            ClearPedestal();
            // Thrown away, or gone some other way, before it was taken home: the introduction is not over. Pick again.
            if (State == Mode.Room && firstAnimal == t && (t == null || t.T == null) && !Progress.Onboarded)
            {
                Trace.Info("Tutorial", "the first animal is gone: offered again");
                firstAnimal = null;
                firstAnimalOffered = false;
                OfferFirstAnimal();
            }
        }

        /// <summary>The visit whose task (or its absence) has been set up: until then the dock offers no step.</summary>
        int taskVisit = -1;

        void StartTask(Recipe r)
        {
            if (State != Mode.World) return;
            taskVisit = visit;
            darkName = ""; darkPieces = null; darkAfter = "";
            // Brought an animal home: the moment is the animal among its herd, not a task.
            // Today's flight: she calls as soon as you arrive (the task waits for another visit).
            // (Travel is set up on these visits too: without it the first jump after her call went nowhere sensible.)
            if (flightVisit) { flightVisit = false; OpenLand(r); StartCoroutine(CallSoon(visit)); return; }
            if (homeVisit) { homeVisit = false; OpenLand(r); Trace.Info("Gather", "no task on a home visit"); return; }
            // Mara's call came first (asked for during the reveal): her table stays clear, the build waits for another visit.
            if (Mission != null && Mission.Active) { OpenLand(r); stormThisWorld = true; return; }
            int n = firstWorld ? FirstTaskItems : TaskItems;
            // Small things to pick up and toss appear with the task (after the reveal, never during it).
            Loose?.Begin(r, Rooms.Room);
            OpenLand(r);
            Gather.Begin(r, Rooms.Room, n);
            lastTaskMove = Time.time;
            // A seat with no clear space in reach (squeezed between beds, a wall close in front): no task, the story
            // goes on (the room sweep found a few such seats).
            if (!Gather.Running)
            {
                Trace.Warn("Gather", "no room for the pieces here: task skipped");
                StartCoroutine(AfterTask());
                return;
            }
            // Furniture she lost last time shows as it really is, dark, until these pieces light it again.
            darkName = DarkHere(out darkPieces);
            if (darkName.Length > 0 && Skins != null) foreach (var p in darkPieces) Skins.Dim(p, true);
            if (darkName.Length > 0) Trace.Info("Runway", "still dark", "piece", darkName, "count", darkPieces.Count);
            Trace.Info("Gather", "task started", "items", Gather.Total, "wanted", n, "first_world", firstWorld, "item", r.Def.Item);
            var def = r.Def;
            Guide.Place(TaskCaptionSpot());
            Guide.SetMood(Guide.Mood.Speaking);
            bool eyes = Hands != null && !Hands.AnyTracked;
            Guide.Say(eyes ? def.TaskLine + "\nLook at a glowing " + def.Item + " to bring it to your table." + (darkName.Length > 0 ? Story.DarkTask(darkName) : "")
                    : firstWorld ? def.TaskLine + "\nPinch a glowing " + def.Item + " and put it on your table." : def.TaskLine + (darkName.Length > 0 ? Story.DarkTask(darkName) : ""), firstWorld || eyes ? 0f : 6f);
            StartCoroutine(CoachGatherSoon(firstWorld));
            if (Mission != null && Emergency != null) StartCoroutine(FaintCallSoon());
            roastOver = false;
            StartCoroutine(RescueGather());
        }

        Vector3 TaskCaptionSpot()
        {
            var room = Rooms.Room;
            var t = room.Table;
            Vector3 toHead = room.Head - t.TopCenter; toHead.y = 0;
            Vector3 back = toHead.sqrMagnitude > 1e-4f ? -toHead.normalized : room.Forward;
            Vector3 p = t.TopCenter + back * Mathf.Min(t.Size.x, t.Size.z) * 0.3f + Vector3.up * 0.42f;
            p.y = Mathf.Min(p.y, room.Head.y - 0.05f);
            return p;
        }

        void OnTaskDone()
        {
            taskDoneThisVisit = true;
            if (!Progress.Onboarded) { Progress.Onboarded = true; Trace.Info("Tutorial", "first world done: introduction complete"); }
            var r = Current;
            Trace.Info("Gather", "task done", "seconds", Gather.Seconds, "items", Gather.Total, "world", r.Line());
            Sfx.Instance?.At("build_complete", Site.transform.position, 0.9f);
            string flourish = BuildSound(r.Def.Build);
            if (flourish != null) Sfx.Instance?.At(flourish, Site.transform.position, 0.9f);
            if (r.Def.Build == "jukebox") Sfx.Instance?.Loop("jukebox_music", Site.transform.position, 0.55f);
            if (r.Def.Build == "fire") Sfx.Instance?.LoopVolume(0.3f);
            // What went dark in her last storm is lit again by what you gathered.
            bool relit = darkName.Length > 0 && darkPieces != null && darkPieces.Count > 0;
            if (relit)
            {
                foreach (var p in darkPieces)
                {
                    Progress.DarkPieces.Remove(p.Key);
                    Skins?.Dim(p, false);
                    Sfx.Instance?.At("souvenir_chime", p.TopCenter, 0.8f, 1.15f);
                }
                relitName = darkName; relitVisit = visit; darkName = "";
                maraOffered = true;   // the line below says where she is: no second line over it
                Trace.Info("Runway", "lit again", "piece", relitName);
                Log?.Add("runway", new Dictionary<string, object> { { "relit", relitName } });
            }
            Guide.SetMood(Guide.Mood.Speaking);
            // (She may have flown today already: then the line doesn't send you to a button that isn't there.)
            Guide.Say(relit ? Story.RelitLine(relitName, MaraHasMore) : r.Def.DoneLine, relit ? 6f : 4f);
            var v = Progress.Finished(r, DateTime.UtcNow);
            if (v != null) pendingSouvenir = v;
            if (r.Title.StartsWith("Today", StringComparison.Ordinal) || LastHow == "daily") Progress.DailyDone(DateTime.UtcNow);
            SaveProgress();
            Log?.Add("task", new Dictionary<string, object>
            {
                { "biome", r.Biome }, { "items", Gather.Total }, { "seconds", Math.Round(Gather.Seconds, 1) }, { "first", firstWorld },
                { "souvenir", v != null }
            });
            StartCoroutine(AfterTask());
        }

        IEnumerator FaintCallSoon()
        {
            int v = visit;
            yield return new WaitForSeconds(firstWorld ? 14f : 6f);
            if (State != Mode.World || visit != v || stormThisWorld || Mission == null || Mission.Active) yield break;
            if (Gather != null && !Gather.Running) yield break;
            if (!MaraCallsUnasked) yield break;   // she is waiting to be answered: no crackle from the table
            callWaiting = false;
            Mission.Avoid = Site != null ? Site.transform.position : (Vector3?)null;
            Mission.Tease(Rooms.Room);
        }

        IEnumerator AfterTask()
        {
            var r = Current;
            int v = visit;
            bool hands = Hands != null && Hands.AnyTracked;
            // The story: as soon as the build is done, the call comes through (the roast waits for another visit).
            if (Mission != null && Emergency != null && !stormThisWorld && r != null)
            {
                yield return new WaitForSeconds(2.5f);
                if (State != Mode.World || visit != v || stormThisWorld) yield break;
                if (firstWorld) { if (!Progress.Onboarded) { Progress.Onboarded = true; SaveProgress(); } firstWorld = false; }
                if (MaraCallsUnasked) { StartMission("task"); yield break; }
                // She waits: the world is yours, and the button on your table answers her. Said once a session;
                // after that the button says it. The world's own ending (the roast, the finale) plays as usual.
                if (MaraHasMore)
                {
                    callWaiting = true;
                    Log?.Add("mission_waits", new Dictionary<string, object> { { "chapter", Progress.MissionChapter }, { "biome", r.Biome } });
                    if (!maraOffered)
                    {
                        maraOffered = true;
                        Guide.SetMood(Guide.Mood.Speaking);
                        Guide.Say("Mara's on the radio when you want her:\nthe bright button on your table.", 5f);
                        yield return new WaitForSeconds(4f);
                        if (State != Mode.World || visit != v) yield break;
                    }
                }
            }
            // Your furniture just lit again: Juniper's line about it is read in full, with nothing over it (no roast,
            // no "Look up!" that sends the eyes away from the sofa), and the bright button answers Mara.
            bool relitHere = relitVisit == v;
            if (relitHere)
            {
                float until = Time.unscaledTime + 10f;
                while (State == Mode.World && visit == v && Guide.Words != null && Guide.Words.Fresh && Time.unscaledTime < until) yield return null;
                if (State != Mode.World || visit != v) yield break;
            }
            if (r != null && r.Def.Build == "fire" && Campfire != null && !relitHere)
            {
                // The campfire's own play: roast, then stoke the sparks into the sky.
                yield return new WaitForSeconds(3f);
                if (State != Mode.World || visit != v) yield break;
                if (Emergency != null && Emergency.Running) yield break;
                Move?.Home(true, "roast");
                var spots = Gather.Spots(Rooms.Room, Rooms.Room.Head, Rooms.Room.Forward, 1, r.Seed + 99);
                Vector3 rest = spots.Count > 0 ? spots[0] : Rooms.Room.Table.TopCenter + Vector3.up * 0.1f;
                Campfire.Begin(Site.transform.position + Vector3.up * 0.03f, rest);
                Guide.SetMood(Guide.Mood.Speaking);
                Guide.Say("Roast a marshmallow!\nPinch the stick, hold it over the fire.", 0f);
                StartCoroutine(CoachRoastSoon());
                float until = Time.time + (hands ? 40f : 6f);
                while (State == Mode.World && visit == v && Campfire.Eaten == 0 && Time.time < until) yield return null;
                roastOver = true;
                if (State != Mode.World || visit != v) yield break;
                yield return new WaitForSeconds(1.5f);
                if (State != Mode.World || visit != v) yield break;
                PlayFinale(r);
                yield return new WaitForSeconds(6f);
            }
            else
            {
                yield return new WaitForSeconds(relitHere ? 0.5f : 2.5f);
                if (State != Mode.World || visit != v) yield break;
                PlayFinale(r, quiet: relitHere);
                yield return new WaitForSeconds(4.5f);
            }
            if (State != Mode.World || visit != v) yield break;
            if (firstWorld) { if (!Progress.Onboarded) { Progress.Onboarded = true; SaveProgress(); } firstWorld = false; }
            // Mara is waiting: the world opens for exploring, and the button answers her when you like.
            // (Mid-story the bright button answers her: the world beyond opens by itself only once her story is told.)
            if (callWaiting && !stormThisWorld) { if (Progress.MissionChapter >= Story.Chapters) StartCoroutine(InviteOut(visit)); yield break; }
            Guide.SetMood(Guide.Mood.Speaking);
            Guide.Say("Ready for more?\nPress the bright button on your table.", 6f);
        }

        /// <summary>
        /// Mara rings by herself only for her very first call (the story starts there), and not even then if the
        /// player chose "when I'm ready". In every world after that she waits to be answered.
        /// </summary>
        bool MaraCallsUnasked => !Progress.CallWhenReady && Progress.MissionChapter == 0;
        /// <summary>She has something to fly: a chapter left, or (her story told) today's flight not yet flown.</summary>
        bool MaraHasMore => Progress.MissionChapter < Story.Chapters || !Progress.FlownOn(DateTime.UtcNow);
        // "Mara's on the radio when you want her": said once a session.
        bool maraOffered;

        void PlayFinale(Recipe r, bool quiet = false)
        {
            if (Finale == null || r == null) return;
            Finale.Play(r.Def.Souvenir, WorldColors.Hex(r.Def.Palette.Emissive, Color.white), Head.forward);
            if (!quiet) Guide.Say("Look up!", 3f);
            Log?.Add("finale", new Dictionary<string, object> { { "biome", r.Biome } });
        }

        void OnAte(string how)
        {
            Guide.SetMood(Guide.Mood.Speaking);
            switch (how)
            {
                case "golden":
                    Guide.Say("Perfect! Golden.", 3f);
                    // A golden marshmallow makes this world's souvenir the gold one.
                    var s = Progress.Souvenirs.Find(x => x.Biome == "campfire");
                    if (s != null && s.Variant != "gold") { s.Variant = "gold"; SaveProgress(); }
                    Sfx.Instance?.Ui("souvenir_chime", 0.6f);
                    break;
                case "raw": Guide.Say("Still cold... try holding it longer.", 3f); break;
                case "toasty": Guide.Say("Toasty!", 2.5f); break;
                default: Guide.Say("Crunchy... very crunchy.", 3f); break;
            }
            Log?.Add("roast", new Dictionary<string, object> { { "how", how } });
        }

        static string BuildSound(string build) => build switch
        {
            "fire" => "build_fire", "sandcastle" => "build_sandcastle", "sun_crystal" => "build_crystal", "snowman" => "build_snowman",
            "star_map" => "build_starmap", "anemone_glow" => "build_anemone", "jukebox" => "build_jukebox", "blossom_tree" => "build_blossom",
            _ => null
        };

        // ---------- editing and the controls ----------

        public void Edit(Recipe r, string how)
        {
            if (State != Mode.World || r == null) return;
            r = r.Copy().Clamp();
            if (Current != null) undo.Add(Current);
            Trim();
            float t0 = askedAt > 0f ? askedAt : Time.unscaledTime;
            if (!Builder.Edit(r))
            {
                Trace.Info("World", "edit needs a rebuild", "how", how, "to", r.Line());
                Go(ChangeWorld(r, how, pushUndo: false));
                return;
            }
            Trace.Info("World", "edit", "how", how, "to", r.Line(), "undo_depth", undo.Count);
            Look.Show(r);
            Current = r;
            LastWordsToWorldMs = (Time.unscaledTime - t0) * 1000f;
            Log?.Add("edit", new Dictionary<string, object> { { "line", r.Line() }, { "how", how }, { "ms", Math.Round(LastWordsToWorldMs) } });
            askedAt = -1f;
        }

        void Trim() { while (undo.Count > 20) undo.RemoveAt(0); }

        public void Undo()
        {
            if (State != Mode.World) return;
            if (undo.Count == 0) { Guide.Say("Nothing to undo.", 2.5f); return; }
            var r = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            if (Current != null && r.Biome == Current.Biome && r.Seed == Current.Seed && Builder.Edit(r)) { Look.Show(r); Current = r; }
            else Go(ChangeWorld(r, "undo", pushUndo: false));
            Sfx.Instance?.Ui("not_understood", 0.5f);
            Guide.Say("Undone.", 2f);
        }

        string frozenBy = "";

        public void SetFrozen(bool on, string why)
        {
            if (State != Mode.World && on) return;
            Frozen = on;
            if (Loose != null) Loose.Frozen = on;
            frozenBy = on ? why : "";
            Time.timeScale = on ? 0f : 1f;
            Trace.Info("Director", on ? "frozen" : "resumed", "why", why);
            bool asked = why != "headset off" && why != "system menu";
            if (Frozen && asked)
            {
                // Freeze: the world holds still mid-motion, and what hangs in the air can be touched.
                Freeze?.Begin();
                Sfx.Instance?.Ui("listen_stop", 0.7f);
                bool weather = Current != null && (Current.Precip != Precip.None || Current.Extras.Contains(Extras.Fireflies) || Current.Def.Build == "fire");
                Guide.Say(weather ? "Frozen. Touch what's hanging in the air.\nYour wrist menu resumes." : "Frozen. Your wrist menu resumes.", 0f);
            }
            else if (on) Guide.Say("", 0.1f);
            else
            {
                Freeze?.End();
                Sfx.Instance?.Ui("listen_start", 0.6f);
                Guide.Say("", 0.1f);
            }
            Log?.Add(on ? "freeze" : "resume", new Dictionary<string, object> { { "why", why } });
        }

        public void Close(string why)
        {
            if (State == Mode.Room || State == Mode.Closing) return;
            Go(CloseWorld(why));
        }

        IEnumerator CloseWorld(string why)
        {
            if (Frozen) SetFrozen(false, "");
            CleanTransition();
            Look.Veil = 0f;
            visit++;
            Emergency?.Stop();
            Gifts?.Clear();
            Mission?.Stop();
            Maker?.Clear();
            Move?.Home(true, "world closing");
            Wonders?.Clear();
            undo.Clear();
            interview = null;
            SetState(Mode.Closing);
            Panel?.Hide();
            Gather.Stop();
            Campfire?.Stop();
            Loose?.Stop();
            StopCoach("world closing");
            Finale?.Clear();
            Watch?.Disarm();
            Sfx.Instance?.Ui("world_end", 0.8f);
            Sfx.Instance?.BedOff();
            Sfx.Instance?.Loop(null, Vector3.zero, 0f);
            worldTune = false;   // the background music carries on into your room
            Log?.EndVisit(why);
            Trace.Info("Director", "closing", "why", why, "world", Current != null ? Current.Line() : "-", "placed", Gather != null ? Gather.PlacedCount : 0,
                       "taps", Ripples != null ? Ripples.Taps : 0, "touches", Ripples != null ? Ripples.Count : 0);
            Shell.HoldAll(true);
            Skins.Show(false);
            // Walls come back from the far side towards you.
            float full = Shell.FullRadius, t = 0f;
            while (t < CloseSeconds)
            {
                t += Time.deltaTime;
                Shell.Radius = full * (1f - Util.Ease(t / CloseSeconds));
                if (Light != null) Light.Amount = 1f - Util.Ease(t / CloseSeconds);
                yield return null;
            }
            Shell.Radius = -1f;
            Builder.SetVisible(false);
            Builder.Clear();
            Skins.Clear();
            Site.Clear();
            Look.ShowRoom();
            Current = null;
            SetState(Mode.Room);
            var room = Rooms.Room;
            PlaceGuide(room);
            // A first session never reached the room's own offer (it needs the introduction done, which happens in a
            // world): the first time you are back, after anything Juniper has to say about the visit.
            if (room != null && !room.Scanned && !invitedScan && Rooms != null && !Rooms.Loading && Progress.Onboarded)
            {
                invitedScan = true;
                StartCoroutine(InviteScanSoon(12f));
            }
            if (Shelf != null)
            {
                Shelf.Show(Progress, room);
                Shelf.SetVisible(true);
                SaveProgress();
            }
            if (pendingSouvenir != null)
            {
                Sfx.Instance?.Ui("souvenir_chime", 0.9f);
                Guide.Say("You kept a " + pendingSouvenir.Kind.Replace('_', ' ') + " from there.\nTouch it to go back.\n" +
                          Progress.Souvenirs.Count + " of " + Kit.Biomes.Length + " · Tomorrow: " + Recipe.Daily(DateTime.UtcNow.AddDays(1)).Def.Short, 8f);
                pendingSouvenir = null;
            }
            else RoomGreeting();
            // Her story is over: the first time you are home after it, its ending happens here, in your room.
            if (Progress.MissionChapter >= Story.Chapters && !Progress.StoryEndSeen && Shelf != null)
            {
                Progress.StoryEndSeen = true;
                SaveProgress();
                StartCoroutine(StoryEndAtHome());
                yield break;
            }
            string recap = Recap();
            if (recap.Length > 0) { Guide.SetMood(Guide.Mood.Speaking); Guide.Say(recap, 10f); Trace.Info("Director", "recap", "worlds", sessionWorlds, "wonders", sessionWonders, "storm", sessionStorm); }
        }

        // ---------- talking ----------

        void OnGuideTapped()
        {
            if (State == Mode.Room && Rooms != null && Rooms.Loading && Time.realtimeSinceStartup < 20f)
            {
                Guide.Say("One moment, reading your room…", 2f);
                return;
            }
            Trace.Info("Guide", "touched", "onboarded", Progress.Onboarded, "room", Rooms != null && Rooms.Room != null ? Rooms.Room.Source : "-", "listening", listening, "busy", Busy);
            if (Busy) return;
            if (listening) { if (Ears != null && Ears.Listening) { Ears.StopListening(); listening = false; Guide.SetMood(Guide.Mood.Idle); } else Mic.End(); return; }
            askedAt = Time.unscaledTime;
            // The very first touch: pick an animal for your table (payoff in seconds). Touching Juniper again, or with no
            // hands, goes straight to the campfire and Mara's story.
            if (!Progress.Onboarded && State == Mode.Room)
            {
                Map?.Hide();
                if (!firstAnimalOffered && firstAnimal == null && Maker != null && Hands != null && Hands.AnyTracked) { OfferFirstAnimal(); return; }
                Guide.ShowChoices(null, null);
                firstAnimal = null;
                ClearPedestal();
                if (Inspect != null) Inspect.FirstHint = "";
                Maker?.Clear();
                Open(Recipe.For("campfire", 1), "first");
                return;
            }
            // Voice when it's set up; otherwise (and always in the room) the map.
            if (AppConfig.HasVoice && State == Mode.World) { StartTalking(); return; }
            if (Map != null && Map.Showing) { if (AppConfig.HasVoice) StartTalking(); else Map.Hide(); return; }
            ShowMap("");
            if (AppConfig.HasVoice) StartTalking();
        }

        void StartTalking()
        {
            if (Ears != null && Ears.Supported && Ears.State != Ears.Status.Failed)
            {
                Mic?.RequestPermission(ok =>
                {
                    if (!ok) { Guide.Say("No microphone, no problem.", 3f); ShowChoices("Where to?"); return; }
                    if (!Progress.VoiceTried)
                    {
                        Progress.VoiceTried = true;
                        SaveProgress();
                        Guide.Say("I listen on this headset only.\nNothing you say leaves it.", 3f);
                    }
                    Ears.Listen(8f);
                    listening = true;
                    Sfx.Instance?.Ui("listen_start", 0.8f);
                    Sfx.Instance?.Duck(8f);
                    Guide.SetMood(Guide.Mood.Listening);
                    Guide.Say(Ears.State == Ears.Status.Ready
                        ? (State == Mode.World ? "Listening… try \"darker\", \"rain\" or a new place." : "Listening… where to?")
                        : "Getting my ears ready…", 0f);
                });
                return;
            }
            // The headset's own recogniser isn't available: choose instead. Speech never goes to the cloud
            // (Juniper promises "Nothing you say leaves it").
            Trace.Info("Voice", "talk requested without the on-device recogniser: choices instead", "mics", Microphone.devices.Length);
            Health.Set(Health.Voice, Health.Level.Fallback, "on-device recogniser not ready", Time.realtimeSinceStartup, "Voice isn't ready on this headset. Choose instead.");
            ShowChoices(State == Mode.World ? "Where next?" : "Where to?");
        }


        /// <summary>Acts on something the player said (or typed in tests).</summary>
        int refineTicket;

        /// <summary>Claude's version of a world opened from the words we knew: applied if nothing else has happened since.</summary>
        IEnumerator Refined(AiAnswer a, int ticket, Recipe opened, string how)
        {
            Trace.Info("AI", "refine answer", "ok", a.Error == null, "ms", Math.Round(a.Ms), "look", a.Recipe?.Look != null, "title", a.Recipe?.Title ?? "-");
            if (a.Error != null || a.Recipe == null || a.Declined || a.Clarify.Length > 0) yield break;
            var r = a.Recipe;
            bool better = r.Look != null && (opened == null || opened.Look == null || r.Look.Name != opened.Look.Name) || (opened != null && r.Biome != opened.Biome);
            if (!better) yield break;
            float until = Time.unscaledTime + 15f;
            while (State != Mode.World && Time.unscaledTime < until) yield return null;
            if (ticket != refineTicket || State != Mode.World || (Mission != null && Mission.Active)) yield break;
            r.Seed = NewSeed();
            Guide.SetMood(Guide.Mood.Speaking);
            Guide.Say("Closer to what you said: " + Lower(r.Title) + ".", 3.5f);
            Open(r, how + "-ai-refine");
        }

        public void Handle(string text, string how)
        {
            refineTicket++;
            Trace.Info("Voice", "handling", "how", how, "words", (text ?? "").Split(' ').Length, "interview", interview != null);
            // Answering the room's question: the words fill in the place being described.
            if (interview != null)
            {
                var hc = Words.Parse(text, Current, NewSeed());
                if (hc.Act == Act.End || hc.Act == Act.Undo) { interview = null; Guide.ShowChoices(null, null); Guide.Say("Okay, never mind.", 2f); return; }
                if (interview.Say(text)) { AskNext(); return; }
            }
            // "Run / open <a saved world>" first: a saved name wins over a place it happens to mention.
            string runName = Words.RunName(text);
            if (runName.Length > 0)
            {
                var saved = Progress.Find(runName);
                if (saved != null) { saved.Visits++; var sr = saved.Recipe.Copy(); sr.Title = saved.Name; Guide.Say("Running " + saved.Name + ".", 3f); Open(sr, "saved"); return; }
            }
            if (Mission != null && Mission.AwaitingChoice && Mission.TryWords(text)) return;
            var h = Words.Parse(text, Current, NewSeed());
            Trace.Info("Voice", "understood", "act", h.Act.ToString(), "changed", h.Changed, "ask_ai", h.AskAi, "result", h.Result != null ? h.Result.Line() : "-");
            Guide.SetMood(Guide.Mood.Speaking);
            // A question about the thing in your hand ("how fast could it run?"): Juniper answers on its card.
            // "Take me where it lives" with an animal in hand: its home, where it stands at real size among its herd.
            var homeFor = WantsItsHome(text);
            if (homeFor != null) { GoWhereItLives(homeFor, Wildlife.HomeOf(homeFor.Kind), how); return; }
            var about = AskingAbout(text, h);
            if (about != null) { AskAbout(text, about, how); return; }
            // Someone lives here: "hi, Wren", or a question while you look at them, is to them.
            // "Hello", "what can I do here?" while someone who lives here is with you: to them, not the help card.
            if (h.Act == Act.Help && local != null && local.gameObject.activeSelf && State == Mode.World && how != "voice-name" && !WakeWord.Find(text, out _) &&
                (local.Watched || Time.unscaledTime - localSpokeAt < 12f) && Locals.Answer(local.Def, text).Length > 0)
            { TalkToLocal(text, how); return; }
            if (LocalAddressed(text, h)) { TalkToLocal(text, how); return; }
            // "Hi, deer", "how old is that deer?": an animal out in the world looks up, and Juniper answers for it.
            var animal = AnimalAddressed(text, h);
            if (animal != null) { TalkToAnimal(text, animal, how); return; }
            if (Maker != null && Maker.Things.Count > 0 && h.Act != Act.Edit && h.Act != Act.NewWorld && h.Act != Act.End)
            {
                var change = Wishes.Change(text, Maker.Names());
                if (change != null)
                {
                    bool did = Maker.Change(change, Rooms != null ? Rooms.Room : null);
                    Guide.Say(Maker.Said.Length > 0 ? Maker.Said : !did ? "Nothing to change yet." : change.Remove ? "Gone." : "Done.", Maker.Said.Length > 0 ? 3.5f : 2f);
                    return;
                }
            }
            // "Are you there?", "can you hear me?": an answer, before anything goes to the helper ("One moment..." to
            // a hello felt like not listening).
            if ((h.Act == Act.Unclear || h.Act == Act.None) && IsSmallTalk(text) && (how.StartsWith("voice") || how == "typed"))
            {
                if (Mission != null && Mission.Active) { MaraFirst(IsHello(text)); return; }   // no world list over her call (review 38)
                SmallTalk(text);
                return;
            }
            if (Mission != null && Mission.Committed && (h.Act == Act.NewWorld || h.Act == Act.Surprise || h.Act == Act.Daily || h.Act == Act.LastWorld || h.Act == Act.Run))
            { Guide.Say("Mara needs you right now.\nSay \"take me home\" to stop.", 3f); return; }
            switch (h.Act)
            {
                case Act.Describe: StartInterview(); return;
                case Act.EnterCode: Guide.Say("Type the code in.", 3f); Panel?.ShowCodes(); return;
                case Act.LastWorld:
                    if (Progress.Last != null) { var lr = Progress.Last.Copy(); Open(lr, "last"); }
                    else Guide.Say("This is your first world. Where to?", 3f);
                    return;
                case Act.Run:
                    {
                        var p = Progress.Find(h.SaveName);
                        if (p != null) { p.Visits++; var rr = p.Recipe.Copy(); rr.Title = p.Name; Guide.Say("Running " + p.Name + ".", 3f); Open(rr, "saved"); }
                        else if (h.SaveName.Length > 0) { Apply(Words.Parse(h.SaveName, Current, NewSeed()), how + "-words"); }
                        return;
                    }
                case Act.End: Close("asked"); return;
                case Act.Music: PlayMusic(true); return;
                case Act.Storm: StartMission("asked"); return;
                case Act.Rewind:
                    if (Emergency != null && Emergency.Running) { float.TryParse(h.Arg, out float secs); if (!(h.Arg == "30" ? Emergency.RewindToFirstMiss() : Emergency.DoRewind(secs > 0f ? secs : 30f))) Guide.Say("Nothing to rewind yet.", 2.5f); }
                    else Guide.Say("Rewind works during a storm drill.\nTry \"start a meteor storm\".", 4f);
                    return;
                case Act.Divert: if (Emergency != null && Emergency.Running && Emergency.Divert(h.Arg)) Guide.Say(h.Reply, 2f); else Guide.Say("Hold your palm in the glowing rings to block.", 3f); return;
                case Act.Evade: if (Emergency != null && Emergency.Running && Emergency.DoEvade()) Guide.Say("Evading!", 2f); else Guide.Say("Hold your palm in the glowing rings to block.", 3f); return;
                case Act.Repair:
                    if (Emergency != null && Emergency.Running && Emergency.FirstBroken() >= 0) { int st = Emergency.FirstBroken(); Emergency.DoRepair(st); Guide.Point(Emergency.StationPos(st), 4f); Guide.Say("On it.", 2f); }
                    else Guide.Say("Press Rewind to mend what broke.", 3f);
                    return;
                case Act.MusicOff: PlayMusic(false); return;
                case Act.Sound: SoundCommand(h.Arg, h.Reply); return;
                case Act.Tour: StartTour(); return;
                case Act.Freeze: SetFrozen(true, "asked"); return;
                case Act.Resume: SetFrozen(false, ""); return;
                case Act.Undo: Undo(); return;
                case Act.Save: Save(h.SaveName); return;
                case Act.Help: Guide.Say("", 0.1f); ShowHelp(); return;
                case Act.Daily: OpenDaily(how); return;
                case Act.None:
                    // Called by name but nothing we know: say so, with something that works, instead of going quiet.
                    Sfx.Instance?.Ui("not_understood", 0.5f);
                    Guide.Say("Sorry, I missed that.\nTry \"make it rain\" or \"a beach at sunset\".", 3.5f);
                    return;
            }
            if (Maker != null && !Busy)
            {
                // A world we know by its words stays instant and offline; a thing is made here, or by Juniper's helper.
                // ("Make a fire" is the campfire world; "three shells" are shells, not the beach.)
                bool knownWorld = h.Act == Act.NewWorld && !h.AskAi;
                var wish = Wishes.Read(text);
                if (wish != null && knownWorld && (wish.Kind.Length == 0 || wish.Kind == "fire")) wish = null;
                // "Puppy" on its own is a puppy, unless the words meant a place or a change ("penguins" can be the snow).
                if (wish != null && wish.Bare && h.Act != Act.Unclear) wish = null;
                if (wish != null && wish.Kind.Length > 0)
                {
                    string name = ActAnswer.CleanName(wish.Object);
                    var item = new ActItem { Do = "make", Kind = wish.Kind, Name = name.Length > 0 ? name : wish.Kind.Replace('_', ' '), Place = wish.Place, Count = wish.Count, Size = Wishes.SizeOf(wish.Kind) };
                    int made = Maker.Make(item, Rooms != null ? Rooms.Room : null, Current ?? Recipe.For(Kit.Biomes[0].Id, 1));
                    Log?.Add("made", new Dictionary<string, object> { { "kind", wish.Kind }, { "count", made }, { "by", "app" } });
                    var newest = made == 1 && Maker.Things.Count > 0 ? Maker.Things[Maker.Things.Count - 1] : null;
                    bool comes = newest != null && !taughtCome && Maker.Made > 1 && newest.Size <= 0.7f && LifeSize.IsCreature(newest.Kind);
                    if (comes) taughtCome = true;
                    Guide.Say(made > 1 ? "Here you go." : comes ? "Here. Rest an open hand near it: it comes to you." : "Here: pinch it to move it.", comes ? 4.5f : 3f);
                    TeachFar();
                    return;
                }
                // "Take me to India": a place we don't know is composed as a place, not handed to the helper (which
                // answered with two lanterns).
                bool trip = wish == null && h.AskAi && AppConfig.HasAi && Words.IsTravel(text);
                if (!trip && ActAi.Ready && (wish != null || h.AskAi))
                {
                    AskHelper(text, h, how);
                    return;
                }
            }
            if (h.AskAi && AppConfig.HasAi)
            {
                Guide.SetMood(Guide.Mood.Thinking);
                Guide.Say("Composing that place\u2026", 0f);
                StartCoroutine(RecipeAi.Ask(text, Current, a => OnAi(a, h, how)));
                return;
            }
            if (h.Refine && h.Act == Act.NewWorld && AppConfig.HasAi && (Mission == null || !Mission.Active))
            {
                // The nearest world at once (offline, instant); Claude makes it what was said, a moment later.
                Apply(h, how + "-words");
                int ticket = ++refineTicket;
                StartCoroutine(RecipeAi.Ask(text, null, a => StartCoroutine(Refined(a, ticket, h.Result, how))));
                return;
            }
            // Said to Juniper, with nothing to act on: an answer, not a list of worlds.
            if (h.Act == Act.Unclear && !h.AskAi && (how.StartsWith("voice") || how == "typed"))
            {
                if (Mission != null && Mission.Active) MaraFirst(false); else SmallTalk(text);
                return;
            }
            Apply(h, how + "-words");
        }

        /// <summary>A greeting, or a check that she hears ("are you there?"), as against "okay" or "thanks".</summary>
        static bool IsHello(string text)
        {
            string t = " " + Words.Normalise(text) + " ";
            return t.Contains(" hello ") || t.Contains(" hi ") || t.Contains(" hey ") || t.Contains(" hear ") || t.Contains(" listening ") ||
                   t.Contains(" there ") || t.Contains(" juniper ");
        }

        /// <summary>Words to Juniper during Mara's call: a short answer that sends you back to her.</summary>
        void MaraFirst(bool hello)
        {
            Guide.SetMood(Guide.Mood.Speaking);
            Guide.Say((hello ? "I'm here!\n" : "") + (Mission.Now == Mission.Step.Farewell ? "Say goodbye to Mara first." : "Mara needs you first."), 3f);
            Trace.Info("Voice", "small talk during her call", "hello", hello);
        }

        static readonly HashSet<string> TalkWords = new HashSet<string>(
            ("hello hi hey hiya yo juniper are you there can hear me listening listen is anyone anybody working work does this it " +
             "test testing okay ok yes no hmm um uh what so well please thanks thank").Split(' '));

        /// <summary>Words that are only a greeting or a check that she hears ("are you there?", "can you hear me").</summary>
        static bool IsSmallTalk(string text)
        {
            var words = Words.Normalise(text).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words.Length > 6) return false;
            foreach (var w in words) if (!TalkWords.Contains(w)) return false;
            return true;
        }

        /// <summary>Words with nothing to act on ("are you there?", "hello"): Juniper answers, says what works, and
        /// listens again (Nov 16 on the headset: three such words got a list of worlds, and felt like not listening).</summary>
        void SmallTalk(string text)
        {
            bool hello = IsHello(text);
            Guide.SetMood(Guide.Mood.Speaking);
            Sfx.Instance?.Ui(hello ? "listen_start" : "not_understood", 0.5f);
            Guide.Say(hello ? "I'm here, and I can hear you!\nTell me a place, or ask me to make something."
                            : "I didn't catch that.\nTell me a place, or ask me to make something.", 0f);
            Guide.ShowChoices(new[] { "A beach at sunset", "Make it rain", "Make me a lion" },
                              new[] { "say:a beach at sunset", "say:make it rain", "say:make me a lion" });
            Trace.Info("Voice", "small talk answered", "hello", hello);
            if (Ears != null && Ears.On) { Ears.Listen(7f); followUpUntil = Time.unscaledTime + 7f; }
        }

        void Apply(Heard h, string how)
        {
            switch (h.Act)
            {
                case Act.NewWorld:
                case Act.Surprise:
                    Guide.Say(h.Reply, 3f);
                    if (State == Mode.World) Open(h.Result, how); else Open(h.Result, how);
                    return;
                case Act.Edit:
                    Guide.Say(h.Reply, 3f);
                    if (State == Mode.World) Edit(h.Result, how); else Open(h.Result, how);
                    return;
                default:
                    Guide.SetMood(Guide.Mood.Idle);
                    Sfx.Instance?.Ui("not_understood", 0.6f);
                    ShowChoices(h.Question.Length > 0 ? h.Question : "Where to?", h.Choices);
                    return;
            }
        }

        /// <summary>The animal in hand (or looked at) whose home the words ask for, or null.</summary>
        Maker.Thing WantsItsHome(string text)
        {
            if (Maker == null || Busy) return null;
            string t = " " + Words.Normalise(text) + " ";
            bool named = t.Contains(" where it lives ") || t.Contains(" where they live ") || t.Contains(" its home ") || t.Contains(" show me its world ");
            bool there = t.Contains(" take me there ");
            if (!named && !there) return null;
            if (Words.IsQuestion(text) && !t.Contains(" take me ") && !t.Contains(" show me ")) return null;   // "where does it live?" is a question
            var th = Inspect != null ? Inspect.Looking : null;
            for (int i = 0; i < 3 && th == null; i++) th = Maker.HeldBy(i);
            // "Take me there" is about the animal in your hand; "where it lives" can mean the one you made last.
            if (th == null && named) th = Maker.Last();
            return th != null && Wildlife.HomeOf(th.Kind).Length > 0 ? th : null;
        }

        /// <summary>Opens the place a held animal lives; there it is let go at its real size, among its own kind.</summary>
        void GoWhereItLives(Maker.Thing t, string place, string how)
        {
            var p = Places.Get(place);
            if (t == null || p == null || Busy) return;
            if (Mission != null && Mission.Committed) { Guide.Say("Mara needs you right now.\nSay \"take me home\" to stop.", 3f); return; }
            Log?.Add("home", new Dictionary<string, object> { { "kind", t.Kind }, { "place", place }, { "how", how } });
            Trace.Info("Director", "where it lives", "kind", t.Kind, "place", place);
            Guide.Say("Let's see where it lives.", 3f);
            if (Builder != null) Builder.Herd = t.Kind;
            homeVisit = true;
            Open(p.Recipe(NewSeed()), how + "-home");
            StartCoroutine(ArriveHome(t, p, visit));
        }

        IEnumerator ArriveHome(Maker.Thing t, Places.Place p, int v)
        {
            float until = Time.unscaledTime + 25f;
            while (Time.unscaledTime < until && !(State == Mode.World && Current != null && Current.Look != null && Current.Look.Name == p.Look.Name)) yield return null;
            if (Builder != null) Builder.Herd = "";
            if (State != Mode.World || Maker == null || t.T == null || !Maker.Things.Contains(t)) yield break;
            yield return new WaitForSecondsRealtime(0.8f);
            Maker.LifeSizeOf(t, Rooms != null ? Rooms.Room : null);
            string name = LifeSize.Title(t.Name, t.Kind);
            Guide.SetMood(Guide.Mood.Speaking);
            Guide.Say(name + ", at home: " + p.Look.Name.ToLowerInvariant() + ".\n" + (Maker.Said.Length > 0 ? Maker.Said : ""), 6f);
            if (firstAnimal == t && !Progress.Onboarded)
            {
                firstAnimal = null;
                if (Inspect != null) Inspect.FirstHint = "";
                Trace.Info("Tutorial", "step", "n", 4, "what", "at home", "kind", t.Kind);
                Log?.Add("tutorial", new Dictionary<string, object> { { "step", "first animal at home" }, { "kind", t.Kind } });
                yield return new WaitForSecondsRealtime(4.5f);
                if (State != Mode.World || Current == null || Current.Look == null || Current.Look.Name != p.Look.Name) yield break;
                Sfx.Instance?.Ui("listen_start", 0.6f);
                Guide.SetMood(Guide.Mood.Speaking);
                Guide.Say("Listen\u2026 someone is calling from a campfire.\nA pilot, lost. She needs you.", 0f);
                Guide.ShowChoices(new[] { "Answer her call", "Stay a while" }, new[] { "first:campfire", "first:stay" });
            }
        }

        // ---------- someone's in it ----------

        Local local;
        Caption localCaption;
        float localChipsAt = -10f, localSpokeAt = -10f, localMetAt = -1f;
        bool dockAtEdge;
        int waveTaught;
        /// <summary>The local of the world you are in (null in your room, or before the world is built).</summary>
        public Local Local => local;
        /// <summary>What the local last said (for tests).</summary>
        public string LocalSaid => localCaption != null ? localCaption.Current : "";

        bool LocalChipsShown()
        {
            foreach (var b in PokeButton.Live) if (b != null && b.Id == "Choice local:who") return true;
            return false;
        }

        /// <summary>Every frame: the local hides while Mara's call or a storm is on, says hello when first looked at, and offers three questions while you look.</summary>
        void TickLocal()
        {
            TickFavour();
            if (State != Mode.World || Builder == null || Builder.Root == null)
            {
                if (local != null) { local = null; if (localCaption != null) localCaption.Clear(); }
                return;
            }
            if (local == null)
            {
                local = Builder.Root.GetComponentInChildren<Local>(true);
                localMetAt = -1f;
                favourKind = ""; favourMadeAt = Maker != null ? Maker.Made : -1;   // what was made before is not for them
                if (local == null) return;
                if (localCaption != null) localCaption.Clear();
            }
            bool hide = (Mission != null && Mission.Active) || StormOn;
            if (local.gameObject.activeSelf == hide) local.gameObject.SetActive(!hide);
            if (hide) { if (localCaption != null && localCaption.Showing) localCaption.Clear(); return; }
            if (localCaption != null && localCaption.Showing) localCaption.transform.position = LocalCaptionSpot();
            if (Busy) return;
            // They say hello first: when you look their way (a wide cone), or a few seconds after you arrive, with a
            // chime from where they stand. (In six worlds on the headset nobody was greeted: it took a 13-degree stare.)
            if (!local.Greeted && localMetAt < 0f) localMetAt = Time.unscaledTime;
            float met = Time.unscaledTime - localMetAt;
            // Juniper finishes her line first (up to 6 s): two people talking at once is read by nobody.
            bool juniperTalking = Guide != null && Guide.Words != null && Guide.Words.Fresh && met < 6f;
            if (!local.Greeted && Time.unscaledTime > callComingUntil && !juniperTalking && (local.Watched || (met > 1.5f && local.InView(35f)) || met > 4f))
            {
                local.Greeted = true;
                // The first two hellos teach the wave (nothing else in the app says how).
                bool teach = waveTaught < 2 && Hands != null && Hands.AnyHandTracked;
                if (teach) waveTaught++;
                // They remember you: a visit before this one, the favour you did, the last thing you made here.
                string key = Locals.MemoryKey(local.Def);
                bool back = Progress.LocalVisits(key) > 0;
                string hello = back ? Locals.WelcomeBack(local.Def, Progress.FavourDone(key), Progress.LocalLastMade(key)) : local.Def.Hello;
                Progress.LocalMet(key);
                SaveProgress();
                LocalSays(hello + (teach ? "\nWave back: an open hand, held up." : ""), teach ? 8f : 6f);
                if (back) Trace.Info("Local", "remembers you", "name", local.Def.Name, "visits", Progress.LocalVisits(key), "favour", Progress.FavourDone(key));
                Sfx.Instance?.At("ui_hover", local.HeadTop, 0.5f);
                Log?.Add("local", new Dictionary<string, object> { { "id", local.Def.Id }, { "what", "hello" } });
                Trace.Info("Local", "says hello", "name", local.Def.Name, "world", Current != null ? Current.Line() : "-");
            }
            // Three things to ask, within reach, while you look at them (after the introduction; never over other choices).
            bool ours = LocalChipsShown();
            if (local.Watched) localChipsAt = Time.unscaledTime;
            // While you gather, the pieces float where their questions would go, and a hand reaching for a piece
            // pressed them (Tove's row, Nov 15): the questions wait until the pieces are on the table.
            bool task = (Gather != null && (Gather.AnyHeld || Gather.Running)) || (Campfire != null && Campfire.Held);
            // One row at a time: a thing held up to your face has its own card and buttons, and their questions make
            // way for it (Wren's row was printed over the card in the Nov 15 captures, with Wren behind the thing).
            bool inspecting = Inspect != null && Inspect.Looking != null;
            if (local.Watched && local.Greeted && Progress.Onboarded && !task && !CarryingNow && !inspecting && !Guide.HasChoices && (Type == null || !Type.Open) && (Panel == null || !Panel.Open))
            {
                // Until it is done, the third question is their favour.
                bool favour = !Progress.FavourDone(Locals.MemoryKey(local.Def)) && Maker != null;
                Guide.ShowChoices(new[] { "Who are you?", "What is this place?", favour ? "Can I help?" : "What can I do here?" },
                                  new[] { "local:who", "local:here", favour ? "local:favour" : "local:tip" }, local.HeadTop);
            }
            else if (ours && (task || CarryingNow || inspecting || Time.unscaledTime - localChipsAt > 10f)) Guide.ShowChoices(null, null);
        }

        /// <summary>You waved with the local in front of you: they wave back, and say hello if they hadn't yet.</summary>
        bool WaveToLocal()
        {
            if (State != Mode.World || local == null || !local.gameObject.activeSelf || !local.InView(50f)) return false;
            local.Wave();
            if (!local.Greeted) { local.Greeted = true; LocalSays(local.Def.Hello, 6f); }
            else LocalSays("Hello again!", 3f);
            Sfx.Instance?.At("ui_hover", local.HeadTop, 0.5f);
            Log?.Add("local", new Dictionary<string, object> { { "id", local.Def.Id }, { "what", "wave" } });
            Trace.Info("Local", "waves back", "name", local.Def.Name);
            return true;
        }

        void LocalSays(string text, float seconds)
        {
            if (local == null || Guide == null || string.IsNullOrEmpty(text)) return;
            local.Nod();
            if (localCaption == null)
            {
                localCaption = Caption.Make("Local says", transform, Guide.Font, Guide.Panel, Head, 15f, 26f);
                localCaption.Priority = 5;
                localCaption.LineChars = 32;
            }
            localCaption.transform.position = LocalCaptionSpot();
            localCaption.Say(local.Def.Name + "\n" + text, seconds, new Color(1f, 0.93f, 0.8f));
            localSpokeAt = Time.unscaledTime;
        }

        /// <summary>The most a local's words sit off the middle of your view, left or right (look-3: Wren, standing
        /// well to one side, had his words run off the edge).</summary>
        public const float LocalCaptionMaxDeg = 20f;

        /// <summary>Above the local's head, but drawn in towards the middle of your view when they stand off to one side.</summary>
        Vector3 LocalCaptionSpot()
        {
            Vector3 at = local.HeadTop;
            if (Head == null) return at;
            Vector3 to = at - Head.position;
            Vector3 flat = new Vector3(to.x, 0f, to.z), f = new Vector3(Head.forward.x, 0f, Head.forward.z);
            if (flat.sqrMagnitude < 1e-4f || f.sqrMagnitude < 1e-4f) return at;
            float yaw = Vector3.SignedAngle(f, flat, Vector3.up);
            // Behind you the side would flip with every small head movement: it keeps the side it was on.
            if (Mathf.Abs(yaw) > 160f && captionSide != 0) yaw = captionSide * Mathf.Abs(yaw);
            captionSide = yaw >= 0f ? 1 : -1;
            float cy = Mathf.Clamp(yaw, -LocalCaptionMaxDeg, LocalCaptionMaxDeg);
            // Up and down: never more than this far above where you look (heads are down at the table while gathering).
            float dist = flat.magnitude;
            float headPitch = Mathf.Asin(Mathf.Clamp(Head.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(to.y, dist) * Mathf.Rad2Deg;
            float cp = Mathf.Min(pitch, headPitch + LocalCaptionUpDeg);
            if (Mathf.Abs(cy - yaw) < 0.01f && Mathf.Abs(cp - pitch) < 0.01f) return at;
            if (Mathf.Abs(cp - pitch) >= 0.01f) dist = Mathf.Min(dist, 1.4f);   // drawn in, nearer, so it isn't sunk in the ground
            Vector3 p = Head.position + Quaternion.AngleAxis(cy, Vector3.up) * f.normalized * dist + Vector3.up * Mathf.Tan(cp * Mathf.Deg2Rad) * dist;
            // Never down in the table or the floor.
            float low = Rooms != null && Rooms.Room != null && Rooms.Room.Table != null ? Rooms.Room.Table.TopY + 0.15f : Head.position.y - 0.4f;
            p.y = Mathf.Max(p.y, Mathf.Min(low, Head.position.y - 0.1f));
            return p;
        }

        /// <summary>The most a local's words sit above where you look.</summary>
        public const float LocalCaptionUpDeg = 35f;
        int captionSide;

        /// <summary>Where the local's words are (for tests).</summary>
        public Vector3 LocalCaptionAt => localCaption != null ? localCaption.transform.position : Vector3.zero;

        string favourKind = "", favourKey = "";
        int favourMadeAt = -1;
        /// <summary>The favour asked of you in this world ("" for none; for tests).</summary>
        public string FavourWanted => favourKind;

        /// <summary>They ask their favour; touching "Make …" (or saying it) makes it, and they thank you.</summary>
        void AskFavour()
        {
            var f = Locals.FavourOf(local.Def);
            if (f == null || Maker == null) return;
            favourKind = f.Kind; favourKey = Locals.MemoryKey(local.Def); favourMadeAt = Maker.Made;
            LocalSays(f.Ask, 8f);
            Guide.ShowChoices(new[] { "Make " + f.Name, "Not now" }, new[] { "say:make me " + f.Name, "local:later" }, local.HeadTop);
            Log?.Add("local", new Dictionary<string, object> { { "id", local.Def.Id }, { "what", "favour asked" }, { "kind", f.Kind } });
            Trace.Info("Local", "asks a favour", "name", local.Def.Name, "kind", f.Kind);
        }

        /// <summary>Every frame in a world: the favour done when the thing is made (by touch, voice or typing), and the
        /// last thing made here remembered.</summary>
        void TickFavour()
        {
            if (Maker == null || State != Mode.World || local == null) { if (State != Mode.World) favourKind = ""; return; }
            if ((Mission != null && Mission.Active) || StormOn) { favourMadeAt = Maker.Made; return; }   // not "made here" for them
            if (Maker.Made == favourMadeAt || Maker.Things.Count == 0) return;
            var t = Maker.Things[Maker.Things.Count - 1];
            favourMadeAt = Maker.Made;
            string key = Locals.MemoryKey(local.Def);
            Progress.LocalMade(key, (t.Name ?? t.Kind).Replace('_', ' '));
            if (favourKind.Length > 0 && key == favourKey && t.Kind == favourKind)
            {
                var f = Locals.FavourOf(local.Def);
                favourKind = "";
                Progress.LocalFavour(key);
                local.Notice();
                LocalSays(f.Thanks, 6f);
                Sfx.Instance?.At("souvenir_chime", local.HeadTop, 0.8f);
                Log?.Add("local", new Dictionary<string, object> { { "id", local.Def.Id }, { "what", "favour done" }, { "kind", t.Kind } });
                Trace.Info("Local", "favour done", "name", local.Def.Name, "kind", t.Kind);
            }
            SaveProgress();
        }

        void LocalChoice(string what)
        {
            if (local == null) return;
            if (what == "favour") { local.Notice(); AskFavour(); return; }
            if (what == "later") { Guide.ShowChoices(null, null); LocalSays("Whenever you like.", 3f); return; }
            local.Notice();
            LocalSays(what == "who" ? local.Def.Who : what == "tip" ? local.Def.Tip : local.Def.Here, 8f);
            Log?.Add("local", new Dictionary<string, object> { { "id", local.Def.Id }, { "what", what } });
        }

        /// <summary>The words are to the local: they were named, or you are looking at them (or they just spoke) and ask or greet.</summary>
        bool LocalAddressed(string text, Heard h)
        {
            if (local == null || !local.gameObject.activeSelf || State != Mode.World) return false;
            if (h != null && h.Act != Act.Unclear && h.Act != Act.None) return false;
            if (Locals.AskedTo(text).Length > 0) return true;   // "ask him if he ever found treasure"
            bool own = Locals.Answer(local.Def, text).Length > 0;
            if (Locals.Named(local.Def, text)) return own || Words.IsQuestion(text);
            bool with = local.Watched || Time.unscaledTime - localSpokeAt < 12f;
            if (!with) return false;
            if (Inspect != null && Inspect.Looking != null) return false;   // a question about the thing in your hand
            if (Wishes.Read(text) != null) return false;                    // "can you make a horse" is a wish
            return own || Words.IsQuestion(text);
        }

        void TalkToLocal(string text, string how)
        {
            local.Notice();
            local.Greeted = true;
            string own = Locals.Answer(local.Def, text);
            Log?.Add("local", new Dictionary<string, object> { { "id", local.Def.Id }, { "what", own.Length > 0 ? "own" : "asked" }, { "how", how } });
            Trace.Info("Local", "spoken to", "name", local.Def.Name, "own_line", own.Length > 0);
            if (own.Length > 0) { LocalSays(own, 8f); return; }
            if (!ActAi.Ready) { LocalSays(local.Def.Here, 8f); return; }
            LocalSays("Hmm\u2026", 0f);
            var who = local;
            string place = Current != null ? (Current.Look != null ? Current.Look.Name : Current.Def.Name) : "";
            StartCoroutine(ActAi.Ask(text, Current, Maker != null ? Maker.Names() : new List<string>(), new List<string> { "front" }, a =>
            {
                if (local != who) return;
                Trace.Info("AI", "local answer", "ok", a.Error == null, "ms", Math.Round(a.Ms), "chars", a.Answer.Length);
                LocalSays(a.Answer.Length > 0 ? a.Answer : who.Def.Here, 10f);
            }, "", Locals.Persona(local.Def, place)));
        }

        static readonly string[] Greetings = { " hi ", " hello ", " hey ", " hiya ", " good morning ", " good evening ", " talk to me ", " come here " };

        /// <summary>
        /// The animal out in the world the words are to: named ("hi, deer", "how fast is that zebra?"), or the one
        /// you are watching when you ask a question. Null when nothing living is there or the words aren't to it.
        /// </summary>
        public Wild AnimalAddressed(string text, Heard h)
        {
            if (Builder == null || Builder.Root == null || State != Mode.World || Head == null) return null;
            if (h != null && h.Act != Act.Unclear && h.Act != Act.None) return null;
            string t = " " + Words.Normalise(text) + " ";
            bool greet = false;
            foreach (var g in Greetings) if (t.Contains(g)) greet = true;
            bool question = Words.IsQuestion(text);
            if (!greet && !question) return null;
            Wild best = null, watched = null;
            float bestD = float.MaxValue;
            foreach (var w in Builder.Root.GetComponentsInChildren<Wild>())
            {
                if (w.Watching) watched = w;
                bool named = false;
                foreach (var m in MadeKinds.All)
                    if (m.Kind == w.Kind)
                        foreach (var word in m.Words)
                            if (t.Contains(" " + word + " ") || t.Contains(" " + word + "s ")) named = true;
                if (!named) continue;
                float d = Vector3.Distance(w.transform.position, Head.position);
                if (d < bestD) { bestD = d; best = w; }
            }
            return best ?? (question ? watched : null);
        }

        void TalkToAnimal(string text, Wild w, string how)
        {
            w.Greet();
            string name = w.Kind.Replace('_', ' ');
            string fact = LifeSize.Fact(w.Kind);
            Log?.Add("talked", new Dictionary<string, object> { { "kind", w.Kind }, { "how", how } });
            Trace.Info("Juniper", "spoke to an animal", "kind", w.Kind, "question", Words.IsQuestion(text));
            if (!Words.IsQuestion(text) || !ActAi.Ready)
            {
                Guide.SetMood(Guide.Mood.Speaking);
                Guide.Say("The " + name + " looks up at you." + (fact.Length > 0 ? "\n" + fact : ""), 6f);
                return;
            }
            Guide.SetMood(Guide.Mood.Thinking);
            Guide.Say("Let me think\u2026", 0f);
            StartCoroutine(ActAi.Ask(text, Current, Maker != null ? Maker.Names() : new List<string>(), new List<string> { "front" }, a =>
            {
                string said = a.Answer.Length > 0 ? a.Answer : fact.Length > 0 ? fact : "It just looks at you.";
                Guide.SetMood(Guide.Mood.Speaking);
                Guide.Say(said, 9f);
            }, name));
        }

        /// <summary>The thing held up or held that a question is about, or null when the words aren't one.</summary>
        Maker.Thing AskingAbout(string text, Heard h)
        {
            if (Maker == null || Busy || !Words.IsQuestion(text)) return null;
            if (h.Act != Act.Unclear && h.Act != Act.None && h.Act != Act.Edit) return null;
            if (Wishes.Read(text) != null || Wishes.Change(text, Maker.Names()) != null) return null;
            var t = Inspect != null ? Inspect.Looking : null;
            for (int i = 0; i < 2 && t == null; i++) t = Maker.HeldBy(i);
            return t != null && t.T != null ? t : null;
        }

        /// <summary>A question about the thing in your hand: Claude answers it (online), or Juniper gives its fact.</summary>
        void AskAbout(string text, Maker.Thing t, string how)
        {
            Maker.Touch(t);
            string known = LifeSize.Fact(t.Kind);
            string fallback = known.Length > 0 ? known : "I don't know much about that one yet.";
            Log?.Add("asked", new Dictionary<string, object> { { "kind", t.Kind.Length > 0 ? t.Kind : "built" }, { "how", how } });
            if (!ActAi.Ready) { AnswerAbout(t, fallback, "fact"); return; }
            Guide.SetMood(Guide.Mood.Thinking);
            if (Inspect == null || !Inspect.ShowAnswer(t, "Let me think\u2026")) Guide.Say("Let me think\u2026", 0f);
            StartCoroutine(ActAi.Ask(text, Current, Maker.Names(), new List<string> { "front" }, a =>
            {
                Trace.Info("AI", "question answer", "ok", a.Error == null, "ms", Math.Round(a.Ms), "chars", a.Answer.Length);
                if (a.Error == null) Health.Ok(Health.Ai, a.Source, Time.realtimeSinceStartup);
                string said = a.Answer.Length > 0 ? a.Answer : a.Declined && a.Say.Length > 0 ? a.Say : fallback;
                AnswerAbout(t, said, a.Answer.Length > 0 ? "model" : "fact");
            }, t.Name));
        }

        void AnswerAbout(Maker.Thing t, string text, string source)
        {
            Guide.SetMood(Guide.Mood.Speaking);
            if (Inspect == null || !Inspect.ShowAnswer(t, text)) Guide.Say(text, 8f);
            Trace.Info("Juniper", "answered about a thing", "name", t.Name, "source", source, "chars", text.Length);
        }

        /// <summary>Asks Juniper's helper what to do about a sentence the app doesn't know by itself.</summary>
        void AskHelper(string text, Heard local, string how)
        {
            Guide.SetMood(Guide.Mood.Thinking);
            Guide.Say("One moment…", 0f);
            Maker.Working = true;
            var places = new List<string> { "front", "floor", "left", "right", "there" };
            var room = Rooms != null ? Rooms.Room : null;
            if (room != null)
            {
                if (room.Table != null) places.Add("table");
                foreach (var p in room.Pieces)
                {
                    if (p.Virtual) continue;
                    string place = p.Label == "COUCH" ? "sofa" : p.Label == "STORAGE" ? "shelf" : p.Label == "BED" ? "bed" : "";
                    if (place.Length > 0 && !places.Contains(place)) places.Add(place);
                }
            }
            helperText = text;
            StartCoroutine(ActAi.Ask(text, Current, Maker.Names(), places, a => OnAct(a, local, how)));
        }

        void OnAct(ActAnswer a, Heard local, string how)
        {
            Maker.Working = false;
            Log?.Add("ai", new Dictionary<string, object>
            {
                { "ok", a.Error == null }, { "ms", Math.Round(a.Ms) }, { "source", a.Source }, { "declined", a.Declined }, { "rid", a.Rid ?? "" },
                { "actions", a.Actions.Count }, { "error", a.Error ?? "" }
            });
            Trace.Info("AI", "act answer", "ok", a.Error == null, "ms", Math.Round(a.Ms), "source", a.Source, "declined", a.Declined, "actions", a.Actions.Count, "error", a.Error ?? "");
            if (a.Declined)
            {
                Health.Ok(Health.Ai, a.Source, Time.realtimeSinceStartup);
                Guide.SetMood(Guide.Mood.Speaking);
                Guide.Say(a.Say.Length > 0 ? a.Say : "Let's make something calmer.", 3.5f);
                return;
            }
            if (a.Error != null || (a.Actions.Count == 0 && a.Say.Length == 0))
            {
                Health.Set(Health.Ai, Health.Level.Fallback, a.Error ?? "no actions", Time.realtimeSinceStartup);
                // The words we did understand still count.
                if (local.Act == Act.Edit || local.Act == Act.NewWorld) { Apply(local, how + "-words"); return; }
                Guide.Say("I can't make that one right now. Here's what I can do:", 0f);
                ShowChoices(null);
                return;
            }
            Health.Ok(Health.Ai, a.Source, Time.realtimeSinceStartup);
            Guide.SetMood(Guide.Mood.Speaking);
            // "A thunderstorm" is weather: only meteors start Mara's meteor storm (a misheard thunderstorm began one).
            string heardNow = " " + Words.Normalise(helperText ?? "") + " ";
            bool meteors = heardNow.Contains(" meteor") || heardNow.Contains(" asteroid") || heardNow.Contains(" drill ") || heardNow.Contains(" mara ");
            if (!meteors && local != null && local.Act == Act.Edit && a.Actions.Count == 1 && a.Actions[0].Do == "event" && a.Actions[0].Event == "storm")
            {
                Trace.Info("AI", "a weather storm, not the meteor storm: the words understood here stand");
                Apply(local, how + "-words");
                return;
            }
            int made = 0;
            bool any = false;
            foreach (var it in a.Actions) if (it.Do == "make") any = true;
            Guide.Say(a.Say.Length > 0 ? a.Say : any ? "Here you go." : "Done.", 3.5f);
            foreach (var it in a.Actions)
            {
                switch (it.Do)
                {
                    case "world":
                        if (Busy || it.Recipe == null) break;
                        if (State == Mode.World && Current != null && it.Recipe.Biome == Current.Biome)
                        {
                            it.Recipe.Seed = Current.Seed;
                            // A change to where you are keeps where you are: India at night is still India, with its landmark.
                            if (it.Recipe.Look == null && Current.Look != null)
                            {
                                it.Recipe.Look = Current.Look.Copy();
                                Trace.Info("AI", "world change keeps the place", "place", Current.Look.Name);
                            }
                            Edit(it.Recipe, how + "-ai");
                        }
                        else { it.Recipe.Seed = NewSeed(); Open(it.Recipe, how + "-ai"); }
                        break;
                    case "make":
                        // Within reach unless you asked for somewhere else: the floor is out of reach when seated.
                        if (it.Place == "floor" && !AskedForFloor(helperText)) it.Place = Rooms != null && Rooms.Room != null && Rooms.Room.Table != null ? "table" : "front";
                        made += Maker.Make(it, Rooms != null ? Rooms.Room : null, Current ?? Recipe.For(Kit.Biomes[0].Id, 1));
                        break;
                    case "change": Maker.Change(it, Rooms != null ? Rooms.Room : null); break;
                    case "event":
                        if (it.Event == "storm" && !meteors) { Trace.Info("AI", "meteor storm not started: nobody said meteors"); break; }
                        DoEvent(it.Event);
                        break;
                }
            }
            if (made > 0) Log?.Add("made", new Dictionary<string, object> { { "kind", "built" }, { "count", made }, { "by", "helper" } });
            if (made > 0) TeachFar();
        }

        /// <summary>Something made is out of reach: say how to have it, the first few times (until it has been done twice).</summary>
        void TeachFar()
        {
            if (Maker == null || Head == null || Hints.Instance == null || !Hints.Instance.Wants("point", 2)) return;
            bool far = false;
            foreach (var t in Maker.Things) if (t.T != null && Vector3.Distance(Maker.Centre(t), Head.position) > 0.75f) far = true;
            if (!far) return;
            Trace.Info("Maker", "something is out of reach: said how to bring it");
            StartCoroutine(SayLater(3f, "Too far to reach?\nPoint at it and close your hand:\nit comes to you.", 6f));
        }

        string helperText = "";
        bool scanAnswered;

        /// <summary>The player's own words put the thing on the floor or the ground.</summary>
        static bool AskedForFloor(string said)
        {
            string t = " " + Words.Normalise(said ?? "") + " ";
            return t.Contains(" floor ") || t.Contains(" ground ") || t.Contains(" on the rug ");
        }

        /// <summary>The events Juniper's helper may start: each one is something the player could also do by hand.</summary>
        void DoEvent(string ev)
        {
            switch (ev)
            {
                case "storm": if (State == Mode.World && !Busy) StartStorm("asked"); break;
                case "rewind": if (Emergency != null && Emergency.Running) Emergency.RewindToFirstMiss(); break;
                case "music": PlayMusic(true); break;
                case "music_off": PlayMusic(false); break;
                case "freeze": if (State == Mode.World) SetFrozen(true, "asked"); break;
                case "resume": SetFrozen(false, ""); break;
                case "next_world": if (Mission == null || !Mission.Committed) OnDock("next"); break;
                case "my_room": Close("asked"); break;
                case "quiet": SoundCommand("peace", "Peace and quiet."); break;
                case "mute": SoundCommand("mute", "Muted."); break;
                case "sound_on": SoundCommand("on", "Sound's back."); break;
                case "louder": SoundCommand("up", "Louder."); break;
                case "tour": StartTour(); break;
            }
        }

        void OnAi(AiAnswer a, Heard local, string how)
        {
            Log?.Add("ai", new Dictionary<string, object>
            {
                { "ok", a.Error == null }, { "ms", Math.Round(a.Ms) }, { "source", a.Source }, { "declined", a.Declined }, { "rid", a.Rid ?? "" },
                { "clarify", a.Clarify.Length > 0 }, { "error", a.Error ?? "" }
            });
            Trace.Info("AI", "answer", "ok", a.Error == null, "ms", Math.Round(a.Ms), "source", a.Source, "declined", a.Declined,
                       "clarify", a.Clarify.Length > 0, "recipe", a.Recipe != null ? a.Recipe.Line() : "-", "error", a.Error ?? "");
            if (a.Error != null || a.Recipe == null)
            {
                Health.Set(Health.Ai, Health.Level.Fallback, a.Error ?? "no recipe", Time.realtimeSinceStartup);
                // The words we did understand still count.
                if (local.Act == Act.Edit || local.Act == Act.NewWorld) { Apply(local, how + "-words"); return; }
                Guide.Say("I don't have that one yet. Here's what I can build:", 0f);
                ShowChoices(null, a.Choices.Length > 0 ? a.Choices : null);
                return;
            }
            Health.Ok(Health.Ai, a.Source, Time.realtimeSinceStartup);
            var r = a.Recipe;
            r.Seed = NewSeed();
            if (a.Declined) { Guide.Say("Let's go somewhere calmer.", 3f); }
            if (a.Clarify.Length > 0)
            {
                Guide.Say(a.Clarify, 0f);
                ShowChoices(null, a.Choices.Length > 0 ? a.Choices : new[] { r.Biome });
                return;
            }
            Guide.Say("Here's " + Lower(r.Title) + ".", 3f);
            Open(r, how + "-ai");
        }

        static string Lower(string s) => string.IsNullOrEmpty(s) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

        void ShowChoices(string question, string[] biomes = null)
        {
            if (Guide == null) return;
            var ids = new List<string>();
            var labels = new List<string>();
            if (biomes != null) foreach (var b in biomes) { var d = Kit.Get(b); if (d != null && ids.Count < 3) { ids.Add(b); labels.Add(d.Short); } }
            if (ids.Count == 0)
            {
                ids.Add("today"); labels.Add("Today's world");
                ids.Add("describe"); labels.Add("Describe a place");
                if (Progress.Last != null && State == Mode.Room) { ids.Add("last"); labels.Add("Last: " + Progress.Last.Def.Short); }
                ids.Add("surprise"); labels.Add("Surprise me");
                string other = SuitsNow();
                if ((Current != null && other == Current.Biome) || other == Recipe.Daily(DateTime.UtcNow).Biome)
                    other = Kit.Biomes[(Kit.IndexOf(other) + 1 + Progress.Launches) % Kit.Biomes.Length].Id;
                ids.Add(other); labels.Add(Kit.Get(other).Short);
            }
            if (question != null) Guide.Say(question, 0f);
            Guide.SetMood(Guide.Mood.Idle);
            Guide.ShowChoices(labels.ToArray(), ids.ToArray());
        }

        void OnChoice(string id)
        {
            askedAt = Time.unscaledTime;
            if (id.StartsWith("type:", StringComparison.Ordinal)) { Type?.Pick(id.Substring(5), "typed-suggestion"); return; }
            Trace.Info("Guide", "choice", "id", id, "interview", interview != null);
            if (id.StartsWith("ask:", StringComparison.Ordinal) && interview != null)
            {
                if (int.TryParse(id.Substring(4), out int k)) interview.Choose(k);
                AskNext();
                return;
            }
            if (!Progress.Onboarded && State == Mode.Room && (id == "today" || id == "surprise"))
            {
                Open(Recipe.For("campfire", 1), "first");
                return;
            }
            if (id.StartsWith("m:", StringComparison.Ordinal)) { Mission?.Pick(id); return; }
            if (id == "storm:rewind") { Guide.ShowChoices(null, null); Emergency?.DoRewind(60f); return; }
            if (id == "storm:again") { Guide.ShowChoices(null, null); Emergency?.Stop(); StartStorm("again"); return; }
            if (id == "storm:done") { Guide.ShowChoices(null, null); Emergency?.Stop(); Guide.Say("Stay as long as you like.", 3f); return; }
            if (id == "voice:on") { Guide.ShowChoices(null, null); SetVoice(true); return; }
            if (id.StartsWith("welcome:", StringComparison.Ordinal)) { Guide.ShowChoices(null, null); welcomeChoice = id.Substring(8); return; }
            if (id.StartsWith("first:", StringComparison.Ordinal)) { FirstChoice(id.Substring(6)); return; }
            if (id == "new:yes") { StartCoroutine(NewPlayer()); return; }
            if (id == "new:no") { Guide.Say("Kept.", 2f); return; }
            if (id.StartsWith("local:", StringComparison.Ordinal)) { LocalChoice(id.Substring(6)); return; }
            if (id.StartsWith("say:", StringComparison.Ordinal)) { string w = id.Substring(4); if (w != "no") Handle(w, "suggested"); else Guide.Say("Any time: touch me, and name a place.", 3f); return; }
            if (id == "flight:today") { Guide.ShowChoices(null, null); flightVisit = true; Log?.Add("flight", new Dictionary<string, object> { { "how", "greeting" } }); OpenDaily("flight"); return; }
            if (id == "flight:no") { Guide.ShowChoices(null, null); Guide.Say("She'll keep her radio on.\nTouch me, or look at me, for the map.", 4f); return; }
            if (id == "scan:yes" || id == "scan:no") scanAnswered = true;
            if (id == "scan:yes") { Guide.ShowChoices(null, null); Guide.Say("Opening Quest's room setup\u2026", 3f); if (Rooms != null) _ = Rooms.Load(true); return; }
            if (id == "scan:no") { Guide.ShowChoices(null, null); Guide.Say("Any time: Menu, Use my furniture.", 3f); return; }
            if (id == "voice:off") { Guide.ShowChoices(null, null); Progress.VoiceOffered = true; SaveProgress(); Guide.Say("Okay. It's in the menu if you change your mind.", 3f); return; }
            if (id == "describe") { StartInterview(); return; }
            if (id == "last" && Progress.Last != null) { Open(Progress.Last.Copy(), "last"); return; }
            if (id == "today") { OpenDaily("chip"); return; }
            if (id == "surprise") { Apply(Words.Parse("surprise me", Current, NewSeed()), "chip"); return; }
            if (Kit.Get(id) != null) Open(Recipe.For(id, NewSeed()), "chip");
        }

        bool inFirstRun;

        /// <summary>
        /// The first launch teaches itself in three short beats, with nothing to read beforehand:
        /// 1. Juniper says hello and asks to see your hands (moves on as soon as it sees one, or after 6 s with the
        ///    look-to-press hint); 2. the world map opens with the campfire pulsing: "touch a world, or look at it"
        ///    (choosing teaches pressing); 3. in the world, the build teaches pinching, and the palm menu comes after.
        /// </summary>
        string welcomeChoice = "";
        /// <summary>The welcome card is up (for tests).</summary>
        public bool Welcoming { get; private set; }

        /// <summary>
        /// The welcome card: stays until pressed (nothing goes before it can be read). "Let's go" goes on to the
        /// animals; "Show me how" opens How it works, where the see-through hand acts out each move, and goes on when
        /// it is closed. Touching Juniper skips it. Never stuck: after two minutes it goes on by itself.
        /// </summary>
        IEnumerator Welcome()
        {
            welcomeChoice = "";
            Welcoming = true;
            Guide.SetMood(Guide.Mood.Speaking);
            // Light first, before anything flashes (photosensitivity): the player chooses how bright the storms are.
            // Calm effects removes every flash; nothing here flashes more than three times a second either way.
            Guide.Say(Story.LightNotice, 0f);
            Guide.ShowChoices(new[] { "Full effects", "Calm effects" }, new[] { "welcome:fx", "welcome:calm" });
            Trace.Info("Tutorial", "light choice offered");
            float lightUntil = Time.unscaledTime + 25f;
            while (State == Mode.Room && welcomeChoice.Length == 0 && !Progress.Onboarded && !firstAnimalOffered && firstAnimal == null && Time.unscaledTime < lightUntil) yield return null;
            if (welcomeChoice == "calm") Progress.CalmEffects = true;
            Log?.Add("light", new Dictionary<string, object> { { "choice", welcomeChoice.Length > 0 ? welcomeChoice : "none" }, { "calm", Progress.CalmEffects } });
            SaveProgress();
            welcomeChoice = "";
            // Moved on already (Juniper touched: the animals, or a world): no welcome card over it (review 42).
            if (State != Mode.Room || firstAnimalOffered || firstAnimal != null || Progress.Onboarded)
            {
                Progress.WelcomeSeen = true;
                SaveProgress();
                Welcoming = false;
                yield break;
            }
            Guide.Say(Story.Welcome, 0f);
            Guide.ShowChoices(new[] { "Let's go", "Show me how", "Comfort & access" }, new[] { "welcome:go", "welcome:show", "welcome:access" });
            Trace.Info("Tutorial", "welcome card");
            float until = Time.unscaledTime + 120f;
            while (State == Mode.Room && welcomeChoice.Length == 0 && !firstAnimalOffered && firstAnimal == null && !Progress.Onboarded && Time.unscaledTime < until) yield return null;
            Progress.WelcomeSeen = true;
            SaveProgress();
            Log?.Add("welcome", new Dictionary<string, object> { { "choice", welcomeChoice.Length > 0 ? welcomeChoice : "skipped" } });
            if (welcomeChoice == "show" && State == Mode.Room)
            {
                ShowHelp();
                until = Time.unscaledTime + 240f;
                yield return null;
                while (State == Mode.Room && Help != null && Help.Showing && !firstAnimalOffered && firstAnimal == null && Time.unscaledTime < until) yield return null;
                if (Help != null && Help.Showing) Help.Hide();
            }
            if (welcomeChoice == "access" && State == Mode.Room && Panel != null)
            {
                // Bigger text, Eyes only, a gentler storm, calm effects: chosen before anything else happens.
                Panel.Show(HandPanel.Page.Comfort);
                until = Time.unscaledTime + 240f;
                yield return null;
                while (State == Mode.Room && Panel.Open && !firstAnimalOffered && firstAnimal == null && Time.unscaledTime < until) yield return null;
            }
            if (welcomeChoice.Length == 0 && State == Mode.Room && !firstAnimalOffered && firstAnimal == null) Guide.ShowChoices(null, null);
            Welcoming = false;
        }

        IEnumerator FirstRun()
        {
            inFirstRun = true;
            Trace.Info("Tutorial", "first run started");
            // Wait for the headset to be worn, focused, and the room read (or 12 s).
            float until = Time.unscaledTime + 12f;
            while ((!Application.isFocused || (OVRManager.instance != null && !OVRManager.isHmdPresent) || (Rooms != null && Rooms.Loading)) && Time.unscaledTime < until)
                yield return null;
            while (!Application.isFocused) yield return null;
            yield return new WaitForSeconds(0.8f);
            if (State != Mode.Room || Progress.Onboarded) { inFirstRun = false; yield break; }
            if (Rooms != null && Rooms.Room != null) { Rooms.Recentre(); PlaceGuide(Rooms.Room); }
            Sfx.Instance?.Ui("ember_hello", 0.8f);
            Guide.SetMood(Guide.Mood.Speaking);
            Guide.Say("Hi, I'm Juniper.\nHold up your hands so I can see them.", 0f);
            Trace.Info("Tutorial", "step", "n", 1, "what", "hands");
            float t0 = Time.unscaledTime;
            while (State == Mode.Room && Time.unscaledTime - t0 < 6f && (Hands == null || !Hands.AnyTracked)) yield return null;
            bool hands = Hands != null && Hands.AnyTracked;
            Trace.Info("Tutorial", "hands", "seen", hands, "seconds", Time.unscaledTime - t0, "source_left", Hands != null ? Hands.Source(0) : "-", "source_right", Hands != null ? Hands.Source(1) : "-");
            if (State != Mode.Room) { inFirstRun = false; yield break; }
            Guide.Say(hands ? "There you are!" : "No hands? That's fine:\nyou can press anything by looking at it.", 3f);
            yield return new WaitForSeconds(hands ? 1.2f : 2.5f);
            if (State != Mode.Room) { inFirstRun = false; yield break; }
            // Once, what Worldeck is and what your hands do, with a way to see each move (or straight on).
            if (!Progress.WelcomeSeen && !Progress.Onboarded && !firstAnimalOffered && firstAnimal == null)
            {
                yield return Welcome();
                if (State != Mode.Room || firstAnimalOffered || firstAnimal != null) { inFirstRun = false; yield break; }
            }
            // (No question about scanning the room here: its first button opened Quest's room setup, minutes away from
            // the first animal, and a glance could press it. The room invites a scan later, once you have seen a world.)
            if (hands && Maker != null)
            {
                // Already picked from a touch of Juniper: nothing more to show here (no map over the animal).
                if (!firstAnimalOffered && firstAnimal == null) OfferFirstAnimal();
                inFirstRun = false;
                yield break;
            }
            Trace.Info("Tutorial", "step", "n", 2, "what", "map");
            ShowMap("campfire", hands ? "Pick a world: touch it with a fingertip,\nor look at it until the ring fills." : "Pick a world: look at it until the ring fills.");
            Guide.Say("Try the campfire first.", 0f);
            if (hands && Coach != null && Map != null && Map.Showing) { coachFor = "map"; Coach.Show(HandCoach.Demo.Poke, Map.TilePos("campfire"), default, null, null, 1f, 3); }
            inFirstRun = false;
            // Hands that show up late are still hands: the first six seconds do not decide the whole introduction.
            float late = Time.unscaledTime + 90f;
            while (State == Mode.Room && !Progress.Onboarded && !firstAnimalOffered && firstAnimal == null && Maker != null && Time.unscaledTime < late)
            {
                if (Hands != null && Hands.AnyHandTracked && Map != null && Map.Showing)
                {
                    Trace.Info("Tutorial", "hands seen late: the first animal after all");
                    Map.Hide();
                    Guide.SetMood(Guide.Mood.Speaking);
                    Guide.Say("There you are!", 1.5f);
                    yield return new WaitForSeconds(1.2f);
                    if (State == Mode.Room && !firstAnimalOffered && firstAnimal == null) OfferFirstAnimal();
                    yield break;
                }
                yield return null;
            }
        }

        // ---------- the coach: show, don't tell ----------

        string coachFor = "";
        int visit;
        float busySince = -1f;

        /// <summary>Whatever a transition cut short may have left: the spark, the table outline, the room glow.</summary>
        void CleanTransition()
        {
            if (spark != null) Util.Kill(spark);
            spark = null;
            if (tableTrace != null) tableTrace.enabled = false;
            Shader.SetGlobalFloat(RoomGlowId, 0f);
        }

        /// <summary>
        /// Safety nets that run every frame: a microphone that stopped by itself (a timeout) frees Juniper again,
        /// and a transition that never finished (an exception mid-way) is closed back to the room after 45 s.
        /// </summary>
        void Nets()
        {
            if (listening && Mic != null && !Mic.Listening && (Ears == null || !Ears.Listening))
            {
                listening = false;
                Guide?.SetMood(Guide.Mood.Idle);
                Trace.Info("Voice", "the microphone stopped by itself: Juniper is free again");
            }
            bool awake = Application.isFocused && (OVRManager.instance == null || OVRManager.isHmdPresent);
            if (Busy)
            {
                if (busySince < 0f) busySince = 0f;
                if (awake) busySince += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                if (busySince > 45f)
                {
                    busySince = -1f;
                    Trace.Warn("Director", "a transition never finished: back to the room", "state", State.ToString());
                    Go(CloseWorld("watchdog"));
                }
            }
            else busySince = -1f;
            // In a world whose task was never set up (an error on the way in): the dock comes back after 10 s, so there
            // is always a way on (review 33; the dock waits for the task on arrival).
            if (State == Mode.World && !Frozen && taskVisit != visit)
            {
                if (noTaskSince < 0f) noTaskSince = Time.unscaledTime;
                else if (Time.unscaledTime - noTaskSince > 10f)
                {
                    taskVisit = visit; noTaskSince = -1f;
                    Trace.Warn("Director", "this world's task was never set up: the dock is back");
                }
            }
            else noTaskSince = -1f;
        }
        float noTaskSince = -1f;
        bool wantsPalmWas;
        /// <summary>When this world opened: with no scan, a world looks for one only in its first 3 minutes (a scene load
        /// every 6 s all session was unmeasured: review 32).</summary>
        float worldSince = -1000f;

        void StopCoach(string why)
        {
            if (Coach != null && Coach.Playing != HandCoach.Demo.None) Coach.Stop(why);
            coachFor = "";
        }

        /// <summary>Stops a demonstration as soon as the player does the thing (or it no longer applies).</summary>
        void CoachWatch()
        {
            if (Coach == null) return;
            if (Coach.Playing == HandCoach.Demo.None) { coachFor = ""; return; }
            bool done = coachFor switch
            {
                "map" => Map == null || !Map.Showing || State != Mode.Room,
                "gather" => State != Mode.World || Gather == null || !Gather.Running || Gather.AnyHeld,
                "roast" => State != Mode.World || Campfire == null || !Campfire.Running || Campfire.Held,
                "stoke" => State != Mode.World || Campfire == null || Campfire.Pumps > 0 || Campfire.Stokes > 0,
                "menu" => State != Mode.World || (Panel != null && Panel.Open),
                "help" => Busy,
                _ => false
            };
            if (done) StopCoach("done: " + coachFor);
        }

        void CoachCarry(GatherItem it, string line)
        {
            if (Coach == null || it == null || Gather == null) return;
            var mf = it.GetComponent<MeshFilter>();
            coachFor = "gather";
            Coach.Show(HandCoach.Demo.Carry, it.transform.position, Gather.NextSocket, line, mf != null ? mf.sharedMesh : null, Mathf.Max(0.5f, it.transform.localScale.x), 3);
        }

        /// <summary>A few seconds into the task with nothing picked up, the see-through hand shows the whole move.</summary>
        IEnumerator CoachGatherSoon(bool first)
        {
            float t0 = Time.time, wait = first ? 3.5f : 12f;
            while (State == Mode.World && Gather != null && Gather.Running && Time.time - t0 < wait)
            {
                if (Gather.AnyHeld || Gather.PlacedCount > 0) yield break;
                yield return null;
            }
            if (State != Mode.World || Gather == null || !Gather.Running || Gather.AnyHeld || Gather.PlacedCount > 0) yield break;
            if (!first && (Hints == null || Hints.Count("pinch") > 0)) yield break;   // they already know how
            GatherItem next = null;
            float best = float.MaxValue;
            foreach (var it in Gather.Items)
            {
                if (it.Placed || it.HeldBy >= 0) continue;
                float d = Vector3.Distance(it.transform.position, Head.position);
                if (d < best) { best = d; next = it; }
            }
            CoachCarry(next, first ? "Pinch it, carry it to your table, let go" : null);
        }

        IEnumerator CoachRoastSoon()
        {
            int v = visit;
            yield return new WaitForSeconds(2.5f);
            if (visit != v || State != Mode.World || Campfire == null || !Campfire.Running || Campfire.Held || Coach == null) yield break;
            coachFor = "roast";
            Coach.Show(HandCoach.Demo.Roast, Campfire.Handle, Campfire.FirePos, "Pinch the stick, hold the marshmallow over the fire",
                       GhostHand.Capsule(0.31f, 0.005f, 0.004f, 6, 2), 1f, 3);
        }

        /// <summary>The menu, shown twice over: palm up, then the wrist button.</summary>
        IEnumerator CoachMenu()
        {
            if (Coach == null || Hands == null || !Hands.AnyHandTracked) yield break;
            int v = visit;
            coachFor = "menu";
            Coach.Show(HandCoach.Demo.Wrist, ChestSpot(-0.06f), default, "The menu: the button on your left wrist", null, 1f, 3);
        }

        /// <summary>In front of the chest, low, where hands rest (x: metres to the right).</summary>
        Vector3 ChestSpot(float x)
        {
            Vector3 f = Head.forward; f.y = 0f;
            f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            return Head.position + f * 0.33f + Vector3.down * 0.3f + right * x;
        }

        /// <summary>"Show me" on the How it works card: the see-through hand acts the row out, twice.</summary>
        void ShowMe(string what)
        {
            if (Coach == null || Head == null) return;
            coachFor = "help";
            Vector3 f = Head.forward; f.y = 0f;
            f = f.sqrMagnitude > 1e-4f ? f.normalized : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, f);
            switch (what)
            {
                case "press":
                    Coach.Show(HandCoach.Demo.Poke, Help != null ? Help.OkButton : Head.position + f * 0.5f, default, "Touch it,\nor look at it and pinch.", null, 1f, 2);
                    break;
                case "menu":
                    Coach.Show(HandCoach.Demo.Wrist, ChestSpot(-0.06f), default, "Touch the button on your wrist, or turn a palm up", null, 1f, 2);
                    break;
                case "move":
                {
                    Vector3 from = Head.position + f * 0.42f + Vector3.down * 0.3f - right * 0.12f;
                    Coach.Show(HandCoach.Demo.Carry, from, from + right * 0.24f, "Pinch, carry, let go", GhostHand.Capsule(0f, 0.022f, 0.022f, 10, 3), 1f, 2);
                    break;
                }
                case "jump":
                    Coach.Show(HandCoach.Demo.Jump, ChestSpot(0.06f) + Vector3.up * 0.08f, default, "Hand out, palm down.\nQuick pinch to jump.", null, 1f, 2);
                    break;
                case "block":
                    Coach.Show(HandCoach.Demo.PalmUp, Head.position + f * 0.42f + Vector3.down * 0.05f, default, "Open palm in the glowing ring", null, 1f, 2);
                    break;
                case "tap":
                {
                    var t = Rooms != null && Rooms.Room != null ? Rooms.Room.Table : null;
                    Vector3 at = t != null ? t.TopCenter : Head.position + f * 0.45f + Vector3.down * 0.45f;
                    Vector3 toHead = Head.position - at; toHead.y = 0f;
                    if (t != null && toHead.sqrMagnitude > 1e-4f) at += toHead.normalized * Mathf.Min(0.12f, Mathf.Min(t.Size.x, t.Size.z) * 0.3f);
                    Coach.Show(HandCoach.Demo.Tap, at, default, "Tap your table", null, 1f, 2);
                    break;
                }
            }
        }

        // ---------- on-device voice ----------

        /// <summary>
        /// Said without Juniper's name, but plainly a command ("make it rain", "take me to the beach", "make me a
        /// lantern"): two to seven words that the app understands by itself. Anything that ends, saves or undoes
        /// still needs the name.
        /// </summary>
        bool IsClearCommand(string said)
        {
            if (State != Mode.World && State != Mode.Room) return false;
            int words = said.Split(' ').Length;
            if (Mission != null && Mission.AwaitingChoice && Mission.TryWordsProbe(said)) return true;
            if (words < 2 || words > 7) return false;
            var wish = Wishes.Read(said);
            if (wish != null) return wish.Kind.Length > 0 && !wish.Bare;   // "a dog" said to someone else isn't a wish
            if (Maker != null && Maker.Things.Count > 0 && Wishes.Change(said, Maker.Names()) != null) return true;
            if (!Words.IsQuestion(said) && AnimalAddressed(said, null) != null) return true;
            // "Hi, Wren", "Wren, who are you?": their own lines, on the headset (anything else needs Juniper's name).
            if (local != null && local.gameObject.activeSelf && Locals.Named(local.Def, said) && Locals.Answer(local.Def, said).Length > 0) return true;
            var p = Words.Parse(said, Current, 1);
            if (p.AskAi) return false;
            // Without the name, a new world needs every word understood ("she never give me a tree" opened a forest).
            if (p.Act == Act.NewWorld && p.Refine) return false;
            return p.Act == Act.Edit || p.Act == Act.NewWorld || p.Act == Act.Storm || p.Act == Act.Music || p.Act == Act.MusicOff ||
                   p.Act == Act.Rewind || p.Act == Act.Freeze || p.Act == Act.Resume || p.Act == Act.Surprise;
        }

        /// <summary>A phrase for Juniper: called by name ("Juniper, make it rain") or said while it was listening.</summary>
        void OnHeard(Ears.Heard h)
        {
            listening = false;
            Sfx.Instance?.Ui("listen_stop", 0.6f);
            Log?.Add("voice", new Dictionary<string, object> { { "ok", true }, { "on_device", true }, { "ms", Math.Round(h.DecodeMs) }, { "woke", h.Woke }, { "words", h.Command.Split(' ').Length } });
            Health.Ok(Health.Voice, "on-device ok", Time.realtimeSinceStartup);
            if (Busy && !h.Command.Contains("end")) { Guide.Say("One moment…", 1.5f); return; }
            // A follow-up window is forgiving: anything that isn't a command (the room, Juniper's own chirps) is ignored quietly.
            bool follow = followUpUntil > Time.unscaledTime && !h.Woke;
            followUpUntil = 0f;
            if (follow)
            {
                var probe = Words.Parse(h.Command, Current, 1);
                if (probe.Act == Act.Unclear || probe.Act == Act.None) return;
            }
            askedAt = Time.unscaledTime;
            // What was heard, shown back (never words on the blocked list: the recogniser can mishear anything).
            if (Words.IsClean(h.Text)) Guide.Words.Say("\"" + h.Text.Trim() + "\"", 0f, new Color(0.72f, 0.75f, 0.8f));
            Handle(h.Command, h.Woke ? "voice-name" : "voice");
            // Keep listening for a moment: "make it rain" … "darker" … "add fireflies", without touching Juniper again.
            if (Ears != null && Ears.On)
            {
                Ears.Listen(7f);
                followUpUntil = Time.unscaledTime + 7f;
            }
            if (!Progress.VoiceOn && !Progress.VoiceOffered) StartCoroutine(OfferVoiceSoon());
        }

        float followUpUntil;

        IEnumerator OfferVoiceSoon()
        {
            yield return new WaitForSecondsRealtime(5f);
            while (Busy) yield return null;
            if (State == Mode.World || State == Mode.Room) OfferVoice();
        }

        /// <summary>"Juniper" on its own: it lights up and waits for the rest.</summary>
        void OnWoke()
        {
            listening = true;
            Sfx.Instance?.Ui("listen_start", 0.8f);
            Sfx.Instance?.Duck(6f);
            Guide.SetMood(Guide.Mood.Listening);
            Guide.Say("Yes?", 0f);
        }

        void SetVoice(bool on)
        {
            Progress.VoiceOffered = true;
            if (Ears == null || !Ears.Supported) return;
            if (!on)
            {
                Progress.VoiceOn = false;
                SaveProgress();
                Ears.TurnOff();
                Guide.Say("Voice off. Touch me to talk instead.", 3f);
                Trace.Info("Ears", "voice turned off by the player");
                return;
            }
            Mic?.RequestPermission(ok =>
            {
                if (!ok) { Guide.Say("No microphone, no problem.\nTouch to choose instead.", 3f); return; }
                Progress.VoiceOn = true;
                Progress.VoiceTried = true;
                SaveProgress();
                Ears.TurnOn();
                Trace.Info("Ears", "voice turned on by the player");
                Guide.SetMood(Guide.Mood.Speaking);
                Guide.Say("I'm listening for my name, on this headset only.\nSay \"Juniper, make it rain\" or \"Juniper, a volcano\".\nMenu, What can I say?, shows the rest.", 8f);
            });
        }

        /// <summary>Once, a moment after the first words said to Juniper by touch: offer voice, with what it means.</summary>
        void OfferVoice()
        {
            if (Ears == null || !Ears.Supported || Progress.VoiceOffered || Progress.VoiceOn) return;
            Progress.VoiceOffered = true;
            SaveProgress();
            Guide.SetMood(Guide.Mood.Speaking);
            Guide.Say("Want me to listen all the time?\nThen just say \"Juniper\" and a wish.\nI listen on this headset only.", 0f);
            Guide.ShowChoices(new[] { "Turn on voice", "Not now" }, new[] { "voice:on", "voice:off" });
        }

        void PlayMusic(bool on)
        {
            Progress.MusicOn = on;
            SaveProgress();
            // "Play some music" in a world: that world's own tune, a little louder, until you leave it.
            worldTune = on && State == Mode.World;
            Guide.Say(on ? (worldTune ? "Here's a tune." : "Music on.") : "Music off.", 2f);
            Log?.Add("music", new Dictionary<string, object> { { "on", on }, { "biome", Current != null ? Current.Biome : "room" } });
        }

        /// <summary>Calm music for the background: the same piece in every world and in your room.</summary>
        public const string CalmMusic = "music_calm";
        bool worldTune;
        string musicNow = "";

        /// <summary>
        /// Every frame: soft music under everything, on unless turned off. It dips under Mara's radio and while
        /// Juniper listens for your words, and fades out for her storm (the meteors have their own sound).
        /// </summary>
        void BackgroundMusic()
        {
            var sfx = Sfx.Instance;
            if (sfx == null || Head == null) return;
            if (State != Mode.World) worldTune = false;
            bool on = Progress.MusicOn && (State == Mode.Room || State == Mode.World || State == Mode.Opening || State == Mode.Changing);
            string want = !on ? "" : worldTune && Current != null ? MusicFor(Current) : CalmMusic;
            if (want != musicNow)
            {
                musicNow = want;
                var at = Rooms != null && Rooms.Room != null && Rooms.Room.Table != null ? Rooms.Room.Table.TopCenter + Vector3.up * 0.3f : Head.position;
                sfx.Music(want.Length > 0 ? want : null, at, worldTune ? 0.3f : CalmMusicVolume);
                Trace.Info("Music", want.Length > 0 ? "playing" : "off", "tune", want.Length > 0 ? want : "-");
            }
            bool storm = Emergency != null && Emergency.Running && Emergency.Sim != null && Emergency.Sim.State.Phase >= Sim.Warning && Emergency.Sim.State.Phase < Sim.Aftermath;
            bool her = Mission != null && Mission.Active;
            bool hearing = Guide != null && Guide.State == Guide.Mood.Listening;
            // One tune at a time: the jukebox, or the city's jazz, unless you asked for a tune ("play some music").
            bool worldMusic = sfx.LoopIsMusic || (sfx.BedIsMusic && !worldTune);
            sfx.MusicDuck = storm || worldMusic ? 0f : hearing ? 0.25f : her ? 0.35f : 1f;
            // One thing in front: under the music (and under the storm's own sounds) the world's sound is a faint
            // layer; with the music off it comes back. A jazz bed under another tune is all but gone.
            float bed = 1f;
            if (sfx.BedIsMusic && (sfx.LoopIsMusic || worldTune)) bed = 0.15f;
            else if (storm) bed = BedUnderMusic;
            else if (on && !worldMusic) bed = BedUnderMusic;
            sfx.BedDuck = bed;
            // A loop that is music (the jukebox) steps back under Mara's radio and her storm too (review 32).
            sfx.LoopDuck = sfx.LoopIsMusic && (storm || her) ? 0.25f : 1f;
        }

        /// <summary>The calm music's level, and the world's ambience under it (a share of its own level).</summary>
        public const float CalmMusicVolume = 0.16f, BedUnderMusic = 0.35f;

        /// <summary>The tune for a world (all our own, made by Tools/make_sounds.py).</summary>
        static string MusicFor(Recipe r) => r == null ? "music_campfire" : r.Biome switch
        {
            "campfire" or "meadow" => "music_campfire",
            "beach" or "desert" => "music_beach",
            "space" or "reef" or "snow" => "music_space",
            "city" => "jukebox_music",
            _ => "music_campfire"
        };

        // ---------- the emergency: a world where what you do decides what happens ----------

        int stormRuns;

        /// <summary>Starts the meteor storm: your table becomes the console.</summary>
        public void StartStorm(string how) => StartStorm(how, Pace.Standard);

        public void StartStorm(string how, Pace pace)
        {
            if (State != Mode.World || Emergency == null || Rooms == null || Rooms.Room == null || Current == null) { Guide.Say("Open a world first.", 3f); return; }
            if (Emergency.Running && Emergency.Sim != null && Emergency.Sim.State.Phase < Sim.Aftermath) return;
            Move?.Home(true, "storm");
            Gather?.Stop();
            Campfire?.Stop();
            StopCoach("storm");
            stormRuns++;
            saidHit.Clear();
            stormThisWorld = true;
            exploring = false;
            Emergency.Teach = !Progress.SeenBreak;
            // Her storms: what goes dark is remembered, and her last chapter ends with a big one (not with Gentle storm
            // on, or with no hands seen).
            bool forMara = how == "mission" && Mission != null && Mission.Active;
            Emergency.Remember = forMara;
            Emergency.Level = forMara && !Progress.GentleStorm && Hands != null && Hands.AnyTracked ? Mission.Chapter : 0;
            Emergency.PreDark = forMara ? new HashSet<string>(Progress.DarkPieces) : null;
            Emergency.Focus = forMara && Emergency.Level >= 2 ? (relitVisit == visit && relitName.Length > 0 ? relitName : Progress.LastRelit()) : "";
            Emergency.Begin(Rooms.Room, Current, Current.Seed + stormRuns, pace, Skins);
            Log?.Add("storm", new Dictionary<string, object> { { "biome", Current.Biome }, { "how", how } });
            Guide.Place(TaskCaptionSpot());
        }

        void OnStormPhase(int phase)
        {
            Guide.SetMood(Guide.Mood.Speaking);
            if (Mission != null && Mission.Active)
            {
                if (phase == Sim.Warning) StartCoroutine(SayLater(5.4f, "Hold your open palm in each glowing ring\nto block the meteors.", 6f));
                else if (phase == Sim.Storm)
                {
                    Guide.Point(Emergency.NextTarget, 3.5f);
                    bool teachNow = Emergency.Teach && Emergency.Defended > 0;
                    if (!teachNow && Coach != null && Hands != null && Hands.AnyHandTracked) { coachFor = "block"; Coach.Show(HandCoach.Demo.PalmUp, Emergency.NextTarget + Vector3.down * 0.02f, default, null, null, 1f, 2); }
                }
                return;
            }
            if (phase == Sim.Warning)
            {
                string last = Progress.StormBest > 0 ? "\nLast time: " + Sim.OutcomeName(Progress.StormLast).ToLowerInvariant() + "." : "";
                Guide.Say((Emergency.Defended > 0 ? "Meteors are heading for your furniture!\n" : "Meteors are coming!\n") + "Hold your open palm in each glowing ring\nto block them." + last, 0f);
            }
            else if (phase == Sim.Storm)
            {
                bool teach = Emergency.Teach && Emergency.Defended > 0;
                Guide.Say(teach ? "The first one is too big to stop.\nWatch what it does to your furniture." : "Here they come! Palm in the ring.", teach ? 5f : 4f);
                Guide.Point(Emergency.NextTarget, 3.5f);
                if (!teach && Coach != null && Hands != null && Hands.AnyHandTracked) { coachFor = "block"; Coach.Show(HandCoach.Demo.PalmUp, Emergency.NextTarget + Vector3.down * 0.02f, default, null, null, 1f, 2); }
            }
        }

        void OnStormStrike(Strike s, StrikeResult r)
        {
            string piece = Emergency.PieceLabel(s);
            // The next ring out of easy view (rings sit on your furniture, up to 60 degrees aside): Juniper points to it.
            if (Emergency.Running && Head != null && Guide != null && Vector3.Angle(Head.forward, Emergency.NextTarget - Head.position) > 35f)
                Guide.Point(Emergency.NextTarget, 2.5f);
            if (Mission != null && Mission.Active)
            {
                if (r == StrikeResult.Blocked && coachFor == "block") StopCoach("blocked");
                if (r == StrikeResult.Hit && Emergency.Defended > 0 && !Progress.SeenBreak) { Progress.SeenBreak = true; SaveProgress(); }
                // The first one cannot be stopped and has no ring: the block is shown now, on the first ring there is.
                if (r != StrikeResult.Blocked && Emergency.Sim.State.Blocked == 0 && Coach != null && Hands != null && Hands.AnyHandTracked && Coach.Playing == HandCoach.Demo.None)
                {
                    coachFor = "block";
                    Coach.Show(HandCoach.Demo.PalmUp, Emergency.NextTarget + Vector3.down * 0.02f, default, "Open palm in the glowing ring", null, 1f, 3);
                    Guide.Point(Emergency.NextTarget, 3.5f);
                }
                return;
            }
            if (r == StrikeResult.Hit && Emergency.Defended > 0 && !Progress.SeenBreak)
            {
                Progress.SeenBreak = true;
                SaveProgress();
                Guide.SetMood(Guide.Mood.Speaking);
                Guide.Say("Look: that's your real " + (piece == "furniture" || piece.Length == 0 ? "room" : piece) + " showing through.\nBlock the rest. Rewind will win it back.", 6f);
                saidHit.Add(Emergency.Sim.Strikes.IndexOf(s) % Mathf.Max(1, Emergency.Defended));
                return;
            }
            if (r == StrikeResult.Hit && piece.Length > 0 && saidHit.Add(Emergency.Sim.Strikes.IndexOf(s) % Mathf.Max(1, Emergency.Defended))) Guide.Say("Your " + piece + " took a hit!\nLook: it's your real " + (piece == "furniture" ? "room" : piece) + " again.", 3.5f);
            else if (r == StrikeResult.Hit && Emergency.Sim.State.Taken == 1) Guide.Say("That one got through!\nPalm in the ring before it lands.", 3.5f);
            else if (r == StrikeResult.Blocked && Emergency.Sim.State.Blocked == 1) { if (coachFor == "block") StopCoach("blocked"); Guide.Say("Blocked! Keep going.", 2.5f); }
            else if (r == StrikeResult.Dodged && Emergency.Sim.State.Dodged == 1) Guide.Say("Dodged!", 2f);
        }

        void NoteVisit(Recipe r)
        {
            if (r.Look != null && !string.IsNullOrEmpty(r.Look.Name) && !Progress.PlacesSeen.Contains(r.Look.Name) && Progress.PlacesSeen.Count < 200)
            {
                Progress.PlacesSeen.Add(r.Look.Name);
                MetaPlatform.Achieve(MetaPlatform.SayAWorld);
            }
            int n = Records.WorldsVisited(Progress);
            MetaPlatform.Post(MetaPlatform.WorldsVisited, n);
            if (n >= Records.WorldwalkerCount) MetaPlatform.Achieve(MetaPlatform.Worldwalker);
        }

        void OnStormEnded(int outcome)
        {
            var st = Emergency.Sim.State;
            if (st.Blocked > Progress.BestBlocked) Progress.BestBlocked = st.Blocked;
            MetaPlatform.Post(MetaPlatform.MostBlocked, st.Blocked);
            if (outcome == Sim.Untouched) MetaPlatform.Achieve(MetaPlatform.Untouched);
            Progress.StormLast = outcome;
            if (sessionStorm == 0 || outcome < sessionStorm) sessionStorm = outcome;
            if (Progress.StormBest == 0 || outcome < Progress.StormBest) Progress.StormBest = outcome;
            Progress.StormRuns++;
            SaveProgress();
            if (Mission != null && Mission.Active) { Log?.Add("storm_end", new Dictionary<string, object> { { "outcome", Sim.OutcomeName(outcome) }, { "mission", Mission.Chapter }, { "rewinds", Emergency.Rewinds } }); return; }
            Guide.SetMood(Guide.Mood.Speaking);
            string safe = Emergency.SafePieces();
            string line = Emergency.RoomBack ? "The storm won." : outcome == Sim.Untouched ? "You blocked every one!" + (safe.Length > 0 ? "\nYour " + safe + (safe.Contains(" and ") ? " are safe." : " is safe.") : "") : "You blocked " + st.Blocked + " of " + (st.Blocked + st.Taken + st.Dodged) + ".";
            Guide.Say(line + (outcome == Sim.Untouched || Emergency.RoomBack ? "" : "\nPress Rewind to block the ones you missed."), 8f);
            Guide.ShowChoices(null, null);
            Log?.Add("storm_end", new Dictionary<string, object> { { "outcome", Sim.OutcomeName(outcome) }, { "hull", Mathf.RoundToInt(st.Hull) }, { "rewinds", Emergency.Rewinds } });
            if (outcome == Sim.Untouched && Current != null) PlayFinale(Current);
        }

        bool pictureSet;
        string journeyDo = "";
        readonly HashSet<int> saidHit = new HashSet<int>();
        bool invitedScan;
        bool roastOver;
        bool exploring;
        /// <summary>Mara asked to call later (or calls only when asked): the dock's main button answers her.</summary>
        bool callWaiting;
        int sessionWonders, sessionStorm, sessionWorlds;
        Landing sessionMission = Landing.None;
        int sessionChapter;

        /// <summary>The storm, as Mara's story: she calls, you answer and choose, then the storm is her approach.</summary>
        public void StartMission(string how)
        {
            if (Mission == null) { StartStorm(how); return; }
            if (State != Mode.World || Emergency == null || Rooms == null || Rooms.Room == null || Current == null) { Guide.Say("Open a world first.", 3f); return; }
            if (Mission.Active || (Emergency.Running && Emergency.Sim != null && Emergency.Sim.State.Phase < Sim.Aftermath)) return;
            // Your scan turned up while you were in this world: it is rebuilt round your furniture first, and she
            // calls as you arrive (her chapter is about your furniture; without it nothing is defended).
            if (Rooms.HasHeld && !Rooms.Room.Scanned && !Busy)
            {
                Trace.Info("Room", "scan found: the world is rebuilt round your furniture before her chapter");
                Guide.SetMood(Guide.Mood.Speaking);
                Guide.Say("Your furniture just joined in.\nOne moment…", 3f);
                flightVisit = true;
                Go(ChangeWorld(Current.Copy(), "scan", pushUndo: false));
                return;
            }
            Emergency.Stop();
            Move?.Home(true, "mission");
            Gather?.Stop();
            Campfire?.Stop();
            Loose?.Stop();   // a clear table for her story (the toys come back next visit)
            StopCoach("mission");
            stormThisWorld = true;
            exploring = false;
            Guide.ShowChoices(null, null);
            Guide.Place(TaskCaptionSpot());
            Mission.Avoid = Site != null ? Site.transform.position : (Vector3?)null;
            // (No scan: the tip about scanning comes after her chapter, not over her call and its title.)
            // She remembers the chapter before.
            Mission.LastLanding = Progress.ChapterLanding(Progress.MissionChapter - 1, out string lastPace, out string lastHeld);
            Mission.LastPace = lastPace;
            Mission.LastHeld = lastHeld;
            Mission.DarkNow = DarkHere(out _);
            Mission.Relit = relitVisit == visit ? relitName : "";
            Mission.Begin(Progress.MissionChapter, Rooms.Room);
            Log?.Add("mission", new Dictionary<string, object> { { "chapter", Progress.MissionChapter }, { "how", how }, { "biome", Current.Biome } });
        }

        /// <summary>Back home after her last chapter: her keepsakes light up, her ship crosses over the room, and
        /// Juniper says what you did.</summary>
        IEnumerator StoryEndAtHome()
        {
            int v = visit;
            yield return new WaitForSecondsRealtime(1.5f);
            if (visit != v || State != Mode.Room || Shelf == null) yield break;
            Trace.Info("Mission", "story ending at home", "held", Progress.HeldThroughout());
            Log?.Add("story_end", new Dictionary<string, object> { { "held", Progress.HeldThroughout() } });
            storyEnding = true;
            try
            {
                yield return Shelf.StoryEnd(Head);
                if (visit != v || State != Mode.Room) yield break;
                // The souvenir's line is read first (it was cut after 2 s), then the story's last words, in full.
                float until = Time.unscaledTime + 6f;
                while (Guide.Words != null && Guide.Words.Fresh && Time.unscaledTime < until) yield return null;
                if (visit != v || State != Mode.Room) yield break;
                Guide.SetMood(Guide.Mood.Speaking);
                Guide.Say(Story.StoryEnd(Progress.HeldThroughout()), 9f);
                yield return null;
                until = Time.unscaledTime + 12f;
                while (Guide.Words != null && Guide.Words.Fresh && Time.unscaledTime < until) yield return null;
                // And your room's own part in it, from what really happened.
                string relit = Progress.LastRelit();
                string room = Story.RoomStory(Progress.ChapterNote(0, 3), relit, relit.Length > 0 && Progress.ChapterNote(Story.Chapters - 1, 3) != relit);
                if (room.Length > 0 && visit == v && State == Mode.Room)
                {
                    Trace.Info("Mission", "your room's story", "first_dark", Progress.ChapterNote(0, 3), "relit", relit);
                    Guide.Say(room, 10f);
                    yield return null;
                    until = Time.unscaledTime + 14f;
                    while (Guide.Words != null && Guide.Words.Fresh && Time.unscaledTime < until) yield return null;
                }
            }
            finally
            {
                storyEnding = false;
                roomIdleSince = Time.unscaledTime;   // the map waits a while longer after the ending
            }
        }
        /// <summary>Her story's ending is playing in your room: nothing else is offered over it (the map came up over it
        /// 9 s in, seen in a recorded run on Nov 16).</summary>
        bool storyEnding;
        public bool StoryEnding => storyEnding;

        /// <summary>After Mara's second chapter: name any place (the tagline's verb), with one example to touch, and voice offered.</summary>
        IEnumerator TeachSayAWorld(int v)
        {
            yield return new WaitForSecondsRealtime(9f);
            float until = Time.unscaledTime + 25f;
            while (Time.unscaledTime < until && visit == v && Guide.HasChoices) yield return null;
            if (visit != v || State != Mode.World || Guide.HasChoices) yield break;
            Guide.SetMood(Guide.Mood.Speaking);
            // Voice is offered here, at a quiet moment after her second chapter, with what it is for (review 30, 31: it was
            // found only after touching Juniper).
            bool offer = !Progress.VoiceOn && Ears != null;
            Guide.Say(Progress.VoiceOn ? "Now name any place, and it's around you.\nSay \u201CJuniper, a volcano at night\u201D."
                    : "Now name any place, and it's around you.\nTouch this one, or turn on voice and say it.\nI listen on this headset only.", 0f);
            if (offer) Guide.ShowChoices(new[] { "A volcano at night", "Turn on voice", "Later" }, new[] { "say:a volcano at night", "voice:on", "say:no" });
            else Guide.ShowChoices(new[] { "A volcano at night", "Later" }, new[] { "say:a volcano at night", "say:no" });
            Trace.Info("Tutorial", "step", "n", 5, "what", "say a world", "voice_offered", offer);
        }

        float callComingUntil = -1f;

        // ---------- the runway remembers ----------

        string darkName = "", relitName = "", darkAfter = "";
        int relitVisit = -1;
        List<RoomModel.Piece> darkPieces;

        /// <summary>The plain name of the first of these pieces (by key) in this room ("" for none).</summary>
        string NameOfKeys(List<string> keys)
        {
            if (keys == null || keys.Count == 0 || Rooms == null || Rooms.Room == null) return "";
            string any = "";
            foreach (var p in Rooms.Room.Pieces)
            {
                if (string.IsNullOrEmpty(p.Key) || !keys.Contains(p.Key)) continue;
                string n = Emergency.Plain(p.Label);
                if (n != "furniture") return n;
                any = n;
            }
            return any;
        }

        /// <summary>Your furniture in this room that went dark in her last storm, by name ("shelf"; "furniture" when
        /// it has no plainer name; "" for none or without a scan).</summary>
        string DarkHere(out List<RoomModel.Piece> pieces)
        {
            pieces = new List<RoomModel.Piece>();
            if (Rooms == null || Rooms.Room == null || !Rooms.Room.Scanned || Progress.DarkPieces.Count == 0) return "";
            foreach (var p in Rooms.Room.Pieces)
                if (!p.Virtual && p != Rooms.Room.Table && !string.IsNullOrEmpty(p.Key) && Progress.DarkPieces.Contains(p.Key)) pieces.Add(p);
            foreach (var p in pieces) { string n = Emergency.Plain(p.Label); if (n != "furniture") return n; }
            return pieces.Count > 0 ? "furniture" : "";
        }

        IEnumerator CallSoon(int v)
        {
            callComingUntil = Time.unscaledTime + 6f;   // past the call's start even after a slow frame
            yield return new WaitForSecondsRealtime(2.5f);
            if (visit == v && State == Mode.World && Mission != null && !Mission.Active) StartMission("flight");
        }

        IEnumerator SayLater(float wait, string line, float seconds)
        {
            int v = visit;
            yield return new WaitForSecondsRealtime(wait);
            if (visit == v && State == Mode.World) { Guide.SetMood(Guide.Mood.Speaking); Guide.Say(line, seconds); }
        }

        void OnMissionDone(int chapter, Landing landing, string farewell)
        {
            if (!Progress.Onboarded) { Progress.Onboarded = true; firstWorld = false; SaveProgress(); }
            if (chapter == 1 && landing != Landing.Lost) StartCoroutine(TeachSayAWorld(visit));
            if (landing == Landing.Clean || landing == Landing.Rough)
            {
                Progress.Landings++;
                MetaPlatform.Achieve(MetaPlatform.FirstLanding);
                if (chapter + 1 >= Story.Chapters) MetaPlatform.Achieve(MetaPlatform.StoryDone);
            }
            if (landing == Landing.Clean && Current != null) PlayFinale(Current);
            if (landing == Landing.Clean || landing == Landing.Rough)
            {
                if (Mission != null) Progress.Darken(Mission.EndDefended, Mission.EndDark);
                if (chapter < Story.Chapters)
                    Progress.RememberChapter(chapter, landing, Mission != null ? Mission.PaceId : "", Mission != null ? Mission.Held : "",
                                             Mission != null ? NameOfKeys(Mission.EndDark) : "", relitVisit == visit ? relitName : "");
                if (chapter >= Progress.MissionChapter) Progress.MissionChapter = Mathf.Min(Story.Chapters, chapter + 1);
                Progress.FlightDone(DateTime.UtcNow);
                SaveProgress();
            }
            if (sessionMission == Landing.None || landing < sessionMission) sessionMission = landing;
            sessionChapter = chapter;
            Log?.Add("mission_end", new Dictionary<string, object> { { "chapter", chapter }, { "landing", landing.ToString() }, { "farewell", farewell } });
            // Her keepsake is on your table now, and will be on your real table when you go home (Souvenirs).
            Guide.SetMood(Guide.Mood.Speaking);
            bool noScan = Rooms != null && Rooms.Room != null && !Rooms.Room.Scanned;
            darkAfter = landing != Landing.Lost && chapter + 1 < Story.Chapters ? DarkHere(out _) : "";
            Guide.Say(chapter + 1 < Story.Chapters
                ? (darkAfter.Length > 0 ? Story.ChapterDark(darkAfter)
                   : noScan ? "She'll call in your next world.\nBack in your room: Menu, Use my furniture,\nand your furniture joins her runway." : "She'll be on the radio in your next world.\nAnswer her when you like.")
                : "That's Mara's story. Thank you, beacon.\nGo home to see how it ends.", 7f);
            // The world beyond opens by itself only when her story is told: mid-story the next step is her next world.
            if (landing != Landing.Lost && chapter + 1 >= Story.Chapters) StartCoroutine(InviteOut(visit));
        }

        /// <summary>After Mara's story: the world beyond the table opens by itself (beams, jumping on).</summary>
        IEnumerator InviteOut(int v)
        {
            yield return new WaitForSecondsRealtime(6.5f);
            // Not over the map or the menu (Nov 15 on the headset: it opened, with its jump coach, while the map was up).
            float until = Time.unscaledTime + 60f;
            while (visit == v && State == Mode.World && Time.unscaledTime < until && ((Map != null && Map.Showing) || (Panel != null && Panel.Open))) yield return null;
            if (visit != v || State != Mode.World || exploring || (Mission != null && Mission.Active)) yield break;
            if ((Map != null && Map.Showing) || (Panel != null && Panel.Open)) yield break;
            if (StoryEndWaits) yield break;   // her story ends in your room first: the bright button says go home
            StartExplore();
        }
        bool stormThisWorld;
        /// <summary>Her last chapter is flown and its ending (in your room) not yet seen: the next step is home.</summary>
        bool StoryEndWaits => Progress.MissionChapter >= Story.Chapters && !Progress.StoryEndSeen && Shelf != null;
        const string GoHomeLine = "Next: go home. Her story ends in your room.";

        /// <summary>
        /// Always an answer to "what now?": one line above the dock and one bright button for the next step.
        /// The path through a world: its task, the nearest wonder, the storm drill, then the next world.
        /// </summary>
        void Journey()
        {
            string next = "", label = "", act = "";
            bool away = Move != null && Move.Away;
            if (Mission != null && Mission.Active && (Emergency == null || !Emergency.Running))
            {
                next = Mission.AwaitingChoice ? "" : "Mara is calling...";   // her choices speak for themselves
            }
            else if (Mission != null && Mission.Active && Mission.Now == Mission.Step.Farewell) next = "Say goodbye to Mara: touch a choice, or wave.";
            else if (Emergency != null && Emergency.Running)
            {
                var st = Emergency.Sim.State;
                if (Mission != null && Mission.Active && st.Phase < Sim.Aftermath && !Emergency.RoomBack) next = "Keep Mara's signal: palm in each ring. Signal " + Mathf.RoundToInt(Mission.Signal * 100f) + "%";
                else if (Mission != null && Mission.Active && Emergency.RoomBack) { next = "You lost Mara's signal. Press Rewind to bring her back."; label = ""; act = ""; }
                else if (Mission != null && Mission.Active) { next = Mission.Now == Mission.Step.Farewell ? "Say goodbye to Mara: touch a choice, or wave." : "Mara is coming in to land..."; label = ""; act = ""; }
                else if (Emergency.RoomBack) { next = "This is your real room. Press Rewind to win the world back."; label = ""; act = ""; }
                else if (st.Phase >= Sim.Aftermath && Mission != null && Progress.MissionChapter > 0 && Progress.MissionChapter < Story.Chapters && !exploring && !away)
                {
                    // Her chapter is over (the storm is still out, in its aftermath): one next step, her next world.
                    next = darkAfter.Length > 0 ? "Next: your next world lights your\n" + darkAfter + " again for Mara." : "Next: Mara's next chapter is in your next world.";
                    label = "Next world"; act = "next";
                }
                else if (st.Phase >= Sim.Aftermath && Mission != null && StoryEndWaits && !exploring && !away) { next = GoHomeLine; label = "My room"; act = "room"; }
                else if (st.Phase >= Sim.Aftermath) { next = "The storm has passed."; label = "Next world"; act = "next"; }
                else next = Emergency.Score.Length > 0 ? Emergency.Score : "Hold your palm in each glowing ring to block.";
            }
            else if (Gather != null && Gather.Running && !away) next = darkName.Length > 0 ? "Next: put the glowing pieces on your table\nto light your " + darkName + " again." : "Next: put the glowing pieces on your table.";
            else if (Campfire != null && Campfire.Running && !away && Campfire.Eaten == 0 && !roastOver) next = "Next: roast a marshmallow over the fire.";
            else if (Gifts != null && Gifts.Waiting > 0 && !away) next = "Next: touch the gift on your table.";
            else if (away && Wonders != null && Wonders.FoundCount > 0 && Gifts != null && Gifts.Waiting > 0) { next = "A gift is waiting at camp."; label = "Back to camp"; act = "camp"; }
            else if (callWaiting && !stormThisWorld && Mission != null && !away && !Mission.Active)
            { next = exploring ? "Explore, or answer Mara when you like." : "Mara's on the radio when you want her."; label = "Answer Mara"; act = "storm"; }
            else if (exploring && Wonders != null && Wonders.FoundCount < Wonders.All.Count) next = "Next: jump to the beam of light (palm down, then pinch).";
            else if (stormThisWorld && !exploring && !away && Mission != null && !Mission.Active && Progress.MissionChapter > 0 && Progress.MissionChapter < Story.Chapters)
            {
                // Mid-story, one next step: her next chapter is in your next world (and it can light what went dark).
                next = darkAfter.Length > 0 ? "Next: your next world lights your\n" + darkAfter + " again for Mara." : "Next: Mara's next chapter is in your next world.";
                label = "Next world"; act = "next";
            }
            else if (stormThisWorld && !exploring && !away && Mission != null && !Mission.Active && StoryEndWaits) { next = GoHomeLine; label = "My room"; act = "room"; }
            else if (stormThisWorld && !exploring && !away && Wonders != null && (Wonders.All.Count == 0 || Wonders.FoundCount < Wonders.All.Count))
            { next = "Next: explore this world. Its wonders are out there."; label = "Explore"; act = "explore"; }
            else if (!stormThisWorld && Emergency != null && Mission != null && Progress.MissionChapter > 0)
            {
                // You know her by now: she is on the radio if you want her, and "Next world" is as good a step.
                if (MaraHasMore) { next = "Mara's on the radio when you want her."; label = "Answer Mara"; act = "storm"; }
                // Her story's ending waits at home (a world opened after her last chapter points there too).
                else if (StoryEndWaits && !exploring && !away) { next = GoHomeLine; label = "My room"; act = "room"; }
                // After her story, each world's wonders are the next thing (Explore was only in the menu).
                else if (!exploring && !away && Progress.MissionChapter >= Story.Chapters && Wonders != null && (Wonders.All.Count == 0 || Wonders.FoundCount < Wonders.All.Count))
                { next = "Next: explore this world. Its wonders are out there."; label = "Explore"; act = "explore"; }
                else { next = "Ready for another world?"; label = "Next world"; act = "next"; }
            }
            else if (!stormThisWorld && Emergency != null) { next = Mission != null ? "Next: someone is calling for help." : "Next: a meteor storm. Shield it with your hands."; label = Mission != null ? "Answer the call" : "Start the storm"; act = "storm"; }
            else { next = "Ready for another world?"; label = "Next world"; act = "next"; }
            // Arriving, before this world's task has started: no step yet (review 32: for up to 4 s the last world's
            // bright Next world showed, inside the world that was to light the sofa, where the finger had just pressed).
            if (taskVisit != visit && !(Mission != null && Mission.Active)) { next = ""; label = ""; act = ""; }
            Dock.Next = next; Dock.ActionLabel = label; journeyDo = act;
            Dock.Locked = Mission != null && Mission.Active;
            // A hand carrying a piece to the table passes over the dock: nothing on it presses until a moment after.
            Dock.Busy = (Gather != null && (Gather.AnyHeld || (Gather.Running && Time.time - lastTaskMove < 1.2f))) ||
                        (Loose != null && (Loose.Holding(0) || Loose.Holding(1)));
        }

        /// <summary>What this session held, said once when you come back to your room.</summary>
        string Recap()
        {
            var parts = new List<string>();
            if (sessionWorlds > 1) parts.Add(sessionWorlds + " worlds");
            if (sessionWonders > 0) parts.Add(sessionWonders + (sessionWonders == 1 ? " wonder found" : " wonders found"));
            if (sessionMission != Landing.None) parts.Add(Story.Recap(sessionChapter, sessionMission));
            else if (sessionStorm > 0) parts.Add("the storm: " + Sim.OutcomeName(sessionStorm).ToLowerInvariant());
            if (parts.Count == 0) return "";
            string tomorrow = Recipe.Daily(DateTime.UtcNow.AddDays(1)).Def.Short;
            return "This time: " + string.Join(", ", parts) + ".\nTomorrow's world: " + tomorrow + ".";
        }

        /// <summary>The always-there buttons: the map, next world, back to your room (and "do": the bright one above them).</summary>
        void OnDock(string id)
        {
            if (Busy || State != Mode.World) return;
            if (Mission != null && Mission.Active && (id == "next" || id == "worlds")) { Guide.Say("Mara needs you right now.\n\"My room\" still takes you home.", 3f); return; }
            askedAt = Time.unscaledTime;
            if (id == "do")
            {
                if (journeyDo == "storm") { StartMission("dock"); return; }
                if (journeyDo == "camp") { Move?.Home(true, "dock"); return; }
                if (journeyDo == "explore") { StartExplore(); return; }
                if (journeyDo == "room") { Close("dock"); return; }
                if (journeyDo != "next") return;
                id = "next";
            }
            if (id == "worlds") { ShowMap(""); return; }
            if (id == "room") { Close("dock"); return; }
            int n = Featured.Ids.Length, at = -1;
            for (int i = 0; i < n; i++) if (Current != null && Featured.Ids[i] == Current.Biome) at = i;
            int to = at < 0 ? 0 : ((at + (id == "next" ? 1 : -1)) % n + n) % n;
            Open(Recipe.For(Featured.Ids[to], NewSeed()), "dock");
        }

        /// <summary>With no room scan: once, a few seconds after the greeting, Juniper says how to bring your own furniture in.</summary>
        IEnumerator InviteScanSoon(float wait = 5f)
        {
            // After the greeting, when nothing else is on screen (not over the map or the menu).
            float until = Time.unscaledTime + 60f + wait;
            yield return new WaitForSecondsRealtime(wait);
            // (Not over her story's ending either: the first time home after onboarding can be that ending: review 36.)
            while (Time.unscaledTime < until && State == Mode.Room && (storyEnding || (Map != null && Map.Showing) || (Panel != null && Panel.Open) || (Hands != null && !Hands.AnyTracked))) yield return null;
            if (storyEnding) { invitedScan = false; yield break; }
            if (State != Mode.Room || (Hands != null && !Hands.AnyTracked)) { invitedScan = false; yield break; }
            if (Rooms != null && Rooms.Room != null && Rooms.Room.Scanned) yield break;   // it turned up meanwhile (review 32)
            Guide.SetMood(Guide.Mood.Speaking);
            Guide.Say("Want your own sofa and table in the worlds?", 0f);
            Guide.ShowChoices(new[] { "Bring my furniture in", "Not now" }, new[] { "scan:yes", "scan:no" });
            Trace.Info("Room", "offered a scan");
        }

        /// <summary>Forty seconds into a task with nothing picked up: Juniper brings the pieces over, so nobody is stuck.</summary>
        IEnumerator RescueGather()
        {
            int v = visit;
            float t0 = Time.time;
            int placed = 0;
            while (visit == v && State == Mode.World && Gather != null && Gather.Running && Time.time - t0 < 40f)
            {
                if (Gather.PlacedCount != placed || Gather.AnyHeld) { placed = Gather.PlacedCount; t0 = Time.time; }
                yield return null;
            }
            if (visit != v || State != Mode.World || Gather == null || !Gather.Running) yield break;
            StopCoach("rescue");
            Guide.SetMood(Guide.Mood.Speaking);
            Guide.Say("Let me bring those over for you.", 4f);
            Gather.AutoPlace();
        }

        /// <summary>Exploring starts only when asked for (the menu's Explore): the beams appear and jumping is on.</summary>
        void StartExplore()
        {
            if (State != Mode.World || Current == null || Rooms == null || Rooms.Room == null) return;
            if (Gather != null && Gather.Running) { Guide.Say("First put the glowing pieces on your table.", 4f); return; }
            if (Mission != null && Mission.Active) { Guide.Say(Mission.Now == Mission.Step.Farewell ? "Say goodbye to Mara first." : "Mara needs you first.", 3f); return; }
            if (Emergency != null && Emergency.Running)
            {
                if (Emergency.Sim != null && Emergency.Sim.State.Phase >= Sim.Aftermath && !Emergency.RoomBack) Emergency.Stop();
                else { Guide.Say("After the storm.", 3f); return; }
            }
            exploring = true;
            var room = Rooms.Room;
            if (Wonders != null && Wonders.All.Count == 0) { Wonders.Remember(Progress.Wonders); Wonders.Build(Current, Builder, room.Head, room.Forward, LimitFor(Current.Biome, room)); }
            Guide.SetMood(Guide.Mood.Speaking);
            bool pads = (OVRInput.GetActiveController() & OVRInput.Controller.Touch) != 0;
            int wonders = Wonders != null ? Wonders.All.Count : 5;
            string count = wonders == 1 ? "A wonder, under a beam of light." : (wonders >= 2 && wonders <= 4 ? new[] { "Two", "Three", "Four" }[wonders - 2] : "Five") + " wonders,\nunder the beams of light.";
            Guide.Say(count + "\n" + (pads ? "Push the left stick to go." : "Hand out, palm down:\na quick pinch jumps there.") + "\nYou stay in your seat.", 0f);
            if (!pads && Coach != null && Hands != null && Hands.AnyHandTracked) { coachFor = "jump"; Coach.Show(HandCoach.Demo.Jump, ChestSpot(0.06f) + Vector3.up * 0.08f, default, null, null, 1f, 3); }
            Trace.Info("Move", "exploring started", "controllers", pads);
        }

        // ---------- exploring ----------

        /// <summary>How far from camp each world lets you go: the space deck is small, the land is big.</summary>
        float LimitFor(string biome, RoomModel room) => biome switch
        {
            "reef" => 120f,
            "city" => 120f,
            _ => 160f
        };

        /// <summary>The world beyond the table: travel and the five wonders.</summary>
        void OpenLand(Recipe r)
        {
            if (Rooms == null || Rooms.Room == null || r == null) return;
            var room = Rooms.Room;
            float limit = LimitFor(r.Biome, room);
            Move?.Begin(room.Head, limit, r.Biome);
            exploring = false;
            callWaiting = false;
            tipsHere = 0;
            Wonders?.Clear();
            if (Watch != null) Watch.SetObstacles(room);
            Gifts?.Begin(room, r);
            stormThisWorld = false;
            sessionWorlds++;
        }

        void ShowExplore()
        {
            if (State != Mode.World || Progress.ExploreShown) return;
            Progress.ExploreShown = true;
            SaveProgress();
            Guide.SetMood(Guide.Mood.Speaking);
            bool pads = (Hands != null && ((Hands[0].Tracked && Hands[0].Controller) || (Hands[1].Tracked && Hands[1].Controller))) ||
                        (OVRInput.GetActiveController() & OVRInput.Controller.Touch) != 0;
            Guide.Say("There's more out there: five wonders,\nunder the beams of light. " +
                      (pads ? "Push the left stick to go." : "Stay seated: hold a hand out\npalm down; a quick pinch jumps."), 0f);
            if (!pads && Coach != null && Hands != null && Hands.AnyHandTracked)
            {
                coachFor = "jump";
                Coach.Show(HandCoach.Demo.Jump, ChestSpot(0.06f) + Vector3.up * 0.08f, default, null, null, 1f, 3);
            }
            Pads?.ShowNow(10f);
            Trace.Info("Move", "exploring shown", "controllers", pads, "wonders", Wonders != null ? Wonders.All.Count : 0);
        }

        void OnWonder(Wonders.Wonder w)
        {
            sessionWonders++;
            string key = Wonders.Biome + "/" + w.Name;
            if (!Progress.Wonders.Contains(key)) Progress.Wonders.Add(key);
            SaveProgress();
            Guide.SetMood(Guide.Mood.Speaking);
            int found = Wonders.FoundCount, all = Wonders.All.Count;
            string gift = Gifts != null ? Gifts.Give(Wonders.All.IndexOf(w)) : "";
            Guide.Say((found >= all ? "You found " + w.Name + "!\nEvery wonder here, found." : "You found " + w.Name + "!\n" + found + " of " + all) +
                      (gift.Length > 0 ? "\nIt gave you " + gift + ": it's on your table." : ""), 6f);
            Log?.Add("wonder", new Dictionary<string, object> { { "biome", Wonders.Biome }, { "name", w.Name }, { "found", found }, { "of", all } });
            if (found >= all && Current != null) PlayFinale(Current);
            if (found >= all) MetaPlatform.Achieve(MetaPlatform.AllWonders);
        }

        float followAt;

        /// <summary>Away from camp, Juniper comes along: a little ahead and to the right, never in the way.</summary>
        void FollowWhileAway()
        {
            if (State != Mode.World || Move == null || !Move.Away || Guide == null || Head == null) return;
            if (Time.unscaledTime < followAt) return;
            followAt = Time.unscaledTime + 0.5f;
            Vector3 f = Flat(Head.forward), right = Vector3.Cross(Vector3.up, f);
            Vector3 want = Head.position + f * 0.7f + right * 0.32f + Vector3.down * 0.12f;
            if (Vector3.Distance(Guide.transform.position, want) > 0.6f) Guide.Place(want);
        }

        /// <summary>For tests: an answer from Juniper's helper, as if it had just arrived.</summary>
        public void OnActForTests(ActAnswer a, string said) { helperText = said; OnAct(a, new Heard { Act = Act.Unclear }, "test"); }

        /// <summary>For tests: a menu command, as if its button were pressed.</summary>
        public void OnPanelForTests(string cmd) => OnPanel(cmd);
        /// <summary>For tests: a dock button, as if pressed.</summary>
        public void OnDockForTests(string id) => OnDock(id);
        public void OnLocalForTests(string what) => LocalChoice(what);
        public void StartFirstRunForTests() { StartCoroutine(WelcomeThenAnimalsForTests()); }
        IEnumerator WelcomeThenAnimalsForTests()
        {
            yield return Welcome();
            if (State == Mode.Room && !firstAnimalOffered && firstAnimal == null && Maker != null) OfferFirstAnimal();
        }

        void OnWristMenu()
        {
            Map?.Hide();   // one panel at a time (review 42: the wrist opened the menu over the map)
            if (Busy || Panel == null) return;
            if (coachFor == "menu") StopCoach("wrist button");
            Panel.Show(HandPanel.Page.Main);
        }

        void ShowMap(string highlight, string heading = "")
        {
            interview = null;
            if (Map == null) { ShowChoices("Where to?"); return; }
            Help?.Hide();
            Panel?.Hide();   // one panel at a time (the menu's title sat on the map's)
            Guide.ShowChoices(null, null);
            Map.Show(Progress, highlight, State == Mode.World, heading, Frozen);
            if (State == Mode.Room) Guide.Place(Map.Top + Vector3.up * 0.06f);
            roomIdleSince = Time.unscaledTime;
        }

        void ShowHelp()
        {
            Map?.Hide();
            Panel?.Hide();   // one card at a time (the menu's title sat on its rows)
            Guide.ShowChoices(null, null);
            Guide.SetMood(Guide.Mood.Hidden);
            Help?.Show(AppConfig.HasVoice, State != Mode.Room);
            Pads?.ShowNow(12f);
        }

        void OnMap(string id)
        {
            askedAt = Time.unscaledTime;
            if (id.StartsWith("world:", StringComparison.Ordinal))
            {
                string b = id.Substring(6);
                // The first world ever is the one the tutorial was built around.
                if (!Progress.Onboarded && State == Mode.Room && b == "campfire") { Open(Recipe.For("campfire", 1), "first"); return; }
                Open(Recipe.For(b, NewSeed()), "map");
                return;
            }
            switch (id)
            {
                case "describe": StartInterview(); break;
                case "surprise": Apply(Words.Parse("surprise me", Current, NewSeed()), "map"); break;
                case "saved": if (Panel != null) { Panel.SavedList = Progress.Programs; Panel.Show(HandPanel.Page.Saved); } break;
                case "code": Panel?.ShowCodes(); break;
                case "help": ShowHelp(); break;
                case "settings": Map?.Hide(); Panel?.Show(HandPanel.Page.Comfort); break;
                case "end": Map?.Hide(); Close("map"); break;
                case "resume": Map?.Hide(); SetFrozen(false, "map"); break;
            }
        }

        /// <summary>A friend's share code: it becomes a token on your real table (and opens at once if you're in your room).</summary>
        void ReceiveCode(string code)
        {
            var entry = Progress.Receive(code, DateTime.UtcNow);
            if (entry == null)
            {
                Trace.Warn("Director", "code did not read", "code", code);
                Sfx.Instance?.Ui("not_understood", 0.7f);
                Guide.Say("That code doesn't read. Check it with your friend.", 4f);
                return;
            }
            SaveProgress();
            Trace.Info("Director", "code received", "code", entry.Text, "world", entry.Title);
            Sfx.Instance?.Ui("souvenir_chime", 0.8f);
            if (Shelf != null && Rooms != null) { Shelf.Show(Progress, Rooms.Room); Shelf.SetVisible(State == Mode.Room); }
            Guide.Say("A world from a friend: " + entry.Title.ToLowerInvariant() + ".\nIt's on your table. Touch it to go.", 6f);
        }

        /// <summary>Building a place by conversation: a few short questions, then it's built.</summary>
        void StartInterview()
        {
            interview = new Interview(NewSeed());
            askedAt = Time.unscaledTime;
            Log?.Add("describe", null);
            AskNext();
        }

        void AskNext()
        {
            if (interview == null) return;
            if (interview.Done)
            {
                var r = interview.Finish();
                interview = null;
                Guide.ShowChoices(null, null);
                Guide.SetMood(Guide.Mood.Speaking);
                Guide.Say("Done. Here it is.", 2.5f);
                Open(r, "described");
                return;
            }
            var q = interview.Current;
            var labels = new List<string>();
            var ids = new List<string>();
            for (int i = 0; i < q.Choices.Count; i++) { labels.Add(q.Choices[i].Label); ids.Add("ask:" + i); }
            Guide.SetMood(Guide.Mood.Speaking);
            Guide.Say(q.Text, 0f);
            Guide.ShowChoices(labels.ToArray(), ids.ToArray());
            if (Rooms != null && Rooms.Room != null && State == Mode.Room) PlaceGuide(Rooms.Room);
        }

        void OpenDaily(string how)
        {
            var d = Recipe.Daily(DateTime.UtcNow);
            Guide.Say(d.Title + ".", 3f);
            Open(d, "daily");
        }

        void Save(string name)
        {
            if (Current == null) { Guide.Say("Open a world first, then save it.", 3f); return; }
            var s = Progress.Save(name, Current, DateTime.UtcNow);
            SaveProgress();
            Trace.Info("Director", "saved world", "name", s.Name, "code", ShareCode.Encode(Current));
            Guide.Say("Saved as \"" + s.Name + "\".\nShare code " + ShareCode.Encode(Current), 6f);
            Sfx.Instance?.Ui("souvenir_chime", 0.5f);
            Log?.Add("save", new Dictionary<string, object> { { "code", ShareCode.Encode(Current) } });
        }

        /// <summary>Comfort & access settings, into every part they touch (cheap: called every frame).</summary>
        void ApplySettings()
        {
            LegibleText.Scale = Progress.CaptionsLarge ? 1.35f : 1f;
            Effects.Calm = Progress.CalmEffects;
            Emergency.Gentle = Progress.GentleStorm;
            Emergency.EyesToo = Progress.LookToPress;
            if (Pokes != null) Pokes.AlwaysDwell = Progress.LookToPress;
            if (Sfx.Instance != null) Sfx.Instance.Master = Progress.Muted ? 0f : 2.2f * (Progress.SoundLevel == 0 ? 0.5f : Progress.SoundLevel == 2 ? 1.45f : 1f);
            AudioListener.volume = Progress.Muted ? 0f : 1f;
            if (Panel != null && Panel.Settings != Progress) { Panel.Settings = Progress; Panel.RunningText = RunningText; Panel.RecordsText = () => Records.Text(Progress); }
        }

        int tipsHere, tipIndex;
        float lastTip = -100f;
        static readonly string[] Tips =
        {
            "Juniper, make it snow", "Juniper, a volcano", "Juniper, add northern lights", "Juniper, make me a lantern",
            "Juniper, at sunset", "Juniper, take me to Mars", "Juniper, make me a dinosaur", "Juniper, freeze"
        };

        /// <summary>Voice on, nothing asked for a while, nothing else going on: Juniper suggests one thing to say (twice a world at most).</summary>
        void VoiceTip()
        {
            if (State != Mode.World || Guide == null || !Progress.VoiceOn || tipsHere >= 2) return;
            float now = Time.unscaledTime;
            if (now - lastTip < 60f || now - Mathf.Max(askedAt, lastTaskMove) < 45f) return;
            if ((Mission != null && Mission.Active) || (Gather != null && Gather.Running) || (Emergency != null && Emergency.Running) || (Type != null && Type.Open)) return;
            if (Panel != null && Panel.Open) return;
            lastTip = now;
            tipsHere++;
            string tip = Tips[tipIndex++ % Tips.Length];
            Guide.SetMood(Guide.Mood.Speaking);
            Guide.Say("Try saying \u201C" + tip + "\u201D", 5f);
            Trace.Info("Ears", "tip", "n", tipsHere);
        }

        /// <summary>Juniper's sound controls. Peace: music off, everything quiet, calm effects, still air, no tips.</summary>
        void SoundCommand(string what, string reply)
        {
            switch (what)
            {
                case "mute": Progress.Muted = true; break;   // silent with everything else; it comes back with the sound
                case "on": Progress.Muted = false; if (Progress.SoundLevel == 0) Progress.SoundLevel = 1; break;
                case "down": Progress.Muted = false; Progress.SoundLevel = Mathf.Max(0, Progress.SoundLevel - 1); break;
                case "up": Progress.Muted = false; Progress.SoundLevel = Mathf.Min(2, Progress.SoundLevel + 1); break;
                case "peace":
                    Progress.Muted = false;
                    Progress.SoundLevel = 0;
                    Progress.CalmEffects = true;
                    PlayMusic(false);
                    tipsHere = 99;
                    if (State == Mode.World && Current != null && !(Emergency != null && Emergency.Running))
                    {
                        var r = Current.Copy();
                        r.Wind = Mathf.Min(r.Wind, 0.1f);
                        if (r.Precip == Precip.Rain && r.Wind > 0.7f) r.Precip = Precip.None;
                        Edit(r.Clamp(), "peace");
                    }
                    break;
            }
            SaveProgress();
            ApplySettings();
            Guide.SetMood(Guide.Mood.Speaking);
            Guide.Say(reply, 3f);
            Trace.Info("Settings", "sound", "what", what, "level", Progress.SoundLevel, "muted", Progress.Muted);
            Log?.Add("sound", new Dictionary<string, object> { { "what", what } });
        }

        /// <summary>A tour from your seat: Juniper takes you to each wonder in turn (short jumps), then home to camp.</summary>
        void StartTour()
        {
            if (State != Mode.World || Move == null) { Guide.Say("Open a world first.", 3f); return; }
            if (Mission != null && Mission.Active) { Guide.Say("After Mara's call.", 3f); return; }
            if (!exploring) StartExplore();
            if (!exploring || Wonders == null || Wonders.All.Count == 0) return;
            if (touring != null) StopCoroutine(touring);
            touring = StartCoroutine(Tour(visit));
        }

        Coroutine touring;
        public bool Touring => touring != null;

        IEnumerator Tour(int v)
        {
            Trace.Info("Move", "tour started", "wonders", Wonders.All.Count);
            Guide.SetMood(Guide.Mood.Speaking);
            Guide.Say("Sit back. I'll take you round.", 3f);
            yield return new WaitForSecondsRealtime(2.5f);
            var stops = new List<Wonders.Wonder>(Wonders.All);
            foreach (var w in stops)
            {
                if (visit != v || State != Mode.World || (Mission != null && Mission.Active)) { touring = null; yield break; }
                // A few metres short of it, looking at it.
                Vector3 from = Head.position; from.y = 0f;
                Vector3 to = w.At; to.y = 0f;
                Vector3 dir = (to - from).sqrMagnitude > 0.01f ? (to - from).normalized : Vector3.forward;
                if (!Move.TeleportTo(w.At - dir * 2.2f, dir, "tour")) continue;
                Guide.Say(w.Name + ".", 4f);
                yield return new WaitForSecondsRealtime(6f);
            }
            if (visit == v && State == Mode.World)
            {
                Move.Home(true, "tour");
                Guide.Say("That's the tour. Ask for another world any time.", 4f);
            }
            touring = null;
        }

        object SettingValue(string which) => which switch
        {
            "captions" => (object)Progress.CaptionsLarge, "gentle" => Progress.GentleStorm, "calm" => Progress.CalmEffects,
            "look" => Progress.LookToPress, "mute" => Progress.Muted, "music" => Progress.MusicOn, "call" => Progress.CallWhenReady, "sound" => Progress.SoundLevel,
            _ => ""
        };

        void ChangeSetting(string which)
        {
            if (which == "newplayer")
            {
                // Asked first: it clears everything this headset remembers.
                Panel?.Hide();
                Guide.SetMood(Guide.Mood.Speaking);
                Guide.Say("Start fresh for a new player?\nSouvenirs and progress are cleared;\ncomfort settings stay.", 0f);
                Guide.ShowChoices(new[] { "Start fresh", "Keep everything" }, new[] { "new:yes", "new:no" });
                return;
            }
            switch (which)
            {
                case "captions": Progress.CaptionsLarge = !Progress.CaptionsLarge; break;
                case "gentle": Progress.GentleStorm = !Progress.GentleStorm; break;
                case "calm": Progress.CalmEffects = !Progress.CalmEffects; break;
                case "look": Progress.LookToPress = !Progress.LookToPress; break;
                case "mute": Progress.Muted = !Progress.Muted; break;
                case "music": Progress.MusicOn = !Progress.MusicOn; worldTune = false; break;
                case "call": Progress.CallWhenReady = !Progress.CallWhenReady; break;
                case "sound": Progress.SoundLevel = (Progress.SoundLevel + 1) % 3; break;
                default: return;
            }
            SaveProgress();
            ApplySettings();
            Trace.Info("Settings", "changed", "which", which);
            Log?.Add("setting", new Dictionary<string, object> { { "which", which }, { "value", SettingValue(which) } });
            if (which == "sound") Sfx.Instance?.Ui("ack", 0.8f);
        }

        /// <summary>The How it's running page: the app's own health, live.</summary>
        string RunningText()
        {
            var sb = new System.Text.StringBuilder();
            // A first diagnosis a player can screenshot: which build, this world's frame times, memory, errors.
            string build = Application.buildGUID ?? "";
            sb.Append("Worldeck ").Append(Application.version).Append(Debug.isDebugBuild ? " (test build" : " (release")
              .Append(build.Length >= 8 ? ", " + build.Substring(0, 8) : "").Append(")\n");
            if (Log != null)
            {
                if (Log.Frames.Count > 0) sb.Append("This world: ").Append(Log.Frames.Line()).Append('\n');
                sb.Append("Memory ").Append(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024)).Append(" MB, errors ").Append(Log.Errors.Total);
                if (!RelayGate.Open) sb.Append(", helper paused (").Append(RelayGate.Why).Append(')');
                sb.Append('\n');
            }
            foreach (var e in Health.All)
            {
                string mark = e.Level == Health.Level.Ok ? "ok" : e.Level == Health.Level.Fallback ? "backup" : "problem";
                string detail = e.Detail ?? "";
                if (detail.Length > 46) detail = detail.Substring(0, 45) + "\u2026";
                sb.Append(e.Part).Append(": ").Append(mark).Append(detail.Length > 0 ? " (" + detail + ")" : "").Append('\n');
            }
            sb.Append("Your words and voice are never stored.");
            return sb.ToString();
        }

        void OnPanel(string cmd)
        {
            askedAt = Time.unscaledTime;
            Trace.Info("Panel", "command", "cmd", cmd);
            if (cmd.StartsWith("set:", StringComparison.Ordinal)) { ChangeSetting(cmd.Substring(4)); return; }
            if (cmd.StartsWith("say:", StringComparison.Ordinal)) { Handle(cmd.Substring(4), "say-page"); return; }
            if (cmd.StartsWith("code:", StringComparison.Ordinal)) { ReceiveCode(cmd.Substring(5)); return; }
            if (cmd.StartsWith("world:", StringComparison.Ordinal)) { Open(Recipe.For(cmd.Substring(6), NewSeed()), "panel"); return; }
            if (cmd.StartsWith("saved:", StringComparison.Ordinal))
            {
                if (int.TryParse(cmd.Substring(6), out int i) && i >= 0 && i < Progress.Programs.Count)
                {
                    var p = Progress.Programs[i];
                    p.Visits++;
                    var r = p.Recipe.Copy(); r.Title = p.Name;
                    Open(r, "saved");
                }
                return;
            }
            switch (cmd)
            {
                case "talk": StartTalking(); break;
                case "undo": Undo(); break;
                case "freeze": SetFrozen(true, "asked"); break;
                case "resume": SetFrozen(false, ""); break;
                case "save": Save(""); break;
                case "end": Close("panel"); break;
                case "today": OpenDaily("panel"); break;
                case "describe": StartInterview(); break;
                case "code": Panel?.ShowCodes(); break;
                case "map": ShowMap(""); break;
                case "voice": SetVoice(!Progress.VoiceOn); break;
                case "type": if (Type != null) Type.Show(); break;
                case "tour": StartTour(); break;
                case "camp": Move?.Home(true, "menu"); break;
                case "explore": StartExplore(); break;
                case "storm": StartStorm("menu"); break;
                case "rewind": if (Emergency != null && Emergency.Running) Emergency.RewindToFirstMiss(); break;
                case "stepmove":
                    Progress.StepMove = !Progress.StepMove; SaveProgress();
                    Guide.Say(Progress.StepMove ? "Steps: the stick hops you forward. Nothing slides." : "Smooth: the stick glides where you look.", 4f);
                    Trace.Info("Move", "comfort", "steps", Progress.StepMove);
                    break;
                case "help": ShowHelp(); break;
                case "surprise": Apply(Words.Parse("surprise me", Current, NewSeed()), "panel"); break;
                case "scan":
                    Guide.Say("Opening Quest's room setup…", 3f);
                    if (Rooms != null) _ = Rooms.Load(true);
                    break;
            }
        }

        // ---------- helpers ----------

        void SetState(Mode m)
        {
            if (State != m) Trace.Info("Director", "state", "from", State.ToString(), "to", m.ToString(), "world", Current != null ? Current.Line() : "-");
            if (m == Mode.World && State != Mode.World) worldSince = Time.unscaledTime;
            State = m;
            if (Panel != null)
            {
                Panel.SavedList = Progress.Programs;
                Panel.Footer = m == Mode.World && Current != null ? "Share code " + ShareCode.Encode(Current) :
                    "Today: " + Recipe.Daily(DateTime.UtcNow).Def.Short + (Progress.DailyStreak > 1 ? $" · {Progress.DailyStreak}-day streak" : "") +
                    $" · {Progress.Souvenirs.Count} of {Kit.Biomes.Length} souvenirs";
            }
            Changed?.Invoke(m);
        }

        int seedCounter;
        int NewSeed() => (int)((DateTime.UtcNow.Ticks / 10000) % 4000) + (seedCounter++ * 7) % 96 + 1;

        Vector3 RevealOrigin(RoomModel room)
        {
            Vector3 head = room.Head;
            Vector3 f = room.Forward;
            float best = float.MaxValue;
            Vector3 hit = head + f * 2f;
            foreach (var w in room.Walls)
            {
                // Ray (head, f) against the wall segment in the horizontal plane.
                Vector2 o = new Vector2(head.x, head.z), d = new Vector2(f.x, f.z), a = new Vector2(w.A.x, w.A.z), b = new Vector2(w.B.x, w.B.z);
                Vector2 e = b - a;
                float den = d.x * e.y - d.y * e.x;
                if (Mathf.Abs(den) < 1e-5f) continue;
                Vector2 ao = a - o;
                float t = (ao.x * e.y - ao.y * e.x) / den;
                float u = (ao.x * d.y - ao.y * d.x) / den;
                if (t > 0.2f && u >= 0f && u <= 1f && t < best) { best = t; hit = head + f * t; }
            }
            hit.y = head.y - 0.1f;
            return hit;
        }

        void MakeTrace()
        {
            var go = new GameObject("Table trace");
            go.transform.SetParent(transform, false);
            tableTrace = go.AddComponent<LineRenderer>();
            tableTrace.useWorldSpace = true;
            tableTrace.loop = true;
            tableTrace.widthMultiplier = 0.006f;
            tableTrace.sharedMaterial = GlowFlat;
            tableTrace.startColor = tableTrace.endColor = new Color(1f, 0.62f, 0.25f, 1f);
            tableTrace.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tableTrace.enabled = false;
        }

        void TraceTable(RoomModel room, bool on)
        {
            if (tableTrace == null) return;
            tableTrace.enabled = on && room.Table != null;
            if (!on || room.Table == null) return;
            var t = room.Table;
            float hx = t.Size.x * 0.5f, hz = t.Size.z * 0.5f, y = t.Size.y + 0.004f;
            tableTrace.positionCount = 4;
            tableTrace.SetPositions(new[] { t.ToWorld(new Vector3(-hx, y, -hz)), t.ToWorld(new Vector3(-hx, y, hz)), t.ToWorld(new Vector3(hx, y, hz)), t.ToWorld(new Vector3(hx, y, -hz)) });
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward; }

        /// <summary>True once the saved progress has been read: nothing is written before that.</summary>
        public bool ProgressLoaded { get; private set; }
        bool saveWanted;

        void LoadProgress()
        {
            ProgressLoaded = true;
            try
            {
                string text = File.Exists(progressPath) ? File.ReadAllText(progressPath) : "";
                Progress = Progress.Parse(text, out bool damaged);
                string tmp = progressPath + ".tmp";
                if (damaged && File.Exists(tmp))   // only a damaged file: a deleted one means start fresh
                {
                    var backup = Progress.Parse(File.ReadAllText(tmp), out bool backupDamaged);
                    if (!backupDamaged) { Progress = backup; damaged = false; Trace.Warn("App", "progress restored from the save copy"); }
                }
                if (damaged)
                {
                    Health.Set(Health.Save, Health.Level.Fallback, "progress file unreadable, started fresh", Time.realtimeSinceStartup);
                    try { File.Copy(progressPath, progressPath + ".damaged", true); } catch { }
                }
            }
            catch (Exception e)
            {
                Progress = new Progress();
                Health.Set(Health.Save, Health.Level.Fallback, e.Message, Time.realtimeSinceStartup);
            }
        }

        public void SaveProgress()
        {
            // Something can ask for a save before Start has run (a room callback on the first frame).
            if (string.IsNullOrEmpty(progressPath)) progressPath = Path.Combine(Application.persistentDataPath, "worldeck-progress.json");
            // Before the saved progress is read, a save would write an empty one over it (this wiped every player's
            // keepsakes and chapter on each launch). Wait: Start loads it, and saves straight after.
            if (!ProgressLoaded) { if (!saveWanted) Trace.Warn("App", "save asked before progress was loaded: held"); saveWanted = true; return; }
            try
            {
                // Write a complete copy first; the main file is only overwritten from it, and loading falls back
                // to the copy if the main file is missing or damaged (a kill mid-save loses nothing).
                string tmp = progressPath + ".tmp";
                File.WriteAllText(tmp, Progress.ToJsonString());
                File.Copy(tmp, progressPath, true);
            }
            catch (Exception e) { Health.Set(Health.Save, Health.Level.Fallback, "save failed: " + e.Message, Time.realtimeSinceStartup); }
        }

        /// <summary>For tests: start from a fresh (or given) progress.</summary>
        /// <summary>Hands the headset to someone new: back to your room, nothing remembered, and the introduction plays.</summary>
        IEnumerator NewPlayer()
        {
            if (State == Mode.World) Close("new player");
            float until = Time.unscaledTime + 20f;
            while (State != Mode.Room && Time.unscaledTime < until) yield return null;
            Hints.Instance?.Forget();
            waveTaught = 0;
            // Comfort settings are about the headset's owner and the room, not the game: they stay.
            var p0 = Progress;
            ResetProgress(new Progress
            {
                CaptionsLarge = p0.CaptionsLarge, GentleStorm = p0.GentleStorm, CalmEffects = p0.CalmEffects, LookToPress = p0.LookToPress,
                StepMove = p0.StepMove, SoundLevel = p0.SoundLevel, Muted = p0.Muted
            });
            Trace.Info("Tutorial", "new player: progress and learned hints cleared");
            Log?.Add("new_player", new Dictionary<string, object>());
            if (!inFirstRun) yield return FirstRun();
        }

        public void ResetProgress(Progress p = null)
        {
            ProgressLoaded = true; Progress = p ?? new Progress(); firstAnimalOffered = false; firstAnimal = null; SaveProgress();
            if (Shelf != null && Rooms != null && Rooms.Room != null && State == Mode.Room) Shelf.Show(Progress, Rooms.Room);
            RoomGreeting();
        }

#if UNITY_EDITOR
        void EditorKeys()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.spaceKey.wasPressedThisFrame) OnGuideTapped();
            if (kb.escapeKey.wasPressedThisFrame) Close("key");
            for (int i = 0; i < Kit.Biomes.Length && i < 8; i++)
                if (kb[(UnityEngine.InputSystem.Key)((int)UnityEngine.InputSystem.Key.Digit1 + i)].wasPressedThisFrame)
                    Open(Recipe.For(Kit.Biomes[i].Id, NewSeed()), "key");
        }
#endif
    }
}
