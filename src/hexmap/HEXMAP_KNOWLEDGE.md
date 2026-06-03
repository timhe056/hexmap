# CatlikeCoding Hex Map 教程知识点总结

> 来源：https://catlikecoding.com/unity/tutorials/hex-map/  
> Unity 六边形地图系列教程，20+ 部分，覆盖从基础网格到战争迷雾的完整系统。

---

## Part 1：六边形网格基础

### 为什么用六边形？
- 正方形有 8 个邻居（4 边 + 4 角），对角线距离 = √2，导致移动和距离计算复杂
- 六边形只有 6 个邻居，全部是边邻居，距离统一

### HexMetrics 几何常量与角落
```csharp
public static class HexMetrics {
    public const float outerRadius = 10f;
    public const float innerRadius = outerRadius * 0.866025404f;

    // 6 个角落，Pointy-top（尖顶朝上），从顶部开始顺时针
    static Vector3[] corners = {
        new Vector3(0f, 0f, outerRadius),
        new Vector3(innerRadius, 0f, 0.5f * outerRadius),
        new Vector3(innerRadius, 0f, -0.5f * outerRadius),
        new Vector3(0f, 0f, -outerRadius),
        new Vector3(-innerRadius, 0f, -0.5f * outerRadius),
        new Vector3(-innerRadius, 0f, 0.5f * outerRadius)
    };
}
```

### 矩形网格的奇偶偏移
相邻六边行错位排列，用整数除法强制矩形布局：
```csharp
// 核心公式：x 方向间距 = innerRadius * 2，z 方向间距 = outerRadius * 1.5
// 奇数行向右偏移半个间距
position.x = (x + z * 0.5f - z / 2) * (HexMetrics.innerRadius * 2f);
position.z = z * (HexMetrics.outerRadius * 1.5f);
```

### 程序化网格生成
不用预制体，直接用 Mesh API。每个六边形由 6 个三角形组成（中心 + 两个相邻顶点）：
```csharp
void AddTriangle(Vector3 v1, Vector3 v2, Vector3 v3) {
    int vertexIndex = vertices.Count;
    vertices.Add(v1);
    vertices.Add(v2);
    vertices.Add(v3);
    triangles.Add(vertexIndex);
    triangles.Add(vertexIndex + 1);
    triangles.Add(vertexIndex + 2);
}
```

### 立方体坐标 (Cube Coordinates)
```csharp
public struct HexCoordinates {
    public int X { get; private set; }
    public int Z { get; private set; }
    public int Y => -X - Z; // 约束：x + y + z = 0

    public static HexCoordinates FromOffsetCoordinates(int x, int z) {
        return new HexCoordinates(x - z / 2, z);
    }
}
```

---

## Part 2：邻居连接与颜色混合

### 六方向枚举
```csharp
public enum HexDirection { NE, E, SE, SW, W, NW }

public static class HexDirectionExtensions {
    public static HexDirection Opposite(this HexDirection direction) {
        return (int)direction < 3 ? (direction + 3) : (direction - 3);
    }
    public static HexDirection Previous(this HexDirection direction) {
        return direction == HexDirection.NE ? HexDirection.NW : (direction - 1);
    }
    public static HexDirection Next(this HexDirection direction) {
        return direction == HexDirection.NW ? HexDirection.NE : (direction + 1);
    }
}
```

### 双向邻居连接
```csharp
public class HexCell {
    [SerializeField] HexCell[] neighbors;

    public HexCell GetNeighbor(HexDirection direction) {
        return neighbors[(int)direction];
    }

    public void SetNeighbor(HexDirection direction, HexCell cell) {
        neighbors[(int)direction] = cell;
        cell.neighbors[(int)direction.Opposite()] = this;
    }
}
```

### 网格创建时的邻居初始化
```csharp
void CreateCell(int x, int z, int i) {
    // 西邻居（同排前一个）
    if (x > 0) {
        cell.SetNeighbor(HexDirection.W, cells[i - 1]);
    }
    // 南北方向邻居（上一排）
    if (z > 0) {
        if ((z & 1) == 0) { // 偶数排
            cell.SetNeighbor(HexDirection.SE, cells[i - width]);
            if (x > 0) {
                cell.SetNeighbor(HexDirection.SW, cells[i - width - 1]);
            }
        } else { // 奇数排
            cell.SetNeighbor(HexDirection.SW, cells[i - width]);
            if (x < width - 1) {
                cell.SetNeighbor(HexDirection.SE, cells[i - width + 1]);
            }
        }
    }
}
```

### Blend Regions（混合区域）
核心思想：每个六边形内部 75% 为纯色实心区，边缘 25% 为颜色混合区。
```csharp
public const float solidFactor = 0.75f;
public const float blendFactor = 1f - solidFactor;

public static Vector3 GetFirstSolidCorner(HexDirection direction) {
    return corners[(int)direction] * solidFactor;
}
public static Vector3 GetSecondSolidCorner(HexDirection direction) {
    return corners[(int)direction + 1] * solidFactor;
}
```

