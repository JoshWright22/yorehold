using System.Numerics;

namespace Yorehold.Rules;

/// <summary>What the sky gives at a time of day. At night and underground it is the map's own ambient.</summary>
public readonly record struct Sky(ContentColor Outdoors, ContentColor Indoors, LightLevel Level, float Sight, bool Differs);

/// <summary>
/// A chapter's map in play: the tiles as the rules read them (floor, walls, roofs), the edges that
/// block sight, and the objects as they are now. The game plays on floor 0 for now. This is the
/// map's one region; the C++ framework's streaming of several regions is not used by the game.
/// </summary>
public sealed class MapState
{
    private readonly bool[] _open;
    private readonly bool[] _sight;
    private readonly bool[] _roofed;
    private readonly List<Wall> _tileWalls = new();
    private List<WorldObject> _objects = new();

    public MapState(GameMap map)
    {
        Map = map;
        int cells = map.Width * map.Height;
        _open = new bool[cells];
        _sight = new bool[cells];
        _roofed = new bool[cells];
        CacheTiles();
        BuildWalls();
        BuildIndoorAreas();
        Lights = map.Lights.Select(l => new WorldLight(
            new Vector2((float)l.X * GameMap.CellSize, (float)l.Y * GameMap.CellSize), (float)l.Radius * GameMap.CellSize)).ToList();
        ResetObjects();
    }

    public GameMap Map { get; }
    public int Width => Map.Width;
    public int Height => Map.Height;
    public IReadOnlyList<WorldObject> Objects => _objects;
    /// <summary>Edges between sight-blocking and open cells plus the sides of shut doors: they stop sight and light.</summary>
    public List<Wall> Walls { get; } = new();
    /// <summary>The indoor cells as rectangles in world units, one per run along a row.</summary>
    public List<Rect> IndoorAreas { get; } = new();
    /// <summary>The map's lamps, in world units.</summary>
    public List<WorldLight> Lights { get; }

    public bool Inside(Cell c) => Map.Inside(c);

    /// <summary>Ground under it, nothing unwalkable over it, and no shut door or other object in the way.</summary>
    public bool Walkable(Cell c)
    {
        if (!Inside(c) || !_open[c.Y * Width + c.X])
        {
            return false;
        }
        // A little inside the cell, so an object on the next cell over doesn't count.
        return Passable(new Rect(c.X * GameMap.CellSize + 1, c.Y * GameMap.CellSize + 1, GameMap.CellSize - 2, GameMap.CellSize - 2), 0);
    }

    /// <summary>Tiles only; outside the map counts as solid. Objects add their own walls.</summary>
    public bool BlocksSight(Cell c) => !Inside(c) || _sight[c.Y * Width + c.X];

    public bool Indoors(Cell c) => Inside(c) && _roofed[c.Y * Width + c.X];

    public WorldObject? Get(int id) => id >= 1 && id <= _objects.Count ? _objects[id - 1] : null;

    /// <summary>The object on this cell, if any; the last placed wins.</summary>
    public WorldObject? ObjectAt(Cell c)
    {
        var centre = new Vector2((c.X + 0.5f) * GameMap.CellSize, (c.Y + 0.5f) * GameMap.CellSize);
        for (int i = _objects.Count - 1; i >= 0; i--)
        {
            WorldObject o = _objects[i];
            if (o.Floor == 0 && !o.Destroyed && o.Area.Contains(centre))
            {
                return o;
            }
        }
        return null;
    }

    public static Cell CellOf(WorldObject o)
    {
        return new Cell((int)MathF.Floor((o.Area.X + 1) / GameMap.CellSize), (int)MathF.Floor((o.Area.Y + 1) / GameMap.CellSize));
    }

    /// <summary>The cells an object covers.</summary>
    public static List<Cell> CellsOf(WorldObject o)
    {
        Cell first = CellOf(o);
        int lastX = (int)MathF.Floor((o.Area.X + o.Area.W - 1) / GameMap.CellSize);
        int lastY = (int)MathF.Floor((o.Area.Y + o.Area.H - 1) / GameMap.CellSize);
        var cells = new List<Cell>();
        for (int y = first.Y; y <= Math.Max(first.Y, lastY); y++)
        {
            for (int x = first.X; x <= Math.Max(first.X, lastX); x++)
            {
                cells.Add(new Cell(x, y));
            }
        }
        return cells;
    }

