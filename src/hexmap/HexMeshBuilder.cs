using Godot;
using System.Collections.Generic;

namespace HexMap;

/// <summary>
/// Part 5+8：六边形网格三角化器。
/// 对应教程中的 HexMesh，负责将一组 HexCell 三角化为 Godot Mesh。
/// 使用 ArrayMesh 列表式构建，与 Unity 教程的 HexMesh 结构对齐。
/// 所有方法均为静态，不依赖任何 Node 实例状态。
/// </summary>
public static class HexMeshBuilder
{
    public struct EdgeVertices
    {
        public Vector3 v1, v2, v3, v4, v5;

        public EdgeVertices(Vector3 corner1, Vector3 corner2)
        {
            v1 = corner1;
            v2 = corner1.Lerp(corner2, 0.25f);
            v3 = corner1.Lerp(corner2, 0.5f);
            v4 = corner1.Lerp(corner2, 0.75f);
            v5 = corner2;
        }

        public EdgeVertices(Vector3 corner1, Vector3 corner2, float outerStep)
        {
            v1 = corner1;
            v2 = corner1.Lerp(corner2, outerStep);
            v3 = corner1.Lerp(corner2, 0.5f);
            v4 = corner1.Lerp(corner2, 1f - outerStep);
            v5 = corner2;
        }

        public static EdgeVertices TerraceLerp(EdgeVertices a, EdgeVertices b, int step)
        {
            EdgeVertices result;
            result.v1 = HexMetrics.TerraceLerp(a.v1, b.v1, step);
            result.v2 = HexMetrics.TerraceLerp(a.v2, b.v2, step);
            result.v3 = HexMetrics.TerraceLerp(a.v3, b.v3, step);
            result.v4 = HexMetrics.TerraceLerp(a.v4, b.v4, step);
            result.v5 = HexMetrics.TerraceLerp(a.v5, b.v5, step);
            return result;
        }
    }

    // ==================== MeshData ====================

    /// <summary>封装单个 mesh 的顶点数据列表，与 Unity 教程 HexMesh 对齐。</summary>
    private class MeshData
    {
        public readonly bool UseColors;
        public readonly bool UseUV;
        public readonly bool UseUV2;

        public List<Vector3> Vertices = new List<Vector3>();
        public List<int> Triangles = new List<int>();
        public List<Color> Colors;
        public List<Vector2> UVs;
        public List<Vector2> UV2s;

        public MeshData(bool useColors = false, bool useUV = false, bool useUV2 = false)
        {
            UseColors = useColors;
            UseUV = useUV;
            UseUV2 = useUV2;
            if (useColors) Colors = new List<Color>();
            if (useUV) UVs = new List<Vector2>();
            if (useUV2) UV2s = new List<Vector2>();
        }

        public Mesh ToMesh()
        {
            if (Vertices.Count == 0) return new ArrayMesh();

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = Vertices.ToArray();
            arrays[(int)Mesh.ArrayType.Index] = Triangles.ToArray();
            if (UseColors) arrays[(int)Mesh.ArrayType.Color] = Colors.ToArray();
            if (UseUV) arrays[(int)Mesh.ArrayType.TexUV] = UVs.ToArray();
            if (UseUV2) arrays[(int)Mesh.ArrayType.TexUV2] = UV2s.ToArray();

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            // 使用 SurfaceTool 生成法线（Godot 4 ArrayMesh 无直接生成法线 API）
            var st = new SurfaceTool();
            st.CreateFrom(mesh, 0);
            st.GenerateNormals();
            return st.Commit();
        }
    }

    // ==================== 公共入口 ====================

