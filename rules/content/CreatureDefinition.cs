namespace Yorehold.Rules;

/// <summary>How a creature looks on the map until it has art.</summary>
public record CreatureToken(ContentColor Color, double Size = 0.4, string Image = "");

/// <summary>One creature file: creatures/goblin.json.</summary>
public class CreatureDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int Hp { get; init; } = 7;
    public int Level { get; init; } = 1;
    /// <summary>The final AC, whatever the armour and abilities.</summary>
    public int ArmorClass { get; init; } = 12;
    public int Speed { get; init; } = 30;
    public int Darkvision { get; init; }
    public Dictionary<string, int> Abilities { get; init; } = new();
    public List<string> Proficiencies { get; init; } = new();
    public Dictionary<string, string> ProficiencyRanks { get; init; } = new();
    public string DcAbility { get; init; } = "";
    public bool DeathSaves { get; init; }
    public Dictionary<string, Resource> Resources { get; init; } = new();
    public List<string> Items { get; init; } = new();
    public LootTable Loot { get; init; } = new();
    public CreatureToken Token { get; init; } = new(new ContentColor(200, 200, 200));
    /// <summary>A profile name or an object of changes, as written. Null = "cunning".</summary>
    public ContentNode? Ai { get; init; }

    public static CreatureDefinition Read(ContentNode node)
    {
        node.RequireObject("a creature is a JSON object");
        string id = node.At("id").AsId();
        var token = new CreatureToken(new ContentColor(200, 200, 200));
        if (node.Get("token") is ContentNode t)
        {
            t.RequireObject("is an object");
            double size = t.Number("size", 0.4, 0, 10);
            if (size <= 0)
            {
                throw t.Fail("size", "is above 0 and at most 10");
            }
            token = new CreatureToken(t.Get("color") is ContentNode color ? ContentParts.ColorFrom(color) : token.Color, size, t.Text("image", ""));
        }
        ContentNode? ai = node.Get("ai");
        if (ai is ContentNode aiNode && !aiNode.IsString && !aiNode.IsObject)
        {
            throw aiNode.Fail("is a profile name or an object");
        }
        return new CreatureDefinition
        {
            Id = id,
            Name = node.Text("name", id),
            Description = node.Text("description", ""),
            Hp = node.Int("hp", 7, 1, 100000),
            Level = node.Int("level", 1, 1, 1000),
            ArmorClass = node.Int("armorClass", 12, 0, 100),
            Speed = node.Int("speed", 30, 0, 1000),
            Darkvision = node.Int("darkvision", 0, 0, 10000),
            Abilities = ContentParts.NumbersFrom(node, "abilities", int.MinValue, int.MaxValue, idKeys: false),
            Proficiencies = node.Texts("proficiencies").Distinct().ToList(),
            ProficiencyRanks = ContentParts.NamesFrom(node, "proficiencyRanks", ids: false),
            DcAbility = node.Text("dcAbility", "", 64),
            DeathSaves = node.Bool("deathSaves", false),
            Resources = ClassDefinition.ResourcesFrom(node),
            Items = node.Texts("items"),
            Loot = node.Get("loot") is ContentNode loot ? LootTable.Read(loot) : new LootTable(),
            Token = token,
            Ai = ai,
        };
    }
}
