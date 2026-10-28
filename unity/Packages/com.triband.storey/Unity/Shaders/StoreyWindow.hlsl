// Windows (docs/EDITOR.md §6.8): the panes that aren't see-through (shell and filled storeys in LOD0, every pane of
// LOD1, LOD2's painted windows) show a room behind them, after the project's Window shader graph: an interior
// mapped from a room atlas (one room per window, picked and mirrored at random), lit by the main light by day; at
// night, as the night blend rises, more of them light up in one of two lamp colours; a faint glare texture over them.
//
// Every value is global (StoreyWindows sets them), so the Storey materials keep their SRP Batcher-compatible
// buffers and the effect is off (_StoreyWindowsOn = 0) until a scene turns it on.
//
// A window's number comes from its centre on the wall (Facade.WindowId, baked into LOD0's and LOD1's panes; worked
// out the same way for LOD2's), so a window keeps its room and its light across an LOD switch.
//
// Included by StoreyFragment.hlsl, after a project shader's own lighting, which can put its own nodes in first: define
// STOREY_WINDOW_PARALLAX as a function with StoreyWindowParallax's inputs that gives the atlas UV (the project's
// WindowInteriorParallax node, say), and STOREY_WINDOW_NIGHT_BLEND as the project's night blend (0 day .. 1 night) in
// place of StoreyWindows' (the Color Palette Lit sample does both).
#ifndef STOREY_WINDOW_INCLUDED
#define STOREY_WINDOW_INCLUDED

TEXTURE2D(_StoreyWindowRooms);  SAMPLER(sampler_StoreyWindowRooms);
TEXTURE2D(_StoreyWindowGlare);  SAMPLER(sampler_StoreyWindowGlare);
float  _StoreyWindowsOn;        // 0: the plain dark glass of before
float4 _StoreyWindowAtlas;      // xy: the atlas's columns and rows; z: room depth (1: as deep as the window is wide); w: glare strength
float4 _StoreyWindowTint;       // by day: the room × the main light's colour × this
float4 _StoreyWindowLight;      // at night: the room × a lamp colour between these two
float4 _StoreyWindowLight2;
float4 _StoreyWindowNight;      // x: night blend (0 day .. 1 night); y: the share of windows that light at full night
float4 _StoreyGlassFill;        // LOD0's see-through panes: x clear (see-through) within this distance, y painted (LOD1's look) from this one; y 0: always clear

#include "StoreyWindowId.hlsl"

// LOD1's flat walls (Lod1.cs): a plain window's pane stands 3 cm proud of the wall (Lod1.PaneProud), where LOD0 cuts the
// window 16.5 cm into it (Dim.T_EXT × 0.55) with the wall's colour round its pane. Seen from a side, from above or below,
// LOD0 shows that reveal beside, under or over its pane, a share of the window that grows with the angle. The recess's
// depth from LOD1's pane to LOD0's, metres:
#define STOREY_PANE_RECESS 0.195
// LOD0's centre bar in a wide window (Facade.CentreBar): 3 cm either side of its middle (Facade.BarHalf), its front 15 cm
// behind LOD1's pane (Dim.T_EXT × 0.6 from the wall's inside), and on past LOD0's pane
#define STOREY_BAR_HALF 0.03
#define STOREY_BAR_FRONT 0.15

// What the line of sight at a point of a flat pane (uv: across and up it, 0 to 1) meets in the recess LOD0 cuts: LOD0's
// pane, uv then where on it, and 0 returned; or a reveal or (bar: the window has one) the centre bar, whose outward
// normal (world space) is returned, trim saying which. The window's uv per metre comes from the screen derivatives of uv
// and of the point (posDx, posDy): on a flat pane uv is affine in it.
float3 StoreyPaneRecess(inout float2 uv, float2 uvDx, float2 uvDy, float3 posDx, float3 posDy, float3 posWS, float3 N, bool bar, out bool trim)
{
    trim = false;
    float3 T = normalize(float3(N.z, 0.0, -N.x) + float3(1e-6, 0.0, 0.0));   // across: cross(up, N), as the pane's u runs
    float2 perM = float2(abs(uvDx.x) + abs(uvDy.x), abs(uvDx.y) + abs(uvDy.y))
                / max(float2(abs(dot(posDx, T)) + abs(dot(posDy, T)), abs(posDx.y) + abs(posDy.y)), 1e-6);
    float3 V = normalize(_WorldSpaceCameraPos - posWS);
    // going in to LOD0's pane, the line of sight moves this far across and up the window
    float2 go = -float2(dot(V, T), V.y) / max(dot(V, N), 1e-3) * (STOREY_PANE_RECESS * perM);
    float2 hit = uv + go;
    if (all(hit >= 0.0) && all(hit <= 1.0))
    {
        // the centre bar, met at its front, or at its side towards the eye between its front and the pane
        float front = uv.x + go.x * (STOREY_BAR_FRONT / STOREY_PANE_RECESS), hw = STOREY_BAR_HALF * perM.x;
        if (bar && max(front, hit.x) > 0.5 - hw && min(front, hit.x) < 0.5 + hw)
        {
            trim = true;
            if (abs(front - 0.5) < hw) return N;
            return -sign(go.x) * T;
        }
        uv = hit; return float3(0.0, 0.0, 0.0);
    }
    // it leaves the window's outline on the way: the side it reaches first (in shares of the way in) faces back at it
    float2 at = lerp(uv, 1.0 - uv, step(0.0, go)) / max(abs(go), 1e-6);
    if (at.x < at.y) return -sign(go.x) * T;
    return float3(0.0, -sign(go.y), 0.0);
}

