// URP 12 Forward additional-light sampling for the editable toon graphs.
// Material colour, texture, strength and surface masks remain in Shader Graph.
#ifndef PAPER_SEIZER_STONE_TOON_ADDITIONAL_LIGHTS_INCLUDED
#define PAPER_SEIZER_STONE_TOON_ADDITIONAL_LIGHTS_INCLUDED

#if !defined(SHADERGRAPH_PREVIEW)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

void PSS_AccumulateAdditionalToonLight(
    Light light,
    float3 normalWS,
    float3 viewDirectionWS,
    float shadowThreshold,
    float lightThreshold,
    float bandSoftness,
    float highlightThreshold,
    float highlightSoftness,
    inout float3 diffuse,
    inout float3 specular)
{
    float ndotl = saturate(dot(normalWS, (float3)light.direction));
    float soft = max(bandSoftness, 0.001);
    float middleEdge = saturate(shadowThreshold);
    float brightEdge = max(saturate(lightThreshold), middleEdge + 0.001);
    float middleBand = smoothstep(middleEdge - soft, middleEdge + soft, ndotl);
    float brightBand = smoothstep(brightEdge - soft, brightEdge + soft, ndotl);

    // Keep three readable value planes while softening only the terminator.
    float diffuseBand = lerp(0.15, 0.6, middleBand);
    diffuseBand = lerp(diffuseBand, 1.0, brightBand);
    diffuseBand *= smoothstep(0.0, soft, ndotl);

    float3 halfDirection = SafeNormalize((float3)light.direction + viewDirectionWS);
    float ndoth = saturate(dot(normalWS, halfDirection));
    float specularSoft = max(highlightSoftness, 0.001);
    float highlightBand = smoothstep(highlightThreshold - specularSoft,
                                    highlightThreshold + specularSoft, ndoth);

    // URP folds point range falloff and spot cone attenuation into this term.
    float attenuation = light.distanceAttenuation * light.shadowAttenuation;
    float3 radiance = (float3)light.color * attenuation;
    diffuse += radiance * diffuseBand;
    specular += radiance * highlightBand * ndotl;
}
#endif

void ToonAdditionalLights_float(
    float3 PositionWS,
    float3 NormalWS,
    float3 ViewDirectionWS,
    float ShadowThreshold,
    float LightThreshold,
    float BandSoftness,
    float HighlightThreshold,
    float HighlightSoftness,
    out float3 Diffuse,
    out float3 Specular)
{
    Diffuse = float3(0.0, 0.0, 0.0);
    Specular = float3(0.0, 0.0, 0.0);

    // Preview already has the original main-light fallback. Do not add a
    // second artificial lamp. Disabled additional-light quality modes stay 0.
    #if !defined(SHADERGRAPH_PREVIEW) && (defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX))
        float3 normalWS = SafeNormalize(NormalWS);
        float3 viewDirectionWS = SafeNormalize(ViewDirectionWS);
        uint renderingLayers = GetMeshRenderingLightLayer();
        uint lightCount = (uint)GetAdditionalLightsCount();
        half4 shadowMask = half4(1.0, 1.0, 1.0, 1.0);

        // Current project uses Forward. Explicit per-object loops avoid the
        // clustered LIGHT_LOOP macro's implicit InputData dependency.
        #if _USE_WEBGL1_LIGHTS
            for (uint lightIndex = 0u; lightIndex < _WEBGL1_MAX_LIGHTS; ++lightIndex)
            {
                if (lightIndex >= lightCount) break;
        #else
            for (uint lightIndex = 0u; lightIndex < lightCount; ++lightIndex)
            {
        #endif
                // The shadow-mask overload samples real-time point/spot
                // shadows and cookies; the two-argument overload does not.
                Light light = GetAdditionalLight(lightIndex, PositionWS, shadowMask);
                if (IsMatchingLightLayer(light.layerMask, renderingLayers))
                {
                    PSS_AccumulateAdditionalToonLight(light, normalWS, viewDirectionWS,
                        ShadowThreshold, LightThreshold, BandSoftness,
                        HighlightThreshold, HighlightSoftness, Diffuse, Specular);
                }
            }
    #endif
}

void ToonAdditionalLights_half(
    half3 PositionWS,
    half3 NormalWS,
    half3 ViewDirectionWS,
    half ShadowThreshold,
    half LightThreshold,
    half BandSoftness,
    half HighlightThreshold,
    half HighlightSoftness,
    out half3 Diffuse,
    out half3 Specular)
{
    float3 diffuse;
    float3 specular;
    ToonAdditionalLights_float((float3)PositionWS, (float3)NormalWS, (float3)ViewDirectionWS,
        (float)ShadowThreshold, (float)LightThreshold, (float)BandSoftness,
        (float)HighlightThreshold, (float)HighlightSoftness, diffuse, specular);
    Diffuse = (half3)diffuse;
    Specular = (half3)specular;
}

#endif
