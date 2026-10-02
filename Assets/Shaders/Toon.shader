Shader "Wildbound/Toon" {
 Properties { _Color("Color", Color)=(1,1,1,1) }
 SubShader { Tags { "RenderType"="Opaque" } LOD 200
 CGPROGRAM
 #pragma surface surf Toon fullforwardshadows
 #pragma target 3.0
 fixed4 _Color;
 half4 LightingToon(SurfaceOutput s, half3 lightDir, half atten) {
 half n=dot(s.Normal,lightDir); half band=n>0.55?1:(n>0.05?0.72:0.42);
 half4 c; c.rgb=s.Albedo*(_LightColor0.rgb*band*atten+half3(0.16,0.19,0.20));c.a=s.Alpha;return c;
 }
 struct Input { float2 uv_MainTex; };
 void surf(Input IN,inout SurfaceOutput o){o.Albedo=_Color.rgb;o.Alpha=1;}
 ENDCG
 } FallBack "Diffuse"
}