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

// All marks are locked to card UVs. No time, animation or emission is involved.
void CardEngraving_float(float4 In, float2 UV, float4 Data, float4 Tint,
    float4 Rect, float3 View, out float4 Out)
{
    Out = In;
    if (Data.x < 0.5 || Data.y <= 0.0) return;
    float2 border = min(UV - Rect.xy, Rect.zw - UV);
    float mask = smoothstep(0.0, 0.035, min(border.x, border.y));
    if (mask <= 0.0) return;
    float2 p = (UV * float2(1.0, 1.5) + Data.z) * max(0.3, Data.w);
    float grain = PSS_TagNoise(p * 125.0);
    float cloud = PSS_TagNoise(p * 9.0) * 0.65 + PSS_TagNoise(p * 27.0) * 0.35;
    float ink = 1.0 - smoothstep(0.12, 0.48, dot(In.rgb, float3(0.2126,0.7152,0.0722)));
    float sheen = pow(saturate(1.0 - abs(normalize(View + 0.0001).x * 0.8
                                          + normalize(View + 0.0001).z * 0.45)), 12.0);
    float3 result = In.rgb;
    if (Data.x < 1.5)
    {
        float scrape = smoothstep(0.91, 0.98, sin((p.x + p.y * 1.9) * 170.0 + cloud * 11.0));
        result = lerp(In.rgb, Tint.rgb * (0.55 + cloud * 0.65), saturate(ink * 0.85 + cloud * 0.26));
        result = lerp(result, Tint.rgb * 1.6, scrape * (0.08 + ink * 0.23) * grain);
    }
    else if (Data.x < 2.5)
    {
        // Distance between the two closest jittered cells gives foil fractures.
        float2 cell = floor(p * 19.0), f = frac(p * 19.0);
        float first = 8.0, second = 8.0;
        [unroll] for (int y = -1; y <= 1; y++)
        [unroll] for (int x = -1; x <= 1; x++)
        {
            float2 q = float2(x,y);
            float2 jitter = float2(PSS_TagHash(cell + q), PSS_TagHash(cell + q + 17.0));
            float dist = length(q + jitter - f);
            if (dist < first) { second = first; first = dist; }
            else second = min(second, dist);
        }
        float gap = second - first;
        float crack = 1.0 - smoothstep(0.012, 0.012 + max(fwidth(gap),0.015), gap);
        float foil = smoothstep(0.47,0.65,cloud) * (0.4 + ink * 0.6);
        float3 gold = Tint.rgb * (0.65 + 0.65 * grain + sheen * 1.0);
        result = lerp(In.rgb, gold, foil * 0.88);
        result = lerp(result, In.rgb * 0.4, crack * (foil * 0.8 + 0.12));
    }
    else if (Data.x < 3.5)
    {
        float edge = 1.0 - smoothstep(0.02,0.15,min(border.x,border.y));
        result = In.rgb * lerp(float3(1,1,1), Tint.rgb * 1.35, 0.22 + edge * 0.28);
        result += float3(0.12,0.1,0.065) * sheen * (0.3 + edge) * (0.7 + cloud * 0.3);
        result += (grain - 0.5) * 0.025;
    }
    else if (Data.x < 4.5)
    {
        float hatchPhase = (p.x * 1.2 + p.y) * 190.0;
        float hatch = smoothstep(0.4,0.8,sin(hatchPhase)) * 0.065;
        result = lerp(Tint.rgb * (0.65 + cloud * 0.4), float3(0.78,0.76,0.65), ink);
        result += hatch + (grain - 0.5) * 0.045;
    }
    else
    {
        float2 q = (UV - (Rect.xy + Rect.zw) * 0.5) / max(Rect.zw - Rect.xy,0.01);
        q.x = abs(q.x);
        float wave = length(q * float2(1.8,1.0)) * 112.0 + sin(q.y * 19.0) * 3.5;
        float lineMask = 1.0 - smoothstep(0.12, 0.12 + max(fwidth(wave),0.15), abs(sin(wave)));
        float fine = 1.0 - smoothstep(0.09,0.09 + max(fwidth(wave),0.15),abs(sin(wave * 0.71 + 1.8)));
        result = lerp(In.rgb, Tint.rgb, lineMask * (1.0 - ink) * 0.62);
        result = lerp(result, float3(0.8,0.76,0.6), fine * (1.0 - ink) * 0.34);
    }
    Out.rgb = lerp(In.rgb, max(result,0.0), mask * saturate(Data.y));
}
#endif
