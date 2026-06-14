using Godot;
using System.Collections.Generic;

namespace HexMap;

/// <summary>
/// Part 9-10: 六边形地形特征管理器。
/// 作为 HexGridChunk 的子节点，管理地形特征（Urban/Farm/Plant）和城墙（Walls）。
/// </summary>
public partial class HexFeatureManager : Node3D
{
    /* Part 2.1: 特征 prefab 集合，仅由本类使用，故嵌套在内部 */
    [System.Serializable]
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

    /* Part 9: 三种特征类型的 prefab 集合数组（每级含多个变体） */
    private HexFeatureCollection[] _urbanCollections;
    private HexFeatureCollection[] _farmCollections;
    private HexFeatureCollection[] _plantCollections;

    /* Part 10: 城墙 mesh 实例（由 HexGridChunk 设置） */
    public MeshInstance3D WallsMeshInstance { get; set; }

    /* Part 10: 城墙顶点数据 */
    private List<Vector3> _wallVertices;
    private List<int> _wallTriangles;

    /* Part 11: 城墙塔楼、桥梁、特殊特征 prefab */
    private PackedScene _wallTowerPrefab;
    private PackedScene _bridgePrefab;
    private PackedScene[] _specialPrefabs;

    /* Part 2.0: 特征 shader 材质（共享基材质 + 按颜色缓存） */
    private static ShaderMaterial _featureBaseMaterial;
    private static readonly Dictionary<Color, ShaderMaterial> FeatureMaterialCache = new();

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

    public void SetWallTower(PackedScene prefab) => _wallTowerPrefab = prefab;
    public void SetBridge(PackedScene prefab) => _bridgePrefab = prefab;
    public void SetSpecialPrefabs(PackedScene[] prefabs) => _specialPrefabs = prefabs;

    /// <summary>Part 2.0: 为实例化的特征应用 feature shader，使其受战争迷雾影响。</summary>
    private static void ApplyFeatureMaterial(Node3D feature)
    {
        if (_featureBaseMaterial == null)
        {
            _featureBaseMaterial = ResourceLoader.Load<ShaderMaterial>("res://assets/materials/feature.tres");
        }

        ApplyFeatureMaterialRecursive(feature);
    }

    private static void ApplyFeatureMaterialRecursive(Node node)
    {
        if (node is MeshInstance3D mesh)
        {
            Material current = mesh.GetSurfaceOverrideMaterial(0);
            if (current is not ShaderMaterial)
            {
                Color color = Colors.White;
                if (current is StandardMaterial3D standard)
                {
                    color = standard.AlbedoColor;
                }

                if (!FeatureMaterialCache.TryGetValue(color, out ShaderMaterial mat))
                {
                    mat = (ShaderMaterial)_featureBaseMaterial.Duplicate();
                    mat.SetShaderParameter("feature_color", color);
                    FeatureMaterialCache[color] = mat;
                }

                mesh.SetSurfaceOverrideMaterial(0, mat);
            }
        }

        foreach (Node child in node.GetChildren())
        {
            ApplyFeatureMaterialRecursive(child);
        }
    }

    /// <summary>Part 9-10: 清除所有已放置的特征节点和城墙数据</summary>
    public void Clear()
    {
        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }

