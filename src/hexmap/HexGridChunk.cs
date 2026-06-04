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
        label.Modulate = Colors.Black;
        label.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
        label.Position = cell.BasePosition + new Vector3(0f, 2f, 0f);
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

    /// <summary>标记需要刷新。编辑器中立即三角化，运行时延迟到 _Process</summary>
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

    private void Triangulate()
    {
        if (_meshInstance == null) return;

        var mesh = HexMeshBuilder.BuildMesh(_cells);
        _meshInstance.Mesh = mesh;

        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        };
        _meshInstance.MaterialOverride = mat;
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
    }
}
