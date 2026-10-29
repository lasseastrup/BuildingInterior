// The opaque building material: every LOD0 and LOD1 mesh of every building uses this one material.
Shader "Storey/Opaque"
{
    Properties
    {
        _StoreySmoothness ("Smoothness", Range(0, 1)) = 0.14
        _StoreyMetallic ("Metallic", Range(0, 1)) = 0.0
        // Off (0): every face drawn. SiteRenderer sets Back (2) on the steady copies when the quality asset culls their back faces
        [HideInInspector] _StoreyCull ("Cull", Float) = 0
    }
    SubShader
    {
        // AlphaTest: this variant can discard (cross-fades, cutaway, occluders). The steady copies (STOREY_STEADY, no discard) draw
        // at Geometry (SiteRenderer.SteadyCopy), so every opaque draw comes before every discarding one, as Apple's GPUs want
        // (docs/PERFORMANCE.md §7.6)
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        // Both faces by default: a cut wall shows its inside as the section cap (STOREY_CAP). The steady copies cull back faces
        // when the quality asset says so (SiteRenderer.CullSteadyBackFaces, docs/PERFORMANCE.md §7.1)
        Cull [_StoreyCull]

        HLSLINCLUDE
        #define STOREY_CAP
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StoreyVert
            #pragma fragment StoreyFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX   // URP's SH Evaluation Mode: the ambient probe per vertex on mobile (docs/PERFORMANCE.md §7.3)
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_local _ STOREY_STEADY
            #pragma multi_compile_fragment _ STOREY_SIMPLE_LIGHTING
            #include "StoreyLit.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StoreyVert
            #pragma fragment StoreyDepthFrag
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_local _ STOREY_STEADY
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #define STOREY_DEPTH
            #define STOREY_SHADOW
            #include "StoreyLit.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StoreyVert
            #pragma fragment StoreyDepthFrag
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_local _ STOREY_STEADY
            #define STOREY_DEPTH
            #include "StoreyLit.hlsl"
            ENDHLSL
        }

        // The Scene view's selection outline, never drawn in a player: where the depth pass draws, of the building being
        // edited alone while the editor names one (StoreySelectionFrag)
        Pass
        {
            Name "SceneSelectionPass"
            Tags { "LightMode" = "SceneSelectionPass" }
            Cull Off
            ZWrite On
            Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StoreyVert
            #pragma fragment StoreySelectionFrag
            // compiled when first drawn, as URP's own selection pass for the GPU Resident Drawer (BRGPicking.shader): compiled
            // in the background instead, the buildings it draws had no outline at all
            #pragma editor_sync_compilation
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #define STOREY_DEPTH
            #define STOREY_SELECTION
            #include "StoreyLit.hlsl"
            ENDHLSL
        }
    }
}
