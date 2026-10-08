namespace Yorehold.Rules;

public enum ConditionStacking
{
    Refresh,
    Longest,
    Value,
}

/// <summary>One condition file: rulesets/yorehold/conditions/prone.json.</summary>
public class ConditionDefinition
{
    /// <summary>What can end a condition, and what an effect step can wait for.</summary>
    public static readonly string[] Events =
    {
        "turnStart", "turnEnd", "attack", "damage", "healed", "move", "rest", "fightStart", "fightEnd",
    };

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public List<Modifier> Modifiers { get; init; } = new();
    public bool AdvantageOnAttacks { get; init; }
    public bool DisadvantageOnAttacks { get; init; }
    /// <summary>Attacks against whoever has it are made with advantage or disadvantage (prone, dodging, restrained).</summary>
    public bool AttackersAdvantage { get; init; }
    public bool AttackersDisadvantage { get; init; }
    /// <summary>
    /// Squares the attackers' advantage or disadvantage reaches; 0 = any distance. Further off,
    /// AttackersBeyond applies instead ("advantage", "disadvantage" or nothing): 5e's prone gives
    /// attackers within 5 feet advantage and those further disadvantage.
    /// </summary>
    public double AttackersWithin { get; init; }
    public string AttackersBeyond { get; init; } = "";
    /// <summary>
    /// A plain d20 an attacker has to reach before its attack can land (PF2e: 11 against the
    /// hidden, 5 against the concealed); 0 = none. Failing it, the attack misses whatever it rolls.
    /// </summary>
    public int AttackersFlatCheck { get; init; }
    /// <summary>A hit on it from within AttackersWithin (or next to it) is a critical hit (5e's paralysed, unconscious).</summary>
    public bool HitsAreCritical { get; init; }
    /// <summary>Its checks (skills and abilities, not attacks or saves) are rolled with advantage or disadvantage (5e's poisoned, frightened).</summary>
    public bool AdvantageOnChecks { get; init; }
    public bool DisadvantageOnChecks { get; init; }
    public List<string> Flags { get; init; } = new();
    /// <summary>Rounds it lasts when applied without one; -1 until something ends it.</summary>
    public int Duration { get; init; } = -1;
    public ConditionStacking Stacking { get; init; }
    public int MaxValue { get; init; } = 1;
    public bool PerValue { get; init; }
    public int Decay { get; init; }
    public List<string> Ends { get; init; } = new();
    /// <summary>A save at the end of each round that ends it. Empty = no save.</summary>
    public string SaveAbility { get; init; } = "";
    public int SaveDc { get; init; } = 10;
    public List<string> Removes { get; init; } = new();

    public bool HasFlag(string flag) => Flags.Contains(flag);
    public bool EndsOn(string name) => Ends.Contains(name);

    public static ConditionDefinition Read(ContentNode node)
    {
        node.RequireObject("a condition is a JSON object");
        string id = node.At("id").AsName();
        int duration = node.Int("duration", -1);
        if (duration == 0 || duration < -1 || duration > 100000)
        {
            throw node.Fail("duration", "is a number of rounds, or -1 for until something ends it");
        }
        ConditionStacking stacking = node.Text("stacking", "refresh") switch
        {
            "refresh" => ConditionStacking.Refresh,
            "longest" => ConditionStacking.Longest,
            "value" => ConditionStacking.Value,
            _ => throw node.Fail("stacking", "is refresh, longest or value"),
        };
        List<string> ends = DifferentNames(node, "ends");
        foreach (string name in ends)
        {
            if (!Events.Contains(name))
            {
                throw node.Fail("ends", $"unknown event \"{name}\"");
            }
        }
        string saveAbility = "";
        int saveDc = 10;
        if (node.Get("save") is ContentNode save)
        {
            save.RequireObject("needs an ability and a dc");
            saveAbility = save.At("ability").AsName();
            saveDc = save.Int("dc", 10, -1000, 1000);
        }
        return new ConditionDefinition
        {
            Id = id,
            Name = node.Name("name", id),
            Description = node.Text("description", ""),
            Modifiers = ContentParts.ModifiersFrom(node, strict: false),
            AdvantageOnAttacks = node.Bool("advantageOnAttacks", false),
            DisadvantageOnAttacks = node.Bool("disadvantageOnAttacks", false),
            AttackersAdvantage = node.Bool("attackersAdvantage", false),
            AttackersDisadvantage = node.Bool("attackersDisadvantage", false),
            AttackersWithin = node.Number("attackersWithin", 0, 0, 1000),
            AttackersBeyond = node.Text("attackersBeyond", "", 16) is var beyond && beyond is "" or "advantage" or "disadvantage" ? beyond
                : throw node.Fail("attackersBeyond", "is advantage or disadvantage"),
            HitsAreCritical = node.Bool("hitsAreCritical", false),
            AttackersFlatCheck = node.Int("attackersFlatCheck", 0, 0, 20),
            AdvantageOnChecks = node.Bool("advantageOnChecks", false),
            DisadvantageOnChecks = node.Bool("disadvantageOnChecks", false),
            Flags = DifferentNames(node, "flags"),
            Duration = duration,
            Stacking = stacking,
            MaxValue = node.Int("maxValue", 1, 1, 1000),
            PerValue = node.Bool("perValue", false),
            Decay = node.Int("decay", 0, 0, 1000),
            Ends = ends,
            SaveAbility = saveAbility,
            SaveDc = saveDc,
            Removes = DifferentNames(node, "removes"),
        };
    }

    private static List<string> DifferentNames(ContentNode node, string key)
    {
        List<string> list = node.Names(key);
        if (list.Distinct().Count() != list.Count)
        {
            throw node.Fail(key, "is a list of different names, 1 to 64 characters each");
        }
        return list;
    }
}
