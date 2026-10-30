Shader "Hidden/PaperSeizerStone/StyleFilter"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _StyleSourceTexelSize;
            float4 _Style, _Print, _Surface, _Grade, _Vignette;
            float4 _InkColor, _ShadowColor, _HighlightColor, _VignetteColor;
            float _SplitTone;
            float Luma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
            float3 ToDisplay(float3 c)
            {
                #if defined(UNITY_COLORSPACE_GAMMA)
                return c;
                #else
                return LinearToSRGB(max(c, 0));
                #endif
            }
            float3 ToOutput(float3 c)
            {
                #if defined(UNITY_COLORSPACE_GAMMA)
                return c;
                #else
                return SRGBToLinear(max(c, 0));
                #endif
            }
            float3 SampleDisplay(float2 uv)
            {
                float2 edge = abs(_StyleSourceTexelSize.xy) * 0.5;
                return ToDisplay(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, clamp(uv, edge, 1-edge)).rgb);
            }
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }
            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f*f*(3-2*f);
                return lerp(lerp(Hash(cell), Hash(cell+float2(1,0)), f.x),
                            lerp(Hash(cell+float2(0,1)), Hash(cell+1), f.x), f.y);
            }
            // Four quadrant Kuwahara: select the mean with the least color variance.
            float3 Oil(float2 uv)
            {
                int radius = (int)_Surface.y;
                float bestVariance = 1e20;
                float3 best = 0;
                [unroll] for (int quadrant = 0; quadrant < 4; quadrant++)
                {
                    float2 direction = float2((quadrant & 1) ? 1 : -1, (quadrant & 2) ? 1 : -1);
                    float3 sum = 0, squareSum = 0;
                    [loop] for (int y = 0; y <= radius; y++)
                    [loop] for (int x = 0; x <= radius; x++)
                    {
                        float3 c = SampleDisplay(uv + float2(x,y)*direction*abs(_StyleSourceTexelSize.xy));
                        sum += c; squareSum += c*c;
                    }
                    float count = (radius+1)*(radius+1);
                    float3 mean = sum/count;
                    float variance = dot(max(0, squareSum/count-mean*mean), float3(1,1,1));
                    if (variance < bestVariance) { bestVariance = variance; best = mean; }
                }
                return best;
            }
            float4 StyleFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float3 c = ToDisplay(source.rgb);
                [branch] if (_Surface.x > 0.0001) c = lerp(c, Oil(uv), _Surface.x);
                c = ToDisplay(ToOutput(c) * exp2(_Grade.x));
                c *= float3(1 + _Grade.w*0.1, 1, 1 - _Grade.w*0.1);
                c = (c-0.5)*(1+_Grade.y)+0.5;
                c = lerp(Luma(c).xxx, c, 1+_Grade.z);
                float lum = saturate(Luma(c));
                c *= lerp(1.0.xxx, lerp(_ShadowColor.rgb, _HighlightColor.rgb, smoothstep(0.1,0.9,lum)), _SplitTone);
                // Pattern sizes use a 1080p reference so changing Game view size preserves the style.
                float2 pixel = uv * float2(_ScreenParams.x/_ScreenParams.y, 1) * 1080;
                [branch] if (_Style.y > 0.0001)
                {
                    float3 bounded = saturate(c);
                    float noise = (Hash(floor(pixel/max(1,_Print.w)))-0.5)*_Style.w;
                    float levels = max(3,_Style.z)-1;
                    float3 quantized = floor(saturate(bounded+noise/levels)*levels+0.5)/levels;
                    c = lerp(c, quantized, _Style.y);
                }
                [branch] if (_Print.x > 0.0001)
                {
                    float s = sin(_Print.z), co = cos(_Print.z);
                    float2 grid = float2(pixel.x*co-pixel.y*s, pixel.x*s+pixel.y*co)/max(3,_Print.y);
                    float distanceToDot = length(frac(grid)-0.5);
                    float darkness = saturate(1-Luma(c));
                    float radius = sqrt(darkness)*0.70;
                    float aa = max(fwidth(distanceToDot), 0.01);
                    float coverage = 1-smoothstep(radius-aa, radius+aa, distanceToDot);
                    coverage *= smoothstep(0, 0.06, darkness);
                    c = lerp(c, _InkColor.rgb, coverage*_Print.x);
                }
                [branch] if (_Surface.z > 0.0001)
                {
                    float2 grainUV = pixel/max(1,_Surface.w);
                    float grain = (Noise(grainUV)-0.5)*1.3+(Hash(floor(grainUV*2))-0.5)*0.35;
                    c += grain*_Surface.z;
                }
                float2 offset = (uv-_Vignette.zw)*2;
                offset.x *= _ScreenParams.x/_ScreenParams.y;
                float vignette = smoothstep(0.35, 0.35+max(0.05,_Vignette.y)*1.7, length(offset));
                c = lerp(c, _VignetteColor.rgb, vignette*_Vignette.x);
                return float4(lerp(source.rgb, ToOutput(saturate(c)), _Style.x), source.a);
            }
            float4 CopyFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, input.texcoord);
            }
        ENDHLSL
        Pass
        {
            Name "StyleFilter"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment StyleFragment
            ENDHLSL
        }
        Pass
        {
            Name "CopyCameraColor"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment CopyFragment
            ENDHLSL
        }
    }
}
