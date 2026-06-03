using Godot;

namespace HexMap;

/// <summary>
/// 六边形轴向/立方坐标。内部存储 X(axial-q) 和 Z(axial-r)，Y 由 -X-Z 推导。
/// 对应教程中的 HexCoordinates。
/// </summary>
public readonly struct HexCoordinates : System.IEquatable<HexCoordinates>
{
    public int X { get; }
    public int Z { get; }
    public int Y => -X - Z;

    public HexCoordinates(int x, int z)
    {
        X = x;
        Z = z;
    }

    /// <summary>从 even-r offset 坐标转换为 axial 坐标</summary>
    public static HexCoordinates FromOffsetCoordinates(int x, int z)
    {
        return new HexCoordinates(x - z / 2, z);
    }

    /// <summary>从世界坐标反推 HexCoordinates（用于鼠标点击等）</summary>
    public static HexCoordinates FromPosition(Vector3 position)
    {
        float x = position.X / (HexMetrics.InnerRadius * 2f);
        float y = -x;
        float offset = position.Z / (HexMetrics.OuterRadius * 3f);
        x -= offset;
        y -= offset;

        int iX = Mathf.RoundToInt(x);
        int iY = Mathf.RoundToInt(y);
        int iZ = Mathf.RoundToInt(-x - y);

        if (iX + iY + iZ != 0)
        {
            float dX = Mathf.Abs(x - iX);
            float dY = Mathf.Abs(y - iY);
            float dZ = Mathf.Abs(-x - y - iZ);

            if (dX > dY && dX > dZ)
                iX = -iY - iZ;
            else if (dY > dZ)
                iY = -iX - iZ;
            else
                iZ = -iX - iY;
        }

        return new HexCoordinates(iX, iZ);
    }

    public int DistanceTo(HexCoordinates other)
    {
        return (Mathf.Abs(X - other.X) + Mathf.Abs(Y - other.Y) + Mathf.Abs(Z - other.Z)) / 2;
    }

    public override string ToString() => $"({X}, {Y}, {Z})";
    public string ToStringOnSeparateLines() => $"{X}\n{Y}\n{Z}";

    public bool Equals(HexCoordinates other) => X == other.X && Z == other.Z;
    public override bool Equals(object obj) => obj is HexCoordinates other && Equals(other);
    public override int GetHashCode() => System.HashCode.Combine(X, Z);

    public static bool operator ==(HexCoordinates lhs, HexCoordinates rhs) => lhs.Equals(rhs);
    public static bool operator !=(HexCoordinates lhs, HexCoordinates rhs) => !lhs.Equals(rhs);
}
