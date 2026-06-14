using Godot;

namespace HexMap;

/// <summary>
/// Part 13: 新建地图弹出菜单。
/// </summary>
public partial class NewMapMenu : CanvasLayer
{
    [Export]
    public HexGrid Grid { get; set; }

    [Export]
    public HexMapGenerator MapGenerator { get; set; }

    private bool _generateMaps = true;
    private bool _wrapping;
    private Panel _menuPanel;

    public override void _Ready()
    {
        Visible = false;
        BuildUI();
    }

    private void BuildUI()
    {
        // 半透明全屏背景（遮挡交互）
        var overlay = new Panel();
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.Modulate = new Color(0f, 0f, 0f, 0.5f);
        AddChild(overlay);

        // 中央菜单面板
        _menuPanel = new Panel();
        _menuPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        _menuPanel.CustomMinimumSize = new Vector2(240, 260);
        _menuPanel.OffsetLeft = -120;
        _menuPanel.OffsetTop = -130;
        _menuPanel.OffsetRight = 120;
        _menuPanel.OffsetBottom = 130;
        AddChild(_menuPanel);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        vbox.OffsetLeft = 12;
        vbox.OffsetTop = 12;
        vbox.OffsetRight = -12;
        vbox.OffsetBottom = -12;
        _menuPanel.AddChild(vbox);

        // 标题
        var title = new Label();
        title.Text = "New Map";
        title.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(title);

        vbox.AddChild(new Label { Text = " " }); // spacer

        // Generate Maps toggle
        var generateToggle = new CheckBox();
        generateToggle.Text = "Generate Maps";
        generateToggle.ButtonPressed = _generateMaps;
        generateToggle.Toggled += (pressed) => _generateMaps = pressed;
        vbox.AddChild(generateToggle);

        // Wrapping toggle
        var wrappingToggle = new CheckBox();
        wrappingToggle.Text = "Wrapping";
        wrappingToggle.ButtonPressed = _wrapping;
        wrappingToggle.Toggled += (pressed) => _wrapping = pressed;
        vbox.AddChild(wrappingToggle);

        vbox.AddChild(new Label { Text = " " }); // spacer

        // Small Map
        var smallBtn = new Button();
        smallBtn.Text = "Small Map";
        smallBtn.Pressed += () => CreateMap(20, 15);
        vbox.AddChild(smallBtn);

        // Medium Map
        var mediumBtn = new Button();
        mediumBtn.Text = "Medium Map";
        mediumBtn.Pressed += () => CreateMap(40, 30);
        vbox.AddChild(mediumBtn);

        // Large Map
        var largeBtn = new Button();
        largeBtn.Text = "Large Map";
        largeBtn.Pressed += () => CreateMap(80, 60);
        vbox.AddChild(largeBtn);

        vbox.AddChild(new Label { Text = " " }); // spacer

        // Cancel
        var cancelBtn = new Button();
        cancelBtn.Text = "Cancel";
        cancelBtn.Pressed += Close;
        vbox.AddChild(cancelBtn);
    }

    public void Open()
    {
        Visible = true;
        HexCamera.Locked = true;
    }

    public void Close()
    {
        Visible = false;
        HexCamera.Locked = false;
    }

    public void ToggleMapGeneration(bool toggle)
    {
        _generateMaps = toggle;
    }

    private void CreateMap(int x, int z)
    {
        if (Grid == null) return;
        if (_generateMaps && MapGenerator != null)
        {
            MapGenerator.GenerateMap(x, z, _wrapping);
        }
        else
        {
            Grid.CreateMap(x, z, _wrapping);
        }
        HexCamera.CenterOnGrid();
        HexCamera.ValidatePosition();
        Close();
    }
}
