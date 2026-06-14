using Godot;
using System.IO;

namespace HexMap;

/// <summary>
/// Part 13: 保存/加载地图弹出菜单，支持文件列表、命名、删除。
/// </summary>
public partial class SaveLoadMenu : CanvasLayer
{
    [Export]
    public HexGrid Grid { get; set; }

    private bool _saveMode;
    private Label _titleLabel;
    private Button _actionButton;
    private ItemList _fileList;
    private LineEdit _nameInput;

    public override void _Ready()
    {
        Visible = false;
        BuildUI();
    }

    private void BuildUI()
    {
        // 半透明全屏背景
        var overlay = new Panel();
        overlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        overlay.Modulate = new Color(0f, 0f, 0f, 0.5f);
        AddChild(overlay);

        // 中央菜单面板
        var menuPanel = new Panel();
        menuPanel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        menuPanel.CustomMinimumSize = new Vector2(320, 400);
        menuPanel.OffsetLeft = -160;
        menuPanel.OffsetTop = -200;
        menuPanel.OffsetRight = 160;
        menuPanel.OffsetBottom = 200;
        AddChild(menuPanel);

        var vbox = new VBoxContainer();
        vbox.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        vbox.OffsetLeft = 12;
        vbox.OffsetTop = 12;
        vbox.OffsetRight = -12;
        vbox.OffsetBottom = -12;
        menuPanel.AddChild(vbox);

        // 标题
        _titleLabel = new Label();
        _titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
        vbox.AddChild(_titleLabel);

        vbox.AddChild(new Label { Text = " " }); // spacer

        // 文件列表
        var listLabel = new Label { Text = "Files" };
        vbox.AddChild(listLabel);

        _fileList = new ItemList();
        _fileList.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _fileList.ItemSelected += OnFileSelected;
        vbox.AddChild(_fileList);

        // 输入框
        vbox.AddChild(new Label { Text = "Name" });
        _nameInput = new LineEdit();
        _nameInput.PlaceholderText = "Enter map name...";
        vbox.AddChild(_nameInput);

        vbox.AddChild(new Label { Text = " " }); // spacer

        // 按钮行
        var btnRow = new HBoxContainer();
        vbox.AddChild(btnRow);

        _actionButton = new Button();
        _actionButton.CustomMinimumSize = new Vector2(80, 32);
        _actionButton.Pressed += OnAction;
        btnRow.AddChild(_actionButton);

        var deleteBtn = new Button();
        deleteBtn.Text = "Delete";
        deleteBtn.CustomMinimumSize = new Vector2(80, 32);
        deleteBtn.Pressed += OnDelete;
        btnRow.AddChild(deleteBtn);

        var cancelBtn = new Button();
        cancelBtn.Text = "Cancel";
        cancelBtn.CustomMinimumSize = new Vector2(80, 32);
        cancelBtn.Pressed += Close;
        btnRow.AddChild(cancelBtn);
    }

    public void Open(bool saveMode)
    {
        _saveMode = saveMode;
        _titleLabel.Text = saveMode ? "Save Map" : "Load Map";
        _actionButton.Text = saveMode ? "Save" : "Load";
        FillList();
        Visible = true;
        HexCamera.Locked = true;
    }

    public void Close()
    {
        Visible = false;
        HexCamera.Locked = false;
    }

    private void FillList()
    {
        _fileList.Clear();

        string dir = ProjectSettings.GlobalizePath("res://maps");
        if (!Directory.Exists(dir)) return;

        string[] paths = Directory.GetFiles(dir, "*.map");
        System.Array.Sort(paths);

        foreach (string path in paths)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            _fileList.AddItem(name);
        }
    }

    private void OnFileSelected(long index)
    {
        if (index >= 0 && index < _fileList.ItemCount)
        {
            _nameInput.Text = _fileList.GetItemText((int)index);
        }
    }

    private string GetSelectedPath()
    {
        string mapName = _nameInput.Text.Trim();
        if (string.IsNullOrEmpty(mapName))
        {
            return null;
        }
        string dir = ProjectSettings.GlobalizePath("res://maps");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, mapName + ".map");
    }

    private void OnAction()
    {
        string path = GetSelectedPath();
        if (path == null) return;

        if (_saveMode)
        {
            Save(path);
        }
        else
        {
            Load(path);
        }
        Close();
    }

    private void OnDelete()
    {
        string path = GetSelectedPath();
        if (path == null) return;

        if (File.Exists(path))
        {
            File.Delete(path);
        }
        _nameInput.Text = "";
        FillList();
    }

    private void Save(string path)
    {
        using (BinaryWriter writer = new BinaryWriter(File.Open(path, FileMode.Create)))
        {
            writer.Write(5); // version header (Part 27)
            Grid.Save(writer);
        }
        GD.Print($"[SaveLoadMenu] Map saved to {path}");
    }

    private void Load(string path)
    {
        if (!File.Exists(path))
        {
            GD.PushError($"[SaveLoadMenu] File does not exist: {path}");
            return;
        }
        using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
        {
            int header = reader.ReadInt32();
            if (header <= 5)
            {
                Grid.Load(reader, header);
                HexCamera.ValidatePosition();
                GD.Print($"[SaveLoadMenu] Map loaded from {path}");
            }
            else
            {
                GD.PushError($"[SaveLoadMenu] Unknown map format {header}");
            }
        }
    }
}
