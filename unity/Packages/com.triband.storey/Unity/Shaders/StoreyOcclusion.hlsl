// Storey: per-building state, LOD cross-fade, cutaway and buildings-in-the-way, shared by every
// Storey material (SPEC §5, §6.4). A port of the prototype's OCC_VERT / OCC_SINK / occlude().
//
// Everything that hides geometry is colour-pass only: the shadow caster and depth passes define
// STOREY_DEPTH and cast from the whole building, so the light indoors is the same with or without
// the cutaway (Plan §6.3). The Sink displacement runs in the vertex stage before projection on every
// pass, so shadows follow a sinking building.
#ifndef STOREY_OCCLUSION_INCLUDED
#define STOREY_OCCLUSION_INCLUDED

// per building: displayed LOD, previous LOD, cross-fade 0..1, occluder slot + 1
StructuredBuffer<float4> _StoreyState;
// per occluder slot, STOREY_OCC_W texels: (segments, dark, clip height, base height), then (floor line, scale, gone, slab gone) per storey
StructuredBuffer<float4> _StoreyOcc;
// how far each wall has slid down, by global wall id
StructuredBuffer<float> _StoreyWall;

#define STOREY_OCC_W 64
#define STOREY_SLAB 0.25

float3 _StoreyCam;      // camera position for the cutaway (focus + camera offset from the play camera's target)
float3 _StoreyFocus;    // the player
float2 _StoreyCamDir;   // camera forward in the XZ plane
float4 _StoreyCut;      // x: cutaway on, y: stub height, z: cut base, w: cut top
float4 _StoreyActive;   // x: active building index (-1 none), y: ceiling clip height
float4 _StoreyCap;      // section cap colour
float4 _StoreyIso;      // x: isolate amount, y: isolated building index
float _StoreyOccMode;   // 0 off, 1 Sink/Slice, 2 Cutout, 3 Fade
float4 _StoreyPlayer;   // xy: player screen position (pixels), z: player depth (0..1), w: hole radius (pixels)
float _StoreyLodTint;

struct StoreyVarying
{
    float3 worldPos;    // world position after displacement
    float4 wall;        // cutaway data: wall start (xy), normal scaled by 1 + length (zw)
    float kind;         // 0 slab, 1 wall, 2 tall part; + 8 × (neighbour index + 1) for party walls
    float wid;          // global wall id (0 = none)
    float bid;          // building index
    float lod;          // LOD this mesh is
    float fade;         // 3 = solid, 0..1 fading in, -1..-2 fading out
    float occ;          // occluder slot, or -1
    float origY;        // world y before the Sink displacement
};

float StoreyBayer(float2 p)
{
    int x = (int)fmod(p.x, 4.0); int y = (int)fmod(p.y, 4.0);
    static const float m[16] = { 0., 8., 2., 10., 12., 4., 14., 6., 3., 11., 1., 9., 15., 7., 13., 5. };
    return (m[x + y * 4] + 0.5) / 16.0;
}

float3 StoreyLodTint(float l) { return l < 0.5 ? float3(0.16, 0.62, 0.30) : l < 1.5 ? float3(0.95, 0.72, 0.12) : float3(0.90, 0.30, 0.10); }

// Sink: squash each storey of a building in the way between its floor lines. World space, before projection.
float3 StoreySink(float3 worldPos, float tag, float kind, out float origY)
{
    origY = worldPos.y;
#ifndef STOREY_DEPTH
    int ib = (int)(fmod(tag, 65536.0) + 0.5);
    int slot = (int)(_StoreyState[ib].w + 0.5) - 1;
    int im = (int)floor(kind / 8.0) - 1;   // a party wall also belongs to the neighbour: it stays unless the neighbour is sinking too
    bool mateUp = im >= 0 && _StoreyState[im].w < 0.5;
    if (slot >= 0 && abs((float)ib - _StoreyActive.x) > 0.5 && !mateUp)
    {
        int n = (int)(_StoreyOcc[slot * STOREY_OCC_W].x + 0.5);
        if (n > 0)
        {
            float y = origY, f0 = _StoreyOcc[slot * STOREY_OCC_W + 1].x;
            if (y > f0)
            {
                float f = f0;
                for (int i = 0; i < STOREY_OCC_W - 1; i++)
                {
                    if (i >= n) break;
                    float4 a = _StoreyOcc[slot * STOREY_OCC_W + 1 + i];
                    float top = i + 1 < n ? _StoreyOcc[slot * STOREY_OCC_W + 2 + i].x : 1e6;
                    f += clamp(y - a.x, 0.0, top - a.x) * a.y;
                }
                worldPos.y += f - y;
            }
        }
    }
#endif
    return worldPos;
}

// Fill the varyings from the building's state row. Returns false when this LOD is not shown at all
// (the caller clips the vertex by moving it behind the camera).
bool StoreyVertex(float tag, float4 wall, float kind, float wid, float3 worldPos, float origY, out StoreyVarying v)
{
    v.worldPos = worldPos; v.wall = wall; v.kind = kind; v.wid = wid; v.origY = origY;
    v.bid = fmod(tag, 65536.0); v.lod = floor(tag / 65536.0);
    int ib = (int)(v.bid + 0.5);
    float4 st = _StoreyState[ib];   // x: displayed LOD, y: previous LOD, z: cross-fade 0..1, w: occluder slot + 1
    bool isDisp = abs(v.lod - st.x) < 0.5;
    bool isFrom = st.z < 1.0 && abs(v.lod - st.y) < 0.5;
    v.fade = st.z >= 1.0 ? 3.0 : (isDisp ? st.z : -1.0 - st.z);
    v.occ = st.w - 1.0;
    return isDisp || isFrom;
}

