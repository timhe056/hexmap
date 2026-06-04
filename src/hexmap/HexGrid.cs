using Godot;

namespace HexMap;

/// <summary>Part 6：三态开关（忽略 / 是 / 否）</summary>
public enum OptionalToggle { Ignore, Yes, No }

/// <summary>
/// Part 5：六边形网格管理器。
/// 负责创建 Chunk、分配 Cell、响应 Inspector 参数变化。
/// 三角化逻辑已完全移至 HexMeshBuilder。
/// </summary>
[Tool]
public partial class HexGrid : Node3D
{
    // ==================== Inspector 可调参数 ====================

    [Export(PropertyHint.Range, "1,50,1")]
    public int ChunkCountX
    {
        get => _chunkCountX;
        set
        {
            _chunkCountX = Mathf.Max(1, value);
            if (_isReady) Regenerate();
        }
    }
    private int _chunkCountX = 4;

    [Export(PropertyHint.Range, "1,50,1")]
    public int ChunkCountZ
    {
        get => _chunkCountZ;
        set
        {
            _chunkCountZ = Mathf.Max(1, value);
            if (_isReady) Regenerate();
        }
    }
    private int _chunkCountZ = 3;

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
    private Color _gridColor = new Color(1f, 0.85f, 0.55f);

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

    [Export(PropertyHint.Range, "0,4,1")]
    public int BrushSize
    {
        get => _brushSize;
        set => _brushSize = value;
    }
    private int _brushSize = 0;

    /// <summary>当前要高程设置的目标值（UI 滑块控制）</summary>
    public int ActiveElevation { get; set; } = 0;

    /// <summary>是否应用颜色编辑</summary>
    public bool ApplyColor { get; set; } = false;

    /// <summary>是否应用高程编辑</summary>
    public bool ApplyElevation { get; set; } = true;

    /// <summary>当前颜色索引，-1 表示不涂色</summary>
    public int ActiveColorIndex { get; set; } = -1;

    /// <summary>笔刷模式开关：开启时鼠标移动显示笔刷范围预览</summary>
    public bool BrushModeEnabled { get; set; } = false;

    /// <summary>Part 6：河流编辑模式</summary>
    public OptionalToggle RiverMode { get; set; } = OptionalToggle.Ignore;

