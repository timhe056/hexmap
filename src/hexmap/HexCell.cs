using Godot;

namespace HexMap;

/// <summary>
/// 单个六边形单元格的数据模型（纯数据类）。
/// 后续教程会逐步添加河流、道路、水域、城墙等属性。
/// </summary>
public class HexCell
{
    public HexCoordinates Coordinates { get; set; }
    public Vector3 Position { get; set; }
    public Color Color { get; set; } = Colors.White;

    public int TerrainTypeIndex { get; set; }
    public int Elevation { get; set; }
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

    public bool IsUnderwater => WaterLevel > Elevation;
    public float StreamBedY => (Elevation + HexMetrics.StreamBedElevationOffset) * HexMetrics.ElevationStep;
    public float RiverSurfaceY => (Elevation + HexMetrics.WaterElevationOffset) * HexMetrics.ElevationStep;
    public float WaterSurfaceY => (WaterLevel + HexMetrics.WaterElevationOffset) * HexMetrics.ElevationStep;

    public bool Walled { get; set; }
    public int UrbanLevel { get; set; }
    public int FarmLevel { get; set; }
    public int PlantLevel { get; set; }
    public int SpecialIndex { get; set; }

    public HexCell GetNeighbor(HexDirection direction) => Neighbors[(int)direction];

    public void SetNeighbor(HexDirection direction, HexCell cell)
    {
        Neighbors[(int)direction] = cell;
        cell.Neighbors[(int)direction.Opposite()] = this;
    }

    public int GetElevationDifference(HexDirection direction)
    {
        HexCell neighbor = GetNeighbor(direction);
        return neighbor == null ? int.MaxValue : Elevation - neighbor.Elevation;
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
        // Refresh visual...
    }

    public void RemoveIncomingRiver()
    {
        if (!HasIncomingRiver) return;
        HasIncomingRiver = false;
        // Refresh visual...
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
        if (!IsValidRiverDestination(neighbor)) return;

        RemoveOutgoingRiver();
        if (HasIncomingRiver && IncomingRiver == direction) RemoveIncomingRiver();

        HasOutgoingRiver = true;
        OutgoingRiver = direction;

        neighbor.RemoveIncomingRiver();
        neighbor.HasIncomingRiver = true;
        neighbor.IncomingRiver = direction.Opposite();
    }

    public bool HasRoadThroughEdge(HexDirection direction) => Roads[(int)direction];

    public void RemoveRoads()
    {
        for (int i = 0; i < Roads.Length; i++)
        {
            if (Roads[i])
            {
                Roads[i] = false;
                HexCell neighbor = Neighbors[i];
                if (neighbor != null)
                {
                    neighbor.Roads[(int)((HexDirection)i).Opposite()] = false;
                }
            }
        }
    }

    public void AddRoad(HexDirection direction)
    {
        if (!Roads[(int)direction] && !HasRiverThroughEdge(direction)
            && GetElevationDifference(direction) <= 1)
        {
            Roads[(int)direction] = true;
            HexCell neighbor = Neighbors[(int)direction];
            if (neighbor != null)
            {
                neighbor.Roads[(int)direction.Opposite()] = true;
            }
        }
    }

    private bool IsValidRiverDestination(HexCell neighbor)
    {
        return neighbor != null && (Elevation >= neighbor.Elevation || WaterLevel == neighbor.Elevation);
    }

    // 用于后续 Hash Grid 特征放置
    public HexHash Hash { get; set; }
}

/// <summary>用于确定性随机放置地形特征（Part 9）</summary>
public readonly struct HexHash
{
    public readonly float A;
    public readonly float B;

    public HexHash(float a, float b)
    {
        A = a;
        B = b;
    }
}
