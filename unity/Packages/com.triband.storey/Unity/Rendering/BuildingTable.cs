#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Triband.Storey.Unity
{
    /// <summary>
    /// The per-building GPU tables every Storey material reads (SPEC §6.4, Plan §6.1): one row per
    /// building for LOD state and the occluder slot, the occluder rows for buildings in the way, the
    /// cutaway slide per wall id, the LOD2 parameter rows, and the colour rows that name each style's
    /// palette entries (docs/COLOURS.md §3.3). All are global structured buffers,
    /// so thousands of buildings need no per-object material state and merged meshes can still
    /// switch individual buildings. Written on the CPU into arrays; once per frame, the chunks that changed are
    /// uploaded (<see cref="Lod.DirtyChunks"/>), not the whole buffers (docs/CITY.md §7).
    ///
    /// Global buffers mean one table for everything drawn (docs/CITY.md §5): every Storey Site in the loaded scenes
    /// shares the one from <see cref="Acquire"/>, each with its own block of building indices (<see cref="AllocBlock"/>).
    /// Two tables would each bind themselves as the globals, and the last to bind would win for every site.
    /// </summary>
    public sealed class BuildingTable : IDisposable, Generate.IColorRowSink
    {
        static BuildingTable? shared; static int users;

        /// <summary>The one table every site draws through; made by the first, disposed with the last (<see cref="ReleaseShared"/>).</summary>
        public static BuildingTable Acquire() { users++; return shared ??= new BuildingTable(); }

        /// <summary>A site is done with the shared table.</summary>
        public static void ReleaseShared(BuildingTable t)
        {
            if (!ReferenceEquals(t, shared)) { t.Dispose(); return; }
            if (--users > 0) return;
            shared.Dispose(); shared = null; users = 0;
        }

        // building indices, handed out in blocks: one per site, its layout's buildings in order. The last OccSlots rows
        // are the footprints' (StoreyOcclusion), one per occluder slot
        readonly Lod.IndexBlocks blocks = new Lod.IndexBlocks(Capacity);

        /// <summary>A block of <paramref name="n"/> building indices (the first returned), or -1 when none is free.</summary>
        public int AllocBlock(int n) => blocks.Alloc(n);

        /// <summary>Give a block back: its buildings culled, the indices free for another site.</summary>
        public void ReleaseBlock(int start, int n)
        {
            if (n <= 0 || start < 0) return;
            for (int i = start; i < start + n; i++) State[i] = new Vector4(3, 3, 1, 0);
            stateDirty.Mark(start, n);
            blocks.Release(start, n);
        }
        public const int MaxBuildings = 8192;
        public const int OccSlots = 16, OccWidth = 64;
        /// <summary>The buildings all loaded sites can have between them: the rest of the rows are the footprints'.</summary>
        public const int Capacity = MaxBuildings - OccSlots;
        /// <summary>The row the footprint of the occluder in <paramref name="slot"/> draws through: its own, so it can fade.</summary>
        public static int FootprintRow(int slot) => MaxBuildings - 1 - slot;
        /// <summary>The most wall ids there can be (a vertex carries its wall's id as a float).</summary>
        public const int WallIds = 65536;
        /// <summary>The wall tables start this big and double when full, up to <see cref="WallIds"/>: the walls of the buildings at LOD0, a few thousand.</summary>
        public const int WallIdsStart = 8192;
        /// <summary>
        /// The parameter (LOD2 massing) and colour-row tables start this big and double when full. They used to be fixed,
        /// and a big layout (the 3,000-building test city) wrote every row past the last into the last one, so many
        /// buildings' massings showed another building's colours and windows.
        /// </summary>
        public const int ParamRowsStart = 1024, ParamTexels = 6;
        public const int ColorRowsStart = 1024;

        /// <summary>
        /// Per building: displayed LOD (past <see cref="Lod.StateRow.Hidden"/> while hidden, <see cref="SetHidden"/>), previous
        /// LOD, cross-fade 0..1, occluder slot + 1 (0 = none).
        /// </summary>
        public readonly Vector4[] State = new Vector4[MaxBuildings];
        /// <summary>Per occluder slot, OccWidth texels: header (segments, dark, clip height, base height) then storey rows.</summary>
        public readonly Vector4[] Occ = new Vector4[OccSlots * OccWidth];
        /// <summary>How far each wall has slid down, 0 up .. 1 down, by global wall id. Write through <see cref="SetWall"/>.</summary>
        public float[] Wall { get; private set; } = new float[WallIdsStart];
        /// <summary>Per wall id: the wall's start (x, z) and its normal scaled by 1 + its length, for the cutaway (written once, at upload; <see cref="SetWallData"/>).</summary>
        public Vector4[] WallData { get; private set; } = new Vector4[WallIdsStart];
        /// <summary>LOD2 parameter rows: wall, trim, glass, roof colours, window spec, run data.</summary>
        public Vector4[] Params { get; private set; } = new Vector4[ParamRowsStart * ParamTexels];
        /// <summary>
        /// Colour rows: per style, the palette index of each slot and the remap row, 32 16-bit entries in 16 words, four of
        /// the buffer's 16-byte elements a row, so a changed row goes up alone (docs/PERFORMANCE.md §6.8).
        /// </summary>
        public ColorWords[] Colors { get; private set; } = new ColorWords[ColorRowsStart * Generate.ColorRows.Uint4s];

        /// <summary>Four words of a colour row: one of the colour buffer's elements (a <c>uint4</c> to the shaders).</summary>
        public struct ColorWords { public uint x, y, z, w; }
        readonly uint[] packed = new uint[Generate.ColorRows.Words];
        /// <summary>
        /// Palette indices of the colours artists' parts keep whatever the style (slots 32 and up, <see cref="Generate.KindColors"/>,
        /// docs/OPENINGS.md §6): written by <see cref="SyncKindColors"/>. One for every slot a vertex's byte can name: the
        /// shaders read it for every colour (StoreyPalette.hlsl), and on Metal a read past a buffer's end can fault the GPU.
        /// </summary>
        public readonly uint[] DetailColors = new uint[256 - Generate.ColorRows.Entries];

        readonly GraphicsBuffer state, occ, details;
        GraphicsBuffer wall, wallData, prms, colors;   // these grow (WallIdsStart, ParamRowsStart)
        // what changed since the last upload, chunk by chunk: a frame uploads a few kilobytes, not every buffer whole
        readonly Lod.DirtyChunks stateDirty = new Lod.DirtyChunks(MaxBuildings, 64), wallDirty = new Lod.DirtyChunks(WallIdsStart, 256),
            wallDataDirty = new Lod.DirtyChunks(WallIdsStart, 64), paramsDirty = new Lod.DirtyChunks(ParamRowsStart * ParamTexels, 16 * ParamTexels),
            colorsDirty = new Lod.DirtyChunks(ColorRowsStart * Generate.ColorRows.Uint4s, 16 * Generate.ColorRows.Uint4s);
        // partial uploads only where an array element is a buffer element
        readonly Lod.DirtyChunks occDirty = new Lod.DirtyChunks(OccSlots * OccWidth, OccWidth);   // a chunk an occluder slot
        bool detailsDirty = true;
        // the palette the kind colours were written with, the colours' version then, and whether its indices moved since
        Generate.IColorPalette? kindPalette; int kindVersion = -1; bool kindStale;
        readonly List<(int start, int count)> runs = new List<(int, int)>();

        // free lists, as the prototype keeps them
        readonly Stack<int> freeRow = new Stack<int>(); int nextRow;
        readonly Stack<int> freeColorRow = new Stack<int>(); int nextColorRow;
        readonly Stack<int> freeSlot = new Stack<int>();
        readonly List<(int start, int n)> freeWalls = new List<(int, int)>();

        public BuildingTable()
        {
            for (int i = 0; i < MaxBuildings; i++) State[i] = new Vector4(3, 3, 1, 0);   // culled, culled, faded in, no occluder
            for (int s = OccSlots - 1; s >= 0; s--) freeSlot.Push(s);
            freeWalls.Add((1, WallIdsStart - 1));   // id 0 = none
            state = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxBuildings, 16);
            occ = new GraphicsBuffer(GraphicsBuffer.Target.Structured, OccSlots * OccWidth, 16);
            wall = new GraphicsBuffer(GraphicsBuffer.Target.Structured, WallIdsStart, 4);
            wallData = new GraphicsBuffer(GraphicsBuffer.Target.Structured, WallIdsStart, 16);
            stateDirty.MarkAll(); occDirty.MarkAll(); wallDirty.MarkAll(); wallDataDirty.MarkAll(); paramsDirty.MarkAll(); colorsDirty.MarkAll();
            prms = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ParamRowsStart * ParamTexels, 16);
            colors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ColorRowsStart * Generate.ColorRows.Uint4s, 16);
            details = new GraphicsBuffer(GraphicsBuffer.Target.Structured, DetailColors.Length, 4);
        }

        // ---- allocation --------------------------------------------------------------------

        /// <summary>The wall ids handed out now, of the <see cref="Wall"/> table's (for <see cref="StoreyDiagnostics"/>).</summary>
        public int WallsInUse { get { int free = 0; foreach (var f in freeWalls) free += f.n; return Wall.Length - 1 - free; } }

        /// <summary>An occluder slot, or -1 when all are taken.</summary>
        public int AllocOccSlot() => freeSlot.Count > 0 ? freeSlot.Pop() : -1;
        public void ReleaseOccSlot(int slot) { freeSlot.Push(slot); }

        /// <summary>A block of <paramref name="n"/> wall ids (first id returned), or -1 when none is free. Ids are 1-based; 0 means no id.</summary>
        public int AllocWalls(int n)
        {
            if (n <= 0) return -1;
            int got = TakeWalls(n);
            // full: the wall tables twice the size (up to WallIds), new buffers of them next upload
            while (got < 0 && Wall.Length < WallIds)
            {
                int was = Wall.Length, now = Math.Min(WallIds, was * 2);
                var w = Wall; Array.Resize(ref w, now); Wall = w;
                var wd = WallData; Array.Resize(ref wd, now); WallData = wd;
                wall.Dispose(); wall = new GraphicsBuffer(GraphicsBuffer.Target.Structured, now, 4);
                wallData.Dispose(); wallData = new GraphicsBuffer(GraphicsBuffer.Target.Structured, now, 16);
                wallDirty.Grow(now); wallDataDirty.Grow(now);
                if (StoreyDiagnostics.Recording) StoreyDiagnostics.Note($"wall table grown: {was:N0} → {now:N0} ids");
                ReleaseWalls(was, now - was);
                got = TakeWalls(n);
            }
            return got;
        }

        int TakeWalls(int n)
        {
            for (int i = 0; i < freeWalls.Count; i++)
            {
                var f = freeWalls[i];
                if (f.n < n) continue;
                int start = f.start;
                if (f.n == n) freeWalls.RemoveAt(i); else freeWalls[i] = (f.start + n, f.n - n);
                return start;
            }
            return -1;
        }

        public void ReleaseWalls(int start, int n)
        {
            if (start < 0 || n <= 0) return;
            Array.Clear(Wall, start, n); wallDirty.Mark(start, n);
            freeWalls.Add((start, n));
            freeWalls.Sort((a, b) => a.start.CompareTo(b.start));
            for (int i = freeWalls.Count - 2; i >= 0; i--)
                if (freeWalls[i].start + freeWalls[i].n == freeWalls[i + 1].start) { freeWalls[i] = (freeWalls[i].start, freeWalls[i].n + freeWalls[i + 1].n); freeWalls.RemoveAt(i + 1); }
        }

        /// <summary>A parameter row, written from a generator row; rows are freed with <see cref="ReleaseRow"/>.</summary>
        public int WriteRow(Generate.ParamRow row, int colorRow)
        {
            int x = freeRow.Count > 0 ? freeRow.Pop() : nextRow++;
            if ((x + 1) * ParamTexels > Params.Length) GrowParams(x + 1);
            Span<float> t = stackalloc float[ParamTexels * 4];   // the row's texels, no array of their own (docs/PERFORMANCE.md §6.8)
            row.GpuTexels(colorRow, t); int o = x * ParamTexels;
            for (int i = 0; i < ParamTexels; i++) Params[o + i] = new Vector4(t[i * 4], t[i * 4 + 1], t[i * 4 + 2], t[i * 4 + 3]);
            paramsDirty.Mark(o, ParamTexels);
            return x;
        }

        // full: doubled until it holds rows, a new buffer of it next upload
        void GrowParams(int rows)
        {
            int n = Params.Length; while (n < rows * ParamTexels) n *= 2;
            if (n == Params.Length) return;
            var grown = new Vector4[n]; Array.Copy(Params, grown, Params.Length); Params = grown;
            prms.Dispose(); prms = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Params.Length, 16);
            paramsDirty.Grow(Params.Length);
            if (StoreyDiagnostics.Recording) StoreyDiagnostics.Note($"parameter table grown to {Params.Length / ParamTexels:N0} rows");
        }

        void GrowColors(int rows)
        {
            int n = Colors.Length; while (n < rows * Generate.ColorRows.Uint4s) n *= 2;
            if (n == Colors.Length) return;
            var grown = new ColorWords[n]; Array.Copy(Colors, grown, Colors.Length); Colors = grown;
            colors.Dispose(); colors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Colors.Length, 16);
            colorsDirty.Grow(Colors.Length);
            if (StoreyDiagnostics.Recording) StoreyDiagnostics.Note($"colour table grown to {Colors.Length / Generate.ColorRows.Uint4s:N0} rows");
        }

        /// <summary>
        /// Room for this many more parameter and colour rows than are in use, made at once: a layout loading sizes the tables
        /// once, where they doubled as it went, each doubling a new buffer and a copy (docs/PERFORMANCE.md §6.8).
        /// </summary>
        public void Reserve(int paramRows, int colorRows)
        {
            GrowParams(nextRow + paramRows);
            GrowColors(nextColorRow + colorRows);
        }
        public void ReleaseRow(int row) => freeRow.Push(row);

        /// <summary>A colour row, filled with <see cref="WriteColorRow"/>; freed with <see cref="ReleaseColorRow"/>.</summary>
        public int AllocColorRow()
        {
            int x = freeColorRow.Count > 0 ? freeColorRow.Pop() : nextColorRow++;
            if ((x + 1) * Generate.ColorRows.Uint4s > Colors.Length) GrowColors(x + 1);
            return x;
        }
        public void ReleaseColorRow(int row) => freeColorRow.Push(row);

        /// <summary>A style's palette indices (in <see cref="Generate.ColorSlot"/> order) and its remap's atlas row (0 = none).</summary>
        public void WriteColorRow(int row, int[] paletteIndices, int remapRow)
        {
            Generate.ColorRows.Pack(paletteIndices, remapRow, packed, 0);
            int o = row * Generate.ColorRows.Uint4s;
            for (int q = 0; q < Generate.ColorRows.Uint4s; q++)
                Colors[o + q] = new ColorWords { x = packed[q * 4], y = packed[q * 4 + 1], z = packed[q * 4 + 2], w = packed[q * 4 + 3] };
            colorsDirty.Mark(o, Generate.ColorRows.Uint4s);
        }

        // ---- writes ---------------------------------------------------------------------------

        /// <summary>A building's LOD state: what it shows, what fades out and how far. A hidden one stays hidden.</summary>
        public void SetLod(int idx, int displayed, int from, float fade)
        {
            var s = State[idx];
            SetState(idx, new Vector4(Lod.StateRow.Displayed(displayed, Lod.StateRow.IsHidden(s.x)), from, fade, s.w));
        }

        /// <summary>
        /// A building hidden, or shown again (docs/PLAY.md §6, <see cref="SiteRenderer.SetHidden"/>): its displayed LOD moved past
        /// <see cref="Lod.StateRow.Hidden"/> or back, the rest of its row as it is. The shaders draw none of it in the colour and
        /// depth passes, at any LOD and in its cell, and cast its shadow as before. A row given back (a building let go, a
        /// site's block released) is shown again.
        /// </summary>
        public void SetHidden(int idx, bool hidden)
        {
            var s = State[idx];
            SetState(idx, new Vector4(Lod.StateRow.Hide(s.x, hidden), s.y, s.z, s.w));
        }
        public void SetOccluder(int idx, int slotOrMinusOne) { var s = State[idx]; SetState(idx, new Vector4(s.x, s.y, s.z, slotOrMinusOne + 1)); }

        /// <summary>
        /// A building's state row (the chunk is uploaded only when it changed). One whose steady answer may have changed with
        /// it is marked in <see cref="SteadyChanged"/>.
        /// </summary>
        public void SetState(int idx, Vector4 v)
        {
            var s = State[idx]; if (s == v) return;
            if (!Occlusion.Steady.Same(s.z, s.w, v.z, v.w)) SteadyChanged.Mark(idx);
            State[idx] = v; stateDirty.Mark(idx);
        }

        /// <summary>
        /// The buildings' rows whose fade or occluder slot changed in a way the steady variant goes by
        /// (<see cref="Occlusion.Steady.Same"/>): each site takes its block's before the cameras render, and looks at only
        /// those buildings again (docs/PERFORMANCE.md §3.3). The footprints' rows are left out.
        /// </summary>
        public readonly Lod.Marks SteadyChanged = new Lod.Marks(Capacity);

        /// <summary>A wall's slide (uploaded only when it changed).</summary>
        public void SetWall(int id, float slide) { if (Wall[id] == slide) return; Wall[id] = slide; wallDirty.Mark(id); }

        /// <summary>A wall's cutaway data.</summary>
        public void SetWallData(int id, Vector4 v) { WallData[id] = v; wallDataDirty.Mark(id); }

        /// <summary>The occluder rows written straight into <see cref="Occ"/>: all of them go up.</summary>
        public void MarkOccDirty() => occDirty.MarkAll();
        /// <summary>One occluder slot's row written into <see cref="Occ"/>: it goes up.</summary>
        public void MarkOccDirty(int slot) => occDirty.Mark(slot * OccWidth, OccWidth);
        /// <summary>Everything written straight into the arrays: the whole buffer goes up (use the setters, which upload only what changed).</summary>
        public void MarkStateDirty() => stateDirty.MarkAll();
        public void MarkWallDirty() => wallDirty.MarkAll();
        public void MarkWallDataDirty() => wallDataDirty.MarkAll();

        /// <summary>
        /// The palette index of each colour an artist's part keeps (<see cref="Generate.KindColors"/>), written into
        /// <see cref="DetailColors"/> when one has been handed out since, or the palette's indices moved. Cheap when nothing
        /// changed: every site calls it before the upload.
        /// </summary>
        public void SyncKindColors(Generate.IColorPalette palette)
        {
            if (ReferenceEquals(palette, kindPalette) && Generate.KindColors.Version == kindVersion && !kindStale) return;
            if (!ReferenceEquals(palette, kindPalette))
            {
                if (kindPalette != null) kindPalette.Invalidated -= KindColorsStale;
                palette.Invalidated += KindColorsStale;
                kindPalette = palette;
            }
            kindStale = false; kindVersion = Generate.KindColors.Version;
            Generate.KindColors.Fill(palette, DetailColors);
            detailsDirty = true;
        }

        void KindColorsStale() => kindStale = true;

        /// <summary>Upload what changed and bind the buffers globally. Call once per frame before rendering.</summary>
        public void Upload()
        {
            using var _t = StoreyTimings.Time("site: table upload");
            Send(state, State, stateDirty);
            Send(occ, Occ, occDirty);
            Send(wall, Wall, wallDirty);
            Send(wallData, WallData, wallDataDirty);
            Send(prms, Params, paramsDirty);
            Send(colors, Colors, colorsDirty);
            if (detailsDirty) { details.SetData(DetailColors); detailsDirty = false; }
            Shader.SetGlobalBuffer(StoreyShaderIds.State, state);
            Shader.SetGlobalBuffer(StoreyShaderIds.Occ, occ);
            Shader.SetGlobalBuffer(StoreyShaderIds.Wall, wall);
            Shader.SetGlobalBuffer(StoreyShaderIds.WallData, wallData);
            Shader.SetGlobalBuffer(StoreyShaderIds.Params, prms);
            Shader.SetGlobalBuffer(StoreyShaderIds.Colors, colors);
            Shader.SetGlobalBuffer(StoreyShaderIds.DetailColors, details);
            // with the buffers, their sizes: the shaders keep every index inside them, a vertex's own ids too (on Metal a read
            // past a buffer's end can fault the GPU, StoreyOcclusion.hlsl)
            Shader.SetGlobalVector(StoreyShaderIds.TableSize, new Vector4(Wall.Length, Params.Length / ParamTexels, Colors.Length / Generate.ColorRows.Uint4s, 0));
        }

        /// <summary>The changed chunks of one buffer, uploaded.</summary>
        void Send(GraphicsBuffer buffer, Array data, Lod.DirtyChunks dirty)
        {
            if (!dirty.Any) return;
            dirty.Take(runs);
            foreach (var (start, count) in runs) buffer.SetData(data, start, start, count);
        }

        public void Dispose()
        {
            if (kindPalette != null) { kindPalette.Invalidated -= KindColorsStale; kindPalette = null; }
            state.Dispose(); occ.Dispose(); wall.Dispose(); wallData.Dispose(); prms.Dispose(); colors.Dispose(); details.Dispose();
        }
    }
}