    /// <summary>地形颜色预设（自然常用色）</summary>
    public static readonly Color[] TerrainColors = new[] {
        new Color(0.90f, 0.85f, 0.55f), // 沙色
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
    private HexCell[] _cells;
    private HexGridChunk[] _chunks;
    private MeshInstance3D _brushPreview;

    private int CellCountX => _chunkCountX * HexMetrics.ChunkSizeX;
    private int CellCountZ => _chunkCountZ * HexMetrics.ChunkSizeZ;

    // ==================== 生命周期 ====================

    public override void _Ready()
    {
        HexMetrics.InitializeNoise();
        EnsureBrushPreview();
        _isReady = true;
        Regenerate();
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

    private void Regenerate()
    {
        ClearChunks();
        CreateChunks();
        CreateCells();
        // 初始化完成后立即三角化所有 chunk（避免运行时延迟一帧）
        if (_chunks != null)
        {
            foreach (var chunk in _chunks)
            {
                chunk?.Refresh(immediate: true);
            }
        }
    }

    private void ClearChunks()
    {
        var oldMesh = GetNodeOrNull<MeshInstance3D>("HexMesh");
        if (oldMesh != null)
        {
            oldMesh.QueueFree();
        }

        foreach (var child in GetChildren())
        {
            if (child is HexGridChunk)
            {
                child.QueueFree();
            }
        }
        _chunks = null;
        _cells = null;
    }

    private void CreateChunks()
    {
        _chunks = new HexGridChunk[_chunkCountX * _chunkCountZ];
        for (int z = 0, i = 0; z < _chunkCountZ; z++)
        {
            for (int x = 0; x < _chunkCountX; x++)
            {
                var chunk = new HexGridChunk();
                chunk.Name = $"Chunk_{x}_{z}";
                AddChild(chunk);
                if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
                {
                    chunk.Owner = GetTree().EditedSceneRoot;
                }
                _chunks[i++] = chunk;
            }
        }
    }

    private void CreateCells()
    {
        _cells = new HexCell[CellCountZ * CellCountX];
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
        position.X = (x + z * 0.5f - z / 2) * (HexMetrics.InnerRadius * 2f);
        position.Y = 0f;
        position.Z = z * (HexMetrics.OuterRadius * 1.5f);

        Color randomColor = GetRandomColor(x, z);

        HexCell cell = new HexCell
        {
            Coordinates = HexCoordinates.FromOffsetCoordinates(x, z),
            BasePosition = position,
            Color = randomColor,
        };

        _cells[i] = cell;

        if (x > 0)
        {
            cell.SetNeighbor(HexDirection.W, _cells[i - 1]);
        }
        if (z > 0)
        {
            if ((z & 1) == 0)
            {
                cell.SetNeighbor(HexDirection.SE, _cells[i - CellCountX]);
                if (x > 0)
                {
                    cell.SetNeighbor(HexDirection.SW, _cells[i - CellCountX - 1]);
                }
            }
            else
            {
                cell.SetNeighbor(HexDirection.SW, _cells[i - CellCountX]);
                if (x < CellCountX - 1)
                {
                    cell.SetNeighbor(HexDirection.SE, _cells[i - CellCountX + 1]);
                }
            }
        }

        cell.Elevation = DefaultElevation;
        AddCellToChunk(x, z, cell);
    }

    private void AddCellToChunk(int x, int z, HexCell cell)
    {
        int chunkX = x / HexMetrics.ChunkSizeX;
        int chunkZ = z / HexMetrics.ChunkSizeZ;
        HexGridChunk chunk = _chunks[chunkX + chunkZ * _chunkCountX];

        int localX = x - chunkX * HexMetrics.ChunkSizeX;
        int localZ = z - chunkZ * HexMetrics.ChunkSizeZ;
        chunk.AddCell(localX + localZ * HexMetrics.ChunkSizeX, cell);
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
        int z = coordinates.Z;
        if (z < 0 || z >= CellCountZ) return null;
        int x = coordinates.X + z / 2;
        if (x < 0 || x >= CellCountX) return null;
        return _cells[x + z * CellCountX];
    }

    public HexCell GetCell(Vector3 position)
    {
        HexCoordinates coordinates = HexCoordinates.FromPosition(position);
        return GetCell(coordinates);
    }

    public HexMapData Save()
    {
        var data = new HexMapData
        {
            Width = CellCountX,
            Height = CellCountZ,
            Seed = 0
        };
        return data;
    }

    public void Load(HexMapData data)
    {
        if (data == null) return;
        _chunkCountX = Mathf.CeilToInt((float)data.Width / HexMetrics.ChunkSizeX);
        _chunkCountZ = Mathf.CeilToInt((float)data.Height / HexMetrics.ChunkSizeZ);
        Regenerate();
    }

    /// <summary>基于坐标生成伪随机颜色</summary>
    private static Color GetRandomColor(int x, int z)
    {
        float hue = ((x * 7 + z * 13) % 360) / 360f;
        return Color.FromHsv(hue, 0.6f, 0.9f);
    }

    // ==================== 鼠标交互（运行时） ====================

    private Vector2? _clickAnchor;
    private const float ClickDragThreshold = 10f;

    // Part 6: 拖拽绘制河流
    private HexCell _previousCell;
    private HexDirection _dragDirection;
    private bool _isDrag;

    public override void _Input(InputEvent @event)
    {
        if (Engine.IsEditorHint()) return;

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
        }

        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    _clickAnchor = mouseButton.Position;
                    _previousCell = null;
                    _isDrag = false;
                }
                else if (_clickAnchor.HasValue)
                {
                    var cell = RaycastToCell(mouseButton.Position);
                    if (_isDrag)
                    {
                        // 河流拖拽：使用最后一次检测到的 cell 和方向
                        if (cell != null) EditCells(cell, false);
                    }
                    else if (_clickAnchor.Value.DistanceTo(mouseButton.Position) < ClickDragThreshold)
                    {
                        if (cell != null) EditCells(cell, false);
                    }
                    _clickAnchor = null;
                    _previousCell = null;
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
                        var cell = RaycastToCell(mouseButton.Position);
                        if (cell != null) EditCells(cell, true);
                    }
                    _clickAnchor = null;
                }
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            // 拖拽过程中取消点击判定（河流编辑模式下不取消，因为需要支持长拖拽）
            if (_clickAnchor.HasValue && _clickAnchor.Value.DistanceTo(motion.Position) >= ClickDragThreshold)
            {
                if (RiverMode == OptionalToggle.Ignore)
                {
                    _clickAnchor = null;
                }
            }

