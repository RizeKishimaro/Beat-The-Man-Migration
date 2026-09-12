Shader "BeatTheMan/SlotPromptIcon"
{
    Properties
    {
        [MainTexture] _MainTex("Icon", 2D) = "white" {}
        [MainColor] _Color("Color", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }
        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
            CBUFFER_END

            struct Attr { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct V2F { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            V2F vert(Attr v)
            {
                V2F o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(V2F i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half a = tex.a * _Color.a;
                clip(a - 0.05);
                return half4(_Color.rgb, a);
            }
            ENDHLSL
        }
    }
}
