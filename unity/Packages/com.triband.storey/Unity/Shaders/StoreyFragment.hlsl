// The fragment program of the Storey materials' colour passes. Included by StoreyLit.hlsl, or by a project shader
// after it has defined StoreyLighting (STOREY_CUSTOM_LIGHTING). Everything here is Storey's own and stays the same
// whatever the lighting: occlusion, LOD cross-fade, tints, the section cap and the darkening.
#ifndef STOREY_FRAGMENT_INCLUDED
#define STOREY_FRAGMENT_INCLUDED

float4 StoreyFrag(Varyings IN, FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(IN);
    bool isFront = IS_FRONT_VFACE(cullFace, true, false);
    float dark = StoreyOcclude(StoreyUnpack(IN), IN.positionCS.xy, IN.positionCS.z);

    StoreySurface s;
#ifdef STOREY_MASSING
    s.albedo = StoreyFacadeColor(IN.fac, IN.fac2.xy, IN.fac2.z);
    s.alpha = 1.0;
#else
    s.albedo = IN.color.rgb;
    s.alpha = _StoreyAlpha;
#endif
    if (_StoreyLodTint > 0.5) s.albedo = lerp(s.albedo, StoreyLodTint(IN.misc.w), 0.65);
    if (_StoreyIso.x > 0.001 && abs(IN.misc.z - _StoreyIso.y) > 0.5) s.albedo = lerp(s.albedo, float3(0.78, 0.82, 0.83), _StoreyIso.x * 0.7);
    s.positionWS = IN.worldPos;
    s.normalWS = normalize(isFront ? IN.normalWS : -IN.normalWS);
    s.shadowCoord = IN.shadowCoord;
    s.positionCS = IN.positionCS;
    s.isFront = isFront;

    float4 color = StoreyLighting(s);
#if !defined(STOREY_GLASS)
    if (_StoreyWindowsOn > 0.5)
    {
    #ifdef STOREY_MASSING
        float2 wuv; float id; float onPane = StoreyFacadeWindow(IN.fac, IN.fac2.xy, IN.fac2.z, s.positionWS, IN.occ.z, s.normalWS, wuv, id);
    #else
        float2 wuv = IN.win.xy; float id = IN.win.w; float onPane = IN.win.z;
    #endif
        float2 wdx = ddx(wuv), wdy = ddy(wuv);   // uniform branch: every pixel of the quad takes them
        if (onPane > 0.5 && isFront) color.rgb = StoreyWindowPane(wuv, wdx, wdy, id, s.positionWS, s.normalWS);
    }
#endif
#ifdef STOREY_CAP
    if (!isFront) color.rgb = _StoreyCap.rgb;   // a cut exposes a wall's inside: the section cap
#endif
    return StoreyDarken(color, dark);
}

#endif
