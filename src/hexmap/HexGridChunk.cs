using Godot;

namespace HexMap;

/// <summary>
/// Part 5：六边形网格 Chunk，每个 Chunk 管理自己的一组 Cell 和独立 Mesh。
/// 修改 Cell 后调用 Refresh()，会在下一帧自动重新三角化。
/// </summary>
public partial class HexGridChunk : Node3D
{
    private HexCell[] _cells;
    private MeshInstance3D _meshInstance;
    private MeshInstance3D _riverMeshInstance;
    /* Part 7: 道路网格实例 */
    private MeshInstance3D _roadMeshInstance;
    private bool _needsRefresh = false;
    private Label3D[] _labels;
    /* Part 9: 地形特征管理器 */
    private HexFeatureManager _featureManager;

    public override void _Ready()
    {
        EnsureMeshInstance();
        _cells = new HexCell[HexMetrics.ChunkSizeX * HexMetrics.ChunkSizeZ];
        _labels = new Label3D[_cells.Length];

        /* Part 9: 创建特征管理器子节点 */
        _featureManager = new HexFeatureManager();
        _featureManager.Name = "Features";
        AddChild(_featureManager);
        if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
        {
            _featureManager.Owner = GetTree().EditedSceneRoot;
        }
    }

    public void AddCell(int index, HexCell cell)
    {
        _cells[index] = cell;
        cell.Chunk = this;

        // 创建 Label3D 显示坐标
        var label = new Label3D();
        label.Text = cell.Coordinates.ToStringOnSeparateLines();
        label.FontSize = 32;
        label.PixelSize = 0.04f;
        label.Modulate = Colors.White;
        label.OutlineSize = 8;
        label.OutlineModulate = Colors.Black;
        label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        label.Position = cell.BasePosition + new Vector3(0f, HexMetrics.ElevationStep * 0.5f, 0f);
        label.Name = $"Label_{index}";
        label.Visible = false;
        AddChild(label);
        _labels[index] = label;
    }

    /// <summary>控制本 Chunk 内所有 Label 的显示/隐藏</summary>
    public void ShowLabels(bool visible)
    {
        if (_labels == null) return;
        foreach (var label in _labels)
        {
            if (label != null) label.Visible = visible;
        }
    }

    /// <summary>标记需要刷新。编辑器中立即三角化，运行时默认延迟到 _Process</summary>
    public void Refresh(bool immediate = false)
    {
        if (immediate || Engine.IsEditorHint())
        {
            Triangulate();
        }
        else
        {
            _needsRefresh = true;
        }
    }

    public override void _Process(double delta)
    {
        if (_needsRefresh)
        {
            Triangulate();
            _needsRefresh = false;
        }
    }

    private void Triangulate()
    {
        if (_meshInstance == null) return;

        /* Part 9: 清除旧特征 */
        _featureManager?.Clear();

        /* Part 7: 三输出 BuildMeshes */
        HexMeshBuilder.BuildMeshes(_cells, out Mesh terrainMesh, out Mesh riverMesh, out Mesh roadMesh);
        _meshInstance.Mesh = terrainMesh;
        _riverMeshInstance.Mesh = riverMesh;
        _roadMeshInstance.Mesh = roadMesh;

        if (_meshInstance.MaterialOverride == null)
            _meshInstance.MaterialOverride = LoadTerrainMaterial();

        if (_riverMeshInstance.MaterialOverride == null) _riverMeshInstance.MaterialOverride = LoadRiverMaterial();
        if (_roadMeshInstance.MaterialOverride == null) _roadMeshInstance.MaterialOverride = LoadRoadMaterial();

        /* Part 9: 为每个单元格放置特征 */
        foreach (var cell in _cells)
        {
            if (cell != null) TriangulateCellFeatures(cell);
        }
        _featureManager?.Apply();

        // Debug: 打印河流 mesh 顶点数
        int riverVertexCount = 0;
        if (riverMesh != null && riverMesh.GetSurfaceCount() > 0)
        {
            var arrays = riverMesh.SurfaceGetArrays(0);
            if (arrays.Count > 0 && arrays[0].AsGodotArray().Count > 0)
            {
                riverVertexCount = arrays[0].AsGodotArray().Count;
            }
        }
        int riverCellCount = 0;
        foreach (var c in _cells) if (c != null && c.HasRiver) riverCellCount++;
        if (riverCellCount > 0)
        {
            GD.Print($"[HexGridChunk] Triangulate: {Name}, riverCells={riverCellCount}, riverVertices={riverVertexCount}");
        }
    }

    /* Part 9: 为单个单元格放置特征 */
    private void TriangulateCellFeatures(HexCell cell)
    {
        /* 中心特征：仅当无河流且非水下时放置 */
        if (!cell.IsUnderwater && !cell.HasRiver)
        {
            _featureManager.AddFeature(cell.Position, cell);
        }

        /* 各方向特征 */
        for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
        {
            if (!cell.IsUnderwater && (!cell.HasRiver || !cell.HasRiverThroughEdge(d)) && !cell.HasRoadThroughEdge(d))
            {
                Vector3 edgePos = cell.Position * 2f + HexMetrics.GetSolidEdgeMiddle(d);
                _featureManager.AddFeature(edgePos, cell);
            }
        }
    }

    private void EnsureMeshInstance()
    {
        _meshInstance = GetNodeOrNull<MeshInstance3D>("HexMesh");
        if (_meshInstance == null)
        {
            _meshInstance = new MeshInstance3D();
            _meshInstance.Name = "HexMesh";
            AddChild(_meshInstance);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _meshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }

        _riverMeshInstance = GetNodeOrNull<MeshInstance3D>("Rivers");
        if (_riverMeshInstance == null)
        {
            _riverMeshInstance = new MeshInstance3D();
            _riverMeshInstance.Name = "Rivers";
            if (_riverMeshInstance.MaterialOverride == null) _riverMeshInstance.MaterialOverride = LoadRiverMaterial();
            AddChild(_riverMeshInstance);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _riverMeshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }

        /* Part 7: 创建道路网格实例 */
        _roadMeshInstance = GetNodeOrNull<MeshInstance3D>("Roads");
        if (_roadMeshInstance == null)
        {
            _roadMeshInstance = new MeshInstance3D();
            _roadMeshInstance.Name = "Roads";
            if (_roadMeshInstance.MaterialOverride == null) _roadMeshInstance.MaterialOverride = LoadRoadMaterial();
            AddChild(_roadMeshInstance);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _roadMeshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }
    }

    private static ShaderMaterial LoadRiverMaterial()
    {
        return ResourceLoader.Load<ShaderMaterial>("res://assets/materials/river.tres");
    }

    /* Part 7: 创建道路材质（基于 UV.x 透明度混合 + 噪声扰动产生粗糙边缘） */
    private static ShaderMaterial LoadRoadMaterial()
    {
        return ResourceLoader.Load<ShaderMaterial>("res://assets/materials/road.tres");
    }

    private static Material LoadTerrainMaterial()
    {
        return ResourceLoader.Load<ShaderMaterial>("res://assets/materials/terrain.tres");
    }
}
