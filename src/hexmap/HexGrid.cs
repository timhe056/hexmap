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

    /// <summary>运行时点击格子变色的目标颜色</summary>
    [Export]
    public Color TouchColor
    {
        get => _touchColor;
        set => _touchColor = value;
    }
    private Color _touchColor = new Color(0.8f, 0.2f, 0.2f); // 默认红色

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

    private void CreateCell(int x, int z)
    {
        Vector3 position;
        position.X = (x + z * 0.5f - z / 2) * (HexMetrics.InnerRadius * 2f);
        position.Y = 0f;
        position.Z = z * (HexMetrics.OuterRadius * 1.5f);

        HexCell cell = new HexCell
        {
            Coordinates = HexCoordinates.FromOffsetCoordinates(x, z),
            Position = position,
            Color = GridColor,
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

    /// <summary>三角化一个扇区：中心三角形 + 桥接四边形 + 角落三角形（颜色混合）</summary>
    private void TriangulateSector(HexDirection direction, HexCell cell, Vector3 center, SurfaceTool st)
    {
        Vector3 v1 = center + HexMetrics.GetFirstSolidCorner(direction);
        Vector3 v2 = center + HexMetrics.GetSecondSolidCorner(direction);

        // 1. 中心三角形（当前格子颜色）
        st.SetColor(cell.Color);
        st.AddVertex(center);
        st.SetColor(cell.Color);
        st.AddVertex(v1);
        st.SetColor(cell.Color);
        st.AddVertex(v2);

        // 2. 桥接四边形（只处理 E/NE/SE，避免与邻居重复绘制）
        if (direction <= HexDirection.SE)
        {
            HexCell neighbor = cell.GetNeighbor(direction);
            if (neighbor != null)
            {
                Vector3 bridge = HexMetrics.GetBridge(direction);
                Vector3 v3 = v1 + bridge;
                Vector3 v4 = v2 + bridge;

                // 四边形 v1-v2-v4-v3 拆成两个三角形，颜色渐变
                // 三角形 1: v1(cell) → v2(cell) → v4(neighbor)
                st.SetColor(cell.Color);
                st.AddVertex(v1);
                st.SetColor(cell.Color);
                st.AddVertex(v2);
                st.SetColor(neighbor.Color);
                st.AddVertex(v4);

                // 三角形 2: v1(cell) → v4(neighbor) → v3(neighbor)
                st.SetColor(cell.Color);
                st.AddVertex(v1);
                st.SetColor(neighbor.Color);
                st.AddVertex(v4);
                st.SetColor(neighbor.Color);
                st.AddVertex(v3);

                // 3. 角落三角形（三个格子交汇区域，只画 NE/E 避免重复）
                if (direction <= HexDirection.E)
                {
                    HexCell nextNeighbor = cell.GetNeighbor(direction.Next());
                    if (nextNeighbor != null)
                    {
                        Vector3 v5 = v2 + HexMetrics.GetBridge(direction.Next());

                        st.SetColor(cell.Color);
                        st.AddVertex(v2);
                        st.SetColor(neighbor.Color);
                        st.AddVertex(v4);
                        st.SetColor(nextNeighbor.Color);
                        st.AddVertex(v5);
                    }
                }
            }
        }
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

    /// <summary>运行时鼠标左键点击格子，将其颜色设为 TouchColor</summary>
    public void TouchCell(HexCell cell)
    {
        if (cell == null) return;
        cell.Color = TouchColor;
        Refresh(); // 只重绘 Mesh，不重建 HexCell（避免颜色被重置）
    }

    /// <summary>仅重新三角化，不重建单元格数据。用于颜色变化等轻量更新。</summary>
    private void Refresh()
    {
        if (_meshInstance == null || _cells == null) return;
        Triangulate();
    }

    public override void _Input(InputEvent @event)
    {
        // 编辑器中不响应点击
        if (Engine.IsEditorHint()) return;

        if (@event is InputEventMouseButton mouseButton
            && mouseButton.Pressed
            && mouseButton.ButtonIndex == MouseButton.Left)
        {
            HandleTouch(mouseButton.Position);
        }
    }

    private void HandleTouch(Vector2 screenPosition)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null)
        {
            GD.PrintErr("[HexGrid] Camera3D is null! Make sure a Camera3D has current=true.");
            return;
        }

        var from = camera.ProjectRayOrigin(screenPosition);
        var to = from + camera.ProjectRayNormal(screenPosition) * 1000f;

        // 射线与 Y=0 平面相交（地形基面）
        if (Mathf.Abs(to.Y - from.Y) < 0.001f) return;
        float t = -from.Y / (to.Y - from.Y);
        if (t < 0) return;

        Vector3 hit = from + (to - from) * t;
        var cell = GetCell(hit);
        if (cell != null)
        {
            GD.Print($"[HexGrid] Clicked cell {cell.Coordinates}");
            TouchCell(cell);
        }
        else
        {
            GD.Print($"[HexGrid] No cell at hit position {hit}");
        }
    }
}
