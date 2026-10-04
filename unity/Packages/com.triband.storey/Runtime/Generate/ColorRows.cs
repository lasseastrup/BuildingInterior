#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Triband.Storey.Generate
{
    /// <summary>Where colour references become palette indices: Color Pipeline's palette, or the built-in hex palette.</summary>
    public interface IColorPalette
    {
        /// <summary>The palette index (atlas column) of a colour reference: a CSS literal or a palette id.</summary>
        int IndexOf(string colorRef);

        /// <summary>The project's colours for slots a style leaves out.</summary>
        StyleDefaults Defaults { get; }

        /// <summary>
        /// Register a remap (palette id → palette id, pairwise) and return its atlas row; 0 for an empty one. Rows
        /// handed out before <see cref="Invalidated"/> are stale and must be registered again.
        /// </summary>
        int RegisterRemap(IReadOnlyList<string> original, IReadOnlyList<string> overwrite);

        /// <summary>Raised when palette indices or remap rows may have moved. May fire inside <see cref="RegisterRemap"/>.</summary>
        event Action? Invalidated;
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
            for (int s = 0; s < Slots; s++) r[s] = palette.IndexOf(StyleColors.Of(st, (ColorSlot)s, palette.Defaults));
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

        /// <summary>
        /// A window pane's vertex (docs/EDITOR.md §6.8): its place on the pane across (the colour's shade byte, 0 to 255),
        /// up (the normal's fourth byte, 1 to 127; 0 marks every other vertex) and the window's number (the slot byte). A
        /// pane's slot and shade are the opaque glass's, fixed in the shader.
        /// </summary>
        public static (byte a, sbyte nw, byte b) EncodePane(double u, double v, byte id) =>
            ((byte)Math.Round(Math.Max(0, Math.Min(1, u)) * 255), (sbyte)(1 + Math.Round(Math.Max(0, Math.Min(1, v)) * 126)), id);

        /// <summary>What the shader reads back (StoreyWindow.hlsl): u, v, the window's number.</summary>
        public static (double u, double v, int id) DecodePane(byte a, sbyte nw, byte b) => (a / 255.0, (nw - 1) / 126.0, b);

        /// <summary>A pane's shade: the opaque glass's (<c>Palette.glassDark</c>).</summary>
        public const double PaneTone = 0.42;

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

        public StyleDefaults Defaults { get; } = new StyleDefaults();

        /// <summary>Remaps need Color Pipeline's atlas; only the empty remap (row 0) exists here.</summary>
        public int RegisterRemap(IReadOnlyList<string> original, IReadOnlyList<string> overwrite)
        {
            if (original.Count == 0 && overwrite.Count == 0) return 0;
            throw new NotSupportedException("colour remaps need Color Pipeline; the built-in palette has no remap rows");
        }

        /// <summary>Never raised: indices never move, colours are only added.</summary>
        public event Action? Invalidated { add { } remove { } }

        public int IndexOf(string colorRef)
        {
            if (ColorRef.IsPaletteId(colorRef))
                throw new FormatException($"palette id {colorRef} needs Color Pipeline; the built-in palette knows CSS colours only");
            string key = ColorRef.Normalise(colorRef);
            if (byHex.TryGetValue(key, out int i)) return i;
            if (Colors.Count == Capacity) throw new InvalidOperationException($"the built-in palette is full ({Capacity} colours)");
            i = Colors.Count; byHex[key] = i; Colors.Add(Generate.Colors.Col(key)); Version++;
            return i;
        }
    }
}

namespace Triband.Storey.Generate
{
    /// <summary>Where colour rows live: the GPU building table, or a test's array.</summary>
    public interface IColorRowSink
    {
        int AllocColorRow();
        void ReleaseColorRow(int row);
        void WriteColorRow(int row, int[] paletteIndices, int remapRow);
    }

