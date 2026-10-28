// Source GLES expressions, original pass states and 16 URP2D shape-light combinations.
Shader "Shader Graphs/Character_TopL1" {
Properties {
		[NoScaleOffset] _MainTex ("Main Texture", 2D) = "white" {}
		Color_4F6CF892 ("Tint Color", Vector) = (1,1,1,0.5372549)
		Vector1_86b5d72ee75f4e31a7bbd56dd845af23 ("Normal", Float) = 0
		[ToggleUI] _Erases_Bottom_of_L1 ("Erases Bottom of L1", Float) = 0
		[HideInInspector] White ("Color", Vector) = (1,1,1,1)
		[HideInInspector] [NoScaleOffset] unity_Lightmaps ("unity_Lightmaps", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_LightmapsInd ("unity_LightmapsInd", 2DArray) = "" {}
		[HideInInspector] [NoScaleOffset] unity_ShadowMasks ("unity_ShadowMasks", 2DArray) = "" {}
	[PerRendererData] _RendererColor ("Renderer color", Color)=(1,1,1,1)
}

SubShader {
Tags {"QUEUE"="Transparent" "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "ShaderGraphShader"="true" "ShaderGraphTargetId"="UniversalSpriteLitSubTarget" "UniversalMaterialType"="Lit"}
Pass {
Name "Sprite Lit"
Tags {"LightMode"="Universal2D" "QUEUE"="Transparent" "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "ShaderGraphShader"="true" "ShaderGraphTargetId"="UniversalSpriteLitSubTarget" "UniversalMaterialType"="Lit"}
Cull Back ZWrite On ZTest LEqual
Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
Stencil { Ref 128 ReadMask 127 WriteMask 128 CompFront Always PassFront Replace FailFront Keep ZFailFront Keep CompBack Always PassBack Replace FailBack Keep ZFailBack Keep }

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5
#pragma multi_compile _ USE_SHAPE_LIGHT_TYPE_0
#pragma multi_compile _ USE_SHAPE_LIGHT_TYPE_1
#pragma multi_compile _ USE_SHAPE_LIGHT_TYPE_2
#pragma multi_compile _ USE_SHAPE_LIGHT_TYPE_3
#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
#if !defined(USE_SHAPE_LIGHT_TYPE_0) && !defined(USE_SHAPE_LIGHT_TYPE_1) && !defined(USE_SHAPE_LIGHT_TYPE_2) && !defined(USE_SHAPE_LIGHT_TYPE_3)
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
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
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
bool u_xlatb1;
float2 u_xlat2;
bool u_xlatb6;
float2 u_xlat8;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb6 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb6) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat8.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat8.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat16_0 = max(u_xlat0, float4(0.0,0.0,0.0,0.0));
    result = u_xlat16_0 * White;
    return result;

}

#endif
#if !defined(USE_SHAPE_LIGHT_TYPE_0) && !defined(USE_SHAPE_LIGHT_TYPE_1) && !defined(USE_SHAPE_LIGHT_TYPE_2) && defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors3;
float4 _ShapeLightMaskFilter3;
float4 _ShapeLightInvertedFilter3;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture3;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float3 u_xlat16_3;
float3 u_xlat16_4;
bool u_xlatb10;
float2 u_xlat12;
bool u_xlatb16;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb10 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb10) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat12.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat12.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture3,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter3, _ShapeLightMaskFilter3);
    u_xlatb16 = u_xlat16_3.x!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter3) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_2, _ShapeLightMaskFilter3);
    u_xlat16_3.xyz = u_xlat16_1.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb16)) ? u_xlat16_3.xyz : u_xlat16_1.xyz;
    u_xlat16_4.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors3.xxx;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors3.yyy;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_4.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
#if !defined(USE_SHAPE_LIGHT_TYPE_0) && !defined(USE_SHAPE_LIGHT_TYPE_1) && defined(USE_SHAPE_LIGHT_TYPE_2) && !defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors2;
float4 _ShapeLightMaskFilter2;
float4 _ShapeLightInvertedFilter2;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture2;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float3 u_xlat16_3;
float3 u_xlat16_4;
bool u_xlatb10;
float2 u_xlat12;
bool u_xlatb16;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb10 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb10) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat12.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat12.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture2,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter2, _ShapeLightMaskFilter2);
    u_xlatb16 = u_xlat16_3.x!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter2) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_2, _ShapeLightMaskFilter2);
    u_xlat16_3.xyz = u_xlat16_1.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb16)) ? u_xlat16_3.xyz : u_xlat16_1.xyz;
    u_xlat16_4.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors2.xxx;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors2.yyy;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_4.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
