Shader "PaperSeizerStone/MainMenu/PlateSurface"
{
    Properties
    {
        [MainTexture] _BaseMap ("Plate Artwork", 2D) = "white" {}
        [MainColor] _Tint ("Warm Bronze Tint", Color) = (0.97,0.94,0.90,1)
        _Saturation ("Saturation", Range(0,1.5)) = 0.78
        _Contrast ("Contrast", Range(0.5,1.5)) = 0.94
        _Brightness ("Brightness", Range(0,2)) = 0.92
        _BrushStrength ("Broad Brush Variation", Range(0,0.1)) = 0.025
        _AmbientStrength ("Ambient", Range(0,1.5)) = 0.7
        _AdditionalLightStrength ("Additional Lights", Range(0,1)) = 0.35
        _HighlightStrength ("Restrained Highlight", Range(0,0.15)) = 0.025
        _ShadowTint ("Warm Shadow", Color) = (0.80,0.74,0.65,1)
        _LightTint ("Soft Light", Color) = (1,0.97,0.91,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Back
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _Tint;
            float4 _ShadowTint;
            float4 _LightTint;
            float _Saturation;
            float _Contrast;
            float _Brightness;
            float _BrushStrength;
            float _AmbientStrength;
            float _AdditionalLightStrength;
            float _HighlightStrength;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "PlateForward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float fog : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fog = ComputeFogFactor(position.positionCS.z);
                return output;
            }

            float ThreeBands(float ndotl)
            {
                float middle = smoothstep(0.16, 0.28, ndotl);
                float light = smoothstep(0.59, 0.71, ndotl);
                return lerp(lerp(0.62, 0.82, middle), 1.0, light);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 textureColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                float luminance = dot(textureColor, float3(0.2126,0.7152,0.0722));
                float3 albedo = lerp(luminance.xxx, textureColor, _Saturation);
                albedo = max(0.0, (albedo - 0.5) * _Contrast + 0.5) * _Tint.rgb * _Brightness;
                // Broad, low-contrast variation only. The scene's existing print filter
                // already supplies the fine grain, so no second stipple pattern is added here.
                float brush = sin(input.uv.x * 15.3 + sin(input.uv.y * 7.1))
                    * sin(input.uv.y * 12.7 + 0.8);
                albedo *= 1.0 + brush * _BrushStrength;

                float3 normal = normalize(input.normalWS);
                Light main = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float ndotl = saturate(dot(normal, main.direction));
                float band = ThreeBands(ndotl);
                float shadow = lerp(0.58, 1.0, main.shadowAttenuation);
                float3 bandTint = lerp(_ShadowTint.rgb, _LightTint.rgb, smoothstep(0.12,0.8,ndotl));
                float3 ambient = max(SampleSH(normal), 0.0) * 0.22;
                // The source texture already contains its bronze modelling. Retain a
                // stable body tone and use live light for depth rather than crushing it twice.
                float3 lighting = 0.7 * _AmbientStrength + ambient
                    + main.color * (band * shadow * main.distanceAttenuation * 0.58) * bandTint;

                #if defined(_ADDITIONAL_LIGHTS)
                    uint lightCount = GetAdditionalLightsCount();
                    for (uint index = 0u; index < lightCount; ++index)
                    {
                        Light light = GetAdditionalLight(index, input.positionWS);
                        float diffuse = saturate(dot(normal, light.direction));
                        lighting += light.color * diffuse * light.distanceAttenuation
                            * light.shadowAttenuation * _AdditionalLightStrength * 0.35;
                    }
                #endif

                float3 view = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
                float3 halfVector = SafeNormalize(main.direction + view);
                float highlight = pow(saturate(dot(normal, halfVector)), 40.0)
                    * _HighlightStrength * ndotl * main.shadowAttenuation;
                float3 color = albedo * lighting + _LightTint.rgb * main.color * highlight;
                return half4(MixFog(color, input.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            // This pass only writes shadow depth; it does not sample shadow maps.
            // Keep the URP 12 bias calculation local instead of importing fragment
            // shadow sampling helpers (and their LerpWhiteTo dependency) into it.
            float3 _LightDirection;
            float3 _LightPosition;
            float4 _ShadowBias;
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 position = TransformObjectToWorld(input.positionOS.xyz);
                float3 normal = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition - position);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                float normalBias = (1.0 - saturate(dot(lightDirection, normal))) * _ShadowBias.y;
                position += lightDirection * _ShadowBias.x + normal * normalBias;
                output.positionCS = TransformWorldToHClip(position);
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return output;
            }
            half4 ShadowFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            half4 DepthFrag(Varyings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
