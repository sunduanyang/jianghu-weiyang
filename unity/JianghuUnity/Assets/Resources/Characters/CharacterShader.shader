Shader "Puppet/CharacterModules" {
 Properties {
  _MainTex ("Artwork",2D)="white" {} _ClipTex ("Socket alpha",2D)="white" {} _ColorMask ("Colour regions",2D)="black" {}
  _Skin ("Skin",Color)=(1,1,1,1) _Hair ("Hair",Color)=(.1,.1,.1,1) _Primary ("Cloth",Color)=(.2,.3,.3,1) _Secondary ("Trim",Color)=(.7,.6,.4,1) _Head ("Head mask",Float)=0
  _PartMap ("Source sprite ownership",2D)="black" {} _SourcePart ("Source sprite ID",Float)=0
 }
 SubShader {
  Tags {"Queue"="Transparent" "RenderType"="Transparent"} Cull Off ZWrite Off Lighting Off
  Blend SrcAlpha OneMinusSrcAlpha,One OneMinusSrcAlpha
  Pass {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   sampler2D _MainTex,_ClipTex,_ColorMask,_PartMap; fixed4 _Skin,_Hair,_Primary,_Secondary;float _Head,_SourcePart;
   struct app {float4 vertex:POSITION;float2 uv:TEXCOORD0;};struct data {float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;};
   data vert(app v){data o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;return o;}
   fixed4 frag(data i):SV_Target {
    fixed4 c=tex2D(_MainTex,i.uv),m=tex2D(_ColorMask,i.uv);float l=dot(c.rgb,float3(.299,.587,.114));
    c.rgb=lerp(c.rgb,_Skin.rgb*(.78+l*.3),m.r*.92);
    c.rgb=lerp(c.rgb,lerp(_Primary.rgb,_Hair.rgb,_Head)*(.65+l*.6),m.g*.88);
    c.rgb=lerp(c.rgb,lerp(_Secondary.rgb,_Primary.rgb,_Head)*(.65+l*.6),m.b*.88);
    c.a*=tex2D(_ClipTex,i.uv).a;
    if(_SourcePart>.5)c.a*=abs(tex2D(_PartMap,i.uv).r*255-_SourcePart)<.5?1:0;
    return c;
   }
   ENDCG
  }
 }
}
