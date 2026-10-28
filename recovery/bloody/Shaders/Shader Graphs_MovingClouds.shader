// Source GLES expressions, original pass states and 16 URP2D shape-light combinations.
Shader "Shader Graphs/MovingClouds" {
Properties {
		[NoScaleOffset] _MainTex ("Main Texture", 2D) = "white" {}
		Vector1_fa6fa116710f4857905d550a8a83c964 ("Wind Speed", Float) = 0.05
		_Color ("Color", Vector) = (1,1,1,0)
		[HideInInspector] White ("Color", Vector) = (1,1,1,1)
		[HideInInspector] [NoScaleOffset] unity_Lightmaps ("unity_Lightmaps", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_LightmapsInd ("unity_LightmapsInd", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_ShadowMasks ("unity_ShadowMasks", 2DArray) = "" {}
	[PerRendererData] _RendererColor ("Renderer color", Color)=(1,1,1,1)
}

SubShader {
Tags {"QUEUE"="Transparent" "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "ShaderGraphShader"="true" "ShaderGraphTargetId"="UniversalSpriteUnlitSubTarget" "UniversalMaterialType"="Unlit"}
Pass {
Name "Sprite Unlit"
Tags {"LightMode"="Universal2D" "QUEUE"="Transparent" "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "ShaderGraphShader"="true" "ShaderGraphTargetId"="UniversalSpriteUnlitSubTarget" "UniversalMaterialType"="Unlit"}
Cull Off ZWrite Off ZTest LEqual
Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
float4 _RendererColor;
float4 _TimeParameters;
float Vector1_fa6fa116710f4857905d550a8a83c964;
float4 _Color;
sampler2D _MainTex;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float3 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float3 u_xlat2;
float u_xlat6;

    u_xlat0.xy = input.vertex.xyz.xy * unity_SpriteProps.xy;
    u_xlat2.xyz = u_xlat0.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * u_xlat0.xxx + u_xlat2.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP2.xyz = u_xlat0.xyz;
    output.vertex = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vs_INTERP0 = input.texcoord;
    u_xlat0 = _RendererColor * unity_SpriteColor;
    output.vs_INTERP1 = u_xlat0 * input.color;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP3.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
float u_xlat1;
bool u_xlatb1;

    u_xlat0.x = _TimeParameters.x * Vector1_fa6fa116710f4857905d550a8a83c964;
    u_xlat0.y = 0.0;
    u_xlat0.xy = u_xlat0.xy + input.vs_INTERP0.xy;
    u_xlat16_0 = tex2Dbias(_MainTex,float4(u_xlat0.xy,0,_GlobalMipBias.x));
    u_xlatb1 = u_xlat16_0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat16_0.xyz = u_xlat16_0.xyz * _Color.xyz;
    u_xlat1 = u_xlat16_0.w + -0.5;
    u_xlatb1 = u_xlat1<0.0;
    if(u_xlatb1){discard;}
    u_xlat0 = u_xlat16_0 * input.vs_INTERP1;
    result = u_xlat0;
    return result;

}

ENDHLSL
}
Pass {
Name "Sprite Unlit"
Tags {"LightMode"="UniversalForward" "QUEUE"="Transparent" "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "ShaderGraphShader"="true" "ShaderGraphTargetId"="UniversalSpriteUnlitSubTarget" "UniversalMaterialType"="Unlit"}
Cull Off ZWrite Off ZTest LEqual
Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
float4 _RendererColor;
float4 _TimeParameters;
float Vector1_fa6fa116710f4857905d550a8a83c964;
float4 _Color;
sampler2D _MainTex;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float3 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float3 u_xlat2;
float u_xlat6;

    u_xlat0.xy = input.vertex.xyz.xy * unity_SpriteProps.xy;
    u_xlat2.xyz = u_xlat0.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * u_xlat0.xxx + u_xlat2.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP2.xyz = u_xlat0.xyz;
    output.vertex = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vs_INTERP0 = input.texcoord;
    u_xlat0 = _RendererColor * unity_SpriteColor;
    output.vs_INTERP1 = u_xlat0 * input.color;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP3.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb1;

    u_xlat0.x = _TimeParameters.x * Vector1_fa6fa116710f4857905d550a8a83c964;
    u_xlat0.y = 0.0;
    u_xlat0.xy = u_xlat0.xy + input.vs_INTERP0.xy;
    u_xlat16_0 = tex2Dbias(_MainTex,float4(u_xlat0.xy,0,_GlobalMipBias.x));
    u_xlatb1 = u_xlat16_0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat16_0.xyz = u_xlat16_0.xyz * _Color.xyz;
    u_xlat0 = u_xlat16_0 * input.vs_INTERP1;
    result = u_xlat0;
    return result;

}

ENDHLSL
}
}
}
