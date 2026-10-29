// Storey: a pitched roof's surface, drawn here rather than modelled (docs/EDITOR.md §6.2): clay tiles, slates, shingles or
// a metal roof's standing seams, from its style (FacadeStyle.roofSurface). The numbers are in the style's colour row
// (ColorRows.RoofEntry), and the vertex stage hands them to a roof face's fragments in a pane's place (StoreyVert): a pane
// never lies on a roof. The pattern runs along each face's own eave and up its slope, from the face's normal and the
// pixel's place, so it needs nothing in the mesh. LOD0 and LOD1; the massing's roof keeps the roof's plain colour, which
// every pattern averages out to, as it fades to in the distance, where a tile is a few pixels.
#ifndef STOREY_ROOF_INCLUDED
#define STOREY_ROOF_INCLUDED

// PI: the render pipeline's (Common.hlsl, through Core.hlsl)

// a tile's own number from its place on the roof, 0 to 1
float StoreyRoofHash(float2 c)
{
    float3 p = frac(float3(c.x, c.y, c.x) * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

// One pattern at g (tiles along the eave, rows up the slope): its shade, which multiplies the roof's colour and averages
// 1, and how the surface leans there, along the eave and up the slope, per unit of relief
void StoreyRoofPattern(int pattern, float2 g, float variation, out float shade, out float2 lean)
{
    // slates and shingles are laid bonded, each row half a tile along from the one under it
    float row = floor(g.y);
    float2 h = (pattern == 2 || pattern == 3) ? float2(g.x + 0.5 * (row - 2.0 * floor(row * 0.5)), g.y) : g;
    float2 c = floor(h), f = frac(h);
    float own = (StoreyRoofHash(pattern == 4 ? float2(c.x, 0.0) : c) - 0.5) * variation;   // each tile a shade of its own; a seamed panel runs the whole slope
    if (pattern == 1)
    {
        // clay tiles: barrels up the slope, dark in the troughs between them and under the foot of the row above
        float crest = sin(f.x * PI);
        float trough = (1.0 - crest) * (1.0 - crest), under = smoothstep(0.82, 1.0, f.y), lip = 1.0 - smoothstep(0.0, 0.1, f.y);
        shade = 1.095 * (1.0 - 0.25 * trough) * (1.0 - 0.4 * under) * (1.0 + 0.08 * lip);
        lean = float2(-cos(f.x * PI) * 0.9, 0.15);
    }
    else if (pattern == 2)
    {
        // slates: flat, a fine joint between two and the shadow of the next row's foot
        float joint = 1.0 - smoothstep(0.0, 0.03, min(f.x, 1.0 - f.x)), foot = smoothstep(0.88, 1.0, f.y);
        shade = 1.044 * (1.0 - 0.5 * joint) * (1.0 - 0.45 * foot);
        lean = float2((StoreyRoofHash(c + 17.0) - 0.5) * 0.1, 0.12);
    }
    else if (pattern == 3)
    {
        // shingles: each foot a round, the shingle under it shaded just below that edge and its rim catching the light
        float x = 2.0 * f.x - 1.0, arc = 0.35 * (1.0 - sqrt(saturate(1.0 - x * x)));
        float below = f.y < arc ? 1.0 - smoothstep(0.0, 0.14, arc - f.y) : 0.0;
        float rim = f.y >= arc ? 1.0 - smoothstep(0.0, 0.05, f.y - arc) : 0.0;
        shade = 1.015 * (1.0 - 0.45 * below) * (1.0 + 0.12 * rim);
        lean = float2(x * 0.25, 0.15);
    }
    else
    {
        // standing seams: a rib up the slope at each joint, one flank lit and the other in its shade
        float side = f.x < 0.5 ? 1.0 : -1.0, seam = 1.0 - smoothstep(0.0, 0.07, min(f.x, 1.0 - f.x));
        shade = 1.0 + 0.2 * seam * side;
        lean = float2(side * seam * 1.2, 0.0);
    }
    shade *= 1.0 + own;
}

// The roof's surface at a fragment of a roof face (win: StoreyVert's), on its colour and normal: the pattern along the face's
// eave and up its slope, a tile win.xy metres, faded to the plain roof where a tile is under a few pixels (dpx, dpy: the
// fragment's place's derivatives, taken before the branch it is called in)
void StoreyRoofSurface(float4 win, float3 positionWS, float3 dpx, float3 dpy, inout float3 albedo, inout float3 normalWS)
{
    float3 n = normalWS;
    float l = length(n.xz);
    if (l < 1e-3) return;
    float2 down = n.xz / l, along = float2(-down.y, down.x);   // down the slope and along the eave, in plan
    float cs = max(n.y, 0.05);                                  // a metre down the slope covers cs of it in plan
    float2 size = max(win.xy, 0.01);
    float2 g = float2(dot(positionWS.xz, along), -dot(positionWS.xz, down) / cs) / size;
    float2 gx = float2(dot(dpx.xz, along), -dot(dpx.xz, down) / cs) / size, gy = float2(dot(dpy.xz, along), -dot(dpy.xz, down) / cs) / size;
    float px = 1.0 / max(max(abs(gx.x), abs(gy.x)), max(abs(gx.y), abs(gy.y)) + 1e-6);   // pixels to a tile
    float fade = smoothstep(1.5, 6.0, px);
    if (fade <= 0.0) return;
    float p = -win.z;
    int pattern = (int)floor(p);
    float shade; float2 lean;
    StoreyRoofPattern(pattern, g, 2.0 * frac(p), shade, lean);
    float3 e3 = float3(along.x, 0.0, along.y), up = normalize(cross(e3, n));
    albedo *= lerp(1.0, shade, fade);
    normalWS = normalize(n + (e3 * lean.x + up * lean.y) * (win.w * fade));
}

#endif
