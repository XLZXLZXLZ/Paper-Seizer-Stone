Shader "PaperSeizerStone/MainMenu/BackdropGrade"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Sprite Tint", Color) = (1,1,1,1)
        _Tint ("Warm Grade", Color) = (1,0.985,0.965,1)
        _Saturation ("Saturation", Range(0,1.5)) = 0.94
        _Contrast ("Contrast", Range(0.5,1.5)) = 0.98
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _Flip ("Flip", Vector) = (1,1,1,1)
        [PerRendererData] _AlphaTex ("External Alpha", 2D) = "white" {}
        [PerRendererData] _EnableExternalAlpha ("Use External Alpha", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "CanUseSpriteAtlas"="True"
        }
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Name "BackdropGrade"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ ETC1_EXTERNAL_ALPHA
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_AlphaTex); SAMPLER(sampler_AlphaTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _Tint;
                float4 _RendererColor;
                float4 _Flip;
                float _Saturation;
                float _Contrast;
                float _EnableExternalAlpha;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                input.positionOS.xy *= _Flip.xy;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color * _Color * _RendererColor;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float4 source = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                #if defined(ETC1_EXTERNAL_ALPHA)
                    source.a = lerp(source.a, SAMPLE_TEXTURE2D(_AlphaTex, sampler_AlphaTex, input.uv).r, _EnableExternalAlpha);
                #endif
                float luminance = dot(source.rgb, float3(0.2126,0.7152,0.0722));
                float3 color = lerp(luminance.xxx, source.rgb, _Saturation);
                color = max(0.0, (color - 0.5) * _Contrast + 0.5);
                return half4(color * _Tint.rgb * input.color.rgb, source.a * _Tint.a * input.color.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
