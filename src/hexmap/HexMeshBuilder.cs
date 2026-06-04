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
        public Vector3 v1, v2, v3, v4;

        public EdgeVertices(Vector3 corner1, Vector3 corner2)
        {
            v1 = corner1;
            v2 = corner1.Lerp(corner2, 1f / 3f);
            v3 = corner1.Lerp(corner2, 2f / 3f);
            v4 = corner2;
        }

        public static EdgeVertices TerraceLerp(EdgeVertices a, EdgeVertices b, int step)
        {
            EdgeVertices result;
            result.v1 = HexMetrics.TerraceLerp(a.v1, b.v1, step);
            result.v2 = HexMetrics.TerraceLerp(a.v2, b.v2, step);
            result.v3 = HexMetrics.TerraceLerp(a.v3, b.v3, step);
            result.v4 = HexMetrics.TerraceLerp(a.v4, b.v4, step);
            return result;
        }
    }

    // ==================== 公共入口 ====================

    public static Mesh BuildMesh(HexCell[] cells)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);

        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null)
                TriangulateCell(cells[i], st);
        }

        st.GenerateNormals();
        return st.Commit();
    }

    // ==================== Cell / Sector ====================

    private static void TriangulateCell(HexCell cell, SurfaceTool st)
    {
        for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
        {
            Triangulate(st, d, cell);
        }
    }

    private static void Triangulate(SurfaceTool st, HexDirection direction, HexCell cell)
    {
        Vector3 center = cell.Position;
        EdgeVertices e = new EdgeVertices(
            center + HexMetrics.GetFirstSolidCorner(direction),
            center + HexMetrics.GetSecondSolidCorner(direction)
        );

        TriangulateEdgeFan(st, center, e, cell.Color);

        if (direction <= HexDirection.SE)
        {
            TriangulateConnection(st, direction, cell, e);
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

    // ==================== Edge ====================

    private static void TriangulateEdgeFan(SurfaceTool st, Vector3 center, EdgeVertices edge, Color color)
    {
        AddTriangle(st, center, edge.v1, edge.v2, color, color, color);
        AddTriangle(st, center, edge.v2, edge.v3, color, color, color);
        AddTriangle(st, center, edge.v3, edge.v4, color, color, color);
    }

    private static void TriangulateEdgeStrip(SurfaceTool st, EdgeVertices e1, Color c1, EdgeVertices e2, Color c2)
    {
        AddQuad(st, e1.v1, e1.v2, e2.v1, e2.v2, c1, c1, c2, c2);
        AddQuad(st, e1.v2, e1.v3, e2.v2, e2.v3, c1, c1, c2, c2);
        AddQuad(st, e1.v3, e1.v4, e2.v3, e2.v4, c1, c1, c2, c2);
    }

    private static void TriangulateConnection(SurfaceTool st, HexDirection direction, HexCell cell, EdgeVertices e1)
    {
        HexCell neighbor = cell.GetNeighbor(direction);
        if (neighbor == null) return;

        Vector3 bridge = HexMetrics.GetBridge(direction);
        bridge.Y = neighbor.Position.Y - cell.Position.Y;
        EdgeVertices e2 = new EdgeVertices(
            e1.v1 + bridge,
            e1.v4 + bridge
        );

        if (cell.GetEdgeType(direction) == HexEdgeType.Slope)
        {
            TriangulateEdgeTerraces(st, e1, cell, e2, neighbor);
        }
        else
        {
            TriangulateEdgeStrip(st, e1, cell.Color, e2, neighbor.Color);
        }

        HexCell nextNeighbor = cell.GetNeighbor(direction.Next());
        if (direction <= HexDirection.E && nextNeighbor != null)
        {
            Vector3 v5 = e1.v4 + HexMetrics.GetBridge(direction.Next());
            v5.Y = nextNeighbor.Position.Y;

            if (cell.Elevation <= neighbor.Elevation)
            {
                if (cell.Elevation <= nextNeighbor.Elevation)
                {
                    TriangulateCorner(st, e1.v4, cell, e2.v4, neighbor, v5, nextNeighbor);
                }
                else
                {
                    TriangulateCorner(st, v5, nextNeighbor, e1.v4, cell, e2.v4, neighbor);
                }
            }
            else if (neighbor.Elevation <= nextNeighbor.Elevation)
            {
                TriangulateCorner(st, e2.v4, neighbor, v5, nextNeighbor, e1.v4, cell);
            }
            else
            {
                TriangulateCorner(st, v5, nextNeighbor, e1.v4, cell, e2.v4, neighbor);
            }
        }
    }

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
}
