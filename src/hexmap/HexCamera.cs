using Godot;

namespace HexMap;

/// <summary>
/// Part 5：六边形地图相机控制器。
/// 支持 WASD 移动、QE 旋转、滚轮缩放，并限制在地图范围内。
/// 保留鼠标左键拖拽平移。
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

    [Export(PropertyHint.Range, "10,500,1")]
    public float MoveSpeedMinZoom { get; set; } = 100f;

    [Export(PropertyHint.Range, "10,500,1")]
    public float MoveSpeedMaxZoom { get; set; } = 400f;

    [Export(PropertyHint.Range, "10,360,1")]
    public float RotationSpeed { get; set; } = 180f;

    [Export]
    public HexGrid Grid { get; set; }

    // ==================== 内部状态 ====================

    private Vector3? _dragAnchor;
    private float _rotationAngle;

    // ==================== 生命周期 ====================

    public override void _Ready()
    {
        _rotationAngle = RotationDegrees.Y;
    }

    // ==================== 输入处理 ====================

    public override void _Process(double delta)
    {
        float xDelta = Input.GetAxis("ui_left", "ui_right");
        float zDelta = Input.GetAxis("ui_up", "ui_down");

        // QE 旋转
        float rotDelta = 0f;
        if (Input.IsKeyPressed(Key.Q)) rotDelta = -1f;
        if (Input.IsKeyPressed(Key.E)) rotDelta = 1f;

        if (rotDelta != 0f)
        {
            AdjustRotation(rotDelta, (float)delta);
        }

        if (xDelta != 0f || zDelta != 0f)
        {
            AdjustPosition(xDelta, zDelta, (float)delta);
        }
    }

    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            switch (mouseButton.ButtonIndex)
            {
                case MouseButton.Left:
                    if (mouseButton.Pressed)
                    {
                        // Part 6/7: 河流或道路编辑模式下禁用相机拖拽
                        if (Grid != null && (Grid.RiverMode != OptionalToggle.Ignore || Grid.RoadMode != OptionalToggle.Ignore))
                            break;
                        _dragAnchor = GetGroundIntersection(mouseButton.Position);
                    }
                    else
                    {
                        _dragAnchor = null;
                    }
                    break;

                case MouseButton.WheelUp:
                    AdjustZoom(-1f);
                    break;

                case MouseButton.WheelDown:
                    AdjustZoom(1f);
                    break;
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if (_dragAnchor.HasValue && Input.IsMouseButtonPressed(MouseButton.Left))
            {
                // Part 6/7: 河流或道路编辑模式下禁用相机拖拽
                if (Grid != null && (Grid.RiverMode != OptionalToggle.Ignore || Grid.RoadMode != OptionalToggle.Ignore))
                    return;
                var currentPoint = GetGroundIntersection(motion.Position);
                if (currentPoint.HasValue)
                {
                    GlobalPosition += _dragAnchor.Value - currentPoint.Value;
                }
            }
        }
    }

    // ==================== 相机控制 ====================

    private void AdjustZoom(float delta)
    {
        Size = Mathf.Clamp(Size + delta * ZoomStep, MinSize, MaxSize);
    }

    private void AdjustRotation(float delta, float dt)
    {
        _rotationAngle += delta * RotationSpeed * dt;
        if (_rotationAngle < 0f) _rotationAngle += 360f;
        else if (_rotationAngle >= 360f) _rotationAngle -= 360f;

        RotationDegrees = new Vector3(RotationDegrees.X, _rotationAngle, RotationDegrees.Z);
    }

    private void AdjustPosition(float xDelta, float zDelta, float dt)
    {
        Vector3 direction = (GlobalTransform.Basis * new Vector3(xDelta, 0f, zDelta)).Normalized();
        float damping = Mathf.Max(Mathf.Abs(xDelta), Mathf.Abs(zDelta));
        float speed = Mathf.Lerp(MoveSpeedMaxZoom, MoveSpeedMinZoom, Mathf.InverseLerp(MinSize, MaxSize, Size));
        float distance = speed * damping * dt;

        Vector3 position = GlobalPosition;
        position += direction * distance;
        GlobalPosition = ClampPosition(position);
    }

    private Vector3 ClampPosition(Vector3 position)
    {
        if (Grid == null) return position;

        // 计算地图边界
        int cellCountX = Grid.ChunkCountX * HexMetrics.ChunkSizeX;
        int cellCountZ = Grid.ChunkCountZ * HexMetrics.ChunkSizeZ;

        float xMax = (cellCountX - 0.5f) * (HexMetrics.InnerRadius * 2f);
        float zMax = (cellCountZ - 1) * (HexMetrics.OuterRadius * 1.5f);

        position.X = Mathf.Clamp(position.X, 0f, xMax);
        position.Z = Mathf.Clamp(position.Z, 0f, zMax);

        return position;
    }

    // ==================== 工具方法 ====================

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
