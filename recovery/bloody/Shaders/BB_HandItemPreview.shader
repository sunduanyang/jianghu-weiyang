// Source GLES expressions, original pass states and 16 URP2D shape-light combinations.
Shader "BB/HandItemPreview" {
Properties {
		_MainTex ("Sprite Texture", 2D) = "white" {}
		_Color ("Tint", Vector) = (1,1,1,1)
	}

SubShader {
Tags {"QUEUE"="Transparent" "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent"}
Pass {
Name ""
Tags {"QUEUE"="Transparent" "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent"}
Cull Off ZWrite Off ZTest LEqual
Blend One OneMinusSrcAlpha, One OneMinusSrcAlpha

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
float4 _Color;
float4 _ShopClipRect;
sampler2D _MainTex;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 uv1:TEXCOORD1;float4 uv2:TEXCOORD2;float4 uv3:TEXCOORD3;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float2 vs_TEXCOORD0:TEXCOORD0;
float4 vs_COLOR0:TEXCOORD1;
float4 vs_TEXCOORD1:TEXCOORD2; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;

    u_xlat0.xyz = input.vertex.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat0 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    u_xlat0 = u_xlat0 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_TEXCOORD0.xy = input.texcoord.xy;
    output.vs_COLOR0 = input.color * _Color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_TEXCOORD1.zw = u_xlat0.zw;
    output.vs_TEXCOORD1.xy = u_xlat1.zz + u_xlat1.xw;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 sourceFragCoord=input.vertex;
#if UNITY_UV_STARTS_AT_TOP
sourceFragCoord.y=_ScreenParams.y-sourceFragCoord.y;
#endif
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float2 u_xlat2;

    u_xlat0.xy = input.vs_TEXCOORD1.xy / input.vs_TEXCOORD1.ww;
    u_xlat2.xy = u_xlat0.xy + (-_ShopClipRect.xy);
    u_xlat0.xy = (-u_xlat0.xy) + _ShopClipRect.zw;
    u_xlat0.xy = min(u_xlat0.xy, u_xlat2.xy);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_TEXCOORD0.xy,0,_GlobalMipBias.x));
    u_xlat0 = u_xlat16_0 * input.vs_COLOR0;
    result.xyz = u_xlat0.www * u_xlat0.xyz;
    result.w = u_xlat0.w;
    return result;

}

ENDHLSL
}
}
}
