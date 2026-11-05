Shader "PaperSeizerStone/MainMenu/ContactShadow"
{
    Properties
    {
        _ShadowColor ("Shadow Tint", Color) = (0.025,0.017,0.012,1)
        _Opacity ("Shadow Strength", Range(0,1)) = 0.38
        _Softness ("Soft Edge", Range(0.05,0.7)) = 0.28
        _CastOffset ("Cast Offset", Vector) = (0.07,-0.075,0,0)
        _ContactStrength ("Contact Strength", Range(0,1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _ShadowColor;
                float4 _CastOffset;
                float _Opacity;
                float _Softness;
                float _ContactStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float softness = clamp(_Softness, 0.05, 0.7);
                float contact = 1.0 - smoothstep(0.68 - softness * 0.3, 0.89, length(p));
                float cast = 1.0 - smoothstep(1.0 - softness, 1.0, length(p - _CastOffset.xy));
                float alpha = saturate(_Opacity) * (1.0 - (1.0 - cast * 0.75)
                    * (1.0 - contact * saturate(_ContactStrength)));
                // A still, restrained edge variation fits the illustrated tabletop.
                float grain = frac(sin(dot(floor(input.uv * 240.0), float2(12.9898,78.233))) * 43758.5453);
                alpha *= lerp(0.96, 1.0, grain);
                return half4(_ShadowColor.rgb, alpha * _ShadowColor.a);
            }
            ENDHLSL
        }
    }
}
