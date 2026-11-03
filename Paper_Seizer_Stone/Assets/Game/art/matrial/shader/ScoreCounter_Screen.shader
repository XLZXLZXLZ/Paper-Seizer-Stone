Shader "PaperSeizerStone/Props/ScoreCounterScreen"
{
    Properties
    {
        [MainTexture] _BaseMap("原屏幕贴图", 2D) = "white" {}
        [MainColor] _BaseColor("屏幕底色", Color) = (1, 1, 1, 1)
        [HDR] _HealthyColor("生命正常", Color) = (0.15, 1.15, 0.44, 1)
        [HDR] _DamagedColor("生命受损", Color) = (1.6, 0.38, 0.065, 1)
        _EmissionStrength("发光强度", Range(0, 4)) = 1
        _LiquidStrength("液体填充强度", Range(0, 1)) = 0.75
        _WaveSpeed("液面速度", Range(0, 10)) = 2.3
        _WaveHeight("液面波幅", Range(0, 0.08)) = 0.028
        _PanelInset("保留边框宽度", Range(0, 0.25)) = 0.085
        _DiamondRadius("中心棱形大小", Range(0.05, 0.45)) = 0.31
        _EnemyDiamondCenter("左侧棱形中心 XY", Vector) = (0.5, 0.5, 0, 0)
        _PlayerDiamondCenter("右侧棱形中心 XY", Vector) = (0.5, 0.5, 0, 0)
        [HideInInspector] _EnemyFill("敌方比分填充", Range(0, 1)) = 0
        [HideInInspector] _PlayerFill("玩家比分填充", Range(0, 1)) = 0
        [HideInInspector] _EnemyDamaged("敌方受损", Range(0, 1)) = 0
        [HideInInspector] _PlayerDamaged("玩家受损", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent+10" }
        Pass
        {
            Name "ScoreCounterDisplay"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _BaseColor, _HealthyColor, _DamagedColor;
            float4 _EnemyDiamondCenter, _PlayerDiamondCenter;
            float _EmissionStrength, _LiquidStrength, _WaveSpeed, _WaveHeight, _PanelInset, _DiamondRadius;
            float _EnemyFill, _PlayerFill, _EnemyDamaged, _PlayerDamaged;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 atlasUV : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 atlasUV : TEXCOORD0;
            };

            Varyings Vertex(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.atlasUV = TRANSFORM_TEX(input.atlasUV, _BaseMap);
                return output;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float3 original = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.atlasUV).rgb * _BaseColor.rgb;
                // Calibrate directly against the painted atlas so the motif stays
                // aligned even when the FBX importer omits the secondary UV set.
                float2 atlas = input.atlasUV;
                float playerSide = 1.0 - step(0.28, atlas.x);
                float2 enemyUV = float2(dot(atlas, float2(-0.51691, 5.52370)) - 1.53986,
                    dot(atlas, float2(-6.17507, -0.56955)) + 3.49226);
                float2 playerUV = float2(dot(atlas, float2(0.66081, 5.61416)) - 3.81610,
                    dot(atlas, float2(-6.17131, 0.54631)) + 0.76817);
                float2 uv = lerp(enemyUV, playerUV, playerSide);
                float2 center = lerp(_EnemyDiamondCenter.xy, _PlayerDiamondCenter.xy, playerSide);
                float fill = saturate(lerp(_EnemyFill, _PlayerFill, playerSide));
                float damaged = saturate(lerp(_EnemyDamaged, _PlayerDamaged, playerSide));
                float3 glow = lerp(_HealthyColor.rgb, _DamagedColor.rgb, damaged);

                float2 fromCenter = uv - center;
                float2 centerDistance = abs(fromCenter);
                float insidePanel = smoothstep(_PanelInset, _PanelInset + 0.012,
                    min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y)));
                // Follow the pointed, concave four-ray silhouette of the original motif.
                float motif = pow(centerDistance.x / _DiamondRadius, 0.72) + pow(centerDistance.y / _DiamondRadius, 0.72);
                float diamond = (1.0 - smoothstep(0.97, 1.04, motif)) * insidePanel;
                float originalDetail = dot(original, float3(0.2126, 0.7152, 0.0722));
                float coreGlow = diamond * lerp(0.48, 1.12, smoothstep(0.02, 0.24, originalDetail));

                // Fill each quadrant in turn, with a flowing surface inside the active quadrant.
                float right = step(0.0, fromCenter.x);
                float upper = step(0.0, fromCenter.y);
                float section = upper * 2.0 + right;
                float sectionFill = saturate(fill * 4.0 - section);
                float lowerY = lerp(_PanelInset, center.y, upper);
                float upperY = lerp(center.y, 1.0 - _PanelInset, upper);
                float phase = _Time.y * _WaveSpeed;
                float wave = sin(uv.x * 31.0 + phase) * _WaveHeight
                    + sin(uv.x * 58.0 - phase * 1.4) * (_WaveHeight * 0.35);
                float surface = lerp(lowerY, upperY, sectionFill)
                    + wave * saturate(min(sectionFill, 1.0 - sectionFill) * 5.0);
                float quadrantMask = smoothstep(0.009, 0.024, min(centerDistance.x, centerDistance.y))
                    * insidePanel * (1.0 - smoothstep(0.0, 0.8, diamond));
                float partial = (1.0 - smoothstep(surface - 0.006, surface + 0.006, uv.y));
                float liquid = lerp(partial, 1.0, step(0.999, sectionFill))
                    * step(0.0001, sectionFill) * quadrantMask;
                float rim = (1.0 - smoothstep(0.002, 0.013, abs(surface - uv.y)))
                    * step(0.001, sectionFill) * (1.0 - step(0.999, sectionFill)) * quadrantMask;
                float3 light = glow * (coreGlow + _LiquidStrength * (liquid * (0.22 + originalDetail * 0.8) + rim * 0.38));
                return half4(light * _EmissionStrength, 0);
            }
            ENDHLSL
        }
    }
}
