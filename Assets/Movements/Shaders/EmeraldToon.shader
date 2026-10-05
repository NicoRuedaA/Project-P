// Shader para las animaciones horneadas de los movimientos Emerald (EmeraldVfxPlayer),
// reestilizado como el resto de shaders BotW (solo URP).
//
// Interfaz que fija el reproductor (no cambiar los nombres):
//   _Color (lineal, vía MaterialPropertyBlock), _Mode, _Cull, _ZWrite, _ZTest.
// Modos (misma semántica que el shader original "EmeraldArena/CelVFX"):
//   0 fuego / energía: bandas por fresnel (centro = núcleo, silueta = borde)
//   1 rayo: degradado a lo largo de uv.x
//   2 banda: degradado transversal en uv.y
//   3 paneles: bordes de panel por UV
//   4 sólido toon iluminado (como BotwVFX/Environment/Toon Lit)
//   5 contorno (casco invertido; el reproductor pone Cull Front)
//
// Estilo BotW en los modos de energía (0-3):
//   - color HDR (núcleo casi blanco, cuerpo saturado, borde aún más saturado) para el bloom,
//   - bandas toon nítidas antialiasadas con fwidth,
//   - borde erosionado por ruido animado (en vez de fundido), más erosión cuanto menor es el alpha,
//   - brillo suave donde la malla corta el escenario (necesita la Depth Texture de URP).
Shader "BotwVFX/Emerald Toon"
{
    Properties
    {
        _Color ("Color (lineal)", Color) = (1, 1, 1, 1)
        _Mode ("Mode", Float) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        _ZWrite ("Depth write", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test", Float) = 4

        [Header(Energia HDR)]
        _EmissionBoost ("HDR Boost", Range(1, 4)) = 1.6
        _CoreBoost ("Core Extra Boost", Range(1, 3)) = 1.4
        _BodyMax ("Body Max (canal dominante)", Range(0.5, 2)) = 1.2
        _CoreWhite ("Core -> White (colores claros)", Range(0, 1)) = 0.9
        _Saturation ("Saturation", Range(1, 2.5)) = 1.5

        [Header(Erosion por ruido)]
        _NoiseScale ("Noise Scale (por metro)", Float) = 2.2
        _NoiseSpeed ("Noise Speed", Float) = 1.8
        _NoiseAmount ("Noise Amount", Range(0, 1)) = 0.5
        _EdgeErosion ("Edge Erosion", Range(0, 0.5)) = 0.08
        _AlphaErosion ("Alpha -> Erosion", Range(0, 1)) = 0.35

        [Header(Interseccion)]
        _IntersectionDistance ("Intersection Distance", Float) = 0.3
        _IntersectionStrength ("Intersection Strength", Range(0, 1)) = 0.8

        [Header(Solido toon)]
        _ShadowColor ("Shadow Tint", Color) = (0.55, 0.62, 0.8, 1)
        _RampThreshold ("Ramp Threshold", Range(-1, 1)) = 0.05
        _RampSoftness ("Ramp Softness", Range(0.001, 0.5)) = 0.02
        _AmbientAmount ("Ambient", Range(0, 1)) = 0.25
        _RimColor ("Rim Color (A = strength)", Color) = (1, 0.93, 0.78, 0.35)

        [Header(Contorno)]
        _OutlineWidth ("Outline Width (por metro de distancia)", Range(0, 0.01)) = 0.0026
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        Pass
        {
            Name "EmeraldToon"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull [_Cull]
            ZWrite [_ZWrite]
            ZTest [_ZTest]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "BotwVFX.cginc"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Mode;
                float _EmissionBoost;
                float _CoreBoost;
                float _BodyMax;
                float _CoreWhite;
                float _Saturation;
                float _NoiseScale;
                float _NoiseSpeed;
                float _NoiseAmount;
                float _EdgeErosion;
                float _AlphaErosion;
                float _IntersectionDistance;
                float _IntersectionStrength;
                float4 _ShadowColor;
                float _RampThreshold;
                float _RampSoftness;
                float _AmbientAmount;
                float4 _RimColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float3 noisePos : TEXCOORD3; // posición en objeto, en metros (el ruido viaja con la malla)
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                float4x4 m = GetObjectToWorldMatrix();
                float3 scale = float3(length(m._m00_m10_m20), length(m._m01_m11_m21), length(m._m02_m12_m22));

                // Contorno: casco invertido empujado por la normal (mismo criterio que el original).
                if (_Mode > 4.5)
                {
                    float s = min(scale.x, min(scale.y, scale.z));
                    positionWS += n * min(s * 0.16, _OutlineWidth * max(distance(_WorldSpaceCameraPos, positionWS), 0.1));
                }

                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionWS = positionWS;
                o.normalWS = n;
                o.uv = v.uv;
                o.noisePos = v.positionOS.xyz * scale;
                return o;
            }

            float BotwLuma(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }
            float3 BotwSaturate(float3 c, float k) { return max(lerp(BotwLuma(c).xxx, c, k), 0.0); }

            float4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 v = normalize(_WorldSpaceCameraPos - i.positionWS);
                float ndv = dot(n, v);
                float3 c = max(_Color.rgb, 0.0);
                float a = saturate(_Color.a);
                float3 col;

                if (_Mode < 3.5)
                {
                    // ---- Métrica de banda según el modo
                    float x;
                    if (_Mode < 0.5)      x = abs(ndv);
                    else if (_Mode < 1.5) x = 1.0 - i.uv.x;
                    else if (_Mode < 2.5) x = 1.0 - abs(i.uv.y - 0.5) * 2.0;
                    else { float2 e = min(i.uv, 1.0 - i.uv); x = min(e.x, e.y) * 6.0; }

                    // Ruido 3D animado (sube como una llama) que deforma las bandas.
                    float t = _Time.y * _NoiseSpeed;
                    float noise = BotwFbm3(i.noisePos * _NoiseScale + float3(0.0, -t, t * 0.37));
                    bool panel = _Mode > 2.5;
                    float xn = x + (noise - 0.5) * _NoiseAmount * (panel ? 0.2 : 0.6);

                    float coreEdge = _Mode < 0.5 ? 0.86 : 0.55;
                    float bodyEdge = _Mode < 0.5 ? 0.45 : (panel ? 0.22 : 0.2);
                    float core = panel ? 0.0 : BotwToonStep(coreEdge, 0.0, xn);
                    float body = BotwToonStep(bodyEdge, 0.0, xn);

                    // ---- Paleta HDR: borde saturado, cuerpo saturado brillante, núcleo casi blanco.
                    // Sin tonemapping cada canal se recorta a 1: el cuerpo y el borde solo pasan de 1 en el
                    // canal dominante (_BodyMax) para conservar el tono en pantalla; los colores oscuros
                    // (tinta, humo) se aclaran como mucho _EmissionBoost.
                    float3 sat = BotwSaturate(c, _Saturation);
                    float3 rimSat = pow(BotwSaturate(c, _Saturation * 1.25), 1.4);
                    float3 rimCol = rimSat * min(_EmissionBoost * 0.75, _BodyMax * 0.9 / max(max(rimSat.r, max(rimSat.g, rimSat.b)), 1e-3));
                    float3 bodyCol = sat * min(_EmissionBoost, _BodyMax / max(max(sat.r, max(sat.g, sat.b)), 1e-3));
                    // Núcleo: blanco HDR solo en los colores claros (como el original); los oscuros conservan el tono.
                    float whiteness = _CoreWhite * smoothstep(0.25, 0.7, BotwLuma(c));
                    float3 coreCol = lerp(bodyCol * 1.2, float3(1.0, 0.99, 0.94) * (_EmissionBoost * _CoreBoost), whiteness);
                    if (panel)
                    {
                        // Paneles: interior del color del cuerpo y aristas encendidas (estilo Sheikah).
                        col = lerp(coreCol, bodyCol * 0.85, body);
                    }
                    else
                    {
                        col = lerp(lerp(rimCol, bodyCol, body), coreCol, core);
                    }
                    a = saturate(a * lerp(1.25, 1.0, body));

                    // ---- Erosión: el borde se rompe con el ruido en vez de fundirse.
                    if (!panel)
                    {
                        float value = x * lerp(1.0, noise * 2.0, _NoiseAmount);
                        float erosion = _EdgeErosion + (1.0 - a) * _AlphaErosion;
                        a *= BotwToonStep(erosion, 0.0, value);
                    }

                    // ---- Brillo de intersección con el escenario (suelo, personajes).
                    if (_IntersectionDistance > 0.0)
                    {
                        float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                        float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                        float fragZ = LinearEyeDepth(i.positionWS, GetWorldToViewMatrix());
                        float d = sceneZ - fragZ;
                        float inter = d >= 0.0 ? saturate(1.0 - d / _IntersectionDistance) : 0.0;
                        inter = BotwToonStep(0.5, 0.0, inter) * _IntersectionStrength;
                        col = lerp(col, coreCol, inter);
                    }
                }
                else if (_Mode < 4.5)
                {
                    // ---- Sólido toon, coherente con Toon Lit: luz principal en dos tonos + rim duro.
                    if (ndv < 0.0) { n = -n; ndv = -ndv; }
                    Light mainLight = GetMainLight();
                    float ndl = dot(n, mainLight.direction);
                    float ramp = smoothstep(_RampThreshold - _RampSoftness, _RampThreshold + _RampSoftness, ndl);
                    col = c * lerp(_ShadowColor.rgb, mainLight.color, ramp) + c * _AmbientAmount;
                    float rim = pow(1.0 - saturate(ndv), 4.0) * ramp;
                    col += _RimColor.rgb * step(0.5, rim) * _RimColor.a * max(c, 0.35);
                }
                else
                {
                    // ---- Contorno plano.
                    col = c;
                }

                // Opacidad en escalones (look toon), igual que el original.
                a = a < 0.1667 ? a * 2.0 : min(1.0, ceil(a * 3.0 - 0.5) / 3.0);
                clip(a - 0.01);

                #ifdef UNITY_COLORSPACE_GAMMA
                    col = LinearToSRGB(col);
                #endif
                return float4(col, a);
            }
            ENDHLSL
        }

        // Profundidad de los sólidos (modo 4). El reproductor pone a todos los dibujos de un mismo "order" la misma
        // cola de render (3000 + order); el sólido sin ZWrite (flags 0) y su contorno de casco invertido (modo 5)
        // empatan y el orden de dibujo depende de la cámara: si el contorno va después, pinta encima del sólido
        // entero (029: nube de polvo crema que salía marrón oscuro en la vista de combate). Con este pase el
        // sólido siempre escribe profundidad y el contorno solo asoma por la silueta, en cualquier orden.
        // Solo modo 4 y con opacidad escalonada >= 0,5 (un sólido casi transparente no tapa nada).
        Pass
        {
            Name "EmeraldToonSolidDepth"
            Tags { "LightMode" = "UniversalForward" }
            ColorMask 0
            Cull [_Cull]
            ZWrite On
            ZTest [_ZTest]

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Mode;
                float _EmissionBoost;
                float _CoreBoost;
                float _BodyMax;
                float _CoreWhite;
                float _Saturation;
                float _NoiseScale;
                float _NoiseSpeed;
                float _NoiseAmount;
                float _EdgeErosion;
                float _AlphaErosion;
                float _IntersectionDistance;
                float _IntersectionStrength;
                float4 _ShadowColor;
                float _RampThreshold;
                float _RampSoftness;
                float _AmbientAmount;
                float4 _RimColor;
                float _OutlineWidth;
            CBUFFER_END

            float4 vertDepth(float4 positionOS : POSITION) : SV_POSITION
            {
                return TransformObjectToHClip(positionOS.xyz);
            }

            half4 fragDepth() : SV_Target
            {
                float a = saturate(_Color.a);
                a = a < 0.1667 ? a * 2.0 : min(1.0, ceil(a * 3.0 - 0.5) / 3.0);
                clip((abs(_Mode - 4.0) < 0.5 && a >= 0.5) ? 1.0 : -1.0);
                return 0;
            }
            ENDHLSL
        }
    }
}
