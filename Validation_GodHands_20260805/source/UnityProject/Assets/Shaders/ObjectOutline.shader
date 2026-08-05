Shader "GodHand/ObjectOutline" {
    Properties {
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Width (World)", Float) = 0.05
    }
    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Transparent+1" }

        CGINCLUDE
        #include "UnityCG.cginc"
        float4 _OutlineColor;
        float _OutlineWidth;

        struct v2f { float4 pos : SV_POSITION; };

        v2f vert_planar(appdata_base v, float2 direction) {
            v2f o;
            float4 clipPos = UnityObjectToClipPos(v.vertex);
            // 将世界厚度转换为屏幕平面位移
            float2 clipOffset = direction * _OutlineWidth * float2(unity_CameraProjection._m00, unity_CameraProjection._m11);
            clipPos.xy += clipOffset * clipPos.w;
            o.pos = clipPos;
            return o;
        }
        fixed4 frag(v2f i) : SV_Target { return _OutlineColor; }
        ENDCG

        // 核心 4 方向对角线偏移：这是实现精确“矩形包围盒”扩张的唯一正确方式
        // 这样可以确保所有 90 度折角在大图下依然是尖锐的直角，不会变圆或变锯齿
        Pass { Stencil { Ref 1 Comp NotEqual } CGPROGRAM #pragma vertex vert #pragma fragment frag
            v2f vert(appdata_base v) { return vert_planar(v, float2(1, 1)); } ENDCG }
        Pass { Stencil { Ref 1 Comp NotEqual } CGPROGRAM #pragma vertex vert #pragma fragment frag
            v2f vert(appdata_base v) { return vert_planar(v, float2(-1, 1)); } ENDCG }
        Pass { Stencil { Ref 1 Comp NotEqual } CGPROGRAM #pragma vertex vert #pragma fragment frag
            v2f vert(appdata_base v) { return vert_planar(v, float2(1, -1)); } ENDCG }
        Pass { Stencil { Ref 1 Comp NotEqual } CGPROGRAM #pragma vertex vert #pragma fragment frag
            v2f vert(appdata_base v) { return vert_planar(v, float2(-1, -1)); } ENDCG }
    }
}
