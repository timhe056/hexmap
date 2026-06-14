using Godot;

namespace HexMap;

/// <summary>
/// Part 3.3.0: 把 cell 的 flags、values、coordinates 打包到一个结构体中。
/// 不再作为实例数据放在 HexCell 里，而是由 HexGrid 统一用数组持有。
/// </summary>
public struct HexCellData
{
    public HexFlags flags;
    public HexValues values;
    public HexCoordinates coordinates;

    public readonly int Elevation => values.Elevation;
    public readonly int WaterLevel => values.WaterLevel;
    public readonly int TerrainTypeIndex => values.TerrainTypeIndex;
    public readonly int UrbanLevel => values.UrbanLevel;
    public readonly int FarmLevel => values.FarmLevel;
    public readonly int PlantLevel => values.PlantLevel;
    public readonly int SpecialIndex => values.SpecialIndex;

    public readonly bool Walled => flags.HasAny(HexFlags.Walled);
    public readonly bool HasRoads => flags.HasAny(HexFlags.Roads);
    public readonly bool IsExplored =>
        flags.HasAll(HexFlags.Explored | HexFlags.Explorable);
    public readonly bool IsSpecial => values.SpecialIndex > 0;
    public readonly bool IsUnderwater => values.WaterLevel > values.Elevation;

    public readonly bool HasIncomingRiver => flags.HasAny(HexFlags.RiverIn);
    public readonly bool HasOutgoingRiver => flags.HasAny(HexFlags.RiverOut);
    public readonly bool HasRiver => flags.HasAny(HexFlags.River);
    public readonly bool HasRiverBeginOrEnd =>
        HasIncomingRiver != HasOutgoingRiver;

    public readonly HexDirection IncomingRiver => flags.RiverInDirection();
    public readonly HexDirection OutgoingRiver => flags.RiverOutDirection();

    public readonly float StreamBedY =>
        (values.Elevation + HexMetrics.StreamBedElevationOffset) *
        HexMetrics.ElevationStep;

    public readonly float RiverSurfaceY =>
        (values.Elevation + HexMetrics.WaterElevationOffset) *
        HexMetrics.ElevationStep;

    public readonly float WaterSurfaceY =>
        (values.WaterLevel + HexMetrics.WaterElevationOffset) *
        HexMetrics.ElevationStep;

    public readonly int ViewElevation =>
        values.Elevation >= values.WaterLevel
            ? values.Elevation
            : values.WaterLevel;

    public readonly HexEdgeType GetEdgeType(HexCellData otherCell) =>
        HexMetrics.GetEdgeType(values.Elevation, otherCell.values.Elevation);

    public readonly bool HasIncomingRiverThroughEdge(HexDirection direction) =>
        flags.HasRiverIn(direction);

    public readonly bool HasRiverThroughEdge(HexDirection direction) =>
        flags.HasRiverIn(direction) || flags.HasRiverOut(direction);

    public readonly bool HasRoadThroughEdge(HexDirection direction) =>
        flags.HasRoad(direction);
}
