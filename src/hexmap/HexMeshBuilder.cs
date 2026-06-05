using Godot;

namespace HexMap;

/// <summary>
/// Part 5：六边形网格三角化器。
/// 对应教程中的 HexMesh，负责将一组 HexCell 三角化为 Godot Mesh。
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

    // ==================== 公共入口 ====================

    /* Part 8: 新增 waterMesh 输出 */
    public static void BuildMeshes(HexCell[] cells, out Mesh terrainMesh, out Mesh riverMesh, out Mesh roadMesh, out Mesh waterMesh)
    {
        var terrainSt = new SurfaceTool();
        var riverSt = new SurfaceTool();
        var roadSt = new SurfaceTool();
        var waterSt = new SurfaceTool();
        terrainSt.Begin(Mesh.PrimitiveType.Triangles);
        riverSt.Begin(Mesh.PrimitiveType.Triangles);
        roadSt.Begin(Mesh.PrimitiveType.Triangles);
        waterSt.Begin(Mesh.PrimitiveType.Triangles);

        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null)
                TriangulateCell(cells[i], terrainSt, riverSt, roadSt, waterSt);
        }

        terrainSt.GenerateNormals();
        riverSt.GenerateNormals();
        roadSt.GenerateNormals();
        waterSt.GenerateNormals();
        terrainMesh = terrainSt.Commit();
        riverMesh = riverSt.Commit();
        roadMesh = roadSt.Commit();
        waterMesh = waterSt.Commit();
    }

    public static Mesh BuildMesh(HexCell[] cells)
    {
        BuildMeshes(cells, out Mesh terrainMesh, out _, out _, out _);
        return terrainMesh;
    }

    // ==================== Cell / Sector ====================

    /* Part 8: 新增 waterSt 参数 */
    private static void TriangulateCell(HexCell cell, SurfaceTool terrainSt, SurfaceTool riverSt, SurfaceTool roadSt, SurfaceTool waterSt)
    {
        for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
        {
            Triangulate(terrainSt, riverSt, roadSt, waterSt, d, cell);
        }
    }

    /* Part 8: 新增 waterSt 参数 */
    private static void Triangulate(SurfaceTool terrainSt, SurfaceTool riverSt, SurfaceTool roadSt, SurfaceTool waterSt, HexDirection direction, HexCell cell)
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
                    TriangulateWithRiverBeginOrEnd(terrainSt, riverSt, direction, cell, center, e);
                }
                else
                {
                    TriangulateWithRiver(terrainSt, riverSt, direction, cell, center, e);
                }
            }
            else
            {
                /* Part 7: 透传 roadSt */
                TriangulateAdjacentToRiver(terrainSt, riverSt, roadSt, direction, cell, center, e);
            }
        }
        else
        {
            /* Part 7: 使用道路感知的三角化方法 */
            TriangulateWithoutRiver(terrainSt, roadSt, direction, cell, center, e);
        }

        if (direction <= HexDirection.SE)
        {
            /* Part 7: 透传 roadSt */
            TriangulateConnection(terrainSt, riverSt, roadSt, direction, cell, e);
        }

        /* Part 8: 开放水面三角化 */
        if (cell.IsUnderwater)
        {
            TriangulateOpenWater(direction, cell, waterSt);
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

    private static void AddTriangle(SurfaceTool st, Vector3 v1, Vector3 v2, Vector3 v3, Color c1, Color c2, Color c3)
    {
        st.SetColor(c1); st.AddVertex(Perturb(v1));
        st.SetColor(c2); st.AddVertex(Perturb(v2));
        st.SetColor(c3); st.AddVertex(Perturb(v3));
    }

    private static void AddTriangleUnperturbed(SurfaceTool st, Vector3 v1, Vector3 v2, Vector3 v3, Color c1, Color c2, Color c3)
    {
        st.SetColor(c1); st.AddVertex(v1);
        st.SetColor(c2); st.AddVertex(v2);
        st.SetColor(c3); st.AddVertex(v3);
    }

    private static void AddQuad(SurfaceTool st, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, Color c1, Color c2, Color c3, Color c4)
    {
        st.SetColor(c1); st.AddVertex(Perturb(v1));
        st.SetColor(c4); st.AddVertex(Perturb(v4));
        st.SetColor(c2); st.AddVertex(Perturb(v2));
        st.SetColor(c1); st.AddVertex(Perturb(v1));
        st.SetColor(c3); st.AddVertex(Perturb(v3));
        st.SetColor(c4); st.AddVertex(Perturb(v4));
    }

    private static void AddQuadUnperturbed(SurfaceTool st, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, Color c1, Color c2, Color c3, Color c4)
    {
        st.SetColor(c1); st.AddVertex(v1);
        st.SetColor(c4); st.AddVertex(v4);
        st.SetColor(c2); st.AddVertex(v2);
        st.SetColor(c1); st.AddVertex(v1);
        st.SetColor(c3); st.AddVertex(v3);
        st.SetColor(c4); st.AddVertex(v4);
    }

    /* Part 7: 道路网格使用 UV 而非顶点颜色 */
    private static void AddRoadQuad(SurfaceTool st, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, float uMin, float uMax)
    {
        st.SetUV(new Vector2(uMin, 0f)); st.AddVertex(Perturb(v1));
        st.SetUV(new Vector2(uMax, 0f)); st.AddVertex(Perturb(v4));
        st.SetUV(new Vector2(uMax, 0f)); st.AddVertex(Perturb(v2));
        st.SetUV(new Vector2(uMin, 0f)); st.AddVertex(Perturb(v1));
        st.SetUV(new Vector2(uMin, 0f)); st.AddVertex(Perturb(v3));
        st.SetUV(new Vector2(uMax, 0f)); st.AddVertex(Perturb(v4));
    }

    private static void AddRoadTriangle(SurfaceTool st, Vector3 v1, Vector3 v2, Vector3 v3, Vector2 uv1, Vector2 uv2, Vector2 uv3)
    {
        st.SetUV(uv1); st.AddVertex(Perturb(v1));
        st.SetUV(uv3); st.AddVertex(Perturb(v3));
        st.SetUV(uv2); st.AddVertex(Perturb(v2));
    }

    // ==================== Edge ====================

    private static void TriangulateEdgeFan(SurfaceTool st, Vector3 center, EdgeVertices edge, Color color)
    {
        AddTriangle(st, center, edge.v1, edge.v2, color, color, color);
        AddTriangle(st, center, edge.v2, edge.v3, color, color, color);
        AddTriangle(st, center, edge.v3, edge.v4, color, color, color);
        AddTriangle(st, center, edge.v4, edge.v5, color, color, color);
    }

    /* Part 7: 新增 hasRoad 参数，有道路时在中间 quad 段画路面 */
    private static void TriangulateEdgeStrip(SurfaceTool st, EdgeVertices e1, Color c1, EdgeVertices e2, Color c2, bool hasRoad = false)
    {
        AddQuad(st, e1.v1, e1.v2, e2.v1, e2.v2, c1, c1, c2, c2);
        AddQuad(st, e1.v2, e1.v3, e2.v2, e2.v3, c1, c1, c2, c2);
        AddQuad(st, e1.v3, e1.v4, e2.v3, e2.v4, c1, c1, c2, c2);
        AddQuad(st, e1.v4, e1.v5, e2.v4, e2.v5, c1, c1, c2, c2);
    }

    /* Part 7: 新增 roadSt 参数，在连接处传入道路信息 */
    private static void TriangulateConnection(SurfaceTool terrainSt, SurfaceTool riverSt, SurfaceTool roadSt, HexDirection direction, HexCell cell, EdgeVertices e1)
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
            TriangulateRiverQuad(riverSt,
                e1.v2, e1.v4, e2.v2, e2.v4,
                cell.RiverSurfaceY, neighbor.RiverSurfaceY, 0.8f,
                cell.HasIncomingRiver && cell.IncomingRiver == direction
            );
        }

        /* Part 7: 获取道路信息 */
        bool hasRoad = cell.HasRoadThroughEdge(direction);

        if (cell.GetEdgeType(direction) == HexEdgeType.Slope)
        {
            TriangulateEdgeTerraces(terrainSt, e1, cell, e2, neighbor);
        }
        else
        {
            TriangulateEdgeStrip(terrainSt, e1, cell.Color, e2, neighbor.Color);
        }

        /* Part 7: 道路单独画到 roadSt 网格 */
        if (hasRoad)
        {
            TriangulateRoadSegment(roadSt, e1.v2, e1.v3, e1.v4, e2.v2, e2.v3, e2.v4);
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
                    TriangulateCorner(terrainSt, e1.v5, cell, e2.v5, neighbor, v5, nextNeighbor);
                }
                else
                {
                    TriangulateCorner(terrainSt, v5, nextNeighbor, e1.v5, cell, e2.v5, neighbor);
                }
            }
            else if (neighbor.Elevation <= nextNeighbor.Elevation)
            {
                TriangulateCorner(terrainSt, e2.v5, neighbor, v5, nextNeighbor, e1.v5, cell);
            }
            else
            {
                TriangulateCorner(terrainSt, v5, nextNeighbor, e1.v5, cell, e2.v5, neighbor);
            }
        }
    }

    /* Part 7: 新增 hasRoad 参数，透传到 TriangulateEdgeStrip */
    private static void TriangulateEdgeTerraces(SurfaceTool st,
        EdgeVertices begin, HexCell beginCell,
        EdgeVertices end, HexCell endCell)
    {
        EdgeVertices e2 = EdgeVertices.TerraceLerp(begin, end, 1);
        Color c2 = HexMetrics.TerraceLerp(beginCell.Color, endCell.Color, 1);

        TriangulateEdgeStrip(st, begin, beginCell.Color, e2, c2);

        for (int i = 2; i < HexMetrics.TerraceSteps; i++)
        {
            EdgeVertices e1 = e2;
            Color c1 = c2;
            e2 = EdgeVertices.TerraceLerp(begin, end, i);
            c2 = HexMetrics.TerraceLerp(beginCell.Color, endCell.Color, i);
            TriangulateEdgeStrip(st, e1, c1, e2, c2);
        }

        TriangulateEdgeStrip(st, e2, c2, end, endCell.Color);
    }

    /* Part 7: 有道路时在河流相邻侧画道路 */
    private static void TriangulateRoadAdjacentToRiver(SurfaceTool terrainSt, SurfaceTool roadSt, HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e)
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

        TriangulateRoad(roadSt, roadCenter, mL, mR, e, hasRoadThroughEdge);

        if (previousHasRiver && !hasRoadThroughEdge &&
            !cell.HasRiverThroughEdge(direction.Previous2()))
        {
            Vector3 middle = HexMetrics.GetFirstSolidCorner(direction);
            Vector3 middle2 = HexMetrics.GetSecondSolidCorner(direction);
            TriangulateRoadEdge(roadSt, roadCenter, mL, middle);
            TriangulateRoadEdge(roadSt, roadCenter, middle2, mR);
        }
        else if (nextHasRiver && !hasRoadThroughEdge &&
                 !cell.HasRiverThroughEdge(direction.Next2()))
        {
            Vector3 middle = HexMetrics.GetFirstSolidCorner(direction);
            Vector3 middle2 = HexMetrics.GetSecondSolidCorner(direction);
            TriangulateRoadEdge(roadSt, roadCenter, middle, mR);
            TriangulateRoadEdge(roadSt, roadCenter, mL, middle);
        }
    }

    /* Part 7: 无河流时画道路（如果存在） */
    private static void TriangulateWithoutRiver(SurfaceTool terrainSt, SurfaceTool roadSt, HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e)
    {
        TriangulateEdgeFan(terrainSt, center, e, cell.Color);

        if (cell.HasRoads)
        {
            Vector2 interpolators = GetRoadInterpolators(direction, cell);
            TriangulateRoad(roadSt,
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
    private static void TriangulateRoad(SurfaceTool roadSt, Vector3 center, Vector3 mL, Vector3 mR, EdgeVertices e, bool hasRoadThroughCellEdge)
    {
        if (hasRoadThroughCellEdge)
        {
            Vector3 mC = center.Lerp(mR, 0.5f);
            TriangulateRoadSegment(roadSt, mL, mC, mR, e.v2, e.v3, e.v4);
            AddRoadTriangle(roadSt, center, mL, mC,
                new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(1f, 0f));
            AddRoadTriangle(roadSt, center, mC, mR,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f));
        }
        else
        {
            TriangulateRoadEdge(roadSt, center, mL, mR);
        }
    }

    /* Part 7: 画两个连接的 quad 作为道路段 */
    private static void TriangulateRoadSegment(SurfaceTool roadSt, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, Vector3 v5, Vector3 v6)
    {
        AddRoadQuad(roadSt, v1, v2, v4, v5, 0f, 1f);
        AddRoadQuad(roadSt, v2, v3, v5, v6, 1f, 0f);
    }

    /* Part 7: 仅画路沿三角形 */
    private static void TriangulateRoadEdge(SurfaceTool roadSt, Vector3 center, Vector3 mL, Vector3 mR)
    {
        AddRoadTriangle(roadSt, center, mL, mR,
            new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f));
    }

    // ==================== River ====================

    private static void TriangulateWithRiver(SurfaceTool terrainSt, SurfaceTool riverSt,
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

        TriangulateEdgeStrip(terrainSt, m, cell.Color, e, cell.Color);

        AddTriangle(terrainSt, centerL, m.v1, m.v2, cell.Color, cell.Color, cell.Color);
        AddQuad(terrainSt, centerL, center, m.v2, m.v3, cell.Color, cell.Color, cell.Color, cell.Color);
        AddQuad(terrainSt, center, centerR, m.v3, m.v4, cell.Color, cell.Color, cell.Color, cell.Color);
        AddTriangle(terrainSt, centerR, m.v4, m.v5, cell.Color, cell.Color, cell.Color);

        bool reversed = cell.IncomingRiver == direction;
        TriangulateRiverQuad(riverSt, centerL, centerR, m.v2, m.v4, cell.RiverSurfaceY, 0.4f, reversed);
        TriangulateRiverQuad(riverSt, m.v2, m.v4, e.v2, e.v4, cell.RiverSurfaceY, 0.6f, reversed);
    }

    private static void TriangulateWithRiverBeginOrEnd(SurfaceTool terrainSt, SurfaceTool riverSt,
        HexDirection direction, HexCell cell, Vector3 center, EdgeVertices e)
    {
        EdgeVertices m = new EdgeVertices(
            center.Lerp(e.v1, 0.5f),
            center.Lerp(e.v5, 0.5f)
        );
        m.v3 = new Vector3(m.v3.X, e.v3.Y, m.v3.Z);

        TriangulateEdgeStrip(terrainSt, m, cell.Color, e, cell.Color);
        TriangulateEdgeFan(terrainSt, center, m, cell.Color);

        bool reversed = cell.HasIncomingRiver;
        TriangulateRiverQuad(riverSt, m.v2, m.v4, e.v2, e.v4, cell.RiverSurfaceY, 0.6f, reversed);

        Vector3 riverCenter = new Vector3(center.X, cell.RiverSurfaceY, center.Z);
        Vector3 m2 = new Vector3(m.v2.X, cell.RiverSurfaceY, m.v2.Z);
        Vector3 m4 = new Vector3(m.v4.X, cell.RiverSurfaceY, m.v4.Z);
        Vector3 prc = Perturb(riverCenter);
        Vector3 pm2 = Perturb(m2);
        Vector3 pm4 = Perturb(m4);
        if (reversed)
        {
            riverSt.SetUV(new Vector2(0.5f, 0.4f)); riverSt.AddVertex(prc);
            riverSt.SetUV(new Vector2(1f, 0.2f)); riverSt.AddVertex(pm4);
            riverSt.SetUV(new Vector2(0f, 0.2f)); riverSt.AddVertex(pm2);
        }
        else
        {
            riverSt.SetUV(new Vector2(0.5f, 0.4f)); riverSt.AddVertex(prc);
            riverSt.SetUV(new Vector2(0f, 0.6f)); riverSt.AddVertex(pm2);
            riverSt.SetUV(new Vector2(1f, 0.6f)); riverSt.AddVertex(pm4);
        }
    }

    /* Part 7: 新增 roadSt 参数，有道路时调用 TriangulateRoadAdjacentToRiver */
    private static void TriangulateAdjacentToRiver(SurfaceTool terrainSt, SurfaceTool riverSt, SurfaceTool roadSt,
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

        TriangulateEdgeStrip(terrainSt, m, cell.Color, e, cell.Color);
        TriangulateEdgeFan(terrainSt, center, m, cell.Color);

        /* Part 7: 有道路时在河流相邻侧画道路 */
        if (cell.HasRoads)
        {
            TriangulateRoadAdjacentToRiver(terrainSt, roadSt, direction, cell, center, e);
        }
    }

    private static void TriangulateRiverQuad(SurfaceTool st,
        Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        float y1, float y2, float v, bool reversed)
    {
        v1.Y = v2.Y = y1;
        v3.Y = v4.Y = y2;

        Vector3 p1 = Perturb(v1);
        Vector3 p2 = Perturb(v2);
        Vector3 p3 = Perturb(v3);
        Vector3 p4 = Perturb(v4);

        if (reversed)
        {
            st.SetUV(new Vector2(1f, 0.8f - v)); st.AddVertex(p1);
            st.SetUV(new Vector2(0f, 0.8f - v)); st.AddVertex(p2);
            st.SetUV(new Vector2(0f, 0.6f - v)); st.AddVertex(p4);
            st.SetUV(new Vector2(1f, 0.8f - v)); st.AddVertex(p1);
            st.SetUV(new Vector2(0f, 0.6f - v)); st.AddVertex(p4);
            st.SetUV(new Vector2(1f, 0.6f - v)); st.AddVertex(p3);
        }
        else
        {
            st.SetUV(new Vector2(0f, v)); st.AddVertex(p1);
            st.SetUV(new Vector2(1f, v)); st.AddVertex(p2);
            st.SetUV(new Vector2(1f, v + 0.2f)); st.AddVertex(p4);
            st.SetUV(new Vector2(0f, v)); st.AddVertex(p1);
            st.SetUV(new Vector2(1f, v + 0.2f)); st.AddVertex(p4);
            st.SetUV(new Vector2(0f, v + 0.2f)); st.AddVertex(p3);
        }
    }

    private static void TriangulateRiverQuad(SurfaceTool st,
        Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        float y, float v, bool reversed)
    {
        TriangulateRiverQuad(st, v1, v2, v3, v4, y, y, v, reversed);
    }

    // ==================== Corner ====================

    private static void TriangulateCorner(SurfaceTool st,
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
                TriangulateCornerTerraces(st, bottom, bottomCell, left, leftCell, right, rightCell);
                return;
            }
            if (rightEdgeType == HexEdgeType.Flat)
            {
                TriangulateCornerTerraces(st, left, leftCell, right, rightCell, bottom, bottomCell);
                return;
            }
            TriangulateCornerTerracesCliff(st, bottom, bottomCell, left, leftCell, right, rightCell);
            return;
        }
        if (rightEdgeType == HexEdgeType.Slope)
        {
            if (leftEdgeType == HexEdgeType.Flat)
            {
                TriangulateCornerTerraces(st, right, rightCell, bottom, bottomCell, left, leftCell);
                return;
            }
            TriangulateCornerCliffTerraces(st, bottom, bottomCell, left, leftCell, right, rightCell);
            return;
        }
        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            if (leftCell.Elevation < rightCell.Elevation)
            {
                TriangulateCornerCliffTerraces(st, right, rightCell, bottom, bottomCell, left, leftCell);
                return;
            }
            TriangulateCornerTerracesCliff(st, left, leftCell, right, rightCell, bottom, bottomCell);
            return;
        }
        AddTriangle(st, bottom, left, right, bottomCell.Color, leftCell.Color, rightCell.Color);
    }

    private static void TriangulateCornerTerraces(SurfaceTool st,
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell)
    {
        Vector3 v3 = HexMetrics.TerraceLerp(begin, left, 1);
        Vector3 v4 = HexMetrics.TerraceLerp(begin, right, 1);
        Color c3 = HexMetrics.TerraceLerp(beginCell.Color, leftCell.Color, 1);
        Color c4 = HexMetrics.TerraceLerp(beginCell.Color, rightCell.Color, 1);

        AddTriangle(st, begin, v3, v4, beginCell.Color, c3, c4);

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
            AddQuad(st, v1, v2, v3, v4, c1, c2, c3, c4);
        }

        AddQuad(st, v3, v4, left, right, c3, c4, leftCell.Color, rightCell.Color);
    }

    private static void TriangulateCornerTerracesCliff(SurfaceTool st,
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell)
    {
        float b = 1f / (rightCell.Elevation - beginCell.Elevation);
        if (b < 0) b = -b;
        Vector3 boundary = Perturb(begin).Lerp(Perturb(right), b);
        Color boundaryColor = beginCell.Color.Lerp(rightCell.Color, b);

        TriangulateBoundaryTriangle(st, begin, beginCell, left, leftCell, boundary, boundaryColor);

        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            TriangulateBoundaryTriangle(st, left, leftCell, right, rightCell, boundary, boundaryColor);
        }
        else
        {
            AddTriangleUnperturbed(st, Perturb(left), Perturb(right), boundary,
                leftCell.Color, rightCell.Color, boundaryColor);
        }
    }

    private static void TriangulateCornerCliffTerraces(SurfaceTool st,
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 right, HexCell rightCell)
    {
        float b = 1f / (leftCell.Elevation - beginCell.Elevation);
        if (b < 0) b = -b;
        Vector3 boundary = Perturb(begin).Lerp(Perturb(left), b);
        Color boundaryColor = beginCell.Color.Lerp(leftCell.Color, b);

        TriangulateBoundaryTriangle(st, right, rightCell, begin, beginCell, boundary, boundaryColor);

        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            TriangulateBoundaryTriangle(st, left, leftCell, right, rightCell, boundary, boundaryColor);
        }
        else
        {
            AddTriangleUnperturbed(st, Perturb(left), Perturb(right), boundary,
                leftCell.Color, rightCell.Color, boundaryColor);
        }
    }

    private static void TriangulateBoundaryTriangle(SurfaceTool st,
        Vector3 begin, HexCell beginCell,
        Vector3 left, HexCell leftCell,
        Vector3 boundary, Color boundaryColor)
    {
        Vector3 v2 = Perturb(HexMetrics.TerraceLerp(begin, left, 1));
        Color c2 = HexMetrics.TerraceLerp(beginCell.Color, leftCell.Color, 1);

        AddTriangleUnperturbed(st, Perturb(begin), v2, boundary,
            beginCell.Color, c2, boundaryColor);

        for (int i = 2; i < HexMetrics.TerraceSteps; i++)
        {
            Vector3 v1 = v2;
            Color c1 = c2;
            v2 = Perturb(HexMetrics.TerraceLerp(begin, left, i));
            c2 = HexMetrics.TerraceLerp(beginCell.Color, leftCell.Color, i);
            AddTriangleUnperturbed(st, v1, v2, boundary, c1, c2, boundaryColor);
        }

        AddTriangleUnperturbed(st, v2, Perturb(left), boundary,
            c2, leftCell.Color, boundaryColor);
    }

    /* Part 8: 开放水面三角化 */
    private static void TriangulateOpenWater(HexDirection direction, HexCell cell, SurfaceTool waterSt)
    {
        Vector3 center = cell.Position;
        center.Y = cell.WaterSurfaceY;

        Vector3 c1 = center + HexMetrics.GetFirstWaterCorner(direction);
        Vector3 c2 = center + HexMetrics.GetSecondWaterCorner(direction);

        AddTriangleUnperturbed(waterSt, center, c1, c2, Colors.White, Colors.White, Colors.White);

        if (direction <= HexDirection.SE)
        {
            HexCell neighbor = cell.GetNeighbor(direction);
            if (neighbor != null)
            {
                if (neighbor.IsUnderwater)
                {
                    /* 开放水面连接桥（两个水下 cell 之间） */
                    Vector3 bridge = HexMetrics.GetWaterBridge(direction);
                    Vector3 e1 = c1 + bridge;
                    Vector3 e2 = c2 + bridge;
                    AddQuadUnperturbed(waterSt, c2, c1, e2, e1, Colors.White, Colors.White, Colors.White, Colors.White);
                }
                else
                {
                    /* Part 8.2: 岸边水体（当前水下，邻居不水下） */
                    TriangulateShoreWater(direction, cell, neighbor, waterSt);
                }
            }
        }
    }

    /* Part 8.2: 岸边水体 — 从当前 cell 水面边缘延伸到邻居 solid 边缘。
       WaterFactor(0.6) 与 SolidFactor(0.8) 半径不同，四边形顶点在 XZ 投影上形成凹形，
       不能用 AddQuadUnperturbed（会产生自相交），改用两个独立三角形。 */
    private static void TriangulateShoreWater(HexDirection direction, HexCell cell, HexCell neighbor, SurfaceTool waterSt)
    {
        Vector3 c1 = cell.Position + HexMetrics.GetFirstWaterCorner(direction);
        Vector3 c2 = cell.Position + HexMetrics.GetSecondWaterCorner(direction);
        c1.Y = cell.WaterSurfaceY;
        c2.Y = cell.WaterSurfaceY;

        // 邻居在该方向上的 solid corner（使用 Opposite 方向，从邻居中心看回来）
        Vector3 s1 = neighbor.Position + HexMetrics.GetFirstSolidCorner(direction.Opposite());
        Vector3 s2 = neighbor.Position + HexMetrics.GetSecondSolidCorner(direction.Opposite());

        // 两个三角形拼成 shore 四边形，确保逆时针 winding（法向量朝上）
        AddTriangleUnperturbed(waterSt, c2, s1, c1, Colors.White, Colors.White, Colors.White);
        AddTriangleUnperturbed(waterSt, c2, s2, s1, Colors.White, Colors.White, Colors.White);
    }
}
