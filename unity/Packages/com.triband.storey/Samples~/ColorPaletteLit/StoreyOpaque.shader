// Storey's Opaque material lit like Flamingo/Color Palette/ColorPallete-Lit (StoreyColorPaletteLit.hlsl). Everything but
// the lighting is Storey's: Packages/com.triband.storey/Unity/Shaders/StoreyOpaque.shader is the same shader with URP PBR.
Shader "Flamingo/Storey/Opaque"
{
    Properties
    {
        _StoreyShadowStrength ("Shadow Strength", Float) = 1
        // Off (0): every face drawn. SiteRenderer sets Back (2) on the steady copies when the quality asset culls their back faces
        [HideInInspector] _StoreyCull ("Cull", Float) = 0
    }
    SubShader
    {
        // AlphaTest: this variant can discard (cross-fades, cutaway, occluders). The steady copies (STOREY_STEADY, no discard) draw
        // at Geometry (SiteRenderer.SteadyCopy), so every opaque draw comes before every discarding one, as Apple's GPUs want
        // (docs/PERFORMANCE.md §7.6)
        Tags { "RenderType" = "Opaque" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        // Both faces by default: a cut wall shows its inside as the section cap (STOREY_CAP). The steady copies cull back faces
        // when the quality asset says so (SiteRenderer.CullSteadyBackFaces, docs/PERFORMANCE.md §7.1)
        Cull [_StoreyCull]

        HLSLINCLUDE
        #define STOREY_CAP
        #define STOREY_CUSTOM_LIGHTING
        #define STOREY_MATERIAL_PROPERTIES float _StoreyShadowStrength;
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex StoreyVert
            #pragma fragment StoreyFrag
            // the graph's lighting keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            // only Per Vertex additional lights count in the project's nodes (README): Per Pixel and Forward+ (_ADDITIONAL_LIGHTS,
            // _CLUSTER_LIGHT_LOOP) made variants that drew the same, so they aren't declared
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ TRIBAND_SHADOWS_ON
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile_local _ STOREY_STEADY
            #include "Packages/com.triband.storey/Unity/Shaders/StoreyLit.hlsl"
            #include_with_pragmas "StoreyColorPaletteLit.hlsl"
            #include "Packages/com.triband.storey/Unity/Shaders/StoreyFragment.hlsl"
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
            #include "Packages/com.triband.storey/Unity/Shaders/StoreyLit.hlsl"
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
            #include "Packages/com.triband.storey/Unity/Shaders/StoreyLit.hlsl"
            ENDHLSL
        }
    }
}
