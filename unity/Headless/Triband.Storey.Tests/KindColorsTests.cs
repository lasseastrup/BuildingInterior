using System;
using System.Collections.Generic;
using System.Linq;
using Triband.Storey.Edit;
using Triband.Storey.Generate;
using Xunit;

namespace Triband.Storey.Tests
{
    /// <summary>
    /// Colours artists' parts keep whatever the style (<see cref="KindColors"/>, docs/OPENINGS.md §6): a slot of their own from
    /// 32 up, carried in the vertex's slot byte, resolved through the palette like a style's. Storey's own.
    /// </summary>
    [Collection("OpeningKinds")]   // the registries are shared
    public class KindColorsTests
    {
        [Fact]
        public void EachColourGetsASlotOfItsOwnPastTheStyles()
        {
            var a = KindColors.SlotOf("#123401")!.Value; var b = KindColors.SlotOf("#123402")!.Value;
            Assert.True((int)a >= ColorRows.Entries && (int)b >= ColorRows.Entries && a != b);
            Assert.Equal(a, KindColors.SlotOf("#123401"));   // the same colour, the same slot
            Assert.Equal("#123402", KindColors.RefOf(b));
            Assert.True(KindColors.Is(a)); Assert.False(KindColors.Is(ColorSlot.Plinth));
            Assert.Null(KindColors.RefOf(ColorSlot.Wall));
            Assert.True(ColorRows.Slots <= ColorRows.Entries - 1, "a style's slots stay under the first kind colour (entry 23 is the remap row)");
        }

        [Fact]
        public void TheTableTakesEachColoursPaletteIndex()
        {
            var palette = new HexPaletteIndex();
            var s = KindColors.SlotOf("#ab0001")!.Value;
            var dest = new uint[KindColors.Capacity];
            int n = KindColors.Fill(palette, dest);
            Assert.True(n > (int)s - KindColors.First);
            Assert.Equal((uint)palette.IndexOf("#ab0001"), dest[(int)s - KindColors.First]);
        }

        /// <summary>A window of a pane and a frame strip, the strip keeping its own colour.</summary>
        static OpeningKind Window(string id, string? frameOwn, string? glassOwn = null)
        {
            var f = new P3(0, 0, 1);
            var m = new OpeningKind.Mesh
            {
                P = new[] { new P3(-0.5, 1.4, 0.03), new P3(0.5, 1.4, 0.03), new P3(0.5, 1.5, 0.03), new P3(-0.5, 1.5, 0.03), new P3(-0.5, 0, -0.05), new P3(0.5, 0, -0.05), new P3(0.5, 1.4, -0.05), new P3(-0.5, 1.4, -0.05) },
                N = Enumerable.Repeat(f, 8).ToArray(),
            };
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Frame, own = frameOwn, tris = new[] { 0, 1, 2, 0, 2, 3 } });
            m.parts.Add(new OpeningKind.Part { slot = ColorSlot.Glass, own = glassOwn, tris = new[] { 4, 5, 6, 4, 6, 7 } });
            return new OpeningKind { id = id, name = id, w = 1, h = 1.5, lod0 = m };
        }

        static (BuildingData b, Site s) Block(string windowKind)
        {
            var d = new StoreyDocument();
            var b = Buildings.Add(d, "rect", new Vec2(0, 0)); b.pos = new Vec2(0, 0);
            b.footprint = new() { new Vec2(0, 0), new Vec2(14, 0), new Vec2(14, 10), new Vec2(0, 10) };
            b.entrances.Clear(); b.details.Clear(); b.shafts.Clear(); b.interior = false;
            b.style = Styles.Preset("brick"); b.style.ground = GroundType.Match; b.style.windowKind = windowKind;
            return (b, new Site(d.buildings));
        }

        [Fact]
        public void APartKeepsItsOwnColourThroughThePacking()
        {
            var k = Window("t-own-colour", "#b0302a", glassOwn: "#00ff00");
            OpeningKinds.Register(k);
            try
            {
                var (b, site) = Block(k.id);
                var op = Lod0.Build(site, b).Op;
                var own = KindColors.SlotOf("#b0302a")!.Value;
                var slots = Enumerable.Range(0, op.Verts).Select(i => op.C[i].slot).ToList();
                Assert.Contains(own, slots);
                Assert.DoesNotContain(ColorSlot.Frame, slots);   // the strip is in its own colour, not the style's frames
                Assert.Contains(ColorSlot.Glass, slots);         // glass keeps the style's: it is the window shader's
                // the census resolves it to the colour itself, whatever the building's style
                var rgb = new ColorResolver(site).Resolve(op.C[slots.IndexOf(own)]);
                var want = Colors.Col("#b0302a");
                Assert.InRange(rgb.r, want.r - 1e-9, want.r + 1e-9); Assert.InRange(rgb.b, want.b - 1e-9, want.b + 1e-9);
                // and the packer writes the slot into the vertex's slot byte
                var packed = MeshPack.Pack(op, windows: true);
                bool seen = false;
                foreach (var pm in packed) for (int i = 0; i < pm.V.Count; i++) if (pm.V[i].b == (byte)own) seen = true;
                Assert.True(seen);
                foreach (var pm in packed) pm.Release();
            }
            finally { OpeningKinds.Remove(k.id); }
        }

        [Fact]
        public void WithoutAnOwnColourAPartTakesTheStyles()
        {
            var k = Window("t-no-own-colour", null);
            OpeningKinds.Register(k);
            try
            {
                var (b, site) = Block(k.id);
                var op = Lod0.Build(site, b).Op;
                Assert.Contains(ColorSlot.Frame, Enumerable.Range(0, op.Verts).Select(i => op.C[i].slot));
                Assert.DoesNotContain(Enumerable.Range(0, op.Verts), i => KindColors.Is(op.C[i].slot));
            }
            finally { OpeningKinds.Remove(k.id); }
        }

        [Fact]
        public void AFacadeDetailsPartKeepsItsOwnColourToo()
        {
            var m = new DetailKind.Mesh
            {
                P = new[] { new P3(-0.3, 0, 0.2), new P3(0.3, 0, 0.2), new P3(0.3, 0.5, 0.2), new P3(-0.3, 0.5, 0.2) },
                N = Enumerable.Repeat(new P3(0, 0, 1), 4).ToArray(),
            };
            m.parts.Add(new DetailKind.Part { slot = ColorSlot.DetailMetal, own = "#2a6f3c", tris = new[] { 0, 1, 2, 0, 2, 3 } });
            var k = new DetailKind { id = Guid.NewGuid().ToString("N"), name = "Test planter", anchor = Details.Anchor.Wall, w = 0.6, h = 0.5, d = 0.2, lod0 = m };
            DetailKinds.Register(k);
            try
            {
                var (b, site) = Block("none");
                b.details.Add(new DetailData { kind = k.id, k = 1, edge = 0, t = 0.5, y = 1.0 });
                var op = Lod0.Build(site, b).Op;
                var own = KindColors.SlotOf("#2a6f3c")!.Value;
                Assert.Contains(Enumerable.Range(0, op.Verts), i => op.C[i].slot == own);
            }
            finally { DetailKinds.Remove(k.id); }
        }
    }
}
