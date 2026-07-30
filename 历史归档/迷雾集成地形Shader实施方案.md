# 迷雾集成地形 Shader 实施方案

## 目标

将战争迷雾从独立的透明 Mesh（`Fog` GameObject）改为集成到地形 Shader 中，通过顶点色控制探索/未探索状态的渲染切换。未探索地块显示与现有迷雾相同的纹理外观，且参与正常深度测试，从根本上解决斜视角下遮挡错误的问题。

---

## 前置决策

| 决策项 | 选择 | 说明 |
|--------|------|------|
| 顶点色通道 | `.r` = 已探索(0/1)，`.g` = 当前可见(0/1，预留) | 预留三态战争迷雾扩展 |
| 迷雾纹理 UV | 世界空间 XZ 投影 | 确保迷雾纹理在地图上连续无缝 |
| 基础地形 Shader | 1 个通用 Shader（非 3 个） | 减少维护成本 |
| 迷雾 Shader 属性 | 全局 Shader 属性 | 所有材质副本自动生效 |
| 未探索区域光照 | 保留 Emission（自发光） | 匹配现有迷雾视觉效果 |
| 探索/未探索边界 | 硬边（后续优化柔化） | 六边形地块顶点不共享，边界天然硬边 |
| 主体 Fog GameObject | 移除 | 由地形 Shader 替代 |
| FogCover / FogCover_two | 保留 | 负责地图外围封皮，逻辑独立 |
| Shader 改造方式 | 公共 `FogBlend.cginc`，所有地形 Shader include | 统一维护 |

---

## 实施步骤

### 步骤 1：HexCellData 增加字段

**文件**：`Assets/Scripts/Core/Models/HexCellData.cs`

新增：

```csharp
public int MeshVertexStartIndex;  // 该地块在合并地形 Mesh 中的首顶点索引
```

连同已有的 `SolidAreaColors`（已声明但从未赋值），在本次实施中正式起用。

---

### 步骤 2：MeshDataGenerator（或 MapRenderer）填充顶点色与起始索引

**文件**：`Assets/Scripts/Core/Services/MeshDataGenerator.cs` 或 `Assets/Scripts/Managers/MapRenderer.cs`

在 `MainMapMeshCreat` 中，按地块遍历收集顶点时：

1. 记录每个地块的 `MeshVertexStartIndex` = 当前合并顶点列表的 count
2. 为每个地块的 44 个实心区域顶点写入 `SolidAreaColors`：
   - 初始全部 `Color.white`（或根据 `IsExplored` 动态设置）
3. 同理为矩形过渡、三角过渡的顶点写入对应 color 列表
4. 将 colors 合并到一个 `List<Color>`，传递给 `MapController.CreatMesh`

```csharp
// 伪代码示意
int vertexStartIndex = allVertices.Count;
hexCellData.MeshVertexStartIndex = vertexStartIndex;

Color cellColor = hexCellData.IsExplored ? exploredColor : unexploredColor;
for (int i = 0; i < 44; i++)
    allColors.Add(cellColor);
```

---

### 步骤 3：MapController.CreatMesh 接受顶点色参数

**文件**：`Assets/Scripts/Utilities/MapController.cs`

在地形 Mesh 创建的重载方法中增加参数：

```csharp
public static Mesh CreatMesh(
    Vector3[] vertices,
    Vector2[] uv,
    Color[] colors,              // ← 新增
    int[][] subMeshIndices,
    GameObject gameObject,
    // ... 其余参数不变
)
```

在方法体中增加：

```csharp
mesh.colors = colors;
```

---

### 步骤 4：创建 FogBlend.cginc

**文件**：`Assets/Shader/FogBlend.cginc`（新建）

统一迷雾混合逻辑，所有地形 Shader 的顶点函数和表面函数调用它：

```hlsl
#ifndef FOG_BLEND_INCLUDED
#define FOG_BLEND_INCLUDED

// 全局 Shader 属性（由 MapRenderer 初始化时设置一次）
// _FogTex       - 迷雾纹理
// _FogColor     - 迷雾色调
// _FogTexScale  - 迷雾纹理 tiling
// _FogEmission  - 未探索区域自发光强度（0~1）

sampler2D _FogTex;
float4 _FogTex_ST;
fixed4 _FogColor;
float  _FogTexScale;
float  _FogEmission;

struct FogInput
{
    float2 uv_FogTex;    // 世界空间 XZ 投影 UV
    float  exploration;  // vertexColor.r
};

// 在 vert 中调用，计算世界空间迷雾 UV
void FogBlend_vert(in float4 vertex, out float2 uv_FogTex)
{
    // 注意：vertex 需为世界空间坐标，如果在 appdata 中，用 unity_ObjectToWorld 转换
    float3 worldPos = mul(unity_ObjectToWorld, vertex).xyz;
    uv_FogTex = worldPos.xz * _FogTexScale;
}

// 在 surf 中调用，混合迷雾与地形颜色
// 返回最终 albedo，同时设置 emission
fixed3 FogBlend_surf(float2 uv_FogTex, float exploration, fixed3 terrainAlbedo, inout fixed3 o_Emission)
{
    fixed4 fogTex = tex2D(_FogTex, uv_FogTex);
    fixed3 fogColor = fogTex.rgb * _FogColor.rgb;

    // exploration: 0 = 未探索(迷雾), 1 = 已探索(正常)
    fixed3 finalAlbedo = lerp(fogColor, terrainAlbedo, exploration);

    // 未探索时增加自发光，匹配现有迷雾效果（不受场景光影响）
    o_Emission += fogColor * (1.0 - exploration) * _FogEmission;

    return finalAlbedo;
}

// 简化版：同时混合 albedo 和其他 PBR 参数
fixed FogBlend_alpha(float exploration, float param)
{
    return param * exploration;
}

#endif
```