    /* Part 8: 六路输出：terrain, rivers, roads, water, waterShore, estuaries */
    public static void BuildMeshes(HexCell[] cells,
        out Mesh terrainMesh, out Mesh riverMesh, out Mesh roadMesh,
        out Mesh waterMesh, out Mesh waterShoreMesh, out Mesh estuaryMesh)
    {
        var terrain = new MeshData(useColors: true);
        var rivers = new MeshData(useUV: true);
        var roads = new MeshData(useUV: true);
        var water = new MeshData();
        var waterShore = new MeshData(useUV: true);
        var estuaries = new MeshData(useUV: true, useUV2: true);

        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null)
                TriangulateCell(cells[i], terrain, rivers, roads, water, waterShore, estuaries);
        }

        terrainMesh = terrain.ToMesh();
        riverMesh = rivers.ToMesh();
        roadMesh = roads.ToMesh();
        waterMesh = water.ToMesh();
        waterShoreMesh = waterShore.ToMesh();
        estuaryMesh = estuaries.ToMesh();
    }

    public static Mesh BuildMesh(HexCell[] cells)
    {
        BuildMeshes(cells, out Mesh terrainMesh, out _, out _, out _, out _, out _);
        return terrainMesh;
    }

    // ==================== Cell / Sector ====================

    private static void TriangulateCell(HexCell cell, MeshData terrain, MeshData rivers, MeshData roads,
        MeshData water, MeshData waterShore, MeshData estuaries)
    {
        for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
        {
            Triangulate(terrain, rivers, roads, water, waterShore, estuaries, d, cell);
        }
    }

    private static void Triangulate(MeshData terrain, MeshData rivers, MeshData roads,
        MeshData water, MeshData waterShore, MeshData estuaries, HexDirection direction, HexCell cell)
    {
        Vector3 center = cell.Position;
        EdgeVertices e = new EdgeVertices(
            center + HexMetrics.GetFirstSolidCorner(direction),
            center + HexMetrics.GetSecondSolidCorner(direction)
        );

        if (cell.HasRiver)
        {
            if (cell.HasRiverThroughEdge(direction))
            {
                e.v3 = new Vector3(e.v3.X, cell.StreamBedY, e.v3.Z);
                if (cell.HasRiverBeginOrEnd)
                {
                    TriangulateWithRiverBeginOrEnd(terrain, rivers, direction, cell, center, e);
                }
                else
                {
                    TriangulateWithRiver(terrain, rivers, direction, cell, center, e);
                }
            }
            else
            {
                /* Part 7: 透传 roads */
                TriangulateAdjacentToRiver(terrain, rivers, roads, direction, cell, center, e);
            }
        }
        else
        {
            /* Part 7: 使用道路感知的三角化方法 */
            TriangulateWithoutRiver(terrain, roads, direction, cell, center, e);
        }

        if (direction <= HexDirection.SE)
        {
            /* Part 7: 透传 roads */
            TriangulateConnection(terrain, rivers, roads, direction, cell, e);
        }

        /* Part 8: 开放水面三角化 */
        if (cell.IsUnderwater)
        {
            TriangulateOpenWater(direction, cell, water, waterShore, estuaries);
        }
    }

    // ==================== 顶点扰动 ====================

    private static Vector3 Perturb(Vector3 position)
    {
        Vector4 sample = HexMetrics.SampleNoise(position);
        position.X += (sample.X * 2f - 1f) * HexMetrics.CellPerturbStrength;
        position.Z += (sample.Z * 2f - 1f) * HexMetrics.CellPerturbStrength;
        return position;
    }

    // ==================== 基础图元 ====================

    private static void AddTriangle(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3)
    {
        int vi = md.Vertices.Count;
        md.Vertices.Add(Perturb(v1));
        md.Vertices.Add(Perturb(v2));
        md.Vertices.Add(Perturb(v3));
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 1);
        md.Triangles.Add(vi + 2);
    }

    private static void AddTriangleColor(MeshData md, Color c1, Color c2, Color c3)
    {
        if (md.UseColors)
        {
            md.Colors.Add(c1);
            md.Colors.Add(c2);
            md.Colors.Add(c3);
        }
    }

    private static void AddTriangleUV(MeshData md, Vector2 uv1, Vector2 uv2, Vector2 uv3)
    {
        if (md.UseUV)
        {
            md.UVs.Add(uv1);
            md.UVs.Add(uv2);
            md.UVs.Add(uv3);
        }
    }

    private static void AddTriangleUV2(MeshData md, Vector2 uv1, Vector2 uv2, Vector2 uv3)
    {
        if (md.UseUV2)
        {
            md.UV2s.Add(uv1);
            md.UV2s.Add(uv2);
            md.UV2s.Add(uv3);
        }
    }

    private static void AddTriangleUnperturbed(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3,
        Color c1, Color c2, Color c3)
    {
        int vi = md.Vertices.Count;
        md.Vertices.Add(v1);
        md.Vertices.Add(v2);
        md.Vertices.Add(v3);
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 1);
        md.Triangles.Add(vi + 2);
        if (md.UseColors)
        {
            md.Colors.Add(c1);
            md.Colors.Add(c2);
            md.Colors.Add(c3);
        }
    }

    private static void AddTriangle(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3,
        Color c1, Color c2, Color c3)
    {
        AddTriangle(md, v1, v2, v3);
        AddTriangleColor(md, c1, c2, c3);
    }

    private static void AddQuad(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4)
    {
        int vi = md.Vertices.Count;
        md.Vertices.Add(Perturb(v1));
        md.Vertices.Add(Perturb(v4));
        md.Vertices.Add(Perturb(v2));
        md.Vertices.Add(Perturb(v3));
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 1);
        md.Triangles.Add(vi + 2);
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 3);
        md.Triangles.Add(vi + 1);
    }

    private static void AddQuadColor(MeshData md, Color c1, Color c2, Color c3, Color c4)
    {
        if (md.UseColors)
        {
            md.Colors.Add(c1);
            md.Colors.Add(c4);
            md.Colors.Add(c2);
            md.Colors.Add(c3);
        }
    }

    private static void AddQuadUV(MeshData md,
        Vector2 uv1, Vector2 uv2, Vector2 uv3, Vector2 uv4)
    {
        if (md.UseUV)
        {
            md.UVs.Add(uv1);
            md.UVs.Add(uv4);
            md.UVs.Add(uv2);
            md.UVs.Add(uv3);
        }
    }

    private static void AddQuadUV2(MeshData md,
        Vector2 uv1, Vector2 uv2, Vector2 uv3, Vector2 uv4)
    {
        if (md.UseUV2)
        {
            md.UV2s.Add(uv1);
            md.UV2s.Add(uv4);
            md.UV2s.Add(uv2);
            md.UV2s.Add(uv3);
        }
    }

    private static void AddQuadUV(MeshData md, float uA, float vA, float uB, float vB)
    {
        AddQuadUV(md,
            new Vector2(uA, vA), new Vector2(uA, vA),
            new Vector2(uB, vB), new Vector2(uB, vB));
    }

    private static void AddQuadUnperturbed(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        Color c1, Color c2, Color c3, Color c4)
    {
        int vi = md.Vertices.Count;
        md.Vertices.Add(v1);
        md.Vertices.Add(v4);
        md.Vertices.Add(v2);
        md.Vertices.Add(v3);
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 1);
        md.Triangles.Add(vi + 2);
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 3);
        md.Triangles.Add(vi + 1);
        if (md.UseColors)
        {
            md.Colors.Add(c1);
            md.Colors.Add(c4);
            md.Colors.Add(c2);
            md.Colors.Add(c3);
        }
    }

    private static void AddQuad(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        Color c1, Color c2, Color c3, Color c4)
    {
        AddQuad(md, v1, v2, v3, v4);
        AddQuadColor(md, c1, c2, c3, c4);
    }

    /* Part 7: 道路网格使用 UV 而非顶点颜色 */
    private static void AddRoadQuad(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, float uMin, float uMax)
    {
        int vi = md.Vertices.Count;
        md.Vertices.Add(Perturb(v1));
        md.Vertices.Add(Perturb(v4));
        md.Vertices.Add(Perturb(v2));
        md.Vertices.Add(Perturb(v3));
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 1);
        md.Triangles.Add(vi + 2);
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 3);
        md.Triangles.Add(vi + 1);
        if (md.UseUV)
        {
            md.UVs.Add(new Vector2(uMin, 0f));
            md.UVs.Add(new Vector2(uMax, 0f));
            md.UVs.Add(new Vector2(uMax, 0f));
            md.UVs.Add(new Vector2(uMin, 0f));
        }
    }

    private static void AddRoadTriangle(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, Vector2 uv1, Vector2 uv2, Vector2 uv3)
    {
        int vi = md.Vertices.Count;
        md.Vertices.Add(Perturb(v1));
        md.Vertices.Add(Perturb(v3));
        md.Vertices.Add(Perturb(v2));
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 1);
        md.Triangles.Add(vi + 2);
        if (md.UseUV)
        {
            md.UVs.Add(uv1);
            md.UVs.Add(uv3);
            md.UVs.Add(uv2);
        }
    }

    // ==================== Edge ====================

    private static void TriangulateEdgeFan(MeshData md, Vector3 center, EdgeVertices edge, Color color)
    {
        AddTriangle(md, center, edge.v1, edge.v2);
        AddTriangleColor(md, color, color, color);
        AddTriangle(md, center, edge.v2, edge.v3);
        AddTriangleColor(md, color, color, color);
        AddTriangle(md, center, edge.v3, edge.v4);
        AddTriangleColor(md, color, color, color);
        AddTriangle(md, center, edge.v4, edge.v5);
        AddTriangleColor(md, color, color, color);
    }

    /* Part 7: 新增 hasRoad 参数，有道路时在中间 quad 段画路面 */
    private static void TriangulateEdgeStrip(MeshData md, EdgeVertices e1, Color c1, EdgeVertices e2, Color c2, bool hasRoad = false)
    {
        AddQuad(md, e1.v1, e1.v2, e2.v1, e2.v2, c1, c1, c2, c2);
        AddQuad(md, e1.v2, e1.v3, e2.v2, e2.v3, c1, c1, c2, c2);
        AddQuad(md, e1.v3, e1.v4, e2.v3, e2.v4, c1, c1, c2, c2);
        AddQuad(md, e1.v4, e1.v5, e2.v4, e2.v5, c1, c1, c2, c2);
    }

    /* Part 7: 新增 roads 参数，在连接处传入道路信息 */
    private static void TriangulateConnection(MeshData terrain, MeshData rivers, MeshData roads, HexDirection direction, HexCell cell, EdgeVertices e1)
    {
        HexCell neighbor = cell.GetNeighbor(direction);
        if (neighbor == null) return;

        Vector3 bridge = HexMetrics.GetBridge(direction);
        bridge.Y = neighbor.Position.Y - cell.Position.Y;
        EdgeVertices e2 = new EdgeVertices(
            e1.v1 + bridge,
            e1.v5 + bridge
        );

        // Part 6: 河流通过连接
        if (cell.HasRiverThroughEdge(direction))
        {
            e2.v3 = new Vector3(e2.v3.X, neighbor.StreamBedY, e2.v3.Z);
            /* Part 8: 水下隐藏河流 / 瀑布 */
            if (!cell.IsUnderwater)
            {
                if (!neighbor.IsUnderwater)
                {
                    TriangulateRiverQuad(rivers,
                        e1.v2, e1.v4, e2.v2, e2.v4,
                        cell.RiverSurfaceY, neighbor.RiverSurfaceY, 0.8f,
                        cell.HasIncomingRiver && cell.IncomingRiver == direction
                    );
                }
                /* 按教程：仅当 cell 海拔高于 neighbor 水位时才画瀑布 */
                else if (cell.Elevation > neighbor.WaterLevel)
                {
                    /* 瀑布：cell 在水上，neighbor 在水下 */
                    TriangulateWaterfallInWater(rivers,
                        e1.v2, e1.v4, e2.v2, e2.v4,
                        cell.RiverSurfaceY, neighbor.RiverSurfaceY, neighbor.WaterSurfaceY);
                }
            }
            /* 按教程：反向瀑布需要 neighbor 海拔高于 cell 水位 */
            else if (!neighbor.IsUnderwater && neighbor.Elevation > cell.WaterLevel)
            {
                /* 反向瀑布：neighbor 在水上，cell 在水下 */
                TriangulateWaterfallInWater(rivers,
                    e2.v4, e2.v2, e1.v4, e1.v2,
                    neighbor.RiverSurfaceY, cell.RiverSurfaceY, cell.WaterSurfaceY);
            }
        }

        /* Part 7: 获取道路信息 */
        bool hasRoad = cell.HasRoadThroughEdge(direction);

        if (cell.GetEdgeType(direction) == HexEdgeType.Slope)
        {
            TriangulateEdgeTerraces(terrain, e1, cell, e2, neighbor);
        }
        else
        {
            TriangulateEdgeStrip(terrain, e1, cell.Color, e2, neighbor.Color);
        }

        /* Part 7: 道路单独画到 roads 网格 */
        if (hasRoad)
        {
            TriangulateRoadSegment(roads, e1.v2, e1.v3, e1.v4, e2.v2, e2.v3, e2.v4);
        }

        HexCell nextNeighbor = cell.GetNeighbor(direction.Next());
        if (direction <= HexDirection.E && nextNeighbor != null)
        {
            Vector3 v5 = e1.v5 + HexMetrics.GetBridge(direction.Next());
            v5.Y = nextNeighbor.Position.Y;

            if (cell.Elevation <= neighbor.Elevation)
            {
                if (cell.Elevation <= nextNeighbor.Elevation)
                {
                    TriangulateCorner(terrain, e1.v5, cell, e2.v5, neighbor, v5, nextNeighbor);
                }
                else
                {
                    TriangulateCorner(terrain, v5, nextNeighbor, e1.v5, cell, e2.v5, neighbor);
                }
            }
            else if (neighbor.Elevation <= nextNeighbor.Elevation)
            {
                TriangulateCorner(terrain, e2.v5, neighbor, v5, nextNeighbor, e1.v5, cell);
            }
            else
            {
                TriangulateCorner(terrain, v5, nextNeighbor, e1.v5, cell, e2.v5, neighbor);
            }
        }
    }

    /* Part 7: 新增 hasRoad 参数，透传到 TriangulateEdgeStrip */
    private static void TriangulateEdgeTerraces(MeshData md,
        EdgeVertices begin, HexCell beginCell,
        EdgeVertices end, HexCell endCell)
    {
        EdgeVertices e2 = EdgeVertices.TerraceLerp(begin, end, 1);
        Color c2 = HexMetrics.TerraceLerp(beginCell.Color, endCell.Color, 1);

        TriangulateEdgeStrip(md, begin, beginCell.Color, e2, c2);

        for (int i = 2; i < HexMetrics.TerraceSteps; i++)
        {
            EdgeVertices e1 = e2;
            Color c1 = c2;
            e2 = EdgeVertices.TerraceLerp(begin, end, i);
            c2 = HexMetrics.TerraceLerp(beginCell.Color, endCell.Color, i);
            TriangulateEdgeStrip(md, e1, c1, e2, c2);
        }

        TriangulateEdgeStrip(md, e2, c2, end, endCell.Color);
    }

    /* Part 7: 有道路时在河流相邻侧画道路 */
    private static void TriangulateRoadAdjacentToRiver(MeshData terrain, MeshData roads, HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e)
    {
        bool hasRoadThroughEdge = cell.HasRoadThroughEdge(direction);
        bool previousHasRiver = cell.HasRiverThroughEdge(direction.Previous());
        bool nextHasRiver = cell.HasRiverThroughEdge(direction.Next());

        Vector2 interpolators = GetRoadInterpolators(direction, cell);
        Vector3 roadCenter = center;

        if (cell.HasRiverBeginOrEnd)
        {
            roadCenter += HexMetrics.GetSolidEdgeMiddle(
                cell.RiverBeginOrEndDirection.Opposite()
            ) * (1f / 3f);
        }
        else if (cell.HasRiverThroughEdge(direction.Previous()))
        {
            if (!hasRoadThroughEdge && !nextHasRiver)
            {
                return;
            }
            Vector3 middle = HexMetrics.GetSecondSolidCorner(direction);
            roadCenter += middle * 0.25f;
        }
        else if (cell.HasRiverThroughEdge(direction.Next()))
        {
            if (!hasRoadThroughEdge && !previousHasRiver)
            {
                return;
            }
            Vector3 middle = HexMetrics.GetFirstSolidCorner(direction);
            roadCenter += middle * 0.25f;
        }
        else if (cell.HasRiverThroughEdge(direction.Previous2()))
        {
            if (!hasRoadThroughEdge && !nextHasRiver)
            {
                return;
            }
            Vector3 middle = HexMetrics.GetFirstSolidCorner(direction);
            roadCenter += middle * 0.25f;
        }
        else if (cell.HasRiverThroughEdge(direction.Next2()))
        {
            if (!hasRoadThroughEdge && !previousHasRiver)
            {
                return;
            }
            Vector3 middle = HexMetrics.GetSecondSolidCorner(direction);
            roadCenter += middle * 0.25f;
        }
        else
        {
            return;
        }

        Vector3 mL = roadCenter.Lerp(e.v1, interpolators.X);
        Vector3 mR = roadCenter.Lerp(e.v5, interpolators.Y);

        TriangulateRoad(roads, roadCenter, mL, mR, e, hasRoadThroughEdge);

        if (previousHasRiver && !hasRoadThroughEdge &&
            !cell.HasRiverThroughEdge(direction.Previous2()))
        {
            Vector3 middle = HexMetrics.GetFirstSolidCorner(direction);
            Vector3 middle2 = HexMetrics.GetSecondSolidCorner(direction);
            TriangulateRoadEdge(roads, roadCenter, mL, middle);
            TriangulateRoadEdge(roads, roadCenter, middle2, mR);
        }
        else if (nextHasRiver && !hasRoadThroughEdge &&
                 !cell.HasRiverThroughEdge(direction.Next2()))
        {
            Vector3 middle = HexMetrics.GetFirstSolidCorner(direction);
            Vector3 middle2 = HexMetrics.GetSecondSolidCorner(direction);
            TriangulateRoadEdge(roads, roadCenter, middle, mR);
            TriangulateRoadEdge(roads, roadCenter, mL, middle);
        }
    }

    /* Part 7: 无河流时画道路（如果存在） */
    private static void TriangulateWithoutRiver(MeshData terrain, MeshData roads, HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e)
    {
        TriangulateEdgeFan(terrain, center, e, cell.Color);

        if (cell.HasRoads)
        {
            Vector2 interpolators = GetRoadInterpolators(direction, cell);
            TriangulateRoad(roads,
                center,
                center.Lerp(e.v1, interpolators.X),
                center.Lerp(e.v5, interpolators.Y),
                e,
                cell.HasRoadThroughEdge(direction)
            );
        }
    }

    /* Part 7: 确定左右中点插值系数 */
    private static Vector2 GetRoadInterpolators(HexDirection direction, HexCell cell)
    {
        Vector2 interpolators;
        if (cell.HasRoadThroughEdge(direction))
        {
            interpolators.X = interpolators.Y = 0.5f;
        }
        else
        {
            interpolators.X = cell.HasRoadThroughEdge(direction.Previous()) ? 0.5f : 0.25f;
            interpolators.Y = cell.HasRoadThroughEdge(direction.Next()) ? 0.5f : 0.25f;
        }
        return interpolators;
    }

    /* Part 7: 从边缘到中心画路面或路沿 */
    private static void TriangulateRoad(MeshData roads, Vector3 center, Vector3 mL, Vector3 mR, EdgeVertices e, bool hasRoadThroughCellEdge)
    {
        if (hasRoadThroughCellEdge)
        {
            Vector3 mC = center.Lerp(mR, 0.5f);
            TriangulateRoadSegment(roads, mL, mC, mR, e.v2, e.v3, e.v4);
            AddRoadTriangle(roads, center, mL, mC,
                new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(1f, 0f));
            AddRoadTriangle(roads, center, mC, mR,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f));
        }
        else
        {
            TriangulateRoadEdge(roads, center, mL, mR);
        }
    }

    /* Part 7: 画两个连接的 quad 作为道路段 */
    private static void TriangulateRoadSegment(MeshData roads, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, Vector3 v5, Vector3 v6)
    {
        AddRoadQuad(roads, v1, v2, v4, v5, 0f, 1f);
        AddRoadQuad(roads, v2, v3, v5, v6, 1f, 0f);
    }

    /* Part 7: 仅画路沿三角形 */
    private static void TriangulateRoadEdge(MeshData roads, Vector3 center, Vector3 mL, Vector3 mR)
    {
        AddRoadTriangle(roads, center, mL, mR,
            new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f));
    }

    // ==================== River ====================

    private static void TriangulateWithRiver(MeshData terrain, MeshData rivers,
        HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e)
    {
        Vector3 centerL, centerR;
        if (cell.HasRiverThroughEdge(direction.Opposite()))
        {
            centerL = center + HexMetrics.GetFirstSolidCorner(direction.Previous()) * 0.25f;
            centerR = center + HexMetrics.GetSecondSolidCorner(direction.Next()) * 0.25f;
        }
        else if (cell.HasRiverThroughEdge(direction.Next()))
        {
            centerL = center;
            centerR = center.Lerp(e.v5, 2f / 3f);
        }
        else if (cell.HasRiverThroughEdge(direction.Previous()))
        {
            centerL = center.Lerp(e.v1, 2f / 3f);
            centerR = center;
        }
        else if (cell.HasRiverThroughEdge(direction.Next2()))
        {
            centerL = center;
            centerR = center + HexMetrics.GetSolidEdgeMiddle(direction.Next()) *
                (0.5f * HexMetrics.InnerToOuter);
        }
        else
        {
            centerL = center + HexMetrics.GetSolidEdgeMiddle(direction.Previous()) *
                (0.5f * HexMetrics.InnerToOuter);
            centerR = center;
        }
        Vector3 mid = centerL.Lerp(centerR, 0.5f);
        center = new Vector3(mid.X, cell.StreamBedY, mid.Z);

        EdgeVertices m = new EdgeVertices(
            centerL.Lerp(e.v1, 0.5f),
            centerR.Lerp(e.v5, 0.5f),
            1f / 6f
        );
        m.v3 = new Vector3(m.v3.X, cell.StreamBedY, m.v3.Z);

        TriangulateEdgeStrip(terrain, m, cell.Color, e, cell.Color);

        AddTriangle(terrain, centerL, m.v1, m.v2, cell.Color, cell.Color, cell.Color);
        AddQuad(terrain, centerL, center, m.v2, m.v3, cell.Color, cell.Color, cell.Color, cell.Color);
        AddQuad(terrain, center, centerR, m.v3, m.v4, cell.Color, cell.Color, cell.Color, cell.Color);
        AddTriangle(terrain, centerR, m.v4, m.v5, cell.Color, cell.Color, cell.Color);

        /* Part 8: 水下隐藏河流 */
        if (!cell.IsUnderwater)
        {
            bool reversed = cell.IncomingRiver == direction;
            TriangulateRiverQuad(rivers, centerL, centerR, m.v2, m.v4, cell.RiverSurfaceY, 0.4f, reversed);
            TriangulateRiverQuad(rivers, m.v2, m.v4, e.v2, e.v4, cell.RiverSurfaceY, 0.6f, reversed);
        }
    }

    private static void TriangulateWithRiverBeginOrEnd(MeshData terrain, MeshData rivers,
        HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e)
    {
        EdgeVertices m = new EdgeVertices(
            center.Lerp(e.v1, 0.5f),
            center.Lerp(e.v5, 0.5f)
        );
        m.v3 = new Vector3(m.v3.X, e.v3.Y, m.v3.Z);

        TriangulateEdgeStrip(terrain, m, cell.Color, e, cell.Color);
        TriangulateEdgeFan(terrain, center, m, cell.Color);

        /* Part 8: 水下隐藏河流 */
        if (!cell.IsUnderwater)
        {
            bool reversed = cell.HasIncomingRiver;
            TriangulateRiverQuad(rivers, m.v2, m.v4, e.v2, e.v4, cell.RiverSurfaceY, 0.6f, reversed);

            Vector3 riverCenter = new Vector3(center.X, cell.RiverSurfaceY, center.Z);
            Vector3 m2 = new Vector3(m.v2.X, cell.RiverSurfaceY, m.v2.Z);
            Vector3 m4 = new Vector3(m.v4.X, cell.RiverSurfaceY, m.v4.Z);
            Vector3 prc = Perturb(riverCenter);
            Vector3 pm2 = Perturb(m2);
            Vector3 pm4 = Perturb(m4);
            int vi = rivers.Vertices.Count;
            if (reversed)
            {
                rivers.Vertices.Add(prc); rivers.UVs.Add(new Vector2(0.5f, 0.4f));
                rivers.Vertices.Add(pm4); rivers.UVs.Add(new Vector2(1f, 0.2f));
                rivers.Vertices.Add(pm2); rivers.UVs.Add(new Vector2(0f, 0.2f));
            }
            else
            {
                rivers.Vertices.Add(prc); rivers.UVs.Add(new Vector2(0.5f, 0.4f));
                rivers.Vertices.Add(pm2); rivers.UVs.Add(new Vector2(0f, 0.6f));
                rivers.Vertices.Add(pm4); rivers.UVs.Add(new Vector2(1f, 0.6f));
            }
            rivers.Triangles.Add(vi);
            rivers.Triangles.Add(vi + 1);
            rivers.Triangles.Add(vi + 2);
        }
    }

    /* Part 7: 新增 roads 参数，有道路时调用 TriangulateRoadAdjacentToRiver */
    private static void TriangulateAdjacentToRiver(MeshData terrain, MeshData rivers, MeshData roads,
        HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e)
    {
        if (cell.HasRiverThroughEdge(direction.Next()))
        {
            if (cell.HasRiverThroughEdge(direction.Previous()))
            {
                center += HexMetrics.GetSolidEdgeMiddle(direction) *
                    (HexMetrics.InnerToOuter * 0.5f);
            }
            else if (cell.HasRiverThroughEdge(direction.Previous2()))
            {
                center += HexMetrics.GetFirstSolidCorner(direction) * 0.25f;
            }
        }
        else if (
            cell.HasRiverThroughEdge(direction.Previous()) &&
            cell.HasRiverThroughEdge(direction.Next2())
        )
        {
            center += HexMetrics.GetSecondSolidCorner(direction) * 0.25f;
        }

        EdgeVertices m = new EdgeVertices(
            center.Lerp(e.v1, 0.5f),
            center.Lerp(e.v5, 0.5f)
        );

        TriangulateEdgeStrip(terrain, m, cell.Color, e, cell.Color);
        TriangulateEdgeFan(terrain, center, m, cell.Color);

        /* Part 7: 有道路时在河流相邻侧画道路 */
        if (cell.HasRoads)
        {
            TriangulateRoadAdjacentToRiver(terrain, roads, direction, cell, center, e);
        }
    }

    private static void TriangulateRiverQuad(MeshData md,
        Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        float y1, float y2, float v, bool reversed)
    {
        v1.Y = v2.Y = y1;
        v3.Y = v4.Y = y2;

        Vector3 p1 = Perturb(v1);
        Vector3 p2 = Perturb(v2);
        Vector3 p3 = Perturb(v3);
        Vector3 p4 = Perturb(v4);

        int vi = md.Vertices.Count;
        if (reversed)
        {
            md.Vertices.Add(p1); md.UVs.Add(new Vector2(1f, 0.8f - v));
            md.Vertices.Add(p2); md.UVs.Add(new Vector2(0f, 0.8f - v));
            md.Vertices.Add(p4); md.UVs.Add(new Vector2(0f, 0.6f - v));
            md.Vertices.Add(p1); md.UVs.Add(new Vector2(1f, 0.8f - v));
            md.Vertices.Add(p4); md.UVs.Add(new Vector2(0f, 0.6f - v));
            md.Vertices.Add(p3); md.UVs.Add(new Vector2(1f, 0.6f - v));
        }
        else
        {
            md.Vertices.Add(p1); md.UVs.Add(new Vector2(0f, v));
            md.Vertices.Add(p2); md.UVs.Add(new Vector2(1f, v));
            md.Vertices.Add(p4); md.UVs.Add(new Vector2(1f, v + 0.2f));
            md.Vertices.Add(p1); md.UVs.Add(new Vector2(0f, v));
            md.Vertices.Add(p4); md.UVs.Add(new Vector2(1f, v + 0.2f));
            md.Vertices.Add(p3); md.UVs.Add(new Vector2(0f, v + 0.2f));
        }
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 1);
        md.Triangles.Add(vi + 2);
        md.Triangles.Add(vi + 3);
        md.Triangles.Add(vi + 4);
        md.Triangles.Add(vi + 5);
    }

    private static void TriangulateRiverQuad(MeshData md,
        Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        float y, float v, bool reversed)
    {
        TriangulateRiverQuad(md, v1, v2, v3, v4, y, y, v, reversed);
    }

    /* Part 8: 瀑布三角剖分 — 河流穿过水陆边界时，缩短四边形到水面高度。
       使用 AddQuadUnperturbed + 手动 UV，匹配教程 (u: 0→1, v: 0.8→1)。 */
    private static void TriangulateWaterfallInWater(MeshData md,
        Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        float y1, float y2, float waterY)
    {
        v1.Y = v2.Y = y1;
        v3.Y = v4.Y = y2;

        v1 = Perturb(v1);
        v2 = Perturb(v2);
        v3 = Perturb(v3);
        v4 = Perturb(v4);

        float t = (waterY - y2) / (y1 - y2);
        v3 = v3.Lerp(v1, t);
        v4 = v4.Lerp(v2, t);

        /* 手动构建四边形 + UV，匹配教程 AddQuadUV(0f, 1f, 0.8f, 1f)
           顶部顶点 (v1,v2) → UV(0,0.8), 底部顶点 (v3,v4) → UV(1,1.0) */
        int vi = md.Vertices.Count;
        md.Vertices.Add(v1); md.UVs.Add(new Vector2(0f, 0.8f));
        md.Vertices.Add(v4); md.UVs.Add(new Vector2(1f, 1f));
        md.Vertices.Add(v2); md.UVs.Add(new Vector2(1f, 0.8f));
        md.Vertices.Add(v1); md.UVs.Add(new Vector2(0f, 0.8f));
        md.Vertices.Add(v3); md.UVs.Add(new Vector2(0f, 1f));
        md.Vertices.Add(v4); md.UVs.Add(new Vector2(1f, 1f));
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 1);
        md.Triangles.Add(vi + 2);
        md.Triangles.Add(vi + 3);
        md.Triangles.Add(vi + 4);
        md.Triangles.Add(vi + 5);
    }

    // ==================== Corner ====================

    private static void TriangulateCorner(MeshData md,
        Vector3 bottom, HexCell bottomCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell)
    {
        HexEdgeType leftEdgeType = bottomCell.GetEdgeType(leftCell);
        HexEdgeType rightEdgeType = bottomCell.GetEdgeType(rightCell);

        if (leftEdgeType == HexEdgeType.Slope)
        {
            if (rightEdgeType == HexEdgeType.Slope)
            {
                TriangulateCornerTerraces(md, bottom, bottomCell, left, leftCell, right, rightCell);
                return;
            }
            if (rightEdgeType == HexEdgeType.Flat)
            {
                TriangulateCornerTerraces(md, left, leftCell, right, rightCell, bottom, bottomCell);
                return;
            }
            TriangulateCornerTerracesCliff(md, bottom, bottomCell, left, leftCell, right, rightCell);
            return;
        }
        if (rightEdgeType == HexEdgeType.Slope)
        {
            if (leftEdgeType == HexEdgeType.Flat)
            {
                TriangulateCornerTerraces(md, right, rightCell, bottom, bottomCell, left, leftCell);
                return;
            }
            TriangulateCornerCliffTerraces(md, bottom, bottomCell, left, leftCell, right, rightCell);
            return;
        }
        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            if (leftCell.Elevation < rightCell.Elevation)
            {
                TriangulateCornerCliffTerraces(md, right, rightCell, bottom, bottomCell, left, leftCell);
                return;
            }
            TriangulateCornerTerracesCliff(md, left, leftCell, right, rightCell, bottom, bottomCell);
            return;
        }
        AddTriangle(md, bottom, left, right, bottomCell.Color, leftCell.Color, rightCell.Color);
    }

    private static void TriangulateCornerTerraces(MeshData md,
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell)
    {
        Vector3 v3 = HexMetrics.TerraceLerp(begin, left, 1);
        Vector3 v4 = HexMetrics.TerraceLerp(begin, right, 1);
        Color c3 = HexMetrics.TerraceLerp(beginCell.Color, leftCell.Color, 1);
        Color c4 = HexMetrics.TerraceLerp(beginCell.Color, rightCell.Color, 1);

        AddTriangle(md, begin, v3, v4, beginCell.Color, c3, c4);

        for (int i = 2; i < HexMetrics.TerraceSteps; i++)
        {
            Vector3 v1 = v3;
            Vector3 v2 = v4;
            Color c1 = c3;
            Color c2 = c4;
            v3 = HexMetrics.TerraceLerp(begin, left, i);
            v4 = HexMetrics.TerraceLerp(begin, right, i);
            c3 = HexMetrics.TerraceLerp(beginCell.Color, leftCell.Color, i);
            c4 = HexMetrics.TerraceLerp(beginCell.Color, rightCell.Color, i);
            AddQuad(md, v1, v2, v3, v4, c1, c2, c3, c4);
        }

        AddQuad(md, v3, v4, left, right, c3, c4, leftCell.Color, rightCell.Color);
    }

    private static void TriangulateCornerTerracesCliff(MeshData md,
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell)
    {
        float b = 1f / (rightCell.Elevation - beginCell.Elevation);
        if (b < 0) b = -b;
        Vector3 boundary = Perturb(begin).Lerp(Perturb(right), b);
        Color boundaryColor = beginCell.Color.Lerp(rightCell.Color, b);

        TriangulateBoundaryTriangle(md, begin, beginCell, left, leftCell, boundary, boundaryColor);

        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            TriangulateBoundaryTriangle(md, left, leftCell, right, rightCell, boundary, boundaryColor);
        }
        else
        {
            AddTriangleUnperturbed(md, Perturb(left), Perturb(right), boundary,
                leftCell.Color, rightCell.Color, boundaryColor);
        }
    }

    private static void TriangulateCornerCliffTerraces(MeshData md,
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell)
    {
        float b = 1f / (leftCell.Elevation - beginCell.Elevation);
        if (b < 0) b = -b;
        Vector3 boundary = Perturb(begin).Lerp(Perturb(left), b);
        Color boundaryColor = beginCell.Color.Lerp(leftCell.Color, b);

        TriangulateBoundaryTriangle(md, right, rightCell, begin, beginCell, boundary, boundaryColor);

        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            TriangulateBoundaryTriangle(md, left, leftCell, right, rightCell, boundary, boundaryColor);
        }
        else
        {
            AddTriangleUnperturbed(md, Perturb(left), Perturb(right), boundary,
                leftCell.Color, rightCell.Color, boundaryColor);
        }
    }

    private static void TriangulateBoundaryTriangle(MeshData md,
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 boundary, Color boundaryColor)
    {
        Vector3 v2 = Perturb(HexMetrics.TerraceLerp(begin, left, 1));
        Color c2 = HexMetrics.TerraceLerp(beginCell.Color, leftCell.Color, 1);

        AddTriangleUnperturbed(md, Perturb(begin), v2, boundary,
            beginCell.Color, c2, boundaryColor);

        for (int i = 2; i < HexMetrics.TerraceSteps; i++)
        {
            Vector3 v1 = v2;
            Color c1 = c2;
            v2 = Perturb(HexMetrics.TerraceLerp(begin, left, i));
            c2 = HexMetrics.TerraceLerp(beginCell.Color, leftCell.Color, i);
            AddTriangleUnperturbed(md, v1, v2, boundary, c1, c2, boundaryColor);
        }

        AddTriangleUnperturbed(md, v2, Perturb(left), boundary,
            c2, leftCell.Color, boundaryColor);
    }

    // ==================== Water ====================

    /* Part 8: 开放水面三角化 */
    private static void TriangulateOpenWater(HexDirection direction, HexCell cell,
        MeshData water, MeshData waterShore, MeshData estuaries)
    {
        Vector3 center = cell.Position;
        center.Y = cell.WaterSurfaceY;

        HexCell neighbor = cell.GetNeighbor(direction);

        /* Shore 方向：全部交给 TriangulateShoreWater，不生成 open water 中心三角形。
           避免同一方向同时生成 open water 和 shore water 中心扇形而重叠。 */
        if (neighbor != null && !neighbor.IsUnderwater)
        {
            TriangulateShoreWater(direction, cell, neighbor, water, waterShore, estuaries);
            return;
        }

        Vector3 c1 = center + HexMetrics.GetFirstWaterCorner(direction);
        Vector3 c2 = center + HexMetrics.GetSecondWaterCorner(direction);

        /* 开放水面三角形（不含 UV，使用默认白色顶点色） */
        AddTriangle(water, center, c1, c2);

        if (neighbor != null && neighbor.IsUnderwater)
        {
            /* 开放水面连接桥和角落 — 只在 <= SE 时生成，避免重复 */
            if (direction <= HexDirection.SE)
            {
                Vector3 waterBridge = HexMetrics.GetWaterBridge(direction);
                Vector3 e1 = c1 + waterBridge;
                Vector3 e2 = c2 + waterBridge;
                AddQuad(water, c1, c2, e1, e2);

                if (direction <= HexDirection.E)
                {
                    HexCell nextNeighbor = cell.GetNeighbor(direction.Next());
                    if (nextNeighbor != null && nextNeighbor.IsUnderwater)
                    {
                        AddTriangle(water, c2, e2, c2 + HexMetrics.GetWaterBridge(direction.Next()));
                    }
                }
            }
        }
    }

    /* Part 8: 岸边水体 — "Between Water and Solid Edges" 版本。
       e2 使用邻居 solid corners（非 bridge），e1 使用 water corners。
       所有三角形/四边形带 UV 输出到 waterShore。 */
    private static void TriangulateShoreWater(HexDirection direction, HexCell cell, HexCell neighbor,
        MeshData water, MeshData waterShore, MeshData estuaries)
    {
        Vector3 center = cell.Position;
        center.Y = cell.WaterSurfaceY;

        EdgeVertices e1 = new EdgeVertices(
            center + HexMetrics.GetFirstWaterCorner(direction),
            center + HexMetrics.GetSecondWaterCorner(direction)
        );

        /* 中心扇形：画到 water mesh（无 UV，同教程） */
        AddTriangle(water, center, e1.v1, e1.v2);
        AddTriangle(water, center, e1.v2, e1.v3);
        AddTriangle(water, center, e1.v3, e1.v4);
        AddTriangle(water, center, e1.v4, e1.v5);

        /* e2 从邻居中心反向计算，使用 solid corners（非 bridge） */
        Vector3 center2 = neighbor.Position;
        center2.Y = center.Y;
        EdgeVertices e2 = new EdgeVertices(
            center2 + HexMetrics.GetSecondSolidCorner(direction.Opposite()),
            center2 + HexMetrics.GetFirstSolidCorner(direction.Opposite())
        );

        /* 检查河口 */
        HexCell nextNeighbor = cell.GetNeighbor(direction.Next());
        bool hasRiver = cell.HasRiverThroughEdge(direction);

        /* Bug fix: 按教程仅判断 hasRiver，不检查 nextNeighbor */
        if (hasRiver)
        {
            /* 河口：梯形由 TriangulateEstuary 处理 */
            TriangulateEstuary(estuaries, e1, e2, cell.IncomingRiver == direction);

            /* 河口两侧的三角形仍然加到 waterShore */
            AddTriangle(waterShore, e1.v2, e2.v1, e1.v1);
            AddTriangleUV(waterShore,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f));
            AddTriangle(waterShore, e1.v5, e2.v5, e1.v4);
            AddTriangleUV(waterShore,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0f));
        }
        else
        {
            /* 无河口：标准 4 个四边形 edge strip，内侧 V=0 外侧 V=1 */
            AddQuad(waterShore, e1.v1, e1.v2, e2.v1, e2.v2);
            AddQuadUV(waterShore, 0f, 0f, 0f, 1f);
            AddQuad(waterShore, e1.v2, e1.v3, e2.v2, e2.v3);
            AddQuadUV(waterShore, 0f, 0f, 0f, 1f);
            AddQuad(waterShore, e1.v3, e1.v4, e2.v3, e2.v4);
            AddQuadUV(waterShore, 0f, 0f, 0f, 1f);
            AddQuad(waterShore, e1.v4, e1.v5, e2.v4, e2.v5);
            AddQuadUV(waterShore, 0f, 0f, 0f, 1f);
        }

        /* Bug fix: 角落三角形移到 if/else 外部 — 教程中无论有无河口都要添加 */
        if (nextNeighbor != null)
        {
            float v3 = nextNeighbor.IsUnderwater ? 0f : 1f;
            Vector3 third = nextNeighbor.Position + (nextNeighbor.IsUnderwater ?
                HexMetrics.GetFirstWaterCorner(direction.Previous()) :
                HexMetrics.GetFirstSolidCorner(direction.Previous()));
            third.Y = center.Y;
            AddTriangle(waterShore, e1.v5, e2.v5, third);
            AddTriangleUV(waterShore,
                new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, v3));
        }
    }

    /* Part 8: 河口三角剖分 — 河流入水口的梯形区域。
       使用独立的 estuaries，同时设置 UV（shore/blend）和 UV2（river flow）。
       incomingRiver 控制 UV2 的方向：入水河流从下往上流，出水河流相反。
       列表式构建允许像教程一样分开提交几何、UV1、UV2。 */
    private static void TriangulateEstuary(MeshData estuaries,
        EdgeVertices e1, EdgeVertices e2, bool incomingRiver)
    {
        /* 几何体（与方向无关）: 左四边形 + 中间三角形 + 右四边形 */
        AddQuad(estuaries, e2.v1, e1.v2, e2.v2, e1.v3);
        AddTriangle(estuaries, e1.v3, e2.v2, e2.v4);
        AddQuad(estuaries, e1.v3, e1.v4, e2.v4, e2.v5);

        /* UV1: blend 因子在 x 中（侧边 = 0，中心 = 1），shore 在 y 中 */
        AddQuadUV(estuaries,
            new Vector2(0f, 1f), new Vector2(0f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 0f));
        AddTriangleUV(estuaries,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(1f, 1f));
        AddQuadUV(estuaries,
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f));

        /* UV2: river flow — 入水（河流流入水体）和出水（河流流出水体）的坐标不同 */
        if (incomingRiver)
        {
            AddQuadUV2(estuaries,
                new Vector2(1.5f, 1f), new Vector2(0.7f, 1.15f),
                new Vector2(1f, 0.8f), new Vector2(0.5f, 1.1f));
            AddTriangleUV2(estuaries,
                new Vector2(0.5f, 1.1f),
                new Vector2(1f, 0.8f),
                new Vector2(0f, 0.8f));
            AddQuadUV2(estuaries,
                new Vector2(0.5f, 1.1f), new Vector2(0.3f, 1.15f),
                new Vector2(0f, 0.8f), new Vector2(-0.5f, 1f));
        }
        else
        {
            /* 出水（反向流动）：U 镜像，V 映射到负值 */
            AddQuadUV2(estuaries,
                new Vector2(-0.5f, -0.2f), new Vector2(0.3f, -0.35f),
                new Vector2(0f, 0f), new Vector2(0.5f, -0.3f));
            AddTriangleUV2(estuaries,
                new Vector2(0.5f, -0.3f),
                new Vector2(0f, 0f),
                new Vector2(1f, 0f));
            AddQuadUV2(estuaries,
                new Vector2(0.5f, -0.3f), new Vector2(0.7f, -0.35f),
                new Vector2(1f, 0f), new Vector2(1.5f, -0.2f));
        }
    }
}
