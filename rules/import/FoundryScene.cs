using System.Globalization;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// A Foundry VTT scene a creator exported from their own world ("Export Data" on a scene), read
/// onto this game's square grid: the map's size in squares, its walls as wall squares (each wall
/// segment drawn through the squares it crosses), its lights with their reach and colour, where
/// the first token stands as the party's start, and its background as the picture to trace over.
/// Doors, tokens and notes have no place on a map file here; the report says how many were left
/// out and where to put them.
/// </summary>
public sealed class FoundryScene
{
    public int Width { get; private init; }
    public int Height { get; private init; }
    public HashSet<Cell> Walls { get; } = new();
    public List<EditorLight> Lights { get; } = new();
    public Cell? Start { get; private set; }
    public MapEditor.MapTrace? Trace { get; private set; }
    public List<string> Report { get; } = new();

    /// <summary>The scene in json, or null with why when it isn't one (an Item or an Actor goes to the compendium import).</summary>
    public static FoundryScene? Read(string json, out string why)
    {
        why = "";
        JsonObject? scene;
        try
        {
            scene = JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException error)
        {
            why = "not JSON: " + error.Message;
            return null;
        }
        if (scene == null || (scene["walls"] is not JsonArray && scene["grid"] == null))
        {
            why = "not a Foundry scene: it has no grid and no walls (an Item or an Actor goes in through the Compendium)";
            return null;
        }
        // the grid: its size in pixels, the scene distance one square is, and the padding round the map
        double size = scene["grid"] is JsonObject grid ? Number(grid["size"], 100) : Number(scene["grid"], 100);
        double distance = scene["grid"] is JsonObject g2 ? Number(g2["distance"], 5) : Number(scene["gridDistance"], 5);
        size = Math.Max(10, size);
        distance = Math.Max(0.1, distance);
        double width = Number(scene["width"], size * 20);
        double height = Number(scene["height"], size * 20);
        double padding = Math.Clamp(Number(scene["padding"], 0.25), 0, 0.5);
        // Foundry draws the map inside a padded canvas; walls and lights are in canvas pixels
        double dx = Math.Ceiling(width * padding / size) * size;
        double dy = Math.Ceiling(height * padding / size) * size;
        var read = new FoundryScene
        {
            Width = Math.Clamp((int)Math.Ceiling(width / size), 1, MapEditor.MaxSide),
            Height = Math.Clamp((int)Math.Ceiling(height / size), 1, MapEditor.MaxSide),
        };
        (double X, double Y) Square(double x, double y) => ((x - dx) / size, (y - dy) / size);

        int walls = 0, doors = 0;
        foreach (JsonObject wall in (scene["walls"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            if (wall["c"] is not JsonArray c || c.Count != 4)
            {
                continue;
            }
            if (Number(wall["door"], 0) > 0)
            {
                doors++;
                continue;
            }
            walls++;
            (double x0, double y0) = Square(Number(c[0], 0), Number(c[1], 0));
            (double x1, double y1) = Square(Number(c[2], 0), Number(c[3], 0));
            // every quarter square along it marks the square it is in
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0)) * 4));
            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                var at = new Cell((int)Math.Floor(x0 + (x1 - x0) * t), (int)Math.Floor(y0 + (y1 - y0) * t));
                if (at.X >= 0 && at.Y >= 0 && at.X < read.Width && at.Y < read.Height)
                {
                    read.Walls.Add(at);
                }
            }
        }
        foreach (JsonObject light in (scene["lights"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            JsonObject config = light["config"] as JsonObject ?? light;
            double reach = Math.Max(Number(config["dim"], 0), Number(config["bright"], 0)) / distance;
            (double x, double y) = Square(Number(light["x"], 0), Number(light["y"], 0));
            if (reach > 0 && x >= 0 && y >= 0 && x < read.Width && y < read.Height)
            {
                read.Lights.Add(new EditorLight(x, y, Math.Min(reach, 64), Colour(config["color"]), true));
            }
        }
        JsonArray tokens = scene["tokens"] as JsonArray ?? new JsonArray();
        if (tokens.FirstOrDefault() is JsonObject first)
        {
            (double x, double y) = Square(Number(first["x"], 0) + size / 2, Number(first["y"], 0) + size / 2);
            var start = new Cell((int)Math.Floor(x), (int)Math.Floor(y));
            if (start.X >= 0 && start.Y >= 0 && start.X < read.Width && start.Y < read.Height && !read.Walls.Contains(start))
            {
                read.Start = start;
            }
        }
        string background = scene["background"] is JsonObject b ? Text(b["src"]) : Text(scene["img"]);
        if (background.Length > 0)
        {
            string file = background.Replace('\\', '/').Split('/').Last();
            read.Trace = new MapEditor.MapTrace("pictures/" + file, 0, 0, read.Width, read.Height);
            read.Report.Add($"the background picture {background}: copy it into the package's pictures/ to draw over it");
        }
        read.Report.Insert(0, $"{read.Width} x {read.Height} squares; {walls} walls drawn as {read.Walls.Count} wall squares; {read.Lights.Count} lights");
        if (doors > 0)
        {
            read.Report.Add($"{doors} doors left open: place doors in Map mode");
        }
        if (tokens.Count > 0)
        {
            read.Report.Add($"{tokens.Count} tokens: place creatures in Encounters" + (read.Start != null ? "; the first one's square is the party's start" : ""));
        }
        return read;
    }

    private static double Number(JsonNode? node, double fallback) =>
        node is JsonValue value && value.TryGetValue(out double number) && double.IsFinite(number) ? number : fallback;

    private static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : "";

    // "#rrggbb", or a torch's amber when there is none
    private static ContentColor Colour(JsonNode? node)
    {
        string text = Text(node);
        if (text.Length == 7 && text[0] == '#' && int.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
        {
            return new ContentColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
        return new ContentColor(232, 179, 58);
    }
}
