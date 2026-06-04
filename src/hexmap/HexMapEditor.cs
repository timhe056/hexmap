using Godot;

namespace HexMap;

/// <summary>
/// 六边形地图运行时编辑器 UI 控制器。
/// 挂载到 CanvasLayer 节点下，通过 Inspector 绑定 HexGrid。
/// </summary>
public partial class HexMapEditor : CanvasLayer
{
    [Export]
    public HexGrid Grid { get; set; }

    [Export]
    public bool ApplyElevation { get; set; } = true;

    [Export]
    public bool ApplyColor { get; set; } = false;

    [Export(PropertyHint.Range, "0,6,1")]
    public int ActiveElevation { get; set; } = 0;

    [Export(PropertyHint.Range, "0,4,1")]
    public int BrushSize
    {
        get => Grid?.BrushSize ?? 0;
        set { if (Grid != null) Grid.BrushSize = value; }
    }

    public int ActiveColorIndex { get; private set; } = -1;

    // UI 引用
    private Panel _panel;
    private HBoxContainer _colorRow;
    private CheckBox _applyElevationCheck;
    private HSlider _elevationSlider;
    private HSlider _brushSlider;
    private CheckBox _showLabelsCheck;
    private CheckBox _brushModeCheck;
    private Button _riverIgnoreBtn;
    private Button _riverAddBtn;
    private Button _riverRemoveBtn;

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;
        BuildUI();
    }

    private void BuildUI()
    {
        // Panel 容器
        _panel = new Panel();
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _panel.OffsetLeft = -220;
        _panel.OffsetTop = 10;
        _panel.OffsetRight = -10;
        _panel.OffsetBottom = 340;
        _panel.MouseFilter = Control.MouseFilterEnum.Stop; // 背景不拦截鼠标事件
        AddChild(_panel);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        vbox.OffsetLeft = 10;
        vbox.OffsetTop = 10;
        vbox.OffsetRight = -10;
        vbox.OffsetBottom = -10;
        _panel.AddChild(vbox);

        // 标题
        var title = new Label();
        title.Text = "Hex Map Editor";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        // 颜色选择行
        var colorLabel = new Label();
        colorLabel.Text = "Color";
        vbox.AddChild(colorLabel);

        _colorRow = new HBoxContainer();
        vbox.AddChild(_colorRow);

        // "---" 按钮（不涂色）
        var noneBtn = CreateColorButton("---", Colors.Gray, -1);
        _colorRow.AddChild(noneBtn);

        // 颜色预设按钮
        for (int i = 0; i < HexGrid.TerrainColors.Length; i++)
        {
            var btn = CreateColorButton("", HexGrid.TerrainColors[i], i);
            _colorRow.AddChild(btn);
        }

        // 高程行
        var elevRow = new HBoxContainer();
        vbox.AddChild(elevRow);

        _applyElevationCheck = new CheckBox();
        _applyElevationCheck.Text = "Elev";
        _applyElevationCheck.ButtonPressed = ApplyElevation;
        _applyElevationCheck.Toggled += OnElevationToggled;
        elevRow.AddChild(_applyElevationCheck);

        _elevationSlider = new HSlider();
        _elevationSlider.MinValue = 0;
        _elevationSlider.MaxValue = 6;
        _elevationSlider.Value = ActiveElevation;
        _elevationSlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _elevationSlider.ValueChanged += OnElevationChanged;
        elevRow.AddChild(_elevationSlider);

        var elevValueLabel = new Label();
        elevValueLabel.Text = ActiveElevation.ToString();
        _elevationSlider.ValueChanged += v => elevValueLabel.Text = ((int)v).ToString();
        elevRow.AddChild(elevValueLabel);

        // 笔刷大小行
        var brushRow = new HBoxContainer();
        vbox.AddChild(brushRow);

        brushRow.AddChild(new Label { Text = "Brush" });
        _brushSlider = new HSlider();
        _brushSlider.MinValue = 0;
        _brushSlider.MaxValue = 4;
        _brushSlider.Value = BrushSize;
        _brushSlider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _brushSlider.ValueChanged += v => BrushSize = (int)v;
        brushRow.AddChild(_brushSlider);

        var brushValueLabel = new Label();
        brushValueLabel.Text = BrushSize.ToString();
        _brushSlider.ValueChanged += v => brushValueLabel.Text = ((int)v).ToString();
        brushRow.AddChild(brushValueLabel);

        // 笔刷模式开关
        _brushModeCheck = new CheckBox();
        _brushModeCheck.Text = "Brush Mode (Tab)";
        _brushModeCheck.ButtonPressed = false;
        _brushModeCheck.Toggled += OnBrushModeToggled;
        vbox.AddChild(_brushModeCheck);

        // Part 6: 河流模式行
        var riverLabel = new Label();
        riverLabel.Text = "River";
        vbox.AddChild(riverLabel);

        var riverRow = new HBoxContainer();
        vbox.AddChild(riverRow);

        _riverIgnoreBtn = CreateRiverModeButton("Ignore", OptionalToggle.Ignore, true);
        riverRow.AddChild(_riverIgnoreBtn);
        _riverAddBtn = CreateRiverModeButton("Add", OptionalToggle.Yes, false);
        riverRow.AddChild(_riverAddBtn);
        _riverRemoveBtn = CreateRiverModeButton("Remove", OptionalToggle.No, false);
        riverRow.AddChild(_riverRemoveBtn);

        // Label 显示开关
        _showLabelsCheck = new CheckBox();
        _showLabelsCheck.Text = "Show Labels";
        _showLabelsCheck.ButtonPressed = false;
        _showLabelsCheck.Toggled += OnShowLabelsToggled;
        vbox.AddChild(_showLabelsCheck);
    }

    private Button CreateColorButton(string text, Color color, int index)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(36, 36);
        if (index >= 0)
        {
            btn.Modulate = color;    
            btn.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = color });        
        }
        // var styleNormal = new StyleBoxFlat();
        // styleNormal.BgColor = index >= 0 ? color : Colors.Gray;
        // styleNormal.BorderWidthBottom = 2;
        // styleNormal.BorderWidthLeft = 2;
        // styleNormal.BorderWidthRight = 2;
        // styleNormal.BorderWidthTop = 2;
        // styleNormal.BorderColor = Colors.White;
        // btn.AddThemeStyleboxOverride("normal", styleNormal);

        // var styleHover = new StyleBoxFlat();
        // styleHover.BgColor = index >= 0 ? color.Lightened(0.2f) : Colors.LightGray;
        // styleHover.BorderWidthBottom = 2;
        // styleHover.BorderWidthLeft = 2;
        // styleHover.BorderWidthRight = 2;
        // styleHover.BorderWidthTop = 2;
        // styleHover.BorderColor = Colors.Yellow;
        // btn.AddThemeStyleboxOverride("hover", styleHover);

        btn.Pressed += () => OnColorSelected(index);
        return btn;
    }

    private void OnColorSelected(int index)
    {
        ActiveColorIndex = index;
        ApplyColor = index >= 0;
        if (Grid != null)
        {
            Grid.ActiveColorIndex = ActiveColorIndex;
            Grid.ApplyColor = ApplyColor;
        }
        GD.Print($"[HexMapEditor] Color index = {index}");
    }

    private void OnElevationToggled(bool toggled)
    {
        ApplyElevation = toggled;
        if (Grid != null)
        {
            Grid.ApplyElevation = ApplyElevation;
        }
    }

    private void OnElevationChanged(double value)
    {
        ActiveElevation = (int)value;
        if (Grid != null)
        {
            Grid.ActiveElevation = ActiveElevation;
        }
    }

    private void OnShowLabelsToggled(bool toggled)
    {
        if (Grid != null)
        {
            Grid.ShowLabels(toggled);
        }
    }

    private void OnBrushModeToggled(bool toggled)
    {
        if (Grid != null)
        {
            Grid.BrushModeEnabled = toggled;
        }
    }

    private Button CreateRiverModeButton(string text, OptionalToggle mode, bool pressed)
    {
        var btn = new Button();
        btn.Text = text;
        btn.ToggleMode = true;
        btn.ButtonPressed = pressed;
        btn.CustomMinimumSize = new Vector2(60, 28);
        btn.Pressed += () => OnRiverModeSelected(mode, btn);
        return btn;
    }

    private void OnRiverModeSelected(OptionalToggle mode, Button sender)
    {
        _riverIgnoreBtn.ButtonPressed = sender == _riverIgnoreBtn;
        _riverAddBtn.ButtonPressed = sender == _riverAddBtn;
        _riverRemoveBtn.ButtonPressed = sender == _riverRemoveBtn;
        if (Grid != null)
        {
            Grid.RiverMode = mode;
        }
        GD.Print($"[HexMapEditor] River mode = {mode}");
    }
}
