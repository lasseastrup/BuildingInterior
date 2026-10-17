#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Generate;

namespace Triband.Storey.Edit
{
    /// <summary>
    /// The prototype's stress-test city (docs/CITY.md §1): blocks of generated buildings around the hand-made ones, mostly
    /// shell-only with a few walk-in ones with a stair core, taller towards the centre. Deterministic and the same as
    /// the prototype's (its seeded generator, number for number), so a run in Unity compares with one there. Generated
    /// buildings are marked <see cref="BuildingData.gen"/>; making a city again replaces them. <see cref="Options"/> varies
    /// it: how many buildings, how many walk-in, the seed, the heights.
    /// </summary>
    public static class TestCity
    {
        /// <summary>
        /// What a test city is made with (the editor's Test City window). The defaults make the prototype's city; the
        /// walk-in share, the heights and the lifts change no other draw, so the same seed lays out the same blocks,
        /// outlines and styles whatever they are.
        /// </summary>
        public sealed class Options
        {
            /// <summary>How many buildings (fewer once the blocks round the hand-made buildings run out).</summary>
            public int Count = 300;
            /// <summary>
            /// The share of the buildings that are walk-in, 0 to 1, each with stairs from the ground to the top and a lift
            /// above <see cref="LiftAbove"/> storeys where they fit; the others are shells. Null: the prototype's own mix,
            /// about one in twenty of the buildings up to 12 storeys, with a stair core where one fits in the middle.
            /// </summary>
            public double? WalkIn;
            /// <summary>The walk-in buildings are the ones nearest the middle (the city is made from the middle out), not spread at random.</summary>
            public bool WalkInNearMiddle;
            /// <summary>The generator's seed: another lays out another city. <see cref="TestCity.Seed"/> is the prototype's.</summary>
            public int Seed = TestCity.Seed;
            /// <summary>Multiplies each building's storeys (the prototype's are 1 to 40, taller towards the middle).</summary>
            public double HeightScale = 1;
            /// <summary>No building has more storeys than this (at most <see cref="TestCity.MostStoreys"/>).</summary>
            public int MaxStoreys = 40;
            /// <summary>A walk-in building gets a lift beside its stairs above this many storeys.</summary>
            public int LiftAbove = 5;
        }

        /// <summary>The most storeys <see cref="Options.MaxStoreys"/> allows: Sink's occluder rows hold 63 storeys.</summary>
        public const int MostStoreys = 60;
        /// <summary>The prototype's mulberry32-style generator with its seed.</summary>
        sealed class Rng
        {
            int a;
            public Rng(int seed) { a = seed; }
            public double Next()
            {
                unchecked
                {
                    a = a + 0x6D2B79F5;
                    int t = (int)((uint)(a ^ (int)((uint)a >> 15)) * (uint)(1 | a));
                    t = (t + (int)((uint)(t ^ (int)((uint)t >> 7)) * (uint)(61 | t))) ^ t;
                    return (uint)(t ^ (int)((uint)t >> 14)) / 4294967296.0;
                }
            }
        }

        public const int Seed = 20260928;
        const double Block = 64, Pitch = 80, Grid = 0.25;
        static readonly string[] StyleKeys = { "brick", "glass", "stucco", "concrete", "warehouse" };

        /// <summary>JavaScript's Math.round: halves go up.</summary>
        static double JsRound(double v) => Math.Floor(v + 0.5);
        static double SnapG(double v) => JsRound(v / Grid) * Grid;

        /// <summary>
        /// Replace the layout's generated buildings with <paramref name="count"/> new ones. The hand-made buildings stay,
        /// with 12 m kept clear round each. <paramref name="walkIn"/>: every building is walk-in, with switchback stairs
        /// from the ground to the top and, above five storeys, a lift (the same city otherwise: the prototype's draws are
        /// made in the same order, so the default city still matches it).
        /// </summary>
        public static int Generate(StoreyDocument d, int count, bool walkIn = false) =>
            Generate(d, new Options { Count = count, WalkIn = walkIn ? 1 : (double?)null });

        /// <summary>Replace the layout's generated buildings with a city made with <paramref name="o"/>. Returns how many were made.</summary>
        public static int Generate(StoreyDocument d, Options o)
        {
            d.buildings.RemoveAll(b => b.gen);
            var made = Buildings(d, o);
            d.buildings.AddRange(made);
            return made.Count;
        }

