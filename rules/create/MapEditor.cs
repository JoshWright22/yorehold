using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>A light as Map mode holds it, in cells like map.json writes them.</summary>
public sealed record EditorLight(double X, double Y, double Radius, ContentColor Color, bool Flame, string Name = "");

/// <summary>
/// Map mode of Create: one chapter's map and the commands that change it. It draws nothing, so
/// tests and any layout use it as it is. Every command goes on the history it was given, which
/// the whole open package shares. Tile strokes record one cell at a time; everything else records
/// what the layers, objects, lights and markers were before and after.
/// It saves the map in the tiles array and tileMap form the game reads, with placed kits written
/// out as whole objects, and keeps the fields it has no tool for (lighting, ambient, trapSpotRange).
/// </summary>
public sealed class MapEditor
{
    public const int MaxSide = 4096;
    public const int LowestFloor = -9;
    public const int HighestFloor = 9;
    private const int ChunkSize = 32;

    private sealed class Layer
    {
        public string Name = "";
        public int Floor;
        public bool Visible = true;
        public int[] Tiles = Array.Empty<int>();
        /// <summary>The layer's "image" as written, or "".</summary>
        public string Image = "";

        public Layer Copy() => new() { Name = Name, Floor = Floor, Visible = Visible, Tiles = (int[])Tiles.Clone(), Image = Image };
    }

    // A placed object: the whole object as JSON, and where it is so a click can find it.
    private sealed record Placed(int Id, string Json, double X, double Y, double Width, double Height, int Floor);

    private sealed class Snapshot
    {
        public List<Layer>? Layers; // null in steps that don't touch tiles
        public int Width, Height;
        public List<Placed> Objects = new();
        public List<EditorLight> Lights = new();
        public SortedDictionary<string, Cell> Markers = new(StringComparer.Ordinal);
    }

    private static readonly string[] Owned = { "name", "tiles", "legend", "layers", "tileMap", "objects", "lights", "markers" };

    private readonly History _history;
    private bool _loaded;
    private string _name = "";
    private string _tiles = "[]";
    private List<TileType> _types = new();
    private JsonObject _rest = new();
    private int _width, _height;
    private List<Layer> _layers = new();
    private List<Placed> _objects = new();
    private int _nextObject = 1;
    private List<EditorLight> _lights = new();
    private SortedDictionary<string, Cell> _markers = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, Kit> _kits = new Dictionary<string, Kit>();
    private GameMap? _map;

    public MapEditor(History history)
    {
        _history = history;
    }

    public bool HasMap => _loaded;
    public int Width => _width;
    public int Height => _height;
    public int LayerCount => _layers.Count;
    public IReadOnlyList<TileType> Types => _types;
    public IReadOnlyList<EditorLight> Lights => _lights;
    public IReadOnlyDictionary<string, Cell> Markers => _markers;
    /// <summary>What the map's objects may name, and what can be placed.</summary>
    public IReadOnlyDictionary<string, Kit> Kits => _kits;

    /// <summary>An empty map of one ground layer, with a tile type for each tile the game can paint by itself.</summary>
    public static string BlankMap(string name, int width, int height)
    {
        width = Math.Clamp(width, 1, MaxSide);
        height = Math.Clamp(height, 1, MaxSide);
        var rows = new JsonArray();
        for (int y = 0; y < height; y++)
        {
            rows.Add(new string('.', width));
        }
        var j = new JsonObject
        {
            ["name"] = name,
            ["tiles"] = new JsonArray(
                new JsonObject { ["name"] = "grass", ["art"] = "grass" },
                new JsonObject { ["name"] = "dirt", ["art"] = "dirt" },
                new JsonObject { ["name"] = "stone", ["art"] = "stone", ["indoors"] = true },
                new JsonObject { ["name"] = "wood", ["art"] = "wood", ["indoors"] = true },
                new JsonObject { ["name"] = "wall", ["art"] = "wall", ["walkable"] = false, ["blocksSight"] = true },
                new JsonObject { ["name"] = "water", ["art"] = "water", ["walkable"] = false },
                new JsonObject { ["name"] = "tree", ["art"] = "tree", ["walkable"] = false, ["blocksSight"] = true }),
            ["legend"] = new JsonObject { ["."] = "grass" },
            ["layers"] = new JsonArray(new JsonObject { ["name"] = "ground", ["rows"] = rows }),
            ["lights"] = new JsonArray(),
            ["markers"] = new JsonObject(),
        };
        return CreateJson.Write(j);
    }

