using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// A pitched roof's surface (docs/EDITOR.md §6.2, <see cref="RoofSurface"/>): clay tiles, slates, shingles or seams, drawn
    /// by the shader (StoreyRoof.hlsl), so it adds no triangles. It rides in its style's colour row, in three spare entries
    /// (<see cref="ColorRows.RoofEntry"/>), which a roof face's vertices read and hand on in a pane's place. Storey's own.
    /// </summary>
    public class RoofSurfaceTests
    {
        static string Code(params string[] path) => Regex.Replace(File.ReadAllText(UnityAsset.Package("com.triband.storey", path)), @"//[^\n]*", "");

        [Fact]
        public void ARowCarriesItsRoofSurfaceInItsSpareEntries()
        {
            // the entries are spare: no colour slot reaches them, and the remap row comes after
            Assert.True(ColorRows.Slots <= ColorRows.RoofEntry, $"{ColorRows.Slots} colour slots reach the roof's entries");
            Assert.True(ColorRows.RoofEntry + 3 <= ColorRows.RemapEntry);
            var pal = new HexPaletteIndex();
            var st = Styles.Preset("brick").Clone();
            var plain = ColorRows.Row(st, pal);
            Assert.Equal(ColorRows.Indices(st, pal), plain.Take(ColorRows.Slots));
            Assert.All(plain.Skip(ColorRows.Slots), e => Assert.Equal(0, e));   // nothing for a plain roof
            st.roofSurface = new RoofSurface { pattern = RoofPattern.Slate, w = 0.32, relief = 0.4, variation = 0.25 };
            var r = ColorRows.Row(st, pal);
            Assert.Equal(ColorRows.Indices(st, pal), r.Take(ColorRows.Slots));
            Assert.Equal((int)RoofPattern.Slate, r[ColorRows.RoofEntry] & 15);
            Assert.Equal(25, (r[ColorRows.RoofEntry] >> 4) & 63);    // relief 0.4 of 63
            Assert.Equal(16, (r[ColorRows.RoofEntry] >> 10) & 63);   // variation 0.25
            Assert.Equal(320, r[ColorRows.RoofEntry + 1]);           // the width set, in millimetres
            Assert.Equal(220, r[ColorRows.RoofEntry + 2]);           // the slate's own height
            // packed, they are where StoreyRoofOf reads them: the row's last uint4, its z and the low half of its w
            var words = new uint[ColorRows.Words]; ColorRows.Pack(r, 7, words, 0);
            Assert.Equal(r[ColorRows.RoofEntry], (int)(words[14] & 0xFFFF));
            Assert.Equal(r[ColorRows.RoofEntry + 1], (int)(words[14] >> 16));
            Assert.Equal(r[ColorRows.RoofEntry + 2], (int)(words[15] & 0xFFFF));
            Assert.Equal(7, (int)(words[15] >> 16));   // the remap row, unmoved
            // every pattern has a tile of its own, and sizes stay inside what 16 bits of millimetres hold
            foreach (RoofPattern p in Enum.GetValues(typeof(RoofPattern)))
            {
                st.roofSurface = new RoofSurface { pattern = p, w = 100, h = 0.001 };
                var (pattern, w, h, _, _) = ColorRows.RoofSurface(st);
                Assert.Equal((int)p, pattern); Assert.Equal(60, w); Assert.Equal(0.02, h);
                Assert.True(ColorRows.TileSize(p).w > 0 && ColorRows.TileSize(p).h > 0);
            }
        }

        [Fact]
        public void TheShaderDrawsItOnAPitchedRoofsFacesAlone()
        {
            var palette = Code("Unity", "Shaders", "StoreyPalette.hlsl");
            Assert.Contains($"#define STOREY_SLOT_ROOF {(int)ColorSlot.Roof}u", palette);
            Assert.Matches(new Regex(@"uint3 StoreyRoofOf\(uint row\)\s+\{\s+row = min\(row, \(uint\)max\(\(int\)_StoreyTableSize\.z - 1, 0\)\);\s+uint4 tail = _StoreyColors\[row \* STOREY_ROW_UINT4S \+ STOREY_ROW_UINT4S - 1\];\s+return uint3\(tail\.z & 0xFFFF, tail\.z >> 16, tail\.w & 0xFFFF\);"), palette);
            // a vertex of the roof's slot on a face that isn't level reads it, and hands it on in the pane's place
            var lit = Code("Unity", "Shaders", "StoreyLit.hlsl");
            Assert.Matches(new Regex(@"\[branch\] if \(!pane && ce\.z == STOREY_SLOT_ROOF && IN\.normalOS\.y < 0\.985\)\s+\{\s+uint3 r = StoreyRoofOf\(ce\.x \| \(ce\.y << 8\)\);\s+if \(\(r\.x & 15u\) != 0u\)\s+roof = float4\(r\.y / 1000\.0, r\.z / 1000\.0, -\(\(float\)\(r\.x & 15u\) \+ 0\.5 \* \(float\)\(\(r\.x >> 10\) & 63u\) / 63\.0\), \(float\)\(\(r\.x >> 4\) & 63u\) / 63\.0\);\s+\}"), lit);
            Assert.Matches(new Regex(@"OUT\.win = pane \? float4\([^;]+\) : roof;"), lit);
            // its fragments draw it (the colour passes of the opaque materials alone), after the derivatives every pixel takes
            var frag = Code("Unity", "Shaders", "StoreyFragment.hlsl");
            Assert.Matches(new Regex(@"#if !defined\(STOREY_MASSING\) && !defined\(STOREY_GLASS\)\s+float3 dpx = ddx\(s\.positionWS\), dpy = ddy\(s\.positionWS\);\s+if \(IN\.win\.z < -0\.5 && isFront\) StoreyRoofSurface\(IN\.win, s\.positionWS, dpx, dpy, s\.albedo, s\.normalWS\);\s+#endif"), frag);
            Assert.Contains("#include \"StoreyRoof.hlsl\"", frag);
            // a pane's place below 0 is never a pane: every test of a pane wants it over 0.5
            Assert.Contains("if (IN.win.z > 1.5 && isFront)", frag);
            Assert.Matches(new Regex(@"float onPane = reveal \? 0\.0 : IN\.win\.z;"), frag);
            Assert.Contains("if (onPane > 0.5 && isFront)", frag);
            // and the pattern decodes the row as packed: minus the pattern plus half its variation, the relief in w
            var roof = Code("Unity", "Shaders", "StoreyRoof.hlsl");
            Assert.Contains("float p = -win.z;", roof);
            Assert.Contains("StoreyRoofPattern(pattern, g, 2.0 * frac(p), shade, lean);", roof);
            Assert.Contains("normalWS = normalize(n + (e3 * lean.x + up * lean.y) * (win.w * fade));", roof);
        }

        // StoreyRoofPattern's shade without the tile's own (variation 0), as the shader works it out
        static double Shade(int pattern, double fx, double fy)
        {
            static double Smooth(double a, double b, double x) { double t = Math.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }
            switch (pattern)
            {
                case 1:
                {
                    double crest = Math.Sin(fx * Math.PI), trough = (1 - crest) * (1 - crest), under = Smooth(0.82, 1.0, fy), lip = 1 - Smooth(0.0, 0.1, fy);
                    return 1.095 * (1 - 0.25 * trough) * (1 - 0.4 * under) * (1 + 0.08 * lip);
                }
                case 2:
                {
                    double joint = 1 - Smooth(0, 0.03, Math.Min(fx, 1 - fx)), foot = Smooth(0.88, 1.0, fy);
                    return 1.044 * (1 - 0.5 * joint) * (1 - 0.45 * foot);
                }
                case 3:
                {
                    double x = 2 * fx - 1, arc = 0.35 * (1 - Math.Sqrt(Math.Clamp(1 - x * x, 0, 1)));
                    double below = fy < arc ? 1 - Smooth(0, 0.14, arc - fy) : 0, rim = fy >= arc ? 1 - Smooth(0, 0.05, fy - arc) : 0;
                    return 1.015 * (1 - 0.45 * below) * (1 + 0.12 * rim);
                }
                default:
                {
                    double side = fx < 0.5 ? 1 : -1, seam = 1 - Smooth(0, 0.07, Math.Min(fx, 1 - fx));
                    return 1 + 0.2 * seam * side;
                }
            }
        }

        [Fact]
        public void EveryPatternAveragesThePlainRoofItFadesTo()
        {
            // the massing's roof and a roof far off are the plain colour: a tile's shade averages it, within a few per cent
            var roof = Code("Unity", "Shaders", "StoreyRoof.hlsl");
            foreach (var k in new[] { "shade = 1.095 * (1.0 - 0.25 * trough) * (1.0 - 0.4 * under) * (1.0 + 0.08 * lip);", "shade = 1.044 * (1.0 - 0.5 * joint) * (1.0 - 0.45 * foot);",
                "shade = 1.015 * (1.0 - 0.45 * below) * (1.0 + 0.12 * rim);", "shade = 1.0 + 0.2 * seam * side;" })
                Assert.Contains(k, roof);   // the shades this test works out
            foreach (int p in new[] { 1, 2, 3, 4 })
            {
                double sum = 0; int n = 400;
                for (int i = 0; i < n; i++) for (int j = 0; j < n; j++) sum += Shade(p, (i + 0.5) / n, (j + 0.5) / n);
                Assert.InRange(sum / (n * n), 0.97, 1.03);
            }
        }

        [Fact]
        public void ItRoundTripsAndBakes()
        {
            var b = PrototypeJson.ReadBuilding(PrototypeJson.Write(new BuildingData { id = "b", style = Styles.Preset("brick").Clone() }));
            Assert.DoesNotContain("roofSurface", PrototypeJson.WriteStyle(b.style!));   // a plain roof writes nothing
            b.style!.roofSurface = new RoofSurface { pattern = RoofPattern.Shingles, h = 0.2 };
            var json = PrototypeJson.WriteStyle(b.style);
            Assert.Contains("\"roofSurface\":{\"pattern\":\"shingles\",\"h\":0.2}", json.Replace(" ", ""));
            var back = PrototypeJson.ReadBuilding(PrototypeJson.Write(b)).style!.roofSurface!;
            Assert.Equal((RoofPattern.Shingles, (double?)null, (double?)0.2, (double?)null, (double?)null), (back.pattern, back.w, back.h, back.relief, back.variation));
            var d = new StoreyDocument(); d.buildings.Add(b);
            var baked = BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].style!.roofSurface!;
            Assert.Equal((back.pattern, back.w, back.h, back.relief, back.variation), (baked.pattern, baked.w, baked.h, baked.relief, baked.variation));
            // a clone owns its surface
            var c = b.style.Clone(); c.roofSurface!.pattern = RoofPattern.Seams;
            Assert.Equal(RoofPattern.Shingles, b.style.roofSurface.pattern);
        }
    }
}