        /// <summary>A layout's generated buildings in numbers: how many, how many walk-in, their storeys in all and the most, and their lifts.</summary>
        public static (int buildings, int walkIn, int storeys, int tallest, int lifts) Summary(StoreyDocument d)
        {
            int n = 0, walk = 0, storeys = 0, tallest = 0, lifts = 0;
            foreach (var b in d.buildings)
            {
                if (!b.gen) continue;
                n++; storeys += b.floors.Count; tallest = Math.Max(tallest, b.floors.Count);
                if (b.interior) walk++;
                foreach (var s in b.shafts) if (s.type == CoreType.Lift) lifts++;
            }
            return (n, walk, storeys, tallest, lifts);
        }

        /// <summary>How many walk-in buildings <paramref name="o"/> asks for: its share of the count, 0 for the prototype's mix.</summary>
        public static int WalkInWanted(Options o) => o.WalkIn is double share ? (int)Math.Round(Math.Max(0, Math.Min(1, share)) * Math.Max(0, o.Count)) : 0;

        /// <summary>Remove the generated buildings. Returns how many there were.</summary>
        public static int Clear(StoreyDocument d) => d.buildings.RemoveAll(b => b.gen);

        static List<BuildingData> Buildings(StoreyDocument d, Options o)
        {
            int count = Math.Max(0, o.Count);
            var R = new Rng(o.Seed); var out_ = new List<BuildingData>();
            // which buildings are walk-in when a share is asked for: exactly that share of the count, picked one at a time
            // as the buildings are made (selection sampling), from a stream of its own so the city is the same whatever the
            // share; or the first made, the nearest the middle
            var W = new Rng(o.Seed ^ 0x2545F491);
            int want = WalkInWanted(o), picked = 0;
            int most = Math.Max(1, Math.Min(MostStoreys, o.MaxStoreys));
            var keep = d.buildings.Where(b => !b.gen).ToList();
            var occ = keep.Select(b => { var bb = Tiers.Bbox(b.footprint); return (x0: bb.x0 + b.pos.x - 12, x1: bb.x1 + b.pos.x + 12, z0: bb.z0 + b.pos.z - 12, z1: bb.z1 + b.pos.z + 12); }).ToList();
            var blocks = new List<(int bx, int bz, double r)>();
            for (int bx = -30; bx <= 30; bx++) for (int bz = -30; bz <= 30; bz++) blocks.Add((bx, bz, Math.Sqrt(bx * bx + bz * 1.1 * (bz * 1.1))));
            blocks = blocks.OrderBy(b => b.r).ToList();   // stable, as the prototype's sort
            foreach (var (bx, bz, _) in blocks)
            {
                if (out_.Count >= count) break;
                double x0 = bx * Pitch - Block / 2, z0 = bz * Pitch - Block / 2 + 8;
                if (occ.Any(o => x0 < o.x1 && x0 + Block > o.x0 && z0 < o.z1 && z0 + Block > o.z0)) continue;
                int nx = 1 + (int)Math.Floor(R.Next() * 3), nz = 1 + (int)Math.Floor(R.Next() * 3);
                double lw = Block / nx, ld = Block / nz, down = Math.Exp(-Math.Sqrt((x0 + Block / 2) * (x0 + Block / 2) + (z0 + Block / 2) * (z0 + Block / 2)) / 520);
                for (int ix = 0; ix < nx && out_.Count < count; ix++)
                    for (int iz = 0; iz < nz && out_.Count < count; iz++)
                    {
                        double inset = 1 + R.Next() * 2, w = SnapG(lw - 2 * inset), dd = SnapG(ld - 2 * inset);
                        if (w < 8 || dd < 8) continue;
                        // the prototype's storeys, which every draw after goes by; the building gets them scaled and capped
                        int drawn = (int)Math.Max(1, Math.Min(40, JsRound(1 + down * (3 + R.Next() * 32) * (0.5 + R.Next()) + R.Next() * 2)));
                        int floors = o.HeightScale == 1 ? Math.Min(drawn, most) : (int)Math.Max(1, Math.Min(most, JsRound(drawn * o.HeightScale)));
                        bool tall = drawn > 10;
                        string sk = tall ? (R.Next() < 0.6 ? "glass" : "concrete") : StyleKeys[(int)Math.Floor(R.Next() * StyleKeys.Length)];
                        double sh = R.Next();
                        double[][] fp;
                        if (sh < 0.7 || w < 14 || dd < 14) fp = Rect(w, dd);
                        else if (sh < 0.82) fp = new[] { P(0, 0), P(w, 0), P(w, SnapG(dd * 0.5)), P(SnapG(w * 0.5), SnapG(dd * 0.5)), P(SnapG(w * 0.5), dd), P(0, dd) };
                        else if (sh < 0.92) fp = new[] { P(0, 0), P(w, 0), P(w, SnapG(dd * 0.45)), P(SnapG(w * 0.68), SnapG(dd * 0.45)), P(SnapG(w * 0.68), dd), P(SnapG(w * 0.32), dd), P(SnapG(w * 0.32), SnapG(dd * 0.45)), P(0, SnapG(dd * 0.45)) };
                        else { double c = SnapG(Math.Min(w, dd) * 0.28); fp = new[] { P(c, 0), P(w - c, 0), P(w, c), P(w, dd - c), P(w - c, dd), P(c, dd), P(0, dd - c), P(0, c) }; }
                        var b = new BuildingData
                        {
                            id = Edit.Buildings.NewId(d), gen = true, name = $"Block {bx},{bz} · {ix * nz + iz + 1}",
                            pos = new Vec2(SnapG(x0 + ix * lw + inset), SnapG(z0 + iz * ld + inset)),
                            footprint = fp.Select(q => new Vec2(q[0], q[1])).ToList(),
                            floors = Enumerable.Range(0, floors).Select(_ => new FloorData()).ToList(),
                        };
                        b.groundHeight = new[] { 3.6, 4.0, 4.5 }[(int)Math.Floor(R.Next() * 3)];
                        b.floorHeight = tall ? 3.3 : 3.0 + Math.Floor(R.Next() * 4) / 10;
                        b.entrances = new List<EntranceData> { new EntranceData { edge = 0, t = 0.25 + R.Next() * 0.5 } };
                        b.blank = R.Next() < 0.3 ? new List<int> { 1 + (int)Math.Floor(R.Next() * (fp.Length - 1)) } : new List<int>();
                        b.style = Styles.Preset(sk); b.interior = false;
                        if (drawn <= 5 && R.Next() < 0.5) { b.style.roofType = R.Next() < 0.5 ? RoofType.Gable : RoofType.Hip; b.style.pitch = 25 + JsRound(R.Next() * 12); }   // low blocks: pitched roofs
                        // the prototype's walk-in buildings: drawn whatever is asked, so the stream stays the prototype's
                        bool proto = drawn <= 12 && R.Next() < 0.06;
                        bool walkIn = false;
                        if (o.WalkIn != null && picked < want)
                        {
                            int i = out_.Count;   // selection sampling: of the count still to make, as many as are still wanted
                            walkIn = o.WalkInNearMiddle || W.Next() * (count - i) < want - picked;
                            if (walkIn) picked++;
                        }
                        if (proto && (o.WalkIn == null || walkIn))
                        {
                            b.interior = true;
                            var sc = new CoreData { id = Shafts.NewId(d), type = CoreType.Stairs, x = SnapG(w / 2), z = SnapG(dd / 2), rot = 0, bottom = 0, top = -1, roof = true };
                            if (Cores.CoreFits(b.footprint, sc)) b.shafts.Add(sc);
                        }
                        if (walkIn) WalkIn(d, b, w, dd, o.LiftAbove);
                        out_.Add(b);
                    }
            }
            return out_;
        }

