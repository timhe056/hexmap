using Godot;

namespace HexMap;

/// <summary>
/// Part 5：六边形网格 Chunk，每个 Chunk 管理自己的一组 Cell 和独立 Mesh。
/// 修改 Cell 后调用 Refresh()，会在下一帧自动重新三角化。
/// </summary>
public partial class HexGridChunk : Node3D
{
    private HexCell[] _cells;
    private MeshInstance3D _meshInstance;
    private MeshInstance3D _riverMeshInstance;
    private bool _needsRefresh = false;
    private Label3D[] _labels;

    public override void _Ready()
    {
        EnsureMeshInstance();
        _cells = new HexCell[HexMetrics.ChunkSizeX * HexMetrics.ChunkSizeZ];
        _labels = new Label3D[_cells.Length];
    }

    public void AddCell(int index, HexCell cell)
    {
        _cells[index] = cell;
        cell.Chunk = this;

        // 创建 Label3D 显示坐标
        var label = new Label3D();
        label.Text = cell.Coordinates.ToStringOnSeparateLines();
        label.FontSize = 32;
        label.PixelSize = 0.04f;
        label.Modulate = Colors.White;
        label.OutlineSize = 8;
        label.OutlineModulate = Colors.Black;
        label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        label.Position = cell.BasePosition + new Vector3(0f, HexMetrics.ElevationStep * 0.5f, 0f);
        label.Name = $"Label_{index}";
        label.Visible = false;
        AddChild(label);
        _labels[index] = label;
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

    /// <summary>标记需要刷新。编辑器中立即三角化，运行时默认延迟到 _Process</summary>
    public void Refresh(bool immediate = false)
    {
        if (immediate || Engine.IsEditorHint())
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

    private void Triangulate()
    {
        if (_meshInstance == null) return;

        HexMeshBuilder.BuildMeshes(_cells, out Mesh terrainMesh, out Mesh riverMesh);
        _meshInstance.Mesh = terrainMesh;
        _riverMeshInstance.Mesh = riverMesh;

        // DEBUG: hide terrain to see river mesh in isolation
        _meshInstance.Visible = false;

        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        _meshInstance.MaterialOverride = mat;

        // DEBUG: make river opaque red
        var riverMat = new StandardMaterial3D
        {
            AlbedoColor = Colors.Red,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
        };
        _riverMeshInstance.MaterialOverride = riverMat;

        // Debug: 打印河流 mesh 顶点数
        int riverVertexCount = 0;
        if (riverMesh != null && riverMesh.GetSurfaceCount() > 0)
        {
            var arrays = riverMesh.SurfaceGetArrays(0);
            if (arrays.Count > 0 && arrays[0].AsGodotArray().Count > 0)
            {
                riverVertexCount = arrays[0].AsGodotArray().Count;
            }
        }
        int riverCellCount = 0;
        foreach (var c in _cells) if (c != null && c.HasRiver) riverCellCount++;
        if (riverCellCount > 0)
        {
            GD.Print($"[HexGridChunk] Triangulate: {Name}, riverCells={riverCellCount}, riverVertices={riverVertexCount}");
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

        _riverMeshInstance = GetNodeOrNull<MeshInstance3D>("Rivers");
        if (_riverMeshInstance == null)
        {
            _riverMeshInstance = new MeshInstance3D();
            _riverMeshInstance.Name = "Rivers";
            _riverMeshInstance.MaterialOverride = CreateRiverMaterial();
            AddChild(_riverMeshInstance);
            if (Engine.IsEditorHint() && GetTree()?.EditedSceneRoot != null)
            {
                _riverMeshInstance.Owner = GetTree().EditedSceneRoot;
            }
        }
    }

    private static ShaderMaterial CreateRiverMaterial()
    {
        var shader = new Shader();
        shader.Code = @"
shader_type spatial;
render_mode blend_mix, cull_disabled, unshaded;

uniform vec4 color : source_color = vec4(0.15, 0.4, 0.8, 0.7);
uniform float speed : hint_range(0.0, 2.0) = 0.25;
uniform sampler2D noise_texture : repeat_enable;

void fragment() {
    vec2 uv = UV;
    uv.x *= 0.0625;
    uv.y -= TIME * speed;
    float n = texture(noise_texture, uv).r;
    vec3 c = clamp(color.rgb + n * 0.3, 0.0, 1.0);
    ALBEDO = c;
    ALPHA = clamp(color.a + n * 0.2, 0.0, 1.0);
}
";
        var mat = new ShaderMaterial();
        mat.Shader = shader;
        mat.SetShaderParameter("color", new Color(0.15f, 0.4f, 0.8f, 0.7f));
        mat.SetShaderParameter("speed", 0.25f);
        // 创建噪声纹理
        var noise = new FastNoiseLite();
        noise.NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin;
        noise.Seed = 12345;
        noise.Frequency = 1.0f;
        var noiseTex = new NoiseTexture2D();
        noiseTex.Noise = noise;
        noiseTex.Width = 256;
        noiseTex.Height = 256;
        noiseTex.Normalize = true;
        noiseTex.Seamless = true;
        mat.SetShaderParameter("noise_texture", noiseTex);
        return mat;
    }
}
