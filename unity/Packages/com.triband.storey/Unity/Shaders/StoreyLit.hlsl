// The shared vertex and fragment programs of the Storey materials (URP). Four variants share this file:
//   Storey/Opaque   palette colour from the vertex's colour reference, double-sided, section cap on back faces
//   Storey/Glass    palette colour from the vertex's colour reference, transparent, no depth write
//   Storey/Massing  LOD2: colour from StoreyFacadeColor()
// each with a shadow-caster / depth pass that defines STOREY_DEPTH. Keywords: STOREY_MASSING, STOREY_CAP.
#ifndef STOREY_LIT_INCLUDED
#define STOREY_LIT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "StoreyOcclusion.hlsl"
#include "StoreyPalette.hlsl"
#ifdef STOREY_MASSING
#include "StoreyFacade.hlsl"
#endif

// Material properties. Declared here, after Core.hlsl, because CBUFFER_START is URP's macro: a
// per-shader HLSLINCLUDE block runs before this file and cannot use it.
CBUFFER_START(UnityPerMaterial)
    float _StoreySmoothness;
    float _StoreyMetallic;
#ifdef STOREY_GLASS
    float _StoreyAlpha;
#endif
CBUFFER_END
#ifndef STOREY_GLASS
static const float _StoreyAlpha = 1.0;
#endif
#ifdef STOREY_SHADOW
float3 _LightDirection;   // set by URP for the shadow caster pass
#endif

struct Attributes
{
    float3 positionOS : POSITION;
    float4 normalOS   : NORMAL;      // SNorm8 × 4
    float4 color      : COLOR;       // UNorm8 × 4 colour reference: row, slot, shade (unused by massing)
    float  tag        : TEXCOORD0;   // building index + 65536 × LOD
    float4 wall       : TEXCOORD1;   // LOD0: cutaway wall data; massing: fac
    float2 kind       : TEXCOORD2;   // LOD0: kind (x); massing: fac2
    float  wid        : TEXCOORD3;   // LOD0: wall id; massing: parameter row
};

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS   : TEXCOORD0;
    float4 color      : TEXCOORD1;
    float4 wall       : TEXCOORD2;
    float4 misc       : TEXCOORD3;   // kind, wid, bid, lod
    float4 occ        : TEXCOORD4;   // fade, occ slot, origY, unused
    float3 worldPos   : TEXCOORD5;
#ifdef STOREY_MASSING
    float4 fac        : TEXCOORD6;
    float4 fac2       : TEXCOORD7;   // fac2.xy, slot
#endif
    float4 shadowCoord : TEXCOORD8;
};

Varyings StoreyVert(Attributes IN)
{
    Varyings OUT = (Varyings)0;
    float3 worldPos = TransformObjectToWorld(IN.positionOS);
    float3 worldPos0 = worldPos;
#ifdef STOREY_MASSING
    float kind = 0.0; float4 wall = 0; float wid = 0;
#else
    float kind = IN.kind.x; float4 wall = IN.wall; float wid = IN.wid;
#endif
    float origY;
    worldPos = StoreySink(worldPos, IN.tag, kind, origY);
    StoreyVarying v;
    bool shown = StoreyVertex(IN.tag, wall, kind, wid, worldPos, origY, v);
    OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS.xyz);
#ifdef STOREY_SHADOW
    OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(worldPos, OUT.normalWS, _LightDirection));
#else
    OUT.positionCS = TransformWorldToHClip(worldPos);
#endif
    if (!shown) OUT.positionCS = float4(2.0, 2.0, 2.0, 1.0);   // LOD not shown: clip the whole triangle
#if !defined(STOREY_MASSING) && !defined(STOREY_DEPTH)
    OUT.color = float4(StoreyVertexColor(IN.color), 1.0);   // a face's vertices share one swatch: exact per vertex
#endif
    OUT.wall = wall;
    OUT.misc = float4(kind, wid, v.bid, v.lod);
    OUT.occ = float4(v.fade, v.occ, origY, 0);
    OUT.worldPos = worldPos;
#ifdef STOREY_MASSING
    OUT.fac = IN.wall;
    OUT.fac2 = float4(IN.kind, IN.wid, 0);
#endif
    OUT.shadowCoord = TransformWorldToShadowCoord(worldPos);
    return OUT;
}

StoreyVarying StoreyUnpack(Varyings IN)
{
    StoreyVarying v;
    v.worldPos = IN.worldPos; v.wall = IN.wall; v.kind = IN.misc.x; v.wid = IN.misc.y; v.bid = IN.misc.z; v.lod = IN.misc.w;
    v.fade = IN.occ.x; v.occ = IN.occ.y; v.origY = IN.occ.z;
    return v;
}

#ifdef STOREY_DEPTH

float4 StoreyDepthFrag(Varyings IN) : SV_Target
{
    StoreyOcclude(StoreyUnpack(IN), IN.positionCS.xy, IN.positionCS.z);   // only the cross-fade and isolate reach the depth passes
    return 0;
}

#else

float4 StoreyFrag(Varyings IN, FRONT_FACE_TYPE cullFace : FRONT_FACE_SEMANTIC) : SV_Target
{
    bool isFront = IS_FRONT_VFACE(cullFace, true, false);
    float dark = StoreyOcclude(StoreyUnpack(IN), IN.positionCS.xy, IN.positionCS.z);

#ifdef STOREY_MASSING
    float3 albedo = StoreyFacadeColor(IN.fac, IN.fac2.xy, IN.fac2.z);
    float alpha = 1.0;
#else
    float3 albedo = IN.color.rgb;
    float alpha = _StoreyAlpha;
#endif
    if (_StoreyLodTint > 0.5) albedo = lerp(albedo, StoreyLodTint(IN.misc.w), 0.65);
    if (_StoreyIso.x > 0.001 && abs(IN.misc.z - _StoreyIso.y) > 0.5) albedo = lerp(albedo, float3(0.78, 0.82, 0.83), _StoreyIso.x * 0.7);

    InputData inputData = (InputData)0;
    inputData.positionWS = IN.worldPos;
    inputData.normalWS = normalize(isFront ? IN.normalWS : -IN.normalWS);
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(IN.worldPos);
    inputData.shadowCoord = IN.shadowCoord;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
    inputData.bakedGI = SampleSH(inputData.normalWS);

    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = albedo;
    surfaceData.alpha = alpha;
    surfaceData.metallic = _StoreyMetallic;
    surfaceData.smoothness = _StoreySmoothness;
    surfaceData.occlusion = 1.0;

    float4 color = UniversalFragmentPBR(inputData, surfaceData);
#ifdef STOREY_CAP
    if (!isFront) color.rgb = _StoreyCap.rgb;   // a cut exposes a wall's inside: the section cap
#endif
    return StoreyDarken(color, dark);
}

#endif
#endif
