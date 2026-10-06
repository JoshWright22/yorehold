namespace Yorehold.Rules;

/// <summary>A map object prototype: kits/door.json. The file name is its id.</summary>
public class Kit
{
    public string Name { get; init; } = "";
    public MapObject Prototype { get; init; } = new();
    /// <summary>The "object" as written, so a map can change fields of its own copy.</summary>
    public ContentNode Source { get; init; }

    public static Kit Read(ContentNode node)
    {
        node.RequireObject("a kit is a JSON object");
        ContentNode source = node.At("object");
        return new Kit { Name = node.At("name").AsText(), Prototype = MapObject.Read(source), Source = source };
    }
}
