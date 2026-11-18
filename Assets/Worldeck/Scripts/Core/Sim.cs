using System;
using System.Collections.Generic;

namespace Worldeck.Core
{
    /// <summary>Everything the emergency simulation knows at one moment. A plain value: copying it is a snapshot.</summary>
    public struct SimState
    {
        public float T;                          // scenario seconds
        public int Phase;                        // Sim.Calm … Sim.Ended
        public float Shields, Engines, Lights;   // power shares, always summing to 100
        public float ShieldCharge;               // 0–100
        public float Hull;                       // 100–0
        public int Broken;                       // bit per station
        public int Repairing;                    // station being repaired, -1 none
        public float RepairLeft;
        public float EvadeUntil, EvadeReadyAt;   // an evade covers strikes until EvadeUntil
        public int Next;                         // index of the next strike to resolve
        public int Blocked, Dodged, Taken;
        public int Outcome;                      // Sim.None … Sim.Battered
        public int HitMask;                      // bit per strike that got through

        /// <summary>A number that changes if anything in the state does (for tests and the rewind check in logs).</summary>
        public int Hash()
        {
            unchecked
            {
                int h = 17;
                void Add(float f) { h = h * 31 + (int)Math.Round(f * 100f); }
                Add(T); h = h * 31 + Phase; Add(Shields); Add(Engines); Add(Lights); Add(ShieldCharge); Add(Hull);
                h = h * 31 + Broken; h = h * 31 + Repairing; Add(RepairLeft); Add(EvadeUntil); Add(EvadeReadyAt);
                h = h * 31 + Next; h = h * 31 + Blocked; h = h * 31 + Dodged; h = h * 31 + Taken; h = h * 31 + Outcome; h = h * 31 + HitMask;
                return h;
            }
        }
    }

    /// <summary>One meteor of the storm: when it arrives, from where (degrees around you), how big (0.6–1.6).</summary>
    public struct Strike
    {
        public float Time, Bearing, Size;
        public int Station;   // the station it breaks if it gets through
        public int Kind;      // Sim.Single, or Sim.Big (two palms, or one held steady)
    }

    /// <summary>What happened to a strike (for the views: a ripple on the shield, sparks, a near miss).</summary>
    public enum StrikeResult { Blocked, Dodged, Hit }

    /// <summary>
    /// The emergency: a deterministic simulation where what you do decides what happens. A storm of meteors is
    /// fixed by a seed; power goes to shields (they charge and block), engines (you can evade) or lights; hits that
    /// get through cost hull and break a station (which weakens what it feeds, until repaired). The only way to
    /// change anything is an action below, each checked; a snapshot is kept every second so time can be rewound,
    /// and the same seed with the same actions always ends the same way. No engine types: tested without Unity.
    /// </summary>
    public sealed class Sim
    {
        public const int Calm = 0, Warning = 1, Storm = 2, Aftermath = 3, Ended = 4;
        public const int None = 0, Untouched = 1, Scraped = 2, Battered = 3;
        /// <summary>Kinds of meteor: an ordinary one; a big one.</summary>
        public const int Single = 0, Big = 2;
        public const int Stations = 3;           // 0 power station, 1 lamp bank, 2 sensor desk
        public const float WarningSeconds = 8f, RepairSeconds = 4f, EvadeSeconds = 3f, EvadeCooldown = 8f;
        public const float BlockCost = 26f, HitDamage = 17f, ChargePerSecond = 9f;

        public SimState State;
        public readonly List<Strike> Strikes = new List<Strike>();
        public int Seed { get; private set; }
        /// <summary>If set, this alone decides whether a strike is blocked (a palm in its way); shield charge is not used.</summary>
        public Func<Strike, bool> Blocker;
        public event Action<Strike, StrikeResult> Struck;
        public event Action<int> Broke, Repaired;
        public event Action<int> PhaseChanged;

        readonly SimState[] snaps = new SimState[120];
        int snapCount, snapHead;
        float lastSnapT = -1f;

        public Sim(int seed = 1, int count = 9) { Reset(seed, count); }
        public Sim(int seed, Pace pace) { Reset(seed, pace.Count, pace.GapMin, pace.GapMax, pace.SlowStart); }
        /// <summary>Her storms grow with her story: from <paramref name="level"/> 2 (her third chapter) the last
        /// meteor is a big one.</summary>
        public Sim(int seed, Pace pace, int level) { Reset(seed, pace.Count, pace.GapMin, pace.GapMax, pace.SlowStart, level); }

