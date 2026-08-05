Shader "GodHand/ShieldDissolve"
{
    Properties
    {
        _MainTex ("Main Texture (Shield Bubble)", 2D) = "white" {}
        _Color ("Main Color", Color) = (0.1, 0.7, 1.0, 1.0)
        
        _Dissolve ("Dissolve Progress", Range(0, 1)) = 0
        _BlockSize ("Base Grid Density", Float) = 25
        _EdgeGlow ("Edge Glow Intensity", Float) = 5.0
        _RadiusScale ("Radius Scale", Float) = 1.0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        Cull Off ZWrite Off
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            sampler2D _MainTex;
            float _Dissolve, _BlockSize, _EdgeGlow, _RadiusScale;
            float4 _Color;

            float hash12(float2 p) {
                float3 p3  = frac(float3(p.xyx) * .1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            v2f vert (appdata v) {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target {
                float progress = _Dissolve;
                float2 uv = i.uv;
                float2 centeredUV = uv - 0.5;
                float dist = length(centeredUV) * 2.0;

                // 修正：对于圆形护盾，超出半径的部分直接舍弃，防止边缘伪影
                if (dist > 1.0) discard;
                
                // 1. 获取基础护盾纹理
                fixed4 tex = tex2D(_MainTex, uv);
                
                // 2. 基于网格的溶解逻辑 - 使用 _RadiusScale 保持粒子物理尺寸固定
                float density = _BlockSize * _RadiusScale;
                float2 cell = floor(uv * density);
                float rnd = hash12(cell);
                
                // 动画逻辑：从四周向中心重建 (dist 从 1 到 0)
                // dist=1 是边缘，dist=0 是中心
                // 我们希望边缘先出现，中心后出现。
                // _Dissolve (progress) 从 1 到 0 (1是完全不可见，0是完全可见)
                // dFactor 越大越容易保留
                float dFactor = dist * 0.7 + rnd * 0.3;
                
                // 阈值检查：如果 progress 接近 1，大部分 dFactor < progress 都会被丢弃
                if (dFactor < progress) discard;
                
                fixed4 finalCol = tex * _Color * i.color;
                
                // 3. 构建边缘亮色效果
                float edge = saturate(1.0 - abs(dFactor - progress) * 15.0);
                finalCol.rgb += _Color.rgb * edge * _EdgeGlow;
                
                // 边缘透明度加深
                finalCol.a *= saturate((dFactor - progress) * 10.0);
                
                return finalCol;
            }
            ENDCG
        }
    }
}