    /// <summary>Reads a map.json in either form. False with the reason when the game wouldn't load it either.</summary>
    public bool Load(string text, out string error, IReadOnlyDictionary<string, Kit>? kits = null)
    {
        error = "";
        kits ??= new Dictionary<string, Kit>();
        try
        {
            ContentNode node = ContentNode.Parse("map.json", text);
            GameMap map = GameMap.Read(node, kits);

            // tile types as an array, in the order their ids count
            var tiles = new JsonArray();
            ContentNode types = node.At("tiles");
            if (types.IsArray)
            {
                foreach (ContentNode type in types.Items())
                {
                    tiles.Add(JsonNode.Parse(type.Raw()));
                }
            }
            else
            {
                foreach (KeyValuePair<string, ContentNode> member in types.Members())
                {
                    // GameMap read it, so each tile type is an object
                    JsonObject type = JsonNode.Parse(member.Value.Raw())!.AsObject();
                    var named = new JsonObject { ["name"] = member.Key };
                    foreach (KeyValuePair<string, JsonNode?> field in type)
                    {
                        if (field.Key != "name")
                        {
                            named[field.Key] = field.Value?.DeepClone();
                        }
                    }
                    tiles.Add(named);
                }
            }

            var layers = new List<Layer>();
            foreach (MapLayer layer in map.Layers)
            {
                string image = layer.Image.Length == 0
                    ? ""
                    : new JsonObject { ["path"] = layer.Image, ["area"] = new JsonArray(layer.ImageArea.Select(a => (JsonNode)JsonValue.Create(a)).ToArray()) }.ToJsonString();
                layers.Add(new Layer { Name = layer.Name, Floor = layer.Floor, Visible = layer.Visible, Tiles = (int[])layer.Tiles.Clone(), Image = image });
            }

            var objects = new List<Placed>();
            int next = 1;
            foreach (ContentNode entry in node.Get("objects")?.Items() ?? Array.Empty<ContentNode>())
            {
                objects.Add(PlacedFrom(next++, GameMap.WholeObject(entry, kits, map.Width, map.Height)));
            }

            var rest = new JsonObject();
            foreach (KeyValuePair<string, ContentNode> member in node.Members())
            {
                if (Array.IndexOf(Owned, member.Key) < 0)
                {
                    rest[member.Key] = JsonNode.Parse(member.Value.Raw());
                }
            }

            _name = map.Name;
            _tiles = tiles.ToJsonString();
            _types = map.Types;
            _rest = rest;
            _width = map.Width;
            _height = map.Height;
            _layers = layers;
            _objects = objects;
            _nextObject = next;
            _lights = map.Lights.Select(l => new EditorLight(l.X, l.Y, l.Radius, l.Color, l.Flame, l.Name)).ToList();
            _markers = new SortedDictionary<string, Cell>(map.Markers, StringComparer.Ordinal);
            _kits = kits;
            _map = null;
            _loaded = true;
            return true;
        }
        catch (ContentException problem)
        {
            error = problem.Message;
            return false;
        }
    }

    /// <summary>The map.json to save.</summary>
    public string ToJson()
    {
        if (!_loaded)
        {
            return "{}";
        }
        var j = new JsonObject { ["name"] = _name };
        foreach (KeyValuePair<string, JsonNode?> field in _rest)
        {
            j[field.Key] = field.Value?.DeepClone();
        }
        j["tiles"] = JsonNode.Parse(_tiles);

        var layers = new JsonArray();
        foreach (Layer layer in _layers)
        {
            var chunks = new JsonArray();
            for (int cy = 0; cy * ChunkSize < _height; cy++)
            {
                for (int cx = 0; cx * ChunkSize < _width; cx++)
                {
                    var tiles = new JsonArray();
                    for (int place = 0; place < ChunkSize * ChunkSize; place++)
                    {
                        int x = cx * ChunkSize + place % ChunkSize, y = cy * ChunkSize + place / ChunkSize;
                        if (x < _width && y < _height && layer.Tiles[y * _width + x] != 0)
                        {
                            tiles.Add(new JsonArray(place, layer.Tiles[y * _width + x]));
                        }
                    }
                    if (tiles.Count > 0)
                    {
                        chunks.Add(new JsonObject { ["x"] = cx, ["y"] = cy, ["tiles"] = tiles });
                    }
                }
            }
            var entry = new JsonObject { ["name"] = layer.Name, ["floor"] = layer.Floor };
            if (!layer.Visible)
            {
                entry["visible"] = false;
            }
            entry["chunks"] = chunks;
            if (layer.Image.Length > 0)
            {
                entry["image"] = JsonNode.Parse(layer.Image);
            }
            layers.Add(entry);
        }
        j["tileMap"] = new JsonObject { ["width"] = _width, ["height"] = _height, ["tileSize"] = GameMap.CellSize, ["layers"] = layers };

        if (_objects.Count > 0)
        {
            j["objects"] = new JsonArray(_objects.Select(o => JsonNode.Parse(o.Json)).ToArray());
        }
        var lights = new JsonArray();
        foreach (EditorLight light in _lights)
        {
            var color = new JsonArray((int)light.Color.R, (int)light.Color.G, (int)light.Color.B);
            if (light.Color.A != 255)
            {
                color.Add((int)light.Color.A);
            }
            var entry = new JsonObject { ["at"] = new JsonArray(light.X, light.Y), ["radius"] = light.Radius, ["flame"] = light.Flame, ["color"] = color };
            if (light.Name.Length > 0)
            {
                entry["name"] = light.Name;
            }
            lights.Add(entry);
        }
        j["lights"] = lights;
        var markers = new JsonObject();
        foreach (KeyValuePair<string, Cell> marker in _markers)
        {
            markers[marker.Key] = new JsonArray(marker.Value.X, marker.Value.Y);
        }
        j["markers"] = markers;
        return CreateJson.Write(j);
    }

