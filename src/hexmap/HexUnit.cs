using Godot;
using System.Collections.Generic;
using System.IO;

namespace HexMap;

/// <summary>
/// Part 18/19/3.0: 六边形地图上的单位（角色）。
/// 3.0 重构：位置与路径使用 int 单元格索引，避免运行时在 cell 对象间保留强引用。
/// </summary>
public partial class HexUnit : Node3D
{
    private const float RotationSpeed = 180f;
    private const float TravelSpeed = 4f;
    public const int VisionRange = 3;

    public HexGrid Grid { get; set; }

    private int _locationCellIndex = -1;
    public HexCell Location
    {
        get => Grid?.GetCell(_locationCellIndex) ?? default;
        set
        {
            HexCell oldLocation = Grid?.GetCell(_locationCellIndex) ?? default;
            if (oldLocation)
            {
                Grid?.DecreaseVisibility(oldLocation, VisionRange);
                oldLocation.Unit = null;
            }

            _locationCellIndex = value ? value.Index : -1;

            HexCell newLocation = Grid?.GetCell(_locationCellIndex) ?? default;
            if (newLocation)
            {
                newLocation.Unit = this;
                Grid?.IncreaseVisibility(newLocation, VisionRange);
                Position = newLocation.Position;
                Grid?.MakeChildOfColumn(this, newLocation.Coordinates.ColumnIndex); // Part 27
            }
        }
    }

    private float _orientation;
    public float Orientation
    {
        get => _orientation;
        set
        {
            _orientation = value;
            RotationDegrees = new Vector3(0f, value, 0f);
        }
    }

    private List<int> _pathToTravel;
    private int _currentTravelLocationCellIndex = -1;
    private int _travelVersion;
    private double _delta;

    public override void _Ready()
    {
        // 创建单位视觉表现：蓝色圆柱体
        var mesh = new MeshInstance3D();
        var cylinder = new CylinderMesh();
        cylinder.TopRadius = 2.0f;
        cylinder.BottomRadius = 2.0f;
        cylinder.Height = 4.0f;
        mesh.Mesh = cylinder;
        mesh.Position = new Vector3(0f, 2.0f, 0f);

        var mat = new StandardMaterial3D();
        mat.AlbedoColor = new Color(0.2f, 0.5f, 0.9f);
        mesh.MaterialOverride = mat;

        AddChild(mesh);
    }

    public override void _Process(double delta)
    {
        _delta = delta;
    }

    public override void _EnterTree()
    {
        HexCell location = Location;
        if (location)
        {
            Position = location.Position;
            if (_currentTravelLocationCellIndex >= 0)
            {
                HexCell currentTravelLocation = Grid.GetCell(_currentTravelLocationCellIndex);
                Grid?.IncreaseVisibility(location, VisionRange);
                if (currentTravelLocation)
                {
                    Grid?.DecreaseVisibility(currentTravelLocation, VisionRange);
                }
                _currentTravelLocationCellIndex = -1;
            }
        }
    }

    public void ValidateLocation()
    {
        HexCell location = Location;
        if (location)
        {
            Position = location.Position;
        }
    }

    public bool IsValidDestination(HexCell cell)
    {
        return cell.Flags.HasAll(HexFlags.Explored | HexFlags.Explorable) &&
            !cell.Values.IsUnderwater && cell.Unit == null;
    }

    public void Travel(List<int> path)
    {
        if (path == null || path.Count == 0) return;

        /* Part 20: 不使用 Location setter（避免瞬间切换视野），直接操作字段 */
        HexCell oldLocation = Grid?.GetCell(_locationCellIndex) ?? default;
        if (oldLocation)
        {
            oldLocation.Unit = null;
        }

        HexCell destination = Grid.GetCell(path[path.Count - 1]);
        if (!destination) return;

        _locationCellIndex = destination.Index;
        destination.Unit = this;

        _pathToTravel = path;
        _travelVersion++;
        TravelPath(_travelVersion);
    }