三角化时，中心到 solidCorner 形成纯色三角形，solidCorner 到 corner 形成梯形混合区：
```csharp
void Triangulate(HexDirection direction, HexCell cell) {
    Vector3 center = cell.transform.localPosition;
    Vector3 v1 = center + HexMetrics.GetFirstSolidCorner(direction);
    Vector3 v2 = center + HexMetrics.GetSecondSolidCorner(direction);

    // 内部纯色三角形
    AddTriangle(center, v1, v2);
    AddTriangleColor(cell.color);

    // 边缘混合梯形
    Vector3 v3 = center + HexMetrics.GetFirstCorner(direction);
    Vector3 v4 = center + HexMetrics.GetSecondCorner(direction);
    AddQuad(v1, v2, v3, v4);

    // 颜色：中心纯色，边缘是相邻3个单元格颜色的平均
    HexCell prevNeighbor = cell.GetNeighbor(direction.Previous()) ?? cell;
    HexCell neighbor = cell.GetNeighbor(direction) ?? cell;
    HexCell nextNeighbor = cell.GetNeighbor(direction.Next()) ?? cell;

    AddQuadColor(
        cell.color, cell.color,
        (cell.color + prevNeighbor.color + neighbor.color) / 3f,
        (cell.color + neighbor.color + nextNeighbor.color) / 3f
    );
}
```

---

## Part 3：海拔与台阶

### 离散海拔
```csharp
public class HexCell {
    int elevation = int.MinValue; // 初始值避免第一次设0时跳过刷新

    public int Elevation {
        get { return elevation; }
        set {
            if (elevation == value) return;
            elevation = value;
            Vector3 position = transform.localPosition;
            position.y = value * HexMetrics.elevationStep;
            transform.localPosition = position;

            // UI 标签同步移动
            Vector3 uiPosition = uiRect.localPosition;
            uiPosition.z = elevation * -HexMetrics.elevationStep;
            uiRect.localPosition = uiPosition;
        }
    }
}
```

### 边类型判断
```csharp
public enum HexEdgeType { Flat, Slope, Cliff }

public static HexEdgeType GetEdgeType(int elevation1, int elevation2) {
    if (elevation1 == elevation2) {
        return HexEdgeType.Flat;
    }
    int delta = elevation2 - elevation1;
    if (delta == 1 || delta == -1) {
        return HexEdgeType.Slope;
    }
    return HexEdgeType.Cliff;
}
```

### Terrace 台阶系统
```csharp
public const int terracesPerSlope = 2;
public const int terraceSteps = terracesPerSlope * 2 + 1; // = 5

public const float horizontalTerraceStepSize = 1f / terraceSteps;
public const float verticalTerraceStepSize = 1f / (terracesPerSlope + 1);

public static Vector3 TerraceLerp(Vector3 a, Vector3 b, int step) {
    float h = step * HexMetrics.horizontalTerraceStepSize;
    a.x += (b.x - a.x) * h;
    a.z += (b.z - a.z) * h;
    // 垂直方向只在奇数步变化：(step + 1) / 2 的整数除法
    // step: 1,2,3,4 → v: 1,1,2,2
    float v = ((step + 1) / 2) * HexMetrics.verticalTerraceStepSize;
    a.y += (b.y - a.y) * v;
    return a;
}

public static Color TerraceLerp(Color a, Color b, int step) {
    float h = step * HexMetrics.horizontalTerraceStepSize;
    return Color.Lerp(a, b, h);
}
```

斜坡三角化：只在 Slope 类型时插入台阶，Flat 直接平面，Cliff 直接悬崖。
```csharp
if (cell.GetEdgeType(direction) == HexEdgeType.Slope) {
    TriangulateEdgeTerraces(v1, v2, cell, v3, v4, neighbor);
} else {
    AddQuad(v1, v2, v3, v4);
    AddQuadColor(cell.color, neighbor.color);
}
```

---

## Part 4：不规则性（摘要）

### 顶点扰动
采样 tiling Perlin noise 纹理，对顶点做 XY 平面偏移：
```csharp
public static Vector3 Perturb(Vector3 position) {
    Vector4 sample = SampleNoise(position);
    position.x += (sample.x * 2f - 1f) * HexMetrics.cellPerturbStrength;
    position.z += (sample.z * 2f - 1f) * HexMetrics.cellPerturbStrength;
    return position;
}
```
关键：保持单元格内部平坦，只扰动边界顶点。

---

## Part 5：更大的地图

### Chunk 化
```csharp
public const int chunkSizeX = 5, chunkSizeZ = 5;

public class HexGridChunk : MonoBehaviour {
    HexCell[] cells;
    HexMesh hexMesh;
    Canvas gridCanvas;

    void Awake() {
        gridCanvas = GetComponentInChildren<Canvas>();
        hexMesh = GetComponentInChildren<HexMesh>();
        cells = new HexCell[HexMetrics.chunkSizeX * HexMetrics.chunkSizeZ];
    }

    public void AddCell(int index, HexCell cell) {
        cells[index] = cell;
        cell.chunk = this;
        cell.transform.SetParent(transform, false);
        cell.uiRect.SetParent(gridCanvas.transform, false);
    }
}
```

### 局部刷新 + LateUpdate 延迟
```csharp
public class HexCell {
    void Refresh() {
        if (chunk) {
            chunk.Refresh();
            // 边界处同时刷新邻居所在 Chunk
            for (int i = 0; i < neighbors.Length; i++) {
                HexCell neighbor = neighbors[i];
                if (neighbor != null && neighbor.chunk != chunk) {
                    neighbor.chunk.Refresh();
                }
            }
        }
    }
}

public class HexGridChunk : MonoBehaviour {
    public void Refresh() {
        enabled = true; // 标记需要更新
    }

    void LateUpdate() {
        hexMesh.Triangulate(cells);
        enabled = false;
    }
}
```

