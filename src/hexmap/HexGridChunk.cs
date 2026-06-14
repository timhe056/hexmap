using Godot;
using System.Collections.Generic;

namespace HexMap;

/// <summary>
/// Part 5：六边形网格 Chunk，每个 Chunk 管理自己的一组 Cell 和独立 Mesh。
/// 修改 Cell 后调用 Refresh()，会在下一帧自动重新三角化。
/// </summary>
public partial class HexGridChunk : Node3D
{
    /* Part 27: 本 chunk 在东西向的列索引，用于接缝偏移 */
    public int ColumnIndex { get; set; }

    public HexGrid Grid { get; set; }

    private int[] _cellIndices;
    private MeshInstance3D _meshInstance;
    private MeshInstance3D _riverMeshInstance;
    /* Part 7: 道路网格实例 */
    private MeshInstance3D _roadMeshInstance;
    /* Part 8: 水面网格实例 */
    private MeshInstance3D _waterMeshInstance;
    private MeshInstance3D _waterShoreMeshInstance;
    private MeshInstance3D _estuaryMeshInstance;
    /* Part 10: 城墙网格实例 */
    private MeshInstance3D _wallsMeshInstance;
    private MeshInstance3D _wallsWireframeInstance;
    private bool _showWireframe = false;

    /* Part 27: 地形碰撞体，用于鼠标射线精确拾取 */
    private StaticBody3D _terrainBody;
    private CollisionShape3D _terrainShape;

    /* Part 27: 手动 mesh 射线拾取用的缓存 */
    private Aabb _terrainAabb;
    public Mesh TerrainMesh => _meshInstance?.Mesh;
    public Aabb TerrainAabb => _terrainAabb;
    private bool _needsRefresh = false;
    private Label3D[] _labels;
    /* Part 16: 路径高亮 mesh */
    private MeshInstance3D[] _highlights;
    /* Part 25: 静态共享 highlight 资源，避免每次 AddCell 创建新 Shader 导致 RID 泄漏 */
    private static Shader _highlightShader;
    private static ShaderMaterial _highlightBaseMaterial;
    private static PlaneMesh _highlightPlaneMesh;
    private static Texture2D _highlightOutlineTexture;
    /* Part 9: 地形特征管理器 */
    private HexFeatureManager _featureManager;

