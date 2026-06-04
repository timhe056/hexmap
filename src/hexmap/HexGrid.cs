using Godot;

namespace HexMap;

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

    // ==================== 内部状态 ====================

    private bool _isReady = false;
    private HexCell[] _cells;
    private HexGridChunk[] _chunks;

    private int CellCountX => _chunkCountX * HexMetrics.ChunkSizeX;
    private int CellCountZ => _chunkCountZ * HexMetrics.ChunkSizeZ;

    // ==================== 生命周期 ====================

    public override void _Ready()
    {
        HexMetrics.InitializeNoise();
        _isReady = true;
        Regenerate();
    }

    // ==================== 网格生成 ====================

    private void Regenerate()
    {
        ClearChunks();
        CreateChunks();
        CreateCells();
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

    public override void _Input(InputEvent @event)
    {
        if (Engine.IsEditorHint()) return;

        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
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
                        if (cell != null) EditCells(cell, 1);
                    }
                    _clickAnchor = null;
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
                        if (cell != null) EditCells(cell, -1);
                    }
                    _clickAnchor = null;
                }
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            // 拖拽过程中取消点击判定
            if (_clickAnchor.HasValue && _clickAnchor.Value.DistanceTo(motion.Position) >= ClickDragThreshold)
            {
                _clickAnchor = null;
            }
        }
    }

    private void EditCells(HexCell center, int delta)
    {
        int centerX = center.Coordinates.X;
        int centerZ = center.Coordinates.Z;

        for (int r = 0, z = centerZ - _brushSize; z <= centerZ; z++, r++)
        {
            for (int x = centerX - r; x <= centerX + _brushSize; x++)
            {
                EditCell(GetCell(new HexCoordinates(x, z)), delta);
            }
        }
        for (int r = 0, z = centerZ + _brushSize; z > centerZ; z--, r++)
        {
            for (int x = centerX - _brushSize; x <= centerX + r; x++)
            {
                EditCell(GetCell(new HexCoordinates(x, z)), delta);
            }
        }
    }

    private void EditCell(HexCell cell, int delta)
    {
        if (cell == null) return;
        cell.Elevation = Mathf.Max(0, cell.Elevation + delta);
        GD.Print($"[HexGrid] Cell {cell.Coordinates} elevation = {cell.Elevation}");
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
        var cell = GetCell(hit);
        if (cell == null)
        {
            GD.Print($"[HexGrid] No cell at hit position {hit}");
        }
        return cell;
    }
}
