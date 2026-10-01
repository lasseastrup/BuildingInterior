// The lighting of Flamingo/Color Palette/ColorPallete-Lit (an Unlit-target Shader Graph), as Storey's
// StoreyLighting. Transcribed from the graph's generated code, node for node, calling the same custom-function
// files, so a building and a prop of the same palette colour shade alike. Change it with the graph.
//
// Included by the three shaders next to it, after StoreyLit.hlsl and before StoreyFragment.hlsl.
#ifndef STOREY_COLOR_PALETTE_LIT_INCLUDED
#define STOREY_COLOR_PALETTE_LIT_INCLUDED

#include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
#include_with_pragmas "Assets/Art/Shaders/CustomNodes/GlobalVariables.hlsl"
#include_with_pragmas "Assets/Art/Shaders/CustomNodes/MainLightNode.hlsl"
#include_with_pragmas "Assets/Art/Shaders/CustomNodes/AdditionalLightsNode.hlsl"

// The graph's Colorspace Conversion node, RGB to Linear
float3 StoreyGraphToLinear(float3 c)
{
    float3 lo = c / 12.92;
    float3 hi = pow(max(abs((c + 0.055) / 1.055), 1.192092896e-07), float3(2.4, 2.4, 2.4));
    return float3(c <= 0.04045) ? lo : hi;
}

// The graph's Lighting sub-graph: hard main-light shadow faded out with distance, the global shadow colour as
// ambient, plus the additional lights
float3 StoreyGraphLighting(float3 positionWS, float3 normalWS, float3 shadowColor)
{
    float3 absWS = GetAbsolutePositionWS(positionWS);
    float3 direction, color; float distanceAtten, shadowAtten;
    MainLight_float(absWS, direction, color, distanceAtten, shadowAtten);
    float ndl = saturate(dot(normalWS, direction));
    float shadow = saturate(lerp(-100.0, 100.0, distanceAtten * shadowAtten));   // a hard step at 0.5

    float fadeStart, fadeInterval;
    GetGlobalShadowFadeDistanceStart_float(fadeStart);
    GetGlobalShadowFadeInterval_float(fadeInterval);
    float fade = saturate((length(absWS - _WorldSpaceCameraPos) - fadeStart) / fadeInterval);
    float lit = saturate(ndl * lerp(shadow, 1.0, fade));

    float3 light = lerp(shadowColor, color, lit);
    float3 directional, omni;
    SG_MobileVertexLights_float(positionWS, normalWS, directional, omni);
    return saturate(light + lerp(directional, omni, 0.7));
}

// The graph's Character Blob: darkens a small spot under the player
float StoreyGraphCharacterBlob(float3 positionWS)
{
    float3 player;
    GetGlobalPlayerPosition_float(player);
    return saturate(distance(player + float3(0, -1, 0), positionWS) + 0.7);
}

// The graph's Handle Fog (shadergraph_LWFog, in world space; the graph's object-space round trip is the same point)
float3 StoreyGraphFog(float3 color, float3 positionWS)
{
    float density = 0.0;
#if defined(FOG_LINEAR_KEYWORD_DECLARED)
    if (FOG_LINEAR || FOG_EXP || FOG_EXP2)
    {
        float viewZ = -TransformWorldToView(positionWS).z;
        density = 1.0 - ComputeFogIntensity(ComputeFogFactorZ0ToFar(max(viewZ - _ProjectionParams.y, 0)));
    }
#endif
    return lerp(color, unity_FogColor.rgb, density);
}

// Base Shading then Handle Fog, as the graph's Base Color. The Unlit target outputs it unchanged.
float4 StoreyLighting(StoreySurface s)
{
    float4 shadowColor;
    GetGlobalShadowColor_float(shadowColor);
    float3 ambient = StoreyGraphToLinear(shadowColor.rgb) * _StoreyShadowStrength;
    float3 c = StoreyGraphLighting(s.positionWS, s.normalWS, ambient) * s.albedo;
    c *= StoreyGraphCharacterBlob(s.positionWS);
    return float4(StoreyGraphFog(c, s.positionWS), s.alpha);
}

#endif
