#nullable enable
using System;

namespace Triband.Storey.Generate
{
    /// <summary>A point in world or building space (metres, y up).</summary>
    public struct P3
    {
        public double x, y, z;
        public P3(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
        public static P3 operator -(P3 a, P3 b) => new P3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static P3 operator +(P3 a, P3 b) => new P3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static P3 operator *(P3 a, double s) => new P3(a.x * s, a.y * s, a.z * s);
        public static P3 Cross(P3 a, P3 b) => new P3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static double Dot(P3 a, P3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public double Length => Math.Sqrt(x * x + y * y + z * z);
        public P3 Normalized { get { double l = Length; return l > 0 ? this * (1 / l) : this; } }
        public override string ToString() => $"({x:0.###}, {y:0.###}, {z:0.###})";
    }

    /// <summary>A linear-space colour, as the prototype's vertex colours (THREE.Color from sRGB hex).</summary>
    public struct Rgb
    {
        public double r, g, b;
        public Rgb(double r, double g, double b) { this.r = r; this.g = g; this.b = b; }
        public Rgb Shade(double f) => new Rgb(r * f, g * f, b * f);
        public override string ToString() => $"({r:0.###}, {g:0.###}, {b:0.###})";
    }

    /// <summary>A 2D frame in the XZ plane: origin O, along-axis u, across-axis w (both unit).</summary>
    public struct Frame
    {
        public Vec2 O, u, w;
        public Frame(Vec2 o, Vec2 u, Vec2 w) { O = o; this.u = u; this.w = w; }
        /// <summary>World point at (a along u, b along w, height y).</summary>
        public P3 At(double a, double y, double b) => new P3(O.x + u.x * a + w.x * b, y, O.z + u.z * a + w.z * b);
        public Vec2 At2(double a, double b) => new Vec2(O.x + u.x * a + w.x * b, O.z + u.z * a + w.z * b);
    }

    /// <summary>One end of a mitred strip: u = s + k·w (w measured from the footprint line).</summary>
    public struct Cut
    {
        public double s, k;
        public Cut(double s, double k) { this.s = s; this.k = k; }
        public double At(double w) => s + k * w;
    }

    /// <summary>Both ends of a strip along an edge.</summary>
    public struct Miter
    {
        public Cut S, E;
        public Miter(Cut s, Cut e) { S = s; E = e; }
        /// <summary>The u range the strip covers between across-offsets wa and wb.</summary>
        public (double s, double e) Span(double wa, double wb) =>
            (Math.Max(S.At(wa), S.At(wb)), Math.Min(E.At(wa), E.At(wb)));
    }

    /// <summary>An opening in a wall: u range, height range above the storey floor.</summary>
    public sealed class Opening
    {
        public double u0, u1, y0, y1;
        public bool door, full, thr;
        /// <summary>A door with no canopy over it (a bridge's: the bridge is its cover).</summary>
        public bool bare;
        public Opening Clone() => (Opening)MemberwiseClone();
    }

    /// <summary>A collision segment (world XZ) with a radius.</summary>
    public struct Seg
    {
        public double ax, az, bx, bz, r;
        public Seg(double ax, double az, double bx, double bz, double r) { this.ax = ax; this.az = az; this.bx = bx; this.bz = bz; this.r = r; }
    }
}