        _wallVertices = new List<Vector3>();
        _wallTriangles = new List<int>();
    }

    /// <summary>Part 9-10: 提交城墙 mesh</summary>
    public void Apply()
    {
        if (_wallVertices == null || _wallVertices.Count == 0)
        {
            if (WallsMeshInstance != null) WallsMeshInstance.Mesh = new ArrayMesh();
            return;
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = _wallVertices.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = _wallTriangles.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        if (WallsMeshInstance != null) WallsMeshInstance.Mesh = mesh;
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
    public void AddFeature(HexCellData cell, Vector3 position)
    {
        if (cell.IsSpecial) return;
        if (_urbanCollections == null || _farmCollections == null || _plantCollections == null) return;

        /* Part 9: 采样确定性哈希 */
        HexHash hash = HexMetrics.SampleHash(position);

        /* Part 9: 分别查询三类特征 */
        PackedScene urban = PickPrefab(_urbanCollections, cell.UrbanLevel, hash.A, hash.D);
        PackedScene farm = PickPrefab(_farmCollections, cell.FarmLevel, hash.B, hash.D);
        PackedScene plant = PickPrefab(_plantCollections, cell.PlantLevel, hash.C, hash.D);

        /* Part 9: 选择最终放置的特征（匹配 Unity cascade 逻辑） */
        PackedScene prefab = urban;
        float selectedHash = hash.A;

        if (prefab != null)
        {
            if (farm != null && hash.B < hash.A)
            {
                prefab = farm;
                selectedHash = hash.B;
            }
        }
        else if (farm != null)
        {
            prefab = farm;
            selectedHash = hash.B;
        }

        /* Part 11: 植物与已选特征竞争（与 tut 10-add 算法一致） */
        PackedScene otherPrefab = plant;
        if (prefab != null)
        {
            if (otherPrefab != null && hash.C < selectedHash)
            {
                prefab = otherPrefab;
            }
        }
        else if (otherPrefab != null)
        {
            prefab = otherPrefab;
        }
        else
        {
            return;
        }

        /* Part 9: 实例化特征节点 */
        Node3D instance = prefab.Instantiate<Node3D>();

        /* Part 9: 设置位置：Y 偏移一半高度使底部贴地 */
        position.Y += instance.Scale.Y * 0.5f;
        instance.Position = HexMetrics.Perturb(position);

        /* Part 9: 随机绕 Y 轴旋转 */
        instance.Rotation = new Vector3(0f, 360f * hash.E, 0f);

        ApplyFeatureMaterial(instance);
        AddChild(instance);
    }

    // ==================== Part 10: 城墙系统 ====================

    /// <summary>
    /// Part 10: 在连接处添加城墙段。
    /// near/far 为两侧 cell 的边缘顶点，hasRiver/hasRoad 控制是否断开。
    /// </summary>
    public void AddWall(
        HexMeshBuilder.EdgeVertices near, HexCellData nearCell,
        HexMeshBuilder.EdgeVertices far, HexCellData farCell,
        bool hasRiver, bool hasRoad)
    {
        if (
            nearCell.Walled != farCell.Walled &&
            !nearCell.IsUnderwater && !farCell.IsUnderwater &&
            nearCell.GetEdgeType(farCell) != HexEdgeType.Cliff
        )
        {
            AddWallSegment(near.v1, far.v1, near.v2, far.v2);
            if (hasRiver || hasRoad)
            {
                AddWallCap(near.v2, far.v2);
                AddWallCap(far.v4, near.v4);
            }
            else
            {
                AddWallSegment(near.v2, far.v2, near.v3, far.v3);
                AddWallSegment(near.v3, far.v3, near.v4, far.v4);
            }
            AddWallSegment(near.v4, far.v4, near.v5, far.v5);
        }
    }

    /// <summary>
    /// Part 10: 在拐角处添加城墙。
    /// 根据三个 cell 的 Walled 状态组合决定放置哪个 segment。
    /// </summary>
    public void AddWall(
        Vector3 c1, HexCellData cell1,
        Vector3 c2, HexCellData cell2,
        Vector3 c3, HexCellData cell3)
    {
        if (cell1.Walled)
        {
            if (cell2.Walled)
            {
                if (!cell3.Walled)
                {
                    AddWallSegment(c3, cell3, c1, cell1, c2, cell2);
                }
            }
            else if (cell3.Walled)
            {
                AddWallSegment(c2, cell2, c3, cell3, c1, cell1);
            }
            else
            {
                AddWallSegment(c1, cell1, c2, cell2, c3, cell3);
            }
        }
        else if (cell2.Walled)
        {
            if (cell3.Walled)
            {
                AddWallSegment(c1, cell1, c2, cell2, c3, cell3);
            }
            else
            {
                AddWallSegment(c2, cell2, c3, cell3, c1, cell1);
            }
        }
        else if (cell3.Walled)
        {
            AddWallSegment(c3, cell3, c1, cell1, c2, cell2);
        }
    }

    /* Part 10: 标准城墙段（4 个顶点的四边形拉伸为带厚度的墙体） */
    private void AddWallSegment(
        Vector3 nearLeft, Vector3 farLeft,
        Vector3 nearRight, Vector3 farRight,
        bool addTower = false)
    {
        nearLeft = HexMetrics.Perturb(nearLeft);
        farLeft = HexMetrics.Perturb(farLeft);
        nearRight = HexMetrics.Perturb(nearRight);
        farRight = HexMetrics.Perturb(farRight);

        Vector3 left = HexMetrics.WallLerp(nearLeft, farLeft);
        Vector3 right = HexMetrics.WallLerp(nearRight, farRight);

        Vector3 leftThicknessOffset = HexMetrics.WallThicknessOffset(nearLeft, farLeft);
        Vector3 rightThicknessOffset = HexMetrics.WallThicknessOffset(nearRight, farRight);

        float leftTop = left.Y + HexMetrics.WallHeight;
        float rightTop = right.Y + HexMetrics.WallHeight;

        Vector3 v1, v2, v3, v4;
        v1 = v3 = left - leftThicknessOffset;
        v2 = v4 = right - rightThicknessOffset;
        v3.Y = leftTop;
        v4.Y = rightTop;
        AddWallQuad(v1, v2, v3, v4);

        Vector3 t1 = v3, t2 = v4;

        v1 = v3 = left + leftThicknessOffset;
        v2 = v4 = right + rightThicknessOffset;
        v3.Y = leftTop;
        v4.Y = rightTop;
        AddWallQuad(v2, v1, v4, v3);

        AddWallQuad(t1, t2, v3, v4);

        /* Part 11: 放置城墙塔楼 */
        if (addTower && _wallTowerPrefab != null)
        {
            var tower = _wallTowerPrefab.Instantiate<Node3D>();
            tower.Position = (left + right) * 0.5f;
            Vector3 rightDir = right - left;
            rightDir.Y = 0f;
            // Unity: transform.right = rightDirection  → 局部X轴指向rightDir
            // Godot: Y旋转θ后，局部X = (cosθ, 0, -sinθ)，令其等于rightDir归一化方向
            tower.Rotation = new Vector3(0f, Mathf.Atan2(-rightDir.Z, rightDir.X), 0f);
            ApplyFeatureMaterial(tower);
            AddChild(tower);
        }
    }

    /* Part 10: 拐角城墙段（处理楔形/封口） */
    private void AddWallSegment(
        Vector3 pivot, HexCellData pivotCell,
        Vector3 left, HexCellData leftCell,
        Vector3 right, HexCellData rightCell)
    {
        if (pivotCell.IsUnderwater) return;

        bool hasLeftWall = !leftCell.IsUnderwater &&
            pivotCell.GetEdgeType(leftCell) != HexEdgeType.Cliff;
        bool hasRightWall = !rightCell.IsUnderwater &&
            pivotCell.GetEdgeType(rightCell) != HexEdgeType.Cliff;

        if (hasLeftWall)
        {
            if (hasRightWall)
            {
                bool hasTower = false;
                if (leftCell.Elevation == rightCell.Elevation)
                {
                    HexHash hash = HexMetrics.SampleHash((pivot + left + right) * (1f / 3f));
                    hasTower = hash.E < HexMetrics.WallTowerThreshold;
                }
                AddWallSegment(pivot, left, pivot, right, hasTower);
            }
            else if (leftCell.Elevation < rightCell.Elevation)
            {
                AddWallWedge(pivot, left, right);
            }
            else
            {
                AddWallCap(pivot, left);
            }
        }
        else if (hasRightWall)
        {
            if (rightCell.Elevation < leftCell.Elevation)
            {
                AddWallWedge(right, pivot, left);
            }
            else
            {
                AddWallCap(right, pivot);
            }
        }
    }

    /* Part 10: 城墙封口（河流/道路处断开） */
    private void AddWallCap(Vector3 near, Vector3 far)
    {
        near = HexMetrics.Perturb(near);
        far = HexMetrics.Perturb(far);

        Vector3 center = HexMetrics.WallLerp(near, far);
        Vector3 thickness = HexMetrics.WallThicknessOffset(near, far);

        Vector3 v1, v2, v3, v4;
        v1 = v3 = center - thickness;
        v2 = v4 = center + thickness;
        v3.Y = v4.Y = center.Y + HexMetrics.WallHeight;
        AddWallQuad(v1, v2, v3, v4);
    }

    /* Part 10: 楔形填充（城墙与悬崖交接） */
    private void AddWallWedge(Vector3 near, Vector3 far, Vector3 point)
    {
        near = HexMetrics.Perturb(near);
        far = HexMetrics.Perturb(far);
        point = HexMetrics.Perturb(point);

        Vector3 center = HexMetrics.WallLerp(near, far);
        Vector3 thickness = HexMetrics.WallThicknessOffset(near, far);

        Vector3 v1, v2, v3, v4;
        Vector3 pointTop = point;
        point.Y = center.Y;

        v1 = v3 = center - thickness;
        v2 = v4 = center + thickness;
        v3.Y = v4.Y = pointTop.Y = center.Y + HexMetrics.WallHeight;

        AddWallQuad(v1, point, v3, pointTop);
        AddWallQuad(point, v2, pointTop, v4);
        AddWallTriangle(pointTop, v3, v4);
    }

    /* Part 10: 辅助：向城墙 mesh 添加四边形（与 Unity AddQuadUnperturbed 顶点顺序对齐，拐角处非平面四边形使用相同对角线） */
    private void AddWallQuad(Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4)
    {
        int vi = _wallVertices.Count;
        _wallVertices.Add(v1);
        _wallVertices.Add(v2);
        _wallVertices.Add(v3);
        _wallVertices.Add(v4);
        // Tri 1: v1, v3, v2 — 对角线 v3-v2（左上到右下），与 Unity 一致
        _wallTriangles.Add(vi);
        _wallTriangles.Add(vi + 2);
        _wallTriangles.Add(vi + 1);
        // Tri 2: v2, v3, v4
        _wallTriangles.Add(vi + 1);
        _wallTriangles.Add(vi + 2);
        _wallTriangles.Add(vi + 3);
    }

    /* Part 10: 辅助：向城墙 mesh 添加三角形（无颜色、无扰动） */
    private void AddWallTriangle(Vector3 v1, Vector3 v2, Vector3 v3)
    {
        int vi = _wallVertices.Count;
        _wallVertices.Add(v1);
        _wallVertices.Add(v2);
        _wallVertices.Add(v3);
        _wallTriangles.Add(vi);
        _wallTriangles.Add(vi + 1);
        _wallTriangles.Add(vi + 2);
    }

    // ==================== Part 11: 桥梁 ====================

    public void AddBridge(Vector3 roadCenter1, Vector3 roadCenter2)
    {
        if (_bridgePrefab == null) return;
        roadCenter1 = HexMetrics.Perturb(roadCenter1);
        roadCenter2 = HexMetrics.Perturb(roadCenter2);
        var instance = _bridgePrefab.Instantiate<Node3D>();
        ApplyFeatureMaterial(instance);
        AddChild(instance);
        instance.Position = (roadCenter1 + roadCenter2) * 0.5f;
        instance.LookAt(roadCenter2, Vector3.Up, true);
        float length = roadCenter1.DistanceTo(roadCenter2);
        instance.Scale = new Vector3(1f, 1f, length * (1f / HexMetrics.BridgeDesignLength));
    }

    // ==================== Part 11: 特殊特征 ====================

    public void AddSpecialFeature(HexCellData cell, Vector3 position)
    {
        if (_specialPrefabs == null || cell.SpecialIndex <= 0 || cell.SpecialIndex > _specialPrefabs.Length) return;
        HexHash hash = HexMetrics.SampleHash(position);
        var instance = _specialPrefabs[cell.SpecialIndex - 1].Instantiate<Node3D>();
        instance.Position = HexMetrics.Perturb(position);
        instance.RotateY(2f * Mathf.Pi * hash.E);
        ApplyFeatureMaterial(instance);
        AddChild(instance);
    }
}