#if !defined(USE_SHAPE_LIGHT_TYPE_0) && !defined(USE_SHAPE_LIGHT_TYPE_1) && defined(USE_SHAPE_LIGHT_TYPE_2) && defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors2;
float4 _ShapeLightMaskFilter2;
float4 _ShapeLightInvertedFilter2;
float2 _ShapeLightBlendFactors3;
float4 _ShapeLightMaskFilter3;
float4 _ShapeLightInvertedFilter3;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture2;
sampler2D _ShapeLightTexture3;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float4 u_xlat16_3;
float3 u_xlat16_4;
float3 u_xlat16_5;
bool u_xlatb12;
bool u_xlatb13;
float2 u_xlat14;
bool u_xlatb19;
float u_xlat16_21;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb12 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb12) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat14.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat14.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture2,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter2, _ShapeLightMaskFilter2);
    u_xlatb13 = u_xlat16_3.x!=0.0;
    u_xlat16_3 = (-_ShapeLightInvertedFilter2) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_3, _ShapeLightMaskFilter2);
    u_xlat16_3.xyz = u_xlat16_2.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb13)) ? u_xlat16_3.xyz : u_xlat16_2.xyz;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture3,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_21 = dot(_ShapeLightMaskFilter3, _ShapeLightMaskFilter3);
    u_xlatb19 = u_xlat16_21!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter3) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_21 = dot(u_xlat16_2, _ShapeLightMaskFilter3);
    u_xlat16_4.xyz = u_xlat16_1.xyz * ((float3)(u_xlat16_21));
    u_xlat16_4.xyz = (bool(u_xlatb19)) ? u_xlat16_4.xyz : u_xlat16_1.xyz;
    u_xlat16_5.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors3.xxx;
    u_xlat16_4.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors3.yyy;
    u_xlat16_5.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors2.xxx + u_xlat16_5.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors2.yyy + u_xlat16_4.xyz;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_5.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
#if !defined(USE_SHAPE_LIGHT_TYPE_0) && defined(USE_SHAPE_LIGHT_TYPE_1) && !defined(USE_SHAPE_LIGHT_TYPE_2) && !defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors1;
float4 _ShapeLightMaskFilter1;
float4 _ShapeLightInvertedFilter1;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture1;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float3 u_xlat16_3;
float3 u_xlat16_4;
bool u_xlatb10;
float2 u_xlat12;
bool u_xlatb16;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb10 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb10) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat12.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat12.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture1,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter1, _ShapeLightMaskFilter1);
    u_xlatb16 = u_xlat16_3.x!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter1) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_2, _ShapeLightMaskFilter1);
    u_xlat16_3.xyz = u_xlat16_1.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb16)) ? u_xlat16_3.xyz : u_xlat16_1.xyz;
    u_xlat16_4.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors1.xxx;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors1.yyy;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_4.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
#if !defined(USE_SHAPE_LIGHT_TYPE_0) && defined(USE_SHAPE_LIGHT_TYPE_1) && !defined(USE_SHAPE_LIGHT_TYPE_2) && defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors1;
float4 _ShapeLightMaskFilter1;
float4 _ShapeLightInvertedFilter1;
float2 _ShapeLightBlendFactors3;
float4 _ShapeLightMaskFilter3;
float4 _ShapeLightInvertedFilter3;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture1;
sampler2D _ShapeLightTexture3;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float4 u_xlat16_3;
float3 u_xlat16_4;
float3 u_xlat16_5;
bool u_xlatb12;
bool u_xlatb13;
float2 u_xlat14;
bool u_xlatb19;
float u_xlat16_21;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb12 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb12) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat14.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat14.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture1,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter1, _ShapeLightMaskFilter1);
    u_xlatb13 = u_xlat16_3.x!=0.0;
    u_xlat16_3 = (-_ShapeLightInvertedFilter1) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_3, _ShapeLightMaskFilter1);
    u_xlat16_3.xyz = u_xlat16_2.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb13)) ? u_xlat16_3.xyz : u_xlat16_2.xyz;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture3,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_21 = dot(_ShapeLightMaskFilter3, _ShapeLightMaskFilter3);
    u_xlatb19 = u_xlat16_21!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter3) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_21 = dot(u_xlat16_2, _ShapeLightMaskFilter3);
    u_xlat16_4.xyz = u_xlat16_1.xyz * ((float3)(u_xlat16_21));
    u_xlat16_4.xyz = (bool(u_xlatb19)) ? u_xlat16_4.xyz : u_xlat16_1.xyz;
    u_xlat16_5.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors3.xxx;
    u_xlat16_4.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors3.yyy;
    u_xlat16_5.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors1.xxx + u_xlat16_5.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors1.yyy + u_xlat16_4.xyz;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_5.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
#if !defined(USE_SHAPE_LIGHT_TYPE_0) && defined(USE_SHAPE_LIGHT_TYPE_1) && defined(USE_SHAPE_LIGHT_TYPE_2) && !defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors1;
float4 _ShapeLightMaskFilter1;
float4 _ShapeLightInvertedFilter1;
float2 _ShapeLightBlendFactors2;
float4 _ShapeLightMaskFilter2;
float4 _ShapeLightInvertedFilter2;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture1;
sampler2D _ShapeLightTexture2;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float4 u_xlat16_3;
float3 u_xlat16_4;
float3 u_xlat16_5;
bool u_xlatb12;
bool u_xlatb13;
float2 u_xlat14;
bool u_xlatb19;
float u_xlat16_21;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb12 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb12) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat14.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat14.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture1,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter1, _ShapeLightMaskFilter1);
    u_xlatb13 = u_xlat16_3.x!=0.0;
    u_xlat16_3 = (-_ShapeLightInvertedFilter1) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_3, _ShapeLightMaskFilter1);
    u_xlat16_3.xyz = u_xlat16_2.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb13)) ? u_xlat16_3.xyz : u_xlat16_2.xyz;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture2,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_21 = dot(_ShapeLightMaskFilter2, _ShapeLightMaskFilter2);
    u_xlatb19 = u_xlat16_21!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter2) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_21 = dot(u_xlat16_2, _ShapeLightMaskFilter2);
    u_xlat16_4.xyz = u_xlat16_1.xyz * ((float3)(u_xlat16_21));
    u_xlat16_4.xyz = (bool(u_xlatb19)) ? u_xlat16_4.xyz : u_xlat16_1.xyz;
    u_xlat16_5.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors2.xxx;
    u_xlat16_4.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors2.yyy;
    u_xlat16_5.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors1.xxx + u_xlat16_5.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors1.yyy + u_xlat16_4.xyz;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_5.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
