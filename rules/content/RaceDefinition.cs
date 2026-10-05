namespace Yorehold.Rules;

/// <summary>One race file: rulesets/yorehold/races/elf.json. Unknown fields are refused.</summary>
public class RaceDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>Feet; replaces the class's when above 0.</summary>
    public int Speed { get; init; }
    public int Darkvision { get; init; }
    public int BonusHp { get; init; }
    /// <summary>Added to the chosen scores.</summary>
    public Dictionary<string, int> Abilities { get; init; } = new();
    public List<string> Proficiencies { get; init; } = new();
    /// <summary>Given free, without checking their requirements.</summary>
    public List<string> Feats { get; init; } = new();

    public static RaceDefinition Read(ContentNode node)
    {
        node.RequireObject("a race is a JSON object");
        node.Only("id", "name", "description", "speed", "darkvision", "bonusHp", "abilities", "proficiencies", "feats");
        string id = node.At("id").AsId();
        return new RaceDefinition
        {
            Id = id,
            Name = node.Text("name", id, 64),
            Description = node.Text("description", "", 4000),
            Speed = node.Int("speed", 0, 0, 1000),
            Darkvision = node.Int("darkvision", 0, 0, 10000),
            BonusHp = node.Int("bonusHp", 0, 0, 1000),
            Abilities = ContentParts.NumbersFrom(node, "abilities", -10, 10, idKeys: true),
            Proficiencies = node.Ids("proficiencies").Distinct().ToList(),
            Feats = node.Ids("feats"),
        };
    }
}
