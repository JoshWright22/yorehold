namespace Yorehold.Rules;

/// <summary>What a character needs before a feat can be taken.</summary>
public class FeatRequirements
{
    /// <summary>The character level it is taken at.</summary>
    public int Level { get; init; } = 1;
    public List<string> Races { get; init; } = new();
    /// <summary>A level in any of these so far.</summary>
    public List<string> Classes { get; init; } = new();
    /// <summary>Minimum scores.</summary>
    public Dictionary<string, int> Abilities { get; init; } = new();
    /// <summary>Trained or better already.</summary>
    public List<string> Proficiencies { get; init; } = new();
}

/// <summary>One feat file: rulesets/yorehold/feats/tough.json. Unknown fields are refused.</summary>
public class FeatDefinition
{
    /// <summary>The game's own feat kinds; a ruleset's featKinds may name others.</summary>
    public static readonly string[] Kinds = { "class", "skill", "general", "race" };

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Kind { get; init; } = "general";
    public bool Repeatable { get; init; }
    public FeatRequirements Needs { get; init; } = new();
    public Grants Gives { get; init; } = new();

    public static FeatDefinition Read(ContentNode node)
    {
        node.RequireObject("a feat is a JSON object");
        node.Only("id", "name", "description", "kind", "repeatable", "requires", "modifiers", "proficiencies", "ranks", "resources", "actions");
        string id = node.At("id").AsId();
        // one of the ruleset's featKinds; the rules folder checks which once it has the ruleset
        string kind = node.Has("kind") ? node.At("kind").AsId() : "general";
        var needs = new FeatRequirements();
        if (node.Get("requires") is ContentNode r)
        {
            r.RequireObject("is an object");
            r.Only("level", "races", "classes", "abilities", "proficiencies");
            needs = new FeatRequirements
            {
                Level = r.Int("level", 1, 1, 1000),
                Races = r.Ids("races"),
                Classes = r.Ids("classes"),
                Abilities = ContentParts.NumbersFrom(r, "abilities", 1, 30, idKeys: true),
                Proficiencies = r.Ids("proficiencies"),
            };
        }
        return new FeatDefinition
        {
            Id = id,
            Name = node.Text("name", id, 64),
            Description = node.Text("description", "", 4000),
            Kind = kind,
            Repeatable = node.Bool("repeatable", false),
            Needs = needs,
            Gives = Grants.Read(node),
        };
    }
}
