Shader "PaperSeizerStone/Effects/BrazierFire"
{
    Properties
    {
        [HDR] _FlameColor ("Outer Flame", Color) = (1.3,0.26,0.025,1)
        [HDR] _CoreColor ("Bright Core", Color) = (1.8,1.15,0.28,1)
        _Intensity ("Emission Intensity", Range(0,8)) = 2.8
        _Speed ("Rise Speed", Range(0.1,5)) = 1.8
        _Boost ("Burning Card Boost", Range(0,2)) = 0
        _Height ("Spark Travel Height", Float) = 0.65
        [Toggle] _SparkMode ("Floating Sparks", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "RenderType"="Transparent"
            "Queue"="Transparent+10"
            "IgnoreProjector"="True"
        }
        Cull Off
        ZWrite Off
        ZTest LEqual
        Blend SrcAlpha One

        Pass
        {
            Name "BrazierFire"
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FlameColor;
                float4 _CoreColor;
                float _Intensity;
                float _Speed;
                float _Boost;
                float _Height;
                float _SparkMode;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float phase : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float2 w = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1,0)), w.x),
                    lerp(Hash(cell + float2(0,1)), Hash(cell + 1.0), w.x), w.y);
            }

            float Flow(float2 p)
            {
                return Noise(p) * 0.65 + Noise(p * 2.11 + 7.4) * 0.35;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 position = input.positionOS.xyz;
                float time = _Time.y * max(0.01, _Speed);
                float boost = max(0.0, _Boost);
                output.uv = input.uv;
                output.phase = 0.0;
                if (_SparkMode > 0.5)
                {
                    // Each quad encodes its shared seed in the integer part of U.
                    // Fractional U still describes the soft round sprite within the quad.
                    float seed = floor(input.uv.x);
                    float life = frac(time * 0.4 + seed * 0.13);
                    position.y += life * max(0.05, _Height) * (1.0 + boost * 0.4);
                    position.x += sin(life * 4.2 + seed * 1.7) * 0.035 * life;
                    position.z += cos(life * 3.7 + seed * 2.3) * 0.035 * life;
                    output.uv.x = frac(input.uv.x);
                    output.phase = life;
                }
                else
                {
                    float height = saturate(input.uv.y);
                    position.y *= 1.0 + boost * 0.45;
                    position.xz *= 1.0 + boost * 0.08;
                    float tip = height * height;
                    position.x += sin(time * 2.3 + position.z * 9.1) * 0.035 * tip;
                    position.z += cos(time * 1.9 + position.x * 8.7) * 0.025 * tip;
                }
                output.positionCS = TransformObjectToHClip(position);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float boost = max(0.0, _Boost);
                float intensity = max(0.0, _Intensity) * (1.0 + boost * 0.65);
                if (_SparkMode > 0.5)
                {
                    float2 centered = input.uv * 2.0 - 1.0;
                    float radius = length(centered);
                    float dotShape = 1.0 - smoothstep(0.15, 1.0, radius);
                    float lifeFade = smoothstep(0.0, 0.1, input.phase)
                        * (1.0 - smoothstep(0.45, 1.0, input.phase));
                    float alpha = dotShape * lifeFade * 0.72;
                    clip(alpha - 0.002);
                    float3 color = lerp(_CoreColor.rgb, _FlameColor.rgb, input.phase) * intensity;
                    return half4(color, alpha);
                }

                float2 uv = saturate(input.uv);
                float time = _Time.y * max(0.01, _Speed);
                float rise = Flow(float2(uv.x * 4.2, uv.y * 5.4 - time * 1.8));
                float detail = Flow(float2(uv.x * 8.0 + 3.1, uv.y * 9.5 - time * 2.7));
                float bend = sin(time * 2.0 + uv.y * 5.5) * 0.065 * uv.y
                    + (rise - 0.5) * 0.13 * uv.y;
                float x = abs(uv.x - 0.5 + bend);
                float width = 0.43 * pow(max(0.0, 1.0 - uv.y), 0.72) + 0.012;
                float edge = 1.0 - smoothstep(width * 0.56, width, x);
                float tip = 1.0 - smoothstep(0.68, 1.0, uv.y + (rise - 0.45) * 0.34);
                float baseFade = smoothstep(0.0, 0.065, uv.y);
                float body = smoothstep(0.14, 0.82, rise * 0.65 + detail * 0.35 + (1.0 - uv.y) * 0.25);
                // Six crossing planes share the volume. A restrained per-plane opacity
                // leaves visible flame tongues instead of adding into an opaque white block.
                float alpha = edge * tip * baseFade * body * 0.18;
                clip(alpha - 0.002);
                float coreWidth = max(0.012, width * 0.48);
                float core = (1.0 - smoothstep(0.0, coreWidth, x))
                    * (1.0 - smoothstep(0.18, 0.72, uv.y + rise * 0.13));
                float3 color = lerp(_FlameColor.rgb, _CoreColor.rgb, core) * intensity;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