        public void Reset(int seed, int count = 9, float gapMin = 2.6f, float gapMax = 4.6f, float slowStart = 0f, int level = 0)
        {
            Seed = seed;
            Strikes.Clear();
            var rng = new Rng(seed * 7 + 3);
            float t = WarningSeconds;
            for (int i = 0; i < count; i++)
            {
                t += i == 0 ? 5f + slowStart : rng.Range(gapMin, gapMax) + (i < 3 ? slowStart : 0f);
                Strikes.Add(new Strike { Time = t, Bearing = rng.Range(-70f, 70f), Size = rng.Range(0.7f, 1.5f), Station = rng.Range(0, Stations) });
            }
            if (level >= 2 && Strikes.Count > 0)
            {
                // The last one is big, and comes a little later, so it is seen coming.
                var last = Strikes[Strikes.Count - 1];
                last.Time += 1.5f; last.Size = 1.6f; last.Kind = Big;
                Strikes[Strikes.Count - 1] = last;
            }
            State = new SimState { Shields = 20f, Engines = 40f, Lights = 40f, ShieldCharge = 20f, Hull = 100f, Repairing = -1, Phase = Calm };
            snapCount = 0; snapHead = 0; lastSnapT = -1f;
            Snap();
        }

        public float StormEnd => Strikes.Count > 0 ? Strikes[Strikes.Count - 1].Time + 2.5f : WarningSeconds;
        public bool IsBroken(int station) => (State.Broken & (1 << station)) != 0;
        public bool CanEvade => State.Engines >= 50f && State.T >= State.EvadeReadyAt && State.Phase >= Warning && State.Phase <= Storm;

        // ---------- actions: the only way the world changes ----------

        /// <summary>Starts the scenario (the warning, then the storm).</summary>
        public bool Begin()
        {
            if (State.Phase != Calm) return false;
            SetPhase(Warning);
            // The earliest moment to rewind to is the start of the warning, never the calm before it.
            snapCount = 0; snapHead = 0;
            Snap();
            return true;
        }

        /// <summary>Sends most of the power to one system: "shields", "engines" or "lights".</summary>
        public bool Divert(string to)
        {
            switch ((to ?? "").Trim().ToLowerInvariant())
            {
                case "shields": case "shield": State.Shields = 70f; State.Engines = 15f; State.Lights = 15f; return true;
                case "engines": case "engine": State.Shields = 15f; State.Engines = 70f; State.Lights = 15f; return true;
                case "lights": case "light": State.Shields = 15f; State.Engines = 15f; State.Lights = 70f; return true;
                case "balance": case "even": State.Shields = 34f; State.Engines = 33f; State.Lights = 33f; return true;
                default: return false;
            }
        }

        /// <summary>Starts repairing a broken station (it takes RepairSeconds).</summary>
        public bool Repair(int station)
        {
            if (station < 0 || station >= Stations || !IsBroken(station) || State.Repairing >= 0) return false;
            State.Repairing = station;
            State.RepairLeft = RepairSeconds;
            return true;
        }

        /// <summary>With the engines powered: strikes in the next EvadeSeconds miss.</summary>
        public bool Evade()
        {
            if (!CanEvade) return false;
            State.EvadeUntil = State.T + EvadeSeconds;
            State.EvadeReadyAt = State.T + EvadeCooldown;
            return true;
        }

        /// <summary>Breaks a station now ("damage the left panel").</summary>
        public bool Damage(int station)
        {
            if (station < 0 || station >= Stations || IsBroken(station)) return false;
            State.Broken |= 1 << station;
            Broke?.Invoke(station);
            return true;
        }

        /// <summary>Back in time by <paramref name="seconds"/> (to the nearest snapshot at or before then).</summary>
        public bool Rewind(float seconds)
        {
            if (!TryPast(seconds, out var s)) return false;
            State = s;
            // Snapshots later than the restored moment are a future that no longer happened.
            while (snapCount > 1 && Newest.T > State.T + 0.01f) { snapHead = (snapHead - 1 + snaps.Length) % snaps.Length; snapCount--; }
            lastSnapT = State.T;
            PhaseChanged?.Invoke(State.Phase);
            return true;
        }

