// Stub of URP's Lighting.hlsl (with Shadows.hlsl's pieces Storey uses).
#ifndef URP_LIGHTING_STUB
#define URP_LIGHTING_STUB
struct Light { float3 direction; float3 color; float distanceAttenuation; float shadowAttenuation; uint layerMask; };
struct InputData { float3 positionWS; float4 positionCS; float3 normalWS; float3 viewDirectionWS; float4 shadowCoord; float fogCoord; float3 vertexLighting; float3 bakedGI; float2 normalizedScreenSpaceUV; float4 shadowMask; };
struct SurfaceData { float3 albedo; float3 specular; float metallic; float smoothness; float3 normalTS; float3 emission; float occlusion; float alpha; float clearCoatMask; float clearCoatSmoothness; };
cbuffer UnityLighting { float4 _MainLightPosition; float4 _MainLightColor; float4x4 _MainLightWorldToShadow; float4 unity_SHAr; };
Light GetMainLight() { Light l = (Light)0; l.direction = _MainLightPosition.xyz; l.color = _MainLightColor.rgb; l.distanceAttenuation = 1; l.shadowAttenuation = 1; return l; }
float3 SampleSH(float3 n) { return unity_SHAr.xyz * n; }
float4 TransformWorldToShadowCoord(float3 p) { return mul(_MainLightWorldToShadow, float4(p, 1)); }
float3 ApplyShadowBias(float3 p, float3 n, float3 l) { return p + n * 0.01 + l * 0.01; }
float4 UniversalFragmentPBR(InputData i, SurfaceData s) { return float4(s.albedo * (i.bakedGI + saturate(dot(i.normalWS, _MainLightPosition.xyz))), s.alpha); }
#endif
