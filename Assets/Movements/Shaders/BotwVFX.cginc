// Funciones compartidas por los shaders de BotwVFX.
// Solo dependen de UnityCG.cginc, así que funcionan tanto en URP como en Built-in.
#ifndef BOTW_VFX_INCLUDED
#define BOTW_VFX_INCLUDED

#define BOTW_TAU 6.28318530718

// Corte "toon": borde duro pero antialiasado con fwidth.
// softness = 0 -> borde nítido estilo BotW; valores mayores lo suavizan.
float BotwToonStep(float edge, float softness, float x)
{
    float w = max(softness, fwidth(x)) * 0.5;
    return smoothstep(edge - w, edge + w, x);
}

// Convierte una erosión 0..1 en el umbral real del corte.
// Con 0 desaparece lo que tenga máscara ~0 (bordes del quad) y con 1 desaparece todo.
float BotwErosionEdge(float erosion)
{
    return lerp(0.02, 1.02, erosion);
}

float2 BotwRotateUV(float2 uv, float angle)
{
    float s, c;
    sincos(angle, s, c);
    uv -= 0.5;
    return float2(uv.x * c - uv.y * s, uv.x * s + uv.y * c) + 0.5;
}

// UV polares: x = ángulo (0..1), y = radio (0 centro, 1 borde del quad).
float2 BotwPolarUV(float2 uv, float twist)
{
    float2 d = uv - 0.5;
    float r = length(d) * 2.0;
    float a = atan2(d.y, d.x) / BOTW_TAU + 0.5;
    return float2(a + r * twist, r);
}

float BotwHash31(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float BotwValueNoise3(float3 x)
{
    float3 i = floor(x);
    float3 f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(lerp(BotwHash31(i + float3(0, 0, 0)), BotwHash31(i + float3(1, 0, 0)), f.x),
                     lerp(BotwHash31(i + float3(0, 1, 0)), BotwHash31(i + float3(1, 1, 0)), f.x), f.y),
                lerp(lerp(BotwHash31(i + float3(0, 0, 1)), BotwHash31(i + float3(1, 0, 1)), f.x),
                     lerp(BotwHash31(i + float3(0, 1, 1)), BotwHash31(i + float3(1, 1, 1)), f.x), f.y), f.z);
}

float BotwFbm3(float3 p)
{
    float v = 0.0;
    float a = 0.5;
    for (int k = 0; k < 3; k++)
    {
        v += a * BotwValueNoise3(p);
        p = p * 2.03 + 17.13;
        a *= 0.5;
    }
    return v / 0.875;
}

// "Negated view direction" del artículo de 80.lv: acerca el vértice a la cámara
// para que flares y destellos no se corten al atravesar mallas.
float3 BotwPushToCamera(float3 positionWS, float distance)
{
    return positionWS + normalize(_WorldSpaceCameraPos - positionWS) * distance;
}

#endif
