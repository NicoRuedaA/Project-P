// Humo/fuego toon (explosiones), basado en el desglose de 80.lv.
//
// Textura de 4 canales (_SmokeTex):
//   R = sombreado pintado (bola iluminada desde arriba)
//   G = ruido "dibujado a mano"
//   B = patrón de erosión (celdas)
//   A = máscara de opacidad
//
// Custom1 de la partícula (TEXCOORD0.zw + TEXCOORD1.xy):
//   x = erosión del fuego   (0 = todo fuego, 1 = todo humo)
//   y = corte de sombreado  (se suma a _ShadeCutout)
//   z = erosión del alpha   (0 = bola entera, 1 = desaparecida)
//   w = rotación del ruido  (vueltas; aleatorio por partícula)
Shader "BotwVFX/Toon Smoke"
{
    Properties
    {
        _SmokeTex ("Smoke (R shade, G noise, B erosion, A mask)", 2D) = "white" {}
        _DetailTiling ("Detail Tiling", Float) = 1

        [Header(Fire)]
        [HDR] _FireColor ("Fire Core", Color) = (4, 2.6, 0.9, 1)
        [HDR] _FireEdgeColor ("Fire Edge", Color) = (2.5, 0.6, 0.15, 1)
        _FireEdgeWidth ("Fire Edge Width", Range(0, 0.5)) = 0.12
        _FireErosion ("Fire Erosion", Range(0, 1)) = 0

        [Header(Smoke)]
        _SmokeLight ("Smoke Light", Color) = (0.55, 0.52, 0.5, 1)
        _SmokeShadow ("Smoke Shadow", Color) = (0.22, 0.2, 0.22, 1)
        _ShadeCutout ("Shade Cutout", Range(0, 1)) = 0.45
        _NoiseAmount ("Shade Noise", Range(0, 1)) = 0.35

        [Header(Alpha)]
        _AlphaErosion ("Alpha Erosion", Range(0, 1)) = 0
        _ErosionStrength ("Erosion Pattern Strength", Range(0, 1)) = 0.6
        _VertexAlphaErosion ("Vertex Alpha -> Erosion", Range(0, 1)) = 0
        _Softness ("Edge Softness", Range(0, 0.5)) = 0

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull [_Cull]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "BotwVFX.cginc"

            sampler2D _SmokeTex;
            float _DetailTiling;
            float4 _FireColor;
            float4 _FireEdgeColor;
            float _FireEdgeWidth;
            float _FireErosion;
            float4 _SmokeLight;
            float4 _SmokeShadow;
            float _ShadeCutout;
            float _NoiseAmount;
            float _AlphaErosion;
            float _ErosionStrength;
            float _VertexAlphaErosion;
            float _Softness;

            struct appdata
            {
                float4 vertex : POSITION;
                float4 color : COLOR;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 custom : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv0.xy;
                o.custom = float4(v.uv0.zw, v.uv1.xy);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // El sombreado (R) y la máscara (A) NO se rotan: la luz siempre viene de arriba.
                float4 baseTex = tex2D(_SmokeTex, i.uv);
                // El ruido (G) y la erosión (B) sí se rotan para que cada bola sea distinta.
                float2 ruv = BotwRotateUV(i.uv, i.custom.w * BOTW_TAU);
                float4 detail = tex2D(_SmokeTex, (ruv - 0.5) * _DetailTiling + 0.5);

                // --- Sombreado toon en dos tonos ---
                float shade = saturate(baseTex.r + (detail.g - 0.5) * _NoiseAmount);
                float lit = BotwToonStep(saturate(_ShadeCutout + i.custom.y), _Softness, shade);
                float3 smoke = lerp(_SmokeShadow.rgb, _SmokeLight.rgb, lit);

                // --- Fuego: ocupa el centro caliente y se retira hacia dentro ---
                float heat = saturate(baseTex.a * 0.55 + detail.g * 0.45);
                float fireEdge = lerp(0.0, 1.05, saturate(_FireErosion + i.custom.x));
                float fire = BotwToonStep(fireEdge, _Softness, heat);
                float fireCore = BotwToonStep(fireEdge + _FireEdgeWidth, _Softness, heat);
                float3 fireCol = lerp(_FireEdgeColor.rgb, _FireColor.rgb, fireCore);

                float3 col = lerp(smoke, fireCol, fire) * i.color.rgb;

                // --- Alpha erosionado con el patrón de celdas ---
                float erosion = saturate(_AlphaErosion + i.custom.z + (1.0 - i.color.a) * _VertexAlphaErosion);
                float value = baseTex.a * lerp(1.0, detail.b, _ErosionStrength);
                float alpha = BotwToonStep(BotwErosionEdge(erosion), _Softness, value);
                alpha *= saturate((1.0 - erosion) * 50.0);
                alpha *= lerp(i.color.a, 1.0, _VertexAlphaErosion);

                return float4(col, alpha);
            }
            ENDCG
        }
    }
}
