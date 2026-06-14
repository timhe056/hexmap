using Godot;

namespace HexMap;

/// <summary>
/// Part 3.4.0: 六边形单元格标识符。
/// 现在是一个轻量级 struct，只保存 index 和 Grid 引用。
/// 实际数据（flags/values/coordinates）和位置都存储在 HexGrid 的数组中。
/// </summary>
public struct HexCell
{
    public readonly int Index;
    public readonly HexGrid Grid;

    public HexCell(int index, HexGrid grid)
    {
        Index = index;
        Grid = grid;
    }

    public readonly HexCoordinates Coordinates => Grid.CellData[Index].coordinates;
    public readonly Vector3 Position => Grid.CellPositions[Index];

    public readonly HexFlags Flags
    {
        get => Grid.CellData[Index].flags;
        set => Grid.CellData[Index].flags = value;
    }

    public readonly HexValues Values
    {
        get => Grid.CellData[Index].values;
        set => Grid.CellData[Index].values = value;
    }

    public readonly int Elevation => Values.Elevation;
    public readonly int WaterLevel => Values.WaterLevel;
    public readonly int ViewElevation => Values.ViewElevation;
    public readonly bool IsUnderwater => Values.IsUnderwater;
    public readonly int TerrainTypeIndex => Values.TerrainTypeIndex;
    public readonly int UrbanLevel => Values.UrbanLevel;
    public readonly int FarmLevel => Values.FarmLevel;
    public readonly int PlantLevel => Values.PlantLevel;
    public readonly int SpecialIndex => Values.SpecialIndex;

    public readonly bool IsSpecial => SpecialIndex > 0;
    public readonly bool Walled => Flags.HasAny(HexFlags.Walled);
    public readonly bool HasRoads => Flags.HasAny(HexFlags.Roads);
    public readonly bool IsExplored => Flags.HasAll(HexFlags.Explored | HexFlags.Explorable);
    public readonly bool Explorable => Flags.HasAny(HexFlags.Explorable);

    public readonly bool HasIncomingRiver => Flags.HasAny(HexFlags.RiverIn);
    public readonly bool HasOutgoingRiver => Flags.HasAny(HexFlags.RiverOut);
    public readonly bool HasRiver => Flags.HasAny(HexFlags.River);
    public readonly bool HasRiverBeginOrEnd => HasIncomingRiver != HasOutgoingRiver;

    public readonly HexDirection IncomingRiver => Flags.RiverInDirection();
    public readonly HexDirection OutgoingRiver => Flags.RiverOutDirection();

    public readonly HexUnit Unit
    {
        get => Grid.CellUnits[Index];
        set => Grid.CellUnits[Index] = value;
    }

    public readonly void SetElevation(int elevation)
    {
        if (Values.Elevation == elevation) return;
        Values = Values.WithElevation(elevation);
        Grid?.ShaderData?.ViewElevationChanged(Index);
        Grid?.RefreshCellPosition(Index);
        ValidateRivers();

        HexFlags flags = Flags;
        for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
        {
            if (flags.HasRoad(d))
            {
                HexCell neighbor = GetNeighbor(d);
                if (Mathf.Abs(elevation - neighbor.Values.Elevation) > 1)
                {
                    RemoveRoad(d);
                }
            }
        }

        Grid?.RefreshCellWithDependents(Index);
    }

    public readonly void SetWaterLevel(int waterLevel)
    {
        if (Values.WaterLevel == waterLevel) return;
        Values = Values.WithWaterLevel(waterLevel);
        Grid?.ShaderData?.ViewElevationChanged(Index);
        ValidateRivers();
        Grid?.RefreshCellWithDependents(Index);
    }

    public readonly void SetUrbanLevel(int urbanLevel)
    {
        if (Values.UrbanLevel != urbanLevel)
        {
            Values = Values.WithUrbanLevel(urbanLevel);
            Refresh();
        }
    }

    public readonly void SetFarmLevel(int farmLevel)
    {
        if (Values.FarmLevel != farmLevel)
        {
            Values = Values.WithFarmLevel(farmLevel);
            Refresh();
        }
    }

    public readonly void SetPlantLevel(int plantLevel)
    {
        if (Values.PlantLevel != plantLevel)
        {
            Values = Values.WithPlantLevel(plantLevel);
            Refresh();
        }
    }

    public readonly void SetSpecialIndex(int specialIndex)
    {
        if (Values.SpecialIndex != specialIndex && Flags.HasNone(HexFlags.River))
        {
            Values = Values.WithSpecialIndex(specialIndex);
            RemoveRoads();
            Refresh();
        }
    }

    public readonly void SetWalled(bool walled)
    {
        HexFlags flags = Flags;
        HexFlags newFlags = walled
            ? flags.With(HexFlags.Walled)
            : flags.Without(HexFlags.Walled);
        if (flags == newFlags) return;
        Flags = newFlags;
        Grid?.RefreshCellWithDependents(Index);
    }

