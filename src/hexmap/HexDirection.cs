namespace HexMap;

/// <summary>
/// 六边形的六个方向。顺序为顺时针：NE → E → SE → SW → W → NW
/// </summary>
public enum HexDirection
{
    NE, E, SE, SW, W, NW
}

public static class HexDirectionExtensions
{
    /// <summary>反方向（相隔3个方向）</summary>
    public static HexDirection Opposite(this HexDirection direction)
        => (int)direction < 3
            ? (HexDirection)((int)direction + 3)
            : (HexDirection)((int)direction - 3);

    /// <summary>顺时针前一个方向</summary>
    public static HexDirection Previous(this HexDirection direction)
        => direction == HexDirection.NE
            ? HexDirection.NW
            : (HexDirection)((int)direction - 1);

    /// <summary>顺时针后一个方向</summary>
    public static HexDirection Next(this HexDirection direction)
        => direction == HexDirection.NW
            ? HexDirection.NE
            : (HexDirection)((int)direction + 1);

    /// <summary>前两个方向</summary>
    public static HexDirection Previous2(this HexDirection direction)
    {
        int d = (int)direction - 2;
        return d < (int)HexDirection.NE
            ? (HexDirection)(d + 6)
            : (HexDirection)d;
    }

    /// <summary>后两个方向</summary>
    public static HexDirection Next2(this HexDirection direction)
    {
        int d = (int)direction + 2;
        return d > (int)HexDirection.NW
            ? (HexDirection)(d - 6)
            : (HexDirection)d;
    }
}