    /// <summary>Back to the objects as the map file placed them (a new adventure).</summary>
    public void ResetObjects()
    {
        _objects = Map.Objects.Select((o, i) => new WorldObject(i + 1, o)).ToList();
        RefreshWalls();
    }

    /// <summary>Goes up each time the walls are made again, so what was worked out from them can tell.</summary>
    public int WallsChanged { get; private set; }

    /// <summary>Call after a door opened or anything else changed what blocks sight.</summary>
    public void RefreshWalls()
    {
        WallsChanged++;
        Walls.Clear();
        Walls.AddRange(_tileWalls);
        foreach (WorldObject o in _objects)
        {
            if (o.Floor != 0 || !o.BlocksSight)
            {
                continue;
            }
            var a = new Vector2(o.Area.X, o.Area.Y);
            var b = new Vector2(o.Area.X + o.Area.W, o.Area.Y);
            var c = new Vector2(o.Area.X + o.Area.W, o.Area.Y + o.Area.H);
            var d = new Vector2(o.Area.X, o.Area.Y + o.Area.H);
            Walls.Add(new Wall(a, b));
            Walls.Add(new Wall(b, c));
            Walls.Add(new Wall(c, d));
            Walls.Add(new Wall(d, a));
        }
    }

    public bool Passable(Rect area, int floor)
    {
        return !_objects.Any(o => o.Floor == floor && o.BlocksMovement && area.Overlaps(o.Area));
    }

    /// <summary>Armed traps whose area overlaps this one, in id order.</summary>
    public List<WorldObject> TrapsIn(Rect area, int floor)
    {
        return _objects.Where(o => o.Floor == floor && o.ArmedTrap && area.Overlaps(o.Area)).ToList();
    }

    /// <summary>
    /// Uses an object. A locked door opens for a key ("key:id" tag that one of the keys matches);
    /// a lever swings every door that shares one of its "link:" tags, locked or not.
    /// </summary>
    public Interaction Interact(int id, IEnumerable<string> keys)
    {
        WorldObject? o = Get(id);
        if (o == null)
        {
            return Interaction.Missing;
        }
        if (o.Destroyed)
        {
            return Interaction.Unavailable;
        }
        if (o.HasDoor)
        {
            if (o.Locked)
            {
                if (!keys.Any(key => key.StartsWith("key:", StringComparison.Ordinal) && o.Has(key)))
                {
                    return Interaction.Locked;
                }
                o.Locked = false;
            }
            o.Open = !o.Open;
            return o.Open ? Interaction.Opened : Interaction.Closed;
        }
        if (o.Has("lever"))
        {
            foreach (WorldObject other in _objects)
            {
                if (!other.HasDoor || other.Destroyed)
                {
                    continue;
                }
                if (o.Tags.Any(tag => tag.StartsWith("link:", StringComparison.Ordinal) && other.Has(tag)))
                {
                    other.Open = !other.Open;
                }
            }
            return Interaction.Activated;
        }
        return o.Has("interactable") || o.Has("container") ? Interaction.Activated : Interaction.Unavailable;
    }

    /// <summary>After a check the caller made. The door stays shut until the next Interact.</summary>
    public bool Unlock(int id)
    {
        WorldObject? o = Get(id);
        if (o == null || o.Destroyed || !o.IsLocked)
        {
            return false;
        }
        o.Locked = false;
        return true;
    }

    public bool Disarm(int id)
    {
        WorldObject? o = Get(id);
        if (o == null || !o.ArmedTrap)
        {
            return false;
        }
        o.TrapArmed = false;
        o.TrapFound = true;
        return true;
    }

    /// <summary>
    /// The trap goes off: it is found, and stays armed only if it rearms. False if no armed trap is
    /// there. The effect to run is the trap's own.
    /// </summary>
    public bool Spring(int id)
    {
        WorldObject? o = Get(id);
        if (o == null || !o.ArmedTrap)
        {
            return false;
        }
        o.TrapFound = true;
        o.TrapArmed = o.Trap!.Rearms; // ArmedTrap above means it has one
        return true;
    }

