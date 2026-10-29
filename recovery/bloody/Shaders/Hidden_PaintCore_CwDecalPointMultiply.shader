Shader "Hidden/PaintCore/CwDecalPointMultiply" {SubShader {Cull Off ZWrite Off ZTest Always Pass {HLSLPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma target 3.5
#pragma multi_compile_local _ ORIGINAL_PAINT_LEGACY_UV
#include "UnityCG.cginc"
float4x4 _MaskMatrix;
float4x4 _DepthMatrix;
float4x4 _Matrix;
float4x4 _TileMatrix;
float4 _Coord;
float3 _Direction;
float _In3D;
float _TileTransition;
float2 _BufferSize;
float4 _MaskChannel;
float3 _MaskStretch;
float2 _MaskInvert;
float4 _LocalMaskChannel;
float4 _DepthData;
float3 _Position;
float4 _Channels;
float4 _ShapeChannel;
float4 _Color;
float _Opacity;
float _Hardness;
float _Wrapping;
float2 _NormalFront;
float2 _NormalBack;
float _TileOpacity;
sampler2D _Shape;
sampler2D _Texture;
sampler2D _MaskTexture;
sampler2D _LocalMaskTexture;
sampler2D _DepthTexture;
sampler2D _TileTexture;
sampler2D _Buffer;
bool2 greaterThanEqual(float2 a,float2 b){return a>=b;}
bool2 lessThan(float2 a,float2 b){return a<b;}
bool3 greaterThanEqual(float3 a,float3 b){return a>=b;}
bool3 lessThan(float3 a,float3 b){return a<b;}
bool4 greaterThanEqual(float4 a,float4 b){return a>=b;}
bool4 lessThan(float4 a,float4 b){return a<b;}
struct appdata { float4 vertex:POSITION;float3 normal:NORMAL;float4 tangent:TANGENT;float4 texcoord:TEXCOORD0;float4 uv1:TEXCOORD1;float4 uv2:TEXCOORD2;float4 uv3:TEXCOORD3;float4 color:COLOR; };
struct varying { float4 vertex:SV_POSITION;float2 vs_TEXCOORD0:TEXCOORD0;
float2 vs_TEXCOORD2:TEXCOORD1;
float3 vs_TEXCOORD1:TEXCOORD2;
float3 vs_TEXCOORD3:TEXCOORD3;
float3 vs_TEXCOORD4:TEXCOORD4;
float3 vs_TEXCOORD5:TEXCOORD5;
float4 vs_TEXCOORD6:TEXCOORD6; };
varying vert(appdata input) { varying output=(varying)0;
float4 u_xlat0;
float3 u_xlat1;
bool u_xlatb1;
float4 u_xlat2;
bool u_xlatb2;
float3 u_xlat3;
float3 u_xlat5;
bool u_xlatb5;
float u_xlat9;
bool u_xlatb9;
float u_xlat12;
float u_xlat13;

    output.vertex.zw = float2(0.5,1.0);
    u_xlat0.xy = input.uv1.xy * _Coord.yy;
    u_xlat0.xy = input.texcoord.xy * _Coord.xx + u_xlat0.xy;
    u_xlat0.xy = input.uv2.xy * _Coord.zz + u_xlat0.xy;
    u_xlat0.xy = input.uv3.xy * _Coord.ww + u_xlat0.xy;
    output.vertex.xy = u_xlat0.xy * float2(2.0,2.0) + float2(-1.0,-1.0);
    // UV rasterization must address the same RT texel as _Buffer sampling.
    // GLES clip Y maps upward; D3D render-target texture Y maps downward.
#if UNITY_UV_STARTS_AT_TOP && !defined(ORIGINAL_PAINT_LEGACY_UV)
    output.vertex.y = -output.vertex.y;
#endif
    u_xlat1.xyz = input.tangent.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat1.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.tangent.xxx + u_xlat1.xyz;
    u_xlat1.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.tangent.zzz + u_xlat1.xyz;
    u_xlat12 = dot(u_xlat1.xyz, u_xlat1.xyz);
    u_xlat12 = rsqrt(u_xlat12);
    u_xlat1.xyz = ((float3)(u_xlat12)) * u_xlat1.xyz;
    u_xlat5.xz = u_xlat1.yy * transpose(_Matrix)[1].yx;
    u_xlat1.xy = transpose(_Matrix)[0].yx * u_xlat1.xx + u_xlat5.xz;
    u_xlat1.xy = transpose(_Matrix)[2].yx * u_xlat1.zz + u_xlat1.xy;
    u_xlat12 = max(abs(u_xlat1.y), abs(u_xlat1.x));
    u_xlat12 = float(1.0) / u_xlat12;
    u_xlat9 = min(abs(u_xlat1.y), abs(u_xlat1.x));
    u_xlat12 = u_xlat12 * u_xlat9;
    u_xlat9 = u_xlat12 * u_xlat12;
    u_xlat13 = u_xlat9 * 0.0208350997 + -0.0851330012;
    u_xlat13 = u_xlat9 * u_xlat13 + 0.180141002;
    u_xlat13 = u_xlat9 * u_xlat13 + -0.330299497;
    u_xlat9 = u_xlat9 * u_xlat13 + 0.999866009;
    u_xlat13 = u_xlat12 * u_xlat9;
    u_xlat13 = u_xlat13 * -2.0 + 1.57079637;
    u_xlatb2 = abs(u_xlat1.y)<abs(u_xlat1.x);
    u_xlat13 = u_xlatb2 ? u_xlat13 : float(0.0);
    u_xlat12 = u_xlat12 * u_xlat9 + u_xlat13;
    u_xlatb9 = u_xlat1.y<(-u_xlat1.y);
    u_xlat9 = u_xlatb9 ? -3.14159274 : float(0.0);
    u_xlat12 = u_xlat12 + u_xlat9;
    u_xlat9 = min(u_xlat1.y, u_xlat1.x);
    u_xlat1.x = max(u_xlat1.y, u_xlat1.x);
    u_xlatb1 = u_xlat1.x>=(-u_xlat1.x);
    u_xlatb5 = u_xlat9<(-u_xlat9);
    u_xlatb1 = u_xlatb1 && u_xlatb5;
    u_xlat12 = (u_xlatb1) ? (-u_xlat12) : u_xlat12;
    output.vs_TEXCOORD2.y = (-u_xlat12);
    u_xlat1.xyz = input.normal.yyy * transpose(unity_ObjectToWorld)[1].xyz;
    u_xlat1.xyz = transpose(unity_ObjectToWorld)[0].xyz * input.normal.xxx + u_xlat1.xyz;
    u_xlat1.xyz = transpose(unity_ObjectToWorld)[2].xyz * input.normal.zzz + u_xlat1.xyz;
    u_xlat12 = dot(u_xlat1.xyz, u_xlat1.xyz);
    u_xlat12 = rsqrt(u_xlat12);
    u_xlat1.xyz = ((float3)(u_xlat12)) * u_xlat1.xyz;
    output.vs_TEXCOORD2.x = dot(u_xlat1.xyz, _Direction.xyz);
    u_xlat1.xyz = log2(abs(u_xlat1.xyz));
    u_xlat1.xyz = u_xlat1.xyz * ((float3)(float3(_TileTransition,_TileTransition,_TileTransition)));
    u_xlat1.xyz = exp2(u_xlat1.xyz);
    output.vs_TEXCOORD0.xy = u_xlat0.xy;
    u_xlat0.z = 0.0;
    u_xlat2 = input.vertex.yyyy * transpose(unity_ObjectToWorld)[1];
    u_xlat2 = transpose(unity_ObjectToWorld)[0] * input.vertex.xxxx + u_xlat2;
    u_xlat2 = transpose(unity_ObjectToWorld)[2] * input.vertex.zzzz + u_xlat2;
    u_xlat2 = transpose(unity_ObjectToWorld)[3] * input.vertex.wwww + u_xlat2;
    u_xlat3.xyz = (-u_xlat0.xyz) + u_xlat2.xyz;
    u_xlat0.xyz = ((float3)(float3(_In3D,_In3D,_In3D))) * u_xlat3.xyz + u_xlat0.xyz;
    u_xlat3.xyz = u_xlat0.yyy * transpose(_Matrix)[1].xyz;
    u_xlat0.xyw = transpose(_Matrix)[0].xyz * u_xlat0.xxx + u_xlat3.xyz;
    output.vs_TEXCOORD1.xyz = transpose(_Matrix)[2].xyz * u_xlat0.zzz + u_xlat0.xyw;
    u_xlat0.xyz = u_xlat2.yyy * transpose(_TileMatrix)[1].xyz;
    u_xlat0.xyz = transpose(_TileMatrix)[0].xyz * u_xlat2.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(_TileMatrix)[2].xyz * u_xlat2.zzz + u_xlat0.xyz;
    output.vs_TEXCOORD3.xyz = transpose(_TileMatrix)[3].xyz * u_xlat2.www + u_xlat0.xyz;
    u_xlat0.x = u_xlat1.y + u_xlat1.x;
    u_xlat0.x = u_xlat1.z + u_xlat0.x;
    output.vs_TEXCOORD4.xyz = u_xlat1.xyz / u_xlat0.xxx;
    u_xlat0.xyz = u_xlat2.yyy * transpose(_MaskMatrix)[1].xyz;
    u_xlat0.xyz = transpose(_MaskMatrix)[0].xyz * u_xlat2.xxx + u_xlat0.xyz;
    u_xlat0.xyz = transpose(_MaskMatrix)[2].xyz * u_xlat2.zzz + u_xlat0.xyz;
    output.vs_TEXCOORD5.xyz = transpose(_MaskMatrix)[3].xyz * u_xlat2.www + u_xlat0.xyz;
    u_xlat0 = u_xlat2.yyyy * transpose(_DepthMatrix)[1];
    u_xlat0 = transpose(_DepthMatrix)[0] * u_xlat2.xxxx + u_xlat0;
    u_xlat0 = transpose(_DepthMatrix)[2] * u_xlat2.zzzz + u_xlat0;
    output.vs_TEXCOORD6 = transpose(_DepthMatrix)[3] * u_xlat2.wwww + u_xlat0;
    return output;

}
float4 frag(varying input):SV_Target { float4 result=0;
float4 sourceFragCoord=input.vertex;
#if UNITY_UV_STARTS_AT_TOP
sourceFragCoord.y=_ScreenParams.y-sourceFragCoord.y;
#endif
float4 u_xlat0;
float4 u_xlat1;
float4 u_xlat16_1;
float4 u_xlat2;
float4 u_xlat16_2;
float4 u_xlat3;
float4 u_xlat16_3;
float2 u_xlat4;
float4 u_xlat16_4;
float3 u_xlat5;
bool u_xlatb5;
float2 u_xlat6;
float2 u_xlat7;
float2 u_xlat10;
bool u_xlatb10;
float u_xlat11;
int u_xlati13;
float u_xlat15;
bool u_xlatb15;
float u_xlat17;
float u_xlat18;
bool u_xlatb18;
float4 TempArray0[8];

    u_xlat0.xyz = input.vs_TEXCOORD1.xyz + (-_Position.xyz);
    u_xlat15 = max(abs(u_xlat0.y), abs(u_xlat0.x));
    u_xlat15 = max(abs(u_xlat0.z), u_xlat15);
    u_xlatb15 = 1.0<u_xlat15;
    if(u_xlatb15){discard;}
    u_xlat15 = u_xlat0.z * _Wrapping;
    u_xlat15 = (-u_xlat15) * u_xlat15 + 1.0;
    u_xlat0.xy = u_xlat0.xy / ((float2)(u_xlat15));
    u_xlat1.xyz = min(abs(u_xlat0.xyz), float3(1.0,1.0,1.0));
    u_xlat1.xyz = log2(u_xlat1.xyz);
    u_xlat10.xy = u_xlat1.xy * float2(1000.0,1000.0);
    u_xlat10.xy = exp2(u_xlat10.xy);
    u_xlat1.x = u_xlat1.z * _Hardness;
    u_xlat1.x = exp2(u_xlat1.x);
    u_xlat6.xy = (-input.vs_TEXCOORD2.xx) + float2(_NormalFront.x,_NormalBack.x);
    u_xlat6.xy = u_xlat6.xy * float2(_NormalFront.y,_NormalBack.y);
    u_xlat6.x = max(u_xlat6.y, u_xlat6.x);
    u_xlat6.x = clamp(u_xlat6.x, 0.0, 1.0);
    u_xlat0.xy = u_xlat0.xy * float2(0.5,0.5) + float2(0.5,0.5);
    u_xlat16_2 = tex2Dbias(_Shape,float4(u_xlat0.xy,0,0));
    u_xlat11 = dot(u_xlat16_2, _ShapeChannel);
    u_xlat15 = max(u_xlat10.y, u_xlat1.x);
    u_xlat10.x = max(u_xlat15, u_xlat10.x);
    u_xlat10.x = (-u_xlat10.x) + 1.0;
    u_xlat15 = u_xlat6.x * -2.0 + 3.0;
    u_xlat1.x = u_xlat6.x * u_xlat6.x;
    u_xlat15 = u_xlat15 * u_xlat1.x;
    u_xlat10.x = u_xlat15 * u_xlat10.x;
    u_xlat10.x = u_xlat10.x * _Opacity;
    u_xlat10.x = u_xlat11 * u_xlat10.x;
    u_xlat16_1 = tex2Dbias(_Texture,float4(u_xlat0.xy,0,0));
    u_xlat1 = u_xlat16_1 * _Color;
    u_xlat0.xy = input.vs_TEXCOORD5.xy + float2(0.5,0.5);
    u_xlat2.xyz = input.vs_TEXCOORD5.xyz * _MaskStretch.xyz;
    u_xlat15 = max(abs(u_xlat2.z), abs(u_xlat2.y));
    u_xlat15 = max(u_xlat15, abs(u_xlat2.x));
    u_xlat15 = u_xlat15 + -1.0;
    u_xlat15 = u_xlat15 * 1000.0;
    u_xlat15 = clamp(u_xlat15, 0.0, 1.0);
    u_xlat16_2 = tex2Dbias(_MaskTexture,float4(u_xlat0.xy,0,0));
    u_xlat0.x = dot(u_xlat16_2, _MaskChannel);
    u_xlat0.x = u_xlat0.x * _MaskInvert.y + _MaskInvert.x;
    u_xlat0.x = u_xlat15 + u_xlat0.x;
    u_xlat0.x = clamp(u_xlat0.x, 0.0, 1.0);
    u_xlat0.x = u_xlat0.x * u_xlat10.x;
    u_xlat16_2 = tex2Dbias(_LocalMaskTexture,float4(input.vs_TEXCOORD0.xy,0,-15.0));
    u_xlat5.x = dot(u_xlat16_2, _LocalMaskChannel);
    u_xlat0.x = u_xlat5.x * u_xlat0.x;
    u_xlat5.xyz = input.vs_TEXCOORD6.xyz / input.vs_TEXCOORD6.www;
    u_xlat5.z = (-u_xlat5.z) + _DepthData.w;
    u_xlat2.x = tex2Dbias(_DepthTexture,float4(u_xlat5.xy,0,0)).x;
    u_xlat7.xy = ddx(u_xlat5.xy);
    u_xlat3.xy = ddy(u_xlat5.xy);
    TempArray0[0].xy = float2(-1.0,0.0);
    TempArray0[1].xy = float2(1.0,0.0);
    TempArray0[2].xy = float2(0.0,-1.0);
    TempArray0[3].xy = float2(0.0,1.0);
    TempArray0[4].xy = float2(1.0,1.0);
    TempArray0[5].xy = float2(-1.0,-1.0);
    TempArray0[6].xy = float2(-1.0,1.0);
    TempArray0[7].xy = float2(1.0,-1.0);
    u_xlat17 = u_xlat2.x;
    u_xlati13 = 0;
    while(true){
        u_xlat18 = float(u_xlati13);
        u_xlatb18 = u_xlat18>=_DepthData.z;
        if(u_xlatb18){break;}
        u_xlat18 = TempArray0[u_xlati13].x;
        u_xlat4.xy = ((float2)(u_xlat18)) * u_xlat7.xy + u_xlat5.xy;
        u_xlat18 = TempArray0[u_xlati13].y;
        u_xlat4.xy = ((float2)(u_xlat18)) * u_xlat3.xy + u_xlat4.xy;
        u_xlat18 = tex2Dbias(_DepthTexture,float4(u_xlat4.xy,0,0)).x;
        u_xlat17 = min(u_xlat17, u_xlat18);
        u_xlati13 = u_xlati13 + 1;
    }
    u_xlat5.xyz = u_xlat5.xyz + float3(-0.5,-0.5,1.0);
    u_xlat5.x = max(abs(u_xlat5.y), abs(u_xlat5.x));
    u_xlatb5 = u_xlat5.x<0.5;
    u_xlat5.x = u_xlatb5 ? 1.0 : float(0.0);
    u_xlat5.x = u_xlat5.x * u_xlat5.z;
    u_xlatb10 = -1.0<_DepthData.w;
    u_xlatb5 = u_xlat17<u_xlat5.x;
    u_xlat5.x = u_xlatb5 ? 1.0 : float(0.0);
    u_xlat5.x = (u_xlatb10) ? u_xlat5.x : 1.0;
    u_xlat0.x = u_xlat5.x * u_xlat0.x;
    u_xlat16_2 = tex2Dbias(_TileTexture,float4(input.vs_TEXCOORD3.yz,0,0));
    u_xlat16_3 = tex2Dbias(_TileTexture,float4(input.vs_TEXCOORD3.xz,0,0));
    u_xlat3 = u_xlat16_3 * input.vs_TEXCOORD4.yyyy;
    u_xlat16_4 = tex2Dbias(_TileTexture,float4(input.vs_TEXCOORD3.xy,0,0));
    u_xlat2 = u_xlat16_2 * input.vs_TEXCOORD4.xxxx + u_xlat3;
    u_xlat2 = u_xlat16_4 * input.vs_TEXCOORD4.zzzz + u_xlat2;
    u_xlat2 = u_xlat2 + float4(-1.0,-1.0,-1.0,-1.0);
    u_xlat2 = ((float4)(_TileOpacity)) * u_xlat2 + float4(1.0,1.0,1.0,1.0);
    u_xlat3 = u_xlat1 * u_xlat2;
    u_xlat5.xy = input.vs_TEXCOORD0.xy * _BufferSize.xy;
    u_xlat5.xy = floor(u_xlat5.xy);
    u_xlat5.xy = u_xlat5.xy + float2(0.5,0.5);
    u_xlat5.xy = u_xlat5.xy / _BufferSize.xy;
    u_xlat16_4 = tex2Dbias(_Buffer,float4(u_xlat5.xy,0,-15.0));
    u_xlat0.x = (-u_xlat3.w) * u_xlat0.x + 1.0;
    u_xlat5.xyz = (-u_xlat1.xyz) * u_xlat2.xyz + float3(1.0,1.0,1.0);
    u_xlat0.xyz = u_xlat0.xxx * u_xlat5.xyz + u_xlat3.xyz;
    u_xlat0.xyz = u_xlat16_4.xyz * u_xlat0.xyz + (-u_xlat16_4.xyz);
    u_xlat0.w = 0.0;
    result = u_xlat0 * _Channels + u_xlat16_4;
    return result;

}

ENDHLSL
}}}
