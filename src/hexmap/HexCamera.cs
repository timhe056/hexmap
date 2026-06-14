using Godot;

namespace HexMap;

/// <summary>
/// Part 5：六边形地图相机控制器。
/// Perspective 投影，固定倾角，zoom 时只移动相机距离。
/// </summary>
public partial class HexCamera : Camera3D
{
    // ==================== Inspector 参数 ====================

    [Export(PropertyHint.Range, "10,500,1")]
    public float MinZoom { get; set; } = 20f;

    [Export(PropertyHint.Range, "10,500,1")]
    public float MaxZoom { get; set; } = 300f;

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
    private float _zoom = 0.5f;
    private Vector3 _swivelPosition;
    private float _swivelAngle;

    /* Part 13: 单例引用，用于 Locked / ValidatePosition */
    private static HexCamera _instance;

    public static bool Locked
    {
        set
        {
            if (_instance != null)
            {
                _instance.ProcessMode = value ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
            }
        }
    }

    public static void ValidatePosition()
    {
        _instance?.AdjustPosition(0f, 0f, 0f);
    }

    /* Part 13: 创建/加载地图后将相机 rig 移到地图中心 */
    public static void CenterOnGrid()
    {
        if (_instance == null || _instance.Grid == null) return;
        Vector3 center = _instance.Grid.CalculateGridCenter();
        _instance._swivelPosition = new Vector3(center.X, 0f, center.Z);
        _instance._swivelPosition = _instance.ClampOrWrapPosition(_instance._swivelPosition);
        _instance.ApplyZoom();
    }

    // ==================== 生命周期 ====================

    public override void _Ready()
    {
        _instance = this;

        // rig 放在地面上，XZ 取当前相机位置
        _swivelPosition = GlobalPosition;
        _swivelPosition.Y = 0f;

        _rotationAngle = RotationDegrees.Y;
        // 从当前 transform 的 Basis 直接提取倾角，避开 RotationDegrees.X 读取符号问题
        _swivelAngle = Mathf.Atan2(GlobalTransform.Basis.Z.Y, GlobalTransform.Basis.Z.Z) * 180f / Mathf.Pi;

        // 切换到 Perspective
        Projection = ProjectionType.Perspective;
        Fov = 60f;
        Near = 0.3f;

        ApplyZoom();
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
        // 滚轮缩放放在 _Input 中，即使鼠标在 UI 上也能缩放
        if (@event is InputEventMouseButton mouseBtn)
        {
            switch (mouseBtn.ButtonIndex)
            {
                case MouseButton.WheelUp:
                    AdjustZoom(-1f);
                    break;
                case MouseButton.WheelDown:
                    AdjustZoom(1f);
                    break;
            }
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    // Part 6/7: 河流或道路编辑模式下禁用相机拖拽
                    if (Grid != null && (Grid.RiverMode != OptionalToggle.Ignore || Grid.RoadMode != OptionalToggle.Ignore))
                        return;
                    _dragAnchor = GetGroundIntersection(mouseButton.Position);
                }
                else
                {
                    _dragAnchor = null;
                }
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
                    _swivelPosition += _dragAnchor.Value - currentPoint.Value;
                    _swivelPosition = ClampOrWrapPosition(_swivelPosition);
                    ApplyZoom();
                }
            }
        }
    }

    // ==================== 相机控制 ====================

    private void AdjustZoom(float delta)
    {
        _zoom = Mathf.Clamp(_zoom + delta * ZoomStep * 0.01f, 0f, 1f);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        float distance = Mathf.Lerp(MinZoom, MaxZoom, _zoom);
        float rad = _swivelAngle * Mathf.Pi / 180f;

        Vector3 offset = new Vector3(
            0f,
            Mathf.Sin(rad) * distance,
            Mathf.Cos(rad) * distance
        );

        // 应用 Y 轴旋转
        Basis rotY = new Basis(Vector3.Up, _rotationAngle * Mathf.Pi / 180f);
        offset = rotY * offset;

        GlobalPosition = _swivelPosition + offset;
        LookAt(_swivelPosition, Vector3.Up);
    }

    private void AdjustRotation(float delta, float dt)
    {
        _rotationAngle += delta * RotationSpeed * dt;
        if (_rotationAngle < 0f) _rotationAngle += 360f;
        else if (_rotationAngle >= 360f) _rotationAngle -= 360f;

        ApplyZoom();
    }

    private void AdjustPosition(float xDelta, float zDelta, float dt)
    {
        Basis rotY = new Basis(Vector3.Up, _rotationAngle * Mathf.Pi / 180f);
        Vector3 direction = (rotY * new Vector3(xDelta, 0f, zDelta)).Normalized();
        float damping = Mathf.Max(Mathf.Abs(xDelta), Mathf.Abs(zDelta));
        float speed = Mathf.Lerp(MoveSpeedMaxZoom, MoveSpeedMinZoom, _zoom);
        float distance = speed * damping * dt;

        _swivelPosition += direction * distance;
        _swivelPosition = ClampOrWrapPosition(_swivelPosition);
        ApplyZoom();
    }

    private Vector3 ClampOrWrapPosition(Vector3 position)
    {
        if (Grid == null) return position;
        return Grid.Wrapping ? WrapPosition(position) : ClampPosition(position);
    }

    private Vector3 ClampPosition(Vector3 position)
    {
        if (Grid == null) return position;

        float xMax = (Grid.CellCountX - 0.5f) * HexMetrics.InnerDiameter;
        float zMax = (Grid.CellCountZ - 1) * (HexMetrics.OuterRadius * 1.5f);

        position.X = Mathf.Clamp(position.X, 0f, xMax);
        position.Z = Mathf.Clamp(position.Z, 0f, zMax);

        return position;
    }

    /* Part 27: 环绕地图的相机位置循环 */
    private Vector3 WrapPosition(Vector3 position)
    {
        if (Grid == null) return position;

        float width = Grid.CellCountX * HexMetrics.InnerDiameter;
        while (position.X < 0f)
        {
            position.X += width;
        }
        while (position.X > width)
        {
            position.X -= width;
        }

        float zMax = (Grid.CellCountZ - 1) * (HexMetrics.OuterRadius * 1.5f);
        position.Z = Mathf.Clamp(position.Z, 0f, zMax);

        Grid.CenterMap(position.X);
        return position;
    }

    // ==================== 工具方法 ====================

    private Vector3? GetGroundIntersection(Vector2 screenPos)
    {
        Vector3 from = ProjectRayOrigin(screenPos);
        Vector3 dir = ProjectRayNormal(screenPos);

        if (Mathf.Abs(dir.Y) < 0.001f)
            return null;

        float t = -from.Y / dir.Y;
        if (t < 0f)
            return null;

        return from + dir * t;
    }
}
