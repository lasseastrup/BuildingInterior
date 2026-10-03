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
#ifndef STOREY_WINDOW_INCLUDED
#define STOREY_WINDOW_INCLUDED

TEXTURE2D(_StoreyWindowRooms);  SAMPLER(sampler_StoreyWindowRooms);
TEXTURE2D(_StoreyWindowGlare);  SAMPLER(sampler_StoreyWindowGlare);
float  _StoreyWindowsOn;        // 0: the plain dark glass of before
float4 _StoreyWindowAtlas;      // xy: the atlas's columns and rows; z: room depth (0..1); w: glare strength
float4 _StoreyWindowTint;       // by day: the room × the main light's colour × this
float4 _StoreyWindowLight;      // at night: the room × a lamp colour between these two
float4 _StoreyWindowLight2;
float4 _StoreyWindowNight;      // x: night blend (0 day .. 1 night); y: the share of windows that light at full night

#include "StoreyWindowId.hlsl"

// The room atlas's UV for a point (uv, 0 to 1) on a pane, seen along viewTS (towards the camera, in the pane's
// tangent space: x across, y up, z out of the wall). The same inputs as the project's WindowInteriorParallax_float
// node, so that node can stand in here. A single-texture interior: the room is a box behind the pane whose back wall
// is depth × the window's size, drawn in one-point perspective in each atlas cell.
float2 StoreyWindowParallax(float2 uv, float3 viewTS, float depth, float2 roomIndex, float2 grid, float flipX, float flipY)
{
    if (flipX > 0.5) { uv.x = 1.0 - uv.x; viewTS.x = -viewTS.x; }
    if (flipY > 0.5) { uv.y = 1.0 - uv.y; viewTS.y = -viewTS.y; }
    float farFrac = clamp(depth, 0.05, 0.95);
    float depthScale = 1.0 / (1.0 - farFrac) - 1.0;
    float3 d = float3(-viewTS.x, -viewTS.y, max(viewTS.z, 1e-4) * depthScale);   // into the room
    float3 pos = float3(uv * 2.0 - 1.0, -1.0);
    float3 id = 1.0 / d;
    float3 k = abs(id) - pos * id;
    pos += min(min(k.x, k.y), k.z) * d;
    float interp = pos.z * 0.5 + 0.5;
    float realZ = saturate(interp) / depthScale + 1.0;
    interp = (1.0 - 1.0 / realZ) * (depthScale + 1.0);
    float2 iuv = saturate(pos.xy * lerp(1.0, farFrac, interp) * 0.5 + 0.5);
    return (roomIndex + iuv) / grid;
}

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
    float2 auv = StoreyWindowParallax(uv, viewTS, _StoreyWindowAtlas.z, room, grid, step(0.5, r3), 0.0);
    // no mip chain across a cell's edge: sample at the level the pane itself would take
    float3 interior = SAMPLE_TEXTURE2D_GRAD(_StoreyWindowRooms, sampler_StoreyWindowRooms, auv, uvDx / grid, uvDy / grid).rgb;

    // lit once the night blend passes the window's number (the shader graph's comparison), lamp colour by it too
    bool lit = r < _StoreyWindowNight.x * _StoreyWindowNight.y;
    float3 light = lit ? lerp(_StoreyWindowLight2.rgb, _StoreyWindowLight.rgb, r) : GetMainLight().color * _StoreyWindowTint.rgb;
    float3 glare = SAMPLE_TEXTURE2D(_StoreyWindowGlare, sampler_StoreyWindowGlare, (float2(dot(posWS, T), posWS.y) + viewTS.xy) * 0.5).rgb;
    return glare * _StoreyWindowAtlas.w + interior * light;
}

#endif