        /// <summary>The state as it was <paramref name="seconds"/> ago (without going there).</summary>
        public bool TryPast(float seconds, out SimState past)
        {
            past = default;
            if (seconds <= 0f || snapCount == 0) return false;
            float want = Math.Max(0f, State.T - seconds);
            bool found = false;
            for (int i = 0; i < snapCount; i++)
            {
                var s = snaps[(snapHead - 1 - i + snaps.Length * 2) % snaps.Length];
                if (s.T <= want + 0.01f) { past = s; found = true; break; }
            }
            if (!found) past = snaps[(snapHead - snapCount + snaps.Length * 2) % snaps.Length];   // the oldest we have
            return true;
        }

        SimState Newest => snaps[(snapHead - 1 + snaps.Length) % snaps.Length];

        void Snap()
        {
            snaps[snapHead] = State;
            snapHead = (snapHead + 1) % snaps.Length;
            if (snapCount < snaps.Length) snapCount++;
            lastSnapT = State.T;
        }

        // ---------- time ----------

        public void Step(float dt)
        {
            if (dt <= 0f || State.Phase == Calm || State.Phase >= Aftermath) return;
            // Small fixed steps: the same result whatever the frame rate.
            while (dt > 1e-6f)
            {
                float h = Math.Min(dt, 0.05f);
                dt -= h;
                Advance(h);
                if (State.Phase == Ended) break;
            }
        }

        void Advance(float h)
        {
            State.T += h;
            float feed = IsBroken(0) ? 0.5f : 1f;
            State.ShieldCharge = Math.Min(100f, State.ShieldCharge + ChargePerSecond * (State.Shields / 100f) * feed * h);
            if (State.Repairing >= 0)
            {
                State.RepairLeft -= h;
                if (State.RepairLeft <= 0f)
                {
                    int st = State.Repairing;
                    State.Broken &= ~(1 << st);
                    State.Repairing = -1;
                    State.RepairLeft = 0f;
                    Repaired?.Invoke(st);
                }
            }
            if (State.Phase == Warning && State.T >= WarningSeconds) SetPhase(Storm);
            while (State.Next < Strikes.Count && Strikes[State.Next].Time <= State.T)
            {
                var s = Strikes[State.Next++];
                StrikeResult r;
                if (State.T <= State.EvadeUntil) { State.Dodged++; r = StrikeResult.Dodged; }
                else if (Blocker != null ? Blocker(s) : State.ShieldCharge >= BlockCost * s.Size)
                {
                    if (Blocker == null) State.ShieldCharge -= BlockCost * s.Size;
                    State.Blocked++; r = StrikeResult.Blocked;
                }
                else
                {
                    State.ShieldCharge = Math.Max(0f, State.ShieldCharge - BlockCost * s.Size * 0.5f);
                    State.Hull = Math.Max(0f, State.Hull - HitDamage * s.Size);
                    State.Taken++;
                    State.HitMask |= 1 << (State.Next - 1);
                    r = StrikeResult.Hit;
                    if (Blocker == null && !IsBroken(s.Station)) { State.Broken |= 1 << s.Station; Broke?.Invoke(s.Station); }
                }
                Struck?.Invoke(s, r);
            }
            if (State.Phase == Storm && State.T >= StormEnd)
            {
                State.Outcome = State.Taken == 0 ? Untouched : State.Taken <= 2 ? Scraped : Battered;
                SetPhase(Aftermath);
            }
            if (State.T - lastSnapT >= 1f) Snap();
        }

        void SetPhase(int p)
        {
            if (State.Phase == p) return;
            State.Phase = p;
            PhaseChanged?.Invoke(p);
        }

        /// <summary>The player has seen the ending.</summary>
        public void Finish() { if (State.Phase == Aftermath) SetPhase(Ended); }

        /// <summary>The storm has won (everything it was aimed at is broken): it ends here.</summary>
        public void Surrender()
        {
            if (State.Phase != Warning && State.Phase != Storm) return;
            State.Outcome = Battered;
            SetPhase(Aftermath);
        }

        /// <summary>Seconds until the next strike (∞ when none are left).</summary>
        public float NextStrikeIn => State.Next < Strikes.Count ? Strikes[State.Next].Time - State.T : float.PositiveInfinity;

        public static string OutcomeName(int o) => o == Untouched ? "Untouched" : o == Scraped ? "Scraped" : o == Battered ? "Battered" : "";
        public static string StationName(int s) => s == 0 ? "power station" : s == 1 ? "lamp bank" : "sensor desk";
    }
}
