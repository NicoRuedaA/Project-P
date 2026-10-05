// Esfera de energía de la Bomba Remota (y cuerpo del rayo Guardián).
// Basado en el Shader Graph "StylisedBomb" del vídeo de Daniel Ilett:
//   emisión = paso( umbral, (fresnel + intersección con el suelo) * ruido )
//   color   = lerp(BaseColor, EmissiveColor, emisión)
// Diferencias: ruido 3D en espacio objeto (sin costuras), wobble de vértices,
// disolución final, y dos pasadas (caras traseras y luego delanteras) para
// que la transparencia se ordene bien.
//
// Necesita la Depth Texture activada en el asset de URP para la intersección.
Shader "BotwVFX/Energy Sphere"
{
    Properties
    {
        [HDR] _BaseColor ("Base Color", Color) = (0.2, 0.55, 1.0, 0.5)
        [HDR] _EmissiveColor ("Emissive Color", Color) = (1.5, 3.5, 6.0, 1.0)
        _FresnelPower ("Fresnel Power", Range(0.1, 8)) = 1.5
        _IntersectionDistance ("Intersection Distance", Float) = 0.6
        _IntersectionPower ("Intersection Power", Range(0.1, 8)) = 1.5
        _NoiseScale ("Noise Scale", Float) = 3
        _NoiseSpeed ("Noise Speed", Float) = 1.5
        _NoiseAmount ("Noise Amount", Range(0, 1)) = 0.8
        _Threshold ("Threshold", Range(-0.1, 1.5)) = 0.45
        _Softness ("Edge Softness", Range(0, 0.5)) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _Wobble ("Vertex Wobble", Range(0, 0.5)) = 0.05
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "BotwVFX.cginc"

    float4 _BaseColor;
    float4 _EmissiveColor;
    float _FresnelPower;
    float _IntersectionDistance;
    float _IntersectionPower;
    float _NoiseScale;
    float _NoiseSpeed;
    float _NoiseAmount;
    float _Threshold;
    float _Softness;
    float _Opacity;
    float _Dissolve;
    float _Wobble;
    UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);

    struct appdata
    {
        float4 vertex : POSITION;
        float3 normal : NORMAL;
    };

    struct v2f
    {
        float4 pos : SV_POSITION;
        float3 positionOS : TEXCOORD0;
        float3 positionWS : TEXCOORD1;
        float3 normalWS : TEXCOORD2;
        float4 screenPos : TEXCOORD3;
    };

    v2f vert(appdata v)
    {
        v2f o;
        float t = _Time.y * _NoiseSpeed;
        float wobble = BotwValueNoise3(v.vertex.xyz * 2.5 + t) - 0.5;
        float4 positionOS = float4(v.vertex.xyz + v.normal * wobble * _Wobble, 1.0);
        o.pos = UnityObjectToClipPos(positionOS);
        o.positionOS = positionOS.xyz;
        o.positionWS = mul(unity_ObjectToWorld, positionOS).xyz;
        o.normalWS = UnityObjectToWorldNormal(v.normal);
        o.screenPos = ComputeScreenPos(o.pos);
        COMPUTE_EYEDEPTH(o.screenPos.z);
        return o;
    }

    float4 frag(v2f i, float facing : VFACE) : SV_Target
    {
        float3 n = normalize(i.normalWS);
        float3 viewDir = normalize(_WorldSpaceCameraPos - i.positionWS);

        // Fresnel solo en caras delanteras (Is Front Face + Branch en el Shader Graph original).
        float fresnel = facing > 0 ? pow(1.0 - saturate(dot(n, viewDir)), _FresnelPower) : 0.0;

        // Brillo donde la esfera corta el suelo (Scene Depth - Screen Position.w).
        float sceneDepth = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.screenPos)));
        float depthDiff = sceneDepth - i.screenPos.z;
        float intersection = pow(saturate(1.0 - depthDiff / max(_IntersectionDistance, 1e-3)), _IntersectionPower);

        float noise = BotwFbm3(i.positionOS * _NoiseScale + _Time.y * _NoiseSpeed);
        float metric = (fresnel + intersection) * lerp(1.0, noise * 2.0, _NoiseAmount);
        float emission = BotwToonStep(_Threshold, _Softness, metric);

        float3 col = lerp(_BaseColor.rgb, _EmissiveColor.rgb, emission);
        float alpha = lerp(_BaseColor.a, _EmissiveColor.a, emission) * _Opacity;

        // Disolución final con el mismo ruido (se rompe en trozos, no hace fade).
        float dissolveNoise = BotwFbm3(i.positionOS * (_NoiseScale * 0.7) + 31.7);
        alpha *= BotwToonStep(BotwErosionEdge(_Dissolve), 0, dissolveNoise);

        return float4(col, saturate(alpha));
    }
    ENDCG

    // URP: dos pasadas con LightMode distintos para que se dibujen las dos.
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            Name "BackFaces"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            ENDCG
        }

        Pass
        {
            Name "FrontFaces"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            ENDCG
        }
    }

    // Built-in Render Pipeline.
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            ENDCG
        }

        Pass
        {
            Cull Back
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            ENDCG
        }
    }
}
