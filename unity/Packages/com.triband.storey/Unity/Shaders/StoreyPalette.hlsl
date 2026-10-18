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

// The atlas's width and size, found once for several lookups (StoreyStyleColors). A width that is a power of two (Color
// Pipeline's 512, Storey's own palette's) is split by a shift and a mask: an integer divide or modulo by a value the
// compiler can't see is hundreds of clocks on Apple's GPUs (docs/PERFORMANCE.md §7.4). The width kept off zero and the
// texel inside the texture: until Color Pipeline binds its atlas, both are whatever Unity has bound in its place
struct StoreyAtlasSize { uint w, shift, tw, th; bool pow2; };

StoreyAtlasSize StoreyAtlasSizeNow()
{
    StoreyAtlasSize a;
    a.w = max(_ColorAtlasWidth, 1u);
    a.pow2 = (a.w & (a.w - 1u)) == 0u;
    a.shift = firstbithigh(a.w);
    _GlobalColorPaletteTex.GetDimensions(a.tw, a.th);
    return a;
}

// Color Pipeline 2.1.11's SampleColorPalette: flat index = remap row * width + palette index
float4 StoreyAtlasAt(uint i, StoreyAtlasSize a)
{
    uint x, y;
    [branch] if (a.pow2) { x = i & (a.w - 1u); y = i >> a.shift; }   // the same for every pixel of a draw: a real branch
    else { x = i % a.w; y = i / a.w; }
    return LOAD_TEXTURE2D(_GlobalColorPaletteTex, int2(min(x, max(a.tw, 1u) - 1u), min(y, max(a.th, 1u) - 1u)));
}

float4 StoreyAtlas(uint i) { return StoreyAtlasAt(i, StoreyAtlasSizeNow()); }

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

// A style's four facade colours at once, shaded, for the massing's every pixel (StoreyFacade.hlsl): wall, trim, roof and
// glass are entries 0, 1, 4 and 6 of the colour row, all four in its first uint4, and the remap row (entry 23) is the
// high half of its third uint4's w (ColorRows.Pack). Two buffer loads where four StoreyPaletteColor lookups made eight,
// and four detail-colour loads that a style's slot never uses, besides finding the atlas's size once, not four times
// (docs/PERFORMANCE.md §7.4). A row past the table reads its last row.
void StoreyStyleColors(uint row, float4 tones, out float3 wall, out float3 trim, out float3 roof, out float3 glass)
{
    row = min(row, (uint)max((int)_StoreyTableSize.z - 1, 0));
    uint4 slots = _StoreyColors[row * 3];
    uint4 last = _StoreyColors[row * 3 + 2];
    StoreyAtlasSize a = StoreyAtlasSizeNow();
    uint base = (last.w >> 16) * _ColorAtlasWidth;
    wall = StoreyAtlasAt(base + (slots.x & 0xFFFF), a).rgb * tones.x;
    trim = StoreyAtlasAt(base + (slots.x >> 16), a).rgb * tones.y;
    roof = StoreyAtlasAt(base + (slots.z & 0xFFFF), a).rgb * tones.z;
    glass = StoreyAtlasAt(base + (slots.w & 0xFFFF), a).rgb * tones.w;
}

// A style's wall colour (entry 0 of its row) at full shade, for LOD1's flat panes' reveals (StoreyFragment): the row's
// first uint4 and its remap row, two buffer loads and the atlas's, where StoreyPaletteColor makes four. A row past the
// table reads its last row.
float3 StoreyWallColor(uint row)
{
    row = min(row, (uint)max((int)_StoreyTableSize.z - 1, 0));
    uint wall = _StoreyColors[row * 3].x & 0xFFFF, remap = _StoreyColors[row * 3 + 2].w >> 16;
    return StoreyAtlasAt(remap * _ColorAtlasWidth + wall, StoreyAtlasSizeNow()).rgb;
}

// The LOD0/LOD1 vertex colour attribute (UNorm8 x 4): row low byte, row high byte, slot, shade x 128
float3 StoreyVertexColor(float4 c)
{
    uint4 e = (uint4)round(c * 255.0);
    return StoreyPaletteColor(e.x | (e.y << 8), e.z, e.w / 128.0);
}

#endif
