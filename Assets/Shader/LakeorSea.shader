Shader "Custom/LakeorSea" {
    Properties {
        // 基础颜色：控制岸边水的整体色调
        _BaseColor ("Base Color", Color) = (0.1, 0.3, 0.5, 1.0)
        // 噪声纹理：与开阔水共用，确保动画风格一致（推荐教程中的 Perlin 噪声图）
        _MainTex ("Noise Texture", 2D) = "white" {}
        // 波浪强度：调节开阔水方向的波浪大小（0=无波浪，0.5=最大）
        _WaveStrength ("Wave Strength", Range(0.0, 0.5)) = 0.2
        // 泡沫强度：调节岸边泡沫的明显程度（0=无泡沫，1=最大）
        _FoamStrength ("Foam Strength", Range(0.0, 1.0)) = 0.8
        // 泡沫密度：控制岸边泡沫的条纹数量（值越大，泡沫越密集）
        _FoamDensity ("Foam Density", Range(5.0, 20.0)) = 10.0
    }

    SubShader {
        // 渲染设置：透明效果，确保水在地形之上、不遮挡其他透明物体
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        LOD 100
        ZWrite Off // 关闭深度写入，避免遮挡透明物体
        Blend SrcAlpha OneMinusSrcAlpha // 标准透明混合模式

        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // 支持 shader model 3.0，确保噪声纹理和复杂计算正常运行
            #pragma target 3.0

            #include "UnityCG.cginc"

            // 声明 Properties 中定义的参数
            uniform float4 _BaseColor;
            uniform sampler2D _MainTex;
            uniform float4 _MainTex_ST; // 噪声纹理的 UV 缩放/偏移
            uniform float _WaveStrength;
            uniform float _FoamStrength;
            uniform float _FoamDensity;

            // 全局迷雾属性（由 MapRenderer.SetupFogGlobalShaderProperties 设置，
            // 与地形迷雾共用同一张贴图与色调，保证水面未探索区与地形迷雾无缝衔接）
            uniform sampler2D _FogTex;
            uniform float4 _FogColor;
            uniform float _FogTexScale;
            uniform float _FogEmission;
            uniform float _FogTexAmount;
            uniform float4 _FogMapOrigin;  // 地图世界 XZ 包围盒起点 (x=minX, y=minZ)
            uniform float4 _FogMapSize;    // 地图世界 XZ 尺寸 (x=sizeX, y=sizeZ)
            uniform float _FogMemoryDim;   // 记忆区亮度系数（与地形一致）

            // ------------------------------
            // 通用波浪函数（复用开阔水逻辑，避免动画断层）
            // ------------------------------
            float Waves(float2 worldXZ) {
                float time = _Time.y; // 时间参数，控制动画速度
                
                // 第一层噪声：纵向滚动（y轴+时间）
                float2 uv1 = worldXZ * 0.025; // 0.025 控制波浪密度（值越小，波浪越稀疏）
                uv1.y += time;
                float4 noise1 = tex2D(_MainTex, uv1);
                
                // 第二层噪声：横向滚动（x轴+时间）
                float2 uv2 = worldXZ * 0.025;
                uv2.x += time;
                float4 noise2 = tex2D(_MainTex, uv2);
                
                // 混合波：动态插值噪声通道，避免波浪图案固定
                float blendWave = sin((worldXZ.x + worldXZ.y) * 0.1 + (noise1.y + noise2.z) + time);
                blendWave *= blendWave; // 将 -1~1 范围转为 0~1，增强对比度
                
                // 双噪声层叠加：通过混合波插值不同通道，实现动态图案
                float waveValue = lerp(noise1.z, noise1.w, blendWave) + lerp(noise2.x, noise2.y, blendWave);
                // 范围映射：仅保留 0.75~2 的波纹，其余平滑为 0（突出明显波纹，弱化微小波动）
                waveValue = smoothstep(0.75, 2.0, waveValue);
                
                return waveValue;
            }

            // ------------------------------
            // 顶点着色器输入/输出结构
            // ------------------------------
            struct appdata {
                float4 vertex : POSITION; // 顶点位置
                float2 uv : TEXCOORD0;    // UV 坐标（y轴=岸边距离因子：0=水侧，1=陆地侧）
                float4 color : COLOR;     // 顶点色 .r = 探索状态(0=未探索,1=已探索)
            };

            struct v2f {
                float2 uv : TEXCOORD0;       // 传递 UV 到片段着色器
                float4 worldPos : TEXCOORD1; // 传递世界坐标（用于计算波浪）
                float  explored : TEXCOORD2; // 探索状态（顶点色 .r）
                float  visible : TEXCOORD3;  // 当前视野（顶点色 .g），记忆区压暗用
                float4 pos : SV_POSITION;    // 裁剪空间坐标（用于渲染）
            };

            // ------------------------------
            // 顶点着色器：处理坐标转换和参数传递
            // ------------------------------
            v2f vert (appdata v) {
                v2f o;
                // 将顶点位置从模型空间转为裁剪空间
                o.pos = UnityObjectToClipPos(v.vertex);
                // 将 UV 应用缩放/偏移（支持在 Inspector 中调整噪声纹理 UV）
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // 将顶点位置从模型空间转为世界空间（用于波浪计算）
                o.worldPos = mul(unity_ObjectToWorld, v.vertex);
                // 探索状态
                o.explored = v.color.r;
                o.visible = v.color.g;
                return o;
            }

            // ------------------------------
            // 片段着色器：核心融合逻辑（泡沫+波浪）
            // ------------------------------
            fixed4 frag (v2f i) : SV_Target {
                // 1. 获取岸边距离因子（UV.y：0=靠近开阔水，1=靠近陆地）
                float shoreFactor = i.uv.y;
                // 平方根处理：让泡沫在陆地侧更集中（避免泡沫均匀分布）
                shoreFactor = sqrt(shoreFactor);

                // 2. 计算动态泡沫（岸边专属效果）
                // 2.1 噪声扰动：让泡沫边缘不规整，避免机械感
                float2 foamNoiseUV = i.worldPos.xz + _Time.y * 0.25; // 噪声随时间滚动
                float4 foamNoise = tex2D(_MainTex, foamNoiseUV * 0.015); // 0.015 控制扰动密度
                // 岸边扰动衰减：靠近陆地时扰动减弱（贴合海岸线形状）
                float distortion1 = foamNoise.x * (1.0 - shoreFactor);
                float distortion2 = foamNoise.z * (1.0 - shoreFactor);

                // 2.2 双向泡沫动画：模拟海浪"前进+后退"效果，避免单一方向
                float foam1 = sin((shoreFactor + distortion1) * _FoamDensity - _Time.y);
                foam1 *= foam1; // 增强泡沫对比度（暗部更暗，亮部更亮）
                float foam2 = sin((shoreFactor + distortion2) * _FoamDensity + _Time.y + 2.0); // 偏移2避免同步
                foam2 *= foam2 * 0.7; // 减弱第二层泡沫强度，避免冲突

                // 2.3 泡沫最终计算：取双向泡沫最大值，按岸边距离和强度参数调整
                float finalFoam = max(foam1, foam2) * shoreFactor * _FoamStrength;

                // 3. 计算波浪（复用开阔水逻辑）
                float waveValue = Waves(i.worldPos.xz);
                // 波浪岸边衰减：靠近陆地时波浪减弱（shoreFactor=1时波浪=0），避免与泡沫冲突
                float finalWave = waveValue * (1.0 - shoreFactor) * _WaveStrength;

                // 4. 融合泡沫与波浪：取最大值，消除过渡断层（泡沫和波浪不会同时占主导）
                float waterEffect = saturate(finalFoam + finalWave);
                // 叠加基础颜色，确保整体色调统一
                fixed4 finalColor = _BaseColor + waterEffect;
                // 保持 alpha 通道值（支持透明调节）
                finalColor.a = _BaseColor.a;

                // 5. 三态迷雾：
                //    未探索(explored=0) → 显示与地形一致的迷雾（不透明覆盖）。
                //    记忆区(explored=1, visible=0) → 水面正常渲染但压暗到 _FogMemoryDim。
                //    可见(explored=1, visible=1) → 正常水面。
                float2 fogUV = (i.worldPos.xz - _FogMapOrigin.xy) / _FogMapSize.xy;
                float3 fogTex = tex2Dlod(_FogTex, float4(fogUV, 0, 0)).rgb;
                float3 fogRGB = _FogColor.rgb * lerp(float3(1,1,1), fogTex, _FogTexAmount) * _FogEmission;
                fixed4 fogColor = fixed4(fogRGB, 1.0); // alpha=1：不透明遮住水面，融入迷雾层
                finalColor.rgb *= lerp(_FogMemoryDim, 1.0, i.visible); // 记忆区压暗
                return lerp(fogColor, finalColor, i.explored);
            }
            ENDCG
        }
    }
    // 降级方案：若设备不支持当前 Shader，使用默认透明 Shader
    FallBack "Transparent/VertexLit"
}