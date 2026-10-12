Shader "RotateRings/Ring 2D"
{
    Properties { [PerRendererData] _MainTex ("Texture", 2D) = "white" {} _Color ("Tint", Color) = (1,1,1,1) [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            CBUFFER_END
            float4 _RendererColor;
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            V Vert(A input) { V o; o.positionCS=TransformObjectToHClip(input.positionOS.xyz); o.uv=input.uv; o.color=input.color*_Color*_RendererColor; return o; }
            half4 Frag(V input):SV_Target { return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv)*input.color; }
            ENDHLSL
        }
    }
}