---

### 步骤 5：创建通用地形 Shader

**文件**：`Assets/Shader/TerrainBase_Fog.shader`（新建）

替代三个基础子网格原先使用的 Unity Standard Shader。

```hlsl
Shader "Custom/TerrainBase_Fog"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _EmissionMap ("Emission", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.0

        #include "FogBlend.cginc"

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _OcclusionMap;
        sampler2D _EmissionMap;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_BumpMap;
            float2 uv_OcclusionMap;
            float2 uv_EmissionMap;
            float2 uv_FogTex;       // 世界空间迷雾 UV
            float  vertexColor_R;   // 顶点色 .r = 探索状态
        };

        half _Glossiness;
        half _Metallic;
        fixed4 _Color;

        void vert(inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.vertexColor_R = v.color.r;
            FogBlend_vert(v.vertex, o.uv_FogTex);
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;

            // 正常 PBR
            fixed3 albedo = c.rgb;
            half smoothness = _Glossiness;
            half metallic = _Metallic;
            fixed4 occlusion = tex2D(_OcclusionMap, IN.uv_OcclusionMap);

            // 迷雾混合
            fixed3 emission = tex2D(_EmissionMap, IN.uv_EmissionMap).rgb;
            o.Albedo = FogBlend_surf(IN.uv_FogTex, IN.vertexColor_R, albedo, emission);
            o.Emission = emission;
            o.Smoothness = FogBlend_alpha(IN.vertexColor_R, smoothness);
            o.Metallic = FogBlend_alpha(IN.vertexColor_R, metallic);
            o.Occlusion = lerp(1.0, occlusion.r, IN.vertexColor_R);
            o.Alpha = c.a;

            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
        }
        ENDCG
    }
    FallBack "Diffuse"
}
```

---

### 步骤 6：修改 RealMaterialMaskBlend Shader

**文件**：`Assets/Shader/RealMaterialMaskBlend.shader`

在 `surf` 末尾增加迷雾混合调用：

```hlsl
// 在 CGPROGRAM 块中增加：
#include "FogBlend.cginc"

// Input 结构体增加：
float2 uv_FogTex;
float  vertexColor_R;

// 增加 vert 函数：
void vert(inout appdata_full v, out Input o) {
    UNITY_INITIALIZE_OUTPUT(Input, o);
    o.vertexColor_R = v.color.r;
    FogBlend_vert(v.vertex, o.uv_FogTex);
}

// surf 末尾，在设置 o.Albedo / o.Smoothness / o.Metallic 之后：
o.Albedo = FogBlend_surf(IN.uv_FogTex, IN.vertexColor_R, o.Albedo, o.Emission);
o.Smoothness = FogBlend_alpha(IN.vertexColor_R, o.Smoothness);
o.Metallic = FogBlend_alpha(IN.vertexColor_R, o.Metallic);
```

---

### 步骤 7：修改 ThreeMaterialBlend_Land Shader

**文件**：`Assets/Shader/ThreeMaterialBlend_Land.shader`

改动与步骤 6 完全一致：增加 `#include`、Input 字段、vert 函数、surf 末尾混合调用。

---

### 步骤 8：MapRenderer 设置全局 Shader 属性

**文件**：`Assets/Scripts/Managers/MapRenderer.cs`

在 `MapRender()` 初始化阶段（生成 Mesh 之前）设置一次全局 Shader 属性：

```csharp
// 从现有 Fog 材质读取贴图引用
Shader.SetGlobalTexture("_FogTex", fogTexture);
Shader.SetGlobalColor("_FogColor", fogColor);
Shader.SetGlobalFloat("_FogTexScale", 3f);
Shader.SetGlobalFloat("_FogEmission", 1.0f);
```

---

### 步骤 9：运行时探索更新逻辑

**文件**：探索相关代码（`HexCellData.ExploreThisHexCell` 或 `MapRenderer` 中新增方法）

当 `IsExplored` 翻为 `true` 时：