#if !defined(USE_SHAPE_LIGHT_TYPE_0) && defined(USE_SHAPE_LIGHT_TYPE_1) && defined(USE_SHAPE_LIGHT_TYPE_2) && defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors1;
float4 _ShapeLightMaskFilter1;
float4 _ShapeLightInvertedFilter1;
float2 _ShapeLightBlendFactors2;
float4 _ShapeLightMaskFilter2;
float4 _ShapeLightInvertedFilter2;
float2 _ShapeLightBlendFactors3;
float4 _ShapeLightMaskFilter3;
float4 _ShapeLightInvertedFilter3;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture1;
sampler2D _ShapeLightTexture2;
sampler2D _ShapeLightTexture3;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float4 u_xlat16_3;
float4 u_xlat16_4;
float3 u_xlat16_5;
float3 u_xlat16_6;
bool u_xlatb14;
bool u_xlatb15;
float2 u_xlat16;
bool u_xlatb22;
float u_xlat16_24;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb14 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb14) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat16.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat16.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture1,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter1, _ShapeLightMaskFilter1);
    u_xlatb15 = u_xlat16_3.x!=0.0;
    u_xlat16_3 = (-_ShapeLightInvertedFilter1) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_3, _ShapeLightMaskFilter1);
    u_xlat16_3.xyz = u_xlat16_2.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb15)) ? u_xlat16_3.xyz : u_xlat16_2.xyz;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture2,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_24 = dot(_ShapeLightMaskFilter2, _ShapeLightMaskFilter2);
    u_xlatb15 = u_xlat16_24!=0.0;
    u_xlat16_4 = (-_ShapeLightInvertedFilter2) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_24 = dot(u_xlat16_4, _ShapeLightMaskFilter2);
    u_xlat16_4.xyz = u_xlat16_2.xyz * ((float3)(u_xlat16_24));
    u_xlat16_4.xyz = (bool(u_xlatb15)) ? u_xlat16_4.xyz : u_xlat16_2.xyz;
    u_xlat16_5.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors2.xxx;
    u_xlat16_4.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors2.yyy;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture3,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_24 = dot(_ShapeLightMaskFilter3, _ShapeLightMaskFilter3);
    u_xlatb22 = u_xlat16_24!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter3) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_24 = dot(u_xlat16_2, _ShapeLightMaskFilter3);
    u_xlat16_6.xyz = u_xlat16_1.xyz * ((float3)(u_xlat16_24));
    u_xlat16_6.xyz = (bool(u_xlatb22)) ? u_xlat16_6.xyz : u_xlat16_1.xyz;
    u_xlat16_5.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors1.xxx + u_xlat16_5.xyz;
    u_xlat16_5.xyz = u_xlat16_6.xyz * _ShapeLightBlendFactors3.xxx + u_xlat16_5.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors1.yyy + u_xlat16_4.xyz;
    u_xlat16_3.xyz = u_xlat16_6.xyz * _ShapeLightBlendFactors3.yyy + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_5.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
#if defined(USE_SHAPE_LIGHT_TYPE_0) && !defined(USE_SHAPE_LIGHT_TYPE_1) && !defined(USE_SHAPE_LIGHT_TYPE_2) && !defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors0;
float4 _ShapeLightMaskFilter0;
float4 _ShapeLightInvertedFilter0;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture0;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float3 u_xlat16_3;
float3 u_xlat16_4;
bool u_xlatb10;
float2 u_xlat12;
bool u_xlatb16;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb10 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb10) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat12.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat12.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture0,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter0, _ShapeLightMaskFilter0);
    u_xlatb16 = u_xlat16_3.x!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter0) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_2, _ShapeLightMaskFilter0);
    u_xlat16_3.xyz = u_xlat16_1.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb16)) ? u_xlat16_3.xyz : u_xlat16_1.xyz;
    u_xlat16_4.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.xxx;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.yyy;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_4.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
#if defined(USE_SHAPE_LIGHT_TYPE_0) && !defined(USE_SHAPE_LIGHT_TYPE_1) && !defined(USE_SHAPE_LIGHT_TYPE_2) && defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors0;
float4 _ShapeLightMaskFilter0;
float4 _ShapeLightInvertedFilter0;
float2 _ShapeLightBlendFactors3;
float4 _ShapeLightMaskFilter3;
float4 _ShapeLightInvertedFilter3;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture0;
sampler2D _ShapeLightTexture3;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float4 u_xlat16_3;
float3 u_xlat16_4;
float3 u_xlat16_5;
bool u_xlatb12;
bool u_xlatb13;
float2 u_xlat14;
bool u_xlatb19;
float u_xlat16_21;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb12 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb12) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat14.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat14.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture0,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter0, _ShapeLightMaskFilter0);
    u_xlatb13 = u_xlat16_3.x!=0.0;
    u_xlat16_3 = (-_ShapeLightInvertedFilter0) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_3, _ShapeLightMaskFilter0);
    u_xlat16_3.xyz = u_xlat16_2.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb13)) ? u_xlat16_3.xyz : u_xlat16_2.xyz;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture3,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_21 = dot(_ShapeLightMaskFilter3, _ShapeLightMaskFilter3);
    u_xlatb19 = u_xlat16_21!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter3) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_21 = dot(u_xlat16_2, _ShapeLightMaskFilter3);
    u_xlat16_4.xyz = u_xlat16_1.xyz * ((float3)(u_xlat16_21));
    u_xlat16_4.xyz = (bool(u_xlatb19)) ? u_xlat16_4.xyz : u_xlat16_1.xyz;
    u_xlat16_5.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors3.xxx;
    u_xlat16_4.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors3.yyy;
    u_xlat16_5.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.xxx + u_xlat16_5.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.yyy + u_xlat16_4.xyz;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_5.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
