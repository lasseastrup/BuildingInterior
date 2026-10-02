// LOD2 massing: windows are drawn procedurally from the same parameters the geometry LODs use, so the
// LOD1 -> LOD2 switch keeps every window in place (SPEC §6.1). A port of the prototype's facadeColor().
//
// Each facade quad covers a run of storeys with one height and window type. fac = (u along the wall, height
// above the run's base, window span start, window span end); fac2 = (bay width, kind); slot = the run's row in
// _StoreyParams: six texels ((colour row, wall, trim and glass shades), (roof shade), (band), one unused, window spec,
// run data). Wall, trim, glass and roof come from the palette through the colour row (StoreyPalette.hlsl).
#ifndef STOREY_FACADE_INCLUDED
#define STOREY_FACADE_INCLUDED

StructuredBuffer<float4> _StoreyParams;
#define STOREY_PARAM_TEXELS 6

float4 StoreyParam(float slot, int i) { return _StoreyParams[(int)(slot + 0.5) * STOREY_PARAM_TEXELS + i]; }

float3 StoreyFacadeColor(float4 fac, float2 fac2, float slot)
{
    float fu = fwidth(fac.x), fv = fwidth(fac.y);
    float4 c0 = StoreyParam(slot, 0); uint row = (uint)(c0.x + 0.5);
    float3 wall = StoreyPaletteColor(row, 0, c0.y), trim = StoreyPaletteColor(row, 1, c0.z);
    float3 glass = StoreyPaletteColor(row, 6, c0.w), roof = StoreyPaletteColor(row, 4, StoreyParam(slot, 1).x);   // ColorSlot Wall, Trim, Glass, Roof
    float kind = fac2.y;   // 1 facade, 2 roof, 3 trim, 4 plain wall
    if (kind > 1.5) return kind < 2.5 ? roof : (kind < 3.5 ? trim : wall);
    float4 sp = StoreyParam(slot, 4), run = StoreyParam(slot, 5);   // sp.x: gap for full-width styles (<0: fixed width), y: width, z: sill, w: head
    float u = fac.x, v = fac.y, uS = fac.z, uE = fac.w, bay = fac2.x, h = run.x;
    if (v >= run.z) return (run.w > 1.5 || v > run.z + 1.0) ? trim : wall;   // parapet (or curb) above the top run
    float fl = floor(v / h), lv = v - fl * h;
    float3 c = wall;
    if (bay > 0.0 && sp.w > sp.z + 0.4)
    {
        float ww = sp.x >= 0.0 ? bay - sp.x : min(sp.y, bay - 0.5);
        if (ww >= 0.45)
        {
            float cx = uS + (floor((u - uS) / bay) + 0.5) * bay;
            float inU = step(uS, u) * step(u, uE);
            float mu = 1.0 - smoothstep(ww * 0.5 - fu, ww * 0.5 + fu, abs(u - cx));
            float mv = smoothstep(sp.z - fv, sp.z + fv, lv) * (1.0 - smoothstep(sp.w - fv, sp.w + fv, lv));
            float cover = (ww / bay) * (sp.w - sp.z) / h;                 // average glass coverage
            float px = min(bay / max(fu, 1e-4), h / max(fv, 1e-4));      // pixels per bay / per storey
            c = lerp(wall, glass, lerp(cover, mu * mv, smoothstep(3.0, 8.0, px)) * inU);   // sub-pixel windows fade to their average
        }
    }
    float4 bd = StoreyParam(slot, 2);   // the band: height (0: 0.22 m), a shade of the wall (y > 0.5, shade z) or the trim
    if (fl < run.y - 0.5 && lv > h - (bd.x > 0.0 ? bd.x : 0.22)) c = bd.y > 0.5 ? wall * bd.z : trim;
    return c;
}

// Where a pixel of LOD2's facade is on one of its painted windows: 1 inside one (uv across and up it, along the
// wall's own "across", cross(up, outward normal), as the geometry LODs store it; the window's number from its centre
// on the wall's face in site space, as Facade.WindowId), 0 elsewhere, and 0 where the windows are too small on
// screen to draw (they fade to their average there, as in StoreyFacadeColor). origY: the height before Sink.
float StoreyFacadeWindow(float4 fac, float2 fac2, float slot, float3 posWS, float origY, float3 N, out float2 uv, out float id)
{
    uv = 0; id = 0;
    // the derivatives first, before any early return
    float3 p = TransformWorldToObject(float3(posWS.x, origY, posWS.z));
    float3 T = normalize(cross(float3(0, 1, 0), TransformWorldToObjectDir(N)));
    float along = dot(p, T);
    float fu = fwidth(fac.x), fv = fwidth(fac.y);
    // the facade's u runs along the wall one way or the other: which, from how the site-space point moves with it
    float su = dot(float2(ddx(along), ddy(along)), float2(ddx(fac.x), ddy(fac.x))) < 0.0 ? -1.0 : 1.0;
    if (fac2.y > 1.5) return 0;
    float4 sp = StoreyParam(slot, 4), run = StoreyParam(slot, 5);
    float u = fac.x, v = fac.y, uS = fac.z, uE = fac.w, bay = fac2.x, h = run.x;
    if (v >= run.z || bay <= 0.0 || sp.w <= sp.z + 0.4) return 0;
    float ww = sp.x >= 0.0 ? bay - sp.x : min(sp.y, bay - 0.5);
    if (ww < 0.45 || u < uS || u > uE) return 0;
    if (min(bay / max(fu, 1e-4), h / max(fv, 1e-4)) < 8.0) return 0;
    float lv = v - floor(v / h) * h;
    float cx = uS + (floor((u - uS) / bay) + 0.5) * bay;
    uv = float2((u - (cx - ww * 0.5)) / ww, (lv - sp.z) / (sp.w - sp.z));
    if (su < 0.0) uv.x = 1.0 - uv.x;
    float3 c = p + T * (su * (cx - u)) + float3(0, (sp.z + sp.w) * 0.5 - lv, 0);
    id = StoreyWindowId(c);
    return all(uv >= 0.0) && all(uv <= 1.0) ? 1.0 : 0.0;
}

#endif
