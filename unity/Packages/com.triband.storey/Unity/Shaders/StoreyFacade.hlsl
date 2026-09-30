// LOD2 massing: windows are drawn procedurally from the same parameters the geometry LODs use, so the
// LOD1 -> LOD2 switch keeps every window in place (SPEC §6.1). A port of the prototype's facadeColor().
//
// Each facade quad covers a run of storeys with one height and window type. fac = (u along the wall, height
// above the run's base, window span start, window span end); fac2 = (bay width, kind); slot = the run's row in
// _StoreyParams: six texels (wall, trim, glass, roof colours; window spec; run data).
#ifndef STOREY_FACADE_INCLUDED
#define STOREY_FACADE_INCLUDED

StructuredBuffer<float4> _StoreyParams;
#define STOREY_PARAM_TEXELS 6

float4 StoreyParam(float slot, int i) { return _StoreyParams[(int)(slot + 0.5) * STOREY_PARAM_TEXELS + i]; }

float3 StoreyFacadeColor(float4 fac, float2 fac2, float slot)
{
    float fu = fwidth(fac.x), fv = fwidth(fac.y);
    float3 wall = StoreyParam(slot, 0).rgb, trim = StoreyParam(slot, 1).rgb, glass = StoreyParam(slot, 2).rgb, roof = StoreyParam(slot, 3).rgb;
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
    if (fl < run.y - 0.5 && lv > h - 0.22) c = trim;
    return c;
}

#endif