// The fragment-stage test. Returns the darkening for a building in the way (0..1); clips with `clip(-1)` where the
// prototype discards.
float StoreyOcclude(StoreyVarying v, float2 screenPos, float fragDepth)
{
    float dark = 0.0;
    // 0. LOD cross-fade: the old and new LOD draw complementary dither patterns
    if (v.fade < 2.0)
    {
        float d = StoreyBayer(screenPos);
        if (v.fade >= 0.0) { if (d >= v.fade) clip(-1); }
        else if (d < -v.fade - 1.0) clip(-1);
    }
    // isolate: every other building fades to a light screen-door ghost
    if (_StoreyIso.x > 0.001 && abs(v.bid - _StoreyIso.y) > 0.5)
    {
#ifdef STOREY_DEPTH
        if (_StoreyIso.x > 0.5) clip(-1);   // faded buildings stop casting shadows
#endif
        if (StoreyBayer(screenPos + float2(1.0, 2.0)) < _StoreyIso.x) clip(-1);
    }
    bool act = abs(v.bid - _StoreyActive.x) < 0.5;
    float kind = fmod(v.kind, 8.0), mate = floor(v.kind / 8.0) - 1.0;
    bool actCut = act || (mate >= 0.0 && abs(mate - _StoreyActive.x) < 0.5);
#ifndef STOREY_DEPTH
    // 1. everything above the ceiling of the active floor
    if (act && v.worldPos.y > _StoreyActive.y) clip(-1);
    // 2. cutaway: walls between camera and focus drop to stub height; tall parts on the camera side too
    if (actCut && _StoreyCut.x > 0.5 && kind > 0.5 && v.worldPos.y > _StoreyCut.z + _StoreyCut.y && v.worldPos.y < _StoreyCut.w && v.worldPos.y > _StoreyCut.z - 0.05)
    {
        if (kind < 1.5 && v.wid > 0.5)
        {
            // walls slide down to the stub and back up (eased per wall on the CPU)
            float a = _StoreyWall[(int)(v.wid + 0.5)];
            if (a > 0.0 && v.worldPos.y > lerp(_StoreyCut.w, _StoreyCut.z + _StoreyCut.y, a * a * (3.0 - 2.0 * a))) clip(-1);
        }
        else if (kind < 1.5)
        {
            // no wall id: the instant test. Only if the sightline from the camera to the player crosses the wall
            // itself (plus room for the character's body), not just the wall's line
            float nl = length(v.wall.zw); float2 n = v.wall.zw / nl;
            float sc = dot(_StoreyCam.xz - v.wall.xy, n);
            float sp = dot(_StoreyFocus.xz - v.wall.xy, n);
            if (sc * sp < 0.0)
            {
                float2 x = _StoreyCam.xz + (_StoreyFocus.xz - _StoreyCam.xz) * (sc / (sc - sp));
                float s = dot(x - v.wall.xy, float2(n.y, -n.x));
                if (nl < 1.01 || (s > -0.5 && s < nl - 1.0 + 0.5)) clip(-1);
            }
        }
        else if (dot(v.wall.xy - _StoreyFocus.xz, _StoreyCamDir) > 0.4) clip(-1);
    }
    // 3. buildings between the camera and the player (never the player's own building, which is sliced above)
    bool mateUp = mate >= 0.0 && _StoreyState[(int)mate].w < 0.5;
    if (_StoreyOccMode > 0.5 && !act && v.occ > -0.5 && !mateUp)
    {
        int slot = (int)(v.occ + 0.5);
        float4 hd = _StoreyOcc[slot * STOREY_OCC_W];   // segments, dark, clip height, solid base height
        if (v.origY > hd.z) clip(-1);                     // Slice: the cut plane lowered to the player's storey
        int n = (int)(hd.x + 0.5);
        if (n > 0)
        {
            // Sink: storeys collapse from the roof down, the player's storey squashes to a low base
            dark = hd.y;
            for (int i = 0; i < STOREY_OCC_W - 1; i++)
            {
                if (i >= n) break;
                float4 a = _StoreyOcc[slot * STOREY_OCC_W + 1 + i];
                float top = i + 1 < n ? _StoreyOcc[slot * STOREY_OCC_W + 2 + i].x : 1e6;
                if (v.origY < a.x || v.origY > top) continue;
                if (a.z > 0.5) clip(-1);                                   // collapsed storey
                float ceil_ = top - (STOREY_SLAB + 0.01);
                if (a.w > 0.5 && v.origY > ceil_) clip(-1);                // the slab above, once the storey on it has gone
                break;
            }
        }
        else if (v.origY < hd.w) dark = hd.y;                              // Cutout / Fade keep a solid dark base
        else if (hd.y > 0.001 && fragDepth < _StoreyPlayer.z)              // only what lies in front of the player
        {
            if (_StoreyOccMode < 2.5)
            {
                float f = (1.0 - smoothstep(_StoreyPlayer.w * 0.6, _StoreyPlayer.w, length(screenPos - _StoreyPlayer.xy))) * hd.y;
                if (f > StoreyBayer(screenPos)) clip(-1);
            }
            else if (StoreyBayer(screenPos + float2(2.0, 1.0)) < hd.y * 0.8) clip(-1);   // fades to a light ghost
        }
    }
#endif
    return dark;
}

// Applied after lighting: a building in the way goes near-black (a trace of shading keeps its form).
float4 StoreyDarken(float4 color, float dark)
{
    color.rgb = lerp(color.rgb, color.rgb * 0.1 + float3(0.01, 0.011, 0.013), dark);
    color.a = lerp(color.a, 1.0, dark);
    return color;
}

#endif
