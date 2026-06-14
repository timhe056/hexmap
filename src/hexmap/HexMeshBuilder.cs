using Godot;
using System;
using System.Collections.Generic;

namespace HexMap;

/// <summary>
/// Part 5+8：六边形网格三角化器。
/// 对应教程中的 HexMesh，负责将一组 HexCellData 三角化为 Godot Mesh。
/// 使用 ArrayMesh 列表式构建，与 Unity 教程的 HexMesh 结构对齐。
/// 所有方法均为静态，不依赖任何 Node 实例状态。
/// </summary>
public static class HexMeshBuilder
{
    /* Part 20: cell 混合权重 */
    private static readonly Color Color1 = new Color(1f, 0f, 0f);
    private static readonly Color Color2 = new Color(0f, 1f, 0f);
    private static readonly Color Color3 = new Color(0f, 0f, 1f);
    private static readonly Color Weights1 = new Color(1f, 0f, 0f);
    private static readonly Color Weights2 = new Color(0f, 1f, 0f);
    private static readonly Color Weights3 = new Color(0f, 0f, 1f);
    private static readonly Vector3 IndicesNone = new Vector3(0f, 0f, 0f);

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
        /* Part 20: forward+ 渲染器支持 CUSTOM0，可完整实现 3-cell visibility 混合 */
        public readonly bool UseColors;
        public readonly bool UseUV;
        public readonly bool UseUV2;
        public readonly bool UseCustom0;

        public List<Vector3> Vertices = new List<Vector3>();
        public List<int> Triangles = new List<int>();
        public List<Color> Colors;
        public List<Vector2> UVs;
        public List<Vector2> UV2s;
        public List<Color> Custom0s;