### 列表共享（静态缓冲区）
```csharp
public class HexMesh : MonoBehaviour {
    static List<Vector3> vertices = new List<Vector3>();
    static List<Color> colors = new List<Color>();
    static List<int> triangles = new List<int>();
    // 所有 Chunk 共用同一组静态列表
}
```

### 相机控制系统
```csharp
public class HexMapCamera : MonoBehaviour {
    Transform swivel, stick;
    float zoom = 1f;

    public float stickMinZoom, stickMaxZoom;
    public float swivelMinZoom, swivelMaxZoom;
    public float moveSpeedMinZoom, moveSpeedMaxZoom;

    void Update() {
        float zoomDelta = Input.GetAxis("Mouse ScrollWheel");
        if (zoomDelta != 0f) AdjustZoom(zoomDelta);

        float xDelta = Input.GetAxis("Horizontal");
        float zDelta = Input.GetAxis("Vertical");
        if (xDelta != 0f || zDelta != 0f) AdjustPosition(xDelta, zDelta);
    }

    void AdjustZoom(float delta) {
        zoom = Mathf.Clamp01(zoom + delta);
        float distance = Mathf.Lerp(stickMinZoom, stickMaxZoom, zoom);
        stick.localPosition = new Vector3(0f, 0f, distance);
        float angle = Mathf.Lerp(swivelMinZoom, swivelMaxZoom, zoom);
        swivel.localRotation = Quaternion.Euler(angle, 0f, 0f);
    }

    void AdjustPosition(float xDelta, float zDelta) {
        Vector3 direction = new Vector3(xDelta, 0f, zDelta).normalized;
        float damping = Mathf.Max(Mathf.Abs(xDelta), Mathf.Abs(zDelta));
        float distance = Mathf.Lerp(moveSpeedMinZoom, moveSpeedMaxZoom, zoom)
                       * damping * Time.deltaTime;
        transform.localPosition += direction * distance;
    }
}
```


---

## Part 6：河流系统

### 河流状态
```csharp
public class HexCell {
    bool hasIncomingRiver, hasOutgoingRiver;
    HexDirection incomingRiver, outgoingRiver;

    public bool HasRiver => hasIncomingRiver || hasOutgoingRiver;
    public bool HasRiverBeginOrEnd => hasIncomingRiver != hasOutgoingRiver;

    public bool HasRiverThroughEdge(HexDirection direction) {
        return (hasIncomingRiver && incomingRiver == direction) ||
               (hasOutgoingRiver && outgoingRiver == direction);
    }
}
```

### 移除河流
```csharp
public void RemoveOutgoingRiver() {
    if (!hasOutgoingRiver) return;
    hasOutgoingRiver = false;
    RefreshSelfOnly();

    HexCell neighbor = GetNeighbor(outgoingRiver);
    neighbor.hasIncomingRiver = false;
    neighbor.RefreshSelfOnly();
}

public void RemoveIncomingRiver() {
    if (!hasIncomingRiver) return;
    hasIncomingRiver = false;
    RefreshSelfOnly();

    HexCell neighbor = GetNeighbor(incomingRiver);
    neighbor.hasOutgoingRiver = false;
    neighbor.RefreshSelfOnly();
}

public void RemoveRiver() {
    RemoveOutgoingRiver();
    RemoveIncomingRiver();
}
```

### 设置河流（只能向下流）
```csharp
public void SetOutgoingRiver(HexDirection direction) {
    if (hasOutgoingRiver && outgoingRiver == direction) return;

    HexCell neighbor = GetNeighbor(direction);
    if (!neighbor || elevation < neighbor.elevation) return;

    RemoveOutgoingRiver();
    if (hasIncomingRiver && incomingRiver == direction) {
        RemoveIncomingRiver();
    }

    hasOutgoingRiver = true;
    outgoingRiver = direction;
    RefreshSelfOnly();

    neighbor.RemoveIncomingRiver();
    neighbor.hasIncomingRiver = true;
    neighbor.incomingRiver = direction.Opposite();
    neighbor.RefreshSelfOnly();
}
```

### 海拔变更时清理非法河流
```csharp
public int Elevation {
    set {
        // ...
        if (hasOutgoingRiver && elevation < GetNeighbor(outgoingRiver).elevation) {
            RemoveOutgoingRiver();
        }
        if (hasIncomingRiver && elevation > GetNeighbor(incomingRiver).elevation) {
            RemoveIncomingRiver();
        }
        Refresh();
    }
}
```

### 拖拽编辑河流
```csharp
public class HexMapEditor : MonoBehaviour {
    enum OptionalToggle { Ignore, Yes, No }
    OptionalToggle riverMode;

    bool isDrag;
    HexDirection dragDirection;
    HexCell previousCell;

    void HandleInput() {
        HexCell currentCell = hexGrid.GetCell(hit.point);
        if (previousCell && previousCell != currentCell) {
            ValidateDrag(currentCell);
        } else {
            isDrag = false;
        }
        EditCell(currentCell);
        previousCell = currentCell;
    }

    void ValidateDrag(HexCell currentCell) {
        for (dragDirection = HexDirection.NE; dragDirection <= HexDirection.NW; dragDirection++) {
            if (previousCell.GetNeighbor(dragDirection) == currentCell) {
                isDrag = true;
                return;
            }
        }
        isDrag = false;
    }

    void EditCell(HexCell cell) {
        if (riverMode == OptionalToggle.No) {
            cell.RemoveRiver();
        } else if (isDrag && riverMode == OptionalToggle.Yes) {
            previousCell.SetOutgoingRiver(dragDirection);
        }
    }
}
```

