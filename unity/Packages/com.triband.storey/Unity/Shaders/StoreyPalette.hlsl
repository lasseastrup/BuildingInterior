// Storey's whole view of Color Pipeline (docs/COLOURS.md §3.5). Colours come from the palette atlas; Storey's
// colour rows only say which palette entry each slot of a style uses.
//
// The atlas globals are declared as in com.triband.colorpipeline 2.1.11's Shaders/ColorPalette.hlsl instead of
// including that file, so Storey's shaders compile without the package (the built-in hex palette binds the same
// names) and Color Pipeline 3.0's two-step lookup is a change to StoreyAtlas alone. Needs Core.hlsl first.
//
// Every read here stays inside its buffer or texture, whatever the slot, as StoreyOcclusion.hlsl's do: HLSL reads
// both sides of a ?: whichever it uses, and on Metal a read outside a buffer can fault the GPU.
#ifndef STOREY_PALETTE_INCLUDED
#define STOREY_PALETTE_INCLUDED

// A shader that also includes Color Pipeline's ColorPalette.hlsl (it has no include guard) includes it first and
// defines STOREY_ATLAS_DECLARED, so the two globals are declared once.
#ifndef STOREY_ATLAS_DECLARED
TEXTURE2D(_GlobalColorPaletteTex);          // ColorMappingManager's atlas (512 x 1024 RGBAHalf, linear), or Storey's hex palette
uint _ColorAtlasWidth;                      // 512 in 2.1.11
#endif
StructuredBuffer<uint4> _StoreyColors;      // per colour row, 3 x uint4: 24 x uint16 (20 style slots, spare, remap row)
StructuredBuffer<uint>  _StoreyDetailColors; // per facade-detail colour (slot - 24): palette index; one for every slot a byte names

#define STOREY_REMAP_ENTRY 23
#define STOREY_ROW_SLOTS 24
#define STOREY_DETAIL_COLORS 232   // _StoreyDetailColors' entries: one for each slot from 24 to 255 (BuildingTable.DetailColors)

// The tables that grow, their sizes now (BuildingTable.Upload): x wall ids, y parameter rows, z colour rows
#ifndef STOREY_TABLE_SIZE_DECLARED
#define STOREY_TABLE_SIZE_DECLARED
float4 _StoreyTableSize;
#endif

// Color Pipeline 2.1.11's SampleColorPalette: flat index = remap row * width + palette index. The width kept off zero and
// the texel inside the texture: until Color Pipeline binds its atlas, both are whatever Unity has bound in its place
float4 StoreyAtlas(uint i)
{
    uint w = max(_ColorAtlasWidth, 1u), tw, th;
    _GlobalColorPaletteTex.GetDimensions(tw, th);
    return LOAD_TEXTURE2D(_GlobalColorPaletteTex, int2(min(i % w, max(tw, 1u) - 1u), min(i / w, max(th, 1u) - 1u)));
}

// Entry e of a colour row: the low (even e) or high half of word e / 2 (ColorRows.Pack on the CPU). An e past the row
// reads its last entry, not the next row's (or past the buffer's end); a row past the table (from a vertex), its last row
uint StoreyRowEntry(uint row, uint e)
{
    e = min(e, STOREY_ROW_SLOTS - 1u);
    row = min(row, (uint)max((int)_StoreyTableSize.z - 1, 0));
    uint4 r = _StoreyColors[row * 3 + (e >> 3)];
    uint q = (e >> 1) & 3;
    uint w = q == 0 ? r.x : (q == 1 ? r.y : (q == 2 ? r.z : r.w));
    return (e & 1) ? (w >> 16) : (w & 0xFFFF);
}

float3 StoreyPaletteColor(uint row, uint slot, float tone)
{
    // both sides are read, whichever is used: a style's slot reads detail colour 0, not slot - 24 wrapped round
    uint detail = _StoreyDetailColors[min(slot >= STOREY_ROW_SLOTS ? slot - STOREY_ROW_SLOTS : 0u, STOREY_DETAIL_COLORS - 1u)];
    uint index = slot < STOREY_ROW_SLOTS ? StoreyRowEntry(row, slot) : detail;
    return StoreyAtlas(StoreyRowEntry(row, STOREY_REMAP_ENTRY) * _ColorAtlasWidth + index).rgb * tone;
}

// The LOD0/LOD1 vertex colour attribute (UNorm8 x 4): row low byte, row high byte, slot, shade x 128
float3 StoreyVertexColor(float4 c)
{
    uint4 e = (uint4)round(c * 255.0);
    return StoreyPaletteColor(e.x | (e.y << 8), e.z, e.w / 128.0);
}

#endif