#if defined(USE_SHAPE_LIGHT_TYPE_0) && !defined(USE_SHAPE_LIGHT_TYPE_1) && defined(USE_SHAPE_LIGHT_TYPE_2) && !defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors0;
float4 _ShapeLightMaskFilter0;
float4 _ShapeLightInvertedFilter0;
float2 _ShapeLightBlendFactors2;
float4 _ShapeLightMaskFilter2;
float4 _ShapeLightInvertedFilter2;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture0;
sampler2D _ShapeLightTexture2;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float4 u_xlat16_3;
float3 u_xlat16_4;
float3 u_xlat16_5;
bool u_xlatb12;
bool u_xlatb13;
float2 u_xlat14;
bool u_xlatb19;
float u_xlat16_21;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb12 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb12) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat14.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat14.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture0,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter0, _ShapeLightMaskFilter0);
    u_xlatb13 = u_xlat16_3.x!=0.0;
    u_xlat16_3 = (-_ShapeLightInvertedFilter0) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_3, _ShapeLightMaskFilter0);
    u_xlat16_3.xyz = u_xlat16_2.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb13)) ? u_xlat16_3.xyz : u_xlat16_2.xyz;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture2,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_21 = dot(_ShapeLightMaskFilter2, _ShapeLightMaskFilter2);
    u_xlatb19 = u_xlat16_21!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter2) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_21 = dot(u_xlat16_2, _ShapeLightMaskFilter2);
    u_xlat16_4.xyz = u_xlat16_1.xyz * ((float3)(u_xlat16_21));
    u_xlat16_4.xyz = (bool(u_xlatb19)) ? u_xlat16_4.xyz : u_xlat16_1.xyz;
    u_xlat16_5.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors2.xxx;
    u_xlat16_4.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors2.yyy;
    u_xlat16_5.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.xxx + u_xlat16_5.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.yyy + u_xlat16_4.xyz;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_5.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
#if defined(USE_SHAPE_LIGHT_TYPE_0) && !defined(USE_SHAPE_LIGHT_TYPE_1) && defined(USE_SHAPE_LIGHT_TYPE_2) && defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors0;
float4 _ShapeLightMaskFilter0;
float4 _ShapeLightInvertedFilter0;
float2 _ShapeLightBlendFactors2;
float4 _ShapeLightMaskFilter2;
float4 _ShapeLightInvertedFilter2;
float2 _ShapeLightBlendFactors3;
float4 _ShapeLightMaskFilter3;
float4 _ShapeLightInvertedFilter3;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture0;
sampler2D _ShapeLightTexture2;
sampler2D _ShapeLightTexture3;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float4 u_xlat16_3;
float4 u_xlat16_4;
float3 u_xlat16_5;
float3 u_xlat16_6;
bool u_xlatb14;
bool u_xlatb15;
float2 u_xlat16;
bool u_xlatb22;
float u_xlat16_24;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb14 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb14) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat16.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat16.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture0,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter0, _ShapeLightMaskFilter0);
    u_xlatb15 = u_xlat16_3.x!=0.0;
    u_xlat16_3 = (-_ShapeLightInvertedFilter0) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_3, _ShapeLightMaskFilter0);
    u_xlat16_3.xyz = u_xlat16_2.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb15)) ? u_xlat16_3.xyz : u_xlat16_2.xyz;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture2,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_24 = dot(_ShapeLightMaskFilter2, _ShapeLightMaskFilter2);
    u_xlatb15 = u_xlat16_24!=0.0;
    u_xlat16_4 = (-_ShapeLightInvertedFilter2) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_24 = dot(u_xlat16_4, _ShapeLightMaskFilter2);
    u_xlat16_4.xyz = u_xlat16_2.xyz * ((float3)(u_xlat16_24));
    u_xlat16_4.xyz = (bool(u_xlatb15)) ? u_xlat16_4.xyz : u_xlat16_2.xyz;
    u_xlat16_5.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors2.xxx;
    u_xlat16_4.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors2.yyy;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture3,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_24 = dot(_ShapeLightMaskFilter3, _ShapeLightMaskFilter3);
    u_xlatb22 = u_xlat16_24!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter3) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_24 = dot(u_xlat16_2, _ShapeLightMaskFilter3);
    u_xlat16_6.xyz = u_xlat16_1.xyz * ((float3)(u_xlat16_24));
    u_xlat16_6.xyz = (bool(u_xlatb22)) ? u_xlat16_6.xyz : u_xlat16_1.xyz;
    u_xlat16_5.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.xxx + u_xlat16_5.xyz;
    u_xlat16_5.xyz = u_xlat16_6.xyz * _ShapeLightBlendFactors3.xxx + u_xlat16_5.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.yyy + u_xlat16_4.xyz;
    u_xlat16_3.xyz = u_xlat16_6.xyz * _ShapeLightBlendFactors3.yyy + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_5.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
