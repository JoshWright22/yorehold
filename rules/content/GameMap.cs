using System.Text.Json.Nodes;

namespace Yorehold.Rules;

public class TileType
{
    public string Name { get; init; } = "";
    /// <summary>A built-in painter ("grass", "wall"...); other names use Color.</summary>
    public string Art { get; init; } = "";
    public ContentColor Color { get; init; } = new(128, 128, 128);
    public bool Walkable { get; init; } = true;
    public bool BlocksSight { get; init; }
    /// <summary>Under a roof: daylight doesn't reach it.</summary>
    public bool Indoors { get; init; }
}

/// <summary>One layer of tiles. A tile id is the place of its type in the map's list plus 1; 0 is empty.</summary>
public class MapLayer
{
    public string Name { get; init; } = "";
    public int Floor { get; init; }
    public bool Visible { get; init; } = true;
    public int[] Tiles { get; init; } = Array.Empty<int>();
    /// <summary>A painted picture behind the tiles, with its area in world units. Empty = none.</summary>
    public string Image { get; init; } = "";
    public double[] ImageArea { get; init; } = Array.Empty<double>();
}

/// <summary>A lamp. Position and radius are in cells; [4.5, 3.5] is the middle of cell [4, 3].</summary>
public record MapLight(double X, double Y, double Radius, ContentColor Color, bool Flame, string Name);

public enum LightingMode
{
    Off,
    Mood,
    Rules,
}

public enum LightLevel
{
    Dark,
    Dim,
    Bright,
}

public enum MapTime
{
    Day,
    Dusk,
    Night,
    Underground,
}

public class MapLighting
{
    public LightingMode Mode { get; init; } = LightingMode.Mood;
    /// <summary>The light level where no lamp reaches.</summary>
    public LightLevel Ambient { get; init; } = LightLevel.Dark;
    public double BrightFraction { get; init; } = 0.5;
    /// <summary>Radius in cells of the light each hero carries; 0 = none.</summary>
    public double Carried { get; init; } = 3.5;
    /// <summary>How far heroes see, in cells.</summary>
    public double Sight { get; init; } = 8.5;
    public MapTime Time { get; init; } = MapTime.Night;
    public double DaySight { get; init; } = 40;
    public double DuskSight { get; init; } = 18;
    public ContentColor DaySky { get; init; } = new(255, 250, 238);
    public ContentColor DuskSky { get; init; } = new(176, 136, 128);
}

/// <summary>
/// map.json: tiles, lights, markers and objects. Maps are written by hand as text rows with a
/// legend, or the way Create saves them, with a tiles array and a tileMap. Both read into the same
/// layers.
/// </summary>
public class GameMap
{
    /// <summary>World units in one cell.</summary>
    public const int CellSize = 64;
    private const int ChunkSize = 32;

