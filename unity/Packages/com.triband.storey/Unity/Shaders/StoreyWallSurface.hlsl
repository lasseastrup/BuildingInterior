// Storey: a wall's surface, drawn here rather than modelled (docs/EDITOR.md §6.7): metal cladding, its panels running up the
// wall or along it, from its style (FacadeStyle.wallSurface). The numbers are in the style's colour row (ColorRows.WallEntry),
// and the vertex stage hands them to a wall face's fragments in a pane's place (StoreyVert), as a roof's (StoreyRoof.hlsl):
// a pane never lies on a wall's face. The panels run along the face's own level or up it, from the face's normal and the
// pixel's place, so it needs nothing in the mesh. LOD0 and LOD1; the massing keeps the walls' plain colour, which the
// pattern averages out to, as it fades to in the distance, where a seam is under a pixel or two.
#ifndef STOREY_WALL_SURFACE_INCLUDED
#define STOREY_WALL_SURFACE_INCLUDED

#include "StoreyRoof.hlsl"   // StoreyRoofHash

// Metal cladding across its panels at g, their edges at its whole numbers: a standing seam at each, k of a panel either
// side of the edge, one flank lit and the other in its shade. Its shade, which multiplies the wall's colour and averages 1,
// and how the surface leans across the panels there, per unit of relief
void StoreyCladding(float g, float k, out float shade, out float lean)
{
    float f = frac(g);
    float side = f < 0.5 ? 1.0 : -1.0, seam = 1.0 - smoothstep(0.0, k, min(f, 1.0 - f));
    shade = 1.0 + 0.2 * seam * side;
    lean = side * seam * 1.2;
}

// The walls' surface at a fragment of a wall's face (win: StoreyVert's): a panel's width in win.x metres, its panels up the
// wall (win.y -1: the seams upright, along the face's level) or along it (-2: the seams level, at heights), minus the
// pattern plus half its variation in win.z, its relief in win.w. Each panel a shade of its own; a seam 3 cm either side of
// the panel's edge, less on narrow ones (6% of a panel at most). Faded to the plain wall where a seam is under a pixel or
// two (dpx, dpy: the fragment's place's derivatives, taken before the branch it is called in)
void StoreyWallSurface(float4 win, float3 positionWS, float3 dpx, float3 dpy, inout float3 albedo, inout float3 normalWS)
{
    float3 n = normalWS;
    float l = length(n.xz);
    if (l < 1e-3) return;
    float3 t = win.y < -1.5 ? float3(0.0, 1.0, 0.0) : float3(-n.z / l, 0.0, n.x / l);   // across the panels
    float w = max(win.x, 0.01), k = min(0.06, 0.03 / w);
    float g = dot(positionWS, t) / w;
    float px = 1.0 / (max(abs(dot(dpx, t)), abs(dot(dpy, t))) / w + 1e-6);   // pixels to a panel
    float fade = smoothstep(0.5, 1.5, 2.0 * k * px);                         // a seam's pixels
    if (fade <= 0.0) return;
    float shade, lean;
    StoreyCladding(g, k, shade, lean);
    shade *= 1.0 + (StoreyRoofHash(float2(floor(g), 7.0)) - 0.5) * 2.0 * frac(-win.z);   // the panel's own
    albedo *= lerp(1.0, shade, fade);
    normalWS = normalize(n + t * (lean * win.w * fade));
}

#endif
