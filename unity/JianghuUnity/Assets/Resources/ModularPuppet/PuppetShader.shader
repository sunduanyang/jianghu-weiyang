Shader "Puppet/CarvedUnlit" {
 Properties { _MainTex ("Artwork", 2D) = "white" {} _MaskTex ("Socket mask", 2D) = "white" {} }
 SubShader {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" }
  Cull Off ZWrite Off Lighting Off
  Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   struct input { float4 vertex:POSITION; float2 uv:TEXCOORD0; };
   struct output { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; };
   sampler2D _MainTex, _MaskTex;
   output vert(input v) { output o; o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o; }
   fixed4 frag(output i):SV_Target { fixed4 c=tex2D(_MainTex,i.uv);c.a*=tex2D(_MaskTex,i.uv).a;return c; }
   ENDCG
  }
 }
}