    /// <summary>
    /// The colour rows of a site (docs/COLOURS.md §3.3, §3.6): one per style a mesh names, written with the
    /// palette's current indices and the building's remap row. A colour change or a palette invalidation
    /// rewrites rows, never meshes. Engine-free so the bookkeeping is tested headlessly; the Unity layer
    /// passes its building table as the sink and the active palette.
    /// </summary>
    public sealed class ColorRowBook : IDisposable
    {
        ColorResolver colors;
        readonly IColorPalette palette;
        readonly IColorRowSink sink;
        readonly Dictionary<StyleRef, int> rows = new Dictionary<StyleRef, int>();
        readonly Dictionary<int, (string[] original, string[] overwrite)> remaps = new Dictionary<int, (string[], string[])>();
        readonly Dictionary<int, int> remapRows = new Dictionary<int, int>();
        bool rewriting, again;

        public ColorRowBook(ColorResolver colors, IColorPalette palette, IColorRowSink sink)
        {
            this.colors = colors; this.palette = palette; this.sink = sink;
            palette.Invalidated += Rewrite;
        }

        public IReadOnlyDictionary<StyleRef, int> Rows => rows;

        /// <summary>The colour row of a style, allocated and written the first time a mesh names it.</summary>
        public int RowOf(StyleRef s)
        {
            if (rows.TryGetValue(s, out int row)) return row;
            row = sink.AllocColorRow();
            rows[s] = row;
            Write(s, row);
            return row;
        }

        /// <summary>The atlas row of a building's remap; 0 = none.</summary>
        public int RemapRowOf(int building) => remapRows.TryGetValue(building, out int r) ? r : 0;

        /// <summary>
        /// Remap a building's colours (palette ids, pairwise), as a <c>ColorRemap</c> would a renderer's. Empty
        /// arrays clear it. Rewrites the building's rows only.
        /// </summary>
        public void SetRemap(int building, IReadOnlyList<string> original, IReadOnlyList<string> overwrite)
        {
            if (original.Count != overwrite.Count) throw new ArgumentException("a remap pairs each original colour with an overwrite colour");
            if (original.Count == 0) { remaps.Remove(building); remapRows.Remove(building); }
            else
            {
                var r = (original.ToArray(), overwrite.ToArray());
                remaps[building] = r;
                remapRows[building] = palette.RegisterRemap(r.Item1, r.Item2);
            }
            foreach (var kv in rows.ToList()) if (kv.Key.building == building) Write(kv.Key, kv.Value);
        }

        /// <summary>
        /// Register every remap again and write every row: the palette's indices or the atlas rows moved. Safe
        /// when the palette raises its event again from inside a registration (Color Pipeline creates its atlas lazily).
        /// </summary>
        public void Rewrite()
        {
            if (rewriting) { again = true; return; }
            rewriting = true;
            try
            {
                do
                {
                    again = false;
                    foreach (var kv in remaps.ToList()) remapRows[kv.Key] = palette.RegisterRemap(kv.Value.original, kv.Value.overwrite);
                    foreach (var kv in rows.ToList()) Write(kv.Key, kv.Value);
                } while (again);
            }
            finally { rewriting = false; }
        }

        void Write(StyleRef s, int row) => sink.WriteColorRow(row, ColorRows.Indices(colors.StyleOf(s), palette), RemapRowOf(s.building));

        /// <summary>
        /// Resolve styles from another site (the layout was edited or undone; building indices unchanged). With
        /// <paramref name="rewrite"/>, every row is written again, keeping its number so meshes that are not rebuilt stay
        /// right. An edit passes false: only the buildings it rebuilds change, and their rows are freed
        /// (<see cref="ReleaseBuilding"/>) and written afresh as they are built. Rewriting all of them cost 0.9 s an edit on
        /// a 300-building layout with Color Pipeline.
        /// </summary>
        public void Retarget(ColorResolver resolver, bool rewrite = true) { colors = resolver; if (rewrite) Rewrite(); }

        /// <summary>Free one building's rows before its meshes are built again (its tiers may have changed).</summary>
        public void ReleaseBuilding(int building)
        {
            foreach (var kv in rows.Where(kv => kv.Key.building == building).ToList()) { sink.ReleaseColorRow(kv.Value); rows.Remove(kv.Key); }
        }

        /// <summary>Free every row (the site is being rebuilt); remaps are kept.</summary>
        public void Clear()
        {
            foreach (var row in rows.Values) sink.ReleaseColorRow(row);
            rows.Clear();
        }

        public void Dispose() { Clear(); palette.Invalidated -= Rewrite; }
    }
}
