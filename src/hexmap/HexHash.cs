using System;

namespace HexMap;

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
