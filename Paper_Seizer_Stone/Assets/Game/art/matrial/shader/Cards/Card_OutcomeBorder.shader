Shader "PaperSeizerStone/Cards/OutcomeBorder"
{
    Properties
    {
        _BorderColor ("Border Color", Color) = (0,0,0,0)
        _QuadSize ("Quad Size", Vector) = (0.84,1.21,0,0)
        _HalfSize ("Card Half Size and Corner Radius", Vector) = (0.37,0.555,0.055,0)
        _EdgeData ("Width Noise Amplitude Frequency Drift", Vector) = (0.024,0.003,55,0.08)
        _BorderSeed ("Card Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                float4 _BorderColor, _QuadSize, _HalfSize, _EdgeData;
                float _BorderSeed;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1,311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f*f*(3-2*f);
                return lerp(lerp(Hash(cell),Hash(cell+float2(1,0)),f.x),
                    lerp(Hash(cell+float2(0,1)),Hash(cell+1),f.x),f.y);
            }
            float4 Frag(Varyings input) : SV_Target
            {
                float2 p = (input.uv - 0.5) * _QuadSize.xy;
                float2 q = abs(p) - (_HalfSize.xy - _HalfSize.z);
                float distance = length(max(q,0)) + min(max(q.x,q.y),0) - _HalfSize.z;
                float noise = Noise(p * _EdgeData.z + _BorderSeed + _Time.y * _EdgeData.w);
                float edge = abs(distance - 0.009 + (noise - 0.5) * 2 * _EdgeData.y);
                float aa = max(fwidth(distance),0.0005);
                float ring = 1 - smoothstep(_EdgeData.x*0.5-aa,_EdgeData.x*0.5+aa,edge);
                // The broken ink pattern is stable per card, with only slow edge movement.
                float ink = lerp(0.18,1,smoothstep(0.2,0.55,Noise(p*32 + _BorderSeed*3.7)));
                return float4(_BorderColor.rgb, _BorderColor.a * ring * ink);
            }
            ENDHLSL
        }
    }
}
