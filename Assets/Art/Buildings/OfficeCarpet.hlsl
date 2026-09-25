// Ticket 184 — procedural office carpet (no texture samples).
// Called from OfficeCarpet.shadergraph Custom Function node.
#ifndef OFFICE_CARPET_INCLUDED
#define OFFICE_CARPET_INCLUDED

float OfficeCarpetHash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float OfficeCarpetValueNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = OfficeCarpetHash(i);
    float b = OfficeCarpetHash(i + float2(1, 0));
    float c = OfficeCarpetHash(i + float2(0, 1));
    float d = OfficeCarpetHash(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

void OfficeCarpet_float(
    float3 WorldPos,
    float4 BaseColor,
    float4 PatchColor,
    float PatchContrast,
    float PatchScale,
    float FibreAmount,
    float FibreScale,
    float SeamVisibility,
    float SeamTileSize,
    float SmoothnessIn,
    out float3 OutColor,
    out float SmoothnessOut)
{
    float2 xz = WorldPos.xz;
    float px = length(float2(ddx(xz.x), ddy(xz.x)));
    float pz = length(float2(ddx(xz.y), ddy(xz.y)));
    float pixelWorld = max(max(px, pz), 1e-6);

    float2 warpUv = xz * (PatchScale * 0.37);
    float2 warp = float2(
        OfficeCarpetValueNoise(warpUv),
        OfficeCarpetValueNoise(warpUv + 17.13)) - 0.5;
    float2 p = xz + warp * 0.4;

    // ~27 degree rotation so the second patch layer is not axis-aligned.
    float2 pr = float2(p.x * 0.8910 - p.y * 0.4540, p.x * 0.4540 + p.y * 0.8910);
    float n1 = OfficeCarpetValueNoise(p * PatchScale);
    float n2 = OfficeCarpetValueNoise(pr * PatchScale * 1.73);
    float patch = n1 * 0.55 + n2 * 0.45;
    patch = (patch - 0.5) * PatchContrast;
    float patchFade = saturate(1.0 - pixelWorld * PatchScale * 8.0);
    patch *= patchFade;

    float s1 = OfficeCarpetHash(xz * FibreScale);
    float s2 = OfficeCarpetHash(xz * FibreScale * 1.61 + 31.7);
    float speckle = (s1 * 0.55 + s2 * 0.45) - 0.5;
    float speckleFade = saturate(1.0 - pixelWorld * FibreScale * 1.6);
    speckle *= FibreAmount * speckleFade;

    float3 baseCol = BaseColor.rgb;
    float3 liteCol = PatchColor.rgb;
    float3 col = lerp(baseCol, liteCol, saturate(0.5 + patch));
    col += speckle * (liteCol - baseCol);
    float3 lo = min(baseCol, liteCol) * 0.92;
    float3 hi = max(baseCol, liteCol) * 1.08;
    col = clamp(col, lo, hi);

    float tile = max(SeamTileSize, 0.05);
    float2 g = abs(frac(xz / tile) - 0.5);
    float dist = min(g.x, g.y) * tile;
    float seamW = max(fwidth(dist), 0.002);
    float seam = 1.0 - smoothstep(0.0, seamW * 1.5, dist);
    col *= 1.0 - seam * SeamVisibility * 0.35;

    OutColor = col;
    SmoothnessOut = SmoothnessIn;
}

void OfficeCarpet_half(
    float3 WorldPos,
    float4 BaseColor,
    float4 PatchColor,
    float PatchContrast,
    float PatchScale,
    float FibreAmount,
    float FibreScale,
    float SeamVisibility,
    float SeamTileSize,
    float SmoothnessIn,
    out float3 OutColor,
    out float SmoothnessOut)
{
    OfficeCarpet_float(
        WorldPos, BaseColor, PatchColor, PatchContrast, PatchScale,
        FibreAmount, FibreScale, SeamVisibility, SeamTileSize, SmoothnessIn,
        OutColor, SmoothnessOut);
}

#endif
