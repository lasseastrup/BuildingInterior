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
    /// switch individual buildings. Written on the CPU into arrays, uploaded when dirty, once per frame.
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
            stateDirty = true;
            blocks.Release(start, n);
        }
        public const int MaxBuildings = 8192;
        public const int OccSlots = 16, OccWidth = 64;
        /// <summary>The buildings all loaded sites can have between them: the rest of the rows are the footprints'.</summary>
        public const int Capacity = MaxBuildings - OccSlots;
        /// <summary>The row the footprint of the occluder in <paramref name="slot"/> draws through: its own, so it can fade.</summary>
        public static int FootprintRow(int slot) => MaxBuildings - 1 - slot;
        public const int WallIds = 65536;
        /// <summary>
        /// The parameter (LOD2 massing) and colour-row tables start this big and double when full. They used to be fixed,
        /// and a big layout (the 3,000-building test city) wrote every row past the last into the last one, so many
        /// buildings' massings showed another building's colours and windows.
        /// </summary>
        public const int ParamRowsStart = 1024, ParamTexels = 6;
        public const int ColorRowsStart = 1024;

        /// <summary>Per building: displayed LOD, previous LOD, cross-fade 0..1, occluder slot + 1 (0 = none).</summary>
        public readonly Vector4[] State = new Vector4[MaxBuildings];
        /// <summary>Per occluder slot, OccWidth texels: header (segments, dark, clip height, base height) then storey rows.</summary>
        public readonly Vector4[] Occ = new Vector4[OccSlots * OccWidth];
        /// <summary>How far each wall has slid down, 0 up .. 1 down, by global wall id.</summary>
        public readonly float[] Wall = new float[WallIds];
        /// <summary>Per wall id: the wall's start (x, z) and its normal scaled by 1 + its length, for the cutaway (written once, at upload).</summary>
        public readonly Vector4[] WallData = new Vector4[WallIds];
        /// <summary>LOD2 parameter rows: wall, trim, glass, roof colours, window spec, run data.</summary>
        public Vector4[] Params { get; private set; } = new Vector4[ParamRowsStart * ParamTexels];
        /// <summary>Colour rows: per style, the palette index of each slot and the remap row, 24 16-bit entries in 12 words.</summary>
        public uint[] Colors { get; private set; } = new uint[ColorRowsStart * Generate.ColorRows.Words];
        /// <summary>Palette indices of facade-detail model colours (slots 24 and up). Empty until detail models exist.</summary>
        public readonly uint[] DetailColors = new uint[1];

        readonly GraphicsBuffer state, occ, wall, wallData, details;
        GraphicsBuffer prms, colors;   // these two grow (ParamRowsStart)
        bool stateDirty = true, occDirty = true, wallDirty = true, wallDataDirty = true, paramsDirty = true, colorsDirty = true, detailsDirty = true;

        // free lists, as the prototype keeps them
        readonly Stack<int> freeRow = new Stack<int>(); int nextRow;
        readonly Stack<int> freeColorRow = new Stack<int>(); int nextColorRow;
        readonly Stack<int> freeSlot = new Stack<int>();
        readonly List<(int start, int n)> freeWalls = new List<(int, int)>();

        public BuildingTable()
        {
            for (int i = 0; i < MaxBuildings; i++) State[i] = new Vector4(3, 3, 1, 0);   // culled, culled, faded in, no occluder
            for (int s = OccSlots - 1; s >= 0; s--) freeSlot.Push(s);
            freeWalls.Add((1, WallIds - 1));   // id 0 = none
            state = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxBuildings, 16);
            occ = new GraphicsBuffer(GraphicsBuffer.Target.Structured, OccSlots * OccWidth, 16);
            wall = new GraphicsBuffer(GraphicsBuffer.Target.Structured, WallIds, 4);
            wallData = new GraphicsBuffer(GraphicsBuffer.Target.Structured, WallIds, 16);
            prms = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ParamRowsStart * ParamTexels, 16);
            colors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, ColorRowsStart * Generate.ColorRows.Uint4s, 16);
            details = new GraphicsBuffer(GraphicsBuffer.Target.Structured, DetailColors.Length, 4);
        }

        // ---- allocation --------------------------------------------------------------------

        /// <summary>An occluder slot, or -1 when all are taken.</summary>
        public int AllocOccSlot() => freeSlot.Count > 0 ? freeSlot.Pop() : -1;
        public void ReleaseOccSlot(int slot) { freeSlot.Push(slot); }

        /// <summary>A block of <paramref name="n"/> wall ids (first id returned), or -1 when none is free. Ids are 1-based; 0 means no id.</summary>
        public int AllocWalls(int n)
        {
            if (n <= 0) return -1;
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
            Array.Clear(Wall, start, n); wallDirty = true;
            freeWalls.Add((start, n));
            freeWalls.Sort((a, b) => a.start.CompareTo(b.start));
            for (int i = freeWalls.Count - 2; i >= 0; i--)
                if (freeWalls[i].start + freeWalls[i].n == freeWalls[i + 1].start) { freeWalls[i] = (freeWalls[i].start, freeWalls[i].n + freeWalls[i + 1].n); freeWalls.RemoveAt(i + 1); }
        }

        /// <summary>A parameter row, written from a generator row; rows are freed with <see cref="ReleaseRow"/>.</summary>
        public int WriteRow(Generate.ParamRow row, int colorRow)
        {
            int x = freeRow.Count > 0 ? freeRow.Pop() : nextRow++;
            if ((x + 1) * ParamTexels > Params.Length)
            {
                // full: twice the size, a new buffer of it next upload
                var grown = new Vector4[Params.Length * 2]; Array.Copy(Params, grown, Params.Length); Params = grown;
                prms.Dispose(); prms = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Params.Length, 16);
            }
            var t = row.GpuTexels(colorRow); int o = x * ParamTexels;
            for (int i = 0; i < ParamTexels; i++) Params[o + i] = new Vector4((float)t[i * 4], (float)t[i * 4 + 1], (float)t[i * 4 + 2], (float)t[i * 4 + 3]);
            paramsDirty = true;
            return x;
        }
        public void ReleaseRow(int row) => freeRow.Push(row);

        /// <summary>A colour row, filled with <see cref="WriteColorRow"/>; freed with <see cref="ReleaseColorRow"/>.</summary>
        public int AllocColorRow()
        {
            int x = freeColorRow.Count > 0 ? freeColorRow.Pop() : nextColorRow++;
            if ((x + 1) * Generate.ColorRows.Words > Colors.Length)
            {
                var grown = new uint[Colors.Length * 2]; Array.Copy(Colors, grown, Colors.Length); Colors = grown;
                colors.Dispose(); colors = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Colors.Length * 4 / 16, 16);
                colorsDirty = true;
            }
            return x;
        }
        public void ReleaseColorRow(int row) => freeColorRow.Push(row);

        /// <summary>A style's palette indices (in <see cref="Generate.ColorSlot"/> order) and its remap's atlas row (0 = none).</summary>
        public void WriteColorRow(int row, int[] paletteIndices, int remapRow)
        {
            Generate.ColorRows.Pack(paletteIndices, remapRow, Colors, row * Generate.ColorRows.Words);
            colorsDirty = true;
        }

        // ---- writes ---------------------------------------------------------------------------

        public void SetLod(int idx, int displayed, int from, float fade)
        {
            var s = State[idx]; State[idx] = new Vector4(displayed, from, fade, s.w); stateDirty = true;
        }
        public void SetOccluder(int idx, int slotOrMinusOne)
        {
            var s = State[idx]; State[idx] = new Vector4(s.x, s.y, s.z, slotOrMinusOne + 1); stateDirty = true;
        }
        public void MarkOccDirty() => occDirty = true;
        public void MarkStateDirty() => stateDirty = true;
        public void MarkWallDirty() => wallDirty = true;
        public void MarkWallDataDirty() => wallDataDirty = true;

        /// <summary>Upload what changed and bind the buffers globally. Call once per frame before rendering.</summary>
        public void Upload()
        {
            if (stateDirty) { state.SetData(State); stateDirty = false; }
            if (occDirty) { occ.SetData(Occ); occDirty = false; }
            if (wallDirty) { wall.SetData(Wall); wallDirty = false; }
            if (wallDataDirty) { wallData.SetData(WallData); wallDataDirty = false; }
            if (paramsDirty) { prms.SetData(Params); paramsDirty = false; }
            if (colorsDirty) { colors.SetData(Colors); colorsDirty = false; }
            if (detailsDirty) { details.SetData(DetailColors); detailsDirty = false; }
            Shader.SetGlobalBuffer(StoreyShaderIds.State, state);
            Shader.SetGlobalBuffer(StoreyShaderIds.Occ, occ);
            Shader.SetGlobalBuffer(StoreyShaderIds.Wall, wall);
            Shader.SetGlobalBuffer(StoreyShaderIds.WallData, wallData);
            Shader.SetGlobalBuffer(StoreyShaderIds.Params, prms);
            Shader.SetGlobalBuffer(StoreyShaderIds.Colors, colors);
            Shader.SetGlobalBuffer(StoreyShaderIds.DetailColors, details);
        }

        public void Dispose() { state.Dispose(); occ.Dispose(); wall.Dispose(); wallData.Dispose(); prms.Dispose(); colors.Dispose(); details.Dispose(); }
    }
}
