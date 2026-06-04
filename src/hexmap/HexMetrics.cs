using Godot;

namespace HexMap;

/// <summary>
/// 六边形几何常量与工具方法。对应教程中的 HexMetrics。
/// </summary>
public static class HexMetrics
{
    /// <summary>六边形外接圆半径（角点到中心的距离）</summary>
    public const float OuterRadius = 10f;
    /// <summary>六边形内切圆半径 = OuterRadius * √3/2，即边到中心的距离</summary>
    public const float InnerRadius = OuterRadius * 0.866025404f;

    /// <summary>实心六边形占外接圆半径的比例（Part 4 调至 0.8）</summary>
    public const float SolidFactor = 0.8f;
    /// <summary>边缘混合区比例（1 - SolidFactor = 0.2），用于相邻格子颜色过渡</summary>
    public const float BlendFactor = 1f - SolidFactor;

    /// <summary>每个海拔台阶的垂直高度。Part 4 降至 3</summary>
    public const float ElevationStep = 3f;
    /// <summary>河流河床下沉偏移（以台阶为单位）。Part 6 加深至 -1.75</summary>
    public const float StreamBedElevationOffset = -1.75f;
    /// <summary>河流水面下沉偏移（以台阶为单位）</summary>
    public const float RiverSurfaceElevationOffset = -0.5f;
    /// <summary>水面下沉偏移（以台阶为单位）。水面比 WaterLevel 低 0.5 个台阶，避免与陆地硬边冲突</summary>
    public const float WaterElevationOffset = -0.5f;

    public const float OuterToInner = 0.866025404f;
    public const float InnerToOuter = 1f / OuterToInner;

    /// <summary>每个斜坡（相邻格子海拔差=1）包含的台阶数</summary>
    public const int TerracesPerSlope = 2;
    /// <summary>三角化一个斜坡需要的总步数 = 台阶数*2 + 1（含起点终点）</summary>
    public const int TerraceSteps = TerracesPerSlope * 2 + 1;
    /// <summary>斜坡水平插值步长（XZ平面），每步前进 1/TerraceSteps</summary>
    public const float HorizontalTerraceStepSize = 1f / TerraceSteps;
    /// <summary>斜坡垂直插值步长（Y轴），每步升高 1/(TerracesPerSlope+1)</summary>
    public const float VerticalTerraceStepSize = 1f / (TerracesPerSlope + 1);

    /// <summary>顶点位置扰动强度（Part 4 降至 4）</summary>
    public const float CellPerturbStrength = 4f;
    /// <summary>高程扰动强度（Part 4）</summary>
    public const float ElevationPerturbStrength = 1.5f;
    /// <summary>Perlin 噪声采样缩放，越小噪声频率越低（地形越平缓）</summary>
    public const float NoiseScale = 0.003f;
    /// <summary>每个 Chunk 的宽度（格子数）</summary>
    public const int ChunkSizeX = 5;
    /// <summary>每个 Chunk 的高度（格子数）</summary>
    public const int ChunkSizeZ = 5;

    private static FastNoiseLite _noise;

    /// <summary>六边形角点（XZ平面，Y=0）。索引0~5为六个角，索引6重复索引0方便取Next corner。</summary>
    public static readonly Vector3[] Corners = {
        new Vector3(0f, 0f, OuterRadius),
        new Vector3(InnerRadius, 0f, 0.5f * OuterRadius),
        new Vector3(InnerRadius, 0f, -0.5f * OuterRadius),
        new Vector3(0f, 0f, -OuterRadius),
        new Vector3(-InnerRadius, 0f, -0.5f * OuterRadius),
        new Vector3(-InnerRadius, 0f, 0.5f * OuterRadius),
        new Vector3(0f, 0f, OuterRadius)
    };

    public static Vector3 GetFirstCorner(HexDirection direction) => Corners[(int)direction];
    public static Vector3 GetSecondCorner(HexDirection direction) => Corners[(int)direction + 1];

    public static Vector3 GetFirstSolidCorner(HexDirection direction)
        => Corners[(int)direction] * SolidFactor;

    public static Vector3 GetSecondSolidCorner(HexDirection direction)
        => Corners[(int)direction + 1] * SolidFactor;

    public static Vector3 GetSolidEdgeMiddle(HexDirection direction)
        => (Corners[(int)direction] + Corners[(int)direction + 1]) * (0.5f * SolidFactor);

    /// <summary>桥接向量：从当前格子 solid corner 直达邻居 solid corner</summary>
    public static Vector3 GetBridge(HexDirection direction)
        => (Corners[(int)direction] + Corners[(int)direction + 1]) * BlendFactor;

    public static Vector3 TerraceLerp(Vector3 a, Vector3 b, int step)
    {
        float h = step * HorizontalTerraceStepSize;
        a.X += (b.X - a.X) * h;
        a.Z += (b.Z - a.Z) * h;
        float v = ((step + 1) / 2) * VerticalTerraceStepSize;
        a.Y += (b.Y - a.Y) * v;
        return a;
    }

    public static Color TerraceLerp(Color a, Color b, int step)
    {
        float h = step * HorizontalTerraceStepSize;
        return a.Lerp(b, h);
    }

    public static HexEdgeType GetEdgeType(int elevation1, int elevation2)
    {
        if (elevation1 == elevation2) return HexEdgeType.Flat;
        int delta = elevation1 - elevation2;
        if (delta == 1 || delta == -1) return HexEdgeType.Slope;
        return HexEdgeType.Cliff;
    }

    /// <summary>Part 4：初始化噪声生成器</summary>
    public static void InitializeNoise()
    {
        if (_noise != null) return;

        _noise = new FastNoiseLite();
        _noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
        _noise.Seed = 12345;
        _noise.Frequency = 1.0f;
    }

    /// <summary>Part 4：根据世界坐标采样噪声（4 通道）</summary>
    public static Vector4 SampleNoise(Vector3 position)
    {
        if (_noise == null) return new Vector4(0.5f, 0.5f, 0.5f, 0.5f);

        float x = position.X * NoiseScale;
        float z = position.Z * NoiseScale;
        float r = _noise.GetNoise2D(x, z) * 0.5f + 0.5f;
        float g = _noise.GetNoise2D(x + 1000f, z) * 0.5f + 0.5f;
        float b = _noise.GetNoise2D(x, z + 1000f) * 0.5f + 0.5f;
        float a = _noise.GetNoise2D(x + 1000f, z + 1000f) * 0.5f + 0.5f;
        return new Vector4(r, g, b, a);
    }
}

public enum HexEdgeType { Flat, Slope, Cliff }
