// Source GLES expressions, original pass states and 16 URP2D shape-light combinations.
Shader "Shader Graphs/ItemPreview" {
Properties {
		[NoScaleOffset] _MainTex ("Main Texture", 2D) = "white" {}
		Color_4F6CF892 ("Tint Color", Vector) = (1,1,1,0.5372549)
		Vector1_86b5d72ee75f4e31a7bbd56dd845af23 ("Normal", Float) = 0
		[HideInInspector] _QueueOffset ("_QueueOffset", Float) = 0
		[HideInInspector] _QueueControl ("_QueueControl", Float) = -1
		[HideInInspector] [NoScaleOffset] unity_Lightmaps ("unity_Lightmaps", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_LightmapsInd ("unity_LightmapsInd", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_ShadowMasks ("unity_ShadowMasks", 2DArray) = "" {}
	}

SubShader {
Tags {"DisableBatching"="False" "QUEUE"="AlphaTest" "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "ShaderGraphShader"="true" "ShaderGraphTargetId"="UniversalUnlitSubTarget" "UniversalMaterialType"="Unlit"}
Pass {
Name "Universal Forward"
Tags {"DisableBatching"="False" "QUEUE"="AlphaTest" "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "ShaderGraphShader"="true" "ShaderGraphTargetId"="UniversalUnlitSubTarget" "UniversalMaterialType"="Unlit"}
Cull Back ZWrite On ZTest LEqual
Blend One Zero, One Zero

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
float4 Color_4F6CF892;
sampler2D _MainTex;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 uv1:TEXCOORD1;float4 uv2:TEXCOORD2;float4 uv3:TEXCOORD3;float4 uv4:TEXCOORD4;float4 uv5:TEXCOORD5;float4 uv6:TEXCOORD6;float4 uv7:TEXCOORD7;float4 color:COLOR;uint vertexId:SV_VertexID; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float3 vs_INTERP1:TEXCOORD1;
float3 vs_INTERP2:TEXCOORD2; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float3 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP1.xyz = u_xlat0.xyz;
    output.vertex = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vs_INTERP0 = input.texcoord;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP2.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 sourceFragCoord=input.vertex;
#if UNITY_UV_STARTS_AT_TOP
sourceFragCoord.y=_ScreenParams.y-sourceFragCoord.y;
#endif
float4 u_xlat16_0;
bool u_xlatb0;
float u_xlat16_1;
float3 u_xlat16_2;
float u_xlat16_4;
float u_xlat16_7;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat16_1 = ddx(u_xlat16_0.w);
    u_xlat16_4 = ddy(u_xlat16_0.w);
    u_xlat16_1 = abs(u_xlat16_4) + abs(u_xlat16_1);
    u_xlat16_4 = u_xlat16_0.w + -0.5;
    u_xlat16_2.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    result.xyz = u_xlat16_2.xyz * float3(1.0,0.971599996,0.92900002);
    u_xlat16_7 = (-u_xlat16_1) * 0.5 + u_xlat16_4;
    u_xlat16_1 = max(u_xlat16_1, 9.99999975e-05);
    u_xlat16_1 = u_xlat16_7 / u_xlat16_1;
    u_xlat16_1 = u_xlat16_1 + 1.0;
    u_xlat16_1 = clamp(u_xlat16_1, 0.0, 1.0);
    u_xlat16_7 = u_xlat16_1 + -9.99999975e-05;
    u_xlatb0 = _AlphaToMaskAvailable!=0.0;
    u_xlat16_4 = (u_xlatb0) ? u_xlat16_7 : u_xlat16_4;
    result.w = (u_xlatb0) ? u_xlat16_1 : 1.0;
    u_xlatb0 = u_xlat16_4<0.0;
    if(u_xlatb0){discard;}
    return result;

}

ENDHLSL
}
Pass {
Name "DepthOnly"
Tags {"DisableBatching"="False" "LightMode"="DepthOnly" "QUEUE"="AlphaTest" "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "ShaderGraphShader"="true" "ShaderGraphTargetId"="UniversalUnlitSubTarget" "UniversalMaterialType"="Unlit"}
Cull Back ZWrite On ZTest LEqual
Blend One Zero, One Zero

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
sampler2D _MainTex;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 uv1:TEXCOORD1;float4 uv2:TEXCOORD2;float4 uv3:TEXCOORD3;float4 uv4:TEXCOORD4;float4 uv5:TEXCOORD5;float4 uv6:TEXCOORD6;float4 uv7:TEXCOORD7;float4 color:COLOR;uint vertexId:SV_VertexID; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0; };
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
    output.vertex = u_xlat0 + transpose(UNITY_MATRIX_VP)[3];
    output.vs_INTERP0 = input.texcoord;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 sourceFragCoord=input.vertex;
#if UNITY_UV_STARTS_AT_TOP
sourceFragCoord.y=_ScreenParams.y-sourceFragCoord.y;
#endif
float u_xlat16_0;
bool u_xlatb0;
float u_xlat16_1;

float4 hlslcc_FragCoord = float4(sourceFragCoord.xyz,1.0/sourceFragCoord.w);
    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x)).w;
    u_xlat16_1 = u_xlat16_0 + -0.5;
    u_xlatb0 = u_xlat16_1<0.0;
    if(u_xlatb0){discard;}
    result = hlslcc_FragCoord.zzzz;
    return result;

}

ENDHLSL
}
Pass {
Name "MotionVectors"
Tags {"DisableBatching"="False" "LightMode"="MOTIONVECTORS" "QUEUE"="AlphaTest" "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "ShaderGraphShader"="true" "ShaderGraphTargetId"="UniversalUnlitSubTarget" "UniversalMaterialType"="Unlit"}
Cull Back ZWrite On ZTest LEqual
Blend One Zero, One Zero

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
sampler2D _MainTex;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 uv1:TEXCOORD1;float4 uv2:TEXCOORD2;float4 uv3:TEXCOORD3;float4 uv4:TEXCOORD4;float4 uv5:TEXCOORD5;float4 uv6:TEXCOORD6;float4 uv7:TEXCOORD7;float4 color:COLOR;uint vertexId:SV_VertexID; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float3 vs_CLIP_POSITION_NO_JITTER0:TEXCOORD1;
float3 vs_PREVIOUS_CLIP_POSITION_NO_JITTER0:TEXCOORD2; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float3 u_xlat2;
bool u_xlatb9;

    u_xlat0.xyz = input.vertex.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vertex = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    u_xlatb9 = unity_MotionVectorsParams.y!=0.0;
    if(u_xlatb9){
        u_xlatb9 = unity_MotionVectorsParams.x==1.0;
        u_xlat1.xyz = (bool(u_xlatb9)) ? input.uv4.xyz : input.vertex.xyz;
        u_xlat2.xyz = u_xlat0.yyy * transpose(_NonJitteredViewProjMatrix)[1].xyw;
        u_xlat0.xyw = transpose(_NonJitteredViewProjMatrix)[0].xyw * u_xlat0.xxx + u_xlat2.xyz;
        u_xlat0.xyz = transpose(_NonJitteredViewProjMatrix)[2].xyw * u_xlat0.zzz + u_xlat0.xyw;
        output.vs_CLIP_POSITION_NO_JITTER0.xyz = u_xlat0.xyz + transpose(_NonJitteredViewProjMatrix)[3].xyw;
        u_xlat0 = u_xlat1.yyyy * transpose(unity_MatrixPreviousM)[1];
        u_xlat0 = transpose(unity_MatrixPreviousM)[0] * u_xlat1.xxxx + u_xlat0;
        u_xlat0 = transpose(unity_MatrixPreviousM)[2] * u_xlat1.zzzz + u_xlat0;
        u_xlat0 = u_xlat0 + transpose(unity_MatrixPreviousM)[3];
        u_xlat1.xyz = u_xlat0.yyy * transpose(_PrevViewProjMatrix)[1].xyw;
        u_xlat1.xyz = transpose(_PrevViewProjMatrix)[0].xyw * u_xlat0.xxx + u_xlat1.xyz;
        u_xlat0.xyz = transpose(_PrevViewProjMatrix)[2].xyw * u_xlat0.zzz + u_xlat1.xyz;
        output.vs_PREVIOUS_CLIP_POSITION_NO_JITTER0.xyz = transpose(_PrevViewProjMatrix)[3].xyw * u_xlat0.www + u_xlat0.xyz;
    } else {
        output.vs_CLIP_POSITION_NO_JITTER0.xyz = float3(0.0,0.0,0.0);
        output.vs_PREVIOUS_CLIP_POSITION_NO_JITTER0.xyz = float3(0.0,0.0,0.0);
    }
    output.vs_INTERP0 = input.texcoord;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 sourceFragCoord=input.vertex;
#if UNITY_UV_STARTS_AT_TOP
sourceFragCoord.y=_ScreenParams.y-sourceFragCoord.y;
#endif
float2 u_xlat0;
float u_xlat16_0;
bool u_xlatb0;
float u_xlat16_1;
float u_xlat4;
bool u_xlatb4;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x)).w;
    u_xlat16_1 = u_xlat16_0 + -0.5;
    u_xlatb0 = u_xlat16_1<0.0;
    if(u_xlatb0){discard;}
    u_xlat0.x = float(1.0) / float(input.vs_PREVIOUS_CLIP_POSITION_NO_JITTER0.z);
    u_xlat0.xy = u_xlat0.xx * input.vs_PREVIOUS_CLIP_POSITION_NO_JITTER0.xy;
    u_xlat4 = float(1.0) / float(input.vs_CLIP_POSITION_NO_JITTER0.z);
    u_xlat0.xy = input.vs_CLIP_POSITION_NO_JITTER0.xy * ((float2)(u_xlat4)) + (-u_xlat0.xy);
    u_xlat0.xy = u_xlat0.xy * float2(0.5,0.5);
    u_xlatb4 = unity_MotionVectorsParams.y!=0.0;
    result.xy = bool(u_xlatb4) ? u_xlat0.xy : float2(0.0,0.0);
    result.zw = float2(0.0,0.0);
    return result;

}

ENDHLSL
}
Pass {
Name "DepthNormalsOnly"
Tags {"DisableBatching"="False" "LightMode"="DepthNormalsOnly" "QUEUE"="AlphaTest" "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "ShaderGraphShader"="true" "ShaderGraphTargetId"="UniversalUnlitSubTarget" "UniversalMaterialType"="Unlit"}
Cull Back ZWrite On ZTest LEqual
Blend One Zero, One Zero

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
sampler2D _MainTex;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 uv1:TEXCOORD1;float4 uv2:TEXCOORD2;float4 uv3:TEXCOORD3;float4 uv4:TEXCOORD4;float4 uv5:TEXCOORD5;float4 uv6:TEXCOORD6;float4 uv7:TEXCOORD7;float4 color:COLOR;uint vertexId:SV_VertexID; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float3 vs_INTERP1:TEXCOORD1; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat0 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vertex = u_xlat0 + transpose(UNITY_MATRIX_VP)[3];
    output.vs_INTERP0 = input.texcoord;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP1.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 sourceFragCoord=input.vertex;
#if UNITY_UV_STARTS_AT_TOP
sourceFragCoord.y=_ScreenParams.y-sourceFragCoord.y;
#endif
float3 u_xlat0;
float u_xlat16_0;
bool u_xlatb0;
float u_xlat16_1;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x)).w;
    u_xlat16_1 = u_xlat16_0 + -0.5;
    u_xlatb0 = u_xlat16_1<0.0;
    if(u_xlatb0){discard;}
    u_xlat0.x = dot(input.vs_INTERP1.xyz, input.vs_INTERP1.xyz);
    u_xlat0.x = rsqrt(u_xlat0.x);
    u_xlat0.xyz = u_xlat0.xxx * input.vs_INTERP1.xyz;
    result.xyz = u_xlat0.xyz;
    result.w = 0.0;
    return result;

}

ENDHLSL
}
}
}
