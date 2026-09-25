using System.Diagnostics;
using UnityEngine;

namespace reromanlee.BlockyMesher
{
    /// <summary>
    /// Main-thread time spent per frame, between <see cref="Begin"/> and <see cref="End"/> calls:
    /// the average and the worst frame over roughly the last second. Its running total for the
    /// current frame is the budget everything in a landscape shares.
    /// </summary>
    internal sealed class FrameTimer
    {
        long started;
        bool timing;
        double frameMs;
        int frame = -1;
        double windowSum;
        double windowPeak;
        int windowFrames;
        float windowStart;

        public float AverageMs { get; private set; }
        public float PeakMs { get; private set; }
        public float LastFrameMs { get; private set; }

        /// <summary>Time spent in the current frame so far, the part being timed right now included.</summary>
        public double CurrentMs => frameMs + (timing ? (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency : 0);

        public void Begin()
        {
            if (Time.frameCount != frame)
                NextFrame();
            started = Stopwatch.GetTimestamp();
            timing = true;
        }

        public void End()
        {
            frameMs += (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
            timing = false;
        }

        void NextFrame()
        {
            if (frame >= 0)
            {
                LastFrameMs = (float)frameMs;
                windowSum += frameMs;
                windowPeak = System.Math.Max(windowPeak, frameMs);
                windowFrames++;
            }
            frame = Time.frameCount;
            frameMs = 0;
            if (Time.unscaledTime - windowStart < 1)
                return;
            AverageMs = windowFrames > 0 ? (float)(windowSum / windowFrames) : 0;
            PeakMs = (float)windowPeak;
            windowSum = windowPeak = 0;
            windowFrames = 0;
            windowStart = Time.unscaledTime;
        }
    }

    /// <summary>How fast a running total grows, per second, updated about once a second.</summary>
    internal sealed class RateMeter
    {
        long lastTotal;
        float windowStart;

        public float PerSecond { get; private set; }

        public void Sample(long total)
        {
            float elapsed = Time.unscaledTime - windowStart;
            if (elapsed < 1)
                return;
            PerSecond = (total - lastTotal) / elapsed;
            lastTotal = total;
            windowStart = Time.unscaledTime;
        }
    }
}
