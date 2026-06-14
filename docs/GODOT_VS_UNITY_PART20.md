# Godot 4 vs Unity：Part 20 (Fog of War) 实现差异记录

> 本文档记录 Catlike Coding Hex Map Part 20（战争迷雾）从 Unity 迁移到 Godot 4 (Compatibility Renderer) 时遇到的核心架构差异。这些问题在后续 Part（21+）中仍会持续产生影响。
>
> **更新 2026-06-04**：问题一（全局 Shader Uniform）已通过 `global uniform` + `RenderingServer.GlobalShaderParameterSet` 解决。

---

## 问题一：Godot 全局 Shader Uniform（已解决 ✅）

### 背景

Unity 支持 `Shader.SetGlobalTexture(name, texture)`，一旦设置，**所有使用同名 uniform 的 Material 会自动获得该值**。

### Godot 4 的解决方案

Godot 4 从 4.0 开始就内置了 **Shader Globals**，通过 `global uniform` 关键字声明，所有 shader 共享同一值。

**Shader 中声明：**
```glsl
shader_type spatial;
global uniform sampler2D hex_cell_data : filter_nearest;
global uniform vec4 hex_cell_data_texel_size;
global uniform bool debug_always_visible = false;
```

**C# 运行时设置（一次设置，全项目生效）：**
```csharp
RenderingServer.GlobalShaderParameterSet("hex_cell_data", _cellShaderData.Texture);
RenderingServer.GlobalShaderParameterSet("hex_cell_data_texel_size", new Vector4(1f / x, 1f / z, x, z));
RenderingServer.GlobalShaderParameterSet("debug_always_visible", false);
```

**不再需要：**
- ❌ 逐个 material 调用 `SetShaderParameter`
- ❌ `ApplyCellDataToMaterial` 辅助方法
- ❌ 在 `HexGridChunk` 中为每个 mesh instance 绑定 material

### 当前代码状态

所有使用 `hex_cell_data` 的 shader（terrain, river, road, water, water_shore）均已改为 `global uniform`，`HexGrid.CreateMap` 中通过 `RenderingServer.GlobalShaderParameterSet` 统一设置。

---

## 问题二：Vertex Attribute 缺失 — 没有 vec4 UV2 / CUSTOM0

### 为什么 Unity 可以轻松实现

Unity 的 `Mesh` API 和 `appdata_full` 结构体提供了**非常丰富**的 vertex attribute 通道。Unity Part 20 的 `HexMesh` 在 `Apply()` 方法中这样提交数据：

```csharp
// Unity HexMesh.Apply()
if (useCellData) {
    hexMesh.SetColors(cellWeights);          // → v.color (vec4)
    hexMesh.SetUVs(2, cellIndices);          // → v.texcoord2 (vec4)
}
if (useUVCoordinates) {
    hexMesh.SetUVs(0, uvs);                  // → v.texcoord (UV0)
}
if (useUV2Coordinates) {
    hexMesh.SetUVs(1, uv2s);                 // → v.texcoord1 (UV1)
}
```

注意关键区别：
- `SetColors()` 对应 shader 中的 `v.color`，是一个完整的 **vec4**
- `SetUVs(2, ...)` 对应 `v.texcoord2`，也是一个完整的 **vec4**（可以存 4 个 float）
- `SetUVs(1, ...)` 对应 `v.texcoord1`（vec4），`SetUVs(0, ...)` 对应 `v.texcoord`（vec4）

Unity `appdata_full` 的结构（Surface Shader 自动生成）：

| 语义 | 名称 | 类型 | 用途 |
|------|------|------|------|
| `TEXCOORD0` | `v.texcoord` | `float4` | UV0（地形/河流纹理） |
| `TEXCOORD1` | `v.texcoord1` | `float4` | UV1（estuary river flow） |
| `TEXCOORD2` | `v.texcoord2` | `float4` | UV2（3 个 cell indices + 备用） |
| `TEXCOORD3` | `v.texcoord3` | `float4` | UV3（未使用，可扩展） |
| `COLOR` | `v.color` | `float4` | 顶点颜色（3 个混合 weights） |

### Unity 各 Mesh 的具体实现

**Terrain Mesh**（`useCellData = true`）：
- `cellIndices` 是 `List<Vector3>`，每个元素 = `(index0, index1, index2)`
- `cellWeights` 是 `List<Color>`，每个元素 = `(w0, w1, w2, 0)`
- `hexMesh.SetUVs(2, cellIndices)` → shader 中 `v.texcoord2.xyz` 存 3 个 index
- `hexMesh.SetColors(cellWeights)` → shader 中 `v.color.xyz` 存 3 个 weight