    private async void TravelPath(int travelVersion)
    {
        if (_pathToTravel == null || _pathToTravel.Count < 2) return;

        HexCell startCell = Grid.GetCell(_pathToTravel[0]);
        HexCell nextCell = Grid.GetCell(_pathToTravel[1]);
        if (!startCell || !nextCell) return;

        Vector3 a, b, c = startCell.Position;
        Position = c;

        await LookAt(nextCell.Position, travelVersion);

        /* Part 20: 旋转完毕后再移除起点视野，避免旋转动画期间视野空缺闪烁 */
        if (_currentTravelLocationCellIndex < 0)
        {
            _currentTravelLocationCellIndex = _pathToTravel[0];
        }
        HexCell currentTravelLocation = Grid.GetCell(_currentTravelLocationCellIndex);
        if (currentTravelLocation)
        {
            Grid?.DecreaseVisibility(currentTravelLocation, VisionRange);
        }
        if (travelVersion != _travelVersion) return;

        int currentColumn = currentTravelLocation.Coordinates.ColumnIndex; // Part 27

        float t = (float)_delta * TravelSpeed;
        for (int i = 1; i < _pathToTravel.Count; i++)
        {
            _currentTravelLocationCellIndex = _pathToTravel[i];
            currentTravelLocation = Grid.GetCell(_currentTravelLocationCellIndex);
            if (!currentTravelLocation) continue;

            Grid?.IncreaseVisibility(currentTravelLocation, VisionRange);

            HexCell prevCell = Grid.GetCell(_pathToTravel[i - 1]);
            if (!prevCell) continue;

            a = c;
            b = prevCell.Position;

            /* Part 27: 跨 seam 时偏移贝塞尔控制点并切换父 column */
            int nextColumn = currentTravelLocation.Coordinates.ColumnIndex;
            if (currentColumn != nextColumn)
            {
                if (nextColumn < currentColumn - 1)
                {
                    a.X -= HexMetrics.wrapSize * HexMetrics.InnerDiameter;
                    b.X -= HexMetrics.wrapSize * HexMetrics.InnerDiameter;
                }
                else if (nextColumn > currentColumn + 1)
                {
                    a.X += HexMetrics.wrapSize * HexMetrics.InnerDiameter;
                    b.X += HexMetrics.wrapSize * HexMetrics.InnerDiameter;
                }
                Grid?.MakeChildOfColumn(this, nextColumn);
                currentColumn = nextColumn;
            }

            c = (b + currentTravelLocation.Position) * 0.5f;

            for (; t < 1f; t += (float)_delta * TravelSpeed)
            {
                if (travelVersion != _travelVersion) return;
                Position = Bezier.GetPoint(a, b, c, t);
                Vector3 d = Bezier.GetDerivative(a, b, c, t);
                d.Y = 0f;
                RotationDegrees = new Vector3(0f, Mathf.RadToDeg(Mathf.Atan2(d.X, d.Z)), 0f);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Grid?.DecreaseVisibility(currentTravelLocation, VisionRange);
            t -= 1f;
        }

        /* Part 20: 先加终点视野，再做最后一段动画，避免动画期间视野空缺闪烁 */
        HexCell location = Location;
        if (location)
        {
            Grid?.IncreaseVisibility(location, VisionRange);
        }

        HexCell lastCell = Grid.GetCell(_pathToTravel[_pathToTravel.Count - 1]);
        if (lastCell)
        {
            a = c;
            b = lastCell.Position;
            c = b;
            for (; t < 1f; t += (float)_delta * TravelSpeed)
            {
                if (travelVersion != _travelVersion) return;
                Position = Bezier.GetPoint(a, b, c, t);
                Vector3 d = Bezier.GetDerivative(a, b, c, t);
                d.Y = 0f;
                RotationDegrees = new Vector3(0f, Mathf.RadToDeg(Mathf.Atan2(d.X, d.Z)), 0f);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }

        if (travelVersion != _travelVersion) return;

        _currentTravelLocationCellIndex = -1;
        if (location)
        {
            Grid?.MakeChildOfColumn(this, location.Coordinates.ColumnIndex); // Part 27
            Position = location.Position;
        }
        Orientation = RotationDegrees.Y;

        _pathToTravel = null;
    }

    private async System.Threading.Tasks.Task LookAt(Vector3 point, int travelVersion)
    {
        /* Part 27: 选择环绕方向上最近的目标点 */
        if (HexMetrics.Wrapping)
        {
            float xDistance = point.X - Position.X;
            if (xDistance < -HexMetrics.InnerRadius * HexMetrics.wrapSize)
            {
                point.X += HexMetrics.wrapSize * HexMetrics.InnerDiameter;
            }
            else if (xDistance > HexMetrics.InnerRadius * HexMetrics.wrapSize)
            {
                point.X -= HexMetrics.wrapSize * HexMetrics.InnerDiameter;
            }
        }

        point.Y = Position.Y;
        float fromAngle = RotationDegrees.Y;
        Vector3 dir = point - Position;
        dir.Y = 0f;
        if (dir.LengthSquared() < 0.0001f) return;

        float toAngle = Mathf.RadToDeg(Mathf.Atan2(dir.X, dir.Z));
        float angleDiff = Mathf.Wrap(toAngle - fromAngle, -180f, 180f);
        float totalAngle = Mathf.Abs(angleDiff);
        if (totalAngle < 0.001f) return;

        float speed = RotationSpeed / totalAngle;
        for (float t = (float)_delta * speed; t < 1f; t += (float)_delta * speed)
        {
            if (travelVersion != _travelVersion) return;
            RotationDegrees = new Vector3(0f, fromAngle + angleDiff * t, 0f);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (travelVersion != _travelVersion) return;
        RotationDegrees = new Vector3(0f, toAngle, 0f);
        Orientation = RotationDegrees.Y;
    }

    public void Die()
    {
        HexCell location = Location;
        if (location)
        {
            Grid?.DecreaseVisibility(location, VisionRange);
            location.Unit = null;
        }
        QueueFree();
    }

    public void Save(BinaryWriter writer)
    {
        Location.Coordinates.Save(writer);
        writer.Write(_orientation);
    }

    public static void Load(BinaryReader reader, HexGrid grid)
    {
        HexCoordinates coordinates = HexCoordinates.Load(reader);
        float orientation = reader.ReadSingle();
        var unit = new HexUnit();
        grid.AddUnit(unit, grid.GetCell(coordinates), orientation);
    }
}
