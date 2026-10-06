using System;
using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The map in the middle of Create's Map and Encounters modes: the tiles of one floor with the
/// floor below dimmed under them, grid lines, objects outlined by kind and what blocks sight. The
/// wheel zooms about the pointer, the middle button and the arrow keys move the view. Left and
/// right presses on a cell go to the mode as events; the mode draws its own marks over the map
/// through DrawOver.
/// </summary>
public partial class EditorMapView : Control
{
    private const float MinZoom = 0.02f, MaxZoom = 3.0f;
    private const int Cell = GameMap.CellSize;

    /// <summary>A press that began on the map, on a cell of it.</summary>
    public event Action<Cell, MouseButton>? Pressed;
    /// <summary>The pointer moved onto another cell while a press that began here is held.</summary>
    public event Action<Cell>? Dragged;
    /// <summary>A button let go after a press that began here; the cell under it, if any.</summary>
    public event Action<Cell?, MouseButton>? Released;
    /// <summary>Draws the mode's own marks, in this control's coordinates.</summary>
    public event Action<EditorMapView>? DrawOver;

    public GameMap? Map { get; private set; }
    public int Floor { get; set; }
    public int LowestFloor { get; set; }
    public bool Grid { get; set; } = true;
    public bool Walls { get; set; } = true;
    /// <summary>The owner sets the view with Look; the wheel, the middle button and the arrow keys leave it alone.</summary>
    public bool Locked { get; set; }
    /// <summary>The cell under the pointer, if it is over the map.</summary>
    public Cell? Hover { get; private set; }
    /// <summary>Screen pixels per world unit; 0 fits the map at the next draw.</summary>
    public float Zoom { get; private set; }
    /// <summary>The world position at the view's top-left.</summary>
    public Vector2 Pan { get; private set; }

