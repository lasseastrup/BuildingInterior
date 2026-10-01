#nullable enable
using System;
using System.Collections.Generic;

namespace Triband.Storey.Generate
{
    /// <summary>Where colour references become palette indices: Color Pipeline's palette, or the built-in hex palette.</summary>
    public interface IColorPalette
    {
        /// <summary>The palette index (atlas column) of a colour reference: a CSS literal or a palette id.</summary>
        int IndexOf(string colorRef);
    }

    /// <summary>
    /// The GPU side of swatches (docs/COLOURS.md §3.3, §3.4), engine-free so the headless tests can check
    /// exactly what the shaders will read. A colour row is 24 16-bit entries in three <c>uint4</c>s: the palette
    /// index of each <see cref="ColorSlot"/> of a style, spare entries, and the atlas row of the building's remap.
    /// </summary>
    public static class ColorRows
    {
        public const int Entries = 24, Words = 12, Uint4s = 3, RemapEntry = 23;
        public static readonly int Slots = Enum.GetValues(typeof(ColorSlot)).Length;
        /// <summary>Rows are numbered in two bytes of the vertex colour.</summary>
        public const int MaxRow = 65535;
        /// <summary>The shade is stored as tone × 128 in one byte: 1.0 exact, up to 1.99.</summary>
        public const double ToneScale = 128;

        /// <summary>The palette index of every slot of a style, in <see cref="ColorSlot"/> order.</summary>
        public static int[] Indices(FacadeStyle st, IColorPalette palette)
        {
            var r = new int[Slots];
            for (int s = 0; s < Slots; s++) r[s] = palette.IndexOf(StyleColors.Of(st, (ColorSlot)s));
            return r;
        }

        /// <summary>Write a row as the shader reads it: entry e is the low (even e) or high half of word e / 2.</summary>
        public static void Pack(int[] indices, int remapRow, uint[] dest, int offset)
        {
            if (indices.Length > RemapEntry) throw new ArgumentException($"a colour row holds {RemapEntry} slots", nameof(indices));
            Array.Clear(dest, offset, Words);
            for (int e = 0; e < indices.Length; e++) Set(dest, offset, e, indices[e]);
            Set(dest, offset, RemapEntry, remapRow);
        }

        static void Set(uint[] w, int offset, int e, int v)
        {
            if (v < 0 || v > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(v), v, "a colour-row entry is 16 bits");
            int i = offset + (e >> 1);
            w[i] = (e & 1) == 0 ? (w[i] & 0xFFFF0000u) | (uint)v : (w[i] & 0xFFFFu) | ((uint)v << 16);
        }

        /// <summary>Entry e of a packed row (what <c>StoreyRowEntry</c> computes in StoreyPalette.hlsl).</summary>
        public static int Entry(uint[] words, int offset, int e)
        {
            uint w = words[offset + (e >> 1)];
            return (int)((e & 1) == 0 ? w & 0xFFFF : w >> 16);
        }

        /// <summary>The LOD0/LOD1 vertex colour bytes: row low, row high, slot, tone × 128.</summary>
        public static (byte r, byte g, byte b, byte a) EncodeVertex(int row, ColorSlot slot, double tone)
        {
            if (row < 0 || row > MaxRow) throw new ArgumentOutOfRangeException(nameof(row), row, "colour rows are numbered in 16 bits");
            double t = Math.Round(tone * ToneScale);
            if (t < 0 || t > 255) throw new ArgumentOutOfRangeException(nameof(tone), tone, "a shade is stored as tone × 128 in one byte");
            return ((byte)(row & 255), (byte)(row >> 8), (byte)slot, (byte)t);
        }

        public static (int row, ColorSlot slot, double tone) DecodeVertex(byte r, byte g, byte b, byte a) =>
            (r | (g << 8), (ColorSlot)b, a / ToneScale);
    }

    /// <summary>
    /// The built-in palette when Color Pipeline is not installed (docs/COLOURS.md §3.7): every distinct CSS
    /// literal in use gets the next index, up to the atlas width. The Unity layer uploads <see cref="Colors"/>
    /// as <c>_GlobalColorPaletteTex</c>.
    /// </summary>
    public sealed class HexPaletteIndex : IColorPalette
    {
        /// <summary>Color Pipeline 2.1.11's atlas width; the shaders address the same way.</summary>
        public const int Capacity = 512;
        readonly Dictionary<string, int> byHex = new Dictionary<string, int>(StringComparer.Ordinal);

        /// <summary>Linear colours by index.</summary>
        public readonly List<Rgb> Colors = new List<Rgb>();
        /// <summary>Bumped whenever a colour is added, so the texture is uploaded only when it changed.</summary>
        public int Version { get; private set; }

        public int IndexOf(string colorRef)
        {
            if (ColorRef.IsPaletteId(colorRef))
                throw new FormatException($"palette id {colorRef} needs Color Pipeline; the built-in palette knows CSS colours only");
            string key = Normalise(colorRef);
            if (byHex.TryGetValue(key, out int i)) return i;
            if (Colors.Count == Capacity) throw new InvalidOperationException($"the built-in palette is full ({Capacity} colours)");
            i = Colors.Count; byHex[key] = i; Colors.Add(Generate.Colors.Col(key)); Version++;
            return i;
        }

        /// <summary><c>#abc</c> and <c>#AABBCC</c> are one colour.</summary>
        static string Normalise(string hex)
        {
            if (!ColorRef.IsHex(hex)) throw new FormatException($"\"{hex}\" is not a CSS colour");
            string h = hex.Substring(1).ToUpperInvariant();
            if (h.Length == 3) h = new string(new[] { h[0], h[0], h[1], h[1], h[2], h[2] });
            return "#" + h;
        }
    }
}
