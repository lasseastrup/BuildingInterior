// Storey's Massing material lit like Flamingo/Color Palette/ColorPallete-Lit (StoreyColorPaletteLit.hlsl). Everything but
// the lighting is Storey's: Packages/com.triband.storey/Unity/Shaders/StoreyMassing.shader is the same shader with URP PBR.
Shader "Flamingo/Storey/Massing"
{
    Properties
    {
        _StoreyShadowStrength ("Shadow Strength", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #define STOREY_MASSING
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ TRIBAND_SHADOWS_ON
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #include "Packages/com.triband.storey/Unity/Shaders/StoreyLit.hlsl"
            #include "StoreyColorPaletteLit.hlsl"
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
            #define STOREY_DEPTH
            #include "Packages/com.triband.storey/Unity/Shaders/StoreyLit.hlsl"
            ENDHLSL
        }
    }
}
