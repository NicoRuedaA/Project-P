// Shader principal de partículas estilo Breath of the Wild.
//
// Idea: en vez de hacer fade de opacidad, las partículas se "erosionan":
//   valor = forma(_MainTex) * ruido(_NoiseTex)
//   alpha = corte duro (valor > erosión)
// y una banda justo en el borde del corte se pinta con _EdgeColor.
//
// Streams de partícula esperados (los configura EffectFactory):
//   TEXCOORD0.xy = UV, TEXCOORD0.zw = Custom1.xy, TEXCOORD1.xy = Custom1.zw
//   Custom1.x = erosión extra (curva sobre la vida)
//   Custom1.y = desplazamiento aleatorio del ruido (cada partícula distinta)
//   Custom1.z = brillo extra (1 + z)
//   Custom1.w = rotación del ruido (vueltas)
// También funciona en MeshRenderer y LineRenderer (los custom valen 0).
Shader "BotwVFX/Toon Particle"
{
    Properties
    {
        [Header(Shape)]
        _MainTex ("Shape Mask (R)", 2D) = "white" {}
        _MainScroll ("Shape Scroll (XY)", Vector) = (0, 0, 0, 0)
        _BorderFade ("Border Fade (U, V)", Vector) = (0, 0, 0, 0)

        [Header(Noise Erosion)]
        _NoiseTex ("Noise (R)", 2D) = "gray" {}
        _NoiseScroll ("Noise Scroll (XY)", Vector) = (0, 0, 0, 0)
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.5
        [Toggle(_POLAR_ON)] _Polar ("Polar Noise UV (portales)", Float) = 0
        _PolarTwist ("Polar Twist", Float) = 0

        [Header(Color)]
        [HDR] _Color ("Core Color", Color) = (1, 1, 1, 1)
        [HDR] _EdgeColor ("Edge Color", Color) = (1, 0.5, 0, 1)
        _EdgeWidth ("Edge Width", Range(0, 1)) = 0.1
        _Erosion ("Erosion", Range(0, 1)) = 0
        _Softness ("Edge Softness", Range(0, 0.5)) = 0
        _AlphaErosion ("Vertex Alpha -> Erosion", Range(0, 1)) = 1

        [Header(Extras)]
        _ShadeAmount ("Fake Light (mesh particles)", Range(0, 1)) = 0
        _LightDir ("Fake Light Dir", Vector) = (0.4, 0.8, 0.3, 0)
        _CameraOffset ("Push To Camera", Float) = 0

        [Header(Blending)]
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        Cull [_Cull]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma shader_feature_local _POLAR_ON
            #include "UnityCG.cginc"
            #include "BotwVFX.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _MainScroll;
            float4 _BorderFade;
            sampler2D _NoiseTex;
            float4 _NoiseTex_ST;
            float4 _NoiseScroll;
            float _NoiseStrength;
            float _PolarTwist;
            float4 _Color;
            float4 _EdgeColor;
            float _EdgeWidth;
            float _Erosion;
            float _Softness;
            float _AlphaErosion;
            float _ShadeAmount;
            float4 _LightDir;
            float _CameraOffset;
            float _BotwDebugMode;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
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
                float3 normalWS : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 positionWS = mul(unity_ObjectToWorld, float4(v.vertex.xyz, 1.0)).xyz;
                positionWS = BotwPushToCamera(positionWS, _CameraOffset);
                o.pos = mul(UNITY_MATRIX_VP, float4(positionWS, 1.0));
                o.color = v.color;
                o.uv = v.uv0.xy;
                o.custom = float4(v.uv0.zw, v.uv1.xy);
                o.normalWS = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                // Forma base (círculo, rayo, estrella...) con scroll opcional.
                float mask = tex2D(_MainTex, uv * _MainTex_ST.xy + _MainTex_ST.zw + _MainScroll.xy * _Time.y).r;

                // "Borrar los bordes" por UV: útil en mallas tipo tira o anillo.
                float2 border = float2(
                    _BorderFade.x > 0 ? saturate(min(uv.x, 1.0 - uv.x) / _BorderFade.x) : 1.0,
                    _BorderFade.y > 0 ? saturate(min(uv.y, 1.0 - uv.y) / _BorderFade.y) : 1.0);
                mask *= border.x * border.y;

                // Ruido de erosión: rotado y desplazado por partícula.
                #if defined(_POLAR_ON)
                    float2 nuv = BotwPolarUV(uv, _PolarTwist);
                #else
                    float2 nuv = BotwRotateUV(uv, i.custom.w * BOTW_TAU);
                #endif
                nuv = nuv * _NoiseTex_ST.xy + _NoiseTex_ST.zw + _NoiseScroll.xy * _Time.y + i.custom.y * float2(0.37, 0.71);
                #if defined(_POLAR_ON)
                    float noise = tex2Dlod(_NoiseTex, float4(nuv, 0, 0)).r; // evita la costura del atan2
                #else
                    float noise = tex2D(_NoiseTex, nuv).r;
                #endif

                float value = mask * lerp(1.0, noise, _NoiseStrength);

                // La erosión viene del material, de Custom1.x y (opcional) del alpha del color de la partícula.
                float erosion = saturate(_Erosion + i.custom.x + (1.0 - i.color.a) * _AlphaErosion);
                float edge = BotwErosionEdge(erosion);
                float alpha = BotwToonStep(edge, _Softness, value);
                float core = BotwToonStep(edge + _EdgeWidth, _Softness, value);

                float3 col = lerp(_EdgeColor.rgb, _Color.rgb, core) * i.color.rgb * (1.0 + i.custom.z);
                float a = alpha * lerp(_EdgeColor.a, _Color.a, core);
                a *= lerp(i.color.a, 1.0, _AlphaErosion);
                a *= saturate((1.0 - erosion) * 50.0);

                // Luz falsa en dos tonos para escombros (mesh particles).
                float nl = dot(normalize(i.normalWS + 1e-5), normalize(_LightDir.xyz));
                col *= lerp(1.0, nl > 0.0 ? 1.0 : 0.55, _ShadeAmount);

                // Depuración de streams (variable global, la activa BotwCapture).
                if (_BotwDebugMode > 0.5)
                {
                    float4 dbg = _BotwDebugMode < 1.5 ? float4(i.color.rgb, 1)
                               : _BotwDebugMode < 2.5 ? float4(i.custom.xyz, 1)
                               : _BotwDebugMode < 3.5 ? float4(i.uv, i.custom.w, 1)
                               : _BotwDebugMode < 4.5 ? float4(i.normalWS * 0.5 + 0.5, 1)
                               : float4(col * 0.25, 1);
                    return float4(dbg.rgb, alpha > 0.01 ? 1.0 : 0.0);
                }

                return float4(col, saturate(a));
            }
            ENDCG
        }
    }
}