    public string Name { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public List<TileType> Types { get; init; } = new();
    public List<MapLayer> Layers { get; init; } = new();
    public MapLighting Lighting { get; init; } = new();
    public ContentColor Ambient { get; init; } = new(46, 54, 86);
    public List<MapLight> Lights { get; init; } = new();
    public SortedDictionary<string, Cell> Markers { get; init; } = new(StringComparer.Ordinal);
    public List<MapObject> Objects { get; init; } = new();
    /// <summary>Squares within which a hero can spot a trap.</summary>
    public double TrapSpotRange { get; init; } = 2;

    public bool Inside(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < Width && cell.Y < Height;

    public Cell? Marker(string name) => Markers.TryGetValue(name, out Cell cell) ? cell : null;

    /// <summary>The tile type on a layer at a cell, or null where the layer is empty.</summary>
    public TileType? TileAt(int layer, Cell cell)
    {
        int id = Inside(cell) ? Layers[layer].Tiles[cell.Y * Width + cell.X] : 0;
        return id == 0 ? null : Types[id - 1];
    }

    /// <summary>
    /// A cell someone can stand on: ground under it, nothing unwalkable painted over it, and no
    /// object in the way. Only floor 0 counts for now.
    /// </summary>
    public bool Walkable(Cell cell)
    {
        if (!Inside(cell))
        {
            return false;
        }
        bool first = true;
        for (int layer = 0; layer < Layers.Count; layer++)
        {
            if (Layers[layer].Floor != 0)
            {
                continue;
            }
            TileType? type = TileAt(layer, cell);
            // Empty ground is a hole, not a floor.
            if ((first && type == null) || (type != null && !type.Walkable))
            {
                return false;
            }
            first = false;
        }
        if (first)
        {
            return false;
        }
        // A little inside the cell, so an object on the next cell over doesn't count.
        double left = cell.X * CellSize + 1, top = cell.Y * CellSize + 1, size = CellSize - 2;
        foreach (MapObject o in Objects)
        {
            if (o.Floor == 0 && o.BlocksMovement && o.X < left + size && o.X + o.Width > left && o.Y < top + size && o.Y + o.Height > top)
            {
                return false;
            }
        }
        return true;
    }

    public bool BlocksSight(Cell cell)
    {
        return Enumerable.Range(0, Layers.Count).Any(layer => Layers[layer].Floor == 0 && TileAt(layer, cell)?.BlocksSight == true);
    }

    public bool Indoors(Cell cell)
    {
        return Enumerable.Range(0, Layers.Count).Any(layer => Layers[layer].Floor == 0 && TileAt(layer, cell)?.Indoors == true);
    }

    public static GameMap Read(ContentNode node, IReadOnlyDictionary<string, Kit> kits)
    {
        bool native = node.Has("tileMap");
        ContentNode? tiles = node.Get("tiles");
        bool text = node.Get("legend")?.IsObject == true && node.Get("layers")?.IsArray == true;
        if (!node.IsObject || tiles == null || (!tiles.Value.IsObject && !tiles.Value.IsArray) || (!native && !text))
        {
            throw node.Fail("maps need tiles and either a tileMap or legend and layers");
        }

        var types = new List<TileType>();
        var ids = new Dictionary<string, int>();
        void AddType(string name, ContentNode t)
        {
            if (name.Length == 0 || ids.ContainsKey(name))
            {
                throw t.Fail("tile names must be unique and not empty");
            }
            types.Add(new TileType
            {
                Name = name,
                Art = t.Text("art", name),
                Color = t.Get("color") is ContentNode color ? ContentParts.ColorFrom(color) : new ContentColor(128, 128, 128),
                Walkable = t.Bool("walkable", true),
                BlocksSight = t.Bool("blocksSight", false),
                Indoors = t.Bool("indoors", false),
            });
            ids[name] = types.Count;
        }
        if (tiles.Value.IsArray)
        {
            foreach (ContentNode t in tiles.Value.Items())
            {
                AddType(t.At("name").AsText(), t);
            }
        }
        else
        {
            foreach (KeyValuePair<string, ContentNode> member in tiles.Value.Members())
            {
                AddType(member.Key, member.Value);
            }
        }
        if (types.Count == 0 || types.Count > 4096)
        {
            throw tiles.Value.Fail("a map needs 1 to 4096 tile types");
        }

        int width, height;
        List<MapLayer> layers = native
            ? ReadTileMap(node.At("tileMap"), types.Count, out width, out height)
            : ReadRows(node, ids, out width, out height);
        if (layers.Count == 0)
        {
            throw node.Fail("a map needs at least one layer");
        }

        var lights = new List<MapLight>();
        foreach (ContentNode l in node.Get("lights")?.Items() ?? Array.Empty<ContentNode>())
        {
            ContentNode at = l.At("at");
            double radius = l.Number("radius", 5, 0, 1000);
            if (!at.IsArray || at.Count != 2 || radius <= 0)
            {
                throw l.Fail("lights need \"at\": [x, y] and a radius above 0, in cells");
            }
            double[] p = at.Items().Select(part => part.AsNumber()).ToArray();
            if (p[0] < 0 || p[1] < 0 || p[0] >= width || p[1] >= height)
            {
                throw at.Fail("is outside the map");
            }
            lights.Add(new MapLight(p[0], p[1], radius,
                l.Get("color") is ContentNode color ? ContentParts.ColorFrom(color) : new ContentColor(255, 200, 120),
                l.Bool("flame", true), l.Text("name", "")));
        }

        var map = new GameMap
        {
            Name = node.Text("name", ""),
            Width = width,
            Height = height,
            Types = types,
            Layers = layers,
            Lighting = LightingFrom(node.Get("lighting")),
            Ambient = node.Get("ambient") is ContentNode ambient ? ContentParts.ColorFrom(ambient) : new ContentColor(46, 54, 86),
            Lights = lights,
            TrapSpotRange = node.Number("trapSpotRange", 2, 0, 100),
        };

        if (node.Get("markers") is ContentNode markers)
        {
            foreach (KeyValuePair<string, ContentNode> member in markers.RequireObject("is an object of name to cell").Members())
            {
                Cell cell = ContentParts.CellFrom(member.Value);
                if (!map.Inside(cell))
                {
                    throw member.Value.Fail("is outside the map");
                }
                map.Markers[member.Key] = cell;
            }
        }

        // Objects: a kit on a cell ({"kit": "door", "at": [x, y]}; any other field changes that
        // copy and "tags" add to the kit's), or a whole object with "at" or a world "area".
        if (node.Get("objects") is ContentNode objects)
        {
            foreach (ContentNode entry in objects.Items())
            {
                map.Objects.Add(PlaceObject(entry, kits, width, height));
            }
        }
        return map;
    }

    private static MapObject PlaceObject(ContentNode entry, IReadOnlyDictionary<string, Kit> kits, int width, int height)
    {
        entry.RequireObject("each object is an object");
        JsonObject made;
        if (entry.Get("kit") is ContentNode kitName)
        {
            if (!kits.TryGetValue(kitName.AsText(), out Kit? kit))
            {
                throw kitName.Fail($"unknown kit \"{kitName.AsText()}\"");
            }
            // The kit file parsed already, so its object is an object.
            made = JsonNode.Parse(kit.Source.Raw())!.AsObject();
            made.Remove("id");
        }
        else
        {
            // One cell unless it says otherwise.
            made = new JsonObject { ["area"] = new JsonArray(0, 0, CellSize, CellSize) };
        }
        JsonObject changes = JsonNode.Parse(entry.Raw())!.AsObject();
        changes.Remove("kit");
        changes.Remove("at");
        if (changes["tags"] is JsonArray added && made["tags"] is JsonArray own)
        {
            foreach (JsonNode? tag in own)
            {
                added.Add(tag?.DeepClone());
            }
        }
        MergePatch(made, changes);

        ContentNode merged = ContentNode.Parse(entry.File, made.ToJsonString()).Moved(entry.Path);
        MapObject placed = MapObject.Read(merged);
        double x = placed.X, y = placed.Y;
        if (entry.Get("at") is ContentNode at)
        {
            Cell cell = ContentParts.CellFrom(at);
            x = cell.X * CellSize;
            y = cell.Y * CellSize;
        }
        if (x < 0 || y < 0 || x + placed.Width > width * CellSize || y + placed.Height > height * CellSize)
        {
            throw entry.Fail("is outside the map");
        }
        return new MapObject
        {
            Name = placed.Name, Texture = placed.Texture, X = x, Y = y, Width = placed.Width, Height = placed.Height,
            Floor = placed.Floor, Tags = placed.Tags, Weight = placed.Weight, Destroyed = placed.Destroyed, Contents = placed.Contents,
            Door = placed.Door, Durability = placed.Durability, Lock = placed.Lock, Trap = placed.Trap, Light = placed.Light,
        };
    }

    // JSON merge patch: objects merge field by field, null removes, anything else replaces.
    private static void MergePatch(JsonObject target, JsonObject patch)
    {
        foreach (KeyValuePair<string, JsonNode?> change in patch.ToList())
        {
            if (change.Value == null)
            {
                target.Remove(change.Key);
            }
            else if (change.Value is JsonObject inner && target[change.Key] is JsonObject existing)
            {
                MergePatch(existing, inner);
            }
            else
            {
                target[change.Key] = change.Value.DeepClone();
            }
        }
    }

    // The text format: one character per cell, looked up in the legend. Spaces are empty.
    private static List<MapLayer> ReadRows(ContentNode node, Dictionary<string, int> ids, out int width, out int height)
    {
        var legend = new Dictionary<char, int>();
        foreach (KeyValuePair<string, ContentNode> member in node.At("legend").Members())
        {
            if (member.Key.Length != 1 || member.Key == " ")
            {
                throw member.Value.Fail("legend keys are single characters (space means empty)");
            }
            if (!ids.TryGetValue(member.Value.AsText(), out int id))
            {
                throw member.Value.Fail($"unknown tile \"{member.Value.AsText()}\"");
            }
            legend[member.Key[0]] = id;
        }

        var layers = new List<MapLayer>();
        width = 0;
        height = 0;
        foreach (ContentNode layer in node.At("layers").Items())
        {
            ContentNode[] rows = layer.At("rows").Items().ToArray();
            if (layers.Count == 0)
            {
                height = rows.Length;
                width = rows.Length == 0 ? 0 : rows[0].AsText().Length;
                if (width < 1 || height < 1 || width > 4096 || height > 4096)
                {
                    throw layer.Fail("rows", "maps are 1 to 4096 cells a side");
                }
            }
            if (rows.Length != height)
            {
                throw layer.Fail("rows", "every layer needs the same number of rows");
            }
            int[] cells = new int[width * height];
            for (int y = 0; y < height; y++)
            {
                string row = rows[y].AsText();
                if (row.Length != width)
                {
                    throw rows[y].Fail($"isn't {width} wide");
                }
                for (int x = 0; x < width; x++)
                {
                    if (row[x] == ' ')
                    {
                        continue;
                    }
                    if (!legend.TryGetValue(row[x], out int id))
                    {
                        throw rows[y].Fail($"'{row[x]}' isn't in the legend");
                    }
                    cells[y * width + x] = id;
                }
            }
            layers.Add(new MapLayer { Name = layer.Text("name", $"layer {layers.Count + 1}"), Tiles = cells });
        }
        return layers;
    }

    // The form Create saves: sparse chunks of 32 by 32 cells, each tile as [place in chunk, tile id].
    private static List<MapLayer> ReadTileMap(ContentNode node, int typeCount, out int width, out int height)
    {
        node.RequireObject("is an object with width, height, tileSize and layers");
        width = node.At("width").AsInt(1, 4096);
        height = node.At("height").AsInt(1, 4096);
        if (node.At("tileSize").AsNumber() != CellSize)
        {
            throw node.Fail("tileSize", "tiles are 64 units");
        }
        if (node.Has("delta"))
        {
            throw node.Fail("delta", "a map with saved changes can't be read as an authored map");
        }
        var layers = new List<MapLayer>();
        foreach (ContentNode layer in node.At("layers").Items())
        {
            int[] cells = new int[width * height];
            Array.Fill(cells, layer.Int("default", 0, 0, typeCount));
            foreach (ContentNode chunk in layer.At("chunks").Items())
            {
                int cx = chunk.At("x").AsInt(0, (width - 1) / ChunkSize);
                int cy = chunk.At("y").AsInt(0, (height - 1) / ChunkSize);
                foreach (ContentNode tile in chunk.At("tiles").Items())
                {
                    if (!tile.IsArray || tile.Count != 2)
                    {
                        throw tile.Fail("is [place, tile id]");
                    }
                    ContentNode[] pair = tile.Items().ToArray();
                    int place = pair[0].AsInt(0, ChunkSize * ChunkSize - 1);
                    int x = cx * ChunkSize + place % ChunkSize, y = cy * ChunkSize + place / ChunkSize;
                    if (x >= width || y >= height)
                    {
                        throw tile.Fail("is outside the map");
                    }
                    if (pair[1].AsInt(0) > typeCount)
                    {
                        throw tile.Fail("uses a tile id with no tile type");
                    }
                    cells[y * width + x] = pair[1].AsInt();
                }
            }
            string image = "";
            double[] imageArea = Array.Empty<double>();
            if (layer.Get("image") is ContentNode picture)
            {
                image = picture.At("path").AsText();
                imageArea = picture.At("area").Items().Select(part => part.AsNumber()).ToArray();
                if (imageArea.Length != 4)
                {
                    throw picture.Fail("area", "needs four numbers");
                }
            }
            layers.Add(new MapLayer
            {
                Name = layer.At("name").AsText(),
                Floor = layer.Int("floor", 0),
                Visible = layer.Bool("visible", true),
                Tiles = cells,
                Image = image,
                ImageArea = imageArea,
            });
        }
        return layers;
    }

    private static MapLighting LightingFrom(ContentNode? found)
    {
        if (found is not ContentNode l)
        {
            return new MapLighting();
        }
        l.RequireObject("is an object");
        var defaults = new MapLighting();
        double sight = l.Number("sight", defaults.Sight, 0, 200);
        double daySight = l.Number("daySight", defaults.DaySight, 0, 200);
        double duskSight = l.Number("duskSight", defaults.DuskSight, 0, 200);
        if (sight <= 0 || daySight <= 0 || duskSight <= 0)
        {
            throw l.Fail("sight, daySight and duskSight are above 0");
        }
        return new MapLighting
        {
            Mode = l.Text("mode", "mood") switch
            {
                "off" => LightingMode.Off,
                "mood" => LightingMode.Mood,
                "rules" => LightingMode.Rules,
                _ => throw l.Fail("mode", "is \"off\", \"mood\" or \"rules\""),
            },
            Ambient = l.Text("ambient", "dark") switch
            {
                "dark" => LightLevel.Dark,
                "dim" => LightLevel.Dim,
                "bright" => LightLevel.Bright,
                _ => throw l.Fail("ambient", "is \"dark\", \"dim\" or \"bright\""),
            },
            BrightFraction = l.Number("brightFraction", defaults.BrightFraction, 0, 1),
            Carried = l.Number("carried", defaults.Carried, 0, 100),
            Sight = sight,
            Time = l.Text("time", "night") switch
            {
                "day" => MapTime.Day,
                "dusk" => MapTime.Dusk,
                "night" => MapTime.Night,
                "underground" => MapTime.Underground,
                _ => throw l.Fail("time", "is \"day\", \"dusk\", \"night\" or \"underground\""),
            },
            DaySight = daySight,
            DuskSight = duskSight,
            DaySky = l.Get("daySky") is ContentNode day ? ContentParts.ColorFrom(day) : defaults.DaySky,
            DuskSky = l.Get("duskSky") is ContentNode dusk ? ContentParts.ColorFrom(dusk) : defaults.DuskSky,
        };
    }
}
