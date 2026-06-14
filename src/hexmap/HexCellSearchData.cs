namespace HexMap;

/// <summary>
/// Part 3.2.0: 搜索/路径查找时使用的临时 cell 数据。
/// 由 HexGrid 统一持有数组，不再保存在每个 HexCell 中。
/// </summary>
public struct HexCellSearchData
{
    /// <summary>从起点到该 cell 的最短距离。</summary>
    public int distance;

    /// <summary>同优先级链表的下一个 cell 索引。</summary>
    public int nextWithSamePriority;

    /// <summary>路径中到达该 cell 的上一个 cell 索引。</summary>
    public int pathFrom;

    /// <summary>A* 启发值。</summary>
    public int heuristic;

    /// <summary>搜索阶段标记，避免每次全局重置。</summary>
    public int searchPhase;

    /// <summary>搜索优先级 = distance + heuristic。</summary>
    public readonly int SearchPriority => distance + heuristic;
}
