using Godot;

namespace HexMap;

/// <summary>
/// 单个六边形单元格的数据模型（纯数据类）。
/// Part 5：Color / Elevation 改为属性，变化时自动刷新所属 Chunk。
/// </summary>
public class HexCell
{
    public HexCoordinates Coordinates { get; set; }
    /// <summary>XZ 平面上的基础位置（不含高程）</summary>
    public Vector3 BasePosition { get; set; }
    /// <summary>实际世界坐标（含高程 + 高程扰动）</summary>
    public Vector3 Position { get; private set; }

    private Color _color;
    public Color Color
    {
        get => _color;
        set
        {
            if (_color == value) return;
            _color = value;
            Refresh();
        }
    }

    private int _elevation = int.MinValue;
    public int Elevation
    {
        get => _elevation;
        set
        {
            if (_elevation == value) return;
            _elevation = value;
            RefreshPosition();

            // Part 6: 改变高程后验证河流合法性，移除上坡河流
            if (HasOutgoingRiver && Elevation < GetNeighbor(OutgoingRiver).Elevation)
            {
                RemoveOutgoingRiver();
            }
            if (HasIncomingRiver && Elevation > GetNeighbor(IncomingRiver).Elevation)
            {
                RemoveIncomingRiver();
            }

            /* Part 7: 高程变化后移除坡度大于 1 的道路（变成悬崖） */
            for (int i = 0; i < Roads.Length; i++)
            {
                if (Roads[i] && GetElevationDifference((HexDirection)i) > 1)
                {
                    SetRoad(i, false);
                }
            }

            Refresh();
        }
    }

    public int WaterLevel { get; set; }

    public HexCell[] Neighbors { get; } = new HexCell[6];

    public bool[] Roads { get; } = new bool[6];

    public bool HasIncomingRiver { get; set; }
    public bool HasOutgoingRiver { get; set; }
    public HexDirection IncomingRiver { get; set; }
    public HexDirection OutgoingRiver { get; set; }

    public bool HasRiver => HasIncomingRiver || HasOutgoingRiver;
    public bool HasRiverBeginOrEnd => HasIncomingRiver != HasOutgoingRiver;
    public bool HasRiverThroughEdge(HexDirection direction)
        => (HasIncomingRiver && IncomingRiver == direction) ||
           (HasOutgoingRiver && OutgoingRiver == direction);

    /* Part 7: 是否有至少一条道路 */
    public bool HasRoads
    {
        get
        {
            for (int i = 0; i < Roads.Length; i++)
            {
                if (Roads[i]) return true;
            }
            return false;
        }
    }

    /* Part 7: 河流起点或终点的方向（仅当 HasRiverBeginOrEnd 为 true 时有效） */
    public HexDirection RiverBeginOrEndDirection
        => HasIncomingRiver ? IncomingRiver : OutgoingRiver;

    public bool IsUnderwater => WaterLevel > Elevation;
    public float StreamBedY => (Elevation + HexMetrics.StreamBedElevationOffset) * HexMetrics.ElevationStep;
    public float RiverSurfaceY => (Elevation + HexMetrics.RiverSurfaceElevationOffset) * HexMetrics.ElevationStep;
    public float WaterSurfaceY => (WaterLevel + HexMetrics.WaterElevationOffset) * HexMetrics.ElevationStep;

    public bool Walled { get; set; }
    public int UrbanLevel { get; set; }
    public int FarmLevel { get; set; }
    public int PlantLevel { get; set; }
    public int SpecialIndex { get; set; }

    /// <summary>所属 Chunk（Part 5）</summary>
    public HexGridChunk Chunk { get; set; }

    public HexCell GetNeighbor(HexDirection direction) => Neighbors[(int)direction];

    public void SetNeighbor(HexDirection direction, HexCell cell)
    {
        Neighbors[(int)direction] = cell;
        cell.Neighbors[(int)direction.Opposite()] = this;
    }

    /* Part 7: 改为返回绝对差值，以便 AddRoad 中的 <= 1 检查正确判断上下坡 */
    public int GetElevationDifference(HexDirection direction)
    {
        HexCell neighbor = GetNeighbor(direction);
        if (neighbor == null) return int.MaxValue;
        int diff = Elevation - neighbor.Elevation;
        return diff >= 0 ? diff : -diff;
    }

    public HexEdgeType GetEdgeType(HexDirection direction)
    {
        HexCell neighbor = GetNeighbor(direction);
        return neighbor == null ? HexEdgeType.Cliff : HexMetrics.GetEdgeType(Elevation, neighbor.Elevation);
    }

    public HexEdgeType GetEdgeType(HexCell otherCell)
        => HexMetrics.GetEdgeType(Elevation, otherCell.Elevation);

    public void RemoveOutgoingRiver()
    {
        if (!HasOutgoingRiver) return;
        HasOutgoingRiver = false;
        RefreshSelfOnly();

        HexCell neighbor = GetNeighbor(OutgoingRiver);
        if (neighbor != null)
        {
            neighbor.HasIncomingRiver = false;
            neighbor.RefreshSelfOnly();
        }
    }

