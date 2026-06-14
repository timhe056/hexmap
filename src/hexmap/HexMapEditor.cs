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
    public HexMapGenerator MapGenerator { get; set; }

    /* Part 13: 弹出菜单引用 */
    private NewMapMenu _newMapMenu;
    private SaveLoadMenu _saveLoadMenu;

    [Export]
    public bool ApplyElevation { get; set; } = true;

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

    /* Part 12: 当前地形类型索引，-1 表示不应用 */
    public int ActiveTerrainTypeIndex { get; private set; } = -1;

    /* Part 15: 编辑模式开关（true=编辑地形，false=查看距离） */
    public static bool EditMode { get; private set; } = false;

    /* Part 2.2: 笔刷高亮数据（hex space: xy=center, z=sqr radius + 0.5, w=wrap size） */
    private static readonly StringName CellHighlightingId = "_CellHighlighting";

    public static void UpdateCellHighlightData(HexCell cell, int brushSize, int wrapSize)
    {
        if (!cell)
        {
            ClearCellHighlightData();
            return;
        }
        RenderingServer.GlobalShaderParameterSet(CellHighlightingId, new Vector4(
            cell.Coordinates.HexX,
            cell.Coordinates.HexZ,
            brushSize * brushSize + 0.5f,
            wrapSize
        ));
    }

    public static void ClearCellHighlightData()
    {
        RenderingServer.GlobalShaderParameterSet(CellHighlightingId, new Vector4(0f, 0f, -1f, 0f));
    }

    /* Part 9: 地形特征级别 */
    [Export(PropertyHint.Range, "0,3,1")]
    public int ActiveUrbanLevel { get; set; } = 0;
    [Export(PropertyHint.Range, "0,3,1")]
    public int ActiveFarmLevel { get; set; } = 0;
    [Export(PropertyHint.Range, "0,3,1")]
    public int ActivePlantLevel { get; set; } = 0;
    [Export(PropertyHint.Range, "0,3,1")]
    public int ActiveSpecialIndex { get; set; } = 0;

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
    private Button _wallIgnoreBtn;
    private Button _wallAddBtn;
    private Button _wallRemoveBtn;
    private CheckBox _applyUrbanCheck;
    private CheckBox _applyFarmCheck;
    private CheckBox _applyPlantCheck;
    private CheckBox _applySpecialCheck;
    private HSlider _urbanSlider;
    private Label _urbanValueLabel;
    private HSlider _farmSlider;
    private Label _farmValueLabel;
    private HSlider _plantSlider;
    private Label _plantValueLabel;
    private HSlider _specialSlider;
    private Label _specialValueLabel;

    // Settings Tab
    private CheckBox _showLabelsCheck;
    private CheckBox _brushModeCheck;

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;
        BuildUI();
        CallDeferred(nameof(EnsureMenus));
        CallDeferred(nameof(ApplyDefaultEditMode));
    }

    private void ApplyDefaultEditMode()
    {
        OnEditModeToggled(false);
    }

    private void EnsureMenus()
    {
        if (Grid == null) return;

        // 查找或创建 NewMapMenu
        _newMapMenu = GetTree().Root.GetNodeOrNull<NewMapMenu>("NewMapMenu");
        if (_newMapMenu == null)
        {
            _newMapMenu = new NewMapMenu();
            _newMapMenu.Name = "NewMapMenu";
            _newMapMenu.Grid = Grid;
            _newMapMenu.MapGenerator = MapGenerator;
            GetTree().Root.AddChild(_newMapMenu);
        }

        // 查找或创建 SaveLoadMenu
        _saveLoadMenu = GetTree().Root.GetNodeOrNull<SaveLoadMenu>("SaveLoadMenu");
        if (_saveLoadMenu == null)
        {
            _saveLoadMenu = new SaveLoadMenu();
            _saveLoadMenu.Name = "SaveLoadMenu";
            _saveLoadMenu.Grid = Grid;
            GetTree().Root.AddChild(_saveLoadMenu);
        }
    }

    private void OnNewMapClicked() => _newMapMenu?.Open();
    private void OnSaveClicked() => _saveLoadMenu?.Open(saveMode: true);
    private void OnLoadClicked() => _saveLoadMenu?.Open(saveMode: false);

    private void BuildUI()
    {
        /* Panel 容器：加宽到 300，高度降到 400，配合 Tab + ScrollContainer */
        _panel = new Panel();
        _panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.TopRight);
        _panel.OffsetLeft = -310;
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

        var terrainLabel = new Label { Text = "Terrain" };
        terrainVBox.AddChild(terrainLabel);

        /* 颜色按钮行：包在 ScrollContainer 里，只开水平滚动 */
        var colorScroll = new ScrollContainer();
        colorScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
        colorScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Disabled;
        colorScroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        colorScroll.CustomMinimumSize = new Vector2(0, 44);
        terrainVBox.AddChild(colorScroll);

        _colorRow = new HBoxContainer();
        colorScroll.AddChild(_colorRow);
        _colorRow.AddChild(CreateColorButton("---", Colors.Gray, null, -1));
        /* Part 14: 地形纹理按钮 */
        string[] terrainTexturePaths = {
            "res://assets/textures/terrain/Sand.png",
            "res://assets/textures/terrain/Grass.png",
            "res://assets/textures/terrain/Mud.png",
            "res://assets/textures/terrain/Stone.png",
            "res://assets/textures/terrain/Snow.png"
        };
        for (int i = 0; i < terrainTexturePaths.Length; i++)
        {
            var tex = ResourceLoader.Load<Texture2D>(terrainTexturePaths[i]);
            _colorRow.AddChild(CreateColorButton("", Grid?.Colors?[i] ?? Colors.White, tex, i));
        }

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

        // 城墙行
        featuresVBox.AddChild(new Label { Text = "Wall" });
        var wallRow = new HBoxContainer();
        featuresVBox.AddChild(wallRow);
        _wallIgnoreBtn = CreateWallModeButton("Ignore", OptionalToggle.Ignore, true);
        _wallAddBtn = CreateWallModeButton("Add", OptionalToggle.Yes, false);
        _wallRemoveBtn = CreateWallModeButton("Remove", OptionalToggle.No, false);
        wallRow.AddChild(_wallIgnoreBtn);
        wallRow.AddChild(_wallAddBtn);
        wallRow.AddChild(_wallRemoveBtn);

        // 地形特征级别（带 apply checkbox）
        featuresVBox.AddChild(new Label { Text = "Features" });
        _applyUrbanCheck = AddFeatureSlider(featuresVBox, "Urban", 0, out _urbanSlider, out _urbanValueLabel, v =>
        {
            ActiveUrbanLevel = (int)v;
            if (Grid != null) Grid.ActiveUrbanLevel = ActiveUrbanLevel;
        });
        _applyFarmCheck = AddFeatureSlider(featuresVBox, "Farm", 0, out _farmSlider, out _farmValueLabel, v =>
        {
            ActiveFarmLevel = (int)v;
            if (Grid != null) Grid.ActiveFarmLevel = ActiveFarmLevel;
        });
        _applyPlantCheck = AddFeatureSlider(featuresVBox, "Plant", 0, out _plantSlider, out _plantValueLabel, v =>
        {
            ActivePlantLevel = (int)v;
            if (Grid != null) Grid.ActivePlantLevel = ActivePlantLevel;
        });
        _applySpecialCheck = AddFeatureSlider(featuresVBox, "Special", 0, out _specialSlider, out _specialValueLabel, v =>
        {
            ActiveSpecialIndex = (int)v;
            if (Grid != null) Grid.ActiveSpecialIndex = ActiveSpecialIndex;
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

        /* Part 15: Edit Mode + Show Grid */
        var editModeCheck = new CheckBox();
        editModeCheck.Text = "Edit Mode";
        editModeCheck.ButtonPressed = false;
        editModeCheck.Toggled += OnEditModeToggled;
        settingsVBox.AddChild(editModeCheck);

        var showGridCheck = new CheckBox();
        showGridCheck.Text = "Show Grid";
        showGridCheck.ButtonPressed = false;
        showGridCheck.Toggled += OnShowGridToggled;
        settingsVBox.AddChild(showGridCheck);

        /* Part 13: New Map / Save / Load 按钮 */
        settingsVBox.AddChild(new Label { Text = " " }); // spacer
        var menuRow = new HBoxContainer();
        settingsVBox.AddChild(menuRow);

        var newMapBtn = new Button();
        newMapBtn.Text = "New Map";
        newMapBtn.CustomMinimumSize = new Vector2(80, 32);
        newMapBtn.Pressed += OnNewMapClicked;
        menuRow.AddChild(newMapBtn);

        var saveBtn = new Button();
        saveBtn.Text = "Save";
        saveBtn.CustomMinimumSize = new Vector2(80, 32);
        saveBtn.Pressed += OnSaveClicked;
        menuRow.AddChild(saveBtn);

        var loadBtn = new Button();
        loadBtn.Text = "Load";
        loadBtn.CustomMinimumSize = new Vector2(80, 32);
        loadBtn.Pressed += OnLoadClicked;
        menuRow.AddChild(loadBtn);
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
        checkBox.CustomMinimumSize = new Vector2(52, 0);
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

    private Button CreateColorButton(string text, Color color, Texture2D icon, int index)
    {
        var btn = new Button();
        btn.Text = text;
        btn.CustomMinimumSize = new Vector2(36, 36);
        if (icon != null)
        {
            btn.Icon = icon;
            btn.ExpandIcon = true;
            btn.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = Colors.White });
        }
        else if (index >= 0)
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

        btn.Pressed += () => OnTerrainTypeSelected(index);
        return btn;
    }

    private void OnTerrainTypeSelected(int index)
    {
        ActiveTerrainTypeIndex = index;
        if (Grid != null)
        {
            Grid.ActiveTerrainTypeIndex = ActiveTerrainTypeIndex;
        }
        GD.Print($"[HexMapEditor] Terrain type index = {index}");
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

    /* Part 15: 编辑模式切换 */
    private void OnEditModeToggled(bool toggled)
    {
        EditMode = toggled;
        if (Grid != null)
        {
            Grid.ShowLabels(!toggled);
            if (toggled) Grid.StopSearch();
        }
    }

    /* Part 15: 网格覆盖层切换 */
    private void OnShowGridToggled(bool toggled)
    {
        var mat = ResourceLoader.Load<ShaderMaterial>("res://assets/materials/terrain.tres");
        mat?.SetShaderParameter("grid_on", toggled);
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

    /* Part 11: 创建带 apply checkbox 的特征设置行 */
    private CheckBox AddFeatureSlider(VBoxContainer parent, string label, int initialValue,
        out HSlider slider, out Label valueLabel, Godot.Range.ValueChangedEventHandler onChanged)
    {
        var row = new HBoxContainer();
        parent.AddChild(row);

        var checkBox = new CheckBox();
        checkBox.Text = label;
        checkBox.CustomMinimumSize = new Vector2(64, 0);
        checkBox.ButtonPressed = false;
        row.AddChild(checkBox);

        slider = new HSlider();
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

        // 绑定 apply toggle 到 Grid
        string featureName = label;
        checkBox.Toggled += toggled =>
        {
            if (Grid == null) return;
            switch (featureName)
            {
                case "Urban": Grid.ApplyUrbanLevel = toggled; break;
                case "Farm": Grid.ApplyFarmLevel = toggled; break;
                case "Plant": Grid.ApplyPlantLevel = toggled; break;
                case "Special": Grid.ApplySpecialIndex = toggled; break;
            }
        };

        return checkBox;
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

    /* Part 10: 城墙模式按钮 */
    private Button CreateWallModeButton(string text, OptionalToggle mode, bool pressed)
    {
        var btn = new Button();
        btn.Text = text;
        btn.ToggleMode = true;
        btn.ButtonPressed = pressed;
        btn.CustomMinimumSize = new Vector2(60, 28);
        btn.Pressed += () => OnWallModeSelected(mode, btn);
        return btn;
    }

    private void OnWallModeSelected(OptionalToggle mode, Button sender)
    {
        _wallIgnoreBtn.ButtonPressed = sender == _wallIgnoreBtn;
        _wallAddBtn.ButtonPressed = sender == _wallAddBtn;
        _wallRemoveBtn.ButtonPressed = sender == _wallRemoveBtn;
        if (Grid != null)
        {
            Grid.WalledMode = mode;
        }
        GD.Print($"[HexMapEditor] Wall mode = {mode}");
    }

    // Part 12/13: Save/Load 逻辑已移至 SaveLoadMenu
}
