using Godot;

namespace HexMap;

/// <summary>Part 6：三态开关（忽略 / 是 / 否）</summary>
public enum OptionalToggle { Ignore, Yes, No }

public static class OptionalToggleExtensions
{
    /// <summary>循环切换：Ignore → Yes → No → Ignore</summary>
    public static OptionalToggle Next(this OptionalToggle toggle)
    {
        return toggle switch
        {
            OptionalToggle.Ignore => OptionalToggle.Yes,
            OptionalToggle.Yes => OptionalToggle.No,
            OptionalToggle.No => OptionalToggle.Ignore,
            _ => OptionalToggle.Ignore,
        };
    }
}

/// <summary>
/// Part 5：六边形网格管理器。
/// 负责创建 Chunk、分配 Cell、响应 Inspector 参数变化。
/// 三角化逻辑已完全移至 HexMeshBuilder。
/// </summary>
[Tool]
public partial class HexGrid : Node3D
{
    // ==================== Inspector 可调参数 ====================

    /* Part 2.1: 以 Cell 为单位的地图尺寸，不再暴露给 Inspector */
    public int CellCountX
    {
        get => _cellCountX;
        set
        {
            _cellCountX = Mathf.Max(1, value);
            if (_isReady) CreateMap(_cellCountX, _cellCountZ);
        }
    }
    private int _cellCountX = 20;

    public int CellCountZ
    {
        get => _cellCountZ;
        set
        {
            _cellCountZ = Mathf.Max(1, value);
            if (_isReady) CreateMap(_cellCountX, _cellCountZ);
        }
    }
    private int _cellCountZ = 15;

    private int _chunkCountX;
    private int _chunkCountZ;

    [Export]
    public Color GridColor
    {
        get => _gridColor;
        set
        {
            _gridColor = value;
            if (_isReady) CreateMap(_cellCountX, _cellCountZ);
        }
    }
    private Color _gridColor = new Color(1f, 0.85f, 0.55f);

    [Export(PropertyHint.Range, "0,100,1")]
    public int DefaultElevation
    {
        get => _defaultElevation;
        set
        {
            _defaultElevation = value;
            if (_isReady) CreateMap(_cellCountX, _cellCountZ);
        }
    }
    private int _defaultElevation = 0;

    /* Part 9: 种子值，控制哈希网格确定性随机 */
    [Export(PropertyHint.Range, "0,9999,1")]
    public int Seed
    {
        get => _seed;
        set
        {
            _seed = value;
            if (_isReady) CreateMap(_cellCountX, _cellCountZ);
        }
    }
    private int _seed = 1234;

    [Export(PropertyHint.Range, "0,4,1")]
    public int BrushSize
    {
        get => _brushSize;
        set => _brushSize = value;
    }
    private int _brushSize = 0;

    /* Part 20: 调试开关，强制全图可见（忽略战争迷雾） */
    [Export]
    public bool DebugAlwaysVisible
    {
        get => _debugAlwaysVisible;
        set
        {
            _debugAlwaysVisible = value;
            RenderingServer.GlobalShaderParameterSet("debug_always_visible", value);
            GD.Print($"[HexGrid] DebugAlwaysVisible = {value}");
        }
    }
    private bool _debugAlwaysVisible = false;

    /// <summary>当前要高程设置的目标值（UI 滑块控制）</summary>
    public int ActiveElevation { get; set; } = 0;

    /// <summary>是否应用高程编辑</summary>
    public bool ApplyElevation { get; set; } = true;

    /* Part 8: 水位编辑 */
    public bool ApplyWaterLevel { get; set; } = false;
    public int ActiveWaterLevel { get; set; } = 0;

    /* Part 12: 地形类型编辑（替代原来的 ApplyColor / ActiveColorIndex） */
    public int ActiveTerrainTypeIndex { get; set; } = -1;

    /// <summary>笔刷模式开关：开启时鼠标移动显示笔刷范围预览</summary>
    public bool BrushModeEnabled { get; set; } = false;

    /// <summary>Part 6：河流编辑模式</summary>
    public OptionalToggle RiverMode { get; set; } = OptionalToggle.Ignore;

    /* Part 7: 道路编辑模式 */
    public OptionalToggle RoadMode { get; set; } = OptionalToggle.Ignore;

    /* Part 10: 城墙编辑模式 */
    public OptionalToggle WalledMode { get; set; } = OptionalToggle.Ignore;

    /* Part 9: 地形特征级别（UI 滑块控制） */
    public int ActiveUrbanLevel { get; set; } = 0;
    public int ActiveFarmLevel { get; set; } = 0;
    public int ActivePlantLevel { get; set; } = 0;
    public bool ApplyUrbanLevel { get; set; } = false;
    public bool ApplyFarmLevel { get; set; } = false;
    public bool ApplyPlantLevel { get; set; } = false;

    /* Part 11: 特殊特征索引 */
    public int ActiveSpecialIndex { get; set; } = 0;
    public bool ApplySpecialIndex { get; set; } = false;

    /// <summary>Part 12: 地形颜色预设（由 HexMetrics.Colors 统一读取）</summary>
    [Export]
    public Color[] Colors { get; set; } = new[] {
        new Color(0.90f, 0.85f, 0.55f), // 沙色 (默认)
        new Color(0.35f, 0.65f, 0.25f), // 草地绿
        new Color(0.15f, 0.45f, 0.20f), // 森林绿
        new Color(0.25f, 0.55f, 0.75f), // 水域蓝
        new Color(0.55f, 0.40f, 0.25f), // 泥土棕
        new Color(0.70f, 0.70f, 0.75f), // 岩石灰
        new Color(0.90f, 0.95f, 0.95f), // 雪地白
        new Color(0.65f, 0.30f, 0.20f), // 熔岩红
    };

    // ==================== 内部状态 ====================

    private bool _isReady = false;
    private HexGridChunk[] _chunks;

    /* Part 3.2.0: cell 搜索临时数据与视野计数，由 grid 统一持有 */
    private HexCellSearchData[] _searchData;
    private int[] _cellVisibility;
    public HexCellSearchData[] SearchData => _searchData;
    public bool IsCellVisible(int cellIndex) => _cellVisibility[cellIndex] > 0;

    /* Part 3.3.0: cell 数据与位置由 grid 统一持有 */
    public HexCellData[] CellData { get; private set; }
    public Vector3[] CellPositions { get; private set; }