#if defined(USE_SHAPE_LIGHT_TYPE_0) && defined(USE_SHAPE_LIGHT_TYPE_1) && !defined(USE_SHAPE_LIGHT_TYPE_2) && !defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors0;
float4 _ShapeLightMaskFilter0;
float4 _ShapeLightInvertedFilter0;
float2 _ShapeLightBlendFactors1;
float4 _ShapeLightMaskFilter1;
float4 _ShapeLightInvertedFilter1;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture0;
sampler2D _ShapeLightTexture1;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float4 u_xlat16_3;
float3 u_xlat16_4;
float3 u_xlat16_5;
bool u_xlatb12;
bool u_xlatb13;
float2 u_xlat14;
bool u_xlatb19;
float u_xlat16_21;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb12 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb12) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat14.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat14.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture0,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter0, _ShapeLightMaskFilter0);
    u_xlatb13 = u_xlat16_3.x!=0.0;
    u_xlat16_3 = (-_ShapeLightInvertedFilter0) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_3, _ShapeLightMaskFilter0);
    u_xlat16_3.xyz = u_xlat16_2.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb13)) ? u_xlat16_3.xyz : u_xlat16_2.xyz;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture1,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_21 = dot(_ShapeLightMaskFilter1, _ShapeLightMaskFilter1);
    u_xlatb19 = u_xlat16_21!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter1) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_21 = dot(u_xlat16_2, _ShapeLightMaskFilter1);
    u_xlat16_4.xyz = u_xlat16_1.xyz * ((float3)(u_xlat16_21));
    u_xlat16_4.xyz = (bool(u_xlatb19)) ? u_xlat16_4.xyz : u_xlat16_1.xyz;
    u_xlat16_5.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors1.xxx;
    u_xlat16_4.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors1.yyy;
    u_xlat16_5.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.xxx + u_xlat16_5.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.yyy + u_xlat16_4.xyz;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_5.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
#if defined(USE_SHAPE_LIGHT_TYPE_0) && defined(USE_SHAPE_LIGHT_TYPE_1) && !defined(USE_SHAPE_LIGHT_TYPE_2) && defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors0;
float4 _ShapeLightMaskFilter0;
float4 _ShapeLightInvertedFilter0;
float2 _ShapeLightBlendFactors1;
float4 _ShapeLightMaskFilter1;
float4 _ShapeLightInvertedFilter1;
float2 _ShapeLightBlendFactors3;
float4 _ShapeLightMaskFilter3;
float4 _ShapeLightInvertedFilter3;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture0;
sampler2D _ShapeLightTexture1;
sampler2D _ShapeLightTexture3;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float4 u_xlat16_3;
float4 u_xlat16_4;
float3 u_xlat16_5;
float3 u_xlat16_6;
bool u_xlatb14;
bool u_xlatb15;
float2 u_xlat16;
bool u_xlatb22;
float u_xlat16_24;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb14 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb14) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat16.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat16.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture0,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter0, _ShapeLightMaskFilter0);
    u_xlatb15 = u_xlat16_3.x!=0.0;
    u_xlat16_3 = (-_ShapeLightInvertedFilter0) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_3, _ShapeLightMaskFilter0);
    u_xlat16_3.xyz = u_xlat16_2.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb15)) ? u_xlat16_3.xyz : u_xlat16_2.xyz;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture1,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_24 = dot(_ShapeLightMaskFilter1, _ShapeLightMaskFilter1);
    u_xlatb15 = u_xlat16_24!=0.0;
    u_xlat16_4 = (-_ShapeLightInvertedFilter1) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_24 = dot(u_xlat16_4, _ShapeLightMaskFilter1);
    u_xlat16_4.xyz = u_xlat16_2.xyz * ((float3)(u_xlat16_24));
    u_xlat16_4.xyz = (bool(u_xlatb15)) ? u_xlat16_4.xyz : u_xlat16_2.xyz;
    u_xlat16_5.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors1.xxx;
    u_xlat16_4.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors1.yyy;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture3,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_24 = dot(_ShapeLightMaskFilter3, _ShapeLightMaskFilter3);
    u_xlatb22 = u_xlat16_24!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter3) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_24 = dot(u_xlat16_2, _ShapeLightMaskFilter3);
    u_xlat16_6.xyz = u_xlat16_1.xyz * ((float3)(u_xlat16_24));
    u_xlat16_6.xyz = (bool(u_xlatb22)) ? u_xlat16_6.xyz : u_xlat16_1.xyz;
    u_xlat16_5.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.xxx + u_xlat16_5.xyz;
    u_xlat16_5.xyz = u_xlat16_6.xyz * _ShapeLightBlendFactors3.xxx + u_xlat16_5.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.yyy + u_xlat16_4.xyz;
    u_xlat16_3.xyz = u_xlat16_6.xyz * _ShapeLightBlendFactors3.yyy + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_5.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
