// The shared vertex and fragment programs of the Storey materials (URP). Four variants share this file:
//   Storey/Opaque   palette colour from the vertex's colour reference, double-sided, section cap on back faces
//   Storey/Glass    palette colour from the vertex's colour reference, transparent, no depth write
//   Storey/Massing  LOD2: colour from StoreyFacadeColor()
// each with a shadow-caster / depth pass that defines STOREY_DEPTH. Keywords: STOREY_MASSING, STOREY_CAP.
//
// Lighting is one function, StoreyLighting(StoreySurface), so a project can light the buildings like the rest
// of its world (the Color Palette Lit sample is one). By default it is URP's PBR. A project shader
// instead defines STOREY_CUSTOM_LIGHTING (and, for its own material properties, STOREY_MATERIAL_PROPERTIES) in
// every pass, includes this file, defines StoreyLighting and then includes StoreyFragment.hlsl. Lighting that fogs also
// defines STOREY_FOG(color, positionWS), so the section cap is fogged with the rest.
#ifndef STOREY_LIT_INCLUDED
#define STOREY_LIT_INCLUDED

// The minimum tier's lighting (StoreyQualitySettings.specularAndReflections off, a global keyword the opaque and massing
// colour passes declare): URP's PBR without the specular term, and a constant colour in place of the reflection probe's
// cube-map fetch (docs/PERFORMANCE.md §7.3). URP reads both where Lighting.hlsl defines its functions, so before it.
#ifdef STOREY_SIMPLE_LIGHTING
#define _SPECULARHIGHLIGHTS_OFF
#define _ENVIRONMENTREFLECTIONS_OFF
#endif

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "StoreyOcclusion.hlsl"
#include "StoreyPalette.hlsl"
#if !defined(STOREY_DEPTH)
#include "StoreyWindow.hlsl"   // the glass too: LOD0's see-through panes start as LOD1's painted ones (StoreyFragment)
#endif
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

// What each pass and variant hands its fragment stage, and nothing more (docs/PERFORMANCE.md §7.2): a tile-based GPU
// writes every vertex output to memory between the two stages. The depth and shadow passes carry the position and,
// unless steady, the five values the cross-fade, isolate and Dissolve tests read; the colour pass leaves out the
// cutaway's and the occluders' values when steady, and the massing has no vertex colour or pane.
struct Varyings
{
    float4 positionCS : SV_POSITION;
#ifdef STOREY_DEPTH
#ifndef STOREY_STEADY
    float4 occ        : TEXCOORD4;   // fade, occ slot, origY, kind
    float  bid        : TEXCOORD3;   // building index
#endif
#else
    // half where 16 bits hold it (a direction, a colour, a place on a pane, a window's number up to 255): Apple's GPUs
    // store half outputs in half the space between the stages (docs/PERFORMANCE.md §7.2). Positions, the facade's metres
    // and building indices (up to 8,192, past a half's 2,048 exact integers) stay float
    half3 normalWS    : TEXCOORD0;
#ifndef STOREY_MASSING
    half4 color       : TEXCOORD1;
    half4 win         : TEXCOORD9;   // a window pane: u, v across and up it, 1 on a pane, its number (LOD0/LOD1)
#endif
#if defined(EVALUATE_SH_VERTEX) || defined(EVALUATE_SH_MIXED)
    half3 vertexSH    : TEXCOORD10;  // the ambient probe, or its L2 part, per vertex: every face is flat, so exact (§7.3)
#endif
#ifdef STOREY_STEADY
    float2 misc       : TEXCOORD3;   // bid, lod
#else
    float4 misc       : TEXCOORD3;   // bid, lod, kind, wid
    float4 wall       : TEXCOORD2;
    float4 occ        : TEXCOORD4;   // fade, occ slot, origY, unused
#endif
    float3 worldPos   : TEXCOORD5;
#ifdef STOREY_MASSING
    float4 fac        : TEXCOORD6;
    float4 fac2       : TEXCOORD7;   // fac2.xy, slot
#endif
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    float4 shadowCoord : TEXCOORD8;   // one cascade or screen-space shadows; with cascades StoreyFrag finds the pixel's
#endif
#endif
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
#ifdef STOREY_MASSING
    float kind = 0.0; float4 wall = 0; float wid = 0;
#else
    float kind = IN.kind.x, wid = IN.kind.y;
#if defined(STOREY_DEPTH) || defined(STOREY_STEADY)
    float4 wall = 0;   // only the colour pass's cutaway reads a wall's data
#else
    // the wall's data by its id: one entry per wall, not 16 bytes on every vertex. Read for every vertex, as HLSL would
    // read it inside a ?: anyway (StoreyOcclusion.hlsl): id 0, no wall, reads the first row; a mesh without the stream
    // (LOD1's) reads whatever Unity gives in its place, kept in the table
    float4 wallRow = _StoreyWallData[StoreyWallId(wid)];
    float4 wall = wid > 0.5 ? wallRow : float4(0, 0, 0, 0);
#endif
#endif
    float origY;
    worldPos = StoreySink(worldPos, IN.tag, kind, origY);
    StoreyVarying v;
    bool shown = StoreyVertex(IN.tag, wall, kind, wid, worldPos, origY, v);
    float3 normalWS = TransformObjectToWorldNormal(IN.normalOS.xyz);
#ifdef STOREY_SHADOW
    OUT.positionCS = TransformWorldToHClip(ApplyShadowBias(worldPos, normalWS, _LightDirection));
#else
    OUT.positionCS = TransformWorldToHClip(worldPos);
#endif
    if (!shown) OUT.positionCS = float4(2.0, 2.0, 2.0, 1.0);   // LOD not shown: clip the whole triangle
#ifdef STOREY_DEPTH
#ifndef STOREY_STEADY
    OUT.occ = float4(v.fade, v.occ, origY, kind);
    OUT.bid = v.bid;
#endif
#else
    OUT.normalWS = normalWS;
#ifndef STOREY_MASSING
    // a window pane carries its place on the pane (the shade byte and the normal's fourth byte) and the window's number
    // (the slot byte) instead (MeshUpload); its slot is the glass's, its shade the opaque glass's (or, for LOD0's
    // see-through panes, the glass's own)
#ifdef STOREY_GLASS
    const float paneTone = 1.0;
#else
    const float paneTone = 0.42;
#endif
    uint4 ce = (uint4)round(IN.color * 255.0);
    bool pane = IN.normalOS.w > 0.5 / 127.0;
    // one lookup, its slot and shade picked first (StoreyVertexColor's otherwise): HLSL evaluates both sides of a ?:, and
    // the colour's side read a pane's number as its slot
    OUT.color = float4(StoreyPaletteColor(ce.x | (ce.y << 8), pane ? 6u : ce.z, pane ? paneTone : ce.w / 128.0), 1.0);   // a face's vertices share one swatch: exact per vertex
    OUT.win = pane ? float4(IN.color.a, (IN.normalOS.w * 127.0 - 1.0) / 126.0, 1.0, (float)ce.z) : float4(0, 0, 0, 0);
#endif
#ifdef STOREY_STEADY
    OUT.misc = float2(v.bid, v.lod);
#else
    OUT.misc = float4(v.bid, v.lod, kind, wid);
    OUT.wall = wall;
    OUT.occ = float4(v.fade, v.occ, origY, 0);
#endif
    OUT.worldPos = worldPos;
#if defined(EVALUATE_SH_VERTEX) || defined(EVALUATE_SH_MIXED)
    OUT.vertexSH = SampleSHVertex(half3(normalWS));
#endif
#ifdef STOREY_MASSING
    OUT.fac = IN.wall;
    OUT.fac2 = float4(IN.kind, IN.wid, 0);
#endif
#if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
    VertexPositionInputs inputs = (VertexPositionInputs)0;
    inputs.positionWS = worldPos; inputs.positionCS = OUT.positionCS;
    OUT.shadowCoord = GetShadowCoord(inputs);   // URP's: the light's space, or the screen's (before the divide) for screen-space shadows
#endif
#endif
    return OUT;
}

