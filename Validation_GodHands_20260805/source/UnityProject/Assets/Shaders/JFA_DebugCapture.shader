Shader "GodHand/JFA_DebugCapture" {
    Properties {
        _MainTex ("Mask (White on Alpha)", 2D) = "white" {}
        _Color ("Overlay Color", Color) = (0,0,0,1)
    }
    SubShader {
        Tags { "Queue"="Transparent+101" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Color;

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target {
                float mask = tex2D(_MainTex, i.uv).a;
                // 将 Alpha 遮罩渲染为目标颜色 (黑色)
                return fixed4(_Color.rgb, mask * _Color.a);
            }
            ENDCG
        }
    }
}
