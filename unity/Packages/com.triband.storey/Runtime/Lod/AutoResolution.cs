#nullable enable
using System;

namespace Triband.Storey.Lod
{
    /// <summary>
    /// Auto resolution: the share of the screen's pixels to draw, held where the GPU draws every frame in time. The
    /// prototype's (prototype/index.html, drsTick), which holds 60 fps on a phone whose GPU is the limit. The caller says
    /// each frame whether it was late for a reason fewer pixels help (the GPU's: later than the frame rate's interval
    /// while the main thread had time to spare), and draws at <see cref="Scale"/>. Three of the last 20 frames late: a
    /// level down (two if most were). Two seconds without a late one: a level up, unless the caller knows the level up
    /// won't fit. A step up that brings late frames back within 3 s makes the level it fell back to the ceiling for 20 s,
    /// so a scene at the edge isn't probed every few seconds. At most a step every half second. Engine-free.
    /// </summary>
    public sealed class AutoResolution
    {
        /// <summary>The shares of the full resolution (of each axis) it steps between, full first.</summary>
        public static readonly float[] Levels = { 1f, 0.9f, 0.8f, 0.72f, 0.65f, 0.58f, 0.5f };

        /// <summary>The lowest share it goes down to: no level below it is used.</summary>
        public float Lowest = 0.5f;

        const int Window = 120, Recent = 20;   // frames: two seconds at 60 fps, and the last third of one
        const double StepGap = 500, UpAfter = 2000, Probe = 3000, Hold = 20000;   // milliseconds
        readonly bool[] late = new bool[Window];
        int frames, level, ceiling;
        double at = double.NegativeInfinity, upAt = double.NegativeInfinity, ceilingUntil = double.NegativeInfinity;

        /// <summary>The level drawn: 0 is full resolution.</summary>
        public int Level => level;
        /// <summary>The share of each axis drawn.</summary>
        public float Scale => Levels[level];

        int Bottom { get { int i = 0; while (i + 1 < Levels.Length && Levels[i + 1] >= Lowest - 1e-4f) i++; return i; } }

        /// <summary>
        /// One frame, at <paramref name="nowMs"/>: whether it was late for the GPU, and whether the level up would fit
        /// (null when not known: it is tried). True when the level changed.
        /// </summary>
        public bool Frame(double nowMs, bool wasLate, bool? fitsAbove = null)
        {
            late[frames % Window] = wasLate; frames++;
            int bottom = Bottom;
            if (level > bottom) { level = bottom; at = nowMs; return true; }   // the lowest share was raised
            if (frames < 30 || nowMs - at < StepGap) return false;
            int n = Math.Min(frames, Window), any = 0, recent = 0;
            for (int j = 1; j <= n; j++) if (late[(frames - j) % Window]) { any++; if (j <= Recent) recent++; }
            int to = level;
            if (recent >= 3)
            {
                to = Math.Min(bottom, level + (recent >= 10 ? 2 : 1));
                if (nowMs - upAt < Probe) { ceiling = to; ceilingUntil = nowMs + Hold; }   // the last step up didn't hold
            }
            else if (any == 0 && n >= Window && nowMs - at > UpAfter && level > 0 && fitsAbove != false
                     && (nowMs > ceilingUntil || level - 1 >= ceiling))
            {
                to = level - 1; upAt = nowMs;
            }
            if (to == level) return false;
            level = to; at = nowMs;
            return true;
        }

        /// <summary>Back to full resolution, the frames so far forgotten.</summary>
        public void Reset()
        {
            Array.Clear(late, 0, Window); frames = 0; level = 0; ceiling = 0;
            at = upAt = ceilingUntil = double.NegativeInfinity;
        }
    }
}
