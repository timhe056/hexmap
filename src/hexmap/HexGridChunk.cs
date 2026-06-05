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
    /* Part 8: 水面网格实例 */
    private MeshInstance3D _waterMeshInstance;
    private MeshInstance3D _waterShoreMeshInstance;
    private MeshInstance3D _estuaryMeshInstance;
    private bool _needsRefresh = false;
    private Label3D[] _labels;
    /* Part 9: 地形特征管理器 */
    private HexFeatureManager _featureManager;

    public override void _Ready()
    {
        EnsureMeshInstance();
        _cells = new HexCell[HexMetrics.ChunkSizeX * HexMetrics.ChunkSizeZ];
        _labels = new Label3D[_cells.Length];

        /* Part 9: 创建特征管理器子节点（如果 SetFeatureCollections 已提前创建则跳过） */
        if (_featureManager == null)
        {
            _featureManager = new HexFeatureManager();
            _featureManager.Name = "Features";
            AddChild(_featureManager);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _featureManager.Owner = GetTree().EditedSceneRoot;
            }
        }
    }

    /* Part 9: 接收 HexGrid 统一加载的 prefab 集合 */
    public void SetFeatureCollections(HexFeatureCollection[] urban, HexFeatureCollection[] farm, HexFeatureCollection[] plant)
    {
        if (_featureManager == null)
        {
            _featureManager = new HexFeatureManager();
            _featureManager.Name = "Features";
            AddChild(_featureManager);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _featureManager.Owner = GetTree().EditedSceneRoot;
            }
        }
        _featureManager.SetCollections(urban, farm, plant);
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

        /* Part 8: 六输出 BuildMeshes */
        HexMeshBuilder.BuildMeshes(_cells,
            out Mesh terrainMesh, out Mesh riverMesh, out Mesh roadMesh,
            out Mesh waterMesh, out Mesh waterShoreMesh, out Mesh estuaryMesh);
        _meshInstance.Mesh = terrainMesh;
        _riverMeshInstance.Mesh = riverMesh;
        _roadMeshInstance.Mesh = roadMesh;
        _waterMeshInstance.Mesh = waterMesh;
        _waterShoreMeshInstance.Mesh = waterShoreMesh;
        _estuaryMeshInstance.Mesh = estuaryMesh;

        if (_meshInstance.MaterialOverride == null)
            _meshInstance.MaterialOverride = LoadTerrainMaterial();

        if (_riverMeshInstance.MaterialOverride == null) _riverMeshInstance.MaterialOverride = LoadRiverMaterial();
        if (_roadMeshInstance.MaterialOverride == null) _roadMeshInstance.MaterialOverride = LoadRoadMaterial();
        if (_waterMeshInstance.MaterialOverride == null) _waterMeshInstance.MaterialOverride = LoadWaterMaterial();
        if (_waterShoreMeshInstance.MaterialOverride == null) _waterShoreMeshInstance.MaterialOverride = LoadWaterShoreMaterial();
        if (_estuaryMeshInstance.MaterialOverride == null) _estuaryMeshInstance.MaterialOverride = LoadEstuaryMaterial();

        /* 确保 river/estuary 在 water/waterShore 之后渲染（匹配 Unity Queue=Transparent+1） */
        _riverMeshInstance.MaterialOverride.RenderPriority = 1;
        _estuaryMeshInstance.MaterialOverride.RenderPriority = 1;

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
        /* 中心特征：仅当无河流、非水下、无道路时放置 */
        if (!cell.IsUnderwater && !cell.HasRiver && !cell.HasRoads)
        {
            _featureManager.AddFeature(cell, cell.Position);
        }

        /* 各方向边缘特征 */
        for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
        {
            if (!cell.IsUnderwater && !cell.HasRiverThroughEdge(d) && !cell.HasRoadThroughEdge(d))
            {
                Vector3 center = cell.Position;
                HexMeshBuilder.EdgeVertices e = new HexMeshBuilder.EdgeVertices(
                    center + HexMetrics.GetFirstSolidCorner(d),
                    center + HexMetrics.GetSecondSolidCorner(d)
                );
                Vector3 edgePos = (center + e.v1 + e.v5) * (1f / 3f);
                _featureManager.AddFeature(cell, edgePos);
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

        /* Part 8: 创建水面网格实例 — 先于 Rivers，使河流渲染在水面之上 */
        _waterMeshInstance = GetNodeOrNull<MeshInstance3D>("Water");
        if (_waterMeshInstance == null)
        {
            _waterMeshInstance = new MeshInstance3D();
            _waterMeshInstance.Name = "Water";
            if (_waterMeshInstance.MaterialOverride == null) _waterMeshInstance.MaterialOverride = LoadWaterMaterial();
            AddChild(_waterMeshInstance);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _waterMeshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }

        /* Part 8: 创建岸边水体网格实例 */
        _waterShoreMeshInstance = GetNodeOrNull<MeshInstance3D>("WaterShore");
        if (_waterShoreMeshInstance == null)
        {
            _waterShoreMeshInstance = new MeshInstance3D();
            _waterShoreMeshInstance.Name = "WaterShore";
            if (_waterShoreMeshInstance.MaterialOverride == null) _waterShoreMeshInstance.MaterialOverride = LoadWaterShoreMaterial();
            AddChild(_waterShoreMeshInstance);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _waterShoreMeshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }

        /* Part 8: 创建河口网格实例 */
        _estuaryMeshInstance = GetNodeOrNull<MeshInstance3D>("Estuaries");
        if (_estuaryMeshInstance == null)
        {
            _estuaryMeshInstance = new MeshInstance3D();
            _estuaryMeshInstance.Name = "Estuaries";
            if (_estuaryMeshInstance.MaterialOverride == null) _estuaryMeshInstance.MaterialOverride = LoadEstuaryMaterial();
            AddChild(_estuaryMeshInstance);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _estuaryMeshInstance.Owner = GetTree().EditedSceneRoot;
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

    /* Part 8: 水面材质（蓝色半透明） */
    private static ShaderMaterial LoadWaterMaterial()
    {
        return ResourceLoader.Load<ShaderMaterial>("res://assets/materials/water.tres");
    }

    /* Part 8: 岸边水体材质（泡沫效果） */
    private static ShaderMaterial LoadWaterShoreMaterial()
    {
        return ResourceLoader.Load<ShaderMaterial>("res://assets/materials/water_shore.tres");
    }

    /* Part 8: 河口材质（岸边+河流混合） */
    private static ShaderMaterial LoadEstuaryMaterial()
    {
        return ResourceLoader.Load<ShaderMaterial>("res://assets/materials/estuary.tres");
    }
}
