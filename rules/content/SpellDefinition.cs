namespace Yorehold.Rules;

/// <summary>An action plus what makes it a spell: rulesets/yorehold/spells/flame-fan.json.</summary>
public class SpellDefinition
{
    private static readonly string[] Own = { "level", "hands", "concentration", "spends" };

    /// <summary>0 is a cantrip and spends nothing; otherwise the level of the slot it needs.</summary>
    public int Level { get; init; }
    public int Hands { get; init; } = 1;
    public bool Concentration { get; init; }
    /// <summary>Resources it costs in place of a slot: focus 1.</summary>
    public Dictionary<string, int> Spends { get; init; } = new();
    public ActionDefinition Action { get; init; } = new();

    public string Id => Action.Id;

    public static SpellDefinition Read(ContentNode node)
    {
        node.RequireObject("a spell is a JSON object");
        int level = node.Int("level", 0, 0, 20);
        int hands = node.Int("hands", 1, 0, 4);

        var spends = new Dictionary<string, int>();
        if (node.Get("spends") is ContentNode spendsNode)
        {
            const string shape = "maps a resource name to how many it costs, 1 to 100";
            if (spendsNode.IsArray)
            {
                // A plain list of names, one each, is the older form and still loads.
                foreach (ContentNode name in spendsNode.Items())
                {
                    if (!name.IsString || name.AsText().Length == 0 || name.AsText().Length > 64)
                    {
                        throw spendsNode.Fail(shape);
                    }
                    spends[name.AsText()] = spends.GetValueOrDefault(name.AsText()) + 1;
                }
            }
            else if (spendsNode.IsObject)
            {
                foreach (KeyValuePair<string, ContentNode> member in spendsNode.Members())
                {
                    if (member.Key.Length == 0 || member.Key.Length > 64 || !member.Value.IsWhole
                        || member.Value.AsInt() < 1 || member.Value.AsInt() > 100)
                    {
                        throw spendsNode.Fail(shape);
                    }
                    spends[member.Key] = member.Value.AsInt();
                }
            }
            else
            {
                throw spendsNode.Fail(shape);
            }
        }

        foreach (string refused in new[] { "general", "endsTurn", "readies", "requires" })
        {
            if (node.Has(refused))
            {
                throw node.Fail(refused, "is not for a spell");
            }
        }
        if (node.Get("cost") is ContentNode cost && !cost.IsWhole)
        {
            throw cost.Fail("is a number of actions; left out, a spell costs its hands");
        }

        // The rest is an action: one action per hand unless it says otherwise, listed after the
        // general actions in order of level, and only for those who know it.
        ActionDefinition action = ActionDefinition.Read(node,
            new ActionDefinition.Defaults(Cost: hands, Order: 500 + level, General: false, AlsoAllowed: Own));
        if (action.Effect.IsEmpty)
        {
            throw node.Fail("effects", "a spell needs at least one step");
        }
        return new SpellDefinition
        {
            Level = level,
            Hands = hands,
            Concentration = node.Bool("concentration", false),
            Spends = spends,
            Action = action,
        };
    }
}