```csharp
public void UpdateExplorationVisuals(List<HexCellData> newlyExploredCells)
{
    Mesh terrainMesh = GetComponent<MeshFilter>().sharedMesh;
    Color[] colors = terrainMesh.colors;

    foreach (var cell in newlyExploredCells)
    {
        int startIndex = cell.MeshVertexStartIndex;
        for (int i = 0; i < 44; i++)  // 44 个实心区域顶点
        {
            colors[startIndex + i] = new Color(1f, 1f, 1f, 1f);  // 白色 = 已探索
        }
    }

    terrainMesh.colors = colors;  // 一次性回写，减少多次赋值开销
}
```

- 如果一次探索多个地块，**收集全部地块后统一更新**，只对 `mesh.colors` 赋值一次。
- 探索事件触发处（`MapVisualEventSO.OnMapVisualChanged` 或探索专用事件）调用此方法。

---

### 步骤 10：移除主体 Fog GameObject

**文件**：`Assets/Scenes/GameScene.unity`

- 删除 `m_Name: Fog` 的 GameObject（ID `856870215`），包括其 FogManager 和 MeshGenerator 组件
- **保留** `FogCover`（ID `244818668`）和 `FogCover_two`（ID `637012643`）及其 FogManager

> 注意：`FogCover` 当前 `m_Enabled: 0`（禁用），需确认是否需要在初始化时启用。

---

### 步骤 11：清理与验证

#### 11.1 FogManager 清理

**文件**：`Assets/Scripts/Managers/FogManager.cs`

- `OnFogInit` 和 `OnMapVisualChanged` 中移除对 `gameObject.name == "Fog"` 的分支
- `GenerateFog()` 方法可保留但不再被调用；或者直接删除 `myMaterial` 和 `GenerateFog()` 并标记 obsolete

#### 11.2 MeshDataGenerator.GetFogVertices 清理

**文件**：`Assets/Scripts/Core/Services/MeshDataGenerator.cs`

- `GetFogVertices()` 不再被主体迷雾调用，但 `GetFogCoverVertices()` 仍被 FogCover 使用
- 可选：将 `GetFogVertices()` 标记为 `[Obsolete]`，确认无引用后移除

#### 11.3 测试清单

| 测试项 | 预期结果 |
|--------|----------|
| 新游戏地图初始状态 | 全部未探索，显示连续迷雾纹理 |
| 开局探索（起始区域） | 已探索地块正常显示地形，边界硬边清晰 |
| 单位移动探索新地块 | 新地块即时切换为正常地形 |
| 斜视角（低角度） | 前方已探索地块正确遮挡后方未探索地块，无穿透 |
| 俯视角 | 表现与原来一致 |
| 不同地形高度（海/平/高） | 三种高度未探索状态均显示相同迷雾纹理 |
| 矩形过渡子网格 | 跨探索边界时渐变过渡正常 |
| 三角过渡子网格 | 跨探索边界时渐变过渡正常 |
| FogCover 封皮 | 地图外围迷雾屏障正常显示 |
| FogCover_two 封皮 | 第二层封皮正常显示 |
| Game 视图无粉色材质 | 所有 Shader 编译正确 |

---

## 涉及文件清单

| 文件 | 操作 |
|------|------|
| `Assets/Scripts/Core/Models/HexCellData.cs` | 修改：新增字段 |
| `Assets/Scripts/Core/Services/MeshDataGenerator.cs` | 修改：填充顶点色 + 记录起始索引 |
| `Assets/Scripts/Managers/MapRenderer.cs` | 修改：传 colors 参数 + 设置全局 Shader 属性 |
| `Assets/Scripts/Utilities/MapController.cs` | 修改：支持 colors 参数 + 基础材质改用新 Shader |
| `Assets/Shader/FogBlend.cginc` | **新建** |
| `Assets/Shader/TerrainBase_Fog.shader` | **新建** |
| `Assets/Shader/RealMaterialMaskBlend.shader` | 修改：增加迷雾混合 |
| `Assets/Shader/ThreeMaterialBlend_Land.shader` | 修改：增加迷雾混合 |
| `Assets/Scripts/Managers/FogManager.cs` | 修改：移除主体迷雾相关代码 |
| `Assets/Scripts/Core/Services/MeshDataGenerator.cs` | 修改：标记 GetFogVertices 废弃 |
| `Assets/Scripts/Core/Interfaces/IMeshGenerator.cs` | 可选修改：标记接口方法废弃 |
| `Assets/Scenes/GameScene.unity` | 修改：移除 Fog GameObject、更新材质引用 |

---

## 回滚策略

如果实施后出现不可预期的问题，回滚路径：

1. 还原 `GameScene.unity` 中删除的 Fog GameObject
2. 还原 `FogManager.cs` 中注释的代码
3. 还原 `MapController.cs` 中 colors 相关改动
4. 恢复 3 个基础子网格使用 Standard Shader
5. 删除新建的 `FogBlend.cginc` 和 `TerrainBase_Fog.shader`
6. 还原 `RealMaterialMaskBlend.shader` 和 `ThreeMaterialBlend_Land.shader` 的修改

建议**每完成一个步骤提交一次 git**，方便回滚到中间状态。
