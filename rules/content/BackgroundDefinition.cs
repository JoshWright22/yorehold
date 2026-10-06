namespace Yorehold.Rules;

/// <summary>One background file: rulesets/yorehold/backgrounds/sage.json. Unknown fields are refused.</summary>
public class BackgroundDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public Dictionary<string, int> Abilities { get; init; } = new();
    public List<string> Proficiencies { get; init; } = new();
    public List<string> Feats { get; init; } = new();
    /// <summary>Gear, given after the class's.</summary>
    public List<string> Items { get; init; } = new();

    public static BackgroundDefinition Read(ContentNode node)
    {
        node.RequireObject("a background is a JSON object");
        node.Only("id", "name", "description", "abilities", "proficiencies", "feats", "items");
        string id = node.At("id").AsId();
        return new BackgroundDefinition
        {
            Id = id,
            Name = node.Text("name", id, 64),
            Description = node.Text("description", "", 4000),
            Abilities = ContentParts.NumbersFrom(node, "abilities", -10, 10, idKeys: true),
            Proficiencies = node.Ids("proficiencies").Distinct().ToList(),
            Feats = node.Ids("feats"),
            Items = node.Ids("items"),
        };
    }
}
