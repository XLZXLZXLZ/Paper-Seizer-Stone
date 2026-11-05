Shader "PaperSeizerStone/MainMenu/HoverParticle"
{
    Properties
    {
        _Intensity ("Brightness", Range(0, 5)) = 1.6
        _StarStrength ("Fine Star Strength", Range(0, 0.35)) = 0.12
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+20" }
        Pass
        {
            Name "HoverParticle"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _StarStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float radiusSquared = dot(p, p);
                float edge = 1.0 - smoothstep(0.62, 1.0, radiusSquared);
                float glow = exp2(-radiusSquared * 9.0);
                // Restrained crossed rays keep tiny points readable without a texture.
                float rays = exp2(-abs(p.x) * 46.0 - abs(p.y) * 7.0)
                    + exp2(-abs(p.y) * 46.0 - abs(p.x) * 7.0);
                half coverage = saturate((glow + rays * _StarStrength) * edge);
                return half4(input.color.rgb * _Intensity, input.color.a * coverage);
            }
            ENDHLSL
        }
    }
}
