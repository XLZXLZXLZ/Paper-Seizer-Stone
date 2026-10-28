Shader "PaperSeizerStone/Environment/VirtualProjection"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("Room Image", 2D) = "black" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Brightness ("Brightness", Float) = 1
        _OutsideColor ("Outside Image Color", Color) = (0.018, 0.014, 0.011, 1)
        _EdgeFeather ("Image Edge Feather", Range(0, 1)) = 0.025
        [HideInInspector] _RoomProjectionParams ("Projection Parameters", Vector) = (0.565, 1.003, 0.05, 0)
        [HideInInspector] _RoomImageRect ("Source Image Rect", Vector) = (0, 0, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4x4 _ProjectionWorldToLocal;
                float4 _RoomProjectionParams;
                float4 _RoomImageRect;
                half4 _Tint;
                half4 _OutsideColor;
                float _Brightness;
                float _EdgeFeather;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // The reference transform looks along positive local Z. It does not render.
                float3 projected = mul(_ProjectionWorldToLocal, float4(input.positionWS, 1.0)).xyz;
                float depth = max(projected.z, max(_RoomProjectionParams.z, 0.0001));
                float2 sourceUV = projected.xy / depth * _RoomProjectionParams.xy + 0.5;
                float2 uv = _RoomImageRect.xy + sourceUV * _RoomImageRect.zw;
                half3 color = _OutsideColor.rgb;

                // Outside rays keep a solid dark fill instead of stretching image borders.
                if (projected.z >= _RoomProjectionParams.z && all(uv >= 0.0) && all(uv <= 1.0))
                {
                    float2 edgeDistance = min(uv, 1.0 - uv);
                    float edge = min(edgeDistance.x, edgeDistance.y);
                    float coverage = _EdgeFeather > 0.0 ? smoothstep(0.0, _EdgeFeather, edge) : 1.0;
                    half3 image = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb;
                    color = lerp(color, image * _Tint.rgb * _Brightness, coverage);
                }

                return half4(color, 1.0);
            }

            half4 DepthFrag(Varyings input) : SV_Target
            {
                return 0;
            }

            half4 DepthNormalsFrag(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS) * IS_FRONT_VFACE(frontFace, 1.0, -1.0);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    return half4(PackFloat2To888(remappedOctNormalWS), 0.0);
                #else
                    return half4(normalWS, 0.0);
                #endif
            }
        ENDHLSL

        Pass
        {
            Name "VirtualProjection"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }

        // URP depth priming only draws color where this surface already wrote depth.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment DepthFrag
            ENDHLSL
        }

        // Renderers using SSAO request the normals prepass instead of DepthOnly.
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }
            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }

    Fallback Off
}
