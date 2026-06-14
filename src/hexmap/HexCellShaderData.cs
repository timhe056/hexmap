using Godot;
using System.Collections.Generic;

namespace HexMap;

/// <summary>
/// Part 21-22 / 2.2: 全局 Cell Data Texture，每个 cell 对应一个像素。
/// R = visibility (0-255), G = explored (0-255), B = water surface Y (0-30), A = terrainTypeIndex (0-255)。
/// 支持平滑过渡动画（0↔255 的渐变）。
/// </summary>
public class HexCellShaderData
{
    private ImageTexture _cellTexture;
    private byte[] _textureData;
    private bool[] _visibilityTransitions;
    private int _cellCountX;
    private int _cellCountZ;
    private bool _needsUpdate = false;

    private readonly List<int> _transitioningCellIndices = new();
    private bool _needsVisibilityReset;
    private const float TransitionSpeed = 255f;
    private const float WaterSurfaceScale = 255f / 30f;

    public bool ImmediateMode { get; set; }
    public HexGrid Grid { get; set; }

    public void Initialize(int x, int z)
    {
        _cellCountX = x;
        _cellCountZ = z;

        if (_textureData == null || _textureData.Length != x * z * 4)
        {
            _textureData = new byte[x * z * 4];
            _visibilityTransitions = new bool[x * z];
        }
        else
        {
            for (int i = 0; i < _textureData.Length; i++)
            {
                _textureData[i] = 0;
            }
            for (int i = 0; i < _visibilityTransitions.Length; i++)
            {
                _visibilityTransitions[i] = false;
            }
        }

        var image = Image.CreateFromData(x, z, false, Image.Format.Rgba8, _textureData);
        _cellTexture = ImageTexture.CreateFromImage(image);
        _transitioningCellIndices.Clear();
        _needsVisibilityReset = false;
    }

    public ImageTexture Texture => _cellTexture;
    public bool NeedsUpdate => _needsUpdate;

    /// <summary>Part 2.2: 刷新地形数据，B 通道写入水面高度（支持 0-30）。</summary>
    public void RefreshTerrain(int cellIndex)
    {
        int i = cellIndex * 4;
        HexCellData cell = Grid.CellData[cellIndex];
        _textureData[i + 2] = cell.IsUnderwater
            ? (byte)Mathf.Clamp(cell.WaterSurfaceY * WaterSurfaceScale, 0f, 255f)
            : (byte)0;
        _textureData[i + 3] = (byte)cell.TerrainTypeIndex;
        _needsUpdate = true;
    }

    /// <summary>Part 2.2: 刷新可见性，用独立 bool 数组记录过渡状态。</summary>
    public void RefreshVisibility(int cellIndex)
    {
        int i = cellIndex * 4;
        if (ImmediateMode)
        {
            _textureData[i + 0] = (byte)(Grid.IsCellVisible(cellIndex) ? 255 : 0);
            _textureData[i + 1] = (byte)(Grid.CellData[cellIndex].IsExplored ? 255 : 0);
            _needsUpdate = true;
        }
        else if (!_visibilityTransitions[cellIndex])
        {
            _visibilityTransitions[cellIndex] = true;
            _transitioningCellIndices.Add(cellIndex);
        }
    }

    /// <summary>Part 2.2: 视野高度变化时更新水面高度并触发可见性重置。</summary>
    public void ViewElevationChanged(int cellIndex)
    {
        int i = cellIndex * 4;
        HexCellData cell = Grid.CellData[cellIndex];
        _textureData[i + 2] = cell.IsUnderwater
            ? (byte)Mathf.Clamp(cell.WaterSurfaceY * WaterSurfaceScale, 0f, 255f)
            : (byte)0;
        _needsVisibilityReset = true;
        _needsUpdate = true;
    }

    public void UpdateTexture(double deltaTime)
    {
        if (_needsVisibilityReset)
        {
            _needsVisibilityReset = false;
            Grid?.ResetVisibility();
        }

        int delta = (int)(deltaTime * TransitionSpeed);
        if (delta < 1) delta = 1;

        for (int i = 0; i < _transitioningCellIndices.Count; i++)
        {
            if (!UpdateCellData(_transitioningCellIndices[i], delta))
            {
                _transitioningCellIndices[i--] = _transitioningCellIndices[_transitioningCellIndices.Count - 1];
                _transitioningCellIndices.RemoveAt(_transitioningCellIndices.Count - 1);
            }
        }

        if (_transitioningCellIndices.Count > 0 || _needsUpdate)
        {
            var image = Image.CreateFromData(_cellCountX, _cellCountZ, false, Image.Format.Rgba8, _textureData);
            _cellTexture.Update(image);
            _needsUpdate = false;
        }
    }

    private bool UpdateCellData(int index, int delta)
    {
        int i = index * 4;
        HexCellData cell = Grid.CellData[index];
        bool stillUpdating = false;

        // G channel: explored (fade in only)
        if (cell.IsExplored && _textureData[i + 1] < 255)
        {
            stillUpdating = true;
            int g = _textureData[i + 1] + delta;
            _textureData[i + 1] = (byte)(g >= 255 ? 255 : g);
        }

        // R channel: visibility (fade in/out)
        if (Grid.IsCellVisible(index))
        {
            if (_textureData[i + 0] < 255)
            {
                stillUpdating = true;
                int r = _textureData[i + 0] + delta;
                _textureData[i + 0] = (byte)(r >= 255 ? 255 : r);
            }
        }
        else if (_textureData[i + 0] > 0)
        {
            stillUpdating = true;
            int r = _textureData[i + 0] - delta;
            _textureData[i + 0] = (byte)(r < 0 ? 0 : r);
        }

        if (!stillUpdating)
        {
            _visibilityTransitions[index] = false;
        }

        return stillUpdating;
    }
}
