// Exact GLES CwReplace fragment expression from the source APK.
Shader "Hidden/PaintCore/CwReplace" {
Properties {_Texture ("Texture",2D)="white" {} _Color("Color",Color)=(1,1,1,1)}
SubShader {Cull Off ZWrite Off ZTest Always
Pass {HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#include "UnityCG.cginc"
sampler2D _Texture;float4 _Color;
struct V {float4 position:SV_POSITION;float2 uv:TEXCOORD0;};
V vert(appdata_img input){V o;o.position=UnityObjectToClipPos(input.vertex);o.uv=input.texcoord;return o;}
float4 frag(V input):SV_Target{return tex2Dlod(_Texture,float4(input.uv,0,0))*_Color;}
ENDHLSL}
}}
