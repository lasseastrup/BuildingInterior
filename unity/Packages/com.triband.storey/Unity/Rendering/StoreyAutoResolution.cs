#nullable enable
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// Auto resolution (<see cref="Lod.AutoResolution"/>, the prototype's): the camera draws fewer pixels while the GPU
    /// can't draw a frame in time, and more again when it can, through Unity's dynamic resolution
    /// (<see cref="ScalableBufferManager"/>; URP then draws into an intermediate texture and scales it up). On a phone
    /// whose GPU is the limit, that is what holds the frame rate: on an iPhone it held the prototype's city views at 60 fps
    /// where full resolution drew 53 (docs/PERFORMANCE.md §7.10). Add it to any object; it turns the camera's Allow
    /// Dynamic Resolution on. The platform must support dynamic resolution (Metal, Vulkan, DirectX 12 and consoles);
    /// elsewhere nothing changes. With Frame Timing Stats on (Player Settings) a frame is late when the GPU took longer
    /// than the frame rate's interval, and a step up waits until the GPU's time says it fits. Without, a frame is late
    /// when it came over a fifth of an interval late (20 ms at 60 fps), whatever made it late.
    /// </summary>
    [AddComponentMenu("Storey/Storey Auto Resolution")]
    [DisallowMultipleComponent]
    public sealed class StoreyAutoResolution : MonoBehaviour
    {
        [Tooltip("The camera to scale: the main camera when empty. Its Allow Dynamic Resolution is turned on.")]
        public Camera? targetCamera;
        [Tooltip("The lowest share of the resolution, of each axis, it goes down to.")]
        [Range(0.5f, 1f)] public float lowest = 0.5f;
        [Tooltip("The frame rate to hold: Application.targetFrameRate when 0, or 60 if that isn't set.")]
        [Min(0)] public int frameRate;

        readonly Lod.AutoResolution auto = new Lod.AutoResolution();
        readonly FrameTiming[] timing = new FrameTiming[1];
        double gpuAverage;   // the GPU's milliseconds a frame at the share drawn, smoothed; 0 until measured

        /// <summary>The share of each axis drawn now.</summary>
        public float Scale => auto.Scale;

        void OnDisable() { auto.Reset(); gpuAverage = 0; ScalableBufferManager.ResizeBuffers(1f, 1f); }

        void Update()
        {
            var cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) return;
            if (!cam.allowDynamicResolution) cam.allowDynamicResolution = true;
            double interval = 1000.0 / (frameRate > 0 ? frameRate : Application.targetFrameRate > 0 ? Application.targetFrameRate : 60);
            double gpu = 0;
            if (FrameTimingManager.IsFeatureEnabled())
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timing) > 0) gpu = timing[0].gpuFrameTime;
            }
            bool late; bool? fits = null;
            if (gpu > 0)
            {
                late = gpu > interval;
                gpuAverage = gpuAverage > 0 ? gpuAverage + (gpu - gpuAverage) * 0.1 : gpu;
                if (auto.Level > 0)
                {
                    // the pixels a level up draws, against these: the GPU's time grows with them
                    float up = Lod.AutoResolution.Levels[auto.Level - 1] / auto.Scale;
                    fits = gpuAverage * up * up < interval * 0.9;
                }
            }
            else late = Time.unscaledDeltaTime * 1000.0 > interval * 1.2;
            auto.Lowest = lowest;
            if (auto.Frame(Time.realtimeSinceStartupAsDouble * 1000.0, late, fits))
            {
                ScalableBufferManager.ResizeBuffers(auto.Scale, auto.Scale);
                gpuAverage = 0;   // measured again at the new share
            }
        }
    }
}
