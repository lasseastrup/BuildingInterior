// Stub of URP's Core.hlsl: only what Storey's shaders call, with URP's signatures, for a compile check outside Unity.
#ifndef URP_CORE_STUB
#define URP_CORE_STUB
#define CBUFFER_START(name) cbuffer name {
#define CBUFFER_END };
#define TEXTURE2D(name) Texture2D name
#define SAMPLER(name) SamplerState name
#define LOAD_TEXTURE2D(tex, coord) tex.Load(int3(coord, 0))
#define SAMPLE_TEXTURE2D(tex, smp, uv) tex.Sample(smp, uv)
#define SAMPLE_TEXTURE2D_GRAD(tex, smp, uv, dx, dy) tex.SampleGrad(smp, uv, dx, dy)
#define UNITY_VERTEX_INPUT_INSTANCE_ID uint instanceID : SV_InstanceID;
#define UNITY_VERTEX_OUTPUT_STEREO
#define UNITY_SETUP_INSTANCE_ID(v)
#define UNITY_TRANSFER_INSTANCE_ID(a, b) b.instanceID = a.instanceID
#define UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o)
#define FRONT_FACE_TYPE bool
#define FRONT_FACE_SEMANTIC SV_IsFrontFace
#define IS_FRONT_VFACE(f, a, b) ((f) ? (a) : (b))
cbuffer UnityPerDraw { float4x4 unity_ObjectToWorld; float4x4 unity_WorldToObject; };
cbuffer UnityPerFrame { float4x4 UNITY_MATRIX_VP; float4x4 UNITY_MATRIX_P; float4x4 UNITY_MATRIX_V; float4 _ScreenParams; float3 _WorldSpaceCameraPos; float4 _ProjectionParams; float4 unity_FogColor; };
float3 TransformObjectToWorld(float3 p) { return mul(unity_ObjectToWorld, float4(p, 1)).xyz; }
float3 TransformWorldToObject(float3 p) { return mul(unity_WorldToObject, float4(p, 1)).xyz; }
float3 TransformWorldToObjectDir(float3 d) { return normalize(mul((float3x3)unity_WorldToObject, d)); }
float3 TransformObjectToWorldNormal(float3 n) { return normalize(mul(n, (float3x3)unity_WorldToObject)); }
float4 TransformWorldToHClip(float3 p) { return mul(UNITY_MATRIX_VP, float4(p, 1)); }
float3 TransformWorldToView(float3 p) { return mul(UNITY_MATRIX_V, float4(p, 1)).xyz; }
float3 GetWorldSpaceNormalizeViewDir(float3 p) { return normalize(_WorldSpaceCameraPos - p); }
float3 GetWorldSpaceViewDir(float3 p) { return _WorldSpaceCameraPos - p; }
float2 GetNormalizedScreenSpaceUV(float4 positionCS) { return positionCS.xy / _ScreenParams.xy; }
float3 GetAbsolutePositionWS(float3 p) { return p; }
#endif