**River/Road Mesh**（`useCellData = true`）：
- `cellIndices` = `(index0, index1, 0)`（只用 xy）
- `cellWeights` = `(w0, w1, 0, 0)`（只用 rg）
- Shader：`GetCellData(v, 0)` 读 `v.texcoord2.x`，`GetCellData(v, 1)` 读 `v.texcoord2.y`
- visibility = `cell0.r * v.color.x + cell1.r * v.color.y`

**Water/WaterShore Mesh**（`useCellData = true`，`useUVCoordinates = true`）：
- `cellIndices` = `(index0, index1, index2)`（用 xyz）
- `cellWeights` = `(w0, w1, w2, 0)`（用 rgb）
- UV0（`v.texcoord`）存水面 shore/foam 的纹理坐标（Water shader 中 `uv_MainTex`）
- Shader：`GetCellData(v, 0/1/2)` 读 `v.texcoord2[0/1/2]`
- visibility = `cell0.r * v.color.x + cell1.r * v.color.y + cell2.r * v.color.z`

**Estuary Mesh**（`useCellData = true`，`useUVCoordinates = true`，`useUV2Coordinates = true`）：
- `cellIndices` = `(index0, index1, index2)`
- UV0（`v.texcoord`）存 blend(x) + shore(y)
- UV1（`v.texcoord1`）存 river flow（`o.riverUV = v.texcoord1.xy`）
- Shader 实际只用了 2 个 cell：`cell0.r * v.color.x + cell1.r * v.color.y`

**Feature Mesh**（特殊处理，不通过 vertex attribute 传 index）：
- Feature 的 shader 不使用 `GetCellData(v, index)`
- 而是通过 world position + `_GridCoordinates` texture 查表，得到 `cellDataCoordinates`（二维坐标）
- 再调用 `GetCellData(cellDataCoordinates)` 采样 `_HexCellData`

### Godot 4 Compatibility 的限制

Godot 4 spatial shader 的 vertex attribute 通道：

| Attribute | Godot 类型 | 对应 shader 变量 | 状态 |
|-----------|-----------|------------------|------|
| `Vertex` | `vec3` | `VERTEX` | ✅ |
| `Normal` | `vec3` | `NORMAL` | ✅ |
| `Tangent` | `vec4` | `TANGENT` | ✅ |
| `Color` | `vec4` | `COLOR` | ✅ |
| `TexUV` | `vec2` | `UV` | ✅ |
| `TexUV2` | `vec2` | `UV2` | ✅ |
| `Custom0` | `vec4` | `CUSTOM0` | ❌ **Compatibility 不支持** |
| `Custom1` | `vec4` | `CUSTOM1` | ❌ **Compatibility 不支持** |
| `Instance` | `mat4` | `INSTANCE_*` | ⚠️ MultiMesh 专用 |

**核心差距**：Unity 有 `texcoord`, `texcoord1`, `texcoord2`, `texcoord3` 四个 vec4 通道 + `color` vec4，共 **20 个 float**。Godot Compatibility 只有 `UV(vec2) + UV2(vec2) + COLOR(vec4)`，共 **8 个 float**。

而且各 attribute 的语义已被占用：

| Mesh | UV (vec2) 已被占用 | UV2 (vec2) 已被占用 | COLOR (vec4) 已被占用 |
|------|-------------------|--------------------|----------------------|
| Terrain | ❌ 空闲 | ❌ 空闲 | ✅ weights (3) |
| River | ✅ 河流纹理坐标 | ❌ 空闲 | ✅ weights (2) |
| Road | ✅ 道路 blend | ❌ 空闲 | ✅ weights (2) |
| Water | ❌ 空闲（shader 未使用） | ❌ 空闲 | ❌ 空闲 |
| WaterShore | ✅ shore (y) | ❌ 空闲 | ❌ 空闲 |
| Estuary | ✅ blend(x)+shore(y) | ✅ river flow | ❌ 空闲 |

### 为什么 Water/WaterShore 在 Godot 中被迫简化

Unity Water mesh 的 3-cell 混合需要：
- **3 个 cell indices**（0~300 的整数）
- **3 个混合 weights**（0~1 的浮点）

在 Unity 中：
- indices → `v.texcoord2.xyz`（vec4 的 xyz，3 个 float）
- weights → `v.color.xyz`（vec4 的 xyz，3 个 float）

在 Godot 中可用的 attribute：
- UV（vec2）：2 个 float
- UV2（vec2）：2 个 float
- COLOR（vec4）：4 个 float
- **合计 8 个 float，但需要 6 个 float（3 indices + 3 weights）**

