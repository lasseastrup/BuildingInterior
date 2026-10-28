// Storey's Glass material lit like Flamingo/Color Palette/ColorPallete-Lit (StoreyColorPaletteLit.hlsl). Everything but
// the lighting is Storey's: Packages/com.triband.storey/Unity/Shaders/StoreyGlass.shader is the same shader with URP PBR.
Shader "Flamingo/Storey/Glass"
{
    Properties
    {
        _StoreyAlpha ("Opacity", Range(0, 1)) = 0.5
        _StoreyShadowStrength ("Shadow Strength", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        HLSLINCLUDE
        #define STOREY_GLASS
        #define _SURFACE_TYPE_TRANSPARENT   // as the graph's transparent target: the shadow map itself, not the screen-space shadows
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
            // additional lights as the project's node takes them (README): Forward+'s cluster loop, or a per-object list where the
            // URP asset sets Per Vertex; one set, as URP never has both. Plain Per Pixel adds nothing there, so _ADDITIONAL_LIGHTS
            // isn't declared
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _LIGHT_LAYERS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ TRIBAND_SHADOWS_ON
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #include "Packages/com.triband.storey/Unity/Shaders/StoreyLit.hlsl"
            #include_with_pragmas "StoreyColorPaletteLit.hlsl"
            #include "Packages/com.triband.storey/Unity/Shaders/StoreyFragment.hlsl"
            ENDHLSL
        }
    }
}
