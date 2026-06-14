using Godot;
using System.IO;

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

    /// <summary>Part 2.2: hex space 中的 X 坐标，东西相邻 cell 中心距为 1。</summary>
    public float HexX => X + Z / 2 + ((Z & 1) == 0 ? 0f : 0.5f);

    /// <summary>Part 2.2: hex space 中的 Z 坐标。</summary>
    public float HexZ => Z * HexMetrics.OuterToInner;

    /// <summary>Part 3.3.0: 所在 chunk 列索引。</summary>
    public int ColumnIndex => (X + Z / 2) / HexMetrics.ChunkSizeX;

    public HexCoordinates(int x, int z)
    {
        /* Part 27: 环绕地图把 X 归约到合法范围 */
        if (HexMetrics.Wrapping)
        {
            int oX = x + z / 2;
            if (oX < 0)
            {
                x += HexMetrics.wrapSize;
            }
            else if (oX >= HexMetrics.wrapSize)
            {
                x -= HexMetrics.wrapSize;
            }
        }
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
        float x = position.X / HexMetrics.InnerDiameter;
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
        int dx = X - other.X;
        int dz = Z - other.Z;
        int dy = -dx - dz;
        int distance = (Mathf.Abs(dx) + Mathf.Abs(dy) + Mathf.Abs(dz)) / 2;

        /* Part 27: 环绕地图取最短距离（直接 / +wrapSize / -wrapSize） */
        if (HexMetrics.Wrapping)
        {
            int otherX = other.X + HexMetrics.wrapSize;
            dx = X - otherX;
            dy = -dx - dz;
            int wrapped = (Mathf.Abs(dx) + Mathf.Abs(dy) + Mathf.Abs(dz)) / 2;
            if (wrapped < distance)
            {
                distance = wrapped;
            }
            else
            {
                otherX -= 2 * HexMetrics.wrapSize;
                dx = X - otherX;
                dy = -dx - dz;
                wrapped = (Mathf.Abs(dx) + Mathf.Abs(dy) + Mathf.Abs(dz)) / 2;
                if (wrapped < distance)
                {
                    distance = wrapped;
                }
            }
        }

        return distance;
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(X);
        writer.Write(Z);
    }

    public static HexCoordinates Load(BinaryReader reader)
    {
        return new HexCoordinates(reader.ReadInt32(), reader.ReadInt32());
    }

    /// <summary>Part 2.3: 返回指定方向的相邻 cell 坐标。</summary>
    public HexCoordinates Step(HexDirection direction) => direction switch
    {
        HexDirection.NE => new HexCoordinates(X, Z + 1),
        HexDirection.E => new HexCoordinates(X + 1, Z),
        HexDirection.SE => new HexCoordinates(X + 1, Z - 1),
        HexDirection.SW => new HexCoordinates(X, Z - 1),
        HexDirection.W => new HexCoordinates(X - 1, Z),
        _ => new HexCoordinates(X - 1, Z + 1)
    };

    /// <summary>Part 6：返回从当前坐标指向邻居坐标的方向。假设两者相邻。</summary>
    public HexDirection GetNeighborDirection(HexCoordinates other)
    {
        int dx = other.X - X;
        int dz = other.Z - Z;
        return (dx, dz) switch
        {
            (1, 0) => HexDirection.E,
            (1, -1) => HexDirection.NE,
            (0, -1) => HexDirection.NW,
            (-1, 0) => HexDirection.W,
            (-1, 1) => HexDirection.SW,
            (0, 1) => HexDirection.SE,
            _ => HexDirection.NE // fallback
        };
    }

    public override string ToString() => $"({X}, {Y}, {Z})";
    public string ToStringOnSeparateLines() => $"{X}\n{Y}\n{Z}";

    public bool Equals(HexCoordinates other) => X == other.X && Z == other.Z;
    public override bool Equals(object obj) => obj is HexCoordinates other && Equals(other);
    public override int GetHashCode() => System.HashCode.Combine(X, Z);

    public static bool operator ==(HexCoordinates lhs, HexCoordinates rhs) => lhs.Equals(rhs);
    public static bool operator !=(HexCoordinates lhs, HexCoordinates rhs) => !lhs.Equals(rhs);
}