            // Part 6: 检测拖拽方向（用于绘制河流）
            if (motion.ButtonMask.HasFlag(MouseButtonMask.Left))
            {
                var cell = RaycastToCell(motion.Position);
                if (cell != null && cell != _previousCell)
                {
                    if (_previousCell != null)
                    {
                        _isDrag = true;
                        _dragDirection = _previousCell.Coordinates.GetNeighborDirection(cell.Coordinates);
                        GD.Print($"[HexGrid] Drag detected: {_previousCell.Coordinates} -> {cell.Coordinates}, direction={_dragDirection}");
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
        if (center == null) return result;

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
        if (cell == null) return;
        if (ApplyElevation)
        {
            cell.Elevation += isRightClick ? -ActiveElevation : ActiveElevation;
        }
        if (ApplyColor && ActiveColorIndex >= 0 && !isRightClick)
        {
            cell.Color = TerrainColors[ActiveColorIndex];
        }

        // Part 6: 河流编辑
        if (RiverMode == OptionalToggle.No)
        {
            cell.RemoveRiver();
            GD.Print($"[HexGrid] RemoveRiver at {cell.Coordinates}");
        }
        else if (_isDrag && RiverMode == OptionalToggle.Yes && !isRightClick)
        {
            HexCell otherCell = cell.GetNeighbor(_dragDirection.Opposite());
            GD.Print($"[HexGrid] EditCell drag: cell={cell.Coordinates}, otherCell={otherCell?.Coordinates}, dir={_dragDirection}, isDrag={_isDrag}");
            if (otherCell != null)
            {
                otherCell.SetOutgoingRiver(_dragDirection);
            }
        }
    }

    /// <summary>控制所有 Chunk 的 Cell Label 显示/隐藏</summary>
    public void ShowLabels(bool visible)
    {
        if (_chunks == null) return;
        foreach (var chunk in _chunks)
        {
            chunk.ShowLabels(visible);
        }
    }

    // ==================== 笔刷预览 ====================

    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint()) return;
        if (!BrushModeEnabled || _brushPreview == null)
        {
            if (_brushPreview != null) _brushPreview.Visible = false;
            return;
        }

        var mousePos = GetViewport().GetMousePosition();
        var cell = RaycastToCell(mousePos);
        if (cell != null)
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

        Color baseColor = ApplyColor && ActiveColorIndex >= 0
            ? TerrainColors[ActiveColorIndex]
            : new Color(0.3f, 1f, 0.3f);
        Color c = new Color(baseColor.R, baseColor.G, baseColor.B, 0.4f);

        foreach (var cell in cells)
        {
            if (cell == null) continue;
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

        if (Mathf.Abs(to.Y - from.Y) < 0.001f) return null;
        float t = -from.Y / (to.Y - from.Y);
        if (t < 0) return null;

        Vector3 hit = from + (to - from) * t;
        return GetCell(hit);
    }
}