### 河床三角化
边缘从 3 个 quad 增加到 4 个 quad（加入中点顶点 v3）：
```csharp
public struct EdgeVertices {
    public Vector3 v1, v2, v3, v4, v5;

    public EdgeVertices(Vector3 corner1, Vector3 corner2) {
        v1 = corner1;
        v2 = Vector3.Lerp(corner1, corner2, 0.25f);
        v3 = Vector3.Lerp(corner1, corner2, 0.5f);
        v4 = Vector3.Lerp(corner1, corner2, 0.75f);
        v5 = corner2;
    }
}
```

河流经过的边缘，中间顶点降到河床高度：
```csharp
public const float streamBedElevationOffset = -1f;

public float StreamBedY => (elevation + HexMetrics.streamBedElevationOffset) * HexMetrics.elevationStep;

// 三角化时
if (cell.HasRiverThroughEdge(direction)) {
    e.v3.y = cell.StreamBedY;
}
```

---

## Part 7：道路系统（详细）

### 7.1 数据模型

每个 `HexCell` 维护一个 `bool[] roads = new bool[6]` 数组，对应 6 个方向。提供以下接口：

```csharp
public bool HasRoadThroughEdge(HexDirection d) => roads[(int)d];

public void RemoveRoads() {
    for (int i = 0; i < roads.Length; i++) {
        if (roads[i]) {
            roads[i] = false;
            neighbors[i].roads[(int)((HexDirection)i).Opposite()] = false;
            neighbors[i].RefreshSelfOnly();
            RefreshSelfOnly();
        }
    }
}

public void AddRoad(HexDirection d) {
    if (!roads[(int)d] && !HasRiverThroughEdge(d) && GetElevationDifference(d) <= 1) {
        roads[(int)d] = true;
        neighbors[(int)d].roads[(int)d.Opposite()] = true;
        neighbors[(int)d].RefreshSelfOnly();
        RefreshSelfOnly();
    }
}
```

**约束条件**：道路不能与河流同边；道路只能连接海拔差 ≤1 的格子。

### 7.2 道路三角化

道路走单元格边缘，用独立的 `roads` Mesh 渲染。材质使用 `Transparent-10` 渲染队列，Shader 顶点向相机微偏移（`viewDir * 0.01`）避免与地表 Z-fighting。

```csharp
void TriangulateRoadSegment(Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, Vector3 v5, Vector3 v6) {
    roads.AddQuad(v1, v2, v4, v5);
    roads.AddQuad(v2, v3, v5, v6);
    roads.AddQuadUV(0f, 1f, 0f);
    roads.AddQuadUV(1f, 0f, 0f);
}
```

```csharp
void TriangulateRoad(Vector3 center, Vector3 mL, Vector3 mR, EdgeVertices e, bool hasRoadThroughCellEdge) {
    if (hasRoadThroughCellEdge) {
        Vector3 mC = Vector3.Lerp(mL, mR, 0.5f);
        TriangulateRoadSegment(mL, mC, mR, e.v2, e.v3, e.v4);
        roads.AddTriangle(center, mL, mC);
        roads.AddTriangle(center, mC, mR);
        roads.AddTriangleUV(new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(1f, 1f));
        roads.AddTriangleUV(new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 0f));
    } else {
        TriangulateRoadEdge(center, mL, mR);
    }
}
```

### 7.3 与河流共存

河流优先级高于道路。若河流流经该边，道路中断。道路桥接规则：
- **Straight River**：道路在河流两侧沿边线走
- **Curved River**：道路在转弯外侧走弧线
- **起始/终止格**：河流起点/终点处，道路通过 `TriangulateRoadAdjacentToRiver` 特殊处理，沿河流边缘绕行

```csharp
void TriangulateRoadAdjacentToRiver(HexDirection d, HexCell cell, Vector3 center, EdgeVertices e) {
    bool hasRoadThroughEdge = cell.HasRoadThroughEdge(d);
    bool previousHasRiver = cell.HasRiverThroughEdge(d.Previous());
    bool nextHasRiver = cell.HasRiverThroughEdge(d.Next());
    Vector2 interpolators = GetRoadInterpolators(d, cell);
    // 根据相邻边是否有河流决定 UV 插值权重
}

Vector2 GetRoadInterpolators(HexDirection d, HexCell cell) {
    Vector2 interpolators;
    if (cell.HasRoadThroughEdge(d)) {
        interpolators.x = interpolators.y = 0.5f;
    } else {
        interpolators.x = cell.HasRoadThroughEdge(d.Previous()) ? 0.5f : 0.25f;
        interpolators.y = cell.HasRoadThroughEdge(d.Next()) ? 0.5f : 0.25f;
    }
    return interpolators;
}
```

**T 型路口**：当当前边无道路，但相邻两边有道路时，中心连接点使用 0.25 插值，形成平滑的 Y 型分叉。

### 7.4 Road Shader

```glsl
Shader "Custom/Road" {
    Properties { _MainTex ("Texture", 2D) = "white" {} }
    SubShader {
        Tags { "RenderType"="Opaque" "Queue"="Geometry-10" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex;
        struct Input { float2 uv_MainTex; };
        void surf(Input IN, inout SurfaceOutputStandard o) {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex);
            o.Albedo = c.rgb;
            o.Metallic = 0; o.Smoothness = 0;
            o.Alpha = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
```

材质启用 `Noise Texture`，道路颜色通过 UV 插值自然过渡，无硬边。

