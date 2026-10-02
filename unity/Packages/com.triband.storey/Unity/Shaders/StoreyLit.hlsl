// The shared vertex and fragment programs of the Storey materials (URP). Four variants share this file:
//   Storey/Opaque   palette colour from the vertex's colour reference, double-sided, section cap on back faces
//   Storey/Glass    palette colour from the vertex's colour reference, transparent, no depth write
//   Storey/Massing  LOD2: colour from StoreyFacadeColor()
// each with a shadow-caster / depth pass that defines STOREY_DEPTH. Keywords: STOREY_MASSING, STOREY_CAP.
//
// Lighting is one function, StoreyLighting(StoreySurface), so a project can light the buildings like the rest
// of its world (the Color Palette Lit sample is one). By default it is URP's PBR. A project shader
// instead defines STOREY_CUSTOM_LIGHTING (and, for its own material properties, STOREY_MATERIAL_PROPERTIES) in
// every pass, includes this file, defines StoreyLighting and then includes StoreyFragment.hlsl.
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
#ifdef STOREY_MATERIAL_PROPERTIES
    STOREY_MATERIAL_PROPERTIES   // a project shader's own; the same in every pass, for the SRP Batcher
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
    float4 wall       : TEXCOORD1;   // massing: fac (LOD0 has no such stream: its walls are in _StoreyWallData)
    float2 kind       : TEXCOORD2;   // LOD0: kind, wall id; massing: fac2
    float  wid        : TEXCOORD3;   // massing: parameter row
    UNITY_VERTEX_INPUT_INSTANCE_ID   // the GPU Resident Drawer draws through instancing: the object's matrix comes from its instance
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
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings StoreyVert(Attributes IN)
{
    Varyings OUT = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(IN);   // before any object-space transform, or an instanced draw uses another object's matrix
    UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
    float3 worldPos = TransformObjectToWorld(IN.positionOS);
    float3 worldPos0 = worldPos;
#ifdef STOREY_MASSING
    float kind = 0.0; float4 wall = 0; float wid = 0;
#else
    // the wall's data by its id: one entry per wall, not 16 bytes on every vertex
    float kind = IN.kind.x, wid = IN.kind.y;
    float4 wall = wid > 0.5 ? _StoreyWallData[(int)(wid + 0.5)] : float4(0, 0, 0, 0);
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
    UNITY_SETUP_INSTANCE_ID(IN);
    StoreyOcclude(StoreyUnpack(IN), IN.positionCS.xy, IN.positionCS.z);   // only the cross-fade and isolate reach the depth passes
    return 0;
}

#else

// What the lighting gets: the surface after Storey's occlusion, LOD and isolate handling.
struct StoreySurface
{
    float3 albedo;        // linear palette colour × shade, LOD/isolate tint applied
    float  alpha;
    float3 positionWS;    // after Sink
    float3 normalWS;      // normalised, towards the viewer on back faces
    float4 shadowCoord;   // main light, from the vertex
    float4 positionCS;    // SV_Position: pixel coordinates in xy
    bool   isFront;
};

#ifndef STOREY_CUSTOM_LIGHTING
// URP's PBR with the material's fixed smoothness and metallic and spherical-harmonics ambient.
float4 StoreyLighting(StoreySurface s)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = s.positionWS;
    inputData.normalWS = s.normalWS;
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(s.positionWS);
    inputData.shadowCoord = s.shadowCoord;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(s.positionCS);
    inputData.bakedGI = SampleSH(s.normalWS);

    SurfaceData surfaceData = (SurfaceData)0;
    surfaceData.albedo = s.albedo;
    surfaceData.alpha = s.alpha;
    surfaceData.metallic = _StoreyMetallic;
    surfaceData.smoothness = _StoreySmoothness;
    surfaceData.occlusion = 1.0;
    return UniversalFragmentPBR(inputData, surfaceData);
}

#include "StoreyFragment.hlsl"
#endif

#endif
#endif
