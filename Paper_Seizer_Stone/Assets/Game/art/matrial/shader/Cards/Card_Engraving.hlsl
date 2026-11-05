#ifndef PSS_CARD_ENGRAVING_INCLUDED
#define PSS_CARD_ENGRAVING_INCLUDED

float PSS_TagHash(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float PSS_TagNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(PSS_TagHash(i), PSS_TagHash(i + float2(1,0)), f.x),
                lerp(PSS_TagHash(i + float2(0,1)), PSS_TagHash(i + 1), f.x), f.y);
}

// Texture samples and the supplied tint are in the project's working color space.
// Classify printed ink in display space, so grey-green paper is not treated as ink in Linear.
float3 PSS_TagDisplayColor(float3 color)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return max(color, 0.0);
#else
    color = max(color, 0.0);
    return lerp(color * 12.92, 1.055 * pow(max(color, 0.000001), 1.0 / 2.4) - 0.055,
                step(0.0031308, color));
#endif
}

float3 PSS_TagWorkingColor(float3 color)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return color;
#else
    return lerp(color / 12.92, pow((color + 0.055) / 1.055, 2.4), step(0.04045, color));
#endif
}

float PSS_TagFineNoise(float2 p)
{
    float footprint = max(length(ddx(p)), length(ddy(p)));
    return lerp(0.5, PSS_TagNoise(p), 1.0 - smoothstep(0.35, 1.0, footprint));
}

// Fade unresolved fine strokes rather than turning them into a solid tint at small card sizes.
float PSS_TagPrintLine(float phase, float width)
{
    float footprint = max(fwidth(phase), 0.0001);
    float distanceToLine = abs(frac(phase + 0.5) - 0.5);
    return (1.0 - smoothstep(width - footprint * 0.5, width + footprint * 0.5, distanceToLine))
        * (1.0 - smoothstep(0.25, 0.65, footprint));
}

// Fixed foil pieces: x is the fracture distance, y is a stable per-piece reflectance.
float2 PSS_TagFoilCells(float2 p)
{
    float2 cell = floor(p), f = frac(p);
    float first = 8.0, second = 8.0;
    float2 nearestCell = cell;
    [unroll] for (int y = -1; y <= 1; y++)
    [unroll] for (int x = -1; x <= 1; x++)
    {
        float2 q = float2(x,y);
        float2 jitter = float2(PSS_TagHash(cell + q), PSS_TagHash(cell + q + 17.0));
        float distanceToCell = length(q + jitter - f);
        if (distanceToCell < first)
        {
            second = first;
            first = distanceToCell;
            nearestCell = cell + q;
        }
        else second = min(second, distanceToCell);
    }
    return float2(second - first, PSS_TagHash(nearestCell + 31.0));
}

