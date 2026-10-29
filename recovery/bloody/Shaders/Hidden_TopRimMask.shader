// Source GLES expressions, original pass states and 16 URP2D shape-light combinations.
Shader "Hidden/TopRimMask" {
Properties {
		[PerRendererData] _MainTex ("Texture", 2D) = "white" {}
		_Erases_Bottom_of_L1 ("Erases Bottom", Float) = 0
	}

SubShader {
Tags {"RenderPipeline"="UniversalPipeline"}
Pass {
Name ""
Tags {"RenderPipeline"="UniversalPipeline"}
Cull Off ZWrite Off ZTest LEqual
Blend One One, One One

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
float _TopRimMaskBlackThreshold;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 uv1:TEXCOORD1;float4 uv2:TEXCOORD2;float4 uv3:TEXCOORD3;float4 uv4:TEXCOORD4;float4 uv5:TEXCOORD5;float4 uv6:TEXCOORD6;float4 uv7:TEXCOORD7;float4 color:COLOR;uint vertexId:SV_VertexID; };
struct varying { float4 vertex:SV_POSITION;float2 vs_TEXCOORD0:TEXCOORD0; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;

    u_xlat0 = input.vertex.yyyy * transpose(unity_ObjectToWorld)[1];
    u_xlat0 = transpose(unity_ObjectToWorld)[0] * input.vertex.xxxx + u_xlat0;
    u_xlat0 = transpose(unity_ObjectToWorld)[2] * input.vertex.zzzz + u_xlat0;
    u_xlat0 = u_xlat0 + transpose(unity_ObjectToWorld)[3];
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vertex = transpose(UNITY_MATRIX_VP)[3] * u_xlat0.wwww + u_xlat1;
    output.vs_TEXCOORD0.xy = input.texcoord.xy;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 sourceFragCoord=input.vertex;
#if UNITY_UV_STARTS_AT_TOP
sourceFragCoord.y=_ScreenParams.y-sourceFragCoord.y;
#endif
float3 u_xlat0;
float4 u_xlat16_0;
bool3 u_xlatb0;
float u_xlat1;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_TEXCOORD0.xy,0,_GlobalMipBias.x));
    u_xlat1 = max(u_xlat16_0.z, u_xlat16_0.y);
    u_xlat0.x = max(u_xlat1, u_xlat16_0.x);
    u_xlatb0.x = u_xlat0.x>=_TopRimMaskBlackThreshold;
    u_xlatb0.y = _Erases_Bottom_of_L1>=0.5;
    u_xlatb0.z = 0.5>=input.vs_TEXCOORD0.y;
    u_xlat0.x = u_xlatb0.x ? float(1.0) : 0.0;
    u_xlat0.y = u_xlatb0.y ? float(1.0) : 0.0;
    u_xlat0.z = u_xlatb0.z ? float(1.0) : 0.0;
;
    u_xlat1 = (-u_xlat0.y) * u_xlat0.z + 1.0;
    u_xlat1 = u_xlat1 * u_xlat16_0.w;
    u_xlat0.x = u_xlat1 * u_xlat0.x + -0.5;
    u_xlatb0.x = u_xlat0.x<0.0;
    if(u_xlatb0.x){discard;}
    result = float4(1.0,0.0,0.0,1.0);
    return result;

}

ENDHLSL
}
}
}
