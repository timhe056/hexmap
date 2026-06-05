using System;
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

    /* Part 8: 水位编辑 */
    [Export]
    public bool ApplyWaterLevel { get; set; } = false;
    [Export(PropertyHint.Range, "0,6,1")]
    public int ActiveWaterLevel { get; set; } = 0;

    [Export(PropertyHint.Range, "0,4,1")]
    public int BrushSize
    {
        get => Grid?.BrushSize ?? 0;
        set { if (Grid != null) Grid.BrushSize = value; }
    }

    public int ActiveColorIndex { get; private set; } = -1;

    /* Part 9: 地形特征级别 */
    [Export(PropertyHint.Range, "0,3,1")]
    public int ActiveUrbanLevel { get; set; } = 0;
    [Export(PropertyHint.Range, "0,3,1")]
    public int ActiveFarmLevel { get; set; } = 0;
    [Export(PropertyHint.Range, "0,3,1")]
    public int ActivePlantLevel { get; set; } = 0;

    // UI 引用
    private Panel _panel;
    private TabContainer _tabContainer;

    // Terrain Tab
    private HBoxContainer _colorRow;
    private CheckBox _applyElevationCheck;
    private HSlider _elevationSlider;
    private CheckBox _applyWaterLevelCheck;
    private HSlider _waterLevelSlider;
    private HSlider _brushSlider;

    // Features Tab
    private Button _riverIgnoreBtn;
    private Button _riverAddBtn;
    private Button _riverRemoveBtn;
    private Button _roadIgnoreBtn;
    private Button _roadAddBtn;
    private Button _roadRemoveBtn;
    private HSlider _urbanSlider;
    private Label _urbanValueLabel;
    private HSlider _farmSlider;
    private Label _farmValueLabel;
    private HSlider _plantSlider;
    private Label _plantValueLabel;

    // Settings Tab
    private CheckBox _showLabelsCheck;
    private CheckBox _brushModeCheck;

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;
        BuildUI();
    }

    private void BuildUI()
    {
        /* Panel 容器：加宽到 260，高度降到 400，配合 Tab + ScrollContainer */
        _panel = new Panel();
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _panel.OffsetLeft = -270;
        _panel.OffsetTop = 10;
        _panel.OffsetRight = -10;
        _panel.OffsetBottom = 400;
        _panel.MouseFilter = Control.MouseFilterEnum.Stop;
        AddChild(_panel);

        _tabContainer = new TabContainer();
        _tabContainer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _tabContainer.OffsetLeft = 8;
        _tabContainer.OffsetTop = 8;
        _tabContainer.OffsetRight = -8;
        _tabContainer.OffsetBottom = -8;
        _panel.AddChild(_tabContainer);

        /* ───────── Tab 1: Terrain ───────── */
        var (_, terrainVBox) = CreateTab("Terrain");

        var colorLabel = new Label { Text = "Color" };
        terrainVBox.AddChild(colorLabel);

        _colorRow = new HBoxContainer();
        terrainVBox.AddChild(_colorRow);
        _colorRow.AddChild(CreateColorButton("---", Colors.Gray, -1));
        for (int i = 0; i < HexGrid.TerrainColors.Length; i++)
            _colorRow.AddChild(CreateColorButton("", HexGrid.TerrainColors[i], i));

        // 高程行
        (_elevationSlider, _applyElevationCheck) = CreateSliderRow(terrainVBox, "Elev", 0, 6, ActiveElevation);
        _applyElevationCheck.Toggled += OnElevationToggled;
        _applyElevationCheck.ButtonPressed = ApplyElevation;
        _elevationSlider.ValueChanged += v => { ActiveElevation = (int)v; if (Grid != null) Grid.ActiveElevation = ActiveElevation; };

        // 水位行
        (_waterLevelSlider, _applyWaterLevelCheck) = CreateSliderRow(terrainVBox, "Water", 0, 6, ActiveWaterLevel);
        _applyWaterLevelCheck.Toggled += OnWaterLevelToggled;
        _applyWaterLevelCheck.ButtonPressed = ApplyWaterLevel;
        _waterLevelSlider.ValueChanged += v => { ActiveWaterLevel = (int)v; if (Grid != null) Grid.ActiveWaterLevel = ActiveWaterLevel; };

        // 笔刷大小行
        (_brushSlider, _) = CreateSliderRow(terrainVBox, "Brush", 0, 4, BrushSize);
        _brushSlider.ValueChanged += v => { BrushSize = (int)v; };

        /* ───────── Tab 2: Features ───────── */
        var (_, featuresVBox) = CreateTab("Features");

        // 河流行
        featuresVBox.AddChild(new Label { Text = "River" });
        var riverRow = new HBoxContainer();
        featuresVBox.AddChild(riverRow);
        _riverIgnoreBtn = CreateRiverModeButton("Ignore", OptionalToggle.Ignore, true);
        _riverAddBtn = CreateRiverModeButton("Add", OptionalToggle.Yes, false);
        _riverRemoveBtn = CreateRiverModeButton("Remove", OptionalToggle.No, false);
        riverRow.AddChild(_riverIgnoreBtn);
        riverRow.AddChild(_riverAddBtn);
        riverRow.AddChild(_riverRemoveBtn);

        // 道路行
        featuresVBox.AddChild(new Label { Text = "Road" });
        var roadRow = new HBoxContainer();
        featuresVBox.AddChild(roadRow);
        _roadIgnoreBtn = CreateRoadModeButton("Ignore", OptionalToggle.Ignore, true);
        _roadAddBtn = CreateRoadModeButton("Add", OptionalToggle.Yes, false);
        _roadRemoveBtn = CreateRoadModeButton("Remove", OptionalToggle.No, false);
        roadRow.AddChild(_roadIgnoreBtn);
        roadRow.AddChild(_roadAddBtn);
        roadRow.AddChild(_roadRemoveBtn);

        // 地形特征级别滑条
        _urbanSlider = AddLevelSlider(featuresVBox, "Urban", 0, out _urbanValueLabel, v =>
        {
            ActiveUrbanLevel = (int)v;
            if (Grid != null) Grid.ActiveUrbanLevel = ActiveUrbanLevel;
        });
        _farmSlider = AddLevelSlider(featuresVBox, "Farm", 0, out _farmValueLabel, v =>
        {
            ActiveFarmLevel = (int)v;
            if (Grid != null) Grid.ActiveFarmLevel = ActiveFarmLevel;
        });
        _plantSlider = AddLevelSlider(featuresVBox, "Plant", 0, out _plantValueLabel, v =>
        {
            ActivePlantLevel = (int)v;
            if (Grid != null) Grid.ActivePlantLevel = ActivePlantLevel;
        });

        /* ───────── Tab 3: Settings ───────── */
        var (_, settingsVBox) = CreateTab("Settings");

        _brushModeCheck = new CheckBox();
        _brushModeCheck.Text = "Brush Mode (Tab)";
        _brushModeCheck.ButtonPressed = false;
        _brushModeCheck.Toggled += OnBrushModeToggled;
        settingsVBox.AddChild(_brushModeCheck);

        _showLabelsCheck = new CheckBox();
        _showLabelsCheck.Text = "Show Labels";
        _showLabelsCheck.ButtonPressed = false;
        _showLabelsCheck.Toggled += OnShowLabelsToggled;
        settingsVBox.AddChild(_showLabelsCheck);
    }

    /* 创建 Tab + ScrollContainer + VBoxContainer，返回 VBox 用于添加内容 */
    private (ScrollContainer, VBoxContainer) CreateTab(string title)
    {
        var scroll = new ScrollContainer();
        scroll.Name = title;
        scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;

        var vbox = new VBoxContainer();
        vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        // 给 vbox 底部留点 padding，避免最后一项贴边
        vbox.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(vbox);

        _tabContainer.AddChild(scroll);
        return (scroll, vbox);
    }

    /* 统一封装 checkbox + slider + value-label 的行，返回 (slider, checkBox) */
    private (HSlider, CheckBox) CreateSliderRow(VBoxContainer parent, string label, int min, int max, int initial)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);

        var checkBox = new CheckBox();
        checkBox.Text = label;
        row.AddChild(checkBox);

        var slider = new HSlider();
        slider.MinValue = min;
        slider.MaxValue = max;
        slider.Step = 1;
        slider.Value = initial;
        slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(slider);

        var valueLabel = new Label();
        valueLabel.Text = initial.ToString();
        valueLabel.CustomMinimumSize = new Vector2(20, 0);
        row.AddChild(valueLabel);

        slider.ValueChanged += v => valueLabel.Text = ((int)v).ToString();

        return (slider, checkBox);
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

    private void OnWaterLevelToggled(bool toggled)
    {
        ApplyWaterLevel = toggled;
        if (Grid != null)
        {
            Grid.ApplyWaterLevel = ApplyWaterLevel;
        }
    }

    private void OnWaterLevelChanged(double value)
    {
        ActiveWaterLevel = (int)value;
        if (Grid != null)
        {
            Grid.ActiveWaterLevel = ActiveWaterLevel;
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

    /* Part 9: 创建特征级别滑块行，返回 HSlider，通过 out 输出 Label */
    private HSlider AddLevelSlider(VBoxContainer parent, string label, int initialValue,
        out Label valueLabel, Godot.Range.ValueChangedEventHandler onChanged)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);

        row.AddChild(new Label { Text = label });

        var slider = new HSlider();
        slider.MinValue = 0;
        slider.MaxValue = 3;
        slider.Step = 1;
        slider.Value = initialValue;
        slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        slider.ValueChanged += onChanged;
        row.AddChild(slider);

        valueLabel = new Label();
        valueLabel.Text = initialValue.ToString();
        var capturedLabel = valueLabel;
        slider.ValueChanged += v => capturedLabel.Text = ((int)v).ToString();
        row.AddChild(valueLabel);

        return slider;
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

    /* Part 7: 道路模式按钮 */
    private Button CreateRoadModeButton(string text, OptionalToggle mode, bool pressed)
    {
        var btn = new Button();
        btn.Text = text;
        btn.ToggleMode = true;
        btn.ButtonPressed = pressed;
        btn.CustomMinimumSize = new Vector2(60, 28);
        btn.Pressed += () => OnRoadModeSelected(mode, btn);
        return btn;
    }

    private void OnRoadModeSelected(OptionalToggle mode, Button sender)
    {
        _roadIgnoreBtn.ButtonPressed = sender == _roadIgnoreBtn;
        _roadAddBtn.ButtonPressed = sender == _roadAddBtn;
        _roadRemoveBtn.ButtonPressed = sender == _roadRemoveBtn;
        if (Grid != null)
        {
            Grid.RoadMode = mode;
        }
        GD.Print($"[HexMapEditor] Road mode = {mode}");
    }
}