// The room atlas's UV for a point (uv, 0 to 1) on a pane, seen along viewTS (towards the camera, in the pane's
// tangent space: x across, y up, z out of the wall). The project's WindowInteriorParallax_float node as it is now (its
// float version), with the same inputs, so that node can stand in here (STOREY_WINDOW_PARALLAX). The room is a box
// depth × the window's width deep behind the pane; where the line of sight first meets its back wall or a side, that
// point is drawn in one-point perspective into the atlas cell, laid out in thirds (ceiling, floor and side walls round
// the back wall in the middle third).
float2 StoreyWindowParallax(float2 uv, float3 viewTS, float depth, float2 roomIndex, float2 grid, float flipX, float flipY)
{
    uv = frac(uv);
    uv.x = lerp(uv.x, 1.0 - uv.x, flipX);
    uv.y = lerp(uv.y, 1.0 - uv.y, flipY);
    float vz = max(viewTS.z, 0.0001);
    float tBack = depth / vz;
    float ax = abs(viewTS.x), ay = abs(viewTS.y);
    float tx = ax < 0.0001 ? 1e9 : (viewTS.x > 0.0 ? uv.x : 1.0 - uv.x) / ax;
    float ty = ay < 0.0001 ? 1e9 : (viewTS.y > 0.0 ? uv.y : 1.0 - uv.y) / ay;
    float t = min(tBack, min(tx, ty));
    float2 hitXY = uv - viewTS.xy * t;
    float hitZ = vz * t;
    const float wallFraction = 0.333;
    float hz = max(hitZ + depth * wallFraction, 0.001);
    float scale = depth * (1.0 - 2.0 * wallFraction);
    float2 roomUV = saturate(float2(0.5, 0.5) + (hitXY - float2(0.5, 0.5)) / hz * scale);
    return (roomUV + roomIndex) / grid;
}

#ifndef STOREY_WINDOW_PARALLAX
#define STOREY_WINDOW_PARALLAX StoreyWindowParallax
#endif
#ifndef STOREY_WINDOW_NIGHT_BLEND
#define STOREY_WINDOW_NIGHT_BLEND _StoreyWindowNight.x
#endif

// A pane's colour at a point: uv on the pane (across along cross(up, N), up), the window's number (StoreyWindowId),
// uv's screen derivatives (taken outside any branch: StoreyFragment), the point and the wall's outward normal in
// world space.
float3 StoreyWindowPane(float2 uv, float2 uvDx, float2 uvDy, float id, float3 posWS, float3 N)
{
    float3 up = float3(0, 1, 0);
    float3 T = cross(up, N); T = dot(T, T) > 1e-6 ? normalize(T) : float3(1, 0, 0);
    // two numbers from the window's one: when it lights (and its lamp's colour), and which room it shows
    float n = floor(id + 0.5);
    float r = (n + 0.5) / 256.0;
    float r2 = frac((n + 0.5) * 0.6180340);
    float r3 = frac((n + 0.5) * 0.7548777);

    float3 V = normalize(_WorldSpaceCameraPos - posWS);
    float3 viewTS = float3(dot(V, T), dot(V, up), dot(V, N));
    float2 grid = max(floor(_StoreyWindowAtlas.xy + 0.5), 1.0);
    float pick = min(floor(r2 * grid.x * grid.y), grid.x * grid.y - 1.0);
    float2 room = float2(fmod(pick, grid.x), floor(pick / grid.x));
    float2 auv = STOREY_WINDOW_PARALLAX(uv, viewTS, _StoreyWindowAtlas.z, room, grid, step(0.5, r3), 0.0);
    // no mip chain across a cell's edge: sample at the level the pane itself would take
    float3 interior = SAMPLE_TEXTURE2D_GRAD(_StoreyWindowRooms, sampler_StoreyWindowRooms, auv, uvDx / grid, uvDy / grid).rgb;

    // lit once the night blend passes the window's number (the shader graph's comparison), lamp colour by it too
    bool lit = r < STOREY_WINDOW_NIGHT_BLEND * _StoreyWindowNight.y;
    float3 light = lit ? lerp(_StoreyWindowLight2.rgb, _StoreyWindowLight.rgb, r) : GetMainLight().color * _StoreyWindowTint.rgb;
    float3 glare = SAMPLE_TEXTURE2D(_StoreyWindowGlare, sampler_StoreyWindowGlare, (float2(dot(posWS, T), posWS.y) + viewTS.xy) * 0.5).rgb;
    return glare * _StoreyWindowAtlas.w + interior * light;
}

#endif
