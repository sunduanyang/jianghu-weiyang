// Source GLES expressions, original pass states and 16 URP2D shape-light combinations.
Shader "BB/ArmorItemPreview" {
Properties {
		_MainTex ("Main Texture", 2D) = "white" {}
		_TintColor ("Tint Color", Vector) = (1,1,1,1)
		_Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
	}

SubShader {
Tags {"QUEUE"="AlphaTest" "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout"}
Pass {
Name ""
Tags {"QUEUE"="AlphaTest" "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout"}
Cull Back ZWrite On ZTest LEqual
Blend One Zero, One Zero

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
float4 _MainTex_ST;
float4 _TintColor;
float _Cutoff;
float4 _ShopClipRect;
sampler2D _MainTex;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float2 vs_TEXCOORD0:TEXCOORD0;
float4 vs_TEXCOORD1:TEXCOORD1; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat0 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    u_xlat0 = u_xlat0 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_TEXCOORD0.xy = input.texcoord.xy * _MainTex_ST.xy + _MainTex_ST.zw;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_TEXCOORD1.zw = u_xlat0.zw;
    output.vs_TEXCOORD1.xy = u_xlat1.zz + u_xlat1.xw;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float u_xlat16_1;
float2 u_xlat4;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_TEXCOORD0.xy,0,_GlobalMipBias.x));
    u_xlat16_1 = u_xlat16_0.w * _TintColor.w + (-_Cutoff);
    u_xlat0 = u_xlat16_0 * _TintColor;
    result = u_xlat0;
    u_xlatb0 = u_xlat16_1<0.0;
    if(u_xlatb0){discard;}
    u_xlat0.xy = input.vs_TEXCOORD1.xy / input.vs_TEXCOORD1.ww;
    u_xlat4.xy = u_xlat0.xy + (-_ShopClipRect.xy);
    u_xlat0.xy = (-u_xlat0.xy) + _ShopClipRect.zw;
    u_xlat0.xy = min(u_xlat0.xy, u_xlat4.xy);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    return result;

}

ENDHLSL
}
}
}