#if defined(USE_SHAPE_LIGHT_TYPE_0) && defined(USE_SHAPE_LIGHT_TYPE_1) && defined(USE_SHAPE_LIGHT_TYPE_2) && !defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors0;
float4 _ShapeLightMaskFilter0;
float4 _ShapeLightInvertedFilter0;
float2 _ShapeLightBlendFactors1;
float4 _ShapeLightMaskFilter1;
float4 _ShapeLightInvertedFilter1;
float2 _ShapeLightBlendFactors2;
float4 _ShapeLightMaskFilter2;
float4 _ShapeLightInvertedFilter2;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture0;
sampler2D _ShapeLightTexture1;
sampler2D _ShapeLightTexture2;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float4 u_xlat16_3;
float4 u_xlat16_4;
float3 u_xlat16_5;
float3 u_xlat16_6;
bool u_xlatb14;
bool u_xlatb15;
float2 u_xlat16;
bool u_xlatb22;
float u_xlat16_24;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb14 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb14) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat16.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat16.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture0,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter0, _ShapeLightMaskFilter0);
    u_xlatb15 = u_xlat16_3.x!=0.0;
    u_xlat16_3 = (-_ShapeLightInvertedFilter0) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_3, _ShapeLightMaskFilter0);
    u_xlat16_3.xyz = u_xlat16_2.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb15)) ? u_xlat16_3.xyz : u_xlat16_2.xyz;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture1,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_24 = dot(_ShapeLightMaskFilter1, _ShapeLightMaskFilter1);
    u_xlatb15 = u_xlat16_24!=0.0;
    u_xlat16_4 = (-_ShapeLightInvertedFilter1) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_24 = dot(u_xlat16_4, _ShapeLightMaskFilter1);
    u_xlat16_4.xyz = u_xlat16_2.xyz * ((float3)(u_xlat16_24));
    u_xlat16_4.xyz = (bool(u_xlatb15)) ? u_xlat16_4.xyz : u_xlat16_2.xyz;
    u_xlat16_5.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors1.xxx;
    u_xlat16_4.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors1.yyy;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture2,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_24 = dot(_ShapeLightMaskFilter2, _ShapeLightMaskFilter2);
    u_xlatb22 = u_xlat16_24!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter2) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_24 = dot(u_xlat16_2, _ShapeLightMaskFilter2);
    u_xlat16_6.xyz = u_xlat16_1.xyz * ((float3)(u_xlat16_24));
    u_xlat16_6.xyz = (bool(u_xlatb22)) ? u_xlat16_6.xyz : u_xlat16_1.xyz;
    u_xlat16_5.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.xxx + u_xlat16_5.xyz;
    u_xlat16_5.xyz = u_xlat16_6.xyz * _ShapeLightBlendFactors2.xxx + u_xlat16_5.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.yyy + u_xlat16_4.xyz;
    u_xlat16_3.xyz = u_xlat16_6.xyz * _ShapeLightBlendFactors2.yyy + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_5.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
