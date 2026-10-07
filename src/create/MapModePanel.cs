using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Map mode of Create, a layout over a MapEditor: the tools on the left, the map in the middle,
/// floors, layers, size and the tool's settings on the right. Left button uses the tool, right
/// removes, middle drags the view and the wheel zooms, as in the C++ client.
/// </summary>
public partial class MapModePanel : HBoxContainer
{
    public enum Tool
    {
        Paint,
        Fill,
        Wall,
        Light,
        Marker,
        Kit,
    }

    private static readonly (Tool Tool, string Name)[] Tools =
    {
        (Tool.Paint, "Paint"), (Tool.Fill, "Fill box"), (Tool.Wall, "Wall"), (Tool.Light, "Light"), (Tool.Marker, "Marker"), (Tool.Kit, "Kit"),
    };

    // palette colours a lamp can have, by what they look like in play
    private static readonly (string Name, Color Color)[] LightColors =
    {
        ("Torch", Palette.Amber), ("Candle", Palette.Straw), ("White", Palette.Bone), ("Ember", Palette.Red), ("Cold", Palette.Sky), ("Witchlight", Palette.Mint),
    };

    private MapEditor? _editor;
    private ToolColumn _tools = null!;
    private ToolColumn _props = null!;
    private EditorMapView _view = null!;
    private Label _error = null!;

    private Tool _tool = Tool.Paint;
    private int _floor;
    private int _layer;
    private int _tile = 1;
    private bool _stroking;
    private Cell? _last;
    private Cell? _rectFrom;
    private int _rectTile;
    private EditorLight _brush = new(0, 0, 5, new ContentColor(0xd3, 0xa0, 0x68), true);
    private int? _light;
    private string _markerName = "partyStart";
    private string _kit = "";
    private string _hint = "";

