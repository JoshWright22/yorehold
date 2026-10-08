namespace Yorehold.Rules;

/// <summary>A kind of pick a system has beside race, background and class: PF2e's heritage, an archetype.</summary>
public sealed record OptionKind(string Id, string Name)
{
    public static OptionKind Read(ContentNode node)
    {
        node.RequireObject("an option kind is an object with an id and a name");
        node.Only("id", "name");
        string id = node.At("id").AsId();
        return new OptionKind(id, node.Text("name", id, 64));
    }
}

/// <summary>
/// A ruleset folder's options/&lt;id&gt;.json: one choice of one of the system's option kinds
/// ("kind": "heritage"), open to the races and classes it lists (any when it lists none), granting
/// what a class feature or a feat grants, and feats.
/// </summary>
public sealed class OptionDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string Kind { get; init; } = "";
    public List<string> Races { get; init; } = new();
    public List<string> Classes { get; init; } = new();
    public List<string> Feats { get; init; } = new();
    public Grants Gives { get; init; } = new();

    /// <summary>Whether a character of this race and first class may take it.</summary>
    public bool OpenTo(string race, string classId) =>
        (Races.Count == 0 || Races.Contains(race)) && (Classes.Count == 0 || Classes.Contains(classId));

    public static OptionDefinition Read(ContentNode node)
    {
        node.RequireObject("an option is a JSON object");
        node.Only("id", "name", "description", "kind", "races", "classes", "feats", "modifiers", "proficiencies", "ranks", "resources", "actions");
        string id = node.At("id").AsId();
        return new OptionDefinition
        {
            Id = id,
            Name = node.Text("name", id, 64),
            Description = node.Text("description", "", 4000),
            Kind = node.At("kind").AsId(),
            Races = node.Ids("races"),
            Classes = node.Ids("classes"),
            Feats = node.Ids("feats"),
            Gives = Grants.Read(node),
        };
    }
}
