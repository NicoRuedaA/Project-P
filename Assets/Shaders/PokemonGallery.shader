Shader "Wildbound/Pokemon Gallery" {
 Properties {
  _MainTex("Color atlas", 2D)="white" {}
  _TileTex("Upper UV tile color atlas", 2D)="white" {}
  _UseTile("Use upper UV tile", Float)=0
  _Color("Color", Color)=(1,1,1,1)
  _Flame("Flame rendering", Float)=0
  _HasFlameTexture("Has flame diffuse texture", Float)=0
  _SrcBlend("Source blend", Float)=1
  _DstBlend("Destination blend", Float)=0
  _ZWrite("Depth write", Float)=1
 }
 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
  Cull Off Blend [_SrcBlend] [_DstBlend] ZWrite [_ZWrite]
  Pass {
   Tags { "LightMode"="UniversalForward" }
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
   TEXTURE2D(_TileTex); SAMPLER(sampler_TileTex);
   CBUFFER_START(UnityPerMaterial)
    float4 _MainTex_ST;
    half4 _Color;
    float _UseTile, _Flame, _HasFlameTexture;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; };
   struct Varyings { float4 positionHCS:SV_POSITION; float2 uv:TEXCOORD0; float3 normalWS:TEXCOORD1; };
   Varyings vert(Attributes input) {
    Varyings output;
    output.positionHCS=TransformObjectToHClip(input.positionOS.xyz);
    output.uv=TRANSFORM_TEX(input.uv,_MainTex);
    output.normalWS=TransformObjectToWorldNormal(input.normalOS);
    return output;
   }
   half4 frag(Varyings input):SV_Target {
    half4 color=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv)*_Color;
    if (_UseTile>0.5 && input.uv.y>1) color=SAMPLE_TEXTURE2D(_TileTex,sampler_TileTex,input.uv-float2(0,1))*_Color;
    if (_Flame>0.5) {
     if (_HasFlameTexture>0.5) {
      half3 flame=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv).rgb;
      half coverage=max(flame.r,max(flame.g,flame.b));
      return half4(flame,coverage*_Color.a);
     }
     return _Color;
    }
    Light mainLight=GetMainLight();
    half diffuse=saturate(dot(normalize(input.normalWS),normalize(mainLight.direction)));
    color.rgb*=0.5h+0.5h*diffuse;
    color.a=_Color.a;
    return color;
   }
   ENDHLSL
  }
 }
 SubShader {
  Tags { "RenderType"="Opaque" "Queue"="Geometry" }
  Cull Off Blend [_SrcBlend] [_DstBlend] ZWrite [_ZWrite]
  Pass {
   Tags { "LightMode"="ForwardBase" }
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fwdbase
   #include "UnityCG.cginc"
   #include "Lighting.cginc"
   sampler2D _MainTex, _TileTex;
   fixed4 _Color;
   float _UseTile, _Flame, _HasFlameTexture;
   struct appdata { float4 vertex:POSITION; float3 normal:NORMAL; float2 uv:TEXCOORD0; };
   struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float3 normal:TEXCOORD1; };
   v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex); o.uv=v.uv; o.normal=UnityObjectToWorldNormal(v.normal); return o; }
   fixed4 frag(v2f i):SV_Target {
    fixed4 color=tex2D(_MainTex,i.uv)*_Color;
    if (_UseTile>0.5 && i.uv.y>1) color=tex2D(_TileTex,i.uv-float2(0,1))*_Color;
    if (_Flame>0.5) {
     if (_HasFlameTexture>0.5) {
      fixed3 flame=tex2D(_MainTex,i.uv).rgb;
      // Combo diffuse maps use black as empty space; derive coverage so the transparent flame retains its shape.
      fixed coverage=max(flame.r,max(flame.g,flame.b));
      return fixed4(flame,coverage*_Color.a);
     }
     return _Color;
    }
    // Opaque source atlases can pack non-opacity data in alpha; do not discard their skin.
    color.a=_Color.a;
    float diffuse=saturate(dot(normalize(i.normal),normalize(_WorldSpaceLightPos0.xyz)));
    color.rgb*=0.5+0.5*diffuse;
    return color;
   }
   ENDCG
  }
 }
 Fallback Off
}