---

## Part 8：水域系统（详细）

### 8.1 数据模型

每个 `HexCell` 增加 `WaterLevel` 属性。当 `WaterLevel > Elevation` 时，该格为水下格。

```csharp
public int WaterLevel { get; set; }
public bool IsUnderwater => WaterLevel > Elevation;
public float WaterSurfaceY => (WaterLevel + HexMetrics.waterElevationOffset) * HexMetrics.elevationStep;
public float RiverSurfaceY => (Elevation + HexMetrics.waterElevationOffset) * HexMetrics.elevationStep;
```

其中 `waterElevationOffset = -0.5f`，让水面略低于整数海拔线，避免与陆地边缘冲突。

### 8.2 水面 Mesh

水面使用独立的 `water` Mesh 渲染，材质为 `Water Shader`（Transparent 队列）。

**Open Water（开放水域）**：当邻居也是水下格时，水面直接覆盖整个连接区域。

```csharp
void TriangulateWater(HexDirection d, HexCell cell, Vector3 center) {
    center.y = cell.WaterSurfaceY;
    HexCell neighbor = cell.GetNeighbor(d);
    if (neighbor != null && !neighbor.IsUnderwater) {
        TriangulateWaterShore(d, cell, neighbor, center);
    } else {
        TriangulateOpenWater(d, cell, neighbor, center);
    }
}

void TriangulateOpenWater(HexDirection d, HexCell cell, HexCell neighbor, Vector3 center) {
    Vector3 c1 = center + HexMetrics.GetFirstSolidCorner(d);
    Vector3 c2 = center + HexMetrics.GetSecondSolidCorner(d);
    water.AddTriangle(center, c1, c2);
    if (d <= HexDirection.SE && neighbor != null) {
        Vector3 bridge = HexMetrics.GetBridge(d);
        Vector3 e1 = c1 + bridge;
        Vector3 e2 = c2 + bridge;
        water.AddQuad(c1, c2, e1, e2);
        if (d == HexDirection.NE) {
            water.AddTriangle(center, c2, c2 + HexMetrics.GetBridge(d.Next()));
        }
    }
}
```

### 8.3 海岸线（Shore）

当水下格与陆地格相邻时，生成 Shore Mesh，包含水-陆交界处的泡沫效果。

```csharp
void TriangulateWaterShore(HexDirection d, HexCell cell, HexCell neighbor, Vector3 center) {
    EdgeVertices e1 = new EdgeVertices(
        center + HexMetrics.GetFirstSolidCorner(d),
        center + HexMetrics.GetSecondSolidCorner(d)
    );
    EdgeVertices e2 = new EdgeVertices(
        e1.v1 + HexMetrics.GetBridge(d),
        e1.v5 + HexMetrics.GetBridge(d)
    );
    TriangulateEdgeStrip(e1, waterColor, e2, waterColor);
    waterShore.AddQuad(e2.v1, e1.v1, e2.v5, e1.v5);
    waterShore.AddQuadUV(0f, 1f, 0f, 0f);
    HexCell nextNeighbor = cell.GetNeighbor(d.Next());
    if (nextNeighbor != null) {
        Vector3 center2 = nextNeighbor.Position;
        center2.y = center.y;
        Vector3 v3 = center2 + (nextNeighbor.IsUnderwater
            ? HexMetrics.GetFirstSolidCorner(d.Previous())
            : HexMetrics.GetSecondSolidCorner(d.Next()));
        waterShore.AddTriangle(e1.v5, e2.v5, v3);
        waterShore.AddTriangleUV(
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(0f, nextNeighbor.IsUnderwater ? 0f : 1f)
        );
    }
}
```

**Foam UV**：`waterShore` Mesh 使用特殊 UV 布局，U=0 为深水侧，U=1 为陆地侧，V 控制泡沫强度。Shader 通过 `foam` 贴图实现岸边浪花效果。

### 8.4 Water Shader

```glsl
Shader "Custom/Water" {
    Properties {
        _Color ("Color", Color) = (0,0,1,0.5)
        _MainTex ("Texture", 2D) = "white" {}
        _FoamTex ("Foam", 2D) = "white" {}
    }
    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Pass {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _FoamTex;
            float4 _Color;
            struct v2f {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            v2f vert(appdata_base v) {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target {
                fixed4 water = tex2D(_MainTex, i.uv) * _Color;
                fixed4 foam = tex2D(_FoamTex, i.uv);
                return lerp(water, foam, foam.a);
            }
            ENDCG
        }
    }
}
```

水面材质使用 `Transparent` 队列，关闭 ZWrite，与陆地正确混合。

### 8.5 水下地形

水下格子的地形渲染不降低海拔，而是通过材质变暗（或单独的水下地形 Shader）表现。`HexCellShaderData` 记录水下状态，Shader 根据 `IsUnderwater` 调整光照。

---

## Part 9：地形特征（详细）

### Feature Manager
```csharp
public class HexFeatureManager : MonoBehaviour {
    Transform container;
    public Transform featurePrefab;

    public void Clear() {
        if (container) Destroy(container.gameObject);
        container = new GameObject("Features Container").transform;
        container.SetParent(transform, false);
    }

    public void AddFeature(HexCell cell, Vector3 position) {
        HexHash hash = HexMetrics.SampleHashGrid(position);
        if (hash.a >= cell.UrbanLevel * 0.25f) return;

        Transform instance = Instantiate(featurePrefab);
        position.y += instance.localScale.y * 0.5f;
        instance.localPosition = HexMetrics.Perturb(position);
        instance.localRotation = Quaternion.Euler(0f, 360f * hash.b, 0f);
        instance.SetParent(container, false);
    }
}
```

