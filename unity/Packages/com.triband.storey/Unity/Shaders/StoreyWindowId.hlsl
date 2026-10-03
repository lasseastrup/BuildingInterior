// A window's number (docs/EDITOR.md §6.8), on its own so that every pass that includes StoreyFacade.hlsl (the
// massing shader's depth and shadow passes too) has it, whether or not it includes StoreyWindow.hlsl.
#ifndef STOREY_WINDOW_ID_INCLUDED
#define STOREY_WINDOW_ID_INCLUDED

// A window's number, 0 to 255, from its centre on the wall's face in site space: Facade.WindowId's integer hash, so
// LOD2 works out the number the geometry LODs carry. The 25 cm cells' edges sit at odd 12.5 cm, never on the
// centimetre grid the centres are on, so float rounding never tips a centre into the next cell.
float StoreyWindowId(float3 c)
{
    uint3 i = (uint3)(int3)floor(c * 4.0 + 0.5);
    uint h = (i.x * 73856093u) ^ (i.y * 19349663u) ^ (i.z * 83492791u);
    h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
    return (float)(h & 255u);
}

#endif
