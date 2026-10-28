#nullable enable
using System;
using System.Collections.Generic;

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
        /// <summary>A bridge's door: always the generator's plain door, never the style's artist-made street door.</summary>
        public bool bare;
        /// <summary>
        /// How far an artist's window or door in it reaches past it (<see cref="OpeningKind.reachL"/>), as placed: left, right,
        /// below and above, metres. 0 for the generator's own.
        /// </summary>
        public double rl, rr, rb, rt;
        /// <summary>
        /// The artist's window or door in it (picked from the style's set where openings are laid out, docs/OPENINGS.md §7.1); null
        /// for the generator's own, and always for a bridge's door.
        /// </summary>
        public OpeningKind? kind;
        /// <summary>
        /// Its own recess, metres from the wall's face, over the style's (<see cref="Facade.RecessOf"/>): a street door's from the
        /// entry its style's set picked (<see cref="OpeningEntry.recess"/>); null = the style's.
        /// </summary>
        public double? recess;
        /// <summary>
        /// How far the hole reaches past the artist's door in it, left, right and over it (below 0, short of it: the door's frame
        /// behind the wall), where the style makes the hole bigger or smaller than the door (<see cref="FacadeStyle.doorRecessOffset"/>);
        /// 0 = the door's own hole. The door is placed in the hole less these. Storey's own.
        /// </summary>
        public double hl, hr, ht;
        /// <summary>The opening with its window's dressing: where what keeps clear of windows keeps clear of.</summary>
        public double Left => u0 - rl;
        public double Right => u1 + rr;
        public double Bottom => y0 - rb;
        public double Top => y1 + rt;
        public Opening Clone() => (Opening)MemberwiseClone();
        /// <summary>This opening made a copy of <paramref name="o"/>, every field (as <see cref="Clone"/>, into one already made).</summary>
        public Opening Set(Opening o) { u0 = o.u0; u1 = o.u1; y0 = o.y0; y1 = o.y1; door = o.door; full = o.full; thr = o.thr; bare = o.bare; rl = o.rl; rr = o.rr; rb = o.rb; rt = o.rt; kind = o.kind; recess = o.recess; hl = o.hl; hr = o.hr; ht = o.ht; return this; }
        /// <summary>Every field set, as a new opening with these would be (one taken from an <see cref="OpeningPool"/>).</summary>
        public Opening Set(double u0, double u1, double y0, double y1, bool door = false, bool full = false, bool thr = false, bool bare = false,
            double rl = 0, double rr = 0, double rb = 0, double rt = 0, OpeningKind? kind = null, double? recess = null, double hl = 0, double hr = 0, double ht = 0)
        {
            this.u0 = u0; this.u1 = u1; this.y0 = y0; this.y1 = y1; this.door = door; this.full = full; this.thr = thr; this.bare = bare;
            this.rl = rl; this.rr = rr; this.rb = rb; this.rt = rt; this.kind = kind; this.recess = recess; this.hl = hl; this.hr = hr; this.ht = ht;
            return this;
        }
    }

    /// <summary>
    /// Openings made again for one wall after another (<see cref="Facade.Ops(Site, BuildingData, int, int, double, double, double, List{Opening}, OpeningPool)"/>):
    /// <see cref="Reset"/> before each wall, and the last wall's are taken again. Only for openings let go before the next reset.
    /// </summary>
    public sealed class OpeningPool
    {
        readonly List<Opening> made = new List<Opening>();
        int used;
        public void Reset() => used = 0;
        public Opening Take()
        {
            if (used == made.Count) made.Add(new Opening());
            return made[used++];
        }
    }

    /// <summary>A collision segment (world XZ) with a radius.</summary>
    public struct Seg
    {
        public double ax, az, bx, bz, r;
        public Seg(double ax, double az, double bx, double bz, double r) { this.ax = ax; this.az = az; this.bx = bx; this.bz = bz; this.r = r; }
    }
}