// The varyings as StoreyOcclude and the fragment stage read them. A value a variant doesn't carry reads as a steady
// building's: solid (fade 3), not in the way (occ -1), no Sink (origY is the height).
StoreyVarying StoreyUnpack(Varyings IN)
{
    StoreyVarying v = (StoreyVarying)0;
    v.fade = 3.0; v.occ = -1.0;
#ifdef STOREY_DEPTH
#ifndef STOREY_STEADY
    v.fade = IN.occ.x; v.occ = IN.occ.y; v.origY = IN.occ.z; v.kind = IN.occ.w; v.bid = IN.bid;
#endif
#else
    v.worldPos = IN.worldPos; v.origY = IN.worldPos.y; v.bid = IN.misc.x; v.lod = IN.misc.y;
#ifndef STOREY_STEADY
    v.kind = IN.misc.z; v.wid = IN.misc.w; v.wall = IN.wall;
    v.fade = IN.occ.x; v.occ = IN.occ.y; v.origY = IN.occ.z;
#endif
#endif
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
    float4 shadowCoord;   // the main light's, as URP's Lit takes it: from the vertex, or the pixel's with cascades
    float4 positionCS;    // SV_Position: pixel coordinates in xy
    bool   isFront;
    float3 vertexSH;      // the ambient probe from the vertex as URP's SH Evaluation Mode splits it (SampleSHPixel); 0 per pixel
};

#ifndef STOREY_CUSTOM_LIGHTING
// URP's PBR with the material's fixed smoothness and metallic and spherical-harmonics ambient, evaluated where the
// pipeline asset's SH Evaluation Mode says (per vertex on mobile by default), as URP's Lit does.
float4 StoreyLighting(StoreySurface s)
{
    InputData inputData = (InputData)0;
    inputData.positionWS = s.positionWS;
    inputData.normalWS = s.normalWS;
    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(s.positionWS);
    inputData.shadowCoord = s.shadowCoord;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(s.positionCS);
    inputData.bakedGI = SampleSHPixel(half3(s.vertexSH), half3(s.normalWS));

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
