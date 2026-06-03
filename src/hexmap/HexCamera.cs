using Godot;

namespace HexMap;

/// <summary>
/// 六边形地图相机控制器。
/// 支持鼠标左键拖拽平移、滚轮缩放。
/// 与 HexGrid 解耦，通过 InitialFocus 硬编码初始注视点。
/// </summary>
public partial class HexCamera : Camera3D
{
    // ==================== Inspector 参数 ====================

    [Export(PropertyHint.Range, "10,300,1")]
    public float MinSize { get; set; } = 10f;

    [Export(PropertyHint.Range, "10,500,1")]
    public float MaxSize { get; set; } = 300f;

    [Export(PropertyHint.Range, "1,30,0.5")]
    public float ZoomStep { get; set; } = 8f;

    /// <summary>相机初始注视的地面点（与 HexGrid 解耦的硬编码值）</summary>
    [Export]
    public Vector3 InitialFocus { get; set; } = new Vector3(47.6314f, 0f, 37.5f);

    /// <summary>相机离地高度</summary>
    [Export(PropertyHint.Range, "10,200,1")]
    public float Height { get; set; } = 40f;

    // ==================== 内部状态 ====================

    /// <summary>拖拽开始时射线与地面的交点</summary>
    private Vector3? _dragAnchor;

    // ==================== 生命周期 ====================

    public override void _Ready()
    {
        // 初始化位置，使相机看向 InitialFocus
        GlobalPosition = new Vector3(InitialFocus.X, Height, InitialFocus.Z + Height);
    }

    // ==================== 输入处理 ====================

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            switch (mouseButton.ButtonIndex)
            {
                case MouseButton.Left:
                    if (mouseButton.Pressed)
                    {
                        _dragAnchor = GetGroundIntersection(mouseButton.Position);
                    }
                    else
                    {
                        _dragAnchor = null;
                    }
                    break;

                case MouseButton.WheelUp:
                    Size = Mathf.Max(MinSize, Size - ZoomStep);
                    break;

                case MouseButton.WheelDown:
                    Size = Mathf.Min(MaxSize, Size + ZoomStep);
                    break;
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if (_dragAnchor.HasValue && Input.IsMouseButtonPressed(MouseButton.Left))
            {
                var currentPoint = GetGroundIntersection(motion.Position);
                if (currentPoint.HasValue)
                {
                    // 移动相机，使 dragAnchor 始终位于鼠标下方
                    GlobalPosition += _dragAnchor.Value - currentPoint.Value;
                }
            }
        }
    }

    // ==================== 工具方法 ====================

    /// <summary>计算屏幕坐标对应的射线与 Y=0 地面的交点</summary>
    private Vector3? GetGroundIntersection(Vector2 screenPos)
    {
        Vector3 from = ProjectRayOrigin(screenPos);
        Vector3 dir = ProjectRayNormal(screenPos);
        Vector3 to = from + dir * 1000f;

        if (Mathf.Abs(to.Y - from.Y) < 0.001f)
            return null;

        float t = -from.Y / (to.Y - from.Y);
        if (t < 0f)
            return null;

        return from + (to - from) * t;
    }
}
