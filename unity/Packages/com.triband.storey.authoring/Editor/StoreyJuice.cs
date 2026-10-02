#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Triband.Storey.Editor
{
    /// <summary>
    /// Small Scene view effects for the Storey tools (docs/EDITOR.md §5): a ring that pulses where something snapped or was
    /// placed, a flash along an edge that was just shared, a glow round what was just selected. Each fades out in a few
    /// tenths of a second. Positions are the site's (drawn inside the tools' handle scope).
    /// </summary>
    internal static class StoreyJuice
    {
        enum Kind { Ring, Line, Glow }
        sealed class Fx { public Kind kind; public Vector3 a, b; public Vector3[][] lines = Array.Empty<Vector3[]>(); public float r; public Color c; public double t0, dur; }
        static readonly List<Fx> fx = new List<Fx>();

        static double Now => EditorApplication.timeSinceStartup;

        /// <summary>Something is still fading: keep the Scene view redrawing.</summary>
        public static bool Active => fx.Count > 0;

        /// <summary>A ring that grows and fades on the ground plane at <paramref name="at"/>.</summary>
        public static void Ring(Vector3 at, float radius, Color c, double dur = 0.35) =>
            fx.Add(new Fx { kind = Kind.Ring, a = at, r = radius, c = c, t0 = Now, dur = dur });

        /// <summary>A thick line along a–b that thins and fades.</summary>
        public static void Flash(Vector3 a, Vector3 b, Color c, double dur = 0.5) =>
            fx.Add(new Fx { kind = Kind.Line, a = a, b = b, c = c, t0 = Now, dur = dur });

        /// <summary>Polylines (closed or not) that glow and fade.</summary>
        public static void Glow(Vector3[][] lines, Color c, double dur = 0.5) =>
            fx.Add(new Fx { kind = Kind.Glow, lines = lines, c = c, t0 = Now, dur = dur });

        /// <summary>Draw what is still fading (repaint only), and forget what has gone.</summary>
        public static void Draw()
        {
            double now = Now;
            fx.RemoveAll(f => now - f.t0 >= f.dur);
            if (Event.current.type != EventType.Repaint) return;
            var keep = Handles.color;
            foreach (var f in fx)
            {
                float t = (float)((now - f.t0) / f.dur), fade = (1 - t) * (1 - t);
                var c = f.c; c.a *= fade; Handles.color = c;
                switch (f.kind)
                {
                    case Kind.Ring:
                        float r = f.r * (0.5f + 1.5f * (1 - (1 - t) * (1 - t)));   // eases out as it grows
                        Handles.DrawWireDisc(f.a, Vector3.up, r, 3f);
                        break;
                    case Kind.Line:
                        Handles.DrawAAPolyLine(2f + 8f * (1 - t), f.a, f.b);
                        break;
                    default:
                        foreach (var l in f.lines) Handles.DrawAAPolyLine(2f + 6f * (1 - t), l);
                        break;
                }
            }
            Handles.color = keep;
        }
    }
}