看起来 8 > 6，似乎够用？问题是 **语义冲突**：

- WaterShore 的 `UV.y` 必须存 **shore 值**（0~1），这是 shader 核心逻辑 `float shore = UV.y`
- Estuary 的 `UV.xy` 必须存 **blend + shore**
- Estuary 的 `UV2.xy` 必须存 **river flow**（两个 0~1 的流动坐标）

如果我们强行把 cell indices 塞进 UV/UV2，就会覆盖这些原有语义。

**关键原因**：Unity 有独立的 `texcoord2`（存 indices）和 `texcoord`/`texcoord1`（存纹理坐标），彼此不冲突。Godot 没有 `texcoord2(vec4)`，只有 `UV2(vec2)`，而且 Godot 的 `UV2` 语义上已经被 estuary 的 river flow 占用了。

### 当前代码中的 workaround

| Mesh | Godot 实现 | 代价 |
|------|-----------|------|
| Terrain | `UV.xy` = index0, index1；`UV2.x` = index2；`COLOR.rgb` = weights | ✅ 基本完整（8 float 刚好够） |
| River/Road | `UV2.xy` = index0, index1；`COLOR.xy` = weights | ✅ 基本完整 |
| Water | `UV.x` = cell index；`UV.y` = 0 | ⚠️ **放弃 3-cell 混合**，退化为单 cell |
| WaterShore | `UV.x` = cell index；`UV.y` = shore | ⚠️ **放弃 3-cell 混合** |
| Estuary | 未实现 visibility | ❌ 完全未加 visibility（UV/UV2 已被 blend/shore/river 占满） |

### 潜在风险

1. **水面混合精度差**：Water/WaterShore 使用单 cell visibility，当水面横跨可见/不可见边界时，不会出现平滑过渡，可能出现硬边。
2. **Estuary 永远全亮**：河口 shader 未绑定 cell data，在迷雾中会显得突兀（一片亮蓝色河口在黑暗地形中）。
3. **后续 Part 无法直接跟教程**：如果 Part 21+ 需要更复杂的 cell data（如 explored 状态、灰度显示），attribute 空间会更加紧张。

### 解决方案：迁移到 Forward+ 渲染器 ✅

项目已将渲染器从 `gl_compatibility` 升级为 `forward_plus`，Unlock 了 `CUSTOM0`~`CUSTOM3`（vec4）通道。

**修改内容**：

1. **`project.godot`**：
   - `renderer/rendering_method="forward_plus"`
   - `renderer/rendering_method.mobile="mobile"`
   - `config/features` 移除 `"GL Compatibility"`，加入 `"Forward Plus"`

2. **`HexMeshBuilder.MeshData`**：新增 `UseCustom0` 和 `List<Color> Custom0s`
   - `ToMesh()` 中通过 `arrays[(int)Mesh.ArrayType.Custom0] = Custom0s.ToArray()` 提交

3. **Water/WaterShore/Estuary MeshData 创建**：
   - `water = new MeshData(useColors: true, useCustom0: true)`
   - `waterShore = new MeshData(useUV: true, useColors: true, useCustom0: true)`
   - `estuaries = new MeshData(useUV: true, useUV2: true, useColors: true, useCustom0: true)`

4. **三角化逻辑重构**：
   - `AddWaterTriangle`/`AddWaterQuad`：新增带 `Vector3 indices` + `Color weights` 的完整版，实现 3-cell 混合
   - `AddShoreTriangle`/`AddShoreQuad`：同理，UV.y 保留 shore，CUSTOM0 传 indices，COLOR 传 weights
   - `TriangulateOpenWater`：连接桥和角落三角形使用 2-cell / 3-cell 混合（对齐 Unity）
   - `TriangulateShoreWater`：shore 四边形和角落三角形使用 2-cell / 3-cell 混合
   - `TriangulateEstuary`：新增 `HexCell cell, HexCell neighbor` 参数，通过 `AddEstuaryCellData` 写入 2-cell 混合数据

5. **Shader 更新**：
   - `water.gdshader`：`CUSTOM0.xyz` 读 3 个 index，`COLOR.xyz` 读 3 个 weight，做加权混合
   - `water_shore.gdshader`：同上，UV.y 保留 shore
   - `estuary.gdshader`：同上，UV 保留 blend/shore，UV2 保留 river flow

**迁移后 Godot 各 Mesh 的 attribute 使用**：

