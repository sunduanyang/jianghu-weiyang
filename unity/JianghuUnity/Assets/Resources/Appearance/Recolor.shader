Shader "Jianghu/AppearanceRecolor" {
    Properties { _MainTex("Source",2D)="white"{} _MaskTex("Mask",2D)="black"{} }
    SubShader {
        Cull Off ZWrite Off ZTest Always
        Pass {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _MaskTex;
            float4 _Crop, _Primary, _Secondary, _Skin, _Hair;
            fixed4 frag(v2f_img input) : SV_Target {
                float2 uv = _Crop.xy + input.uv * _Crop.zw;
                fixed4 source = tex2D(_MainTex,uv);
                fixed4 mask = tex2D(_MaskTex,uv);
                float light = dot(source.rgb,float3(.3,.59,.11));
                float3 color = source.rgb;
                color = lerp(color,_Primary.rgb*(.48+light*.8),mask.g);
                color = lerp(color,_Secondary.rgb*(.48+light*.8),mask.a);
                color = lerp(color,_Skin.rgb*clamp(light/.74,.3,1.16),mask.r);
                color = lerp(color,_Hair.rgb*(.72+light*2),mask.b);
                return fixed4(color,source.a);
            }
            ENDCG
        }
    }
}
