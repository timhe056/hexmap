using Godot;

namespace HexMap;

/// <summary>
/// 六边形网格管理器。负责创建单元格、生成 Mesh、响应 Inspector 参数变化。
/// 标记 [Tool] 以便在 Godot 编辑器中直接预览网格效果。
/// </summary>
[Tool]
public partial class HexGrid : Node3D
{
    // ==================== Inspector 可调参数 ====================

    [Export(PropertyHint.Range, "1,50,1")]
    public int GridWidth
    {
        get => _gridWidth;
        set
        {
            _gridWidth = Mathf.Max(1, value);
            if (_isReady) Regenerate();
        }
    }
    private int _gridWidth = 6;

    [Export(PropertyHint.Range, "1,50,1")]
    public int GridHeight
    {
        get => _gridHeight;
        set
        {
            _gridHeight = Mathf.Max(1, value);
            if (_isReady) Regenerate();
        }
    }
    private int _gridHeight = 6;

    [Export]
    public Color GridColor
    {
        get => _gridColor;
        set
        {
            _gridColor = value;
            if (_isReady) Regenerate();
        }
    }
    private Color _gridColor = new Color(1f, 0.85f, 0.55f); // 默认沙色

    [Export(PropertyHint.Range, "0,100,1")]
    public int DefaultElevation
    {
        get => _defaultElevation;
        set
        {
            _defaultElevation = value;
            if (_isReady) Regenerate();
        }
    }
    private int _defaultElevation = 0;

    // ==================== 内部状态 ====================

    private bool _isReady = false;
    private MeshInstance3D _meshInstance;
    private HexCell[] _cells;

    // ==================== 生命周期 ====================

    public override void _Ready()
    {
        EnsureMeshInstance();
        _isReady = true;
        Regenerate();
    }


    // ==================== Mesh 生成 ====================

    private void EnsureMeshInstance()
    {
        _meshInstance = GetNodeOrNull<MeshInstance3D>("HexMesh");
        if (_meshInstance == null)
        {
            _meshInstance = new MeshInstance3D();
            _meshInstance.Name = "HexMesh";
            AddChild(_meshInstance);

            // 在编辑器中设置 Owner，使子节点显示在场景树中并可随场景保存
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _meshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }
    }

    private void Regenerate()
    {
        if (_meshInstance == null) return;
        CreateCells();
        Triangulate();
    }

    private void CreateCells()
    {
        _cells = new HexCell[GridWidth * GridHeight];

        for (int z = 0; z < GridHeight; z++)
        {
            for (int x = 0; x < GridWidth; x++)
            {
                CreateCell(x, z);
            }
        }
    }

    /// <summary>计算网格中心的世界坐标（用于相机定位等外部用途）</summary>
    public Vector3 CalculateGridCenter()
    {
        int lastX = GridWidth - 1;
        int lastZ = GridHeight - 1;
        Vector3 lastPos;
        lastPos.X = (lastX + lastZ * 0.5f - lastZ / 2) * (HexMetrics.InnerRadius * 2f);
        lastPos.Y = 0f;
        lastPos.Z = lastZ * (HexMetrics.OuterRadius * 1.5f);
        return lastPos * 0.5f;
    }

    private void CreateCell(int x, int z)
    {
        Vector3 position;
        position.X = (x + z * 0.5f - z / 2) * (HexMetrics.InnerRadius * 2f);
        position.Y = 0f;
        position.Z = z * (HexMetrics.OuterRadius * 1.5f);

        // 给每个格子分配伪随机颜色，相邻格子通常不同，便于观察颜色混合效果
        Color randomColor = GetRandomColor(x, z);

        HexCell cell = new HexCell
        {
            Coordinates = HexCoordinates.FromOffsetCoordinates(x, z),
            Position = position,
            Color = randomColor,
            Elevation = DefaultElevation
        };

        int index = z * GridWidth + x;
        _cells[index] = cell;

        // 连接邻居（West、SE、SW 三个方向已足够，其余方向由邻居连接时补全）
        if (x > 0)
        {
            cell.SetNeighbor(HexDirection.W, _cells[index - 1]);
        }
        if (z > 0)
        {
            if ((z & 1) == 0) // 偶数行
            {
                cell.SetNeighbor(HexDirection.SE, _cells[index - GridWidth]);
                if (x > 0)
                {
                    cell.SetNeighbor(HexDirection.SW, _cells[index - GridWidth - 1]);
                }
            }
            else // 奇数行
            {
                cell.SetNeighbor(HexDirection.SW, _cells[index - GridWidth]);
                if (x < GridWidth - 1)
                {
                    cell.SetNeighbor(HexDirection.SE, _cells[index - GridWidth + 1]);
                }
            }
        }
    }

