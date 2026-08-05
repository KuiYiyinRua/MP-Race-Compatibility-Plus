Shader "GodHand/JFAShaders" {
    Properties {
        _MainTex ("Texture", 2D) = "white" {}
        _OriginalMask ("Original Mask", 2D) = "white" {}
        _StepSize ("Step Size", Float) = 1
        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineWidth ("Outline Width (PX)", Float) = 2.0
        _Res ("Resolution", Float) = 256
    }
    SubShader {
        Tags { "Queue"="Transparent+100" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        sampler2D _OriginalMask;
        float _StepSize;
        float4 _OutlineColor;
        float _OutlineWidth;
        float _Res;

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

        fixed4 frag_output(v2f i) : SV_Target {
            float2 seed = tex2D(_MainTex, i.uv).rg;
            if (seed.x < -1.5) discard; 

            float dist_pixels = length(seed - i.uv) * _Res;
            float mask = tex2D(_OriginalMask, i.uv).a;
            
            float alpha = smoothstep(_OutlineWidth + 1.0, _OutlineWidth - 0.5, dist_pixels);
            
            if (mask > 0.5) discard; 
            return fixed4(_OutlineColor.rgb, alpha * _OutlineColor.a);
        }

        fixed4 frag_init(v2f i) : SV_Target {
            float mask = tex2D(_MainTex, i.uv).a;
            return (mask > 0.5) ? fixed4(i.uv.x, i.uv.y, 0, 1) : fixed4(-10, -10, 0, 1);
        }

        fixed4 frag_step(v2f i) : SV_Target {
            float step_uv = _StepSize / _Res;
            float2 bestSeed = tex2D(_MainTex, i.uv).rg;
            float minDist = (bestSeed.x < -1.5) ? 1000.0 : length(bestSeed - i.uv);

            for (int y = -1; y <= 1; y++) {
                for (int x = -1; x <= 1; x++) {
                    float2 sampleUV = i.uv + float2(x, y) * step_uv;
                    float2 s = tex2D(_MainTex, sampleUV).rg;
                    if (s.x > -1.5) {
                        float d = length(s - i.uv);
                        if (d < minDist) {
                            minDist = d;
                            bestSeed = s;
                        }
                    }
                }
            }
            return fixed4(bestSeed.x, bestSeed.y, 0, 1);
        }

        fixed4 frag_mask(v2f i) : SV_Target {
            // 输出纯白色遮罩，A=1 表示模型占用区域
            return fixed4(1, 1, 1, 1);
        }
        ENDCG

        Pass {
            Name "OUTPUT"
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_output
            ENDCG
        }
        Pass {
            Name "INIT"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_init
            ENDCG
        }
        Pass {
            Name "STEP"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_step
            ENDCG
        }
        Pass {
            Name "MASK"
            ZWrite On
            Blend Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_mask
            ENDCG
        }
    }
}