#if defined(USE_SHAPE_LIGHT_TYPE_0) && defined(USE_SHAPE_LIGHT_TYPE_1) && defined(USE_SHAPE_LIGHT_TYPE_2) && defined(USE_SHAPE_LIGHT_TYPE_3)
float2 _ShapeLightBlendFactors0;
float4 _ShapeLightMaskFilter0;
float4 _ShapeLightInvertedFilter0;
float2 _ShapeLightBlendFactors1;
float4 _ShapeLightMaskFilter1;
float4 _ShapeLightInvertedFilter1;
float2 _ShapeLightBlendFactors2;
float4 _ShapeLightMaskFilter2;
float4 _ShapeLightInvertedFilter2;
float2 _ShapeLightBlendFactors3;
float4 _ShapeLightMaskFilter3;
float4 _ShapeLightInvertedFilter3;
float _HDREmulationScale;
float4 White;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
sampler2D _MainTex;
sampler2D _ShapeLightTexture0;
sampler2D _ShapeLightTexture1;
sampler2D _ShapeLightTexture2;
sampler2D _ShapeLightTexture3;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float4 vs_INTERP0:TEXCOORD0;
float4 vs_INTERP1:TEXCOORD1;
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3;
float3 vs_INTERP4:TEXCOORD4; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat1 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vs_INTERP3.xyz = u_xlat0.xyz;
    u_xlat0 = u_xlat1 + transpose(UNITY_MATRIX_VP)[3];
    output.vertex = u_xlat0;
    output.vs_INTERP0 = input.texcoord;
    output.vs_INTERP1 = input.color;
    u_xlat0.y = u_xlat0.y * _ProjectionParams.x;
    u_xlat1.xzw = u_xlat0.xwy * float3(0.5,0.5,0.5);
    output.vs_INTERP2.zw = u_xlat0.zw;
    output.vs_INTERP2.xy = u_xlat1.zz + u_xlat1.xw;
    u_xlat0.x = dot(input.normal.xyz, transpose(unity_WorldToObject)[0].xyz);
    u_xlat0.y = dot(input.normal.xyz, transpose(unity_WorldToObject)[1].xyz);
    u_xlat0.z = dot(input.normal.xyz, transpose(unity_WorldToObject)[2].xyz);
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP4.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
float4 u_xlat16_0;
bool u_xlatb0;
float4 u_xlat1;
float4 u_xlat16_1;
bool u_xlatb1;
float2 u_xlat2;
float4 u_xlat16_2;
float4 u_xlat16_3;
float4 u_xlat16_4;
float3 u_xlat16_5;
float4 u_xlat16_6;
float3 u_xlat16_7;
bool u_xlatb16;
bool u_xlatb17;
float2 u_xlat18;
bool u_xlatb25;
float u_xlat16_27;

    u_xlat16_0 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat1.xyz = u_xlat16_0.xyz * Color_4F6CF892.xyz;
    u_xlat0.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlatb16 = (_Erases_Bottom_of_L1 != 0);
    u_xlat2.y = (u_xlatb16) ? 1.0 : 2.0;
    u_xlat0.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat2.x = 1.0;
    u_xlat0.xy = (-u_xlat2.xy) + abs(u_xlat0.xy);
    u_xlat2.xy = ddx(u_xlat0.xy);
    u_xlat18.xy = ddy(u_xlat0.xy);
    u_xlat2.xy = abs(u_xlat18.xy) + abs(u_xlat2.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat2.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat1.w = u_xlat0.x * u_xlat16_0.w;
    u_xlat0.x = u_xlat16_0.w * u_xlat0.x + -0.5;
    u_xlatb0 = u_xlat0.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat0 = u_xlat1 * input.vs_INTERP1;
    u_xlatb1 = u_xlat0.w==0.0;
    if(u_xlatb1){discard;}
    u_xlat1.xy = input.vs_INTERP2.xy / input.vs_INTERP2.ww;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture0,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_3.x = dot(_ShapeLightMaskFilter0, _ShapeLightMaskFilter0);
    u_xlatb17 = u_xlat16_3.x!=0.0;
    u_xlat16_3 = (-_ShapeLightInvertedFilter0) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_3.x = dot(u_xlat16_3, _ShapeLightMaskFilter0);
    u_xlat16_3.xyz = u_xlat16_2.xyz * u_xlat16_3.xxx;
    u_xlat16_3.xyz = (bool(u_xlatb17)) ? u_xlat16_3.xyz : u_xlat16_2.xyz;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture1,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_27 = dot(_ShapeLightMaskFilter1, _ShapeLightMaskFilter1);
    u_xlatb17 = u_xlat16_27!=0.0;
    u_xlat16_4 = (-_ShapeLightInvertedFilter1) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_27 = dot(u_xlat16_4, _ShapeLightMaskFilter1);
    u_xlat16_4.xyz = u_xlat16_2.xyz * ((float3)(u_xlat16_27));
    u_xlat16_4.xyz = (bool(u_xlatb17)) ? u_xlat16_4.xyz : u_xlat16_2.xyz;
    u_xlat16_5.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors1.xxx;
    u_xlat16_4.xyz = u_xlat16_4.xyz * _ShapeLightBlendFactors1.yyy;
    u_xlat16_2.xyz = tex2Dbias(_ShapeLightTexture2,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_27 = dot(_ShapeLightMaskFilter2, _ShapeLightMaskFilter2);
    u_xlatb17 = u_xlat16_27!=0.0;
    u_xlat16_6 = (-_ShapeLightInvertedFilter2) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_27 = dot(u_xlat16_6, _ShapeLightMaskFilter2);
    u_xlat16_6.xyz = u_xlat16_2.xyz * ((float3)(u_xlat16_27));
    u_xlat16_6.xyz = (bool(u_xlatb17)) ? u_xlat16_6.xyz : u_xlat16_2.xyz;
    u_xlat16_1.xyz = tex2Dbias(_ShapeLightTexture3,float4(u_xlat1.xy,0,_GlobalMipBias.x)).xyz;
    u_xlat16_27 = dot(_ShapeLightMaskFilter3, _ShapeLightMaskFilter3);
    u_xlatb25 = u_xlat16_27!=0.0;
    u_xlat16_2 = (-_ShapeLightInvertedFilter3) + float4(1.0,1.0,1.0,1.0);
    u_xlat16_27 = dot(u_xlat16_2, _ShapeLightMaskFilter3);
    u_xlat16_7.xyz = u_xlat16_1.xyz * ((float3)(u_xlat16_27));
    u_xlat16_7.xyz = (bool(u_xlatb25)) ? u_xlat16_7.xyz : u_xlat16_1.xyz;
    u_xlat16_5.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.xxx + u_xlat16_5.xyz;
    u_xlat16_5.xyz = u_xlat16_6.xyz * _ShapeLightBlendFactors2.xxx + u_xlat16_5.xyz;
    u_xlat16_5.xyz = u_xlat16_7.xyz * _ShapeLightBlendFactors3.xxx + u_xlat16_5.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * _ShapeLightBlendFactors0.yyy + u_xlat16_4.xyz;
    u_xlat16_3.xyz = u_xlat16_6.xyz * _ShapeLightBlendFactors2.yyy + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_7.xyz * _ShapeLightBlendFactors3.yyy + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat0.xyz * u_xlat16_5.xyz + u_xlat16_3.xyz;
    u_xlat16_3.xyz = u_xlat16_3.xyz * ((float3)(_HDREmulationScale));
    u_xlat16_1.xyz = max(u_xlat16_3.xyz, float3(0.0,0.0,0.0));
    u_xlat16_1.w = max(u_xlat0.w, 0.0);
    result = u_xlat16_1 * White;
    return result;

}

#endif
ENDHLSL
}
Pass {
Name "Sprite Normal"
Tags {"LightMode"="NormalsRendering" "QUEUE"="Transparent" "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "ShaderGraphShader"="true" "ShaderGraphTargetId"="UniversalSpriteLitSubTarget" "UniversalMaterialType"="Lit"}
Cull Back ZWrite On ZTest LEqual
Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
Stencil { Ref 128 ReadMask 127 WriteMask 128 CompFront Always PassFront Replace FailFront Keep ZFailFront Keep CompBack Always PassBack Replace FailBack Keep ZFailBack Keep }

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
float4 White;
float _Erases_Bottom_of_L1;
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
float4 vs_INTERP2:TEXCOORD2;
float3 vs_INTERP3:TEXCOORD3; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float u_xlat6;

    u_xlat0.xyz = input.vertex.xyz.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.vertex.xyz.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.vertex.xyz.zzz + u_xlat0.xyz;
    u_xlat0.xyz = u_xlat0.xyz + transpose(unity_ObjectToWorld)[3].xyz;
    u_xlat1 = u_xlat0.yyyy * transpose(UNITY_MATRIX_VP)[1];
    u_xlat1 = transpose(UNITY_MATRIX_VP)[0] * u_xlat0.xxxx + u_xlat1;
    u_xlat0 = transpose(UNITY_MATRIX_VP)[2] * u_xlat0.zzzz + u_xlat1;
    output.vertex = u_xlat0 + transpose(UNITY_MATRIX_VP)[3];
    u_xlat0.xyz = input.tangent.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.tangent.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.tangent.zzz + u_xlat0.xyz;
    u_xlat6 = dot(u_xlat0.xyz, u_xlat0.xyz);
    u_xlat6 = max(u_xlat6, 1.17549435e-38);
    u_xlat6 = rsqrt(u_xlat6);
    output.vs_INTERP0.xyz = ((float3)(u_xlat6)) * u_xlat0.xyz;
    output.vs_INTERP0.w = input.tangent.w;
    output.vs_INTERP1 = input.texcoord;
    output.vs_INTERP2 = input.color;
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
float2 u_xlat0;
bool u_xlatb0;
float2 u_xlat1;
float3 u_xlat16_2;
float u_xlat16_3;
float2 u_xlat6;

    u_xlatb0 = (_Erases_Bottom_of_L1 != 0);
    u_xlat0.y = (u_xlatb0) ? 1.0 : 2.0;
    u_xlat6.xy = input.vs_INTERP1.xy + float2(0.0,-0.5);
    u_xlat6.xy = u_xlat6.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat0.x = 1.0;
    u_xlat0.xy = (-u_xlat0.xy) + abs(u_xlat6.xy);
    u_xlat6.xy = ddx(u_xlat0.xy);
    u_xlat1.xy = ddy(u_xlat0.xy);
    u_xlat6.xy = abs(u_xlat6.xy) + abs(u_xlat1.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat6.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat16_3 = tex2Dbias(_MainTex,float4(input.vs_INTERP1.xy,0,_GlobalMipBias.x)).w;
    u_xlat6.x = u_xlat16_3 * u_xlat0.x + -0.5;
    u_xlat0.x = u_xlat0.x * u_xlat16_3;
    result.w = u_xlat0.x * White.w;
    u_xlatb0 = u_xlat6.x<0.0;
    if(u_xlatb0){discard;}
    u_xlat16_2.xyz = input.vs_INTERP3.xyz + float3(1.0,1.0,1.0);
    u_xlat16_2.xyz = u_xlat16_2.xyz * White.xyz;
    result.xyz = u_xlat16_2.xyz * float3(0.5,0.5,0.5);
    return result;

}

ENDHLSL
}
Pass {
Name "Sprite Forward"
Tags {"LightMode"="UniversalForward" "QUEUE"="Transparent" "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "ShaderGraphShader"="true" "ShaderGraphTargetId"="UniversalSpriteLitSubTarget" "UniversalMaterialType"="Lit"}
Cull Off ZWrite Off ZTest LEqual
Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
float4 _RendererColor;
float4 Color_4F6CF892;
float _Erases_Bottom_of_L1;
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
float3 vs_INTERP2:TEXCOORD2; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float4 u_xlat0;
float4 u_xlat1;
float3 u_xlat2;

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
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 u_xlat0;
bool u_xlatb0;
float2 u_xlat1;
float4 u_xlat16_1;
float2 u_xlat4;

    u_xlatb0 = (_Erases_Bottom_of_L1 != 0);
    u_xlat0.y = (u_xlatb0) ? 1.0 : 2.0;
    u_xlat4.xy = input.vs_INTERP0.xy + float2(0.0,-0.5);
    u_xlat4.xy = u_xlat4.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    u_xlat0.x = 1.0;
    u_xlat0.xy = (-u_xlat0.xy) + abs(u_xlat4.xy);
    u_xlat4.xy = ddx(u_xlat0.xy);
    u_xlat1.xy = ddy(u_xlat0.xy);
    u_xlat4.xy = abs(u_xlat4.xy) + abs(u_xlat1.xy);
    u_xlat0.xy = u_xlat0.xy / u_xlat4.xy;
    u_xlat0.xy = (-u_xlat0.xy) + float2(1.0,1.0);
    u_xlat0.xy = clamp(u_xlat0.xy, 0.0, 1.0);
    u_xlat0.x = min(u_xlat0.y, u_xlat0.x);
    u_xlat16_1 = tex2Dbias(_MainTex,float4(input.vs_INTERP0.xy,0,_GlobalMipBias.x));
    u_xlat0.w = u_xlat0.x * u_xlat16_1.w;
    u_xlat0.xyz = u_xlat16_1.xyz * Color_4F6CF892.xyz;
    u_xlat0 = u_xlat0 * input.vs_INTERP1;
    result = u_xlat0;
    return result;

}

ENDHLSL
}
}
}