    /// <summary>Takes up to amount of an item out of a container that isn't locked; returns how many.</summary>
    public int Take(int id, string item, int amount)
    {
        WorldObject? o = Get(id);
        if (o == null || o.Destroyed || !o.Has("container") || amount <= 0 || o.IsLocked || !o.Contents.TryGetValue(item, out int held))
        {
            return 0;
        }
        int taken = Math.Min(held, amount);
        if (held == taken)
        {
            o.Contents.Remove(item);
        }
        else
        {
            o.Contents[item] = held - taken;
        }
        return taken;
    }

    public Sky SkyAt(MapTime time)
    {
        MapLighting lighting = Map.Lighting;
        ContentColor ambient = Map.Ambient;
        if (time != MapTime.Day && time != MapTime.Dusk)
        {
            return new Sky(ambient, ambient, lighting.Ambient, (float)lighting.Sight, false);
        }
        bool day = time == MapTime.Day;
        ContentColor open = day ? lighting.DaySky : lighting.DuskSky;
        // Some of the daylight finds its way in through doors and windows.
        float leak = day ? 0.3f : 0.15f;
        byte Mix(byte dark, byte bright) => (byte)(dark + (Math.Max(dark, bright) - dark) * leak);
        var roofed = new ContentColor(Mix(ambient.R, open.R), Mix(ambient.G, open.G), Mix(ambient.B, open.B));
        float sight = MathF.Max((float)lighting.Sight, (float)(day ? lighting.DaySight : lighting.DuskSight));
        return new Sky(open, roofed, day ? LightLevel.Bright : LightLevel.Dim, sight, true);
    }

    private void CacheTiles()
    {
        Array.Fill(_open, true);
        bool first = true;
        for (int layer = 0; layer < Map.Layers.Count; layer++)
        {
            if (Map.Layers[layer].Floor != 0)
            {
                continue;
            }
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int i = y * Width + x;
                    TileType? type = Map.TileAt(layer, new Cell(x, y));
                    if (first && type == null)
                    {
                        _open[i] = false; // empty ground is a hole, not a floor
                    }
                    if (type == null)
                    {
                        continue;
                    }
                    _open[i] &= type.Walkable;
                    _sight[i] |= type.BlocksSight;
                    _roofed[i] |= type.Indoors;
                }
            }
            first = false;
        }
        if (first)
        {
            Array.Fill(_open, false);
        }
    }

    // One segment per straight run of edge, so sight and light test as few walls as they can.
    private void BuildWalls()
    {
        bool Edge(Cell a, Cell b) => Inside(a) && Inside(b) && BlocksSight(a) != BlocksSight(b);
        float size = GameMap.CellSize;
        for (int y = 1; y < Height; y++)
        {
            int start = -1;
            for (int x = 0; x <= Width; x++)
            {
                bool on = x < Width && Edge(new Cell(x, y - 1), new Cell(x, y));
                if (on && start < 0)
                {
                    start = x;
                }
                if (!on && start >= 0)
                {
                    _tileWalls.Add(new Wall(new Vector2(start * size, y * size), new Vector2(x * size, y * size)));
                    start = -1;
                }
            }
        }
        for (int x = 1; x < Width; x++)
        {
            int start = -1;
            for (int y = 0; y <= Height; y++)
            {
                bool on = y < Height && Edge(new Cell(x - 1, y), new Cell(x, y));
                if (on && start < 0)
                {
                    start = y;
                }
                if (!on && start >= 0)
                {
                    _tileWalls.Add(new Wall(new Vector2(x * size, start * size), new Vector2(x * size, y * size)));
                    start = -1;
                }
            }
        }
    }

    private void BuildIndoorAreas()
    {
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (!Indoors(new Cell(x, y)))
                {
                    continue;
                }
                int start = x;
                while (x + 1 < Width && Indoors(new Cell(x + 1, y)))
                {
                    x++;
                }
                IndoorAreas.Add(new Rect(start * GameMap.CellSize, y * GameMap.CellSize, (x - start + 1) * GameMap.CellSize, GameMap.CellSize));
            }
        }
    }
}
