#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace Triband.Storey
{
    /// <summary>
    /// A layout baked at import (docs/PERFORMANCE.md §6.3, §6.4): the parsed model in a compact binary, read back in a
    /// fraction of the JSON's time with no JSON tree, each distinct string made once (the colours, the kinds), and a
    /// directory of the buildings, their bounds and where each is, so a site reads only the neighbouring districts'
    /// buildings along its edge. The JSON stays the source and the interchange format; this is what the runtime reads.
    /// Reading is safe from several threads at once. Engine-free.
    /// </summary>
    /// <remarks>
    /// The format follows the model field for field (<see cref="Bake"/>, <see cref="Document"/>). A change to the model
    /// changes it: bump <see cref="Version"/>, which the importer's version follows, so every layout is baked again.
    /// <c>BakedLayoutTests</c> fails until then.
    /// </remarks>
    public sealed class BakedLayout
    {
        /// <summary>The format's version: bytes of another are not read (<see cref="Of"/> gives null).</summary>
        public const int Version = 4;

        static readonly byte[] Magic = { (byte)'S', (byte)'T', (byte)'B', (byte)'L' };
        const int Header = 4 + 1 + 5 * 4;   // magic, version, the sections' offsets
        const int DirEntry = 4 * 8 + 4;     // bounds, the building's offset

        readonly byte[] data;
        readonly int[] stringAt, styleAt;
        readonly string?[] strings;          // made when first read, shared by every building and every read
        readonly int palette, directory, buildings;

        BakedLayout(byte[] data, int[] stringAt, int[] styleAt, int palette, int directory, int buildings, int count)
        {
            this.data = data; this.stringAt = stringAt; this.styleAt = styleAt; strings = new string?[stringAt.Length];
            this.palette = palette; this.directory = directory; this.buildings = buildings; Count = count;
        }

        /// <summary>The buildings.</summary>
        public int Count { get; }

        /// <summary>The baked bytes' size.</summary>
        public int Bytes => data.Length;

        /// <summary>The layout in <paramref name="bytes"/>, or null when they hold none of this <see cref="Version"/> (none baked, an older import).</summary>
        public static BakedLayout? Of(byte[]? bytes)
        {
            if (bytes == null || bytes.Length < Header) return null;
            for (int i = 0; i < Magic.Length; i++) if (bytes[i] != Magic[i]) return null;
            if (bytes[4] != Version) return null;
            var r = new Reader(bytes, 5);
            int strings = r.U32(), styles = r.U32(), palette = r.U32(), directory = r.U32(), buildings = r.U32();
            if (strings > bytes.Length || styles > bytes.Length || palette > bytes.Length || directory > bytes.Length || buildings > bytes.Length) return null;
            r.p = strings;
            var stringAt = new int[r.Count()];
            for (int i = 0; i < stringAt.Length; i++) { stringAt[i] = r.p; int n = r.Count(); r.p += n; }
            r.p = styles;
            var styleAt = new int[r.Count()];
            for (int i = 0; i < styleAt.Length; i++) { int n = r.Count(); styleAt[i] = r.p; r.p += n; }
            r.p = directory;
            int count = r.U32();
            if (count < 0 || directory + 4 + (long)count * DirEntry > buildings) return null;
            return new BakedLayout(bytes, stringAt, styleAt, palette, directory, buildings, count);
        }

        /// <summary>Building <paramref name="i"/>'s bounds in its layout, as <see cref="Generate.Site.BoundsOf"/> gives them.</summary>
        public (double x0, double z0, double x1, double z1) BoundsOf(int i)
        {
            var r = new Reader(data, Entry(i));
            return (r.F64(), r.F64(), r.F64(), r.F64());
        }

        /// <summary>Building <paramref name="i"/>, a new one read from the bytes.</summary>
        public BuildingData Building(int i)
        {
            var r = new Reader(data, Entry(i) + 4 * 8);
            r.p = buildings + r.U32();
            return ReadBuilding(ref r);
        }

        /// <summary>The whole layout, a new one read from the bytes: what <see cref="PrototypeJson.Read"/> gave when it was baked.</summary>
        public StoreyDocument Document()
        {
            var r = new Reader(data, Header);
            var d = new StoreyDocument { v = r.Int(), seq = r.Int(), spawn = r.Vec() };
            r.p = palette;
            for (int i = r.Count(); i > 0; i--) { string key = Str(ref r)!; d.palette[key] = new PaletteEntry(Str(ref r)!, Str(ref r)!); }
            r.p = directory + 4;   // past the count
            d.buildings.Capacity = Count;
            var at = new Reader(data, 0);
            for (int i = 0; i < Count; i++)
            {
                r.p = directory + 4 + i * DirEntry + 4 * 8;
                at.p = buildings + r.U32();
                d.buildings.Add(ReadBuilding(ref at));
            }
            return d;
        }

        int Entry(int i)
        {
            if ((uint)i >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(i));
            return directory + 4 + i * DirEntry;
        }

        // ---- reading -----------------------------------------------------------------------

        struct Reader
        {
            readonly byte[] d; public int p;
            public Reader(byte[] d, int p) { this.d = d; this.p = p; }

            public int Count()
            {
                uint v = 0; int s = 0; byte b;
                do { b = d[p++]; v |= (uint)(b & 0x7F) << s; s += 7; } while ((b & 0x80) != 0);
                return (int)v;
            }
            public int Int() { uint v = (uint)Count(); return (int)(v >> 1) ^ -(int)(v & 1); }   // zigzag
            public bool Bool() => d[p++] != 0;
            public int U32() { int v = d[p] | d[p + 1] << 8 | d[p + 2] << 16 | d[p + 3] << 24; p += 4; return v; }
            public double F64()
            {
                long v = 0;
                for (int i = 7; i >= 0; i--) v = v << 8 | d[p + i];
                p += 8;
                return BitConverter.Int64BitsToDouble(v);
            }
            public double? Opt() => Bool() ? F64() : (double?)null;
            public int? OptInt() => Bool() ? Int() : (int?)null;
            public Vec2 Vec() => new Vec2(F64(), F64());
        }

        string? Str(ref Reader r)
        {
            int i = r.Count() - 1;
            if (i < 0) return null;
            var s = strings[i];
            if (s != null) return s;
            var at = new Reader(data, stringAt[i]);
            int n = at.Count();
            return strings[i] = Encoding.UTF8.GetString(data, at.p, n);   // two threads may both make it: either is kept
        }

        static T E<T>(ref Reader r) where T : struct, Enum
        {
            int i = r.Int(); var by = EnumOf<T>.ByNumber;
            if ((uint)i < (uint)by.Length && EnumOf<T>.Defined[i]) return by[i];
            return (T)Enum.ToObject(typeof(T), i);   // a number the enum doesn't name, as the JSON reader takes one
        }

        static class EnumOf<T> where T : struct, Enum
        {
            // by number: the writer writes each value's number, so a value added later leaves the others as they read
            public static readonly T[] ByNumber;
            public static readonly bool[] Defined;

            static EnumOf()
            {
                var values = (T[])Enum.GetValues(typeof(T)); int max = -1;
                foreach (var v in values) max = Math.Max(max, Convert.ToInt32(v));
                ByNumber = new T[max + 1]; Defined = new bool[max + 1];
                foreach (var v in values) { int i = Convert.ToInt32(v); if (i >= 0) { ByNumber[i] = v; Defined[i] = true; } }
            }
        }

        void Points(ref Reader r, List<Vec2> into)
        {
            int n = r.Count();
            if (n == 0) return;
            if (into.Capacity < into.Count + n) into.Capacity = into.Count + n;
            for (int i = 0; i < n; i++) into.Add(r.Vec());
        }

        static void Ints(ref Reader r, List<int> into)
        {
            for (int n = r.Count(); n > 0; n--) into.Add(r.Int());
        }

        void Corners(ref Reader r, List<CornerData> into)
        {
            for (int n = r.Count(); n > 0; n--)
            {
                var c = new CornerData { at = r.Vec(), shape = E<CornerShape>(ref r), size = r.F64(), door = r.Bool() };
                Points(ref r, c.pts);
                into.Add(c);
            }
        }

        BuildingData ReadBuilding(ref Reader r)
        {
            var b = new BuildingData
            {
                id = Str(ref r)!, name = Str(ref r)!, pos = r.Vec(), elevation = r.F64(), groundHeight = r.F64(), floorHeight = r.F64(),
                interior = r.Bool(), gen = r.Bool(),
            };
            Style(r.Count(), b.style);
            Points(ref r, b.footprint);
            Corners(ref r, b.corners);
            Ints(ref r, b.blank);
            int n = r.Count();
            if (n > 0) b.floors.Capacity = n;
            for (; n > 0; n--)
            {
                var f = new FloorData();
                for (int w = r.Count(); w > 0; w--)
                {
                    var wall = new WallData { a = r.Vec(), b = r.Vec() };
                    for (int k = r.Count(); k > 0; k--) wall.doors.Add(new DoorData { t = r.F64() });
                    f.walls.Add(wall);
                }
                f.h = r.Opt();
                Points(ref r, f.shape);
                Ints(ref r, f.blank);
                Corners(ref r, f.corners);
                int st = r.Count();
                if (st > 0) Style(st - 1, f.style = new FacadeStyle());
                if (r.Bool()) f.terraceRoof = new TerraceRoofData { pitch = r.F64() };
                f.filled = r.Bool();
                b.floors.Add(f);
            }
            for (n = r.Count(); n > 0; n--)
                b.shafts.Add(new CoreData { id = Str(ref r)!, type = E<CoreType>(ref r), x = r.F64(), z = r.F64(), rot = r.F64(), bottom = r.Int(), top = r.Int(), roof = r.Bool() });
            for (n = r.Count(); n > 0; n--)
                b.entrances.Add(new EntranceData { edge = r.Int(), t = r.F64(), k = r.Int() });
            for (n = r.Count(); n > 0; n--)
                b.details.Add(new DetailData { kind = Str(ref r)!, k = r.Int(), edge = r.Int(), t = r.F64(), y = r.Opt() });
            for (n = r.Count(); n > 0; n--)
            {
                var v = new VoidData { id = Str(ref r)!, kind = E<VoidKind>(ref r), bottom = r.Int() };
                Points(ref r, v.shape);
                b.voids.Add(v);
            }
            for (n = r.Count(); n > 0; n--)
                b.bridges.Add(new BridgeData { id = Str(ref r)!, to = Str(ref r)!, k = r.Int(), at = r.Vec(), toK = r.Int(), width = r.F64(), open = r.Bool() });
            return b;
        }

        // style i of the table, into s (a building's own, which it has from the start, or a setback's new one)
        void Style(int i, FacadeStyle s)
        {
            var r = new Reader(data, styleAt[i]);
            s.preset = Str(ref r); s.label = Str(ref r)!;
            s.wall = Str(ref r)!; s.trim = Str(ref r)!; s.interior = Str(ref r)!; s.floor = Str(ref r)!; s.roof = Str(ref r)!; s.core = Str(ref r)!; s.glass = Str(ref r)!;
            s.door = Str(ref r); s.rail = Str(ref r); s.metal = Str(ref r); s.ceiling = Str(ref r); s.liftInterior = Str(ref r); s.liftButton = Str(ref r);
            s.detailMetal = Str(ref r); s.grille = Str(ref r); s.detailDark = Str(ref r); s.dish = Str(ref r);
            s.frame = Str(ref r); s.foundationColor = Str(ref r); s.plinth = Str(ref r);
            s.windows = E<WindowType>(ref r); s.winW = r.F64(); s.bay = r.F64(); s.ground = E<GroundType>(ref r); s.bands = r.Bool(); s.parapet = r.Bool();
            s.roofType = E<RoofType>(ref r); s.pitch = r.Opt(); s.eave = r.Opt();
            s.head = E<HeadType>(ref r); s.sills = r.Bool(); s.heads = r.Bool(); s.paneCols = r.OptInt(); s.paneRows = r.OptInt(); s.frames = r.Bool();
            s.bandH = r.Opt(); s.bandDepth = r.Opt(); s.bandWall = r.Bool();
            s.windowKind = Str(ref r); s.doorKind = Str(ref r); s.sillH = r.Opt();
            s.plinthH = r.Opt(); s.foundation = r.Opt(); s.foundationH = r.Opt(); s.foundationOut = r.Opt(); s.bricks = r.Opt(); s.mansard = r.Opt(); s.dormers = r.Opt();
            s.groundWall = Str(ref r); s.band = Str(ref r); s.cornice = Str(ref r); s.bandGround = r.Bool(); s.corniceH = r.Opt(); s.corniceDepth = r.Opt(); s.plinthDepth = r.Opt();
            s.cap = Str(ref r); s.capRim = Str(ref r); s.capH = r.Opt(); s.capDepth = r.Opt(); s.capStrip = r.Opt();
            if (r.Bool())
            {
                var dr = s.details = new DetailRules { ac = r.Opt(), vents = r.Opt() };
                if (r.Bool())
                {
                    dr.kinds = new SortedDictionary<string, double>(StringComparer.Ordinal);
                    for (int n = r.Count(); n > 0; n--) dr.kinds[Str(ref r)!] = r.F64();
                }
            }
            else s.details = null;
        }

        // ---- baking ------------------------------------------------------------------------

        /// <summary>The layout's bytes: every field of the model, the strings and the styles once each, the buildings' directory.</summary>
        public static byte[] Bake(StoreyDocument d)
        {
            var strings = new Dictionary<string, int>(StringComparer.Ordinal); var stringList = new List<string>();
            var styles = new Dictionary<string, int>(StringComparer.Ordinal); var styleList = new List<byte[]>();
            int S(string? s)
            {
                if (s == null) return 0;
                if (!strings.TryGetValue(s, out int i)) { strings[s] = i = stringList.Count; stringList.Add(s); }
                return i + 1;
            }
            int St(FacadeStyle s)
            {
                var w = new Writer(); WriteStyle(w, s, S);
                var bytes = w.ToArray(); string key = Convert.ToBase64String(bytes);
                if (!styles.TryGetValue(key, out int i)) { styles[key] = i = styleList.Count; styleList.Add(bytes); }
                return i;
            }

            var body = new Writer(); var at = new List<int>(d.buildings.Count);
            foreach (var b in d.buildings) { at.Add(body.Length); WriteBuilding(body, b, S, St); }

            var o = new Writer();
            o.Raw(Magic); o.Byte(Version);
            int offsets = o.Length; for (int i = 0; i < 5; i++) o.U32(0);
            o.Int(d.v); o.Int(d.seq); o.Vec(d.spawn);
            var pal = new Writer();
            pal.Count(d.palette.Count);
            foreach (var kv in d.palette) { pal.Count(S(kv.Key)); pal.Count(S(kv.Value.name)); pal.Count(S(kv.Value.hex)); }

            int stringsAt = o.Length;
            o.Count(stringList.Count);
            foreach (var s in stringList) { var u = Encoding.UTF8.GetBytes(s); o.Count(u.Length); o.Raw(u); }
            int stylesAt = o.Length;
            o.Count(styleList.Count);
            foreach (var s in styleList) { o.Count(s.Length); o.Raw(s); }
            int paletteAt = o.Length;
            o.Raw(pal.ToArray());
            int directoryAt = o.Length;
            o.U32(d.buildings.Count);
            for (int i = 0; i < d.buildings.Count; i++)
            {
                var bb = Generate.Site.BoundsOf(d.buildings[i]);
                o.F64(bb.x0); o.F64(bb.z0); o.F64(bb.x1); o.F64(bb.z1); o.U32(at[i]);
            }
            int buildingsAt = o.Length;
            o.Raw(body.ToArray());
            var bytes = o.ToArray();
            int p = offsets;
            foreach (int v in new[] { stringsAt, stylesAt, paletteAt, directoryAt, buildingsAt })
            {
                bytes[p++] = (byte)v; bytes[p++] = (byte)(v >> 8); bytes[p++] = (byte)(v >> 16); bytes[p++] = (byte)(v >> 24);
            }
            return bytes;
        }

        sealed class Writer
        {
            byte[] b = new byte[256]; int n;
            public int Length => n;
            public byte[] ToArray() { var r = new byte[n]; Array.Copy(b, r, n); return r; }
            void Room(int k) { if (n + k > b.Length) Array.Resize(ref b, Math.Max(b.Length * 2, n + k)); }
            public void Byte(int v) { Room(1); b[n++] = (byte)v; }
            public void Raw(byte[] v) { Room(v.Length); Array.Copy(v, 0, b, n, v.Length); n += v.Length; }
            public void Count(int v)
            {
                if (v < 0) throw new ArgumentOutOfRangeException(nameof(v), v, "a count is never negative");
                uint u = (uint)v;
                while (u >= 0x80) { Byte((int)(u & 0x7F | 0x80)); u >>= 7; }
                Byte((int)u);
            }
            public void Int(int v) => Count((int)((uint)(v << 1) ^ (uint)(v >> 31)));
            public void Bool(bool v) => Byte(v ? 1 : 0);
            public void U32(int v) { Byte(v); Byte(v >> 8); Byte(v >> 16); Byte(v >> 24); }
            public void F64(double v) { long x = BitConverter.DoubleToInt64Bits(v); for (int i = 0; i < 8; i++) Byte((int)(x >> (8 * i))); }
            public void Opt(double? v) { Bool(v.HasValue); if (v.HasValue) F64(v.Value); }
            public void OptInt(int? v) { Bool(v.HasValue); if (v.HasValue) Int(v.Value); }
            public void Vec(Vec2 v) { F64(v.x); F64(v.z); }
            public void E<T>(T v) where T : struct, Enum => Int(Convert.ToInt32(v));
            public void Points(List<Vec2> ps) { Count(ps.Count); foreach (var p in ps) Vec(p); }
            public void Ints(List<int> xs) { Count(xs.Count); foreach (var x in xs) Int(x); }
        }

        static void WriteCorners(Writer w, List<CornerData> cs)
        {
            w.Count(cs.Count);
            foreach (var c in cs) { w.Vec(c.at); w.E(c.shape); w.F64(c.size); w.Bool(c.door); w.Points(c.pts); }
        }

        static void WriteBuilding(Writer w, BuildingData b, Func<string?, int> S, Func<FacadeStyle, int> St)
        {
            w.Count(S(b.id)); w.Count(S(b.name)); w.Vec(b.pos); w.F64(b.elevation); w.F64(b.groundHeight); w.F64(b.floorHeight);
            w.Bool(b.interior); w.Bool(b.gen);
            w.Count(St(b.style));
            w.Points(b.footprint);
            WriteCorners(w, b.corners);
            w.Ints(b.blank);
            w.Count(b.floors.Count);
            foreach (var f in b.floors)
            {
                w.Count(f.walls.Count);
                foreach (var wall in f.walls)
                {
                    w.Vec(wall.a); w.Vec(wall.b);
                    w.Count(wall.doors.Count); foreach (var dr in wall.doors) w.F64(dr.t);
                }
                w.Opt(f.h);
                w.Points(f.shape);
                w.Ints(f.blank);
                WriteCorners(w, f.corners);
                w.Count(f.style == null ? 0 : St(f.style) + 1);
                w.Bool(f.terraceRoof != null); if (f.terraceRoof != null) w.F64(f.terraceRoof.pitch);
                w.Bool(f.filled);
            }
            w.Count(b.shafts.Count);
            foreach (var s in b.shafts) { w.Count(S(s.id)); w.E(s.type); w.F64(s.x); w.F64(s.z); w.F64(s.rot); w.Int(s.bottom); w.Int(s.top); w.Bool(s.roof); }
            w.Count(b.entrances.Count);
            foreach (var e in b.entrances) { w.Int(e.edge); w.F64(e.t); w.Int(e.k); }
            w.Count(b.details.Count);
            foreach (var dd in b.details) { w.Count(S(dd.kind)); w.Int(dd.k); w.Int(dd.edge); w.F64(dd.t); w.Opt(dd.y); }
            w.Count(b.voids.Count);
            foreach (var v in b.voids) { w.Count(S(v.id)); w.E(v.kind); w.Int(v.bottom); w.Points(v.shape); }
            w.Count(b.bridges.Count);
            foreach (var br in b.bridges) { w.Count(S(br.id)); w.Count(S(br.to)); w.Int(br.k); w.Vec(br.at); w.Int(br.toK); w.F64(br.width); w.Bool(br.open); }
        }

        static void WriteStyle(Writer w, FacadeStyle s, Func<string?, int> S)
        {
            foreach (var c in new[] { s.preset, s.label, s.wall, s.trim, s.interior, s.floor, s.roof, s.core, s.glass,
                s.door, s.rail, s.metal, s.ceiling, s.liftInterior, s.liftButton, s.detailMetal, s.grille, s.detailDark, s.dish,
                s.frame, s.foundationColor, s.plinth }) w.Count(S(c));
            w.E(s.windows); w.F64(s.winW); w.F64(s.bay); w.E(s.ground); w.Bool(s.bands); w.Bool(s.parapet);
            w.E(s.roofType); w.Opt(s.pitch); w.Opt(s.eave);
            w.E(s.head); w.Bool(s.sills); w.Bool(s.heads); w.OptInt(s.paneCols); w.OptInt(s.paneRows); w.Bool(s.frames);
            w.Opt(s.bandH); w.Opt(s.bandDepth); w.Bool(s.bandWall);
            w.Count(S(s.windowKind)); w.Count(S(s.doorKind)); w.Opt(s.sillH);
            w.Opt(s.plinthH); w.Opt(s.foundation); w.Opt(s.foundationH); w.Opt(s.foundationOut); w.Opt(s.bricks); w.Opt(s.mansard); w.Opt(s.dormers);
            w.Count(S(s.groundWall)); w.Count(S(s.band)); w.Count(S(s.cornice)); w.Bool(s.bandGround); w.Opt(s.corniceH); w.Opt(s.corniceDepth); w.Opt(s.plinthDepth);
            w.Count(S(s.cap)); w.Count(S(s.capRim)); w.Opt(s.capH); w.Opt(s.capDepth); w.Opt(s.capStrip);
            w.Bool(s.details != null);
            if (s.details != null)
            {
                w.Opt(s.details.ac); w.Opt(s.details.vents);
                var kinds = s.details.kinds;
                w.Bool(kinds != null);
                if (kinds != null) { w.Count(kinds.Count); foreach (var kv in kinds) { w.Count(S(kv.Key)); w.F64(kv.Value); } }
            }
        }
    }
}
