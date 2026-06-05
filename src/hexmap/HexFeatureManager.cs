using Godot;

namespace HexMap;

/* Part 9: 特征 prefab 集合，按 hash 值选择变体 */
public readonly struct HexFeatureCollection
{
    public readonly PackedScene[] Prefabs;

    public HexFeatureCollection(PackedScene[] prefabs)
    {
        Prefabs = prefabs;
    }

    /// <summary>Part 9: 根据 hash 值从数组中选择一个 prefab</summary>
    public PackedScene Pick(float choice)
    {
        return Prefabs[Mathf.FloorToInt(choice * Prefabs.Length)];
    }
}

/// <summary>
/// Part 9: 六边形地形特征管理器。
/// 作为 HexGridChunk 的子节点，管理各地形特征的 Node3D 实例。
/// 特征以独立节点存在，不嵌入地形网格。
/// </summary>
public partial class HexFeatureManager : Node3D
{
    /* Part 9: 三种特征类型的 prefab 集合数组（每级含多个变体） */
    private HexFeatureCollection[] _urbanCollections;
    private HexFeatureCollection[] _farmCollections;
    private HexFeatureCollection[] _plantCollections;

    /* Part 9: 设置 prefab 集合（由 HexGrid 在构建时传入） */
    public void SetCollections(
        HexFeatureCollection[] urban,
        HexFeatureCollection[] farm,
        HexFeatureCollection[] plant)
    {
        _urbanCollections = urban;
        _farmCollections = farm;
        _plantCollections = plant;
    }

    /// <summary>Part 9: 清除所有已放置的特征节点</summary>
    public void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }
    }

    /// <summary>Part 9: 预留的空方法（后续可能用于批处理优化）</summary>
    public void Apply()
    {
        // 预留：后续 Part 可能用于批处理优化
    }

    /// <summary>Part 9: 根据等级和哈希值从集合中选择合适的 prefab</summary>
    private PackedScene PickPrefab(HexFeatureCollection[] collection, int level, float hash, float choice)
    {
        if (level > 0 && collection != null && collection.Length >= level)
        {
            float[] thresholds = HexMetrics.GetFeatureThresholds(level - 1);
            for (int i = 0; i < thresholds.Length; i++)
            {
                if (hash < thresholds[i])
                {
                    return collection[i].Pick(choice);
                }
            }
        }
        return null;
    }

    /// <summary>Part 9: 在指定位置为一个单元格添加地形特征</summary>
    public void AddFeature(HexCell cell, Vector3 position)
    {
        if (_urbanCollections == null || _farmCollections == null || _plantCollections == null) return;

        /* Part 9: 采样确定性哈希 */
        HexHash hash = HexMetrics.SampleHash(position);

        /* Part 9: 分别查询三类特征 */
        PackedScene urban = PickPrefab(_urbanCollections, cell.UrbanLevel, hash.A, hash.D);
        PackedScene farm = PickPrefab(_farmCollections, cell.FarmLevel, hash.B, hash.D);
        PackedScene plant = PickPrefab(_plantCollections, cell.PlantLevel, hash.C, hash.D);

        /* Part 9: 选择最终放置的特征（优先城市；若农场更优则覆盖；若植物哈希值更低则用植物） */
        PackedScene prefab = urban;
        float selectedHash = hash.A;

        if (farm != null)
        {
            prefab = farm;
            selectedHash = hash.B;
        }
        if (plant != null && hash.C < selectedHash)
        {
            prefab = plant;
        }

        if (prefab == null) return;

        /* Part 9: 实例化特征节点 */
        Node3D instance = prefab.Instantiate<Node3D>();

        /* Part 9: 设置位置：Y 偏移一半高度使底部贴地 */
        position.Y += instance.Scale.Y * 0.5f;
        instance.Position = HexMetrics.Perturb(position);

        /* Part 9: 随机绕 Y 轴旋转 */
        instance.Rotation = new Vector3(0f, 360f * hash.E, 0f);

        AddChild(instance);
    }
}