    private void Triangulate()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        for (int i = 0; i < _cells.Length; i++)
        {
            TriangulateCell(_cells[i], st);
        }

        st.GenerateNormals();
        var mesh = st.Commit();
        _meshInstance.Mesh = mesh;

        // 使用顶点颜色材质（无需纹理贴图即可显示颜色）
        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled // 双面可见
        };
        _meshInstance.MaterialOverride = mat;
    }

    /// <summary>
    /// Part 2：每个格子三角化为中心扇形 + 与邻居的桥接四边形。
    /// 桥接区域使用两个格子的顶点颜色实现颜色混合过渡。
    /// </summary>
    private void TriangulateCell(HexCell cell, SurfaceTool st)
    {
        Vector3 center = cell.Position;
        center.Y += cell.Elevation * HexMetrics.ElevationStep;

        for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
        {
            TriangulateSector(d, cell, center, st);
        }
    }

    /// <summary>三角化一个扇区：中心三角形 + 桥接四边形 + 角落三角形（颜色混合 + 海拔）</summary>
    private void TriangulateSector(HexDirection direction, HexCell cell, Vector3 center, SurfaceTool st)
    {
        Vector3 v1 = center + HexMetrics.GetFirstSolidCorner(direction);
        Vector3 v2 = center + HexMetrics.GetSecondSolidCorner(direction);

        // 1. 中心三角形（当前格子颜色）
        AddTriangle(st, center, v1, v2, cell.Color, cell.Color, cell.Color);

        // 2. 桥接四边形（只处理 E/NE/SE，避免与邻居重复绘制）
        if (direction <= HexDirection.SE)
        {
            HexCell neighbor = cell.GetNeighbor(direction);
            if (neighbor != null)
            {
                Vector3 bridge = HexMetrics.GetBridge(direction);
                Vector3 v3 = v1 + bridge;
                Vector3 v4 = v2 + bridge;

                // 调整 v3/v4 的 Y 到邻居的海拔高度
                float neighborY = neighbor.Position.Y + neighbor.Elevation * HexMetrics.ElevationStep;
                v3.Y = neighborY;
                v4.Y = neighborY;

                HexEdgeType edgeType = cell.GetEdgeType(direction);
                if (edgeType == HexEdgeType.Slope)
                {
                    TriangulateEdgeTerraces(st, v1, v2, cell, v3, v4, neighbor);
                }
                else
                {
                    AddQuad(st, v1, v2, v3, v4, cell.Color, cell.Color, neighbor.Color, neighbor.Color);
                }

                // 3. 角落三角形（三个格子交汇区域，只画 NE/E 避免重复）
                if (direction <= HexDirection.E)
                {
                    HexCell nextNeighbor = cell.GetNeighbor(direction.Next());
                    if (nextNeighbor != null)
                    {
                        Vector3 v5 = v2 + HexMetrics.GetBridge(direction.Next());
                        float nextY = nextNeighbor.Position.Y + nextNeighbor.Elevation * HexMetrics.ElevationStep;
                        v5.Y = nextY;

                        // 按海拔重新排序，确保最低海拔的格子作为 bottom
                        if (cell.Elevation <= neighbor.Elevation)
                        {
                            if (cell.Elevation <= nextNeighbor.Elevation)
                            {
                                TriangulateCorner(st, v2, cell, v4, neighbor, v5, nextNeighbor);
                            }
                            else
                            {
                                TriangulateCorner(st, v5, nextNeighbor, v2, cell, v4, neighbor);
                            }
                        }
                        else if (neighbor.Elevation <= nextNeighbor.Elevation)
                        {
                            TriangulateCorner(st, v4, neighbor, v5, nextNeighbor, v2, cell);
                        }
                        else
                        {
                            TriangulateCorner(st, v5, nextNeighbor, v2, cell, v4, neighbor);
                        }
                    }
                }
            }
        }
    }

    // ==================== SurfaceTool 辅助方法 ====================

    private void AddTriangle(SurfaceTool st, Vector3 v1, Vector3 v2, Vector3 v3, Color c1, Color c2, Color c3)
    {
        st.SetColor(c1); st.AddVertex(v1);
        st.SetColor(c2); st.AddVertex(v2);
        st.SetColor(c3); st.AddVertex(v3);
    }

    /// <summary>
    /// 添加四边形 v1-v2-v3-v4（CCW 从上方看），拆成两个三角形。
    /// 顶点/颜色顺序与 Part 2 原始代码严格一致，确保法线正确。
    /// </summary>
    private void AddQuad(SurfaceTool st, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, Color c1, Color c2, Color c3, Color c4)
    {
        // 三角形 1: v1 → v4 → v2
        st.SetColor(c1); st.AddVertex(v1);
        st.SetColor(c4); st.AddVertex(v4);
        st.SetColor(c2); st.AddVertex(v2);

        // 三角形 2: v1 → v3 → v4
        st.SetColor(c1); st.AddVertex(v1);
        st.SetColor(c3); st.AddVertex(v3);
        st.SetColor(c4); st.AddVertex(v4);
    }

    /// <summary>
    /// Part 3：将一条 Slope 边（海拔差=1）拆分为 Terrace 台阶。
    /// 总步数 = TerraceSteps(=5)，水平均匀前进，垂直只在奇数步上升。
    /// </summary>
    private void TriangulateEdgeTerraces(SurfaceTool st,
        Vector3 beginLeft, Vector3 beginRight, HexCell beginCell,
        Vector3 endLeft, Vector3 endRight, HexCell endCell)
    {
        Vector3 t1 = HexMetrics.TerraceLerp(beginLeft, endLeft, 1);
        Vector3 t2 = HexMetrics.TerraceLerp(beginRight, endRight, 1);
        Color tc1 = HexMetrics.TerraceLerp(beginCell.Color, endCell.Color, 1);
        Color tc2 = HexMetrics.TerraceLerp(beginCell.Color, endCell.Color, 1);

        // 第一段
        AddQuad(st, beginLeft, beginRight, t1, t2, beginCell.Color, beginCell.Color, tc1, tc2);

        for (int i = 2; i < HexMetrics.TerraceSteps; i++)
        {
            Vector3 prevT1 = t1;
            Vector3 prevT2 = t2;
            Color prevTC1 = tc1;
            Color prevTC2 = tc2;

            t1 = HexMetrics.TerraceLerp(beginLeft, endLeft, i);
            t2 = HexMetrics.TerraceLerp(beginRight, endRight, i);
            tc1 = HexMetrics.TerraceLerp(beginCell.Color, endCell.Color, i);
            tc2 = HexMetrics.TerraceLerp(beginCell.Color, endCell.Color, i);

            AddQuad(st, prevT1, prevT2, t1, t2, prevTC1, prevTC2, tc1, tc2);
        }

        // 最后一段
        AddQuad(st, t1, t2, endLeft, endRight, tc1, tc2, endCell.Color, endCell.Color);
    }

    // ==================== Terrace Corner 处理（Part 3 完整实现） ====================

    /// <summary>
    /// 三角化三个格子交汇的角落区域。
    /// 根据底部（最低海拔）格子和左右两边的边类型决定绘制方式。
    /// </summary>
    private void TriangulateCorner(SurfaceTool st,
        Vector3 bottom, HexCell bottomCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell)
    {
        HexEdgeType leftEdgeType = bottomCell.GetEdgeType(leftCell);
        HexEdgeType rightEdgeType = bottomCell.GetEdgeType(rightCell);

        if (leftEdgeType == HexEdgeType.Slope)
        {
            if (rightEdgeType == HexEdgeType.Slope)
            {
                // SSF: 双 Slope + 顶部 Flat
                TriangulateCornerTerraces(st, bottom, bottomCell, left, leftCell, right, rightCell);
                return;
            }
            if (rightEdgeType == HexEdgeType.Flat)
            {
                // SFS: 左 Slope + 右 Flat，从左侧开始 Terrace
                TriangulateCornerTerraces(st, left, leftCell, right, rightCell, bottom, bottomCell);
                return;
            }
            // SFC: Slope + Cliff
            TriangulateCornerTerracesCliff(st, bottom, bottomCell, left, leftCell, right, rightCell);
            return;
        }
        if (rightEdgeType == HexEdgeType.Slope)
        {
            if (leftEdgeType == HexEdgeType.Flat)
            {
                // FSS: 左 Flat + 右 Slope，从右侧开始 Terrace
                TriangulateCornerTerraces(st, right, rightCell, bottom, bottomCell, left, leftCell);
                return;
            }
            // FSC: Flat + Slope + Cliff
            TriangulateCornerTerracesCliff(st, bottom, bottomCell, right, rightCell, left, leftCell);
            return;
        }

        // Flat-Flat-Flat 或 Cliff 情况：简单三角形
        AddTriangle(st, bottom, left, right, bottomCell.Color, leftCell.Color, rightCell.Color);
    }

    /// <summary>双 Slope 角落：从 begin 向 left 和 right 同时做 Terrace</summary>
    private void TriangulateCornerTerraces(SurfaceTool st,
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell)
    {
        Vector3 v3 = HexMetrics.TerraceLerp(begin, left, 1);
        Vector3 v4 = HexMetrics.TerraceLerp(begin, right, 1);
        Color c3 = HexMetrics.TerraceLerp(beginCell.Color, leftCell.Color, 1);
        Color c4 = HexMetrics.TerraceLerp(beginCell.Color, rightCell.Color, 1);

        AddTriangle(st, begin, v3, v4, beginCell.Color, c3, c4);

        for (int i = 2; i < HexMetrics.TerraceSteps; i++)
        {
            Vector3 v1 = v3;
            Vector3 v2 = v4;
            Color c1 = c3;
            Color c2 = c4;
            v3 = HexMetrics.TerraceLerp(begin, left, i);
            v4 = HexMetrics.TerraceLerp(begin, right, i);
            c3 = HexMetrics.TerraceLerp(beginCell.Color, leftCell.Color, i);
            c4 = HexMetrics.TerraceLerp(beginCell.Color, rightCell.Color, i);
            AddQuad(st, v1, v2, v3, v4, c1, c2, c3, c4);
        }

        AddQuad(st, v3, v4, left, right, c3, c4, leftCell.Color, rightCell.Color);
    }

    /// <summary>Slope + Cliff 角落：一侧 Terrace，另一侧在 Cliff 边界处截断</summary>
    private void TriangulateCornerTerracesCliff(SurfaceTool st,
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell)
    {
        float b = 1f / (rightCell.Elevation - beginCell.Elevation);
        Vector3 boundary = begin.Lerp(right, b);
        Color boundaryColor = beginCell.Color.Lerp(rightCell.Color, b);

        TriangulateBoundaryTriangle(st, begin, beginCell, left, leftCell, boundary, boundaryColor);

        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            TriangulateBoundaryTriangle(st, left, leftCell, right, rightCell, boundary, boundaryColor);
        }
        else
        {
            AddTriangle(st, left, right, boundary, leftCell.Color, rightCell.Color, boundaryColor);
        }
    }

    /// <summary>从 begin 到 left 做 Terrace，同时连接到固定的 boundary 点</summary>
    private void TriangulateBoundaryTriangle(SurfaceTool st,
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 boundary, Color boundaryColor)
    {
        Vector3 v2 = HexMetrics.TerraceLerp(begin, left, 1);
        Color c2 = HexMetrics.TerraceLerp(beginCell.Color, leftCell.Color, 1);

        AddTriangle(st, begin, v2, boundary, beginCell.Color, c2, boundaryColor);

        for (int i = 2; i < HexMetrics.TerraceSteps; i++)
        {
            Vector3 v1 = v2;
            Color c1 = c2;
            v2 = HexMetrics.TerraceLerp(begin, left, i);
            c2 = HexMetrics.TerraceLerp(beginCell.Color, leftCell.Color, i);
            AddTriangle(st, v1, v2, boundary, c1, c2, boundaryColor);
        }

        AddTriangle(st, v2, left, boundary, c2, leftCell.Color, boundaryColor);
    }

    /// <summary>基于坐标生成伪随机颜色，确保相邻格子颜色不同且可复现</summary>
    private static Color GetRandomColor(int x, int z)
    {
        // 用简单哈希从坐标生成色相，再转成 RGB
        float hue = ((x * 7 + z * 13) % 360) / 360f;
        return Color.FromHsv(hue, 0.6f, 0.9f);
    }

    // ==================== 工具方法 ====================

    /// <summary>根据坐标获取单元格，越界返回 null</summary>
    public HexCell GetCell(HexCoordinates coordinates)
    {
        int z = coordinates.Z;
        if (z < 0 || z >= GridHeight) return null;
        int x = coordinates.X + z / 2;
        if (x < 0 || x >= GridWidth) return null;
        return _cells[z * GridWidth + x];
    }

    /// <summary>根据世界坐标获取单元格</summary>
    public HexCell GetCell(Vector3 position)
    {
        HexCoordinates coordinates = HexCoordinates.FromPosition(position);
        return GetCell(coordinates);
    }

    /// <summary>保存当前地图为 HexMapData 资源（粗略实现）</summary>
    public HexMapData Save()
    {
        var data = new HexMapData
        {
            Width = GridWidth,
            Height = GridHeight,
            Seed = 0
        };
        // 后续扩展：遍历 _cells 填充 TerrainTypes / Elevations 等数组
        return data;
    }

    /// <summary>从 HexMapData 加载地图（粗略实现）</summary>
    public void Load(HexMapData data)
    {
        if (data == null) return;
        GridWidth = data.Width;
        GridHeight = data.Height;
        // 后续扩展：根据 data.Cells 恢复各单元格状态
        Regenerate();
    }

    // ==================== 鼠标交互（运行时） ====================

    /// <summary>仅重新三角化，不重建单元格数据。用于海拔变化等轻量更新。</summary>
    private void Refresh()
    {
        if (_meshInstance == null || _cells == null) return;
        Triangulate();
    }

    public override void _Input(InputEvent @event)
    {
        // 编辑器中不响应点击
        if (Engine.IsEditorHint()) return;

        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                HandleElevation(mouseButton.Position, 1);  // 左键升高
            }
            else if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                HandleElevation(mouseButton.Position, -1); // 右键降低
            }
        }
    }

    /// <summary>右键点击修改海拔。Ctrl+右键降低，普通右键升高。</summary>
    private void HandleElevation(Vector2 screenPosition, int delta)
    {
        var cell = RaycastToCell(screenPosition);
        if (cell != null)
        {
            cell.Elevation = Mathf.Max(0, cell.Elevation + delta);
            GD.Print($"[HexGrid] Cell {cell.Coordinates} elevation = {cell.Elevation}");
            Refresh();
        }
    }

    /// <summary>从屏幕坐标射线检测获取格子，通用方法供 HandleElevation 使用</summary>
    private HexCell RaycastToCell(Vector2 screenPosition)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null)
        {
            GD.PrintErr("[HexGrid] Camera3D is null! Make sure a Camera3D has current=true.");
            return null;
        }

        var from = camera.ProjectRayOrigin(screenPosition);
        var to = from + camera.ProjectRayNormal(screenPosition) * 1000f;

        // 射线与 Y=0 平面相交（地形基面）
        if (Mathf.Abs(to.Y - from.Y) < 0.001f) return null;
        float t = -from.Y / (to.Y - from.Y);
        if (t < 0) return null;

        Vector3 hit = from + (to - from) * t;
        var cell = GetCell(hit);
        if (cell == null)
        {
            GD.Print($"[HexGrid] No cell at hit position {hit}");
        }
        return cell;
    }
}