    public override void _Ready()
    {
        EnsureMeshInstance();
        _cellIndices = new int[HexMetrics.ChunkSizeX * HexMetrics.ChunkSizeZ];
        for (int i = 0; i < _cellIndices.Length; i++) _cellIndices[i] = -1;
        _labels = new Label3D[_cellIndices.Length];
        _highlights = new MeshInstance3D[_cellIndices.Length];

        /* Part 9: 创建特征管理器子节点（如果 SetFeatureCollections 已提前创建则跳过） */
        if (_featureManager == null)
        {
            _featureManager = new HexFeatureManager();
            _featureManager.Name = "Features";
            AddChild(_featureManager);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _featureManager.Owner = GetTree().EditedSceneRoot;
            }
        }
        _featureManager.WallsMeshInstance = _wallsMeshInstance;
    }

    /* Part 9: 接收 HexGrid 统一加载的 prefab 集合 */
    public void SetFeatureCollections(HexFeatureManager.HexFeatureCollection[] urban, HexFeatureManager.HexFeatureCollection[] farm, HexFeatureManager.HexFeatureCollection[] plant,
        PackedScene wallTower, PackedScene bridge, PackedScene[] special)
    {
        if (_featureManager == null)
        {
            _featureManager = new HexFeatureManager();
            _featureManager.Name = "Features";
            AddChild(_featureManager);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _featureManager.Owner = GetTree().EditedSceneRoot;
            }
        }
        _featureManager.WallsMeshInstance = _wallsMeshInstance;
        _featureManager.SetCollections(urban, farm, plant);
        _featureManager.SetWallTower(wallTower);
        _featureManager.SetBridge(bridge);
        _featureManager.SetSpecialPrefabs(special);
    }

    public void AddCell(int localIndex, int cellIndex, out Label3D label, out MeshInstance3D highlight)
    {
        _cellIndices[localIndex] = cellIndex;
        Vector3 cellPosition = Grid.CellPositions[cellIndex];

        // 创建 Label3D 显示坐标 / 距离数字
        label = new Label3D();
        label.Text = "";
        label.FontSize = 72;
        label.PixelSize = 0.06f;
        label.Modulate = Colors.White;
        label.OutlineSize = 10;
        label.OutlineModulate = Colors.Black;
        label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        label.Position = cellPosition + new Vector3(0f, 4.0f, 0f);
        label.RenderPriority = 1;
        label.Name = $"Label_{localIndex}";
        label.Visible = false;
        AddChild(label);
        _labels[localIndex] = label;

        // Part 18: 创建路径高亮 PlaneMesh（使用 Unity 六边形边框纹理）
        highlight = new MeshInstance3D();
        if (_highlightPlaneMesh == null)
        {
            _highlightPlaneMesh = new PlaneMesh();
            _highlightPlaneMesh.Size = new Vector2(HexMetrics.OuterRadius * 2f, HexMetrics.OuterRadius * 2f);
        }
        highlight.Mesh = _highlightPlaneMesh;
        highlight.Position = cellPosition + new Vector3(0f, 0.05f, 0f);
        highlight.Name = $"Highlight_{localIndex}";
        highlight.Visible = false;

        if (_highlightShader == null)
        {
            _highlightShader = new Shader();
            _highlightShader.Code = "shader_type spatial;\nrender_mode unshaded, depth_test_disabled;\nuniform sampler2D outline_texture : source_color;\nuniform vec4 color : source_color;\nvoid fragment() { vec4 tex = texture(outline_texture, UV); ALBEDO = tex.rgb * color.rgb; ALPHA = tex.a * color.a; }";
        }
        if (_highlightOutlineTexture == null)
        {
            _highlightOutlineTexture = ResourceLoader.Load<Texture2D>("res://assets/textures/Cell Outline.png");
        }
        if (_highlightBaseMaterial == null)
        {
            _highlightBaseMaterial = new ShaderMaterial();
            _highlightBaseMaterial.Shader = _highlightShader;
            _highlightBaseMaterial.SetShaderParameter("outline_texture", _highlightOutlineTexture);
        }

        var mat = _highlightBaseMaterial.Duplicate() as ShaderMaterial;
        highlight.MaterialOverride = mat;

        AddChild(highlight);
        _highlights[localIndex] = highlight;
    }

    /// <summary>控制本 Chunk 内所有 Label 的显示/隐藏</summary>
    public void ShowLabels(bool visible)
    {
        if (_labels == null) return;
        foreach (var label in _labels)
        {
            if (label != null) label.Visible = visible;
        }
    }

    /// <summary>标记需要刷新。运行时延迟到 _Process，编辑器中立即三角化。</summary>
    public void Refresh()
    {
        if (Engine.IsEditorHint())
        {
            Triangulate();
        }
        else
        {
            _needsRefresh = true;
        }
    }

    public override void _Process(double delta)
    {
        if (_needsRefresh)
        {
            Triangulate();
            _needsRefresh = false;
        }
    }

    public void ToggleWireframe()
    {
        _showWireframe = !_showWireframe;
        if (_wallsWireframeInstance != null)
            _wallsWireframeInstance.Visible = _showWireframe;
        ToggleFeatureWireframes(_featureManager, _showWireframe);
    }

    private static void ToggleFeatureWireframes(Node parent, bool visible)
    {
        if (parent == null) return;
        foreach (Node child in parent.GetChildren())
        {
            // 跳过已生成的线框节点，避免无限递归
            if (child.Name.ToString().EndsWith("_Wireframe"))
                continue;

            if (child is MeshInstance3D mi && mi.Mesh != null)
            {
                var wireframeName = mi.Name + "_Wireframe";
                var wireframe = mi.GetNodeOrNull<MeshInstance3D>(wireframeName);
                if (wireframe == null)
                {
                    wireframe = CreateWireframeMeshInstance(mi.Mesh);
                    if (wireframe != null)
                    {
                        wireframe.Name = wireframeName;
                        mi.AddChild(wireframe);
                    }
                }
                if (wireframe != null)
                    wireframe.Visible = visible;
            }
            ToggleFeatureWireframes(child, visible);
        }
    }

    private static MeshInstance3D CreateWireframeMeshInstance(Mesh sourceMesh)
    {
        if (sourceMesh == null) return null;
        var wireframe = new ArrayMesh();
        for (int surf = 0; surf < sourceMesh.GetSurfaceCount(); surf++)
        {
            var arrays = sourceMesh.SurfaceGetArrays(surf);
            if (arrays.Count == 0) continue;
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            if (verts.Length == 0) continue;

            var lineVerts = new List<Vector3>();
            var lineInds = new List<int>();
            var edgeSet = new HashSet<(int, int)>();

            var indexVariant = arrays[(int)Mesh.ArrayType.Index];
            var indices = indexVariant.VariantType == Variant.Type.Nil ? new int[0] : indexVariant.AsInt32Array();
            if (indices.Length >= 3)
            {
                for (int i = 0; i + 2 < indices.Length; i += 3)
                {
                    int i0 = indices[i];
                    int i1 = indices[i + 1];
                    int i2 = indices[i + 2];
                    AddWireframeEdge(lineVerts, lineInds, edgeSet, verts, i0, i1);
                    AddWireframeEdge(lineVerts, lineInds, edgeSet, verts, i1, i2);
                    AddWireframeEdge(lineVerts, lineInds, edgeSet, verts, i2, i0);
                }
            }
            else
            {
                // 无索引数组：顶点按顺序每3个一组构成三角形
                for (int i = 0; i < verts.Length; i += 3)
                {
                    int i0 = i;
                    int i1 = i + 1;
                    int i2 = i + 2;
                    AddWireframeEdge(lineVerts, lineInds, edgeSet, verts, i0, i1);
                    AddWireframeEdge(lineVerts, lineInds, edgeSet, verts, i1, i2);
                    AddWireframeEdge(lineVerts, lineInds, edgeSet, verts, i2, i0);
                }
            }

            if (lineVerts.Count > 0)
            {
                var lineArrays = new Godot.Collections.Array();
                lineArrays.Resize((int)Mesh.ArrayType.Max);
                lineArrays[(int)Mesh.ArrayType.Vertex] = lineVerts.ToArray();
                lineArrays[(int)Mesh.ArrayType.Index] = lineInds.ToArray();
                wireframe.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, lineArrays);
            }
        }

        if (wireframe.GetSurfaceCount() == 0) return null;

        var instance = new MeshInstance3D();
        instance.Mesh = wireframe;
        var mat = new StandardMaterial3D();
        mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        mat.AlbedoColor = Colors.White;
        instance.MaterialOverride = mat;
        return instance;
    }

    private void Triangulate()
    {
        if (_meshInstance == null) return;

        /* Part 9: 清除旧特征 */
        _featureManager?.Clear();

        /* Part 8: 七输出 BuildMeshes（传入 featureManager 用于城墙构建） */
        HexMeshBuilder.BuildMeshes(_cellIndices, Grid, _featureManager,
            out Mesh terrainMesh, out Mesh riverMesh, out Mesh roadMesh,
            out Mesh waterMesh, out Mesh waterShoreMesh, out Mesh estuaryMesh);
        _meshInstance.Mesh = terrainMesh;

        /* Part 27: 同步更新地形碰撞体，使鼠标射线与可见表面对齐。
           注意：Godot ConcavePolygonShape3D 默认背面不碰撞，地形 mesh 法线可能朝下，
           因此开启 BackfaceCollision，确保从上方射线也能命中。 */
        if (terrainMesh != null)
        {
            var shape = terrainMesh.CreateTrimeshShape();
            if (shape == null)
            {
                GD.PushWarning($"[HexGridChunk {Name}] CreateTrimeshShape returned null!");
            }
            else
            {
                shape.BackfaceCollision = true;
            }
            _terrainShape.Shape = shape;
            _terrainAabb = terrainMesh.GetAabb();
        }

        _riverMeshInstance.Mesh = riverMesh;
        _roadMeshInstance.Mesh = roadMesh;
        _waterMeshInstance.Mesh = waterMesh;
        _waterShoreMeshInstance.Mesh = waterShoreMesh;
        _estuaryMeshInstance.Mesh = estuaryMesh;

        if (_meshInstance.MaterialOverride == null)
            _meshInstance.MaterialOverride = LoadTerrainMaterial();

        if (_riverMeshInstance.MaterialOverride == null) _riverMeshInstance.MaterialOverride = LoadRiverMaterial();
        if (_roadMeshInstance.MaterialOverride == null) _roadMeshInstance.MaterialOverride = LoadRoadMaterial();
        if (_waterMeshInstance.MaterialOverride == null) _waterMeshInstance.MaterialOverride = LoadWaterMaterial();
        if (_waterShoreMeshInstance.MaterialOverride == null) _waterShoreMeshInstance.MaterialOverride = LoadWaterShoreMaterial();
        if (_estuaryMeshInstance.MaterialOverride == null) _estuaryMeshInstance.MaterialOverride = LoadEstuaryMaterial();

        /* 确保 river/estuary 在 water/waterShore 之后渲染（匹配 Unity Queue=Transparent+1） */
        _riverMeshInstance.MaterialOverride.RenderPriority = 1;
        _estuaryMeshInstance.MaterialOverride.RenderPriority = 1;

        /* Part 9: 为每个单元格放置地形特征 */
        for (int i = 0; i < _cellIndices.Length; i++)
        {
            int index = _cellIndices[i];
            if (index >= 0)
            {
                TriangulateCellFeatures(index);
            }
        }
        _featureManager?.Apply();
        GenerateWireframeMesh();

    }

    /* Part 9: 为单个单元格放置特征 */
    private void TriangulateCellFeatures(int cellIndex)
    {
        HexCellData cell = Grid.CellData[cellIndex];
        Vector3 position = Grid.CellPositions[cellIndex];
        if (cell.IsUnderwater) return;

        /* 中心特征：仅当无河流、无道路时放置 */
        if (!cell.HasRiver && !cell.HasRoads)
        {
            _featureManager.AddFeature(cell, position);
        }

        /* 各方向边缘特征 */
        for (HexDirection d = HexDirection.NE; d <= HexDirection.NW; d++)
        {
            if (!cell.HasRiverThroughEdge(d) && !cell.HasRoadThroughEdge(d))
            {
                Vector3 center = position;
                HexMeshBuilder.EdgeVertices e = new HexMeshBuilder.EdgeVertices(
                    center + HexMetrics.GetFirstSolidCorner(d),
                    center + HexMetrics.GetSecondSolidCorner(d)
                );
                Vector3 edgePos = (center + e.v1 + e.v5) * (1f / 3f);
                _featureManager.AddFeature(cell, edgePos);
            }
        }

        /* Part 11: 特殊特征 */
        if (cell.IsSpecial)
        {
            _featureManager.AddSpecialFeature(cell, position);
        }
    }

    private void EnsureMeshInstance()
    {
        _meshInstance = GetNodeOrNull<MeshInstance3D>("HexMesh");
        if (_meshInstance == null)
        {
            _meshInstance = new MeshInstance3D();
            _meshInstance.Name = "HexMesh";
            AddChild(_meshInstance);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _meshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }



        /* Part 8: 创建水面网格实例 — 先于 Rivers，使河流渲染在水面之上 */
        _waterMeshInstance = GetNodeOrNull<MeshInstance3D>("Water");
        if (_waterMeshInstance == null)
        {
            _waterMeshInstance = new MeshInstance3D();
            _waterMeshInstance.Name = "Water";
            if (_waterMeshInstance.MaterialOverride == null) _waterMeshInstance.MaterialOverride = LoadWaterMaterial();
            AddChild(_waterMeshInstance);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _waterMeshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }

        /* Part 8: 创建岸边水体网格实例 */
        _waterShoreMeshInstance = GetNodeOrNull<MeshInstance3D>("WaterShore");
        if (_waterShoreMeshInstance == null)
        {
            _waterShoreMeshInstance = new MeshInstance3D();
            _waterShoreMeshInstance.Name = "WaterShore";
            if (_waterShoreMeshInstance.MaterialOverride == null) _waterShoreMeshInstance.MaterialOverride = LoadWaterShoreMaterial();
            AddChild(_waterShoreMeshInstance);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _waterShoreMeshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }

        /* Part 8: 创建河口网格实例 */
        _estuaryMeshInstance = GetNodeOrNull<MeshInstance3D>("Estuaries");
        if (_estuaryMeshInstance == null)
        {
            _estuaryMeshInstance = new MeshInstance3D();
            _estuaryMeshInstance.Name = "Estuaries";
            if (_estuaryMeshInstance.MaterialOverride == null) _estuaryMeshInstance.MaterialOverride = LoadEstuaryMaterial();
            AddChild(_estuaryMeshInstance);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _estuaryMeshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }

        _riverMeshInstance = GetNodeOrNull<MeshInstance3D>("Rivers");
        if (_riverMeshInstance == null)
        {
            _riverMeshInstance = new MeshInstance3D();
            _riverMeshInstance.Name = "Rivers";
            if (_riverMeshInstance.MaterialOverride == null) _riverMeshInstance.MaterialOverride = LoadRiverMaterial();
            AddChild(_riverMeshInstance);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _riverMeshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }

        /* Part 10: 创建城墙网格实例 */
        _wallsMeshInstance = GetNodeOrNull<MeshInstance3D>("Walls");
        if (_wallsMeshInstance == null)
        {
            _wallsMeshInstance = new MeshInstance3D();
            _wallsMeshInstance.Name = "Walls";
            if (_wallsMeshInstance.MaterialOverride == null) _wallsMeshInstance.MaterialOverride = LoadWallsMaterial();
            AddChild(_wallsMeshInstance);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _wallsMeshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }

        /* Part 7: 创建道路网格实例 */
        _roadMeshInstance = GetNodeOrNull<MeshInstance3D>("Roads");
        if (_roadMeshInstance == null)
        {
            _roadMeshInstance = new MeshInstance3D();
            _roadMeshInstance.Name = "Roads";
            if (_roadMeshInstance.MaterialOverride == null) _roadMeshInstance.MaterialOverride = LoadRoadMaterial();
            AddChild(_roadMeshInstance);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _roadMeshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }

        /* Part 27: 创建地形碰撞体，用于鼠标精确拾取 */
        _terrainBody = GetNodeOrNull<StaticBody3D>("TerrainBody");
        if (_terrainBody == null)
        {
            _terrainBody = new StaticBody3D();
            _terrainBody.Name = "TerrainBody";
            _terrainBody.CollisionLayer = 2;
            _terrainBody.CollisionMask = 2;
            AddChild(_terrainBody);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _terrainBody.Owner = GetTree().EditedSceneRoot;
            }
        }

        _terrainShape = GetNodeOrNull<CollisionShape3D>("TerrainShape");
        if (_terrainShape == null)
        {
            _terrainShape = new CollisionShape3D();
            _terrainShape.Name = "TerrainShape";
            _terrainBody.AddChild(_terrainShape);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _terrainShape.Owner = GetTree().EditedSceneRoot;
            }
        }
    }

    private static ShaderMaterial LoadRiverMaterial()
    {
        return ResourceLoader.Load<ShaderMaterial>("res://assets/materials/river.tres");
    }

    /* Part 7: 创建道路材质（基于 UV.x 透明度混合 + 噪声扰动产生粗糙边缘） */
    private static ShaderMaterial LoadRoadMaterial()
    {
        return ResourceLoader.Load<ShaderMaterial>("res://assets/materials/road.tres");
    }

    private static Material LoadTerrainMaterial()
    {
        return ResourceLoader.Load<ShaderMaterial>("res://assets/materials/terrain.tres");
    }

    /* Part 8: 水面材质（蓝色半透明） */
    private static ShaderMaterial LoadWaterMaterial()
    {
        return ResourceLoader.Load<ShaderMaterial>("res://assets/materials/water.tres");
    }

    /* Part 8: 岸边水体材质（泡沫效果） */
    private static ShaderMaterial LoadWaterShoreMaterial()
    {
        return ResourceLoader.Load<ShaderMaterial>("res://assets/materials/water_shore.tres");
    }

    /* Part 8: 河口材质（岸边+河流混合） */
    private static ShaderMaterial LoadEstuaryMaterial()
    {
        return ResourceLoader.Load<ShaderMaterial>("res://assets/materials/estuary.tres");
    }

    /* Part 10: 城墙材质（砖灰色，双面渲染避免 Godot 右手系法线方向问题） */
    private static StandardMaterial3D LoadWallsMaterial()
    {
        var mat = new StandardMaterial3D();
        mat.AlbedoColor = new Color(0.5f, 0.5f, 0.5f);
        mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        return mat;
    }

    /* Part 11 Debug: 从城墙 mesh 生成线框 mesh */
    private void GenerateWireframeMesh()
    {
        if (_wallsMeshInstance?.Mesh == null) return;

        var wireframe = new ArrayMesh();
        for (int surf = 0; surf < _wallsMeshInstance.Mesh.GetSurfaceCount(); surf++)
        {
            var arrays = _wallsMeshInstance.Mesh.SurfaceGetArrays(surf);
            var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            var indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
            if (verts.Length == 0 || indices.Length == 0) continue;

            var lineVerts = new List<Vector3>();
            var lineInds = new List<int>();
            var edgeSet = new HashSet<(int, int)>();

            for (int i = 0; i < indices.Length; i += 3)
            {
                int i0 = indices[i];
                int i1 = indices[i + 1];
                int i2 = indices[i + 2];
                AddWireframeEdge(lineVerts, lineInds, edgeSet, verts, i0, i1);
                AddWireframeEdge(lineVerts, lineInds, edgeSet, verts, i1, i2);
                AddWireframeEdge(lineVerts, lineInds, edgeSet, verts, i2, i0);
            }

            if (lineVerts.Count > 0)
            {
                var lineArrays = new Godot.Collections.Array();
                lineArrays.Resize((int)Mesh.ArrayType.Max);
                lineArrays[(int)Mesh.ArrayType.Vertex] = lineVerts.ToArray();
                lineArrays[(int)Mesh.ArrayType.Index] = lineInds.ToArray();
                wireframe.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, lineArrays);
            }
        }

        if (_wallsWireframeInstance == null)
        {
            _wallsWireframeInstance = new MeshInstance3D();
            _wallsWireframeInstance.Name = "WallsWireframe";
            var mat = new StandardMaterial3D();
            mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
            mat.AlbedoColor = Colors.White;
            _wallsWireframeInstance.MaterialOverride = mat;
            AddChild(_wallsWireframeInstance);
        }
        _wallsWireframeInstance.Mesh = wireframe;
        _wallsWireframeInstance.Visible = _showWireframe;
    }

    private static void AddWireframeEdge(List<Vector3> verts, List<int> inds, HashSet<(int, int)> edgeSet, Vector3[] srcVerts, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        if (edgeSet.Contains(key)) return;
        edgeSet.Add(key);
        int vi = verts.Count;
        verts.Add(srcVerts[a]);
        verts.Add(srcVerts[b]);
        inds.Add(vi);
        inds.Add(vi + 1);
    }
}
