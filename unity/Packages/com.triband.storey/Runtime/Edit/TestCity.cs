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
    /// buildings are marked <see cref="BuildingData.gen"/>; making a city again replaces them.
    /// </summary>
    public static class TestCity
    {
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
        /// with 12 m kept clear round each.
        /// </summary>
        public static int Generate(StoreyDocument d, int count)
        {
            d.buildings.RemoveAll(b => b.gen);
            var made = Buildings(d, count);
            d.buildings.AddRange(made);
            return made.Count;
        }

        /// <summary>Remove the generated buildings. Returns how many there were.</summary>
        public static int Clear(StoreyDocument d) => d.buildings.RemoveAll(b => b.gen);

        static List<BuildingData> Buildings(StoreyDocument d, int count)
        {
            var R = new Rng(Seed); var out_ = new List<BuildingData>();
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
                        int floors = (int)Math.Max(1, Math.Min(40, JsRound(1 + down * (3 + R.Next() * 32) * (0.5 + R.Next()) + R.Next() * 2)));
                        bool tall = floors > 10;
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
                        if (floors <= 5 && R.Next() < 0.5) { b.style.roofType = R.Next() < 0.5 ? RoofType.Gable : RoofType.Hip; b.style.pitch = 25 + JsRound(R.Next() * 12); }   // low blocks: pitched roofs
                        if (floors <= 12 && R.Next() < 0.06)
                        {
                            b.interior = true;
                            var sc = new CoreData { id = Shafts.NewId(d), type = CoreType.Stairs, x = SnapG(w / 2), z = SnapG(dd / 2), rot = 0, bottom = 0, top = -1, roof = true };
                            if (Cores.CoreFits(b.footprint, sc)) b.shafts.Add(sc);
                        }
                        out_.Add(b);
                    }
            }
            return out_;
        }

        static double[] P(double x, double z) => new[] { x, z };
        static double[][] Rect(double w, double d) => new[] { P(0, 0), P(w, 0), P(w, d), P(0, d) };
    }
}
