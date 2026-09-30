// The glass panes of LOD0: transparent, no depth write, no shadows (the prototype's MAT.glass).
Shader "Storey/Glass"
{
    Properties
    {
        _StoreyAlpha ("Opacity", Range(0, 1)) = 0.5
        _StoreySmoothness ("Smoothness", Range(0, 1)) = 0.92
        _StoreyMetallic ("Metallic", Range(0, 1)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        HLSLINCLUDE
        #define STOREY_GLASS
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #include "StoreyLit.hlsl"
            ENDHLSL
        }
    }
}
