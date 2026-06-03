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
    /// Part 1：每个格子三角化为一个实心六边形（6个扇形三角形）。
    /// 后续 Part 会扩展为包含邻居桥接、颜色混合、海拔台阶等。
    /// </summary>
    private void TriangulateCell(HexCell cell, SurfaceTool st)
    {
        Vector3 center = cell.Position;
        center.Y += cell.Elevation * HexMetrics.ElevationStep;

        for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
        {
            Vector3 v1 = center + HexMetrics.GetFirstSolidCorner(d);
            Vector3 v2 = center + HexMetrics.GetSecondSolidCorner(d);

            st.SetColor(cell.Color);
            st.AddVertex(center);
            st.AddVertex(v1);
            st.AddVertex(v2);
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
}
