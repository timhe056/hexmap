using System.Collections.Generic;

namespace HexMap;

/// <summary>
/// Part 16 / 3.2.0: 基于桶数组 + 链表链接的优先队列，用于 A* 路径查找。
/// 3.2.0 改为只存储 cell 索引，依赖 HexGrid 的 SearchData 数组。
/// </summary>
public class HexCellPriorityQueue
{
    private readonly HexGrid _grid;
    private readonly List<int> _list = new();
    private int _minimum = int.MaxValue;

    public HexCellPriorityQueue(HexGrid grid)
    {
        _grid = grid;
    }

    public void Enqueue(int cellIndex)
    {
        int priority = _grid.SearchData[cellIndex].SearchPriority;
        if (priority < _minimum)
        {
            _minimum = priority;
        }
        while (priority >= _list.Count)
        {
            _list.Add(-1);
        }
        _grid.SearchData[cellIndex].nextWithSamePriority = _list[priority];
        _list[priority] = cellIndex;
    }

    public bool TryDequeue(out int cellIndex)
    {
        for (; _minimum < _list.Count; _minimum++)
        {
            int index = _list[_minimum];
            if (index >= 0)
            {
                _list[_minimum] = _grid.SearchData[index].nextWithSamePriority;
                cellIndex = index;
                return true;
            }
        }
        cellIndex = -1;
        return false;
    }

    public void Change(int cellIndex, int oldPriority)
    {
        /* 防御：oldPriority 越界、该 priority 下无元素、或 cell 已不在队列中 */
        if (oldPriority < 0 || oldPriority >= _list.Count || _list[oldPriority] < 0)
        {
            Enqueue(cellIndex);
            return;
        }

        int current = _list[oldPriority];
        int next = _grid.SearchData[current].nextWithSamePriority;
        if (current == cellIndex)
        {
            _list[oldPriority] = next;
        }
        else
        {
            while (next != cellIndex && next >= 0)
            {
                current = next;
                next = _grid.SearchData[current].nextWithSamePriority;
            }
            if (next < 0)
            {
                /* cell 不在该 priority 链表中，直接重新 enqueue */
                Enqueue(cellIndex);
                return;
            }
            _grid.SearchData[current].nextWithSamePriority = _grid.SearchData[cellIndex].nextWithSamePriority;
        }
        Enqueue(cellIndex);
    }

    public void Clear()
    {
        _list.Clear();
        _minimum = int.MaxValue;
    }
}
