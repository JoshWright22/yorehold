namespace Yorehold.Rules;

/// <summary>
/// triggers/&lt;id&gt;.json: an effect that happens by itself when something happens to whoever
/// was granted it, like Sneak Attack's extra damage on a hit. "on" is "hit", "miss" or "crit"
/// (after one of its attacks, landing on the one attacked), "hitBy" (an attack hit it, landing
/// on the attacker), "kill" (it dropped someone) or "turnStart" (its turn began), the last two
/// landing on itself; "if" a formula that must hold, reading its own sheet names (level, mod.dex,
/// trait.finesse...), flag.&lt;flag&gt; and targetFlag.&lt;flag&gt;, and for attacks advantage
/// and critical; "once" is "turn" for at most once in each of its turns.
/// </summary>
public sealed class TriggerDefinition
{
    public static readonly string[] On = { "hit", "miss", "crit", "hitBy", "kill", "turnStart" };

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string When { get; init; } = "hit";
    public Formula? If { get; init; }
    public bool OncePerTurn { get; init; }
    public Effect Effect { get; init; } = new();

    public static TriggerDefinition Read(ContentNode node)
    {
        node.RequireObject("a trigger is a JSON object");
        node.Only("id", "name", "description", "on", "if", "once", "effects", "save");
        string on = node.Name("on", "");
        if (!On.Contains(on))
        {
            throw node.Fail("on", $"is {string.Join(", ", On)}");
        }
        Formula? condition = null;
        if (node.Get("if") is ContentNode text)
        {
            condition = Formula.Parse(text.AsText(2000), out string error) ?? throw text.Fail(error);
        }
        string once = node.Text("once", "", 16);
        if (once.Length > 0 && once != "turn")
        {
            throw node.Fail("once", "is \"turn\", or left out for every time");
        }
        string id = node.At("id").AsName();
        return new TriggerDefinition
        {
            Id = id,
            Name = node.Text("name", id, 64),
            Description = node.Text("description", ""),
            When = on,
            If = condition,
            OncePerTurn = once == "turn",
            Effect = Effect.Read(node.Get("effects"), node.Get("save")),
        };
    }
}