    private ImageTexture? _atlas;
    private string _atlasTypes = "";
    private MouseButton _held = MouseButton.None;
    private bool _moved;

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.Click;
        AddToGroup("create_map");
        // until the view is zoomed or moved by hand, a new size fits the map again, so the first
        // frames of the layout don't leave it fitted to a size it no longer has
        Resized += () =>
        {
            if (!_moved)
            {
                Zoom = 0;
            }
            QueueRedraw();
        };
    }

    /// <summary>The map as the editor holds it now. Draws again only when it is another one.</summary>
    public void ShowMap(GameMap map)
    {
        if (ReferenceEquals(map, Map))
        {
            return;
        }
        Map = map;
        string types = string.Join("|", map.Types.ConvertAll(t => t.Name + t.Art + t.Color));
        if (types != _atlasTypes)
        {
            _atlasTypes = types;
            _atlas = TileArt.Atlas(map.Types);
        }
        QueueRedraw();
    }

    /// <summary>The tile atlas, for the palette's swatches: tile id n is at (n - 1) * TileArt.Size.</summary>
    public Texture2D? Atlas => _atlas;

    /// <summary>Puts the view's top-left at pan with zoom screen pixels per world unit.</summary>
    public void Look(Vector2 pan, float zoom)
    {
        if (Pan == pan && Zoom == zoom)
        {
            return;
        }
        _moved = true;
        Pan = pan;
        Zoom = Math.Max(zoom, 0.001f);
        QueueRedraw();
    }

    public void Fit()
    {
        Zoom = 0;
        _moved = false;
        QueueRedraw();
    }

    public Vector2 ToLocal(Vector2 world) => (world - Pan) * Zoom;

    public Vector2 ToWorld(Vector2 local) => local / Zoom + Pan;

    public Vector2 CellCentre(Cell cell) => ToLocal(new Vector2((cell.X + 0.5f) * Cell, (cell.Y + 0.5f) * Cell));

    public Rect2 CellRect(Cell cell) => new(ToLocal(new Vector2(cell.X * Cell, cell.Y * Cell)), new Vector2(Cell, Cell) * Zoom);

    /// <summary>The middle of a cell in the window, for input scripts.</summary>
    public Vector2 ScreenOfCell(Cell cell) => GetGlobalTransformWithCanvas() * CellCentre(cell);

    /// <summary>A label at screen size whatever the zoom, with a hard ink shadow so it reads on any tile.</summary>
    public void Words(Vector2 at, string text, Color color, int size = 13)
    {
        Font font = GetThemeDefaultFont();
        DrawString(font, at + Vector2.One, text, HorizontalAlignment.Left, -1, size, Palette.Ink);
        DrawString(font, at, text, HorizontalAlignment.Left, -1, size, color);
    }

    public override void _Process(double delta)
    {
        // the arrow keys move the view, unless a text box has them
        if (Map == null || Locked || !IsVisibleInTree() || GetViewport().GuiGetFocusOwner() is LineEdit || Input.IsKeyPressed(Key.Ctrl))
        {
            return;
        }
        Vector2 step = Vector2.Zero;
        if (Input.IsKeyPressed(Key.Left)) step.X -= 1;
        if (Input.IsKeyPressed(Key.Right)) step.X += 1;
        if (Input.IsKeyPressed(Key.Up)) step.Y -= 1;
        if (Input.IsKeyPressed(Key.Down)) step.Y += 1;
        if (step != Vector2.Zero && Zoom > 0)
        {
            _moved = true;
            Pan += step * 14 / Zoom;
            QueueRedraw();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (Map == null || Zoom <= 0)
        {
            return;
        }
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel when !Locked:
            {
                _moved = true;
                Vector2 before = ToWorld(wheel.Position);
                Zoom = Math.Clamp(Zoom * (wheel.ButtonIndex == MouseButton.WheelUp ? 1.15f : 1 / 1.15f), MinZoom, MaxZoom);
                Pan += before - ToWorld(wheel.Position);
                QueueRedraw();
                AcceptEvent();
                break;
            }
            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right or MouseButton.Middle } button:
            {
                SetHover(button.Position);
                if (button.Pressed && _held == MouseButton.None)
                {
                    _held = button.ButtonIndex;
                    GrabFocus(); // so a text box being typed in lets go of the keys
                    if (button.ButtonIndex != MouseButton.Middle && Hover is Cell at)
                    {
                        Pressed?.Invoke(at, button.ButtonIndex);
                    }
                }
                else if (!button.Pressed && button.ButtonIndex == _held)
                {
                    _held = MouseButton.None;
                    if (button.ButtonIndex != MouseButton.Middle)
                    {
                        Released?.Invoke(Hover, button.ButtonIndex);
                    }
                }
                QueueRedraw();
                AcceptEvent();
                break;
            }
            case InputEventMouseMotion motion:
            {
                if (_held == MouseButton.Middle && !Locked)
                {
                    _moved = true;
                    Pan -= motion.Relative / Zoom;
                }
                Cell? was = Hover;
                SetHover(motion.Position);
                if (Hover != was)
                {
                    if (_held is MouseButton.Left or MouseButton.Right && Hover is Cell at)
                    {
                        Dragged?.Invoke(at);
                    }
                }
                QueueRedraw();
                break;
            }
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit && Hover != null)
        {
            Hover = null;
            QueueRedraw();
        }
    }

    /// <summary>Whether a press that began on the map is still held with this button.</summary>
    public bool Holding(MouseButton button) => _held == button;

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Ink);
        if (Map == null)
        {
            return;
        }
        Vector2 world = new(Map.Width * Cell, Map.Height * Cell);
        if (Zoom <= 0 && Size.X > 0 && Size.Y > 0)
        {
            Zoom = Math.Clamp(Math.Min(Size.X / world.X, Size.Y / world.Y) * 0.94f, MinZoom, MaxZoom);
            Pan = (world - Size / Zoom) / 2;
        }
        if (Zoom <= 0)
        {
            return;
        }

        // only the cells in view
        Vector2 topLeft = ToWorld(Vector2.Zero), bottomRight = ToWorld(Size);
        int x0 = Math.Max(0, (int)Math.Floor(topLeft.X / Cell)), y0 = Math.Max(0, (int)Math.Floor(topLeft.Y / Cell));
        int x1 = Math.Min(Map.Width - 1, (int)Math.Floor(bottomRight.X / Cell)), y1 = Math.Min(Map.Height - 1, (int)Math.Floor(bottomRight.Y / Cell));

        DrawRect(new Rect2(ToLocal(Vector2.Zero), world * Zoom), Palette.Night);
        // the floor below shows through, dimmed, so stairs and walls can be lined up
        if (Floor > LowestFloor)
        {
            DrawFloor(Floor - 1, x0, y0, x1, y1);
            DrawRect(new Rect2(ToLocal(Vector2.Zero), world * Zoom), Palette.Faded(Palette.Ink, 0.6f));
        }
        DrawFloor(Floor, x0, y0, x1, y1);

        if (Grid && Cell * Zoom >= 8)
        {
            Color line = Palette.Faded(Palette.Ink, 0.35f);
            for (int x = x0; x <= x1 + 1; x++)
            {
                DrawLine(ToLocal(new Vector2(x * Cell, y0 * Cell)), ToLocal(new Vector2(x * Cell, (y1 + 1) * Cell)), line, 1);
            }
            for (int y = y0; y <= y1 + 1; y++)
            {
                DrawLine(ToLocal(new Vector2(x0 * Cell, y * Cell)), ToLocal(new Vector2((x1 + 1) * Cell, y * Cell)), line, 1);
            }
        }

        // objects, outlined by what they are: traps red, containers amber, doors blue, the rest bone
        foreach (MapObject o in Map.Objects)
        {
            if (o.Floor != Floor)
            {
                continue;
            }
            Color kind = o.Trap != null ? Palette.Red : o.Contents.Count > 0 || o.Tags.Contains("container") ? Palette.Amber : o.Door != null ? Palette.Sky : Palette.Bone;
            var box = new Rect2(ToLocal(new Vector2((float)o.X, (float)o.Y)), new Vector2((float)o.Width, (float)o.Height) * Zoom);
            DrawRect(box, Palette.Faded(kind, 0.3f));
            DrawRect(box, kind, false, 2);
        }

        // what blocks sight, as the game builds walls: edges between a blocking cell and an open one, floor 0 only like play
        if (Walls && Floor == 0)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    var at = new Cell(x, y);
                    if (!Map.BlocksSight(at))
                    {
                        continue;
                    }
                    Edge(at, new Cell(x - 1, y), new Vector2(x, y), new Vector2(x, y + 1));
                    Edge(at, new Cell(x + 1, y), new Vector2(x + 1, y), new Vector2(x + 1, y + 1));
                    Edge(at, new Cell(x, y - 1), new Vector2(x, y), new Vector2(x + 1, y));
                    Edge(at, new Cell(x, y + 1), new Vector2(x, y + 1), new Vector2(x + 1, y + 1));
                }
            }
        }

        DrawOver?.Invoke(this);

        if (Cell * Zoom >= 14)
        {
            foreach (MapObject o in Map.Objects)
            {
                if (o.Floor == Floor && o.Name.Length > 0)
                {
                    Words(ToLocal(new Vector2((float)o.X, (float)(o.Y + o.Height))) + new Vector2(2, 12), o.Name, Palette.Bone, 12);
                }
            }
        }
        DrawRect(new Rect2(ToLocal(Vector2.Zero), world * Zoom), Palette.Smoke, false, 1);
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Leather, false, 1);
    }

    private void Edge(Cell inside, Cell other, Vector2 from, Vector2 to)
    {
        if (Map != null && Map.Inside(other) && !Map.BlocksSight(other))
        {
            DrawLine(ToLocal(from * Cell), ToLocal(to * Cell), Palette.Amber, 2);
        }
    }

    private void DrawFloor(int floor, int x0, int y0, int x1, int y1)
    {
        if (Map == null || _atlas == null)
        {
            return;
        }
        var size = new Vector2(Cell, Cell) * Zoom;
        for (int layer = 0; layer < Map.Layers.Count; layer++)
        {
            MapLayer tiles = Map.Layers[layer];
            if (tiles.Floor != floor || !tiles.Visible)
            {
                continue;
            }
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int id = tiles.Tiles[y * Map.Width + x];
                    if (id != 0)
                    {
                        DrawTextureRectRegion(_atlas, new Rect2(ToLocal(new Vector2(x * Cell, y * Cell)), size), new Rect2((id - 1) * TileArt.Size, 0, TileArt.Size, TileArt.Size));
                    }
                }
            }
        }
    }

    private void SetHover(Vector2 local)
    {
        if (Map == null || Zoom <= 0)
        {
            Hover = null;
            return;
        }
        Vector2 world = ToWorld(local);
        var at = new Cell((int)Math.Floor(world.X / Cell), (int)Math.Floor(world.Y / Cell));
        Hover = Map.Inside(at) && new Rect2(Vector2.Zero, Size).HasPoint(local) ? at : null;
    }
}
