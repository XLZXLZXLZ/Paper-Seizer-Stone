// Main-light data only. Toon bands, brush variation and highlights live in Shader Graph.
#ifndef PAPER_SEIZER_STONE_TOON_MAIN_LIGHT_INCLUDED
#define PAPER_SEIZER_STONE_TOON_MAIN_LIGHT_INCLUDED

#if !defined(SHADERGRAPH_PREVIEW)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#endif

void ToonMainLight_float(float3 PositionWS, out float3 Direction, out float3 Color, out float ShadowAttenuation)
{
    #if defined(SHADERGRAPH_PREVIEW)
        Direction = normalize(float3(0.4, 0.75, 0.5));
        Color = float3(1.0, 1.0, 1.0);
        ShadowAttenuation = 1.0;
    #else
        #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
            float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(PositionWS));
        #else
            float4 shadowCoord = TransformWorldToShadowCoord(PositionWS);
        #endif

        Light mainLight = GetMainLight(shadowCoord);
        Direction = mainLight.direction;
        Color = mainLight.color;
        ShadowAttenuation = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
    #endif
}

void ToonMainLight_half(half3 PositionWS, out half3 Direction, out half3 Color, out half ShadowAttenuation)
{
    float3 lightDirection;
    float3 lightColor;
    float shadow;
    ToonMainLight_float((float3)PositionWS, lightDirection, lightColor, shadow);
    Direction = (half3)lightDirection;
    Color = (half3)lightColor;
    ShadowAttenuation = (half)shadow;
}

#endif