### Hash Grid（确定性随机）
```csharp
public const int hashGridSize = 256;
public const float hashGridScale = 0.25f;
static HexHash[] hashGrid;

public static void InitializeHashGrid(int seed) {
    hashGrid = new HexHash[hashGridSize * hashGridSize];
    Random.State currentState = Random.state;
    Random.InitState(seed);
    for (int i = 0; i < hashGrid.Length; i++) {
        hashGrid[i] = HexHash.Create();
    }
    Random.state = currentState;
}

public static HexHash SampleHashGrid(Vector3 position) {
    int x = (int)(position.x * hashGridScale) % hashGridSize;
    if (x < 0) x += hashGridSize;
    int z = (int)(position.z * hashGridScale) % hashGridSize;
    if (z < 0) z += hashGridSize;
    return hashGrid[x + z * hashGridSize];
}
```

### HexHash 结构
```csharp
public struct HexHash {
    public float a, b;
    public static HexHash Create() {
        HexHash hash;
        hash.a = Random.value;
        hash.b = Random.value;
        return hash;
    }
}
```

### Urban Level 控制密度
- `UrbanLevel` 0~3
- `hash.a >= UrbanLevel * 0.25f` 时跳过放置
- 0 级 = 0% 概率，3 级 = 75% 概率

---

## Part 10：城墙系统（详细）

### 10.1 数据模型

`HexCell` 增加 `Walled` bool 属性。城墙沿单元格边缘生成，区分内侧（Walled）和外侧（非 Walled）。

```csharp
public bool Walled { get; set; }
```

### 10.2 城墙尺寸

```csharp
public const float wallHeight = 3f;       // 城墙高度（等于一个海拔台阶）
public const float wallThickness = 0.75f; // 城墙厚度
public const float wallElevationOffset = verticalTerraceStepSize; // 贴地偏移
```

### 10.3 城墙基础段（Edge Segment）

`HexFeatureManager.AddWall` 接收当前格（near）和邻居格（far）的 `EdgeVertices`，判断两者 Walled 状态不同才生成城墙。

```csharp
public void AddWall(EdgeVertices near, HexCell nearCell, EdgeVertices far, HexCell farCell,
    bool hasRiver, bool hasRoad) {
    if (nearCell.Walled != farCell.Walled &&
        !nearCell.IsUnderwater && !farCell.IsUnderwater &&
        nearCell.GetEdgeType(farCell) != HexEdgeType.Cliff) {
        AddWallSegment(near.v1, far.v1, near.v2, far.v2);
        if (hasRiver || hasRoad) {
            // 河流或道路穿墙处留空
            AddWallCap(near.v2, far.v2);
            AddWallCap(far.v4, near.v4); // 注意方向翻转
        } else {
            AddWallSegment(near.v2, far.v2, near.v3, far.v3);
            AddWallSegment(near.v3, far.v3, near.v4, far.v4);
        }
        AddWallSegment(near.v4, far.v4, near.v5, far.v5);
    }
}
```

### 10.4 单段三角化

```csharp
void AddWallSegment(Vector3 nearLeft, Vector3 farLeft, Vector3 nearRight, Vector3 farRight) {
    nearLeft = HexMetrics.Perturb(nearLeft);
    farLeft = HexMetrics.Perturb(farLeft);
    nearRight = HexMetrics.Perturb(nearRight);
    farRight = HexMetrics.Perturb(farRight);

    Vector3 left = HexMetrics.WallLerp(nearLeft, farLeft);
    Vector3 right = HexMetrics.WallLerp(nearRight, farRight);

    Vector3 leftThicknessOffset = HexMetrics.WallThicknessOffset(nearLeft, farLeft);
    Vector3 rightThicknessOffset = HexMetrics.WallThicknessOffset(nearRight, farRight);

    float leftTop = left.y + HexMetrics.wallHeight;
    float rightTop = right.y + HexMetrics.wallHeight;

    // 近侧面
    Vector3 v1, v2, v3, v4;
    v1 = v3 = left - leftThicknessOffset;
    v2 = v4 = right - rightThicknessOffset;
    v3.y = leftTop; v4.y = rightTop;
    walls.AddQuadUnperturbed(v1, v2, v3, v4);
    Vector3 t1 = v3, t2 = v4;

    // 远侧面（顶点顺序翻转）
    v1 = v3 = left + leftThicknessOffset;
    v2 = v4 = right + rightThicknessOffset;
    v3.y = leftTop; v4.y = rightTop;
    walls.AddQuadUnperturbed(v2, v1, v4, v3);

    // 顶部覆盖
    walls.AddQuadUnperturbed(t1, t2, v3, v4);
}
```

**WallLerp**：让城墙贴地，根据两侧海拔选择偏移量。

```csharp
public static Vector3 WallLerp(Vector3 near, Vector3 far) {
    near.x += (far.x - near.x) * 0.5f;
    near.z += (far.z - near.z) * 0.5f;
    float v = near.y < far.y ? wallElevationOffset : (1f - wallElevationOffset);
    near.y += (far.y - near.y) * v;
    return near;
}
```

**WallThicknessOffset**：计算水平偏移向量（Y=0），归一化后缩放半厚度。

