// Source GLES expressions, original pass states and 16 URP2D shape-light combinations.
Shader "Hidden/TopRimComposite" {
Properties {
	}

SubShader {
Tags {"RenderPipeline"="UniversalPipeline"}
Pass {
Name "RimMultiplicative"
Tags {"RenderPipeline"="UniversalPipeline"}
Cull Off ZWrite Off ZTest Always
Blend DstColor One, DstColor One

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
float _RimIntensity;
float4 _RimColor;
sampler2D _TopRimField;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
int int_bitfieldInsert(int baseValue,int insertValue,int offset,int bits) { uint mask=uint(~(int(~0)<<uint(bits))<<uint(offset));return int((uint(baseValue)&~mask)|((uint(insertValue)<<uint(offset))&mask)); }
float4 OriginalSampleRT(sampler2D sourceTexture,float2 sourceUV,float bias) {
#if UNITY_UV_STARTS_AT_TOP
sourceUV.y=1-sourceUV.y;
#endif
return tex2Dbias(sourceTexture,float4(sourceUV,0,bias));
}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 uv1:TEXCOORD1;float4 uv2:TEXCOORD2;float4 uv3:TEXCOORD3;float4 uv4:TEXCOORD4;float4 uv5:TEXCOORD5;float4 uv6:TEXCOORD6;float4 uv7:TEXCOORD7;float4 color:COLOR;uint vertexId:SV_VertexID; };
struct varying { float4 vertex:SV_POSITION;float2 vs_TEXCOORD0:TEXCOORD0; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float2 u_xlat0;
uint3 u_xlatu0;

    u_xlatu0.x =  uint(int(int_bitfieldInsert(0, input.vertexId, 1 & int(0x1F), 1)));
    u_xlatu0.z = uint(uint(input.vertexId) & 2u);
    u_xlat0.xy = ((float2)(u_xlatu0.xz));
    output.vertex.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    output.vs_TEXCOORD0.xy = u_xlat0.xy;
    output.vertex.zw = float2(0.0,1.0);
    output.vertex.z=(output.vertex.z+output.vertex.w)*0.5;
return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 sourceFragCoord=input.vertex;
#if UNITY_UV_STARTS_AT_TOP
sourceFragCoord.y=_ScreenParams.y-sourceFragCoord.y;
#endif
float u_xlat16_0;
float3 u_xlat1;

    u_xlat16_0 = OriginalSampleRT(_TopRimField,input.vs_TEXCOORD0.xy,_GlobalMipBias.x).x;
    u_xlat1.xyz = ((float3)(_RimIntensity)) * _RimColor.xyz;
    result.xyz = ((float3)(u_xlat16_0)) * u_xlat1.xyz;
    result.w = 0.0;
    return result;

}

ENDHLSL
}
Pass {
Name "RimDarkFloor"
Tags {"RenderPipeline"="UniversalPipeline"}
Cull Off ZWrite Off ZTest Always
Blend One One, One One

HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5

#include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
float _RimIntensity;
float _RimDarkFloor;
float4 _RimColor;
sampler2D _TopRimField;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
int int_bitfieldInsert(int baseValue,int insertValue,int offset,int bits) { uint mask=uint(~(int(~0)<<uint(bits))<<uint(offset));return int((uint(baseValue)&~mask)|((uint(insertValue)<<uint(offset))&mask)); }
float4 OriginalSampleRT(sampler2D sourceTexture,float2 sourceUV,float bias) {
#if UNITY_UV_STARTS_AT_TOP
sourceUV.y=1-sourceUV.y;
#endif
return tex2Dbias(sourceTexture,float4(sourceUV,0,bias));
}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 uv1:TEXCOORD1;float4 uv2:TEXCOORD2;float4 uv3:TEXCOORD3;float4 uv4:TEXCOORD4;float4 uv5:TEXCOORD5;float4 uv6:TEXCOORD6;float4 uv7:TEXCOORD7;float4 color:COLOR;uint vertexId:SV_VertexID; };
struct varying { float4 vertex:SV_POSITION;float2 vs_TEXCOORD0:TEXCOORD0; };
varying vert(appdata input) { varying output=(varying)0;SetUpSpriteInstanceProperties();
float2 u_xlat0;
uint3 u_xlatu0;

    u_xlatu0.x =  uint(int(int_bitfieldInsert(0, input.vertexId, 1 & int(0x1F), 1)));
    u_xlatu0.z = uint(uint(input.vertexId) & 2u);
    u_xlat0.xy = ((float2)(u_xlatu0.xz));
    output.vertex.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    output.vs_TEXCOORD0.xy = u_xlat0.xy;
    output.vertex.zw = float2(0.0,1.0);
    output.vertex.z=(output.vertex.z+output.vertex.w)*0.5;
return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 sourceFragCoord=input.vertex;
#if UNITY_UV_STARTS_AT_TOP
sourceFragCoord.y=_ScreenParams.y-sourceFragCoord.y;
#endif
float3 u_xlat0;
float u_xlat16_3;

    u_xlat0.xyz = ((float3)(_RimIntensity)) * _RimColor.xyz;
    u_xlat0.xyz = u_xlat0.xyz * ((float3)(float3(_RimDarkFloor,_RimDarkFloor,_RimDarkFloor)));
    u_xlat16_3 = OriginalSampleRT(_TopRimField,input.vs_TEXCOORD0.xy,_GlobalMipBias.x).x;
    result.xyz = ((float3)(u_xlat16_3)) * u_xlat0.xyz;
    result.w = 0.0;
    return result;

}

ENDHLSL
}
}
}
