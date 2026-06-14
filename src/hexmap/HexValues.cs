using System.IO;

namespace HexMap;

/// <summary>
/// Part 3.1.0: 描述单元格内容的七个值，打包到一个 32 位整数中。
/// 布局：TTTTTTTT SSSSSSSS PPFFUUWW WWWEEEEE
/// </summary>
public struct HexValues
{
    /// <summary>Seven values stored in 32 bits.</summary>
    private int _values;

    private readonly int Get(int mask, int shift) =>
        (int)((uint)_values >> shift) & mask;

    private readonly HexValues With(int value, int mask, int shift) => new()
    {
        _values = (_values & ~(mask << shift)) | ((value & mask) << shift)
    };

    public readonly int Elevation => Get(31, 0) - 15;

    public readonly HexValues WithElevation(int value) =>
        With(value + 15, 31, 0);

    public readonly int WaterLevel => Get(31, 5);

    public readonly int ViewElevation => System.Math.Max(Elevation, WaterLevel);

    public readonly bool IsUnderwater => WaterLevel > Elevation;

    public readonly HexValues WithWaterLevel(int value) =>
        With(value, 31, 5);

    public readonly int UrbanLevel => Get(3, 10);

    public readonly HexValues WithUrbanLevel(int value) =>
        With(value, 3, 10);

    public readonly int FarmLevel => Get(3, 12);

    public readonly HexValues WithFarmLevel(int value) =>
        With(value, 3, 12);

    public readonly int PlantLevel => Get(3, 14);

    public readonly HexValues WithPlantLevel(int value) =>
        With(value, 3, 14);

    public readonly int SpecialIndex => Get(255, 16);

    public readonly HexValues WithSpecialIndex(int index) =>
        With(index, 255, 16);

    public readonly int TerrainTypeIndex => Get(255, 24);

    public readonly HexValues WithTerrainTypeIndex(int index) =>
        With(index, 255, 24);

    /// <summary>保存七个值（7 字节）。</summary>
    public readonly void Save(BinaryWriter writer)
    {
        writer.Write((byte)TerrainTypeIndex);
        writer.Write((byte)(Elevation + 127));
        writer.Write((byte)WaterLevel);
        writer.Write((byte)UrbanLevel);
        writer.Write((byte)FarmLevel);
        writer.Write((byte)PlantLevel);
        writer.Write((byte)SpecialIndex);
    }

    /// <summary>加载七个值。</summary>
    public static HexValues Load(BinaryReader reader, int header)
    {
        HexValues values = default;
        values = values.WithTerrainTypeIndex(reader.ReadByte());
        int elevation = reader.ReadByte();
        if (header >= 4)
        {
            elevation -= 127;
        }
        values = values.WithElevation(elevation);
        values = values.WithWaterLevel(reader.ReadByte());
        values = values.WithUrbanLevel(reader.ReadByte());
        values = values.WithFarmLevel(reader.ReadByte());
        values = values.WithPlantLevel(reader.ReadByte());
        return values.WithSpecialIndex(reader.ReadByte());
    }
}