    public void RemoveIncomingRiver()
    {
        if (!HasIncomingRiver) return;
        HasIncomingRiver = false;
        RefreshSelfOnly();

        HexCell neighbor = GetNeighbor(IncomingRiver);
        if (neighbor != null)
        {
            neighbor.HasOutgoingRiver = false;
            neighbor.RefreshSelfOnly();
        }
    }

    public void RemoveRiver()
    {
        RemoveOutgoingRiver();
        RemoveIncomingRiver();
    }

    public void SetOutgoingRiver(HexDirection direction)
    {
        if (HasOutgoingRiver && OutgoingRiver == direction) return;
        HexCell neighbor = GetNeighbor(direction);
        if (!IsValidRiverDestination(neighbor))
        {
            GD.Print($"[HexCell] Invalid river dest: {Coordinates} -> {direction}, neighbor={neighbor?.Coordinates}, elev={Elevation}, neighborElev={neighbor?.Elevation}");
            return;
        }

        RemoveOutgoingRiver();
        if (HasIncomingRiver && IncomingRiver == direction) RemoveIncomingRiver();

        HasOutgoingRiver = true;
        OutgoingRiver = direction;
        RefreshSelfOnly();

        neighbor.RemoveIncomingRiver();
        neighbor.HasIncomingRiver = true;
        neighbor.IncomingRiver = direction.Opposite();
        neighbor.RefreshSelfOnly();

        /* Part 7: 河流冲毁同方向的道路 */
        SetRoad((int)direction, false);

        GD.Print($"[HexCell] River set: {Coordinates} -> {direction} -> {neighbor.Coordinates}");
    }

    public bool HasRoadThroughEdge(HexDirection direction) => Roads[(int)direction];

    /* Part 7: 使用 SetRoad helper 统一设置道路，包含两侧刷新 */
    public void RemoveRoads()
    {
        for (int i = 0; i < Roads.Length; i++)
        {
            if (Roads[i])
            {
                SetRoad(i, false);
            }
        }
    }

    /* Part 7: 使用 SetRoad helper 统一设置道路 */
    public void AddRoad(HexDirection direction)
    {
        if (!Roads[(int)direction] && !HasRiverThroughEdge(direction)
            && GetElevationDifference(direction) <= 1)
        {
            SetRoad((int)direction, true);
        }
    }

    /* Part 7: 统一的路由设置入口，同时刷新自身和邻居 */
    private void SetRoad(int index, bool state)
    {
        Roads[index] = state;
        HexCell neighbor = Neighbors[index];
        if (neighbor != null)
        {
            neighbor.Roads[(int)((HexDirection)index).Opposite()] = state;
            neighbor.RefreshSelfOnly();
        }
        RefreshSelfOnly();
    }

    private bool IsValidRiverDestination(HexCell neighbor)
    {
        return neighbor != null && (Elevation >= neighbor.Elevation || WaterLevel == neighbor.Elevation);
    }

    /// <summary>根据 Elevation 和噪声重新计算 Position</summary>
    private void RefreshPosition()
    {
        Vector3 pos = BasePosition;
        pos.Y = _elevation * HexMetrics.ElevationStep;
        pos.Y += (HexMetrics.SampleNoise(pos).Y * 2f - 1f) * HexMetrics.ElevationPerturbStrength;
        Position = pos;
    }

    /// <summary>Part 5：刷新自身 Chunk，以及边界处邻居的 Chunk</summary>
    private void Refresh()
    {
        if (Chunk != null)
        {
            Chunk.Refresh();
            for (int i = 0; i < Neighbors.Length; i++)
            {
                HexCell neighbor = Neighbors[i];
                if (neighbor != null && neighbor.Chunk != Chunk)
                {
                    neighbor.Chunk.Refresh();
                }
            }
        }
    }

    /// <summary>Part 6：只刷新自身 Chunk（河流修改用，不影响邻居 Chunk）</summary>
    private void RefreshSelfOnly()
    {
        Chunk?.Refresh();
    }

    // 用于后续 Hash Grid 特征放置
    public HexHash Hash { get; set; }
}

/// <summary>
/// Part 9: 用于确定性随机放置地形特征的哈希值。
/// A=城市阈值, B=农场阈值, C=植物阈值, D=变体选择, E=旋转
/// </summary>
public readonly struct HexHash
{
    public readonly float A;
    public readonly float B;
    public readonly float C;
    public readonly float D;
    public readonly float E;

    public HexHash(float a, float b, float c, float d, float e)
    {
        A = a;
        B = b;
        C = c;
        D = d;
        E = e;
    }

    /// <summary>Part 9: 使用 System.Random 生成确定性 5 值哈希</summary>
    public static HexHash Random(System.Random rng)
    {
        return new HexHash(
            (float)rng.NextDouble() * 0.999f,
            (float)rng.NextDouble() * 0.999f,
            (float)rng.NextDouble() * 0.999f,
            (float)rng.NextDouble() * 0.999f,
            (float)rng.NextDouble() * 0.999f
        );
    }
}