// Only the reflective finish changes over time. Paper grain, ink, cracks and print registration
// remain locked to card UVs and the owned-card seed. Motion=(cycles/second, amount, reserved, reserved).
void CardEngraving_float(float4 In, float2 UV, float4 Data, float4 Tint,
    float4 Rect, float3 View, float4 Motion, out float4 Out)
{
    Out = In;
    if (Data.x < 0.5 || Data.y <= 0.0) return;

    float2 border = min(UV - Rect.xy, Rect.zw - UV);
    float edgeWidth = max(0.018, max(fwidth(UV.x), fwidth(UV.y)) * 1.5);
    float mask = smoothstep(0.0, edgeWidth, min(border.x, border.y));
    // Keep neighboring pixels active for the derivative-based grain and stroke filtering below.
    // Pixels outside the artwork region retain In through the final zero mask.

    float density = max(0.3, Data.w);
    float2 p = (UV * float2(1.0, 1.5) + Data.z * float2(0.73, 1.13)) * density;
    float2 q = (UV - (Rect.xy + Rect.zw) * 0.5) / max(Rect.zw - Rect.xy, 0.01);
    float grain = PSS_TagFineNoise(p * 80.0);
    float cloud = PSS_TagNoise(p * 8.0) * 0.7 + PSS_TagNoise(p * 21.0) * 0.3;
    float luminance = dot(PSS_TagDisplayColor(In.rgb), float3(0.2126, 0.7152, 0.0722));
    float ink = 1.0 - smoothstep(0.16, 0.36, luminance);
    float paper = 1.0 - ink;
    float3 result = In.rgb;

    if (Data.x < 1.5)
    {
        // Heavy rust-red overprint on the existing silhouette, with worn holes and coarse pigment.
        float wornInk = smoothstep(0.12, 0.31, cloud + grain * 0.18);
        float edgeBleed = saturate(fwidth(ink) * 0.45) * paper;
        float imprint = saturate(ink * wornInk + edgeBleed * cloud);
        float3 pigment = lerp(Tint.rgb * 0.72, PSS_TagWorkingColor(float3(0.57, 0.23, 0.105)),
                              smoothstep(0.36, 0.76, cloud) * 0.62);
        pigment *= 0.82 + grain * 0.30;
        result = lerp(In.rgb, pigment, imprint * 0.93);
        float scratches = PSS_TagPrintLine((p.x * 0.45 + p.y) * 35.0 + cloud * 0.8, 0.025);
        result = lerp(result, In.rgb * 0.7, scratches * ink * 0.28);
    }
    else if (Data.x < 2.5)
    {
        // Broken metal leaf follows the printed object; the paper stays visible between fragments.
        float2 cells = PSS_TagFoilCells(p * 14.0);
        float gapAA = max(fwidth(cells.x), 0.009);
        float intact = smoothstep(0.018, 0.045 + gapAA, cells.x);
        float foil = ink * smoothstep(0.25, 0.56, cloud) * (0.60 + grain * 0.40);
        float3 viewDirection = normalize(View + float3(0, 0.0001, 0));
        float viewOffset = dot(viewDirection.xz, float2(0.65, 0.35));
        float alongCard = dot(q, float2(0.82, 0.38));
        float staticGlint = 1.0 - smoothstep(0.05, 0.31, abs(alongCard - viewOffset));
        float motion = Motion.x > 0.0 ? saturate(Motion.y) : 0.0;
        float phase = _Time.y * max(Motion.x, 0.0) + Data.z * 0.071;
        float movingGlint = 1.0 - smoothstep(0.055, 0.24,
            abs(alongCard - sin(phase * 6.2831853) * 0.74));
        float reflectance = saturate(0.17 + cells.y * 0.37 + staticGlint * 0.30
                                     + movingGlint * motion * 1.4);
        float3 gold = lerp(Tint.rgb * 0.55, PSS_TagWorkingColor(float3(0.98, 0.86, 0.53)), reflectance);
        result = lerp(In.rgb, gold, foil * intact * 0.96);
        result = lerp(result, In.rgb * 0.65, (1.0 - intact) * foil * 0.7);
    }
    else if (Data.x < 3.5)
    {
        // A translucent, gently pooled wax finish, not an opaque yellow wash.
        float coatingDistance = max(abs(q.x), abs(q.y) * 0.96) + (cloud - 0.5) * 0.025;
        float coating = 1.0 - smoothstep(0.40, 0.49, coatingDistance);
        float rim = smoothstep(0.31, 0.41, coatingDistance) * coating;
        float3 amber = PSS_TagWorkingColor(float3(1.0, 0.88, 0.64));
        float3 transparentWax = In.rgb * lerp(float3(1,1,1), amber, 0.13 + cloud * 0.055);
        result = lerp(In.rgb, transparentWax, coating);
        result = lerp(result, In.rgb * lerp(float3(1,1,1), Tint.rgb, 0.24), rim * 0.30);
        float3 viewDirection = normalize(View + float3(0, 0.0001, 0));
        float alongCard = dot(q, float2(0.48, 0.80));
        float viewOffset = dot(viewDirection.xz, float2(0.38, 0.55));
        float staticGlaze = 1.0 - smoothstep(0.04, 0.30, abs(alongCard - viewOffset));
        float motion = Motion.x > 0.0 ? saturate(Motion.y) : 0.0;
        float phase = _Time.y * max(Motion.x, 0.0) + Data.z * 0.071;
        float movingGlaze = 1.0 - smoothstep(0.09, 0.32,
            abs(alongCard - sin(phase * 6.2831853) * 0.70));
        float glaze = staticGlaze * 0.065 + movingGlaze * motion * 0.30;
        result += PSS_TagWorkingColor(float3(0.93, 0.88, 0.72))
            * glaze * coating * (0.55 + paper * 0.45) * (0.82 + cloud * 0.18);
    }
    else if (Data.x < 4.5)
    {
        // Bone-white relief against a dark etched plate, retaining the object's recognizable shape.
        float3 plate = lerp(Tint.rgb * 0.72, PSS_TagWorkingColor(float3(0.16, 0.14, 0.19)), cloud * 0.35);
        float3 relief = PSS_TagWorkingColor(float3(0.85, 0.82, 0.72)) * (0.90 + grain * 0.10);
        result = lerp(plate, relief, ink);
        float hatch = PSS_TagPrintLine((p.x * 0.72 + p.y) * 42.0, 0.045);
        result = lerp(result, PSS_TagWorkingColor(float3(0.56, 0.53, 0.47)), hatch * 0.10);
    }
    else
    {
        // Two slightly misregistered, mirrored print passes. Seed changes their phase and wear;
        // density changes spacing while keeping the symmetry axis at the center of the card.
        float2 mirrored = float2(abs(q.x), q.y) * density;
        float seedPhase = frac(Data.z * 0.173);
        float wear = PSS_TagNoise(mirrored * float2(9.0, 14.0) + Data.z);
        float phase = (mirrored.y + mirrored.x * 0.58) * 13.0 + seedPhase + (wear - 0.5) * 0.10;
        float firstPass = PSS_TagPrintLine(phase, 0.040);
        float secondPass = PSS_TagPrintLine(phase + 0.145 + seedPhase * 0.035, 0.030);
        float printWear = smoothstep(0.13, 0.33, wear) * 0.82 + 0.18;
        result = lerp(In.rgb, Tint.rgb, firstPass * paper * printWear * 0.54);
        result = lerp(result, PSS_TagWorkingColor(float3(0.83, 0.79, 0.65)),
                      secondPass * paper * printWear * 0.36);
        result = lerp(result, lerp(In.rgb, Tint.rgb, 0.28), ink * (0.70 + grain * 0.30));
    }
    Out.rgb = lerp(In.rgb, max(result, 0.0), mask * saturate(Data.y));
}
#endif
