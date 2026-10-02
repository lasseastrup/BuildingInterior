// Storey's whole view of Color Pipeline (docs/COLOURS.md §3.5). Colours come from the palette atlas; Storey's
// colour rows only say which palette entry each slot of a style uses.
//
// The atlas globals are declared as in com.triband.colorpipeline 2.1.11's Shaders/ColorPalette.hlsl instead of
// including that file, so Storey's shaders compile without the package (the built-in hex palette binds the same
// names) and Color Pipeline 3.0's two-step lookup is a change to StoreyAtlas alone. Needs Core.hlsl first.
#ifndef STOREY_PALETTE_INCLUDED
#define STOREY_PALETTE_INCLUDED

// A shader that also includes Color Pipeline's ColorPalette.hlsl (it has no include guard) includes it first and
// defines STOREY_ATLAS_DECLARED, so the two globals are declared once.
#ifndef STOREY_ATLAS_DECLARED
TEXTURE2D(_GlobalColorPaletteTex);          // ColorMappingManager's atlas (512 x 1024 RGBAHalf, linear), or Storey's hex palette
uint _ColorAtlasWidth;                      // 512 in 2.1.11
#endif
StructuredBuffer<uint4> _StoreyColors;      // per colour row, 3 x uint4: 24 x uint16 (20 style slots, spare, remap row)
StructuredBuffer<uint>  _StoreyDetailColors; // per facade-detail colour (slot - 24): palette index

#define STOREY_REMAP_ENTRY 23
#define STOREY_ROW_SLOTS 24

// Color Pipeline 2.1.11's SampleColorPalette: flat index = remap row * width + palette index
float4 StoreyAtlas(uint i)
{
    return LOAD_TEXTURE2D(_GlobalColorPaletteTex, int2(i % _ColorAtlasWidth, i / _ColorAtlasWidth));
}

// Entry e of a colour row: the low (even e) or high half of word e / 2 (ColorRows.Pack on the CPU)
uint StoreyRowEntry(uint row, uint e)
{
    uint4 r = _StoreyColors[row * 3 + (e >> 3)];
    uint q = (e >> 1) & 3;
    uint w = q == 0 ? r.x : (q == 1 ? r.y : (q == 2 ? r.z : r.w));
    return (e & 1) ? (w >> 16) : (w & 0xFFFF);
}

float3 StoreyPaletteColor(uint row, uint slot, float tone)
{
    uint index = slot < STOREY_ROW_SLOTS ? StoreyRowEntry(row, slot) : _StoreyDetailColors[slot - STOREY_ROW_SLOTS];
    return StoreyAtlas(StoreyRowEntry(row, STOREY_REMAP_ENTRY) * _ColorAtlasWidth + index).rgb * tone;
}

// The LOD0/LOD1 vertex colour attribute (UNorm8 x 4): row low byte, row high byte, slot, shade x 128
float3 StoreyVertexColor(float4 c)
{
    uint4 e = (uint4)round(c * 255.0);
    return StoreyPaletteColor(e.x | (e.y << 8), e.z, e.w / 128.0);
}

#endif
