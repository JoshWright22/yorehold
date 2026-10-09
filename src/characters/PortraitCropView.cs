using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Picks a hero's portrait and its one 3:4 crop, as the design draws it: the pictures in the
/// portraits folders of the game and the player's art packs (or a file of their own, copied into
/// an art pack) on the left, the picture with a frame to drag and a zoom in the middle, and the
/// crop at each size the game shows it on the right.
/// </summary>
public partial class PortraitCropView : Control
{
    /// <summary>The art pack a picture file from the player's computer is copied into.</summary>
    public const string OwnPack = "my-pictures";
    private const string Folder = "portraits";

    private ContentFiles _files = null!;
    private string _name = "";
    private string _picture = "";
    private PictureFocus _focus = PictureFocus.Middle;
    private Action<string, PictureFocus>? _done;

    private GridContainer _grid = null!;
    private Label _count = null!;
    private CropCanvas _canvas = null!;
    private Label _cropText = null!;
    private HSlider _zoom = null!;
    private readonly List<PortraitView> _previews = new();
    private FileDialog? _dialog;
    private Label _said = null!;

    public override void _Ready()
    {
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        var floor = new ColorRect { Color = Palette.Night, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(floor);
        floor.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", HeroGearColumn.Box(Palette.Ink, Palette.Iron, 0));
        AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        panel.OffsetLeft = 64;
        panel.OffsetTop = 24;
        panel.OffsetRight = -64;
        panel.OffsetBottom = -24;
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 0);
        panel.AddChild(rows);

        // the head: what this is, why one crop, and the ways out
        var head = new PanelContainer();
        head.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Ink, BorderColor = Palette.Iron, BorderWidthBottom = 1,
            ContentMarginLeft = 16, ContentMarginRight = 12, ContentMarginTop = 8, ContentMarginBottom = 8 });
        rows.AddChild(head);
        var headRow = new HBoxContainer();
        headRow.AddThemeConstantOverride("separation", 12);
        head.AddChild(headRow);
        var title = new Label { Text = "Portrait", ThemeTypeVariation = "TitleLabel", VerticalAlignment = VerticalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        title.AddThemeColorOverride("font_color", Palette.Bone);
        headRow.AddChild(title);
        Label why = HeroGearColumn.Dim("every portrait in the game is 3:4", 13);
        why.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        headRow.AddChild(why);
        Button use = HeroGearColumn.Amber("Use this portrait");
        use.Pressed += () =>
        {
            if (_picture.Length > 0)
            {
                _done?.Invoke(_picture, _focus);
            }
            Visible = false;
        };
        headRow.AddChild(use);
        Button close = HeroGearColumn.Outlined("Close  Esc");
        close.Pressed += () => Visible = false;
        headRow.AddChild(close);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 0);
        rows.AddChild(body);

        // the pictures to pick from
        var left = Column(body, 232, "From your art packs");
        _grid = new GridContainer { Columns = 4 };
        _grid.AddThemeConstantOverride("h_separation", 4);
        _grid.AddThemeConstantOverride("v_separation", 4);
        var gridScroll = new ScrollContainer { CustomMinimumSize = new Vector2(0, 260), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        gridScroll.AddChild(_grid);
        left.AddChild(gridScroll);
        _count = HeroGearColumn.Dim("", 12);
        left.AddChild(_count);
        Button file = HeroGearColumn.Outlined("Use a picture file...");
        file.Pressed += OpenFile;
        left.AddChild(file);
        Label hint = HeroGearColumn.Dim("PNG, JPG or WebP, best at least 540 × 720. It is copied into the art pack \"my-pictures\"; pictures made with generators are not allowed.", 12);
        hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        left.AddChild(hint);
        _said = new Label { ThemeTypeVariation = "WarnLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        left.AddChild(_said);

        // the picture and its frame
        var middle = Column(body, 0, "Drag the frame to choose the crop");
        middle.GetParent<Control>().SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _cropText = new Label { ThemeTypeVariation = "NumberLabel", HorizontalAlignment = HorizontalAlignment.Right };
        _cropText.AddThemeFontSizeOverride("font_size", 12);
        middle.AddChild(_cropText);
        _canvas = new CropCanvas { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(300, 240) };
        _canvas.Moved += focus => Show(focus);
        middle.AddChild(_canvas);
        var zoomRow = new HBoxContainer();
        zoomRow.AddThemeConstantOverride("separation", 12);
        var zoomName = new Label { Text = "Zoom", ThemeTypeVariation = "TitleLabel", VerticalAlignment = VerticalAlignment.Center };
        zoomName.AddThemeFontSizeOverride("font_size", 13);
        zoomRow.AddChild(zoomName);
        _zoom = new HSlider { MinValue = 1, MaxValue = 4, Step = 0.01, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        _zoom.ValueChanged += value => Show(_focus with { Zoom = value });
        // a thin grey track, amber up to the grabber
        _zoom.AddThemeStyleboxOverride("slider", new StyleBoxFlat { BgColor = Palette.Iron, ContentMarginTop = 2, ContentMarginBottom = 2 });
        _zoom.AddThemeStyleboxOverride("grabber_area", new StyleBoxFlat { BgColor = Palette.Straw, ContentMarginTop = 2, ContentMarginBottom = 2 });
        _zoom.AddThemeStyleboxOverride("grabber_area_highlight", new StyleBoxFlat { BgColor = Palette.Straw, ContentMarginTop = 2, ContentMarginBottom = 2 });
        zoomRow.AddChild(_zoom);
        Button reset = HeroGearColumn.Outlined("Reset crop", 13);
        reset.Pressed += () => Show(PictureFocus.Middle);
        zoomRow.AddChild(reset);
        middle.AddChild(zoomRow);

        // the crop at each size the game draws it
        var right = Column(body, 280, "How it looks in the game");
        var small = new HFlowContainer();
        small.AddThemeConstantOverride("h_separation", 12);
        small.AddThemeConstantOverride("v_separation", 8);
        right.AddChild(small);
        foreach ((string where, int width) in new[] { ("Party list", 36), ("Lobby", 54), ("Hotbar", 60), ("Talking", 114), ("Inventory", 120) })
        {
            var holder = new VBoxContainer();
            holder.AddThemeConstantOverride("separation", 2);
            var view = new PortraitView { CustomMinimumSize = new Vector2(width, width * 4 / 3), MouseFilter = MouseFilterEnum.Ignore, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
            holder.AddChild(view);
            holder.AddChild(HeroGearColumn.Dim($"{where} {width}×{width * 4 / 3}", 12));
            small.AddChild(holder);
            _previews.Add(view);
        }
        Label one = HeroGearColumn.Dim("One crop is used everywhere: party list, hotbar, inventory, lobby, conversations and the character sheet.", 12);
        one.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        right.AddChild(one);
    }

    // a column of the body: a caps heading over its parts, a line between columns
    private static VBoxContainer Column(HBoxContainer body, float width, string heading)
    {
        var box = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Ink, BorderColor = Palette.Iron, BorderWidthRight = 1,
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 16, ContentMarginBottom = 16 });
        body.AddChild(box);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 10);
        box.AddChild(rows);
        var caps = new Label { Text = heading.ToUpperInvariant(), ThemeTypeVariation = "CapsLabel" };
        caps.AddThemeFontSizeOverride("font_size", 10);
        rows.AddChild(caps);
        return rows;
    }

    /// <summary>Opens on a hero's picture and crop; done hears the picture and crop the player keeps.</summary>
    public void Open(ContentFiles files, string name, string picture, PictureFocus focus, Action<string, PictureFocus> done)
    {
        _files = files;
        _name = name.Trim().Length > 0 ? name.Trim() : "?";
        _done = done;
        _said.Text = "";
        Visible = true;
        FillGrid();
        Pick(picture.Length > 0 ? picture : _files.Pictures(Folder).FirstOrDefault() ?? "", focus);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (Visible && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Visible = false;
            GetViewport().SetInputAsHandled();
        }
    }

    private void FillGrid()
    {
        foreach (Node old in _grid.GetChildren())
        {
            _grid.RemoveChild(old);
            old.QueueFree();
        }
        List<string> pictures = _files.Pictures(Folder);
        foreach (string path in pictures)
        {
            var thumb = new Button { ToggleMode = true, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(46, 60), ThemeTypeVariation = "PickButton",
                TooltipText = System.IO.Path.GetFileNameWithoutExtension(path) };
            thumb.SetPressedNoSignal(path == _picture);
            var face = new PortraitView { MouseFilter = MouseFilterEnum.Ignore };
            thumb.AddChild(face);
            face.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            face.OffsetLeft = face.OffsetTop = 3;
            face.OffsetRight = face.OffsetBottom = -3;
            face.Show(thumb.TooltipText, Palette.Leather, false, PlayerArt.Texture(_files, path));
            string picked = path;
            thumb.Pressed += () => Pick(picked, PictureFocus.Middle);
            _grid.AddChild(thumb);
        }
        _count.Text = pictures.Count == 0 ? "No pictures yet. Art packs with a portraits folder show theirs here."
            : pictures.Count == 1 ? "1 picture" : $"{pictures.Count} pictures";
        _count.AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }

    private void Pick(string picture, PictureFocus focus)
    {
        _picture = picture;
        foreach (Button thumb in _grid.GetChildren().OfType<Button>())
        {
            thumb.SetPressedNoSignal(_files.Pictures(Folder).IndexOf(picture) == thumb.GetIndex());
        }
        _canvas.Picture = picture.Length > 0 ? PlayerArt.Texture(_files, picture) : null;
        Show(focus);
    }

    private void Show(PictureFocus focus)
    {
        _focus = focus with { Zoom = Math.Clamp(focus.Zoom, 1, _zoom.MaxValue) };
        _zoom.SetValueNoSignal(_focus.Zoom);
        _canvas.Focus = _focus;
        Texture2D? picture = _canvas.Picture;
        if (picture != null)
        {
            (double x, double y, double w, double h) = _focus.Cut(picture.GetWidth(), picture.GetHeight(), 0.75);
            _cropText.Text = $"crop {w:0} × {h:0} at {x:0}, {y:0}";
        }
        else
        {
            _cropText.Text = "";
        }
        foreach (PortraitView view in _previews)
        {
            view.Show(_name, Palette.Leather, false, picture, _focus);
        }
    }

    // A picture from the player's computer, copied into their own art pack so the game finds it
    // the way it finds every other portrait.
    private void OpenFile()
    {
        _dialog ??= MakeDialog();
        _dialog.PopupCentered(new Vector2I(900, 600));
    }

    private FileDialog MakeDialog()
    {
        var dialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            Filters = new[] { "*.png, *.jpg, *.jpeg, *.webp ; Pictures" },
            Title = "A picture for the portrait",
            UseNativeDialog = true,
        };
        dialog.FileSelected += path =>
        {
            try
            {
                string folder = System.IO.Path.Combine(ProjectSettings.GlobalizePath("user://art"), OwnPack, Folder);
                System.IO.Directory.CreateDirectory(folder);
                string name = new string(System.IO.Path.GetFileNameWithoutExtension(path).ToLowerInvariant()
                    .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
                name = (name.Length == 0 ? "picture" : name) + System.IO.Path.GetExtension(path).ToLowerInvariant();
                System.IO.File.Copy(path, System.IO.Path.Combine(folder, name), true);
                // the art pack may be new: the content is looked at again with it in
                _files = App.Content();
                FillGrid();
                Pick($"{Folder}/{name}", PictureFocus.Middle);
            }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
            {
                _said.Text = "That picture couldn't be copied: " + error.Message;
            }
        };
        AddChild(dialog);
        return dialog;
    }
}
