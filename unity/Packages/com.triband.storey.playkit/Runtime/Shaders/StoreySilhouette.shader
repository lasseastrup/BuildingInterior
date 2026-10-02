// The play kit's silhouette (SPEC §5): the character drawn a second time, flat, only where something stands in front of
// it (ZTest Greater), so the player is never lost behind a wall or a building in the way.
Shader "Storey/Silhouette"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.49, 0.2, 0.85)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+50" "RenderType" = "Transparent" }
        Pass
        {
            Name "Silhouette"
            ZTest Greater
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float rim : TEXCOORD0; };
            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 p = TransformObjectToWorld(IN.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(p);
                OUT.rim = 1.0 - saturate(dot(normalize(GetWorldSpaceViewDir(p)), n));
                return OUT;
            }
            half4 Frag(Varyings IN) : SV_Target
            {
                return half4(_Color.rgb, _Color.a * lerp(0.55, 1.0, IN.rim));
            }
            ENDHLSL
        }
    }
}