    public EditorMapView View => _view;
    public Tool Using => _tool;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 0);
        _tools = Column(132, false);
        _view = new EditorMapView { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(_view);
        _props = Column(236, true);
        _error = new Label { ThemeTypeVariation = "WarnLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        _view.AddChild(_error);
        _error.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide, LayoutPresetMode.Minsize, 16);

        _view.Pressed += Press;
        _view.Dragged += Drag;
        _view.Released += Release;
        _view.DrawOver += DrawMarks;
    }

    /// <summary>A flat square of one colour, for a button's icon.</summary>
    public static Texture2D Swatch(Color color)
    {
        Image image = Image.CreateEmpty(14, 14, false, Image.Format.Rgba8);
        image.Fill(Palette.Ink);
        image.FillRect(new Rect2I(1, 1, 12, 12), color);
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>The chapter's map, or null with why it can't be shown. Called every frame.</summary>
    public void Present(MapEditor? editor, string error, Func<ContentFiles?> files)
    {
        if (!ReferenceEquals(editor, _editor))
        {
            _editor = editor;
            _floor = 0;
            _light = null;
            _view.Fit();
            _tools.Invalidate();
            _props.Invalidate();
        }
        _error.Visible = editor == null;
        _error.Text = error.Length > 0 ? error : "This package has no chapter to draw a map for.";
        if (editor == null)
        {
            return;
        }

        // an undo can take away the layer or light that was picked
        List<int> layers = editor.LayersOn(_floor);
        if (!layers.Contains(_layer))
        {
            _layer = layers.Count == 0 ? -1 : layers[0];
        }
        _tile = Math.Clamp(_tile, 1, Math.Max(1, editor.Types.Count));
        if (_light is int light && light >= editor.Lights.Count)
        {
            _light = null;
        }
        if (!editor.Kits.ContainsKey(_kit))
        {
            _kit = editor.Kits.Keys.FirstOrDefault() ?? "";
        }
        _view.Floor = _floor;
        _view.LowestFloor = editor.Floors().Low;
        _view.ShowMap(editor.Map(), files);
        _tools.Build("tools", BuildTools);
        string props = $"{_tool}|{_floor}|{string.Join(",", layers.Select(l => editor.LayerName(l)))}|{editor.Types.Count}|{_light}|"
            + $"{string.Join(",", editor.Markers.Keys)}|{editor.Kits.Count}|{_view.Atlas?.GetRid()}";
        _props.Build(props, BuildProps);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_editor != null && IsVisibleInTree() && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Delete } && _tool == Tool.Light && _light is int light)
        {
            _editor.RemoveLight(light);
            _light = null;
            GetViewport().SetInputAsHandled();
        }
    }

    private ToolColumn Column(float width, bool scroll)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        AddChild(panel);
        var column = new ToolColumn { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (scroll)
        {
            var box = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            panel.AddChild(box);
            box.AddChild(column);
        }
        else
        {
            panel.AddChild(column);
        }
        return column;
    }

    private void BuildTools()
    {
        foreach ((Tool tool, string name) in Tools)
        {
            _tools.Toggle(name, () => _tool == tool, () =>
            {
                _tool = tool;
                _hint = "";
            });
        }
        _tools.Gap();
        _tools.Toggle("Grid", () => _view.Grid, () =>
        {
            _view.Grid = !_view.Grid;
            _view.QueueRedraw();
        }, null, "ChipButton");
        _tools.Toggle("Walls", () => _view.Walls, () =>
        {
            _view.Walls = !_view.Walls;
            _view.QueueRedraw();
        }, null, "ChipButton");
        _tools.Act("Fit map", _view.Fit);
        _tools.Gap();
        _tools.Live(() => _editor == null ? "" : $"{_editor.Width} x {_editor.Height} cells");
        _tools.Live(() => _view.Hover is Cell at ? $"at {at.X}, {at.Y}" : "");
        _tools.Gap();
        _tools.Dim("Left: use\nRight: remove\nMiddle: move\nWheel: zoom");
    }

    private void BuildProps()
    {
        if (_editor == null)
        {
            return;
        }
        MapEditor editor = _editor;
        HBoxContainer floorRow = _props.Row();
        _props.Live(() => $"FLOOR {_floor}", "TitleLabel", floorRow).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ToolColumn.Narrow(_props.Act("-", () => _floor = Math.Max(MapEditor.LowestFloor, _floor - 1), () => _floor > MapEditor.LowestFloor, floorRow));
        ToolColumn.Narrow(_props.Act("+", () => _floor = Math.Min(MapEditor.HighestFloor, _floor + 1), () => _floor < MapEditor.HighestFloor, floorRow));
        List<int> layers = editor.LayersOn(_floor);
        foreach (int layer in layers)
        {
            _props.Toggle(editor.LayerName(layer), () => _layer == layer, () => _layer = layer);
        }
        if (layers.Count == 0)
        {
            _props.Dim("Nothing on this floor yet.");
        }
        HBoxContainer layerRow = _props.Row();
        _props.Act("Add layer", () => _layer = editor.AddLayer("", _floor), null, layerRow);
        _props.Act("Remove", () => editor.RemoveLayer(_layer), () => _layer >= 0 && editor.LayerCount > 1, layerRow);
        _props.Gap();

        _props.Live(() => $"SIZE {editor.Width} x {editor.Height}", "TitleLabel");
        HBoxContainer sizeRow = _props.Row();
        _props.Act("-W", () => editor.Resize(editor.Width - 1, editor.Height), () => editor.Width > 1, sizeRow);
        _props.Act("+W", () => editor.Resize(editor.Width + 1, editor.Height), () => editor.Width < MapEditor.MaxSide, sizeRow);
        _props.Act("-H", () => editor.Resize(editor.Width, editor.Height - 1), () => editor.Height > 1, sizeRow);
        _props.Act("+H", () => editor.Resize(editor.Width, editor.Height + 1), () => editor.Height < MapEditor.MaxSide, sizeRow);
        _props.Gap();

        switch (_tool)
        {
            case Tool.Paint or Tool.Fill or Tool.Wall:
                _props.Heading(_tool == Tool.Wall ? "Wall tile" : "Tile");
                for (int i = 0; i < editor.Types.Count; i++)
                {
                    int id = i + 1;
                    TileType type = editor.Types[i];
                    // in wall mode only tiles that block sight make walls; the rest are greyed
                    bool usable = _tool != Tool.Wall || type.BlocksSight;
                    string what = !type.Walkable ? (type.BlocksSight ? "wall" : "blocks") : type.Indoors ? "indoors" : "floor";
                    Button tile = _props.Toggle($"{type.Name}   {what}", () => _tile == id, () =>
                    {
                        if (usable)
                        {
                            _tile = id;
                        }
                    });
                    if (_view.Atlas != null)
                    {
                        tile.Icon = new AtlasTexture { Atlas = _view.Atlas, Region = new Rect2(i * _view.AtlasSize, 0, _view.AtlasSize, _view.AtlasSize) };
                        tile.CustomMinimumSize = new Vector2(0, 36);
                    }
                    if (!usable)
                    {
                        tile.ThemeTypeVariation = "GreyButton";
                    }
                }
                break;
            case Tool.Light:
                _props.Live(() => _light is int picked ? $"LIGHT {picked + 1}" : "NEW LIGHT", "TitleLabel");
                HBoxContainer radiusRow = _props.Row();
                _props.Live(() => $"Radius {(int)_brush.Radius} cells", "", radiusRow).SizeFlagsHorizontal = SizeFlags.ExpandFill;
                ToolColumn.Narrow(_props.Act("-", () => Brush(_brush with { Radius = Math.Max(1, _brush.Radius - 1) }), () => _brush.Radius > 1, radiusRow));
                ToolColumn.Narrow(_props.Act("+", () => Brush(_brush with { Radius = Math.Min(20, _brush.Radius + 1) }), () => _brush.Radius < 20, radiusRow));
                foreach ((string name, Color color) in LightColors)
                {
                    var content = new ContentColor((byte)color.R8, (byte)color.G8, (byte)color.B8);
                    _props.Toggle(name, () => _brush.Color == content, () => Brush(_brush with { Color = content })).Icon = Swatch(color);
                }
                _props.Toggle("Flame", () => _brush.Flame, () => Brush(_brush with { Flame = !_brush.Flame }), null, "ChipButton");
                _props.Act("Remove (Del)", () =>
                {
                    if (_light is int light)
                    {
                        editor.RemoveLight(light);
                        _light = null;
                    }
                }, () => _light != null);
                _props.Live(() => $"{editor.Lights.Count} on this map");
                break;
            case Tool.Marker:
                _props.Heading("Marker name");
                _props.Field(() => _markerName, typed => _markerName = typed.Trim(), null, "partyStart");
                foreach ((string name, Cell at) in editor.Markers)
                {
                    _props.Toggle($"{name}   {at.X}, {at.Y}", () => _markerName == name, () => _markerName = name);
                }
                break;
            case Tool.Kit:
                _props.Heading("Kit");
                foreach ((string id, Kit kit) in editor.Kits)
                {
                    _props.Toggle(kit.Name.Length == 0 ? id : kit.Name, () => _kit == id, () => _kit = id);
                }
                if (editor.Kits.Count == 0)
                {
                    _props.Dim("No kits in this package.");
                }
                break;
        }
        _props.Live(() => _hint, "WarnLabel");
    }

    // The brush keeps the settings; a picked light takes them and keeps its place.
    private void Brush(EditorLight changed)
    {
        _brush = changed;
        if (_editor != null && _light is int light && light < _editor.Lights.Count)
        {
            EditorLight at = _editor.Lights[light];
            _editor.SetLight(light, changed with { X = at.X, Y = at.Y, Name = at.Name }, $"light-{light}");
        }
    }

    // The layer to paint on; adds one if the floor has none.
    private int PaintLayer()
    {
        if (_editor != null && _layer < 0)
        {
            _layer = _editor.AddLayer(_floor == 0 ? "ground" : $"floor {_floor}", _floor);
        }
        return _layer;
    }

    private void Press(Cell at, MouseButton button)
    {
        if (_editor == null)
        {
            return;
        }
        bool left = button == MouseButton.Left;
        switch (_tool)
        {
            case Tool.Paint or Tool.Wall:
                _stroking = true;
                _last = at;
                Stroke(at, left);
                break;
            case Tool.Fill:
                _rectFrom = at;
                _rectTile = left ? _tile : 0;
                break;
            case Tool.Light:
            {
                int? under = LightAt(at);
                if (left && under is int found)
                {
                    _light = found;
                    _brush = _editor.Lights[found];
                }
                else if (left)
                {
                    _light = _editor.AddLight(_brush with { X = at.X + 0.5, Y = at.Y + 0.5, Name = "" });
                }
                else if (under is int gone)
                {
                    _editor.RemoveLight(gone);
                    _light = null;
                }
                break;
            }
            case Tool.Marker:
            {
                string under = _editor.Markers.FirstOrDefault(m => m.Value == at).Key ?? "";
                if (!left)
                {
                    if (under.Length > 0)
                    {
                        _editor.RemoveMarker(under);
                    }
                }
                else if (under.Length > 0)
                {
                    _markerName = under;
                    _props.Invalidate();
                }
                else if (_markerName.Length == 0)
                {
                    _hint = "Give the marker a name first.";
                }
                else
                {
                    _editor.SetMarker(_markerName, at);
                }
                break;
            }
            case Tool.Kit:
            {
                int? under = _editor.ObjectAt(at, _floor);
                if (!left)
                {
                    if (under is int gone)
                    {
                        _editor.RemoveObject(gone);
                    }
                }
                else if (under != null)
                {
                    _hint = "Something is already there.";
                }
                else if (_editor.PlaceKit(_kit, at, _floor) == null)
                {
                    _hint = _kit.Length == 0 ? "This package has no kits." : "It doesn't fit there.";
                }
                else
                {
                    _hint = "";
                }
                break;
            }
        }
    }

    private void Drag(Cell at)
    {
        if (_stroking && _editor != null)
        {
            Stroke(at, _view.Holding(MouseButton.Left));
        }
    }

    private void Release(Cell? at, MouseButton button)
    {
        if (_editor == null)
        {
            return;
        }
        if (_stroking)
        {
            _editor.EndStroke();
            _stroking = false;
            _last = null;
        }
        if (_rectFrom is Cell from)
        {
            int layer = _rectTile != 0 ? PaintLayer() : _layer;
            if (at is Cell to && layer >= 0)
            {
                _editor.Fill(layer, from, to, _rectTile);
            }
            _rectFrom = null;
        }
    }

    // Paints from the last cell to this one, since a fast drag skips cells between frames.
    private void Stroke(Cell at, bool left)
    {
        if (_editor == null)
        {
            return;
        }
        int layer = -1, id = 0;
        if (_tool == Tool.Paint)
        {
            layer = left ? PaintLayer() : _layer;
            id = left ? _tile : 0;
        }
        else
        {
            bool blocks = _tile >= 1 && _tile <= _editor.Types.Count && _editor.Types[_tile - 1].BlocksSight;
            id = left ? (blocks ? _tile : _editor.WallTile()) : 0;
            if (left && id == 0)
            {
                _hint = "No tile on this map blocks sight.";
            }
            else if (left)
            {
                layer = _editor.WallLayer(_floor);
            }
            else
            {
                layer = _editor.LayersOn(_floor).LastOrDefault(l => _editor.LayerName(l) == "walls", -1);
            }
        }
        if (layer < 0)
        {
            return;
        }
        Cell from = _last ?? at;
        int steps = Math.Max(Math.Abs(at.X - from.X), Math.Abs(at.Y - from.Y));
        for (int i = 0; i <= steps; i++)
        {
            double t = steps == 0 ? 0 : (double)i / steps;
            _editor.Paint(layer, new Cell((int)Math.Round(from.X + (at.X - from.X) * t), (int)Math.Round(from.Y + (at.Y - from.Y) * t)), id);
        }
        _last = at;
    }

    private int? LightAt(Cell at)
    {
        if (_editor == null)
        {
            return null;
        }
        int? under = null;
        for (int i = 0; i < _editor.Lights.Count; i++)
        {
            EditorLight light = _editor.Lights[i];
            if (Math.Abs(light.X - (at.X + 0.5)) < 0.6 && Math.Abs(light.Y - (at.Y + 0.5)) < 0.6)
            {
                under = i;
            }
        }
        return under;
    }

    private void DrawMarks(EditorMapView view)
    {
        if (_editor == null)
        {
            return;
        }
        float cell = GameMap.CellSize * view.Zoom;
        for (int i = 0; i < _editor.Lights.Count; i++)
        {
            EditorLight light = _editor.Lights[i];
            Vector2 centre = view.ToLocal(new Vector2((float)light.X, (float)light.Y) * GameMap.CellSize);
            Color color = Palette.Nearest(Color.Color8(light.Color.R, light.Color.G, light.Color.B));
            // the reach as a ring only: a see-through fill snaps to odd colours over the dark
            view.DrawArc(centre, (float)light.Radius * cell, 0, Mathf.Tau, 48, color, 1);
            view.DrawCircle(centre, cell * 0.24f, Palette.Ink);
            view.DrawCircle(centre, cell * 0.18f, color);
            if (_light == i)
            {
                view.DrawRect(new Rect2(centre - new Vector2(cell, cell) * 0.4f, new Vector2(cell, cell) * 0.8f), Palette.Straw, false, 2);
            }
        }
        foreach ((string name, Cell at) in _editor.Markers)
        {
            Rect2 box = view.CellRect(at).Grow(-cell * 0.2f);
            view.DrawRect(box, Palette.Faded(Palette.Mint, 0.55f));
            view.DrawRect(box, name == _markerName && _tool == Tool.Marker ? Palette.Straw : Palette.Teal, false, 2);
            if (cell >= 14)
            {
                view.Words(view.CellRect(at).Position + new Vector2(2, cell * 0.8f + 12), name, Palette.Mint, 12);
            }
        }
        if (_rectFrom is Cell from && view.Hover is Cell to)
        {
            int x0 = Math.Min(from.X, to.X), x1 = Math.Max(from.X, to.X), y0 = Math.Min(from.Y, to.Y), y1 = Math.Max(from.Y, to.Y);
            Rect2 box = view.CellRect(new Cell(x0, y0)).Merge(view.CellRect(new Cell(x1, y1)));
            view.DrawRect(box, _rectTile != 0 ? Palette.Faded(Palette.Bone, 0.2f) : Palette.Faded(Palette.Red, 0.25f));
            view.DrawRect(box, Palette.Bone, false, 2);
        }
        else if (view.Hover is Cell hover)
        {
            view.DrawRect(view.CellRect(hover), Palette.Bone, false, 2);
        }
    }
}