    /* Part 3.4.0: 单元格相关引用由 grid 统一持有 */
    public HexUnit[] CellUnits { get; private set; }
    private HexGridChunk[] _cellChunks;
    private Label3D[] _cellLabels;
    private MeshInstance3D[] _cellHighlights;
    /* Part 27: 每列 chunk 的父节点，用于视觉环绕 */
    private Node3D[] _columns;
    private int _currentCenterColumnIndex = -1;
    private MeshInstance3D _brushPreview;

    /* Part 27: 当前地图是否东西向环绕 */
    public bool Wrapping { get; private set; }

    /* Part 9: 统一加载的特征 prefab 集合 */
    private HexFeatureManager.HexFeatureCollection[] _urbanCollections;
    private HexFeatureManager.HexFeatureCollection[] _farmCollections;
    private HexFeatureManager.HexFeatureCollection[] _plantCollections;

    /* Part 11: 城墙塔楼、桥梁、特殊特征 prefab */
    private PackedScene _wallTowerPrefab;
    private PackedScene _bridgePrefab;
    private PackedScene[] _specialPrefabs;

    // CellCountX/CellCountZ 现在是主属性，chunk 计数由它们推导

    // ==================== 生命周期 ====================

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;
        /* Part 20: 高 process_priority 确保 _Process 在所有默认节点之后执行，等价 Unity LateUpdate */
        ProcessPriority = 100;
        HexMetrics.InitializeNoise();
        /* Part 9: 初始化哈希网格 */
        HexMetrics.InitializeHashGrid(Seed);
        /* Part 12: 注入地形颜色数组 */
        HexMetrics.Colors = Colors;
        LoadFeaturePrefabs();
        EnsureBrushPreview();
        _isReady = true;
        /* Part 2.1: 初始地图硬编码为 20×15 且不环绕 */
        CreateMap(20, 15, false);
    }

    private void EnsureBrushPreview()
    {
        _brushPreview = GetNodeOrNull<MeshInstance3D>("BrushPreview");
        if (_brushPreview == null)
        {
            _brushPreview = new MeshInstance3D();
            _brushPreview.Name = "BrushPreview";
            _brushPreview.Visible = false;
            AddChild(_brushPreview);
        }
        var mat = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = new Color(0.3f, 1f, 0.3f, 0.35f)
        };
        _brushPreview.MaterialOverride = mat;
    }

    // ==================== 网格生成 ====================

    // ==================== Part 13: CreateMap ====================

    public bool CreateMap(int x, int z)
    {
        return CreateMap(x, z, false);
    }

    public bool CreateMap(int x, int z, bool wrapping)
    {
        if (x <= 0 || x % HexMetrics.ChunkSizeX != 0 ||
            z <= 0 || z % HexMetrics.ChunkSizeZ != 0)
        {
            GD.PushError("[HexGrid] Unsupported map size.");
            return false;
        }

        StopSearch();
        ClearUnits();
        ClearChunks();

        _cellCountX = x;
        _cellCountZ = z;
        _chunkCountX = _cellCountX / HexMetrics.ChunkSizeX;
        _chunkCountZ = _cellCountZ / HexMetrics.ChunkSizeZ;
        Wrapping = wrapping;
        _currentCenterColumnIndex = -1;
        HexMetrics.wrapSize = wrapping ? _cellCountX : 0;

        /* Part 20: 初始化 cell data texture */
        _cellShaderData = new HexCellShaderData();
        _cellShaderData.Initialize(x, z);
        _cellShaderData.Grid = this;
        RenderingServer.GlobalShaderParameterSet("hex_cell_data", _cellShaderData.Texture);
        RenderingServer.GlobalShaderParameterSet("hex_cell_data_texel_size", new Vector4(1f / x, 1f / z, x, z));
        RenderingServer.GlobalShaderParameterSet("debug_always_visible", DebugAlwaysVisible);

        CreateChunks();
        CreateCells();

        // 初始化完成后标记所有 chunk 需要在下一帧 _Process 中三角化
        if (_chunks != null)
        {
            foreach (var chunk in _chunks)
            {
                chunk?.Refresh();
            }
        }
        // 同步 label 显示状态到新地图
        ShowLabels(_showLabels);
        return true;
    }

    private void ClearChunks()
    {
        var oldMesh = GetNodeOrNull<MeshInstance3D>("HexMesh");
        if (oldMesh != null)
        {
            oldMesh.QueueFree();
        }

        /* Part 27: 释放 column 父节点会自动释放其下 chunk */
        if (_columns != null)
        {
            for (int i = 0; i < _columns.Length; i++)
            {
                _columns[i]?.Free();
            }
            _columns = null;
        }

        foreach (var child in GetChildren())
        {
            if (child is HexGridChunk)
            {
                child.Free();
            }
        }
        _chunks = null;
        CellData = null;
        CellPositions = null;
        CellUnits = null;
        _cellChunks = null;
        _cellLabels = null;
        _cellHighlights = null;
        _searchData = null;
        _cellVisibility = null;
    }

    private void CreateChunks()
    {
        /* Part 27: 先创建 column 父节点 */
        _columns = new Node3D[_chunkCountX];
        for (int x = 0; x < _chunkCountX; x++)
        {
            _columns[x] = new Node3D();
            _columns[x].Name = $"Column_{x}";
            AddChild(_columns[x]);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _columns[x].Owner = GetTree().EditedSceneRoot;
            }
        }

        _chunks = new HexGridChunk[_chunkCountX * _chunkCountZ];
        for (int z = 0, i = 0; z < _chunkCountZ; z++)
        {
            for (int x = 0; x < _chunkCountX; x++)
            {
                var chunk = new HexGridChunk();
                chunk.Name = $"Chunk_{x}_{z}";
                chunk.ColumnIndex = x;
                chunk.Grid = this;
                chunk.SetFeatureCollections(_urbanCollections, _farmCollections, _plantCollections, _wallTowerPrefab, _bridgePrefab, _specialPrefabs);
                _columns[x].AddChild(chunk);
                if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
                {
                    chunk.Owner = GetTree().EditedSceneRoot;
                }
                _chunks[i++] = chunk;
            }
        }
    }

    /* Part 9: 统一加载特征 prefab（只执行一次） */
    private void LoadFeaturePrefabs()
    {
        _urbanCollections = new HexFeatureManager.HexFeatureCollection[3];
        _urbanCollections[0] = new HexFeatureManager.HexFeatureCollection(new[] {
            LoadPrefab("res://assets/features/urban/urban_high_1.tscn"),
            LoadPrefab("res://assets/features/urban/urban_high_2.tscn")
        });
        _urbanCollections[1] = new HexFeatureManager.HexFeatureCollection(new[] {
            LoadPrefab("res://assets/features/urban/urban_medium_1.tscn"),
            LoadPrefab("res://assets/features/urban/urban_medium_2.tscn")
        });
        _urbanCollections[2] = new HexFeatureManager.HexFeatureCollection(new[] {
            LoadPrefab("res://assets/features/urban/urban_low_1.tscn"),
            LoadPrefab("res://assets/features/urban/urban_low_2.tscn")
        });

        _farmCollections = new HexFeatureManager.HexFeatureCollection[3];
        _farmCollections[0] = new HexFeatureManager.HexFeatureCollection(new[] {
            LoadPrefab("res://assets/features/farm/farm_high_1.tscn"),
            LoadPrefab("res://assets/features/farm/farm_high_2.tscn")
        });
        _farmCollections[1] = new HexFeatureManager.HexFeatureCollection(new[] {
            LoadPrefab("res://assets/features/farm/farm_medium_1.tscn"),
            LoadPrefab("res://assets/features/farm/farm_medium_2.tscn")
        });
        _farmCollections[2] = new HexFeatureManager.HexFeatureCollection(new[] {
            LoadPrefab("res://assets/features/farm/farm_low_1.tscn"),
            LoadPrefab("res://assets/features/farm/farm_low_2.tscn")
        });

        _plantCollections = new HexFeatureManager.HexFeatureCollection[3];
        _plantCollections[0] = new HexFeatureManager.HexFeatureCollection(new[] {
            LoadPrefab("res://assets/features/plant/plant_high_1.tscn"),
            LoadPrefab("res://assets/features/plant/plant_high_2.tscn")
        });
        _plantCollections[1] = new HexFeatureManager.HexFeatureCollection(new[] {
            LoadPrefab("res://assets/features/plant/plant_medium_1.tscn"),
            LoadPrefab("res://assets/features/plant/plant_medium_2.tscn")
        });
        _plantCollections[2] = new HexFeatureManager.HexFeatureCollection(new[] {
            LoadPrefab("res://assets/features/plant/plant_low_1.tscn"),
            LoadPrefab("res://assets/features/plant/plant_low_2.tscn")
        });

        int CountNonNull(HexFeatureManager.HexFeatureCollection[] collections)
        {
            int count = 0;
            foreach (var c in collections)
            {
                if (c.Prefabs != null)
                    foreach (var p in c.Prefabs) if (p != null) count++;
            }
            return count;
        }

        /* Part 11: 加载新 prefab */
        _wallTowerPrefab = LoadPrefab("res://assets/features/wall_tower.tscn");
        _bridgePrefab = LoadPrefab("res://assets/features/bridge.tscn");
        _specialPrefabs = new PackedScene[] {
            LoadPrefab("res://assets/features/special/castle.tscn"),
            LoadPrefab("res://assets/features/special/ziggurat.tscn"),
            LoadPrefab("res://assets/features/special/megaflora.tscn")
        };

        GD.Print($"[HexGrid] Feature prefabs loaded: urban={CountNonNull(_urbanCollections)}, farm={CountNonNull(_farmCollections)}, plant={CountNonNull(_plantCollections)}");
    }

    private PackedScene LoadPrefab(string path)
    {
        var scene = ResourceLoader.Load<PackedScene>(path);
        if (scene == null) GD.PushWarning($"[HexGrid] Failed to load prefab: {path}");
        return scene;
    }


    private void CreateCells()
    {
        int count = CellCountZ * CellCountX;
        CellData = new HexCellData[count];
        CellPositions = new Vector3[count];
        CellUnits = new HexUnit[count];
        _cellChunks = new HexGridChunk[count];
        _cellLabels = new Label3D[count];
        _cellHighlights = new MeshInstance3D[count];
        _searchData = new HexCellSearchData[count];
        _cellVisibility = new int[count];
        for (int z = 0, i = 0; z < CellCountZ; z++)
        {
            for (int x = 0; x < CellCountX; x++)
            {
                CreateCell(x, z, i++);
            }
        }
    }

    private void CreateCell(int x, int z, int i)
    {
        Vector3 position;
        position.X = (x + z * 0.5f - z / 2) * HexMetrics.InnerDiameter;
        position.Y = 0f;
        position.Z = z * (HexMetrics.OuterRadius * 1.5f);

        HexCoordinates coordinates = HexCoordinates.FromOffsetCoordinates(x, z);
        CellPositions[i] = position;
        CellData[i].coordinates = coordinates;
        CellData[i].values = CellData[i].values.WithTerrainTypeIndex(0);

        var cell = new HexCell(i, this);
        bool explorable = Wrapping
            ? z > 0 && z < _cellCountZ - 1
            : x > 0 && z > 0 && x < _cellCountX - 1 && z < _cellCountZ - 1;
        cell.Flags = explorable
            ? cell.Flags.With(HexFlags.Explorable)
            : cell.Flags.Without(HexFlags.Explorable);

        AddCellToChunk(x, z, i);

        cell.Values = cell.Values.WithElevation(DefaultElevation);
        RefreshCellPosition(i);
    }

    private void AddCellToChunk(int x, int z, int cellIndex)
    {
        int chunkX = x / HexMetrics.ChunkSizeX;
        int chunkZ = z / HexMetrics.ChunkSizeZ;
        HexGridChunk chunk = _chunks[chunkX + chunkZ * _chunkCountX];

        int localX = x - chunkX * HexMetrics.ChunkSizeX;
        int localZ = z - chunkZ * HexMetrics.ChunkSizeZ;
        chunk.AddCell(
            localX + localZ * HexMetrics.ChunkSizeX,
            cellIndex,
            out Label3D label,
            out MeshInstance3D highlight);

        _cellLabels[cellIndex] = label;
        _cellHighlights[cellIndex] = highlight;
        _cellChunks[cellIndex] = chunk;
    }

    /* Part 27: 根据相机 X 位置重新排列 column，实现视觉环绕 */
    public void CenterMap(float xPosition)
    {
        if (_columns == null) return;

        int centerColumnIndex = (int)(xPosition / (HexMetrics.InnerDiameter * HexMetrics.ChunkSizeX));

        if (centerColumnIndex == _currentCenterColumnIndex) return;
        _currentCenterColumnIndex = centerColumnIndex;

        int minColumnIndex = centerColumnIndex - _chunkCountX / 2;
        int maxColumnIndex = centerColumnIndex + _chunkCountX / 2;

        Vector3 position;
        position.Y = 0f;
        position.Z = 0f;
        float mapWidth = _chunkCountX * (HexMetrics.InnerDiameter * HexMetrics.ChunkSizeX);
        for (int i = 0; i < _columns.Length; i++)
        {
            if (i < minColumnIndex)
            {
                position.X = mapWidth;
            }
            else if (i > maxColumnIndex)
            {
                position.X = -mapWidth;
            }
            else
            {
                position.X = 0f;
            }
            _columns[i].Position = position;
        }
    }

    /* Part 27: 把子节点挂到指定 column 下，保持世界坐标不变 */
    public void MakeChildOfColumn(Node3D child, int columnIndex)
    {
        if (_columns == null || columnIndex < 0 || columnIndex >= _columns.Length) return;
        if (child.GetParent() == _columns[columnIndex]) return;
        child.Reparent(_columns[columnIndex]);
    }

    /// <summary>计算网格中心的世界坐标（用于相机定位等外部用途）</summary>
    public Vector3 CalculateGridCenter()
    {
        int lastX = CellCountX - 1;
        int lastZ = CellCountZ - 1;
        Vector3 lastPos;
        lastPos.X = (lastX + lastZ * 0.5f - lastZ / 2) * (HexMetrics.InnerRadius * 2f);
        lastPos.Y = 0f;
        lastPos.Z = lastZ * (HexMetrics.OuterRadius * 1.5f);
        return lastPos * 0.5f;
    }

    // ==================== 工具方法 ====================

    public HexCell GetCell(HexCoordinates coordinates)
    {
        if (TryGetCellIndex(coordinates, out int cellIndex))
        {
            return new HexCell(cellIndex, this);
        }
        return default;
    }

    public bool TryGetCell(HexCoordinates coordinates, out HexCell cell)
    {
        if (TryGetCellIndex(coordinates, out int cellIndex))
        {
            cell = new HexCell(cellIndex, this);
            return true;
        }
        cell = default;
        return false;
    }

    public bool TryGetCellIndex(HexCoordinates coordinates, out int cellIndex)
    {
        int z = coordinates.Z;
        int x = coordinates.X + z / 2;
        if (z < 0 || z >= CellCountZ || x < 0 || x >= CellCountX)
        {
            cellIndex = -1;
            return false;
        }
        cellIndex = x + z * CellCountX;
        return true;
    }

    public int GetCellIndex(int xOffset, int zOffset) =>
        xOffset + zOffset * CellCountX;

    public HexCell GetCell(Vector3 position, HexCell stickyCell = default)
    {
        // 射线命中点是全局坐标，而 CellPositions / HexCoordinates.FromPosition 都基于 HexGrid 本地空间。
        position = ToLocal(position);

        if (stickyCell)
        {
            Vector3 delta = position - stickyCell.Position;
            if (delta.X * delta.X + delta.Z * delta.Z < HexMetrics.StickyRadius * HexMetrics.StickyRadius)
            {
                return stickyCell;
            }
        }

        HexCoordinates coordinates = HexCoordinates.FromPosition(position);
        return GetCell(coordinates);
    }

    public HexCell GetCell(int index) =>
        index >= 0 && index < CellData.Length ? new HexCell(index, this) : default;

    public HexCell GetCell(int x, int z)
    {
        if (x < 0 || x >= CellCountX || z < 0 || z >= CellCountZ) return default;
        return new HexCell(x + z * CellCountX, this);
    }

    public void RefreshChunks()
    {
        if (_chunks == null) return;
        foreach (var chunk in _chunks)
        {
            chunk?.Refresh();
        }
    }

    /// <summary>Part 3.4.0: 刷新单个 cell 所在的 chunk。</summary>
    public void RefreshCell(int cellIndex) =>
        _cellChunks[cellIndex]?.Refresh();

    /// <summary>Part 3.4.0: 刷新 cell 自身、相邻不同 chunk 的邻居，以及所在 unit 的位置。</summary>
    public void RefreshCellWithDependents(int cellIndex)
    {
        HexGridChunk chunk = _cellChunks[cellIndex];
        chunk?.Refresh();
        HexCoordinates coordinates = CellData[cellIndex].coordinates;
        for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
        {
            if (TryGetCellIndex(coordinates.Step(d), out int neighborIndex))
            {
                HexGridChunk neighborChunk = _cellChunks[neighborIndex];
                if (chunk != neighborChunk)
                {
                    neighborChunk?.Refresh();
                }
            }
        }
        CellUnits[cellIndex]?.ValidateLocation();
    }

    /// <summary>Part 3.4.0: 根据高程重新计算 cell 的世界位置，并同步 Label/Highlight。</summary>
    public void RefreshCellPosition(int cellIndex)
    {
        Vector3 position = CellPositions[cellIndex];
        position.Y = CellData[cellIndex].Elevation * HexMetrics.ElevationStep;
        position.Y += (HexMetrics.SampleNoise(position).Y * 2f - 1f) * HexMetrics.ElevationPerturbStrength;
        CellPositions[cellIndex] = position;

        if (_cellLabels[cellIndex] != null)
        {
            _cellLabels[cellIndex].Position = position + new Vector3(0f, 4.0f, 0f);
        }
        if (_cellHighlights[cellIndex] != null)
        {
            _cellHighlights[cellIndex].Position = position + new Vector3(0f, 0.05f, 0f);
        }
    }

    /// <summary>Part 3.4.0: 生成地图后统一刷新所有 cell 的位置、地形和可见性。</summary>
    public void RefreshAllCells()
    {
        for (int i = 0; i < CellData.Length; i++)
        {
            SearchData[i].searchPhase = 0;
            RefreshCellPosition(i);
            ShaderData.RefreshTerrain(i);
            ShaderData.RefreshVisibility(i);
        }
    }

    private void SetLabel(int cellIndex, string text)
    {
        if (_cellLabels[cellIndex] != null)
        {
            _cellLabels[cellIndex].Text = text ?? "";
        }
    }

    private void EnableHighlight(int cellIndex, Color color)
    {
        if (_cellHighlights[cellIndex] != null)
        {
            var mat = _cellHighlights[cellIndex].MaterialOverride as ShaderMaterial;
            mat?.SetShaderParameter("color", color);
            _cellHighlights[cellIndex].Visible = true;
        }
    }

    private void DisableHighlight(int cellIndex)
    {
        if (_cellHighlights[cellIndex] != null)
        {
            _cellHighlights[cellIndex].Visible = false;
        }
    }

    // ==================== Part 12: Save / Load ====================

    public void Save(System.IO.BinaryWriter writer)
    {
        writer.Write(_cellCountX);
        writer.Write(_cellCountZ);
        writer.Write(Wrapping); // Part 27

        for (int i = 0; i < CellData.Length; i++)
        {
            CellData[i].values.Save(writer);
            CellData[i].flags.Save(writer);
        }

        writer.Write(_units.Count);
        for (int i = 0; i < _units.Count; i++)
        {
            _units[i].Save(writer);
        }
    }

    public void Load(System.IO.BinaryReader reader, int header)
    {
        StopSearch();
        int x = 20, z = 15;
        bool wrapping = false;
        if (header >= 1)
        {
            x = reader.ReadInt32();
            z = reader.ReadInt32();
        }
        if (header >= 5) // Part 27
        {
            wrapping = reader.ReadBoolean();
        }
        if (x != _cellCountX || z != _cellCountZ || wrapping != Wrapping)
        {
            if (!CreateMap(x, z, wrapping))
            {
                return;
            }
        }

        bool originalImmediateMode = _cellShaderData.ImmediateMode;
        _cellShaderData.ImmediateMode = true;

        for (int i = 0; i < CellData.Length; i++)
        {
            HexCellData data = CellData[i];
            data.values = HexValues.Load(reader, header);
            data.flags = data.flags.Load(reader, header);
            CellData[i] = data;
            RefreshCellPosition(i);
            _cellShaderData.RefreshTerrain(i);
            _cellShaderData.RefreshVisibility(i);
        }
        for (int i = 0; i < _chunks.Length; i++)
        {
            _chunks[i].Refresh();
        }

        if (header >= 2)
        {
            int unitCount = reader.ReadInt32();
            for (int i = 0; i < unitCount; i++)
            {
                HexUnit.Load(reader, this);
            }
        }

        _cellShaderData.ImmediateMode = originalImmediateMode;

        HexCamera.CenterOnGrid();
        HexCamera.ValidatePosition();
    }

    // ==================== Part 18: 单位管理 ====================

    public void AddUnit(HexUnit unit, HexCell location, float orientation)
    {
        _units.Add(unit);
        AddChild(unit);
        unit.Grid = this;
        unit.Location = location;
        unit.Orientation = orientation;
    }

    public void RemoveUnit(HexUnit unit)
    {
        _units.Remove(unit);
        unit.Die();
    }

    private void ClearUnits()
    {
        for (int i = 0; i < _units.Count; i++)
        {
            _units[i].Die();
        }
        _units.Clear();
    }

    // ==================== Part 20: 视野系统 ====================

    public void IncreaseVisibility(HexCell fromCell, int range)
    {
        var cells = GetVisibleCells(fromCell, range);
        for (int i = 0; i < cells.Count; i++)
        {
            int index = cells[i].Index;
            if (++_cellVisibility[index] == 1)
            {
                HexCell c = cells[i];
                c.Flags = c.Flags.With(HexFlags.Explored);
                _cellShaderData.RefreshVisibility(index);
            }
        }
    }

    public void DecreaseVisibility(HexCell fromCell, int range)
    {
        var cells = GetVisibleCells(fromCell, range);
        for (int i = 0; i < cells.Count; i++)
        {
            int index = cells[i].Index;
            if (--_cellVisibility[index] == 0)
            {
                _cellShaderData.RefreshVisibility(index);
            }
        }
    }

    private System.Collections.Generic.List<HexCell> GetVisibleCells(HexCell fromCell, int range)
    {
        var visibleCells = new System.Collections.Generic.List<HexCell>();

        _searchFrontierPhase += 2;
        if (_searchFrontier == null)
            _searchFrontier = new HexCellPriorityQueue(this);
        else
            _searchFrontier.Clear();

        range += fromCell.Values.ViewElevation;
        HexCoordinates fromCoordinates = fromCell.Coordinates;
        int fromIndex = fromCell.Index;
        _searchData[fromIndex].searchPhase = _searchFrontierPhase;
        _searchData[fromIndex].distance = 0;
        _searchFrontier.Enqueue(fromIndex);

        while (_searchFrontier.TryDequeue(out int currentIndex))
        {
            var current = new HexCell(currentIndex, this);
            _searchData[currentIndex].searchPhase += 1;
            visibleCells.Add(current);

            if (_searchData[currentIndex].distance < range)
            {
                for (int i = 0; i < 6; i++)
                {
                    HexDirection d = (HexDirection)i;
                    if (
                        !current.TryGetNeighbor(d, out HexCell neighbor) ||
                        _searchData[neighbor.Index].searchPhase > _searchFrontierPhase
                    )
                    {
                        continue;
                    }
                    if (neighbor.Flags.HasNone(HexFlags.Explorable))
                    {
                        continue;
                    }

                    int distance = _searchData[currentIndex].distance + 1;
                    if (distance + neighbor.Values.ViewElevation > range)
                    {
                        continue;
                    }
                    if (distance > fromCoordinates.DistanceTo(neighbor.Coordinates))
                    {
                        continue;
                    }

                    if (_searchData[neighbor.Index].searchPhase < _searchFrontierPhase)
                    {
                        _searchData[neighbor.Index].searchPhase = _searchFrontierPhase;
                        _searchData[neighbor.Index].distance = distance;
                        _searchData[neighbor.Index].heuristic = 0;
                        _searchFrontier.Enqueue(neighbor.Index);
                    }
                }
            }
        }
        return visibleCells;
    }

    public void ResetVisibility()
    {
        for (int i = 0; i < _cellVisibility.Length; i++)
        {
            if (_cellVisibility[i] > 0)
            {
                _cellVisibility[i] = 0;
                _cellShaderData.RefreshVisibility(i);
            }
        }
        for (int i = 0; i < _units.Count; i++)
        {
            IncreaseVisibility(_units[i].Location, HexUnit.VisionRange);
        }
    }

    private void CreateUnitUnderCursor()
    {
        var cell = RaycastToCell(GetViewport().GetMousePosition());
        if (cell && cell.Unit == null)
        {
            var unit = new HexUnit();
            AddUnit(unit, cell, GD.Randf() * 360f);
        }
    }

    private void DestroyUnitUnderCursor()
    {
        var cell = RaycastToCell(GetViewport().GetMousePosition());
        if (cell && cell.Unit != null)
        {
            RemoveUnit(cell.Unit);
        }
    }

    // ==================== 鼠标交互（运行时） ====================

    private Vector2? _clickAnchor;
    private const float ClickDragThreshold = 10f;

    // Part 6: 拖拽绘制河流
    private HexCell _previousCell;
    private HexCell _lastEditedCell;
    private HexDirection _dragDirection;
    private bool _isDrag;

    /* Part 17: A* 搜索优先队列 + 阶段计数 + 当前路径 */
    private HexCellPriorityQueue _searchFrontier;
    private int _searchFrontierPhase;
    private HexCell _searchFromCell;
    private HexCell _searchToCell;
    private int _currentPathFromIndex = -1;
    private int _currentPathToIndex = -1;
    private bool _currentPathExists;

    public bool HasPath => _currentPathExists;

    /* Part 18: 单位列表 */
    private System.Collections.Generic.List<HexUnit> _units = new System.Collections.Generic.List<HexUnit>();

    /* Part 20: Cell Data Texture */
    private HexCellShaderData _cellShaderData;
    public HexCellShaderData ShaderData => _cellShaderData;

    public override void _Input(InputEvent @event)
    {
        if (Engine.IsEditorHint()) return;

        // 键盘响应放在 _Input（不需要等 UI）
        if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.Keycode == Key.Tab)
            {
                BrushModeEnabled = !BrushModeEnabled;
                if (!BrushModeEnabled && _brushPreview != null)
                {
                    _brushPreview.Visible = false;
                }
                GD.Print($"[HexGrid] BrushModeEnabled = {BrushModeEnabled}");
            }
            else if (keyEvent.Keycode == Key.R)
            {
                RiverMode = RiverMode.Next();
                GD.Print($"[HexGrid] RiverMode = {RiverMode}");
            }
            else if (keyEvent.Keycode == Key.T)
            {
                RoadMode = RoadMode.Next();
                GD.Print($"[HexGrid] RoadMode = {RoadMode}");
            }
            else if (keyEvent.Keycode == Key.Y)
            {
                WalledMode = WalledMode.Next();
                GD.Print($"[HexGrid] WalledMode = {WalledMode}");
            }
            else if (keyEvent.Keycode == Key.F1)
            {
                foreach (var chunk in _chunks)
                {
                    chunk?.ToggleWireframe();
                }
            }
            else if (keyEvent.Keycode == Key.U)
            {
                if (keyEvent.ShiftPressed)
                {
                    DestroyUnitUnderCursor();
                }
                else
                {
                    CreateUnitUnderCursor();
                }
            }
            else if (keyEvent.Keycode == Key.V)
            {
                DebugAlwaysVisible = !DebugAlwaysVisible;
            }
        }

    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Engine.IsEditorHint()) return;

        if (@event is InputEventMouseButton mouseButton)
        {

            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    _clickAnchor = mouseButton.Position;
                    _previousCell = default;
                    _lastEditedCell = default;
                    _isDrag = false;

                    if (HexMapEditor.EditMode)
                    {
                        var cell = RaycastToCell(mouseButton.Position);
                        if (cell)
                        {
                            EditCells(cell, false);
                            _lastEditedCell = cell;
                            _previousCell = cell;
                        }
                    }
                }
                else if (_clickAnchor.HasValue)
                {
                    var cell = RaycastToCell(mouseButton.Position, HexMapEditor.EditMode ? _previousCell : default);
                    if (!cell)
                    {
                        _clickAnchor = null;
                        _previousCell = default;
                        _isDrag = false;
                        return;
                    }
                    /* Part 16: 编辑模式 / 路径查找分支 */
                    if (HexMapEditor.EditMode)
                    {
                        if (!_isDrag &&
                            _clickAnchor.Value.DistanceTo(mouseButton.Position) < ClickDragThreshold &&
                            cell != _lastEditedCell)
                        {
                            EditCells(cell, false);
                        }
                    }
                    else
                    {
                        if (Input.IsKeyPressed(Key.Shift) && _searchToCell != cell)
                        {
                            if (_searchFromCell != cell)
                            {
                                if (_searchFromCell) DisableHighlight(_searchFromCell.Index);
                                _searchFromCell = cell;
                                EnableHighlight(_searchFromCell.Index, Godot.Colors.Blue);
                                if (_searchToCell)
                                {
                                    FindPath(_searchFromCell, _searchToCell, 24);
                                }
                            }
                        }
                        else if (_searchFromCell && _searchFromCell != cell)
                        {
                            if (_searchToCell != cell)
                            {
                                _searchToCell = cell;
                                FindPath(_searchFromCell, _searchToCell, 24);
                            }
                        }
                    }
                    _clickAnchor = null;
                    _previousCell = default;
                    _isDrag = false;
                }
            }
            else if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                if (mouseButton.Pressed)
                {
                    _clickAnchor = mouseButton.Position;
                }
                else if (_clickAnchor.HasValue)
                {
                    if (_clickAnchor.Value.DistanceTo(mouseButton.Position) < ClickDragThreshold)
                    {
                        if (HexMapEditor.EditMode)
                        {
                            var cell = RaycastToCell(mouseButton.Position, _previousCell);
                            if (cell) EditCells(cell, true);
                        }
                        else if (_currentPathExists && _currentPathFromIndex >= 0 && GetCell(_currentPathFromIndex).Unit != null)
                        {
                            var path = GetPath();
                            if (path != null)
                            {
                                GetCell(_currentPathFromIndex).Unit.Travel(path);
                            }
                            ClearPath();
                            _searchFromCell = default;
                            _searchToCell = default;
                        }
                    }
                    _clickAnchor = null;
                }
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            // Part 6: 检测拖拽方向，并在编辑模式下持续绘制（河流/道路/笔刷）
            if (motion.ButtonMask.HasFlag(MouseButtonMask.Left))
            {
                // 没有 _clickAnchor 说明按下发生在 UI 上，跳过拖拽
                if (!_clickAnchor.HasValue) return;

                var cell = RaycastToCell(motion.Position, _previousCell);
                if (cell && cell != _previousCell)
                {
                    if (_previousCell)
                    {
                        _isDrag = false;
                        // 通过邻居查找确定方向，避免坐标映射不一致
                        HexCell previous = _previousCell;
                        for (int i = 0; i < 6; i++)
                        {
                            HexDirection d = (HexDirection)i;
                            if (previous.GetNeighbor(d) == cell)
                            {
                                _isDrag = true;
                                _dragDirection = d;
                                break;
                            }
                        }
                    }
                    else
                    {
                        _isDrag = false;
                    }

                    if (HexMapEditor.EditMode)
                    {
                        EditCells(cell, false);
                    }

                    _previousCell = cell;
                }
            }
        }
    }

    /// <summary>获取笔刷范围内的所有 Cell（包含 null）</summary>
    public System.Collections.Generic.List<HexCell> GetBrushCells(HexCell center)
    {
        var result = new System.Collections.Generic.List<HexCell>();
        if (!center) return result;

        int centerX = center.Coordinates.X;
        int centerZ = center.Coordinates.Z;

        for (int r = 0, z = centerZ - _brushSize; z <= centerZ; z++, r++)
        {
            for (int x = centerX - r; x <= centerX + _brushSize; x++)
            {
                result.Add(GetCell(new HexCoordinates(x, z)));
            }
        }
        for (int r = 0, z = centerZ + _brushSize; z > centerZ; z--, r++)
        {
            for (int x = centerX - _brushSize; x <= centerX + r; x++)
            {
                result.Add(GetCell(new HexCoordinates(x, z)));
            }
        }
        return result;
    }

    private void EditCells(HexCell center, bool isRightClick)
    {
        foreach (var cell in GetBrushCells(center))
        {
            EditCell(cell, isRightClick);
        }
    }

    private void EditCell(HexCell cell, bool isRightClick)
    {
        if (!cell) return;
        if (ApplyElevation)
        {
            cell.SetElevation(cell.Values.Elevation + (isRightClick ? -ActiveElevation : ActiveElevation));
        }
        if (ApplyWaterLevel)
        {
            cell.SetWaterLevel(cell.Values.WaterLevel + (isRightClick ? -ActiveWaterLevel : ActiveWaterLevel));
        }
        if (ActiveTerrainTypeIndex >= 0 && !isRightClick)
        {
            cell.SetTerrainTypeIndex(ActiveTerrainTypeIndex);
        }

        // Part 6: 河流编辑
        if (RiverMode == OptionalToggle.No)
        {
            cell.RemoveRiver();
            GD.Print($"[HexGrid] RemoveRiver at {cell.Coordinates}");
        }
        else if (_isDrag && RiverMode == OptionalToggle.Yes && !isRightClick)
        {
            if (cell.TryGetNeighbor(_dragDirection.Opposite(), out HexCell otherCell))
            {
                otherCell.SetOutgoingRiver(_dragDirection);
            }
        }

        /* Part 7: 道路编辑 */
        if (RoadMode == OptionalToggle.No)
        {
            cell.RemoveRoads();
            GD.Print($"[HexGrid] RemoveRoads at {cell.Coordinates}");
        }
        else if (_isDrag && RoadMode == OptionalToggle.Yes && !isRightClick)
        {
            if (cell.TryGetNeighbor(_dragDirection.Opposite(), out HexCell otherCell))
            {
                GD.Print($"[HexGrid] AddRoad: from={otherCell.Coordinates}, dir={_dragDirection}, to={cell.Coordinates}");
                otherCell.AddRoad(_dragDirection);
            }
        }

        /* Part 9: 设置地形特征级别（按 apply toggle 控制） */
        if (ApplyUrbanLevel) cell.SetUrbanLevel(ActiveUrbanLevel);
        if (ApplyFarmLevel) cell.SetFarmLevel(ActiveFarmLevel);
        if (ApplyPlantLevel) cell.SetPlantLevel(ActivePlantLevel);

        /* Part 10: 设置城墙 */
        if (WalledMode != OptionalToggle.Ignore)
        {
            cell.SetWalled(WalledMode == OptionalToggle.Yes);
        }

        /* Part 11: 设置特殊特征（按 apply toggle 控制） */
        if (ApplySpecialIndex) cell.SetSpecialIndex(ActiveSpecialIndex);
    }

    /// <summary>控制所有 Chunk 的 Cell Label 显示/隐藏</summary>
    private bool _showLabels = false;

    public void ShowLabels(bool visible)
    {
        _showLabels = visible;
        if (_chunks == null) return;
        foreach (var chunk in _chunks)
        {
            chunk.ShowLabels(visible);
        }
    }

    // ==================== Part 17: A* 路径查找（有限移动） ====================

    public void FindPath(HexCell fromCell, HexCell toCell, int speed)
    {
        ClearPath();
        _currentPathFromIndex = fromCell.Index;
        _currentPathToIndex = toCell.Index;
        _currentPathExists = Search(fromCell, toCell, speed);
        ShowPath(speed);
    }

    public new System.Collections.Generic.List<int> GetPath()
    {
        if (!_currentPathExists) return null;
        var path = new System.Collections.Generic.List<int>();
        int currentIndex = _currentPathToIndex;
        while (currentIndex != _currentPathFromIndex)
        {
            path.Add(currentIndex);
            currentIndex = _searchData[currentIndex].pathFrom;
        }
        path.Add(_currentPathFromIndex);
        path.Reverse();
        return path;
    }

    public void StopSearch()
    {
        ClearPath();
        _searchFromCell = default;
        _searchToCell = default;
    }

    private void ShowPath(int speed)
    {
        if (_currentPathExists)
        {
            int currentIndex = _currentPathToIndex;
            while (currentIndex != _currentPathFromIndex)
            {
                int turn = (_searchData[currentIndex].distance - 1) / speed;
                SetLabel(currentIndex, turn.ToString());
                EnableHighlight(currentIndex, Godot.Colors.White);
                currentIndex = _searchData[currentIndex].pathFrom;
            }
        }
        EnableHighlight(_currentPathFromIndex, Godot.Colors.Blue);
        EnableHighlight(_currentPathToIndex, Godot.Colors.Red);
    }

    private void ClearPath()
    {
        if (_currentPathExists)
        {
            int currentIndex = _currentPathToIndex;
            while (currentIndex != _currentPathFromIndex)
            {
                SetLabel(currentIndex, null);
                DisableHighlight(currentIndex);
                currentIndex = _searchData[currentIndex].pathFrom;
            }
            SetLabel(currentIndex, null);
            DisableHighlight(currentIndex);
            _currentPathExists = false;
        }
        else if (_currentPathFromIndex >= 0)
        {
            DisableHighlight(_currentPathFromIndex);
            DisableHighlight(_currentPathToIndex);
        }
        _currentPathFromIndex = _currentPathToIndex = -1;
    }

    private bool Search(HexCell fromCell, HexCell toCell, int speed)
    {
        _searchFrontierPhase += 2;

        if (_searchFrontier == null)
            _searchFrontier = new HexCellPriorityQueue(this);
        else
            _searchFrontier.Clear();

        int fromIndex = fromCell.Index;
        _searchData[fromIndex].searchPhase = _searchFrontierPhase;
        _searchData[fromIndex].distance = 0;
        _searchFrontier.Enqueue(fromIndex);

        while (_searchFrontier.TryDequeue(out int currentIndex))
        {
            var current = new HexCell(currentIndex, this);
            _searchData[currentIndex].searchPhase += 1;

            if (current == toCell)
            {
                return true;
            }

            int currentTurn = (_searchData[currentIndex].distance - 1) / speed;

            for (int i = 0; i < 6; i++)
            {
                HexDirection d = (HexDirection)i;
                if (
                    !current.TryGetNeighbor(d, out HexCell neighbor) ||
                    _searchData[neighbor.Index].searchPhase > _searchFrontierPhase
                )
                {
                    continue;
                }
                if (neighbor.IsUnderwater || neighbor.Unit != null) continue;
                if (current.GetEdgeType(neighbor) == HexEdgeType.Cliff) continue;

                int moveCost;
                if (current.HasRoadThroughEdge(d))
                {
                    moveCost = 1;
                }
                else if (current.Walled != neighbor.Walled)
                {
                    continue;
                }
                else
                {
                    moveCost = current.GetEdgeType(neighbor) == HexEdgeType.Flat ? 5 : 10;
                    moveCost += neighbor.UrbanLevel + neighbor.FarmLevel + neighbor.PlantLevel;
                }

                int distance = _searchData[currentIndex].distance + moveCost;
                int turn = (distance - 1) / speed;
                if (turn > currentTurn)
                {
                    distance = turn * speed + moveCost;
                }

                int neighborIndex = neighbor.Index;
                if (_searchData[neighborIndex].searchPhase < _searchFrontierPhase)
                {
                    _searchData[neighborIndex].searchPhase = _searchFrontierPhase;
                    _searchData[neighborIndex].distance = distance;
                    _searchData[neighborIndex].pathFrom = currentIndex;
                    _searchData[neighborIndex].heuristic = neighbor.Coordinates.DistanceTo(toCell.Coordinates);
                    _searchFrontier.Enqueue(neighborIndex);
                }
                else if (distance < _searchData[neighborIndex].distance)
                {
                    int oldPriority = _searchData[neighborIndex].SearchPriority;
                    _searchData[neighborIndex].distance = distance;
                    _searchData[neighborIndex].pathFrom = currentIndex;
                    _searchFrontier.Change(neighborIndex, oldPriority);
                }
            }
        }
        return false;
    }

    // ==================== 笔刷预览 ====================

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint()) return;

        /* Part 21-22: 批量更新 cell data texture（支持过渡动画） */
        _cellShaderData?.UpdateTexture(delta);

        var mousePos = GetViewport().GetMousePosition();
        var cell = RaycastToCell(mousePos, HexMapEditor.EditMode ? _previousCell : default);

        /* Part 2.2: 编辑模式下更新 terrain 笔刷高亮 */
        if (HexMapEditor.EditMode)
        {
            HexMapEditor.UpdateCellHighlightData(cell, BrushSize, HexMetrics.wrapSize);
        }
        else
        {
            HexMapEditor.ClearCellHighlightData();
        }

        if (!BrushModeEnabled || _brushPreview == null)
        {
            if (_brushPreview != null) _brushPreview.Visible = false;
            return;
        }
        if (cell)
        {
            BuildBrushPreview(cell);
            _brushPreview.Visible = true;
        }
        else
        {
            _brushPreview.Visible = false;
        }
    }

    private void BuildBrushPreview(HexCell centerCell)
    {
        var cells = GetBrushCells(centerCell);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        Color baseColor = ActiveTerrainTypeIndex >= 0 && ActiveTerrainTypeIndex < Colors.Length
            ? Colors[ActiveTerrainTypeIndex]
            : new Color(0.3f, 1f, 0.3f);
        Color c = new Color(baseColor.R, baseColor.G, baseColor.B, 0.4f);

        foreach (var cell in cells)
        {
            if (!cell) continue;
            Vector3 center = cell.Position;
            center.Y += 0.15f;

            for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
            {
                Vector3 v1 = center + HexMetrics.GetFirstSolidCorner(d);
                v1.Y = center.Y;
                Vector3 v2 = center + HexMetrics.GetSecondSolidCorner(d);
                v2.Y = center.Y;

                st.SetColor(c); st.AddVertex(center);
                st.SetColor(c); st.AddVertex(v2);
                st.SetColor(c); st.AddVertex(v1);
            }
        }

        st.GenerateNormals();
        _brushPreview.Mesh = st.Commit();
    }

    private HexCell RaycastToCell(Vector2 screenPosition, HexCell stickyCell = default)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera == null)
        {
            GD.PrintErr("[HexGrid] Camera3D is null! Make sure a Camera3D has current=true.");
            return default;
        }

        var from = camera.ProjectRayOrigin(screenPosition);
        var to = from + camera.ProjectRayNormal(screenPosition) * 1000f;

        /* Part 27: 用物理射线命中地形 mesh 碰撞体。碰撞体已开启 BackfaceCollision，
           避免 Godot 默认只认正面导致从上方射入 miss。 */
        var space = GetWorld3D()?.DirectSpaceState;
        if (space != null)
        {
            var query = PhysicsRayQueryParameters3D.Create(from, to);
            query.CollisionMask = 2;
            var result = space.IntersectRay(query);
            if (result != null && result.Count > 0 && result.ContainsKey("position"))
            {
                Vector3 hit = (Vector3)result["position"];
                return GetCell(hit, stickyCell);
            }
        }

        /* fallback：无碰撞体时仍用 Y=0 平面相交 */
        if (Mathf.Abs(to.Y - from.Y) < 0.001f) return default;
        float t = -from.Y / (to.Y - from.Y);
        if (t < 0) return default;

        Vector3 planeHit = from + (to - from) * t;
        return GetCell(planeHit, stickyCell);
    }
}
