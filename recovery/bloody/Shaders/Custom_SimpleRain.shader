// Source GLES expressions, original pass states and 16 URP2D shape-light combinations.
Shader "Custom/SimpleRain" {
Properties {
		_MainTex ("Rain Texture (R8)", 2D) = "white" {}
		_RainColor ("Rain Color", Vector) = (1,1,1,1)
		_OverallAlpha ("Overall Alpha", Range(0, 1)) = 1
		_ScrollSpeed ("Scroll Speed", Float) = 1
		_ScrollAngle ("Scroll Angle", Range(-1, 1)) = 0.1
		_TextureRotation ("Texture Rotation", Range(-10, 10)) = 0
		_WobbleSpeed ("Wobble Speed", Float) = 2
		_WobbleAmount ("Wobble Amount", Range(0, 0.1)) = 0.02
		_Cutoff ("Alpha Cutoff", Range(0, 1)) = 0.5
	}

SubShader {
Tags {"IGNOREPROJECTOR"="true" "QUEUE"="Transparent" "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent"}
Pass {
Name ""
Tags {"IGNOREPROJECTOR"="true" "LightMode"="Universal2D" "QUEUE"="Transparent" "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent"}
Cull Off ZWrite Off ZTest LEqual
Blend One One, One One

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
float4 _MainTex_ST;
float4 _RainColor;
float _OverallAlpha;
float _ScrollSpeed;
float _ScrollAngle;
float _TextureRotation;
float _WobbleSpeed;
float _WobbleAmount;
float _Cutoff;
sampler2D _MainTex;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float2 vs_TEXCOORD0:TEXCOORD0;
float vs_TEXCOORD1:TEXCOORD1; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat2;
float2 u_xlat6;

    u_xlat0 = input.vertex.xyz.yyyy * transpose(unity_ObjectToWorld)[1];
    u_xlat0 = transpose(unity_ObjectToWorld)[0] * input.vertex.xyz.xxxx + u_xlat0;
    u_xlat0 = transpose(unity_ObjectToWorld)[2] * input.vertex.xyz.zzzz + u_xlat0;
    u_xlat0 = u_xlat0 + transpose(unity_ObjectToWorld)[3];
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vertex = transpose(UNITY_MATRIX_VP)[3] * u_xlat0.wwww + u_xlat1;
    u_xlat0.xy = input.texcoord.yx * _MainTex_ST.yx + _MainTex_ST.wz;
    u_xlat0.xy = u_xlat0.xy + float2(-0.5,-0.5);
    u_xlat6.x = _TextureRotation * 0.5;
    u_xlat1.x = sin(u_xlat6.x);
    u_xlat2 = cos(u_xlat6.x);
    u_xlat6.xy = u_xlat0.xy * u_xlat1.xx;
    u_xlat1.x = u_xlat0.y * u_xlat2 + (-u_xlat6.x);
    u_xlat1.y = u_xlat0.x * u_xlat2 + u_xlat6.y;
    u_xlat0.xy = u_xlat1.xy + float2(0.5,0.5);
    u_xlat6.x = _Time.y * _ScrollSpeed;
    u_xlat1.x = u_xlat6.x * _ScrollAngle;
    u_xlat1.yz = u_xlat6.xx * float2(-1.0,-10.0);
    u_xlat6.xy = frac(u_xlat1.xy);
    u_xlat1.x = _Time.y * _WobbleSpeed + (-u_xlat1.z);
    u_xlat1.x = u_xlat1.x * 0.159154937;
    u_xlat1.x = frac(u_xlat1.x);
    output.vs_TEXCOORD1 = u_xlat1.x * 6.28318548;
    output.vs_TEXCOORD0.xy = u_xlat6.xy + u_xlat0.xy;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float2 u_xlat0;
float u_xlat16_1;
bool u_xlatb2;

    u_xlat0.x = input.vs_TEXCOORD0.y * 10.0 + input.vs_TEXCOORD1;
    u_xlat0.x = sin(u_xlat0.x);
    u_xlat0.x = u_xlat0.x * _WobbleAmount;
    u_xlat0.y = 0.0;
    u_xlat0.xy = u_xlat0.xy + input.vs_TEXCOORD0.xy;
    u_xlat0.x = tex2Dbias(_MainTex,float4(u_xlat0.xy,0,_GlobalMipBias.x)).x;
    u_xlatb2 = u_xlat0.x>=_Cutoff;
    u_xlat16_1 = (u_xlatb2) ? 1.0 : 0.0;
    u_xlat16_1 = u_xlat0.x * u_xlat16_1;
    u_xlat16_1 = u_xlat16_1 * _OverallAlpha;
    result.xyz = ((float3)(u_xlat16_1)) * _RainColor.xyz;
    result.w = 1.0;
    return result;

}

ENDHLSL
}
}
}