        /// <summary>A building made walk-in: stairs, and a lift above <paramref name="liftAbove"/> storeys, where they fit in the footprint.</summary>
        static void WalkIn(StoreyDocument d, BuildingData b, double w, double dd, int liftAbove)
        {
            b.interior = true;
            var fracs = new[] { 0.5, 0.3, 0.7, 0.2, 0.8 };
            CoreData? Fit(CoreType type, CoreData? clear)
            {
                foreach (double rot in new[] { 0.0, 90.0 })
                    foreach (double fx in fracs)
                        foreach (double fz in fracs)
                        {
                            var c = new CoreData { id = "", type = type, x = SnapG(w * fx), z = SnapG(dd * fz), rot = rot, bottom = 0, top = -1, roof = true };
                            if (!Cores.CoreFits(b.footprint, c)) continue;
                            // a corridor's width clear of the stairs: closer, the lift walls off a sliver of floor or the stairs' way in
                            if (clear != null && Shafts.Overlap(c, clear, 1.5)) continue;
                            c.id = Shafts.NewId(d); return c;
                        }
                return null;
            }
            var stairs = b.shafts.FirstOrDefault(s => s.type == CoreType.Stairs) ?? Fit(CoreType.Stairs, null);
            if (stairs == null) return;   // too narrow for stairs anywhere: it stays walk-in, ground floor only
            if (!b.shafts.Contains(stairs)) b.shafts.Add(stairs);
            if (b.floors.Count > liftAbove && Fit(CoreType.Lift, stairs) is CoreData lift) b.shafts.Add(lift);
        }

        static double[] P(double x, double z) => new[] { x, z };
        static double[][] Rect(double w, double d) => new[] { P(0, 0), P(w, 0), P(w, d), P(0, d) };
    }
}