    /// <summary>
    /// The map as the game would load it right now, for where someone can stand, what blocks
    /// sight and for drawing. Built again only after something changed.
    /// </summary>
    public GameMap Map()
    {
        // the editor only ever holds what GameMap read or its own checked commands made, so this reads
        return _map ??= GameMap.Read(ContentNode.Parse("map.json", ToJson()), new Dictionary<string, Kit>());
    }

    public string LayerName(int layer) => _layers[layer].Name;

    public int LayerFloor(int layer) => _layers[layer].Floor;

    /// <summary>The tile id on a layer at a cell; 0 for empty and for anything off the map.</summary>
    public int Tile(int layer, int x, int y)
    {
        return layer >= 0 && layer < _layers.Count && x >= 0 && y >= 0 && x < _width && y < _height ? _layers[layer].Tiles[y * _width + x] : 0;
    }

    public bool Inside(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < _width && cell.Y < _height;

    public List<int> LayersOn(int floor)
    {
        return Enumerable.Range(0, _layers.Count).Where(i => _layers[i].Floor == floor).ToList();
    }

    /// <summary>The lowest and highest floor any layer is on.</summary>
    public (int Low, int High) Floors()
    {
        return _layers.Count == 0 ? (0, 0) : (Math.Min(0, _layers.Min(l => l.Floor)), Math.Max(0, _layers.Max(l => l.Floor)));
    }

    // ---------------------------------------------------------------- tiles

    /// <summary>One cell. Id 0 erases. Cells painted one after another are one undo step until EndStroke.</summary>
    public bool Paint(int layer, Cell at, int id)
    {
        if (!_loaded || layer < 0 || layer >= _layers.Count || !Inside(at) || id < 0 || id > _types.Count)
        {
            return false;
        }
        int old = _layers[layer].Tiles[at.Y * _width + at.X];
        if (old == id)
        {
            return false;
        }
        // one cell at a time, so a long stroke on a big map doesn't copy the map for every cell
        Action Set(int tile) => () =>
        {
            _layers[layer].Tiles[at.Y * _width + at.X] = tile;
            _map = null;
        };
        _history.Perform(id != 0 ? "Paint tiles" : "Erase tiles", Set(id), Set(old), "paint");
        return true;
    }

    /// <summary>A box from any corner to the opposite one; the part off the map is left out.</summary>
    public bool Fill(int layer, Cell from, Cell to, int id)
    {
        if (!_loaded || layer < 0 || layer >= _layers.Count || id < 0 || id > _types.Count)
        {
            return false;
        }
        int x0 = Math.Max(0, Math.Min(from.X, to.X)), x1 = Math.Min(_width - 1, Math.Max(from.X, to.X));
        int y0 = Math.Max(0, Math.Min(from.Y, to.Y)), y1 = Math.Min(_height - 1, Math.Max(from.Y, to.Y));
        if (x0 > x1 || y0 > y1)
        {
            return false;
        }
        int width = _width;
        var old = new List<int>();
        bool changes = false;
        for (int y = y0; y <= y1; y++)
        {
            for (int x = x0; x <= x1; x++)
            {
                old.Add(_layers[layer].Tiles[y * width + x]);
                changes |= old[^1] != id;
            }
        }
        if (!changes)
        {
            return false;
        }
        _history.Perform(id != 0 ? "Fill tiles" : "Erase tiles",
            () =>
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        _layers[layer].Tiles[y * width + x] = id;
                    }
                }
                _map = null;
            },
            () =>
            {
                int i = 0;
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        _layers[layer].Tiles[y * width + x] = old[i++];
                    }
                }
                _map = null;
            });
        return true;
    }

    public void EndStroke() => _history.BreakMerge();

    // ---------------------------------------------------------------- layers and size

    /// <summary>A new empty layer; -1 for a floor out of range. A map keeps at least one layer.</summary>
    public int AddLayer(string name, int floor)
    {
        if (!_loaded || floor < LowestFloor || floor > HighestFloor)
        {
            return -1;
        }
        if (name.Length == 0)
        {
            name = $"layer {_layers.Count + 1}";
        }
        Edit("Add layer", true, () => _layers.Add(new Layer { Name = name, Floor = floor, Tiles = new int[_width * _height] }));
        return _layers.Count - 1;
    }

    public bool RemoveLayer(int layer)
    {
        if (!_loaded || layer < 0 || layer >= _layers.Count || _layers.Count <= 1)
        {
            return false;
        }
        Edit("Remove layer", true, () => _layers.RemoveAt(layer));
        return true;
    }

    /// <summary>The "walls" layer of a floor, added the first time it is asked for.</summary>
    public int WallLayer(int floor)
    {
        foreach (int layer in LayersOn(floor))
        {
            if (_layers[layer].Name == "walls")
            {
                return layer;
            }
        }
        return AddLayer("walls", floor);
    }

    /// <summary>The first tile type that blocks sight, or 0.</summary>
    public int WallTile()
    {
        int index = _types.FindIndex(t => t.BlocksSight);
        return index + 1;
    }

    /// <summary>Cells are kept from the top-left; lights, markers and objects left outside are dropped.</summary>
    public bool Resize(int width, int height)
    {
        if (!_loaded || width < 1 || height < 1 || width > MaxSide || height > MaxSide || (width == _width && height == _height))
        {
            return false;
        }
        Edit("Resize map", true, () =>
        {
            foreach (Layer layer in _layers)
            {
                int[] tiles = new int[width * height];
                for (int y = 0; y < Math.Min(height, _height); y++)
                {
                    Array.Copy(layer.Tiles, y * _width, tiles, y * width, Math.Min(width, _width));
                }
                layer.Tiles = tiles;
            }
            _width = width;
            _height = height;
            _lights.RemoveAll(l => l.X >= width || l.Y >= height);
            foreach (string name in _markers.Where(m => !Inside(m.Value)).Select(m => m.Key).ToList())
            {
                _markers.Remove(name);
            }
            _objects.RemoveAll(o => o.X + o.Width > width * GameMap.CellSize || o.Y + o.Height > height * GameMap.CellSize);
        });
        return true;
    }

    // ---------------------------------------------------------------- lights, markers, objects

    /// <summary>The new light's place in the list; null when it is off the map or its radius isn't above 0.</summary>
    public int? AddLight(EditorLight light)
    {
        if (!_loaded || !GoodLight(light))
        {
            return null;
        }
        Edit("Place light", false, () => _lights.Add(light));
        return _lights.Count - 1;
    }

    /// <summary>A merge key joins a dragged slider into one undo step.</summary>
    public bool SetLight(int index, EditorLight light, string mergeKey = "")
    {
        if (!_loaded || index < 0 || index >= _lights.Count || _lights[index] == light || !GoodLight(light))
        {
            return false;
        }
        Edit("Change light", false, () => _lights[index] = light, mergeKey);
        return true;
    }

    public bool RemoveLight(int index)
    {
        if (!_loaded || index < 0 || index >= _lights.Count)
        {
            return false;
        }
        Edit("Remove light", false, () => _lights.RemoveAt(index));
        return true;
    }

    /// <summary>Places the marker, or moves it when the map has the name already.</summary>
    public bool SetMarker(string name, Cell at)
    {
        if (!_loaded || name.Length == 0 || name.Length > 60 || !Inside(at))
        {
            return false;
        }
        bool there = _markers.TryGetValue(name, out Cell now);
        if (there && now == at)
        {
            return false;
        }
        Edit(there ? "Move marker" : "Place marker", false, () => _markers[name] = at);
        return true;
    }

    public bool RemoveMarker(string name)
    {
        if (!_loaded || !_markers.ContainsKey(name))
        {
            return false;
        }
        Edit("Remove marker", false, () => _markers.Remove(name));
        return true;
    }

    /// <summary>The object whose area covers the cell's middle on that floor.</summary>
    public int? ObjectAt(Cell at, int floor)
    {
        double x = (at.X + 0.5) * GameMap.CellSize, y = (at.Y + 0.5) * GameMap.CellSize;
        foreach (Placed o in _objects)
        {
            if (o.Floor == floor && x >= o.X && x < o.X + o.Width && y >= o.Y && y < o.Y + o.Height)
            {
                return o.Id;
            }
        }
        return null;
    }

    /// <summary>Puts a copy of a kit on a cell. Null for a kit the package lacks, a taken cell or one off the map.</summary>
    public int? PlaceKit(string kit, Cell at, int floor)
    {
        if (!_loaded || !_kits.TryGetValue(kit, out Kit? found) || !Inside(at) || ObjectAt(at, floor) != null)
        {
            return null;
        }
        double x = at.X * GameMap.CellSize, y = at.Y * GameMap.CellSize;
        if (x + found.Prototype.Width > _width * GameMap.CellSize || y + found.Prototype.Height > _height * GameMap.CellSize)
        {
            return null;
        }
        // the kit file parsed already, so its object is an object
        JsonObject whole = JsonNode.Parse(found.Source.Raw())!.AsObject();
        whole.Remove("id");
        whole["area"] = new JsonArray(x, y, found.Prototype.Width, found.Prototype.Height);
        if (floor != 0)
        {
            whole["floor"] = floor;
        }
        int id = _nextObject;
        Edit("Place " + kit, false, () =>
        {
            _objects.Add(PlacedFrom(id, whole));
            _nextObject = id + 1;
        });
        return id;
    }

    public bool RemoveObject(int id)
    {
        if (!_loaded || !_objects.Exists(o => o.Id == id))
        {
            return false;
        }
        Edit("Remove object", false, () => _objects.RemoveAll(o => o.Id == id));
        return true;
    }

    /// <summary>Things a writer should look at. Nothing here stops the map from saving.</summary>
    public List<string> Problems()
    {
        var found = new List<string>();
        if (!_loaded)
        {
            return found;
        }
        GameMap map = Map();
        foreach (KeyValuePair<string, Cell> marker in _markers)
        {
            if (!map.Walkable(marker.Value))
            {
                found.Add($"marker {marker.Key} is on a cell nobody can stand on");
            }
        }
        for (int i = 0; i < _lights.Count; i++)
        {
            if (map.BlocksSight(new Cell((int)_lights[i].X, (int)_lights[i].Y)))
            {
                found.Add($"light {i + 1} is inside a wall");
            }
        }
        return found;
    }

    private bool GoodLight(EditorLight light)
    {
        return light.X >= 0 && light.Y >= 0 && light.X < _width && light.Y < _height && light.Radius > 0 && light.Radius <= 1000;
    }

    private static Placed PlacedFrom(int id, JsonObject whole)
    {
        MapObject read = MapObject.Read(ContentNode.Parse("map.json", whole.ToJsonString()));
        return new Placed(id, whole.ToJsonString(), read.X, read.Y, read.Width, read.Height, read.Floor);
    }

    private Snapshot Take(bool tiles)
    {
        return new Snapshot
        {
            Layers = tiles ? _layers.Select(l => l.Copy()).ToList() : null,
            Width = _width,
            Height = _height,
            Objects = new List<Placed>(_objects),
            Lights = new List<EditorLight>(_lights),
            Markers = new SortedDictionary<string, Cell>(_markers, StringComparer.Ordinal),
        };
    }

    private void Restore(Snapshot snapshot)
    {
        if (snapshot.Layers != null)
        {
            _layers = snapshot.Layers.Select(l => l.Copy()).ToList();
            _width = snapshot.Width;
            _height = snapshot.Height;
        }
        _objects = new List<Placed>(snapshot.Objects);
        _lights = new List<EditorLight>(snapshot.Lights);
        _markers = new SortedDictionary<string, Cell>(snapshot.Markers, StringComparer.Ordinal);
        _map = null;
    }

    // Runs change and records it with what was there before and after.
    private void Edit(string label, bool tiles, Action change, string mergeKey = "")
    {
        Snapshot before = Take(tiles);
        change();
        _map = null;
        Snapshot after = Take(tiles);
        _history.Record(label, () => Restore(after), () => Restore(before), mergeKey);
    }
}