        public MeshData(bool useColors = false, bool useUV = false, bool useUV2 = false, bool useCustom0 = false)
        {
            UseColors = useColors;
            UseUV = useUV;
            UseUV2 = useUV2;
            UseCustom0 = useCustom0;
            if (useColors) Colors = new List<Color>();
            if (useUV) UVs = new List<Vector2>();
            if (useUV2) UV2s = new List<Vector2>();
            if (useCustom0) Custom0s = new List<Color>();
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
            if (UseCustom0)
            {
                // Custom0 RGBA Float: PackedFloat32Array，每顶点 4 个 float，总共 vertex_count * 4 个元素
                var custom0Data = new float[Custom0s.Count * 4];
                for (int i = 0; i < Custom0s.Count; i++)
                {
                    custom0Data[i * 4 + 0] = Custom0s[i].R;
                    custom0Data[i * 4 + 1] = Custom0s[i].G;
                    custom0Data[i * 4 + 2] = Custom0s[i].B;
                    custom0Data[i * 4 + 3] = Custom0s[i].A;
                }
                arrays[(int)Mesh.ArrayType.Custom0] = custom0Data;
            }

            // 构建 ArrayFormat 标志，必须显式声明 Custom0 格式
            // 默认 Rgba8Unorm 期望 PackedByteArray，我们用 RgbaFloat 传 PackedFloat32Array
            Mesh.ArrayFormat flags = Mesh.ArrayFormat.FormatVertex | Mesh.ArrayFormat.FormatIndex;
            if (UseColors) flags |= Mesh.ArrayFormat.FormatColor;
            if (UseUV) flags |= Mesh.ArrayFormat.FormatTexUV;
            if (UseUV2) flags |= Mesh.ArrayFormat.FormatTexUV2;
            if (UseCustom0)
            {
                flags |= Mesh.ArrayFormat.FormatCustom0;
                flags |= (Mesh.ArrayFormat)((int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift);
            }

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, flags: flags);

            // 使用 SurfaceTool 生成法线（Godot 4 ArrayMesh 无直接生成法线 API）
            var st = new SurfaceTool();
            st.CreateFrom(mesh, 0);
            st.GenerateNormals();
            return st.Commit();
        }
    }

    // ==================== 公共入口 ====================

    /* Part 8-10: 六路输出：terrain, rivers, roads, water, waterShore, estuaries
       传入 features 用于 Part 10 城墙构建 */
    public static void BuildMeshes(int[] cellIndices, HexGrid grid, HexFeatureManager features,
        out Mesh terrainMesh, out Mesh riverMesh, out Mesh roadMesh,
        out Mesh waterMesh, out Mesh waterShoreMesh, out Mesh estuaryMesh)
    {
        /* Part 20: terrain 改用 CellData（COLOR=weights, UV/UV2=indices） */
        var terrain = new MeshData(useColors: true, useUV: true, useUV2: true);
        /* Part 20: rivers/roads 也用 COLOR+UV2 传 cell visibility 混合权重与 indices */
        var rivers = new MeshData(useColors: true, useUV: true, useUV2: true);
        var roads = new MeshData(useColors: true, useUV: true, useUV2: true);
        /* Part 20 (forward+): water/waterShore/estuary 用 CUSTOM0 传 indices，COLOR 传 weights */
        var water = new MeshData(useColors: true, useCustom0: true);
        var waterShore = new MeshData(useUV: true, useColors: true, useCustom0: true);
        var estuaries = new MeshData(useUV: true, useUV2: true, useColors: true, useCustom0: true);

        for (int i = 0; i < cellIndices.Length; i++)
        {
            int index = cellIndices[i];
            if (index >= 0)
            {
                HexCellData cell = grid.CellData[index];
                Vector3 center = grid.CellPositions[index];
                TriangulateCell(cell, index, center, grid, features, terrain, rivers, roads, water, waterShore, estuaries);
            }
        }

        terrainMesh = terrain.ToMesh();
        riverMesh = rivers.ToMesh();
        roadMesh = roads.ToMesh();
        waterMesh = water.ToMesh();
        waterShoreMesh = waterShore.ToMesh();
        estuaryMesh = estuaries.ToMesh();
    }

    public static Mesh BuildMesh(int[] cellIndices, HexGrid grid)
    {
        BuildMeshes(cellIndices, grid, null, out Mesh terrainMesh, out _, out _, out _, out _, out _);
        return terrainMesh;
    }

    // ==================== Cell / Sector ====================

    private static void TriangulateCell(HexCellData cell, int cellIndex, Vector3 center, HexGrid grid, HexFeatureManager features,
        MeshData terrain, MeshData rivers, MeshData roads,
        MeshData water, MeshData waterShore, MeshData estuaries)
    {
        for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
        {
            Triangulate(features, grid, terrain, rivers, roads, water, waterShore, estuaries, d, cell, cellIndex, center);
        }
    }

    private static void Triangulate(HexFeatureManager features, HexGrid grid,
        MeshData terrain, MeshData rivers, MeshData roads,
        MeshData water, MeshData waterShore, MeshData estuaries, HexDirection direction, HexCellData cell, int cellIndex, Vector3 center)
    {
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
                    TriangulateWithRiverBeginOrEnd(terrain, rivers, cell, cellIndex, center, e);
                }
                else
                {
                    TriangulateWithRiver(terrain, rivers, direction, cell, cellIndex, center, e);
                }
            }
            else
            {
                /* Part 7: 透传 roads */
                TriangulateAdjacentToRiver(features, terrain, rivers, roads, direction, cell, cellIndex, center, e);
            }
        }
        else
        {
            /* Part 7: 使用道路感知的三角化方法 */
            TriangulateWithoutRiver(terrain, roads, direction, cell, cellIndex, center, e);
        }

        if (direction <= HexDirection.SE)
        {
            /* Part 7: 透传 roads */
            TriangulateConnection(features, grid, terrain, rivers, roads, direction, cell, cellIndex, center, e);
        }

        /* Part 8: 开放水面三角化 */
        if (cell.IsUnderwater)
        {
            TriangulateOpenWater(direction, cell, cellIndex, center, grid, water, waterShore, estuaries);
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

    /* Part 20: cell data — 用 COLOR 传混合权重，UV/UV2 传 cell index */
    private static void AddTriangleCellData(MeshData md, Vector3 indices, Color w1, Color w2, Color w3)
    {
        if (md.UseColors)
        {
            md.Colors.Add(w1);
            md.Colors.Add(w2);
            md.Colors.Add(w3);
        }
        if (md.UseUV)
        {
            var uv = new Vector2(indices.X, indices.Y);
            md.UVs.Add(uv);
            md.UVs.Add(uv);
            md.UVs.Add(uv);
        }
        if (md.UseUV2)
        {
            var uv2 = new Vector2(indices.Z, 0f);
            md.UV2s.Add(uv2);
            md.UV2s.Add(uv2);
            md.UV2s.Add(uv2);
        }
    }

    private static void AddQuadCellData(MeshData md, Vector3 indices, Color w1, Color w2, Color w3, Color w4)
    {
        if (md.UseColors)
        {
            md.Colors.Add(w1); // v1
            md.Colors.Add(w4); // v4
            md.Colors.Add(w2); // v2
            md.Colors.Add(w3); // v3
        }
        if (md.UseUV)
        {
            var uv = new Vector2(indices.X, indices.Y);
            md.UVs.Add(uv); // v1
            md.UVs.Add(uv); // v4
            md.UVs.Add(uv); // v2
            md.UVs.Add(uv); // v3
        }
        if (md.UseUV2)
        {
            var uv2 = new Vector2(indices.Z, 0f);
            md.UV2s.Add(uv2); // v1
            md.UV2s.Add(uv2); // v4
            md.UV2s.Add(uv2); // v2
            md.UV2s.Add(uv2); // v3
        }
    }

    /* Part 20: 辅助方法 — 从 cell index 直接生成 CellData */
    private static void AddTriangleCellData(MeshData md, int c1, int c2, int c3)
    {
        Vector3 indices;
        indices.X = c1;
        indices.Y = c2;
        indices.Z = c3;
        AddTriangleCellData(md, indices, Weights1, Weights2, Weights3);
    }

    private static void AddQuadCellData(MeshData md, int c1, int c2, int c3, int c4)
    {
        Vector3 indices;
        indices.X = c1;
        indices.Y = c2;
        indices.Z = c3;
        AddQuadCellData(md, indices, Weights1, Weights1, Weights2, Weights3);
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

    private static void AddTriangleUnperturbed(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3)
    {
        int vi = md.Vertices.Count;
        md.Vertices.Add(v1);
        md.Vertices.Add(v2);
        md.Vertices.Add(v3);
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 1);
        md.Triangles.Add(vi + 2);
    }

    private static void AddTriangleColor(MeshData md, Color c1, Color c2, Color c3)
    {
        /* Part 20: Colors 已改为 CellWeights，由 AddTriangleCellData 统一管理 */
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

    /* Part 20 (forward+): 水面专用图元 — CUSTOM0 存 3-cell indices，COLOR 存 weights */
    private static void AddWaterTriangle(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3,
        Vector3 indices, Color w1, Color w2, Color w3)
    {
        int vi = md.Vertices.Count;
        md.Vertices.Add(Perturb(v1));
        md.Vertices.Add(Perturb(v2));
        md.Vertices.Add(Perturb(v3));
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 1);
        md.Triangles.Add(vi + 2);
        if (md.UseCustom0)
        {
            Color c0 = new Color(indices.X, indices.Y, indices.Z, 0f);
            md.Custom0s.Add(c0); md.Custom0s.Add(c0); md.Custom0s.Add(c0);
        }
        if (md.UseColors)
        {
            md.Colors.Add(w1); md.Colors.Add(w2); md.Colors.Add(w3);
        }
    }

    private static void AddWaterTriangle(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, float cellIndex)
    {
        Vector3 indices = new Vector3(cellIndex, cellIndex, cellIndex);
        AddWaterTriangle(md, v1, v2, v3, indices, Weights1, Weights1, Weights1);
    }

    private static void AddWaterQuad(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        Vector3 indices, Color w1, Color w2, Color w3, Color w4)
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
        if (md.UseCustom0)
        {
            Color c0 = new Color(indices.X, indices.Y, indices.Z, 0f);
            md.Custom0s.Add(c0); md.Custom0s.Add(c0); md.Custom0s.Add(c0); md.Custom0s.Add(c0);
        }
        if (md.UseColors)
        {
            md.Colors.Add(w1); md.Colors.Add(w4); md.Colors.Add(w2); md.Colors.Add(w3);
        }
    }

    private static void AddWaterQuad(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, float cellIndex)
    {
        Vector3 indices = new Vector3(cellIndex, cellIndex, cellIndex);
        AddWaterQuad(md, v1, v2, v3, v4, indices, Weights1, Weights1, Weights1, Weights1);
    }

    private static void AddQuadColor(MeshData md, Color c1, Color c2, Color c3, Color c4)
    {
        /* Part 20: Colors 已废弃 */
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

    /* Part 20 (forward+): 岸边水体专用 — CUSTOM0 存 indices，COLOR 存 weights，UV.y 存 shore */
    private static void AddShoreTriangle(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3,
        Vector3 indices, Color w1, Color w2, Color w3,
        float shore1, float shore2, float shore3)
    {
        int vi = md.Vertices.Count;
        md.Vertices.Add(Perturb(v1));
        md.Vertices.Add(Perturb(v2));
        md.Vertices.Add(Perturb(v3));
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 1);
        md.Triangles.Add(vi + 2);
        if (md.UseCustom0)
        {
            Color c0 = new Color(indices.X, indices.Y, indices.Z, 0f);
            md.Custom0s.Add(c0); md.Custom0s.Add(c0); md.Custom0s.Add(c0);
        }
        if (md.UseColors)
        {
            md.Colors.Add(w1); md.Colors.Add(w2); md.Colors.Add(w3);
        }
        if (md.UseUV)
        {
            md.UVs.Add(new Vector2(0f, shore1));
            md.UVs.Add(new Vector2(0f, shore2));
            md.UVs.Add(new Vector2(0f, shore3));
        }
    }

    private static void AddShoreTriangle(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3,
        float shore1, float shore2, float shore3, float cellIndex)
    {
        Vector3 indices = new Vector3(cellIndex, cellIndex, cellIndex);
        AddShoreTriangle(md, v1, v2, v3, indices, Weights1, Weights1, Weights1, shore1, shore2, shore3);
    }

    private static void AddShoreQuad(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        Vector3 indices, Color w1, Color w2, Color w3, Color w4,
        float vMin, float vMax)
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
        if (md.UseCustom0)
        {
            Color c0 = new Color(indices.X, indices.Y, indices.Z, 0f);
            md.Custom0s.Add(c0); md.Custom0s.Add(c0); md.Custom0s.Add(c0); md.Custom0s.Add(c0);
        }
        if (md.UseColors)
        {
            md.Colors.Add(w1); md.Colors.Add(w4); md.Colors.Add(w2); md.Colors.Add(w3);
        }
        if (md.UseUV)
        {
            md.UVs.Add(new Vector2(0f, vMin));
            md.UVs.Add(new Vector2(0f, vMax));
            md.UVs.Add(new Vector2(0f, vMin));
            md.UVs.Add(new Vector2(0f, vMax));
        }
    }

    private static void AddShoreQuad(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        float vMin, float vMax, float cellIndex)
    {
        Vector3 indices = new Vector3(cellIndex, cellIndex, cellIndex);
        AddShoreQuad(md, v1, v2, v3, v4, indices, Weights1, Weights1, Weights1, Weights1, vMin, vMax);
    }

    private static void AddQuadUnperturbed(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4)
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
    }

    private static void AddQuad(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        Color c1, Color c2, Color c3, Color c4)
    {
        /* Part 20: 带 Color 的 AddQuad 已废弃 */
        AddQuad(md, v1, v2, v3, v4);
    }

    /* Part 7: 道路网格使用 UV 而非顶点颜色 */
    /* Part 20: 新增带 cell visibility 数据的重载（indices=UV2, Color.xy=weights） */
    private static void AddRoadQuad(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, float uMin, float uMax)
    {
        AddRoadQuad(md, v1, v2, v3, v4, uMin, uMax, Vector2.Zero, new Color(1f, 0f, 0f, 1f), new Color(1f, 0f, 0f, 1f), new Color(1f, 0f, 0f, 1f), new Color(1f, 0f, 0f, 1f));
    }

    private static void AddRoadQuad(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, float uMin, float uMax,
        Vector2 indices, Color w1, Color w2, Color w3, Color w4)
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
        if (md.UseUV2)
        {
            md.UV2s.Add(indices);
            md.UV2s.Add(indices);
            md.UV2s.Add(indices);
            md.UV2s.Add(indices);
        }
        if (md.UseColors)
        {
            md.Colors.Add(w1); // v1
            md.Colors.Add(w4); // v4
            md.Colors.Add(w2); // v2
            md.Colors.Add(w3); // v3
        }
    }

    private static void AddRoadTriangle(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3, Vector2 uv1, Vector2 uv2, Vector2 uv3)
    {
        AddRoadTriangle(md, v1, v2, v3, uv1, uv2, uv3, Vector2.Zero, new Color(1f, 0f, 0f, 1f), new Color(1f, 0f, 0f, 1f), new Color(1f, 0f, 0f, 1f));
    }

    private static void AddRoadTriangle(MeshData md, Vector3 v1, Vector3 v2, Vector3 v3,
        Vector2 uv1, Vector2 uv2, Vector2 uv3, Vector2 indices, Color w1, Color w2, Color w3)
    {
        int vi = md.Vertices.Count;
        md.Vertices.Add(Perturb(v1));
        md.Vertices.Add(Perturb(v2));
        md.Vertices.Add(Perturb(v3));
        md.Triangles.Add(vi);
        md.Triangles.Add(vi + 1);
        md.Triangles.Add(vi + 2);
        if (md.UseUV)
        {
            md.UVs.Add(uv1);
            md.UVs.Add(uv2);
            md.UVs.Add(uv3);
        }
        if (md.UseUV2)
        {
            md.UV2s.Add(indices);
            md.UV2s.Add(indices);
            md.UV2s.Add(indices);
        }
        if (md.UseColors)
        {
            md.Colors.Add(w1);
            md.Colors.Add(w2);
            md.Colors.Add(w3);
        }
    }

    // ==================== Edge ====================

    private static void TriangulateEdgeFan(MeshData md, Vector3 center, EdgeVertices edge, float index)
    {
        AddTriangle(md, center, edge.v1, edge.v2);
        AddTriangle(md, center, edge.v2, edge.v3);
        AddTriangle(md, center, edge.v3, edge.v4);
        AddTriangle(md, center, edge.v4, edge.v5);

        Vector3 indices;
        indices.X = indices.Y = indices.Z = index;
        AddTriangleCellData(md, indices, Weights1, Weights1, Weights1);
        AddTriangleCellData(md, indices, Weights1, Weights1, Weights1);
        AddTriangleCellData(md, indices, Weights1, Weights1, Weights1);
        AddTriangleCellData(md, indices, Weights1, Weights1, Weights1);
    }

    /* Part 7: 新增 hasRoad 参数，有道路时在中间 quad 段画路面 */
    /* Part 20: 参数 type1/type2 改为 index1/index2，用 CellData 替代 Colors/TerrainTypes */
    private static void TriangulateEdgeStrip(MeshData md,
        EdgeVertices e1, float index1,
        EdgeVertices e2, float index2, bool hasRoad = false)
    {
        AddQuad(md, e1.v1, e1.v2, e2.v1, e2.v2);
        AddQuad(md, e1.v2, e1.v3, e2.v2, e2.v3);
        AddQuad(md, e1.v3, e1.v4, e2.v3, e2.v4);
        AddQuad(md, e1.v4, e1.v5, e2.v4, e2.v5);

        Vector3 indices;
        indices.X = indices.Z = index1;
        indices.Y = index2;
        AddQuadCellData(md, indices, Weights1, Weights1, Weights2, Weights2);
        AddQuadCellData(md, indices, Weights1, Weights1, Weights2, Weights2);
        AddQuadCellData(md, indices, Weights1, Weights1, Weights2, Weights2);
        AddQuadCellData(md, indices, Weights1, Weights1, Weights2, Weights2);
    }

    /* Part 7-10: 新增 roads 参数，在连接处传入道路信息，同时触发城墙构建 */
    private static void TriangulateConnection(HexFeatureManager features, HexGrid grid,
        MeshData terrain, MeshData rivers, MeshData roads, HexDirection direction, HexCellData cell, int cellIndex, Vector3 center, EdgeVertices e1)
    {
        if (!grid.TryGetCellIndex(cell.coordinates.Step(direction), out int neighborIndex))
        {
            return;
        }
        HexCellData neighbor = grid.CellData[neighborIndex];
        Vector3 neighborPosition = grid.CellPositions[neighborIndex];

        Vector3 bridge = HexMetrics.GetBridge(direction);
        bridge.Y = neighborPosition.Y - center.Y;
        EdgeVertices e2 = new EdgeVertices(
            e1.v1 + bridge,
            e1.v5 + bridge
        );

        bool hasRiver = cell.HasRiverThroughEdge(direction);

        // Part 6: 河流通过连接
        if (hasRiver)
        {
            e2.v3 = new Vector3(e2.v3.X, neighbor.StreamBedY, e2.v3.Z);
            /* Part 8: 水下隐藏河流 / 瀑布 */
            if (!cell.IsUnderwater)
            {
                if (!neighbor.IsUnderwater)
                {
                    Vector2 riverIndices = new Vector2(cellIndex, neighborIndex);
                    TriangulateRiverQuad(rivers,
                        e1.v2, e1.v4, e2.v2, e2.v4,
                        cell.RiverSurfaceY, neighbor.RiverSurfaceY, 0.8f,
                        cell.HasIncomingRiverThroughEdge(direction),
                        riverIndices
                    );
                }
                /* 按教程：仅当 cell 海拔高于 neighbor 水位时才画瀑布 */
                else if (cell.Elevation > neighbor.WaterLevel)
                {
                    /* 瀑布：cell 在水上，neighbor 在水下 */
                    TriangulateWaterfallInWater(rivers,
                        e1.v2, e1.v4, e2.v2, e2.v4,
                        cell.RiverSurfaceY, neighbor.RiverSurfaceY, neighbor.WaterSurfaceY,
                        new Vector2(cellIndex, neighborIndex));
                }
            }
            /* 按教程：反向瀑布需要 neighbor 海拔高于 cell 水位 */
            else if (!neighbor.IsUnderwater && neighbor.Elevation > cell.WaterLevel)
            {
                /* 反向瀑布：neighbor 在水上，cell 在水下 */
                TriangulateWaterfallInWater(rivers,
                    e2.v4, e2.v2, e1.v4, e1.v2,
                    neighbor.RiverSurfaceY, cell.RiverSurfaceY, cell.WaterSurfaceY,
                    new Vector2(neighborIndex, cellIndex));
            }
        }

        /* Part 7: 获取道路信息 */
        bool hasRoad = cell.HasRoadThroughEdge(direction);

        if (cell.GetEdgeType(neighbor) == HexEdgeType.Slope)
        {
            TriangulateEdgeTerraces(terrain, e1, cellIndex, e2, neighborIndex);
        }
        else
        {
            TriangulateEdgeStrip(terrain, e1, cellIndex, e2, neighborIndex);
        }

        /* Part 7: 道路单独画到 roads 网格 */
        if (hasRoad)
        {
            Vector2 roadIndices = new Vector2(cellIndex, neighborIndex);
            TriangulateRoadSegment(roads, e1.v2, e1.v3, e1.v4, e2.v2, e2.v3, e2.v4, roadIndices);
        }

        if (direction <= HexDirection.E && grid.TryGetCellIndex(cell.coordinates.Step(direction.Next()), out int nextNeighborIndex))
        {
            HexCellData nextNeighbor = grid.CellData[nextNeighborIndex];
            Vector3 nextNeighborPosition = grid.CellPositions[nextNeighborIndex];
            Vector3 v5 = e1.v5 + HexMetrics.GetBridge(direction.Next());
            v5.Y = nextNeighborPosition.Y;

            if (cell.Elevation <= neighbor.Elevation)
            {
                if (cell.Elevation <= nextNeighbor.Elevation)
                {
                    TriangulateCorner(features, terrain, e1.v5, cellIndex, cell, e2.v5, neighborIndex, neighbor, v5, nextNeighborIndex, nextNeighbor);
                }
                else
                {
                    TriangulateCorner(features, terrain, v5, nextNeighborIndex, nextNeighbor, e1.v5, cellIndex, cell, e2.v5, neighborIndex, neighbor);
                }
            }
            else if (neighbor.Elevation <= nextNeighbor.Elevation)
            {
                TriangulateCorner(features, terrain, e2.v5, neighborIndex, neighbor, v5, nextNeighborIndex, nextNeighbor, e1.v5, cellIndex, cell);
            }
            else
            {
                TriangulateCorner(features, terrain, v5, nextNeighborIndex, nextNeighbor, e1.v5, cellIndex, cell, e2.v5, neighborIndex, neighbor);
            }
        }

        /* Part 10: 在连接处添加城墙 */
        features?.AddWall(e1, cell, e2, neighbor, hasRiver, hasRoad);
    }

    /* Part 7: 新增 hasRoad 参数，透传到 TriangulateEdgeStrip */
    private static void TriangulateEdgeTerraces(MeshData md,
        EdgeVertices begin, int beginCellIndex,
        EdgeVertices end, int endCellIndex)
    {
        EdgeVertices e2 = EdgeVertices.TerraceLerp(begin, end, 1);
        Color c2 = HexMetrics.TerraceLerp(Color1, Color2, 1);
        float t1 = beginCellIndex;
        float t2 = endCellIndex;

        TriangulateEdgeStrip(md, begin, t1, e2, t2);

        for (int i = 2; i < HexMetrics.TerraceSteps; i++)
        {
            EdgeVertices e1 = e2;
            Color c1 = c2;
            e2 = EdgeVertices.TerraceLerp(begin, end, i);
            c2 = HexMetrics.TerraceLerp(Color1, Color2, i);
            TriangulateEdgeStrip(md, e1, t1, e2, t2);
        }

        TriangulateEdgeStrip(md, e2, t1, end, t2);
    }

    /* Part 7: 有道路时在河流相邻侧画道路 */
    private static void TriangulateRoadAdjacentToRiver(HexFeatureManager features,
        MeshData terrain, MeshData roads, HexDirection direction, HexCellData cell, int cellIndex, Vector3 center, EdgeVertices e)
    {
        bool hasRoadThroughEdge = cell.HasRoadThroughEdge(direction);
        bool previousHasRiver = cell.HasRiverThroughEdge(direction.Previous());
        bool nextHasRiver = cell.HasRiverThroughEdge(direction.Next());

        Vector2 interpolators = GetRoadInterpolators(direction, cell);
        Vector3 roadCenter = center;

        HexDirection riverIn = cell.IncomingRiver;
        HexDirection riverOut = cell.OutgoingRiver;

        if (cell.HasRiverBeginOrEnd)
        {
            roadCenter += HexMetrics.GetSolidEdgeMiddle(
                (cell.HasIncomingRiver ? riverIn : riverOut).Opposite()
            ) * (1f / 3f);
        }
        else if (riverIn == riverOut.Opposite())
        {
            Vector3 corner;
            if (previousHasRiver)
            {
                if (!hasRoadThroughEdge && !cell.HasRoadThroughEdge(direction.Next()))
                {
                    return;
                }
                corner = HexMetrics.GetSecondSolidCorner(direction);
            }
            else
            {
                if (!hasRoadThroughEdge && !cell.HasRoadThroughEdge(direction.Previous()))
                {
                    return;
                }
                corner = HexMetrics.GetFirstSolidCorner(direction);
            }
            roadCenter += corner * 0.5f;
            if (riverIn == direction.Next() && (
                cell.HasRoadThroughEdge(direction.Next2()) ||
                cell.HasRoadThroughEdge(direction.Opposite())
            ))
            {
                features?.AddBridge(roadCenter, center - corner * 0.5f);
            }
            center += corner * 0.25f;
        }
        else if (riverIn == riverOut.Previous())
        {
            roadCenter -= HexMetrics.GetSecondCorner(riverIn) * 0.2f;
        }
        else if (riverIn == riverOut.Next())
        {
            roadCenter -= HexMetrics.GetFirstCorner(riverIn) * 0.2f;
        }
        else if (previousHasRiver && nextHasRiver)
        {
            if (!hasRoadThroughEdge)
            {
                return;
            }
            Vector3 offset = HexMetrics.GetSolidEdgeMiddle(direction) * HexMetrics.InnerToOuter;
            roadCenter += offset * 0.7f;
            center += offset * 0.5f;
        }
        else
        {
            HexDirection middle;
            if (previousHasRiver)
            {
                middle = direction.Next();
            }
            else if (nextHasRiver)
            {
                middle = direction.Previous();
            }
            else
            {
                middle = direction;
            }
            if (!cell.HasRoadThroughEdge(middle) &&
                !cell.HasRoadThroughEdge(middle.Previous()) &&
                !cell.HasRoadThroughEdge(middle.Next()))
            {
                return;
            }
            Vector3 offset = HexMetrics.GetSolidEdgeMiddle(middle);
            roadCenter += offset * 0.25f;
            if (direction == middle && cell.HasRoadThroughEdge(direction.Opposite()))
            {
                features?.AddBridge(roadCenter, center - offset * (HexMetrics.InnerToOuter * 0.7f));
            }
        }

        Vector3 mL = roadCenter.Lerp(e.v1, interpolators.X);
        Vector3 mR = roadCenter.Lerp(e.v5, interpolators.Y);
        Vector2 roadIndices = new Vector2(cellIndex, cellIndex);
        TriangulateRoad(roads, roadCenter, mL, mR, e, hasRoadThroughEdge, roadIndices);
        if (previousHasRiver)
        {
            TriangulateRoadEdge(roads, roadCenter, center, mL, roadIndices);
        }
        if (nextHasRiver)
        {
            TriangulateRoadEdge(roads, roadCenter, mR, center, roadIndices);
        }
    }

    /* Part 7: 无河流时画道路（如果存在） */
    private static void TriangulateWithoutRiver(MeshData terrain, MeshData roads, HexDirection direction, HexCellData cell, int cellIndex, Vector3 center, EdgeVertices e)
    {
        TriangulateEdgeFan(terrain, center, e, cellIndex);

        if (cell.HasRoads)
        {
            Vector2 interpolators = GetRoadInterpolators(direction, cell);
            Vector2 roadIndices = new Vector2(cellIndex, cellIndex);
            TriangulateRoad(roads,
                center,
                center.Lerp(e.v1, interpolators.X),
                center.Lerp(e.v5, interpolators.Y),
                e,
                cell.HasRoadThroughEdge(direction),
                roadIndices
            );
        }
    }

    /* Part 7: 确定左右中点插值系数 */
    private static Vector2 GetRoadInterpolators(HexDirection direction, HexCellData cell)
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
    private static void TriangulateRoad(MeshData roads, Vector3 center, Vector3 mL, Vector3 mR, EdgeVertices e, bool hasRoadThroughCellEdge, Vector2 indices)
    {
        Color wCell = new Color(1f, 0f, 0f);
        if (hasRoadThroughCellEdge)
        {
            Vector3 mC = mL.Lerp(mR, 0.5f);
            TriangulateRoadSegment(roads, mL, mC, mR, e.v2, e.v3, e.v4, indices);
            AddRoadTriangle(roads, center, mL, mC,
                new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(1f, 0f),
                indices, wCell, wCell, wCell);
            AddRoadTriangle(roads, center, mC, mR,
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
                indices, wCell, wCell, wCell);
        }
        else
        {
            TriangulateRoadEdge(roads, center, mL, mR, indices);
        }
    }

    /* Part 7: 画两个连接的 quad 作为道路段 */
    /* Part 20: v1,v2,v3 = cell0 侧，v4,v5,v6 = cell1 侧；UV2 存两个 cell index */
    private static void TriangulateRoadSegment(MeshData roads, Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4, Vector3 v5, Vector3 v6, Vector2 indices)
    {
        Color w0 = new Color(1f, 0f, 0f); // cell0 only
        Color w1 = new Color(0f, 1f, 0f); // cell1 only
        // AddRoadQuad 内部顶点顺序 v1,v4,v2,v3；传入顺序 v1,v2,v3,v4 分别对应 e1,e1,e2,e2
        AddRoadQuad(roads, v1, v2, v4, v5, 0f, 1f, indices, w0, w0, w1, w1);
        AddRoadQuad(roads, v2, v3, v5, v6, 1f, 0f, indices, w0, w0, w1, w1);
    }

    /* Part 7: 仅画路沿三角形 */
    private static void TriangulateRoadEdge(MeshData roads, Vector3 center, Vector3 mL, Vector3 mR, Vector2 indices)
    {
        Color wCell = new Color(1f, 0f, 0f);
        AddRoadTriangle(roads, center, mL, mR,
            new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
            indices, wCell, wCell, wCell);
    }

    // ==================== River ====================

    private static void TriangulateWithRiver(MeshData terrain, MeshData rivers,
        HexDirection direction, HexCellData cell, int cellIndex, Vector3 center, EdgeVertices e)
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

        TriangulateEdgeStrip(terrain, m, cellIndex, e, cellIndex);

        AddTriangle(terrain, centerL, m.v1, m.v2);
        AddQuad(terrain, centerL, center, m.v2, m.v3);
        AddQuad(terrain, center, centerR, m.v3, m.v4);
        AddTriangle(terrain, centerR, m.v4, m.v5);

        Vector3 riverIndices;
        riverIndices.X = riverIndices.Y = riverIndices.Z = cellIndex;
        AddTriangleCellData(terrain, riverIndices, Weights1, Weights1, Weights1);
        AddQuadCellData(terrain, riverIndices, Weights1, Weights1, Weights1, Weights1);
        AddQuadCellData(terrain, riverIndices, Weights1, Weights1, Weights1, Weights1);
        AddTriangleCellData(terrain, riverIndices, Weights1, Weights1, Weights1);

        /* Part 8: 水下隐藏河流 */
        if (!cell.IsUnderwater)
        {
            bool reversed = cell.HasIncomingRiverThroughEdge(direction);
            Vector2 visIndices = new Vector2(cellIndex, cellIndex);
            TriangulateRiverQuad(rivers, centerL, centerR, m.v2, m.v4, cell.RiverSurfaceY, 0.4f, reversed, visIndices);
            TriangulateRiverQuad(rivers, m.v2, m.v4, e.v2, e.v4, cell.RiverSurfaceY, 0.6f, reversed, visIndices);
        }
    }

    private static void TriangulateWithRiverBeginOrEnd(MeshData terrain, MeshData rivers,
        HexCellData cell, int cellIndex, Vector3 center, EdgeVertices e)
    {
        EdgeVertices m = new EdgeVertices(
            center.Lerp(e.v1, 0.5f),
            center.Lerp(e.v5, 0.5f)
        );
        m.v3 = new Vector3(m.v3.X, e.v3.Y, m.v3.Z);

        TriangulateEdgeStrip(terrain, m, cellIndex, e, cellIndex);
        TriangulateEdgeFan(terrain, center, m, cellIndex);

        /* Part 8: 水下隐藏河流 */
        if (!cell.IsUnderwater)
        {
            bool reversed = cell.HasIncomingRiver;
            Vector2 visIndices = new Vector2(cellIndex, cellIndex);
            TriangulateRiverQuad(rivers, m.v2, m.v4, e.v2, e.v4, cell.RiverSurfaceY, 0.6f, reversed, visIndices);

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

            // Part 20: 补充 cell data（Colors + UV2s）
            Color w1 = Weights1;
            if (rivers.UseColors)
            {
                rivers.Colors.Add(w1); rivers.Colors.Add(w1); rivers.Colors.Add(w1);
            }
            if (rivers.UseUV2)
            {
                var uv2 = new Vector2(cellIndex, 0f);
                rivers.UV2s.Add(uv2); rivers.UV2s.Add(uv2); rivers.UV2s.Add(uv2);
            }
        }
    }

    /* Part 7: 新增 roads 参数，有道路时调用 TriangulateRoadAdjacentToRiver */
    private static void TriangulateAdjacentToRiver(HexFeatureManager features,
        MeshData terrain, MeshData rivers, MeshData roads,
        HexDirection direction, HexCellData cell, int cellIndex, Vector3 center, EdgeVertices e)
    {
        /* Part 7: 有道路时在河流相邻侧画道路 */
        if (cell.HasRoads)
        {
            TriangulateRoadAdjacentToRiver(features, terrain, roads, direction, cell, cellIndex, center, e);
        }

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

        TriangulateEdgeStrip(terrain, m, cellIndex, e, cellIndex);
        TriangulateEdgeFan(terrain, center, m, cellIndex);

        if (!cell.IsUnderwater && !cell.HasRoadThroughEdge(direction))
        {
            features?.AddFeature(cell, (center + e.v1 + e.v5) * (1f / 3f));
        }
    }

    /* Part 20: 河流 quad 增加 cell visibility 数据（indices=UV2, Color.xy=weights） */
    private static void TriangulateRiverQuad(MeshData md,
        Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        float y1, float y2, float v, bool reversed, Vector2 indices)
    {
        v1.Y = v2.Y = y1;
        v3.Y = v4.Y = y2;

        Vector3 p1 = Perturb(v1);
        Vector3 p2 = Perturb(v2);
        Vector3 p3 = Perturb(v3);
        Vector3 p4 = Perturb(v4);

        Color w1 = new Color(1f, 0f, 0f);
        Color w2 = new Color(0f, 1f, 0f);

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

        if (md.UseUV2)
        {
            for (int i = 0; i < 6; i++) md.UV2s.Add(indices);
        }
        if (md.UseColors)
        {
            // v1,v2 属于 cell0；v3,v4 属于 cell1（对应几何顶点顺序）
            md.Colors.Add(w1); md.Colors.Add(w1); md.Colors.Add(w2);
            md.Colors.Add(w1); md.Colors.Add(w2); md.Colors.Add(w2);
        }
    }

    private static void TriangulateRiverQuad(MeshData md,
        Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        float y, float v, bool reversed, Vector2 indices)
    {
        TriangulateRiverQuad(md, v1, v2, v3, v4, y, y, v, reversed, indices);
    }

    /* Part 8: 瀑布三角剖分 — 河流穿过水陆边界时，缩短四边形到水面高度。
       使用 AddQuadUnperturbed + 手动 UV，匹配教程 (u: 0→1, v: 0.8→1)。
       Part 20: 增加 cell visibility 数据。 */
    private static void TriangulateWaterfallInWater(MeshData md,
        Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4,
        float y1, float y2, float waterY, Vector2 indices)
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

        if (md.UseUV2)
        {
            for (int i = 0; i < 6; i++) md.UV2s.Add(indices);
        }
        if (md.UseColors)
        {
            Color w1 = new Color(1f, 0f, 0f);
            Color w2 = new Color(0f, 1f, 0f);
            // v1,v2 属于 cell0；v3,v4 属于 cell1（按几何顶点顺序）
            md.Colors.Add(w1); md.Colors.Add(w2); md.Colors.Add(w1);
            md.Colors.Add(w1); md.Colors.Add(w2); md.Colors.Add(w2);
        }
    }

    // ==================== Corner ====================

    private static void TriangulateCorner(HexFeatureManager features, MeshData md,
        Vector3 bottom, int bottomCellIndex, HexCellData bottomCell,
        Vector3 left, int leftCellIndex, HexCellData leftCell,
        Vector3 right, int rightCellIndex, HexCellData rightCell)
    {
        HexEdgeType leftEdgeType = bottomCell.GetEdgeType(leftCell);
        HexEdgeType rightEdgeType = bottomCell.GetEdgeType(rightCell);

        if (leftEdgeType == HexEdgeType.Slope)
        {
            if (rightEdgeType == HexEdgeType.Slope)
            {
                TriangulateCornerTerraces(md, bottom, bottomCellIndex, left, leftCellIndex, right, rightCellIndex);
            }
            else if (rightEdgeType == HexEdgeType.Flat)
            {
                TriangulateCornerTerraces(md, left, leftCellIndex, right, rightCellIndex, bottom, bottomCellIndex);
            }
            else
            {
                TriangulateCornerTerracesCliff(md, bottom, bottomCellIndex, bottomCell, left, leftCellIndex, leftCell, right, rightCellIndex, rightCell);
            }
        }
        else if (rightEdgeType == HexEdgeType.Slope)
        {
            if (leftEdgeType == HexEdgeType.Flat)
            {
                TriangulateCornerTerraces(md, right, rightCellIndex, bottom, bottomCellIndex, left, leftCellIndex);
            }
            else
            {
                TriangulateCornerCliffTerraces(md, bottom, bottomCellIndex, bottomCell, left, leftCellIndex, leftCell, right, rightCellIndex, rightCell);
            }
        }
        else if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            if (leftCell.Elevation < rightCell.Elevation)
            {
                TriangulateCornerCliffTerraces(md, right, rightCellIndex, rightCell, bottom, bottomCellIndex, bottomCell, left, leftCellIndex, leftCell);
            }
            else
            {
                TriangulateCornerTerracesCliff(md, left, leftCellIndex, leftCell, right, rightCellIndex, rightCell, bottom, bottomCellIndex, bottomCell);
            }
        }
        else
        {
            AddTriangle(md, bottom, left, right);
            AddTriangleCellData(md, bottomCellIndex, leftCellIndex, rightCellIndex);
        }

        features?.AddWall(bottom, bottomCell, left, leftCell, right, rightCell);
    }

    private static void TriangulateCornerTerraces(MeshData md,
        Vector3 begin, int beginCellIndex,
        Vector3 left, int leftCellIndex,
        Vector3 right, int rightCellIndex)
    {
        Vector3 v3 = HexMetrics.TerraceLerp(begin, left, 1);
        Vector3 v4 = HexMetrics.TerraceLerp(begin, right, 1);
        Color c3 = HexMetrics.TerraceLerp(Color1, Color2, 1);
        Color c4 = HexMetrics.TerraceLerp(Color1, Color3, 1);

        AddTriangle(md, begin, v3, v4);
        AddTriangleCellData(md, beginCellIndex, beginCellIndex, beginCellIndex);

        for (int i = 2; i < HexMetrics.TerraceSteps; i++)
        {
            Vector3 v1 = v3;
            Vector3 v2 = v4;
            Color c1 = c3;
            Color c2 = c4;
            v3 = HexMetrics.TerraceLerp(begin, left, i);
            v4 = HexMetrics.TerraceLerp(begin, right, i);
            c3 = HexMetrics.TerraceLerp(Color1, Color2, i);
            c4 = HexMetrics.TerraceLerp(Color1, Color3, i);
            AddQuad(md, v1, v2, v3, v4);
            AddQuadCellData(md, beginCellIndex, beginCellIndex, leftCellIndex, rightCellIndex);
        }

        AddQuad(md, v3, v4, left, right);
        AddQuadCellData(md, beginCellIndex, leftCellIndex, leftCellIndex, rightCellIndex);
    }

    private static void TriangulateCornerTerracesCliff(MeshData md,
        Vector3 begin, int beginCellIndex, HexCellData beginCell,
        Vector3 left, int leftCellIndex, HexCellData leftCell,
        Vector3 right, int rightCellIndex, HexCellData rightCell)
    {
        float b = 1f / (rightCell.Elevation - beginCell.Elevation);
        if (b < 0) b = -b;
        Vector3 boundary = Perturb(begin).Lerp(Perturb(right), b);
        Color boundaryColor = Color1.Lerp(Color3, b);

        TriangulateBoundaryTriangle(md, begin, beginCellIndex, left, leftCellIndex, boundary, rightCellIndex);

        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            TriangulateBoundaryTriangle(md, left, leftCellIndex, right, rightCellIndex, boundary, beginCellIndex);
        }
        else
        {
            AddTriangleUnperturbed(md, Perturb(left), Perturb(right), boundary);
            AddTriangleCellData(md, leftCellIndex, rightCellIndex, beginCellIndex);
        }
    }

    private static void TriangulateCornerCliffTerraces(MeshData md,
        Vector3 begin, int beginCellIndex, HexCellData beginCell,
        Vector3 left, int leftCellIndex, HexCellData leftCell,
        Vector3 right, int rightCellIndex, HexCellData rightCell)
    {
        float b = 1f / (leftCell.Elevation - beginCell.Elevation);
        if (b < 0) b = -b;
        Vector3 boundary = Perturb(begin).Lerp(Perturb(left), b);
        Color boundaryColor = Color1.Lerp(Color2, b);

        TriangulateBoundaryTriangle(md, right, rightCellIndex, begin, beginCellIndex, boundary, leftCellIndex);

        if (leftCell.GetEdgeType(rightCell) == HexEdgeType.Slope)
        {
            TriangulateBoundaryTriangle(md, left, leftCellIndex, right, rightCellIndex, boundary, beginCellIndex);
        }
        else
        {
            AddTriangleUnperturbed(md, Perturb(left), Perturb(right), boundary);
            AddTriangleCellData(md, leftCellIndex, rightCellIndex, beginCellIndex);
        }
    }

    private static void TriangulateBoundaryTriangle(MeshData md,
        Vector3 begin, int beginCellIndex,
        Vector3 left, int leftCellIndex,
        Vector3 boundary, int boundaryCellIndex)
    {
        Vector3 v2 = Perturb(HexMetrics.TerraceLerp(begin, left, 1));

        AddTriangleUnperturbed(md, Perturb(begin), v2, boundary);
        AddTriangleCellData(md, beginCellIndex, beginCellIndex, boundaryCellIndex);

        for (int i = 2; i < HexMetrics.TerraceSteps; i++)
        {
            Vector3 v1 = v2;
            v2 = Perturb(HexMetrics.TerraceLerp(begin, left, i));
            AddTriangleUnperturbed(md, v1, v2, boundary);
            AddTriangleCellData(md, beginCellIndex, beginCellIndex, boundaryCellIndex);
        }

        AddTriangleUnperturbed(md, v2, Perturb(left), boundary);
        AddTriangleCellData(md, beginCellIndex, leftCellIndex, boundaryCellIndex);
    }

    // ==================== Water ====================

    /* Part 8: 开放水面三角化
       Part 20 (forward+): 使用 CUSTOM0 + COLOR 实现 3-cell visibility 混合 */
    private static void TriangulateOpenWater(HexDirection direction, HexCellData cell, int cellIndex, Vector3 center, HexGrid grid,
        MeshData water, MeshData waterShore, MeshData estuaries)
    {
        center.Y = cell.WaterSurfaceY;

        bool hasNeighbor = grid.TryGetCellIndex(cell.coordinates.Step(direction), out int neighborIndex);
        HexCellData neighbor = hasNeighbor ? grid.CellData[neighborIndex] : default;

        /* Shore 方向：全部交给 TriangulateShoreWater */
        if (hasNeighbor && !neighbor.IsUnderwater)
        {
            TriangulateShoreWater(direction, cell, cellIndex, center, neighborIndex, grid, water, waterShore, estuaries);
            return;
        }

        Vector3 c1 = center + HexMetrics.GetFirstWaterCorner(direction);
        Vector3 c2 = center + HexMetrics.GetSecondWaterCorner(direction);

        Vector3 indices;
        indices.X = indices.Y = indices.Z = cellIndex;

        /* 开放水面中心三角形 */
        AddWaterTriangle(water, center, c1, c2, indices, Weights1, Weights1, Weights1);

        if (hasNeighbor && neighbor.IsUnderwater)
        {
            if (direction <= HexDirection.SE)
            {
                Vector3 waterBridge = HexMetrics.GetWaterBridge(direction);
                Vector3 e1 = c1 + waterBridge;
                Vector3 e2 = c2 + waterBridge;

                /* 连接桥：cell 与 neighbor 混合 */
                indices.Y = neighborIndex;
                AddWaterQuad(water, c1, c2, e1, e2, indices, Weights1, Weights1, Weights2, Weights2);

                if (direction <= HexDirection.E)
                {
                    if (grid.TryGetCellIndex(cell.coordinates.Step(direction.Next()), out int nextNeighborIndex))
                    {
                        HexCellData nextNeighbor = grid.CellData[nextNeighborIndex];
                        if (nextNeighbor.IsUnderwater)
                        {
                            /* 角落三角形：cell + neighbor + nextNeighbor 三向混合 */
                            indices.Z = nextNeighborIndex;
                            AddWaterTriangle(water, c2, e2, c2 + HexMetrics.GetWaterBridge(direction.Next()),
                                indices, Weights1, Weights2, Weights3);
                        }
                    }
                }
            }
        }
    }

    /* Part 8 / 27: 岸边水体 — "Between Water and Solid Edges" 版本。
       e2 使用邻居 solid corners（非 bridge），e1 使用 water corners。
       Part 20 (forward+): 使用 CUSTOM0 + COLOR 实现 3-cell visibility 混合。
       Part 27: 跨 seam 时对邻居位置做整体偏移。 */
    private static void TriangulateShoreWater(HexDirection direction, HexCellData cell, int cellIndex, Vector3 center, int neighborIndex, HexGrid grid,
        MeshData water, MeshData waterShore, MeshData estuaries)
    {
        HexCellData neighbor = grid.CellData[neighborIndex];
        Vector3 neighborPosition = grid.CellPositions[neighborIndex];
        center.Y = cell.WaterSurfaceY;

        EdgeVertices e1 = new EdgeVertices(
            center + HexMetrics.GetFirstWaterCorner(direction),
            center + HexMetrics.GetSecondWaterCorner(direction)
        );

        /* 中心扇形：画到 water mesh（单 cell） */
        AddWaterTriangle(water, center, e1.v1, e1.v2, cellIndex);
        AddWaterTriangle(water, center, e1.v2, e1.v3, cellIndex);
        AddWaterTriangle(water, center, e1.v3, e1.v4, cellIndex);
        AddWaterTriangle(water, center, e1.v4, e1.v5, cellIndex);

        /* e2 从邻居中心反向计算，使用 solid corners（非 bridge） */
        Vector3 center2 = GetNeighborPosition(cell, neighborPosition, neighbor.coordinates.ColumnIndex);
        center2.Y = center.Y;
        EdgeVertices e2 = new EdgeVertices(
            center2 + HexMetrics.GetSecondSolidCorner(direction.Opposite()),
            center2 + HexMetrics.GetFirstSolidCorner(direction.Opposite())
        );

        bool hasRiver = cell.HasRiverThroughEdge(direction);
        bool hasNextNeighbor = grid.TryGetCellIndex(cell.coordinates.Step(direction.Next()), out int nextNeighborIndex);

        Vector3 indices;
        indices.X = cellIndex;
        indices.Y = neighborIndex;
        indices.Z = 0f;

        if (hasRiver)
        {
            /* 河口：梯形由 TriangulateEstuary 处理 */
            TriangulateEstuary(estuaries, e1, e2, cell.IncomingRiver == direction, indices);

            /* 河口两侧的三角形仍然加到 waterShore（2-cell 混合） */
            AddShoreTriangle(waterShore, e1.v2, e2.v1, e1.v1,
                indices, Weights1, Weights2, Weights1, 0f, 1f, 0f);
            AddShoreTriangle(waterShore, e1.v5, e2.v5, e1.v4,
                indices, Weights1, Weights2, Weights1, 0f, 1f, 0f);
        }
        else
        {
            /* 无河口：标准 4 个四边形 edge strip（2-cell 混合） */
            AddShoreQuad(waterShore, e1.v1, e1.v2, e2.v1, e2.v2,
                indices, Weights1, Weights1, Weights2, Weights2, 0f, 1f);
            AddShoreQuad(waterShore, e1.v2, e1.v3, e2.v2, e2.v3,
                indices, Weights1, Weights1, Weights2, Weights2, 0f, 1f);
            AddShoreQuad(waterShore, e1.v3, e1.v4, e2.v3, e2.v4,
                indices, Weights1, Weights1, Weights2, Weights2, 0f, 1f);
            AddShoreQuad(waterShore, e1.v4, e1.v5, e2.v4, e2.v5,
                indices, Weights1, Weights1, Weights2, Weights2, 0f, 1f);
        }

        /* 角落三角形 — 教程中无论有无河口都要添加（3-cell 混合） */
        if (hasNextNeighbor)
        {
            HexCellData nextNeighbor = grid.CellData[nextNeighborIndex];
            float v3 = nextNeighbor.IsUnderwater ? 0f : 1f;
            Vector3 nextNeighborPosition = grid.CellPositions[nextNeighborIndex];
            Vector3 third = GetNeighborPosition(cell, nextNeighborPosition, nextNeighbor.coordinates.ColumnIndex) + (nextNeighbor.IsUnderwater ?
                HexMetrics.GetFirstWaterCorner(direction.Previous()) :
                HexMetrics.GetFirstSolidCorner(direction.Previous()));
            third.Y = center.Y;
            indices.Z = nextNeighborIndex;
            AddShoreTriangle(waterShore, e1.v5, e2.v5, third,
                indices, Weights1, Weights2, Weights3, 0f, 1f, v3);
        }
    }

    /* Part 27: 根据 column 索引差对跨 seam 的邻居位置做整体偏移 */
    private static Vector3 GetNeighborPosition(HexCellData cell, Vector3 neighborPosition, int neighborColumnIndex)
    {
        Vector3 position = neighborPosition;
        if (HexMetrics.Wrapping)
        {
            int diff = neighborColumnIndex - cell.coordinates.ColumnIndex;
            if (diff < -1)
            {
                position.X += HexMetrics.wrapSize * HexMetrics.InnerDiameter;
            }
            else if (diff > 1)
            {
                position.X -= HexMetrics.wrapSize * HexMetrics.InnerDiameter;
            }
        }
        return position;
    }

    /* Part 8: 河口三角剖分 — 河流入水口的梯形区域。
       使用独立的 estuaries，同时设置 UV（shore/blend）和 UV2（river flow）。
       Part 20 (forward+): 增加 CUSTOM0 + COLOR 传 cell visibility 数据。
       incomingRiver 控制 UV2 的方向：入水河流从下往上流，出水河流相反。 */
    private static void TriangulateEstuary(MeshData estuaries,
        EdgeVertices e1, EdgeVertices e2, bool incomingRiver, Vector3 indices)
    {

        /* 几何体（与方向无关）: 左四边形 + 中间三角形 + 右四边形 */
        AddQuad(estuaries, e2.v1, e1.v2, e2.v2, e1.v3);
        AddQuadUV(estuaries,
            new Vector2(0f, 1f), new Vector2(0f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 0f));
        /* 左四边形顶点顺序: e2.v1, e1.v3, e1.v2, e2.v2 → neighbor, cell, cell, neighbor */
        AddEstuaryCellData(estuaries, indices, Weights2, Weights1, Weights1, Weights2);

        AddTriangle(estuaries, e1.v3, e2.v2, e2.v4);
        AddTriangleUV(estuaries,
            new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(1f, 1f));
        /* 中间三角形顶点顺序: e1.v3, e2.v2, e2.v4 → cell, neighbor, neighbor */
        AddEstuaryCellData(estuaries, indices, Weights1, Weights2, Weights2);

        AddQuad(estuaries, e1.v3, e1.v4, e2.v4, e2.v5);
        AddQuadUV(estuaries,
            new Vector2(0f, 0f), new Vector2(0f, 0f),
            new Vector2(1f, 1f), new Vector2(0f, 1f));
        /* 右四边形顶点顺序: e1.v3, e2.v5, e1.v4, e2.v4 → cell, neighbor, cell, neighbor */
        AddEstuaryCellData(estuaries, indices, Weights1, Weights2, Weights1, Weights2);

        /* UV2: river flow */
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

    /* Part 20 (forward+): Estuary 专用 cell data 辅助方法 */
    private static void AddEstuaryCellData(MeshData md, Vector3 indices, Color w1, Color w2, Color w3)
    {
        if (md.UseCustom0)
        {
            Color c0 = new Color(indices.X, indices.Y, indices.Z, 0f);
            md.Custom0s.Add(c0); md.Custom0s.Add(c0); md.Custom0s.Add(c0);
        }
        if (md.UseColors)
        {
            md.Colors.Add(w1); md.Colors.Add(w2); md.Colors.Add(w3);
        }
    }

    private static void AddEstuaryCellData(MeshData md, Vector3 indices, Color w1, Color w2, Color w3, Color w4)
    {
        if (md.UseCustom0)
        {
            Color c0 = new Color(indices.X, indices.Y, indices.Z, 0f);
            md.Custom0s.Add(c0); md.Custom0s.Add(c0); md.Custom0s.Add(c0); md.Custom0s.Add(c0);
        }
        if (md.UseColors)
        {
            md.Colors.Add(w1); md.Colors.Add(w2); md.Colors.Add(w3); md.Colors.Add(w4);
        }
    }
}
