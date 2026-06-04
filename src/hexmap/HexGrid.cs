using Godot;

namespace HexMap;

/// <summary>
/// Part 5：六边形网格管理器。
/// 负责创建 Chunk、分配 Cell、响应 Inspector 参数变化。
/// 三角化逻辑已提取为 public static 供 HexGridChunk 调用。
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
        // 清理旧 Part 遗留的 HexMesh
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

    // ==================== Part 4/5：静态三角化方法 ====================

    public static void TriangulateCell(HexCell cell, SurfaceTool st)
    {
        for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
        {
            Triangulate(st, d, cell);
        }
    }

    public struct EdgeVertices
    {
        public Vector3 v1, v2, v3, v4;

        public EdgeVertices(Vector3 corner1, Vector3 corner2)
        {
            v1 = corner1;
            v2 = corner1.Lerp(corner2, 1f / 3f);
            v3 = corner1.Lerp(corner2, 2f / 3f);
            v4 = corner2;
        }

        public static EdgeVertices TerraceLerp(EdgeVertices a, EdgeVertices b, int step)
        {
            EdgeVertices result;
            result.v1 = HexMetrics.TerraceLerp(a.v1, b.v1, step);
            result.v2 = HexMetrics.TerraceLerp(a.v2, b.v2, step);
            result.v3 = HexMetrics.TerraceLerp(a.v3, b.v3, step);
            result.v4 = HexMetrics.TerraceLerp(a.v4, b.v4, step);
            return result;
        }
    }

    private static Vector3 Perturb(Vector3 position)
    {
        Vector4 sample = HexMetrics.SampleNoise(position);
        position.X += (sample.X * 2f - 1f) * HexMetrics.CellPerturbStrength;
        position.Z += (sample.Z * 2f - 1f) * HexMetrics.CellPerturbStrength;
        return position;
    }

    private static void AddTriangle(SurfaceTool st, Vector3 v1, Vector3 v2, Vector3 v3, Color c1, Color c2, Color c3)
    {
        st.SetColor(c1); st.AddVertex(Perturb(v1));
        st.SetColor(c2); st.AddVertex(Perturb(v2));
        st.SetColor(c3); st.AddVertex(Perturb(v3));
    }

    private static void AddTriangleUnperturbed(SurfaceTool st, Vector3 v1, Vector3 v2, Vector3 v3, Color c1, Color c2, Color c3)
    {
        st.SetColor(c1); st.AddVertex(v1);
        st.SetColor(c2); st.AddVertex(v2);
        st.SetColor(c3); st.AddVertex(v3);
    }

    private static void AddQuad(SurfaceTool st, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, Color c1, Color c2, Color c3, Color c4)
    {
        st.SetColor(c1); st.AddVertex(Perturb(v1));
        st.SetColor(c4); st.AddVertex(Perturb(v4));
        st.SetColor(c2); st.AddVertex(Perturb(v2));
        st.SetColor(c1); st.AddVertex(Perturb(v1));
        st.SetColor(c3); st.AddVertex(Perturb(v3));
        st.SetColor(c4); st.AddVertex(Perturb(v4));
    }

    private static void AddQuadUnperturbed(SurfaceTool st, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, Color c1, Color c2, Color c3, Color c4)
    {
        st.SetColor(c1); st.AddVertex(v1);
        st.SetColor(c4); st.AddVertex(v4);
        st.SetColor(c2); st.AddVertex(v2);
        st.SetColor(c1); st.AddVertex(v1);
        st.SetColor(c3); st.AddVertex(v3);
        st.SetColor(c4); st.AddVertex(v4);
    }

    private static void Triangulate(SurfaceTool st, HexDirection direction, HexCell cell)
    {
        Vector3 center = cell.Position;
        EdgeVertices e = new EdgeVertices(
            center + HexMetrics.GetFirstSolidCorner(direction),
            center + HexMetrics.GetSecondSolidCorner(direction)
        );

        TriangulateEdgeFan(st, center, e, cell.Color);

        if (direction <= HexDirection.SE)
        {
            TriangulateConnection(st, direction, cell, e);
        }
    }

    private static void TriangulateEdgeFan(SurfaceTool st, Vector3 center, EdgeVertices edge, Color color)
    {
        AddTriangle(st, center, edge.v1, edge.v2, color, color, color);
        AddTriangle(st, center, edge.v2, edge.v3, color, color, color);
        AddTriangle(st, center, edge.v3, edge.v4, color, color, color);
    }

    private static void TriangulateEdgeStrip(SurfaceTool st, EdgeVertices e1, Color c1, EdgeVertices e2, Color c2)
    {
        AddQuad(st, e1.v1, e1.v2, e2.v1, e2.v2, c1, c1, c2, c2);
        AddQuad(st, e1.v2, e1.v3, e2.v2, e2.v3, c1, c1, c2, c2);
        AddQuad(st, e1.v3, e1.v4, e2.v3, e2.v4, c1, c1, c2, c2);
    }

    private static void TriangulateConnection(SurfaceTool st, HexDirection direction, HexCell cell, EdgeVertices e1)
    {
        HexCell neighbor = cell.GetNeighbor(direction);
        if (neighbor == null) return;

        Vector3 bridge = HexMetrics.GetBridge(direction);
        bridge.Y = neighbor.Position.Y - cell.Position.Y;
        EdgeVertices e2 = new EdgeVertices(
            e1.v1 + bridge,
            e1.v4 + bridge
        );

        if (cell.GetEdgeType(direction) == HexEdgeType.Slope)
        {
            TriangulateEdgeTerraces(st, e1, cell, e2, neighbor);
        }
        else
        {
            TriangulateEdgeStrip(st, e1, cell.Color, e2, neighbor.Color);
        }

        HexCell nextNeighbor = cell.GetNeighbor(direction.Next());
        if (direction <= HexDirection.E && nextNeighbor != null)
        {
            Vector3 v5 = e1.v4 + HexMetrics.GetBridge(direction.Next());
            v5.Y = nextNeighbor.Position.Y;

            if (cell.Elevation <= neighbor.Elevation)
            {
                if (cell.Elevation <= nextNeighbor.Elevation)
                {
                    TriangulateCorner(st, e1.v4, cell, e2.v4, neighbor, v5, nextNeighbor);
                }
                else
                {
                    TriangulateCorner(st, v5, nextNeighbor, e1.v4, cell, e2.v4, neighbor);
                }
            }
            else if (neighbor.Elevation <= nextNeighbor.Elevation)
            {
                TriangulateCorner(st, e2.v4, neighbor, v5, nextNeighbor, e1.v4, cell);
            }
            else
            {
                TriangulateCorner(st, v5, nextNeighbor, e1.v4, cell, e2.v4, neighbor);
            }
        }
    }

    private static void TriangulateEdgeTerraces(SurfaceTool st,
        EdgeVertices begin, HexCell beginCell,
        EdgeVertices end, HexCell endCell)
    {
        EdgeVertices e2 = EdgeVertices.TerraceLerp(begin, end, 1);
        Color c2 = HexMetrics.TerraceLerp(beginCell.Color, endCell.Color, 1);

        TriangulateEdgeStrip(st, begin, beginCell.Color, e2, c2);

        for (int i = 2; i < HexMetrics.TerraceSteps; i++)
        {
            EdgeVertices e1 = e2;
            Color c1 = c2;
            e2 = EdgeVertices.TerraceLerp(begin, end, i);
            c2 = HexMetrics.TerraceLerp(beginCell.Color, endCell.Color, i);
            TriangulateEdgeStrip(st, e1, c1, e2, c2);
        }

        TriangulateEdgeStrip(st, e2, c2, end, endCell.Color);
    }

    private static void TriangulateCorner(SurfaceTool st,
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
                TriangulateCornerTerraces(st, bottom, bottomCell, left, leftCell, right, rightCell);
                return;
            }
            if (rightEdgeType == HexEdgeType.Flat)
            {
                TriangulateCornerTerraces(st, left, leftCell, right, rightCell, bottom, bottomCell);
                return;
            }
            TriangulateCornerTerracesCliff(st, bottom, bottomCell, left, leftCell, right, rightCell);
            return;
        }
        if (rightEdgeType == HexEdgeType.Slope)
        {
            if (leftEdgeType == HexEdgeType.Flat)
            {
                TriangulateCornerTerraces(st, right, rightCell, bottom, bottomCell, left, leftCell);
                return;
            }
            TriangulateCornerCliffTerraces(st, bottom, bottomCell, left, leftCell, right, rightCell);
            return;
        }
        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            if (leftCell.Elevation < rightCell.Elevation)
            {
                TriangulateCornerCliffTerraces(st, right, rightCell, bottom, bottomCell, left, leftCell);
                return;
            }
            TriangulateCornerTerracesCliff(st, left, leftCell, right, rightCell, bottom, bottomCell);
            return;
        }
        AddTriangle(st, bottom, left, right, bottomCell.Color, leftCell.Color, rightCell.Color);
    }

    private static void TriangulateCornerTerraces(SurfaceTool st,
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

    private static void TriangulateCornerTerracesCliff(SurfaceTool st,
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell)
    {
        float b = 1f / (rightCell.Elevation - beginCell.Elevation);
        if (b < 0) b = -b;
        Vector3 boundary = Perturb(begin).Lerp(Perturb(right), b);
        Color boundaryColor = beginCell.Color.Lerp(rightCell.Color, b);

        TriangulateBoundaryTriangle(st, begin, beginCell, left, leftCell, boundary, boundaryColor);

        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            TriangulateBoundaryTriangle(st, left, leftCell, right, rightCell, boundary, boundaryColor);
        }
        else
        {
            AddTriangleUnperturbed(st, Perturb(left), Perturb(right), boundary,
                leftCell.Color, rightCell.Color, boundaryColor);
        }
    }

    private static void TriangulateCornerCliffTerraces(SurfaceTool st,
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell)
    {
        float b = 1f / (leftCell.Elevation - beginCell.Elevation);
        if (b < 0) b = -b;
        Vector3 boundary = Perturb(begin).Lerp(Perturb(left), b);
        Color boundaryColor = beginCell.Color.Lerp(leftCell.Color, b);

        TriangulateBoundaryTriangle(st, right, rightCell, begin, beginCell, boundary, boundaryColor);

        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            TriangulateBoundaryTriangle(st, left, leftCell, right, rightCell, boundary, boundaryColor);
        }
        else
        {
            AddTriangleUnperturbed(st, Perturb(left), Perturb(right), boundary,
                leftCell.Color, rightCell.Color, boundaryColor);
        }
    }

    private static void TriangulateBoundaryTriangle(SurfaceTool st,
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 boundary, Color boundaryColor)
    {
        Vector3 v2 = Perturb(HexMetrics.TerraceLerp(begin, left, 1));
        Color c2 = HexMetrics.TerraceLerp(beginCell.Color, leftCell.Color, 1);

        AddTriangleUnperturbed(st, Perturb(begin), v2, boundary,
            beginCell.Color, c2, boundaryColor);

        for (int i = 2; i < HexMetrics.TerraceSteps; i++)
        {
            Vector3 v1 = v2;
            Color c1 = c2;
            v2 = Perturb(HexMetrics.TerraceLerp(begin, left, i));
            c2 = HexMetrics.TerraceLerp(beginCell.Color, leftCell.Color, i);
            AddTriangleUnperturbed(st, v1, v2, boundary, c1, c2, boundaryColor);
        }

        AddTriangleUnperturbed(st, v2, Perturb(left), boundary,
            c2, leftCell.Color, boundaryColor);
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

    public override void _Input(InputEvent @event)
    {
        if (Engine.IsEditorHint()) return;

        if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                var cell = RaycastToCell(mouseButton.Position);
                if (cell != null)
                {
                    EditCells(cell, 1);
                }
            }
            else if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                var cell = RaycastToCell(mouseButton.Position);
                if (cell != null)
                {
                    EditCells(cell, -1);
                }
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