```csharp
public static Vector3 WallThicknessOffset(Vector3 near, Vector3 far) {
    Vector3 offset;
    offset.x = far.x - near.x;
    offset.y = 0f;
    offset.z = far.z - near.z;
    return offset.normalized * (wallThickness * 0.5f);
}
```

**AddQuadUnperturbed**：墙体使用已扰动的顶点构建，但不再二次扰动，保持墙面平整、厚度均匀。

### 10.5 转角处理（Corners）

三个格子交汇的转角有 8 种配置，其中 6 种需要城墙。以 **Pivot（内侧格）** 为基准，从左侧边绕到右侧边。

```csharp
public void AddWall(Vector3 c1, HexCell cell1, Vector3 c2, HexCell cell2, Vector3 c3, HexCell cell3) {
    if (cell1.Walled) {
        if (cell2.Walled) { if (!cell3.Walled) AddWallSegment(c3, cell3, c1, cell1, c2, cell2); }
        else if (cell3.Walled) { AddWallSegment(c2, cell2, c3, cell3, c1, cell1); }
        else { AddWallSegment(c1, cell1, c2, cell2, c3, cell3); }
    }
    else if (cell2.Walled) {
        if (cell3.Walled) { AddWallSegment(c1, cell1, c2, cell2, c3, cell3); }
        else { AddWallSegment(c2, cell2, c3, cell3, c1, cell1); }
    }
    else if (cell3.Walled) {
        AddWallSegment(c3, cell3, c1, cell1, c2, cell2);
    }
}
```

**3 顶点 AddWallSegment**：Pivot 同时作为 nearLeft 和 nearRight。

```csharp
void AddWallSegment(Vector3 pivot, HexCell pivotCell, Vector3 left, HexCell leftCell, Vector3 right, HexCell rightCell) {
    if (pivotCell.IsUnderwater) return;
    bool hasLeftWall = !leftCell.IsUnderwater && pivotCell.GetEdgeType(leftCell) != HexEdgeType.Cliff;
    bool hasRightWall = !rightCell.IsUnderwater && pivotCell.GetEdgeType(rightCell) != HexEdgeType.Cliff;
    if (hasLeftWall && hasRightWall) {
        AddWallSegment(pivot, left, pivot, right);
    }
    else if (hasLeftWall) {
        if (leftCell.Elevation < rightCell.Elevation)
            AddWallWedge(pivot, left, right);
        else
            AddWallCap(pivot, left);
    }
    else if (hasRightWall) {
        if (rightCell.Elevation < leftCell.Elevation)
            AddWallWedge(right, pivot, left);
        else
            AddWallCap(right, pivot);
    }
}
```

### 10.6 缝隙闭合

**WallCap**：封堵城墙端头。

```csharp
void AddWallCap(Vector3 near, Vector3 far) {
    near = HexMetrics.Perturb(near);
    far = HexMetrics.Perturb(far);
    Vector3 center = HexMetrics.WallLerp(near, far);
    Vector3 thickness = HexMetrics.WallThicknessOffset(near, far);
    Vector3 v1, v2, v3, v4;
    v1 = v3 = center - thickness;
    v2 = v4 = center + thickness;
    v3.y = v4.y = center.y + HexMetrics.wallHeight;
    walls.AddQuadUnperturbed(v1, v2, v3, v4);
}
```

**WallWedge**：城墙楔入悬崖面，厚度逐渐收拢到零。

```csharp
void AddWallWedge(Vector3 near, Vector3 far, Vector3 point) {
    near = HexMetrics.Perturb(near);
    far = HexMetrics.Perturb(far);
    point = HexMetrics.Perturb(point);
    Vector3 center = HexMetrics.WallLerp(near, far);
    Vector3 thickness = HexMetrics.WallThicknessOffset(near, far);
    Vector3 pointTop = point;
    point.y = center.y;
    Vector3 v1, v2, v3, v4;
    v1 = v3 = center - thickness;
    v2 = v4 = center + thickness;
    v3.y = v4.y = pointTop.y = center.y + HexMetrics.wallHeight;
    walls.AddQuadUnperturbed(v1, point, v3, pointTop);
    walls.AddQuadUnperturbed(point, v2, pointTop, v4);
    walls.AddTriangleUnperturbed(pointTop, v3, v4);
}
```

### 10.7 约束总结

| 条件 | 处理方式 |
|------|----------|
| 两侧 Walled 相同 | 不生成城墙 |
| 任一侧水下 | 不生成城墙（边缘+转角） |
| 边缘为 Cliff | 不生成城墙 |
| 河流/道路穿边 | 中间两段留空，两端加 WallCap |
| 转角单侧为 Cliff | 生成 WallWedge 楔入悬崖 |
| 转角单侧无墙 | 生成 WallCap 封堵 |

---

## Part 20：战争迷雾

### Cell Data 纹理系统
```csharp
public class HexCellShaderData : MonoBehaviour {
    Texture2D cellTexture;
    Color32[] cellTextureData;

    public void Initialize(int x, int z) {
        if (cellTexture) {
            cellTexture.Resize(x, z);
        } else {
            cellTexture = new Texture2D(x, z, TextureFormat.RGBA32, false, true);
            cellTexture.filterMode = FilterMode.Point;
            cellTexture.wrapMode = TextureWrapMode.Clamp;
            Shader.SetGlobalTexture("_HexCellData", cellTexture);
        }
        Shader.SetGlobalVector("_HexCellData_TexelSize",
            new Vector4(1f / x, 1f / z, x, z));

        if (cellTextureData == null || cellTextureData.Length != x * z) {
            cellTextureData = new Color32[x * z];
        } else {
            for (int i = 0; i < cellTextureData.Length; i++) {
                cellTextureData[i] = new Color32(0, 0, 0, 0);
            }
        }
        enabled = true;
    }

    public void RefreshTerrain(HexCell cell) {
        cellTextureData[cell.Index].a = (byte)cell.TerrainTypeIndex;
        enabled = true;
    }

    void LateUpdate() {
        cellTexture.SetPixels32(cellTextureData);
        cellTexture.Apply();
        enabled = false;
    }
}
```