| Mesh | UV (vec2) | UV2 (vec2) | COLOR (vec4) | CUSTOM0 (vec4) |
|------|-----------|------------|--------------|----------------|
| Terrain | index0, index1 | index2 | weights (3) | ❌ 未使用 |
| River/Road | 纹理坐标 | index0, index1 | weights (2) | ❌ 未使用 |
| Water | ❌ 未使用 | ❌ 未使用 | weights (3) | indices (3) |
| WaterShore | shore (y) | ❌ 未使用 | weights (3) | indices (3) |
| Estuary | blend(x)+shore(y) | river flow | weights (2) | indices (2) |

> ⚠️ **兼容性代价**：`forward_plus` 需要 Vulkan 支持，无法在纯 OpenGL 环境、旧设备、或 Web 导出中运行。如果后续需要支持这些平台，需要回退到 `gl_compatibility` 并接受 Water/WaterShore/Estuary 的单 cell visibility 简化。

---

## 问题三：没有 MonoBehaviour LateUpdate — Texture 上传需要手动管理

### 背景

Unity 的 `HexCellShaderData` 继承 `MonoBehaviour`，利用 `LateUpdate()` 实现**延迟上传**：

```csharp
// Unity HexCellShaderData
void LateUpdate() {
    cellTexture.SetPixels32(cellTextureData);
    cellTexture.Apply();
    enabled = false; // 本帧只上传一次
}

public void RefreshVisibility(HexCell cell) {
    cellTextureData[cell.Index].r = cell.IsVisible ? (byte)255 : (byte)0;
    enabled = true; // 标记本帧需要上传
}
```

这样 `RefreshVisibility` 只做**内存标记**，真正的 GPU 上传统一在帧末执行，且多个 cell 变化时只上传一次。

### Godot 4 的解决方案（已实施 ✅）

Godot 4 没有 `LateUpdate`，但每个 `Node` 都有 `ProcessPriority` 属性：
- **数值越小**：越早执行 `_Process`
- **数值越大**：越晚执行 `_Process`

把需要「帧末执行」的节点设为 **正数优先级**（如 100），即可等效 `LateUpdate`：

```csharp
// HexGrid._Ready
ProcessPriority = 100; // 在所有默认 0 优先级节点之后执行

public override void _Process(double delta)
{
    if (Engine.IsEditorHint()) return;
    /* Part 20: 帧末统一上传 cell data texture */
    _cellShaderData?.UpdateTexture();
    // ... 笔刷预览等其他逻辑
}
```

`HexCellShaderData` 本身仍是普通 C# 类（无需改成 Node），由 `HexGrid` 在 `_Process` 中统一驱动。

### 效果

1. **不再遗漏**：任何代码路径修改 visibility 后都不需要手动调用 `UpdateTexture()`，帧末自动统一上传
2. **性能更优**：同一帧内多个 cell 变化时，只会触发**一次** texture 上传（`UpdateTexture()` 内部有 `_needsUpdate` 守卫）
3. **代码更简洁**：已移除 `CreateMap`、`Load`、`IncreaseVisibility`、`DecreaseVisibility` 中四处散落的手动调用

### 仍然存在的限制

1. **每帧都进 `_Process`**：即使没有任何 cell 变化，每帧仍会检查一次 `_needsUpdate` bool，开销极小但非零
2. **Image 对象分配**：`UpdateTexture()` 内部每次上传都会 `Image.CreateFromData`，频繁更新时可以考虑对象池优化
3. **多线程问题**：`ImageTexture.Update` 仍必须在主线程执行，如果未来有 background thread 修改 visibility，需要回主线程触发

---

## 附录：当前 Part 20 代码状态速查

| 文件 | 关键修改 |
|------|---------|
| `src/hexmap/HexCellShaderData.cs` | byte[] textureData，手动 UpdateTexture |
| `src/hexmap/HexCell.cs` | Visibility setter → RefreshVisibility |
| `src/hexmap/HexGrid.cs` | BFS + UpdateTexture + `RenderingServer.GlobalShaderParameterSet` |
| `src/hexmap/HexUnit.cs` | TravelPath 中动态增减视野 |
| `src/hexmap/HexMeshBuilder.cs` | Terrain/river/road/water 的 cell data 输出 |
| `assets/shaders/terrain.gdshader` | UV/UV2 传 3 个 index，mix(0.25,1,visibility)，global uniform |
| `assets/shaders/river.gdshader` | UV2 传 2 个 index，COLOR 传 weights，global uniform |
| `assets/shaders/road.gdshader` | 同上 |
| `assets/shaders/water.gdshader` | UV.x 传单 cell index，global uniform |
| `assets/shaders/water_shore.gdshader` | UV.x 传单 cell index，UV.y = shore，global uniform |
| `assets/shaders/hex_cell_data.gdshaderinc` | GetCellData(float) + GetCellData(vec2)，global uniform |
 