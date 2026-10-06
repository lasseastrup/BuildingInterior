// Storey: props (docs/PROPS.md). An object inside a building (furniture, clutter, a rigidbody) is drawn as part of the
// building it is in: the same state row, occluder row and view globals, through the same StoreySink and StoreyOcclude
// the building's own shaders use, so it squashes, clips, ghosts and dissolves with its building and fades with its LOD0.
//
// The building comes from the renderer's user value (Renderer.SetShaderUserValue, Unity 6.3+): the building table's
// index + 1, 0 for none (a prop in the street: never occluded). The Storey Prop component writes it. It keeps the SRP
// Batcher and the GPU Resident Drawer: no material instances, no property blocks.
//
// For a project's Shader Graph: two Custom Function nodes, file mode, on this file.
//   StoreyPropVertex  (vertex stage)    Position (Object space) in, Position out: wire into the master stack's Position.
//   StoreyPropFragment (fragment stage) Position (World space) and Base Color in; Base Color and Alpha out: wire Base
//                                       Color through it, and Alpha into the master stack's Alpha with Alpha Clipping
//                                       on (threshold 0.5), so the depth and shadow passes run it too.
// Its clipping is in the colour pass only, as the buildings' is (the light indoors doesn't change with the cutaway);
// the LOD cross-fade and Dissolve reach the depth and shadow passes.
#ifndef STOREY_PROP_INCLUDED
#define STOREY_PROP_INCLUDED

// the depth and shadow passes: what the buildings' STOREY_DEPTH passes do
#if defined(SHADERPASS) && !defined(STOREY_DEPTH)
    #if defined(SHADERPASS_SHADOWCASTER) && SHADERPASS == SHADERPASS_SHADOWCASTER
        #define STOREY_DEPTH
    #elif defined(SHADERPASS_DEPTHONLY) && SHADERPASS == SHADERPASS_DEPTHONLY
        #define STOREY_DEPTH
    #elif defined(SHADERPASS_DEPTHNORMALSONLY) && SHADERPASS == SHADERPASS_DEPTHNORMALSONLY
        #define STOREY_DEPTH
    #elif defined(SHADERPASS_DEPTHNORMALS) && SHADERPASS == SHADERPASS_DEPTHNORMALS
        #define STOREY_DEPTH
    #endif
#endif

#ifndef SHADERGRAPH_PREVIEW
#include "Packages/com.triband.storey/Unity/Shaders/StoreyOcclusion.hlsl"
#endif

float _StoreyPropDebug;   // Tools > Storey > Props > Show Prop Buildings: each prop tinted by its building, magenta for none

// The renderer's user value. A project whose shaders read it another way defines STOREY_PROP_USER_VALUE first.
#ifndef STOREY_PROP_USER_VALUE
#define STOREY_PROP_USER_VALUE ((uint)unity_RendererUserValue)
#endif

// The building table index of the prop's building, -1 for none.
int StoreyPropBuilding()
{
#ifdef SHADERGRAPH_PREVIEW
    return -1;
#else
    return (int)STOREY_PROP_USER_VALUE - 1;
#endif
}

#ifndef SHADERGRAPH_PREVIEW
// The prop's whole storey has gone (Sink): its pivot's height is in a storey of an occluder that has collapsed.
bool StoreyPropGone(int ib, float pivotY)
{
#ifdef STOREY_DEPTH
    return false;
#else
    int slot = (int)(_StoreyState[ib].w + 0.5) - 1;
    if (slot < 0 || abs((float)ib - _StoreyActive.x) < 0.5) return false;
    int n = (int)(_StoreyOcc[slot * STOREY_OCC_W].x + 0.5);
    for (int i = 0; i < STOREY_OCC_W - 1; i++)
    {
        if (i >= n) break;
        float4 a = _StoreyOcc[slot * STOREY_OCC_W + 1 + i];
        float top = i + 1 < n ? _StoreyOcc[slot * STOREY_OCC_W + 2 + i].x : 1e6;
        if (pivotY >= a.x && pivotY < top) return a.z > 0.5;
    }
    return false;
#endif
}

// The prop shows at its building's current LOD: as an LOD0 mesh (shown at LOD0, fading in and out with it).
bool StoreyPropShown(int ib)
{
    float4 st = _StoreyState[ib];
    return st.x < 0.5 || (st.z < 1.0 && st.y < 0.5);
}
#endif

// Vertex stage: the prop squashes with its storey as its building sinks, and collapses to its pivot (nothing drawn) once
// that storey has gone or its building isn't showing LOD0.
void StoreyPropVertex_float(float3 PositionOS, out float3 Out)
{
    Out = PositionOS;
#ifndef SHADERGRAPH_PREVIEW
    int ib = StoreyPropBuilding();
    if (ib < 0) return;
    float3 pivot = GetObjectToWorldMatrix()._m03_m13_m23;
    if (!StoreyPropShown(ib) || StoreyPropGone(ib, pivot.y)) { Out = TransformWorldToObject(pivot); return; }
    float origY;
    float3 ws = StoreySink(TransformObjectToWorld(PositionOS), (float)ib, 0.0, origY);
    Out = TransformWorldToObject(ws);
#endif
}

void StoreyPropVertex_half(half3 PositionOS, out half3 Out) { float3 o; StoreyPropVertex_float(PositionOS, o); Out = o; }

#ifndef SHADERGRAPH_PREVIEW
float3 StoreyPropDebugColor(int ib)
{
    if (ib < 0) return float3(1, 0, 1);
    float h = frac(sin((float)ib * 12.9898) * 43758.5453);
    return saturate(abs(frac(h + float3(0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0) - 1.0) * 0.8 + 0.2;   // a hue per building
}
#endif

// Fragment stage: clips what the building's own shader clips at this point (the floors above the player, Slice's plane,
// Cutout's hole, Fade's and Dissolve's dither, the LOD cross-fade), and darkens the colour as a building in the way goes
// near-black. Alpha is always 1: it is there so Shader Graph runs this in the depth and shadow passes too.
void StoreyPropFragment_float(float3 PositionWS, float3 BaseColor, out float3 Color, out float Alpha)
{
    Color = BaseColor; Alpha = 1.0;
#ifndef SHADERGRAPH_PREVIEW
    int ib = StoreyPropBuilding();
    if (_StoreyPropDebug > 0.5) Color = StoreyPropDebugColor(ib);
    if (ib < 0) return;
    float4 st = _StoreyState[ib];
    bool isDisp = st.x < 0.5, isFrom = st.z < 1.0 && st.y < 0.5;
    StoreyVarying v;
    v.worldPos = PositionWS; v.wall = 0; v.kind = 0; v.wid = 0; v.bid = (float)ib; v.lod = 0;
    v.fade = st.z >= 1.0 ? 3.0 : (isDisp ? st.z : -1.0 - st.z);
    v.occ = st.w - 1.0;
    v.origY = PositionWS.y;   // a gone storey's props are collapsed in the vertex stage; what is left is where it stands
    float4 cs = TransformWorldToHClip(PositionWS);
    float2 pixel = (cs.xy / cs.w * 0.5 + 0.5) * _ScreenParams.xy;
    float dark = StoreyOcclude(v, pixel, 0.0);
    Color = lerp(Color, Color * 0.1 + float3(0.01, 0.011, 0.013), dark);
#endif
}

void StoreyPropFragment_half(half3 PositionWS, half3 BaseColor, out half3 Color, out half Alpha)
{
    float3 c; float a; StoreyPropFragment_float(PositionWS, BaseColor, c, a); Color = c; Alpha = a;
}

#endif