    public readonly void SetTerrainTypeIndex(int terrainTypeIndex)
    {
        if (Values.TerrainTypeIndex != terrainTypeIndex)
        {
            Values = Values.WithTerrainTypeIndex(terrainTypeIndex);
            Grid?.ShaderData?.RefreshTerrain(Index);
        }
    }

    public readonly void MarkAsExplored() => Flags = Flags.With(HexFlags.Explored);

    public readonly bool HasRiverThroughEdge(HexDirection direction) =>
        Flags.HasRiver(direction);

    public readonly bool HasRoadThroughEdge(HexDirection direction) =>
        Flags.HasRoad(direction);

    public readonly HexEdgeType GetEdgeType(HexCell otherCell) =>
        HexMetrics.GetEdgeType(Values.Elevation, otherCell.Values.Elevation);

    public readonly HexCell GetNeighbor(HexDirection direction) =>
        Grid.GetCell(Coordinates.Step(direction));

    public readonly bool TryGetNeighbor(HexDirection direction, out HexCell cell) =>
        Grid.TryGetCell(Coordinates.Step(direction), out cell);

    private readonly void RemoveIncomingRiver()
    {
        if (!HasIncomingRiver) return;
        HexCell neighbor = GetNeighbor(IncomingRiver);
        Flags = Flags.Without(HexFlags.RiverIn);
        neighbor.Flags = neighbor.Flags.Without(HexFlags.RiverOut);
        neighbor.Refresh();
        Refresh();
    }

    private readonly void RemoveOutgoingRiver()
    {
        if (!HasOutgoingRiver) return;
        HexCell neighbor = GetNeighbor(OutgoingRiver);
        Flags = Flags.Without(HexFlags.RiverOut);
        neighbor.Flags = neighbor.Flags.Without(HexFlags.RiverIn);
        neighbor.Refresh();
        Refresh();
    }

    public readonly void RemoveRiver()
    {
        RemoveOutgoingRiver();
        RemoveIncomingRiver();
    }

    private static bool CanRiverFlow(HexValues from, HexValues to) =>
        from.Elevation >= to.Elevation || from.WaterLevel == to.Elevation;

    public readonly void SetOutgoingRiver(HexDirection direction)
    {
        if (Flags.HasRiverOut(direction)) return;
        HexCell neighbor = GetNeighbor(direction);
        if (!CanRiverFlow(Values, neighbor.Values)) return;

        RemoveOutgoingRiver();
        if (Flags.HasRiverIn(direction)) RemoveIncomingRiver();

        Flags = Flags.WithRiverOut(direction);
        Values = Values.WithSpecialIndex(0);
        neighbor.RemoveIncomingRiver();
        neighbor.Flags = neighbor.Flags.WithRiverIn(direction.Opposite());
        neighbor.Values = neighbor.Values.WithSpecialIndex(0);

        RemoveRoad(direction);
    }

    public readonly void AddRoad(HexDirection direction)
    {
        HexFlags flags = Flags;
        HexCell neighbor = GetNeighbor(direction);
        if (!flags.HasRoad(direction) && !flags.HasRiver(direction) &&
            Values.SpecialIndex == 0 && neighbor.Values.SpecialIndex == 0 &&
            Mathf.Abs(Values.Elevation - neighbor.Values.Elevation) <= 1)
        {
            Flags = flags.WithRoad(direction);
            neighbor.Flags = neighbor.Flags.WithRoad(direction.Opposite());
            neighbor.Refresh();
            Refresh();
        }
    }

    public readonly void RemoveRoads()
    {
        HexFlags flags = Flags;
        for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
        {
            if (flags.HasRoad(d))
            {
                RemoveRoad(d);
            }
        }
    }

    private readonly void ValidateRivers()
    {
        HexFlags flags = Flags;
        if (flags.HasAny(HexFlags.RiverOut) &&
            !CanRiverFlow(Values, GetNeighbor(flags.RiverOutDirection()).Values))
        {
            RemoveOutgoingRiver();
        }
        if (flags.HasAny(HexFlags.RiverIn) &&
            !CanRiverFlow(GetNeighbor(flags.RiverInDirection()).Values, Values))
        {
            RemoveIncomingRiver();
        }
    }

    private readonly void RemoveRoad(HexDirection direction)
    {
        Flags = Flags.WithoutRoad(direction);
        HexCell neighbor = GetNeighbor(direction);
        neighbor.Flags = neighbor.Flags.WithoutRoad(direction.Opposite());
        neighbor.Refresh();
        Refresh();
    }

    private readonly void Refresh() => Grid?.RefreshCell(Index);

    public readonly override bool Equals(object obj) =>
        obj is HexCell cell && this == cell;

    public readonly override int GetHashCode() =>
        Grid != null ? Index.GetHashCode() ^ Grid.GetHashCode() : 0;

    public static implicit operator bool(HexCell cell) => cell.Grid != null;

    public static bool operator ==(HexCell a, HexCell b) =>
        a.Index == b.Index && a.Grid == b.Grid;

    public static bool operator !=(HexCell a, HexCell b) =>
        a.Index != b.Index || a.Grid != b.Grid;
}
