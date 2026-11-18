using System;
using System.Globalization;

namespace Worldeck.Core
{
    /// <summary>
    /// Frame times over a stretch of play (a round), as percentiles. A fixed histogram of quarter-millisecond
    /// bins up to 100 ms, so it costs the same few kilobytes after ten frames or ten hours, allocates nothing
    /// per frame, and every round in every tester's session carries its own measured frame rate.
    /// Engine-independent.
    /// </summary>
    public sealed class FrameStats
    {
        public const double BinMs = 0.25;
        public const int Bins = 400;              // 0–100 ms; longer frames go in the last bin and in MaxMs

        readonly int[] bins = new int[Bins];
        double sumMs;

        public int Count { get; private set; }
        public double MaxMs { get; private set; }
        /// <summary>The display's refresh rate the frames are judged against (Hz).</summary>
        public double RefreshHz { get; private set; } = 72;
        /// <summary>Frames longer than 1.5 frame budgets (a missed frame on the headset).</summary>
        public int Late { get; private set; }

        /// <summary>The histogram's size never changes (tests: memory stays flat however long the play).</summary>
        public int Capacity => bins.Length;

        public void Reset(double refreshHz)
        {
            Array.Clear(bins, 0, bins.Length);
            sumMs = 0; Count = 0; MaxMs = 0; Late = 0;
            RefreshHz = refreshHz > 1 ? refreshHz : 72;
        }

        public void Add(double seconds)
        {
            if (seconds <= 0 || double.IsNaN(seconds)) return;
            double ms = seconds * 1000.0;
            int b = (int)(ms / BinMs);
            if (b >= Bins) b = Bins - 1;
            bins[b]++;
            Count++;
            sumMs += ms;
            if (ms > MaxMs) MaxMs = ms;
            if (ms > 1.5 * BudgetMs) Late++;
        }

        public double BudgetMs => 1000.0 / RefreshHz;
        public double MeanMs => Count > 0 ? sumMs / Count : double.NaN;
        public double MeanFps => Count > 0 ? 1000.0 / MeanMs : double.NaN;
        public double LatePercent => Count > 0 ? 100.0 * Late / Count : 0;

        /// <summary>The frame time (ms) that this fraction of frames came in at or under (p = 0.5, 0.95, 0.99).</summary>
        public double Percentile(double p)
        {
            if (Count == 0) return double.NaN;
            long rank = (long)Math.Ceiling(Math.Max(0, Math.Min(1, p)) * Count);
            if (rank < 1) rank = 1;
            long seen = 0;
            for (int i = 0; i < Bins; i++)
            {
                seen += bins[i];
                if (seen >= rank) return i == Bins - 1 ? MaxMs : Math.Min((i + 1) * BinMs, MaxMs);
            }
            return MaxMs;
        }

        /// <summary>"p50 11.1 ms · p99 12.5 ms · 0.4 % late at 90 Hz" (the device log and the round record).</summary>
        public string Line()
        {
            if (Count == 0) return "no frames";
            var c = CultureInfo.InvariantCulture;
            return "p50 " + Percentile(0.5).ToString("0.0", c) + " ms, p95 " + Percentile(0.95).ToString("0.0", c) +
                   " ms, p99 " + Percentile(0.99).ToString("0.0", c) + " ms, max " + MaxMs.ToString("0.0", c) + " ms, " +
                   LatePercent.ToString("0.0", c) + " % late at " + RefreshHz.ToString("0", c) + " Hz (" + Count + " frames)";
        }
    }
}
