using Godot;

namespace HexMap;

/// <summary>
/// 地图保存数据资源。粗略架构设计，后续随教程逐步完善字段。
/// 使用 Godot Resource 系统，可通过 ResourceSaver / ResourceLoader 存取。
/// </summary>
[GlobalClass]
public partial class HexMapData : Resource
{
    [Export] public int Version = 1;
    [Export] public int Width;
    [Export] public int Height;
    [Export] public int Seed;

    // 预留：扁平数组存储各单元格数据，索引 = z * Width + x
    // Part 1 阶段先占位，后续添加 TerrainType / Elevation / WaterLevel 等字段
    [Export] public Godot.Collections.Array<int> TerrainTypes = new();
    [Export] public Godot.Collections.Array<int> Elevations = new();
    [Export] public Godot.Collections.Array<int> WaterLevels = new();
}