### Shader HLSL 采样
```hlsl
TEXTURE2D(_HexCellData);
SAMPLER(sampler_HexCellData);
float4 _HexCellData_TexelSize;

float4 GetCellData(float3 uv2, int index) {
    float2 uv;
    uv.x = (uv2[index] + 0.5) * _HexCellData_TexelSize.x;
    float row = floor(uv.x);
    uv.x -= row;
    uv.y = (row + 0.5) * _HexCellData_TexelSize.y;
    float4 data = SAMPLE_TEXTURE2D_LOD(_HexCellData, sampler_HexCellData, uv, 0);
    data.w *= 255; // Alpha 存地形索引，GPU 自动归一化到 0-1，乘 255 还原
    return data;
}
```

### Mesh 数据重构
- 顶点颜色 → `cellWeights`（RGB 分别对应 3 个相邻单元格的混合权重）
- UV2 → `cellIndices`（3 个单元格索引）
- 地形类型不再存入 Mesh，改为运行时 Shader 采样 Cell Data 纹理

### 可见性混合
```hlsl
float4 GetTerrainColor(Input IN, int index) {
    float3 uvw = float3(IN.worldPos.xz * 0.02, IN.terrain[index]);
    float4 c = UNITY_SAMPLE_TEX2DARRAY(_MainTex, uvw);
    return c * (IN.color[index] * IN.visibility[index]);
}
```

---

## 2.x / 3.x / 4.x 系列改进（详细）

### 2.0.0：URP 迁移与 Shader Graph
- URP 替代 Built-in RP，SRP Batcher 启用
- 所有表面着色器转为 **Shader Graph** + Custom Function 节点
- 材质渲染队列分层：
  - Road：`Transparent-10`
  - Water Shore：`Transparent-9`
  - Estuary：`Transparent-8`
  - Water：`Transparent-7`
  - River：`Transparent-6`（最后画，瀑布会重叠）
- HLSL：`cginc` → `hlsl`，使用 `TEXTURE2D` / `SAMPLE_TEXTURE2D_LOD` 宏
- 地形纹理数组用 Unity 内置导入替代自定义 atlas

### 3.0.0：消除 Cell Game Objects（重大架构重构）
```csharp
// 之前
public class HexCell : MonoBehaviour { }

// 之后
[System.Serializable]
public class HexCell {
    public Vector3 Position { get; set; }
    public HexGrid Grid { get; set; }
    public int Index { get; set; }

    // 隐式转换保持 null 检查习惯
    public static implicit operator bool(HexCell cell) => cell != null;
}
```

全面索引化替代直接引用：
| 之前 | 之后 |
|------|------|
| `HexCell[] neighbors` | 网格索引计算 |
| `HexCell PathFrom` | `int PathFromIndex` |
| `HexCell location` | `int locationCellIndex` |
| `HexGridChunk.cells[]` | `int[] cellIndices` |
| `List<HexCell> transitioningCells` | `List<int> transitioningCellIndices` |

### 4.0.0：Unity 6 与 UI Toolkit
- UI Toolkit 全面替换 uGUI
- 地图编辑器用 UI Document + UIDocument 组件

---

## 核心数据结构汇总

```csharp
public struct HexCoordinates {
    public int X, Z;
    public int Y => -X - Z;
    public static HexCoordinates FromOffsetCoordinates(int x, int z) =>
        new HexCoordinates(x - z / 2, z);
}

public enum HexDirection { NE, E, SE, SW, W, NW }

public enum HexEdgeType { Flat, Slope, Cliff }

[System.Serializable]
public class HexCell {
    public HexGrid Grid { get; set; }
    public int Index { get; set; }
    public Vector3 Position { get; set; }
    public HexCoordinates coordinates;

    public int TerrainTypeIndex { get; set; }
    public int Elevation { get; set; }
    public int WaterLevel { get; set; }
    public int UrbanLevel { get; set; }

    public bool HasIncomingRiver, HasOutgoingRiver;
    public HexDirection IncomingRiver, OutgoingRiver;
    public bool[] roads = new bool[6];

    public bool IsUnderwater => WaterLevel > Elevation;
    public bool HasRiver => HasIncomingRiver || HasOutgoingRiver;
    public float StreamBedY => (Elevation + HexMetrics.streamBedElevationOffset) * HexMetrics.elevationStep;

    public static implicit operator bool(HexCell cell) => cell != null;
}

public static class HexMetrics {
    public const float outerRadius = 10f;
    public const float innerRadius = outerRadius * 0.866025404f;
    public const float solidFactor = 0.75f;
    public const float elevationStep = 5f;
    public const int terracesPerSlope = 2;
    public const int terraceSteps = terracesPerSlope * 2 + 1;
    public const float streamBedElevationOffset = -1f;
    public const int chunkSizeX = 5, chunkSizeZ = 5;
    public const float cellPerturbStrength = 4f;
    public const float hashGridScale = 0.25f;
    public const int hashGridSize = 256;
}
```
