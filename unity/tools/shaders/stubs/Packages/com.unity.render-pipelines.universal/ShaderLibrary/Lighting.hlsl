// Stub of URP's Lighting.hlsl (with Shadows.hlsl's pieces Storey uses).
#ifndef URP_LIGHTING_STUB
#define URP_LIGHTING_STUB
// Shadows.hlsl's macros, from the main light's shadow keywords as URP 17.3 defines them
#if !defined(_RECEIVE_SHADOWS_OFF)
    #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE) || defined(_MAIN_LIGHT_SHADOWS_SCREEN)
        #define MAIN_LIGHT_CALCULATE_SHADOWS
        #if defined(_MAIN_LIGHT_SHADOWS) || (defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT))
            #define REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR
        #endif
    #endif
#endif
struct Light { float3 direction; float3 color; float distanceAttenuation; float shadowAttenuation; uint layerMask; };
struct InputData { float3 positionWS; float4 positionCS; float3 normalWS; float3 viewDirectionWS; float4 shadowCoord; float fogCoord; float3 vertexLighting; float3 bakedGI; float2 normalizedScreenSpaceUV; float4 shadowMask; };
struct SurfaceData { float3 albedo; float3 specular; float metallic; float smoothness; float3 normalTS; float3 emission; float occlusion; float alpha; float clearCoatMask; float clearCoatSmoothness; };
cbuffer UnityLighting { float4 _MainLightPosition; float4 _MainLightColor; float4x4 _MainLightWorldToShadow; float4 unity_SHAr; };
Light GetMainLight() { Light l = (Light)0; l.direction = _MainLightPosition.xyz; l.color = _MainLightColor.rgb; l.distanceAttenuation = 1; l.shadowAttenuation = 1; return l; }
float3 SampleSH(float3 n) { return unity_SHAr.xyz * n; }
// URP's split of the ambient probe between the stages (GlobalIllumination.hlsl): its SH Evaluation Mode's keywords
half3 SampleSHVertex(half3 normalWS)
{
#if defined(EVALUATE_SH_VERTEX)
    return half3(SampleSH(normalWS));
#elif defined(EVALUATE_SH_MIXED)
    return half3(unity_SHAr.xyz * normalWS);
#else
    return half3(0, 0, 0);
#endif
}
half3 SampleSHPixel(half3 L2Term, half3 normalWS)
{
#if defined(EVALUATE_SH_VERTEX)
    return L2Term;
#elif defined(EVALUATE_SH_MIXED)
    return max(half3(0, 0, 0), L2Term + half3(unity_SHAr.xyz * normalWS));
#else
    return half3(SampleSH(normalWS));
#endif
}
float4 TransformWorldToShadowCoord(float3 p) { return mul(_MainLightWorldToShadow, float4(p, 1)); }
float4 GetShadowCoord(VertexPositionInputs v) { return TransformWorldToShadowCoord(v.positionWS); }
float3 ApplyShadowBias(float3 p, float3 n, float3 l) { return p + n * 0.01 + l * 0.01; }
float4 ApplyShadowClamping(float4 c) { c.z = min(c.z, c.w); return c; }
float4 UniversalFragmentPBR(InputData i, SurfaceData s) { return float4(s.albedo * (i.bakedGI + saturate(dot(i.normalWS, _MainLightPosition.xyz))), s.alpha); }
#endif
