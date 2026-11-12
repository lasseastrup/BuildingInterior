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
    /// A wall's surface (docs/EDITOR.md §6.7, <see cref="WallSurface"/>): metal cladding, its panels up the wall or along it,
    /// drawn by the shader (StoreyWallSurface.hlsl) as a roof's tiles are (<see cref="RoofSurfaceTests"/>), so it adds no
    /// triangles. It rides in its style's colour row, in two spare entries (<see cref="ColorRows.WallEntry"/>), which an
    /// upright wall face's vertices at full shade read and hand on in a pane's place. Storey's own.
    /// </summary>
    public class WallSurfaceTests
    {
        static string Code(params string[] path) => Regex.Replace(File.ReadAllText(UnityAsset.Package("com.triband.storey", path)), @"//[^\n]*", "");

        [Fact]
        public void ARowCarriesItsWallSurfaceInItsSpareEntries()
        {
            // the entries are spare: no colour slot reaches them (one more fits before them), and the roof's come after
            Assert.True(ColorRows.Slots < ColorRows.WallEntry, $"{ColorRows.Slots} colour slots reach the walls' entries");
            Assert.True(ColorRows.WallEntry + 2 <= ColorRows.RoofEntry);
            var pal = new HexPaletteIndex();
            var st = Styles.Preset("brick").Clone();
            Assert.All(ColorRows.Row(st, pal).Skip(ColorRows.Slots), e => Assert.Equal(0, e));   // nothing for plain walls
            st.wallSurface = new WallSurface { w = 0.8, horizontal = true, relief = 0.4, variation = 0.25 };
            st.roofSurface = new RoofSurface { pattern = RoofPattern.Seams };
            var r = ColorRows.Row(st, pal);
            Assert.Equal(ColorRows.Indices(st, pal), r.Take(ColorRows.Slots));
            Assert.Equal((int)WallPattern.Cladding, r[ColorRows.WallEntry] & 7);
            Assert.Equal(8, r[ColorRows.WallEntry] & 8);                      // along the wall
            Assert.Equal(25, (r[ColorRows.WallEntry] >> 4) & 63);             // relief 0.4 of 63
            Assert.Equal(16, (r[ColorRows.WallEntry] >> 10) & 63);            // variation 0.25
            Assert.Equal(800, r[ColorRows.WallEntry + 1]);                    // the width set, in millimetres
            Assert.Equal((int)RoofPattern.Seams, r[ColorRows.RoofEntry] & 15); // the roof's beside it, unmoved
            // packed, they are where StoreyWallOf reads them: the row's last uint4, its y
            var words = new uint[ColorRows.Words]; ColorRows.Pack(r, 7, words, 0);
            Assert.Equal(r[ColorRows.WallEntry], (int)(words[13] & 0xFFFF));
            Assert.Equal(r[ColorRows.WallEntry + 1], (int)(words[13] >> 16));
            Assert.Equal(r[ColorRows.RoofEntry], (int)(words[14] & 0xFFFF));
            Assert.Equal(7, (int)(words[15] >> 16));   // the remap row
            // the defaults: panels up the wall, half a metre; sizes kept inside what 16 bits of millimetres hold
            st.wallSurface = new WallSurface();
            Assert.Equal((1, false, ColorRows.PanelWidth, 0.6, 0.1), ColorRows.WallSurface(st));
            r = ColorRows.Row(st, pal);
            Assert.Equal(0, r[ColorRows.WallEntry] & 8);
            Assert.Equal(500, r[ColorRows.WallEntry + 1]);
            st.wallSurface = new WallSurface { w = 100, relief = 2, variation = -1 };
            Assert.Equal((1, false, 60.0, 1.0, 0.0), ColorRows.WallSurface(st));
            st.wallSurface.w = 0.001;
            Assert.Equal(0.02, ColorRows.WallSurface(st).w);
        }

        [Fact]
        public void TheShaderDrawsItOnUprightWallFacesAtFullShade()
        {
            var palette = Code("Unity", "Shaders", "StoreyPalette.hlsl");
            Assert.Contains($"#define STOREY_SLOT_WALL {(int)ColorSlot.Wall}u", palette);
            Assert.Contains($"#define STOREY_SLOT_GROUND_WALL {(int)ColorSlot.GroundWall}u", palette);
            Assert.Equal(26, ColorRows.WallEntry);   // StoreyWallOf: the last uint4's y, entries 26 and 27
            Assert.Matches(new Regex(@"uint2 StoreyWallOf\(uint row\)\s+\{\s+row = min\(row, \(uint\)max\(\(int\)_StoreyTableSize\.z - 1, 0\)\);\s+uint4 tail = _StoreyColors\[row \* STOREY_ROW_UINT4S \+ STOREY_ROW_UINT4S - 1\];\s+return uint2\(tail\.y & 0xFFFF, tail\.y >> 16\);"), palette);
            // a vertex of the walls' slots (the ground storey's too) at full shade on an upright face reads it, and hands it on
            // in the pane's place: the panel's width, -1 or -2 for which way they run, minus the pattern plus half the
            // variation, the relief
            var lit = Code("Unity", "Shaders", "StoreyLit.hlsl");
            Assert.Matches(new Regex(@"\[branch\] if \(!pane && \(ce\.z == STOREY_SLOT_WALL \|\| ce\.z == STOREY_SLOT_GROUND_WALL\) && ce\.w == 128u && abs\(IN\.normalOS\.y\) < 0\.2\)\s+\{\s+uint2 r = StoreyWallOf\(ce\.x \| \(ce\.y << 8\)\);\s+if \(\(r\.x & 7u\) != 0u\)\s+surface = float4\(r\.y / 1000\.0, -1\.0 - \(float\)\(\(r\.x >> 3\) & 1u\), -\(\(float\)\(r\.x & 7u\) \+ 0\.5 \* \(float\)\(\(r\.x >> 10\) & 63u\) / 63\.0\), \(float\)\(\(r\.x >> 4\) & 63u\) / 63\.0\);\s+\}"), lit);
            Assert.Matches(new Regex(@"OUT\.win = pane \? float4\([^;]+\) : surface;"), lit);
            // full shade is 128 in the colour's fourth byte: the walls' own faces, not the plinth or bands in a shade of them
            Assert.Equal(128, ColorRows.EncodeVertex(1, ColorSlot.Wall, 1.0).a);
            var C = new Palette(new StyleRef(0, 0));
            Assert.Equal(1.0, C.wall.tone); Assert.Equal(1.0, C.groundWall.tone);
            Assert.NotEqual(128, ColorRows.EncodeVertex(1, C.plinth.slot, C.plinth.tone).a);
            Assert.NotEqual(128, ColorRows.EncodeVertex(1, C.wallBand.slot, C.wallBand.tone).a);
            Assert.Equal(ColorSlot.Wall, C.plinth.slot); Assert.Equal(ColorSlot.Wall, C.wallBand.slot);
            // the fragment draws it, and decodes it as packed
            var frag = Code("Unity", "Shaders", "StoreyFragment.hlsl");
            Assert.Contains("#include \"StoreyWallSurface.hlsl\"", frag);
            Assert.Contains("if (IN.win.y < -0.5) StoreyWallSurface(IN.win, s.positionWS, dpx, dpy, s.albedo, s.normalWS);", frag);
            var wall = Code("Unity", "Shaders", "StoreyWallSurface.hlsl");
            Assert.Contains("float3 t = win.y < -1.5 ? float3(0.0, 1.0, 0.0) : float3(-n.z / l, 0.0, n.x / l);", wall);
            Assert.Contains("shade *= 1.0 + (StoreyRoofHash(float2(floor(g), 7.0)) - 0.5) * 2.0 * frac(-win.z);", wall);
            Assert.Contains("normalWS = normalize(n + t * (lean * win.w * fade));", wall);
        }

        // StoreyCladding's shade, as the shader works it out
        static double Shade(double g, double k)
        {
            static double Smooth(double a, double b, double x) { double t = Math.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }
            double f = g - Math.Floor(g), side = f < 0.5 ? 1 : -1, seam = 1 - Smooth(0, k, Math.Min(f, 1 - f));
            return 1 + 0.2 * seam * side;
        }

        [Fact]
        public void CladdingAveragesThePlainWallItFadesTo()
        {
            // the massing's walls and a wall far off are the plain colour: a panel's shade averages it, at any width
            var wall = Code("Unity", "Shaders", "StoreyWallSurface.hlsl");
            Assert.Contains("float side = f < 0.5 ? 1.0 : -1.0, seam = 1.0 - smoothstep(0.0, k, min(f, 1.0 - f));", wall);
            Assert.Contains("shade = 1.0 + 0.2 * seam * side;", wall);
            Assert.Contains("float w = max(win.x, 0.01), k = min(0.06, 0.03 / w);", wall);
            foreach (double w in new[] { 0.1, 0.5, 2.0, 10.0 })
            {
                double k = Math.Min(0.06, 0.03 / w), sum = 0; int n = 20000;
                for (int i = 0; i < n; i++) sum += Shade((i + 0.5) / n, k);
                Assert.InRange(sum / n, 0.999, 1.001);
            }
        }

        [Fact]
        public void ItRoundTripsAndBakes()
        {
            var b = PrototypeJson.ReadBuilding(PrototypeJson.Write(new BuildingData { id = "b", style = Styles.Preset("brick").Clone() }));
            Assert.DoesNotContain("wallSurface", PrototypeJson.WriteStyle(b.style!));   // plain walls write nothing
            b.style!.wallSurface = new WallSurface { w = 0.8, horizontal = true };
            var json = PrototypeJson.WriteStyle(b.style);
            Assert.Contains("\"wallSurface\":{\"pattern\":\"cladding\",\"w\":0.8,\"horizontal\":true}", json.Replace(" ", ""));
            var back = PrototypeJson.ReadBuilding(PrototypeJson.Write(b)).style!.wallSurface!;
            Assert.Equal((WallPattern.Cladding, (double?)0.8, true, (double?)null, (double?)null), (back.pattern, back.w, back.horizontal, back.relief, back.variation));
            // up the wall, the default, writes no way
            b.style.wallSurface = new WallSurface { relief = 0.3 };
            Assert.Contains("\"wallSurface\":{\"pattern\":\"cladding\",\"relief\":0.3}", PrototypeJson.WriteStyle(b.style).Replace(" ", ""));
            b.style.wallSurface = back;
            var d = new StoreyDocument(); d.buildings.Add(b);
            var baked = BakedLayout.Of(BakedLayout.Bake(d))!.Document().buildings[0].style!.wallSurface!;
            Assert.Equal((back.pattern, back.w, back.horizontal, back.relief, back.variation), (baked.pattern, baked.w, baked.horizontal, baked.relief, baked.variation));
            // a clone owns its surface
            var c = b.style.Clone(); c.wallSurface!.horizontal = false;
            Assert.True(b.style.wallSurface.horizontal);
            // a key it doesn't know is reported, as any other's
            var read = PrototypeJson.Read("{\"v\":2,\"buildings\":[{\"id\":\"a\",\"style\":{\"wallSurface\":{\"pattern\":\"cladding\",\"rot\":90}}}]}");
            Assert.Contains(read.Unknown, u => u.EndsWith("wallSurface.rot"));
        }

        [Fact]
        public void ItAddsNothingToTheMesh()
        {
            // drawn by the shader: the same triangles, at every LOD, clad or not
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0));
            var site = new Site(d.buildings);
            int l0 = Lod0.Build(site, b).Op.Tris, l1 = Lod1.Build(site, b).Tris;
            b.style.wallSurface = new WallSurface { horizontal = true };
            site = new Site(d.buildings);
            Assert.Equal(l0, Lod0.Build(site, b).Op.Tris);
            Assert.Equal(l1, Lod1.Build(site, b).Tris);
        }
    }
}
