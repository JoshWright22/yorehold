namespace Yorehold.Rules;

/// <summary>
/// ruleset.json's "formulas": how a creature's own numbers are worked out, for a system that
/// counts them differently from the game's own. One left out is counted as the game always has.
/// Each has its own names (below) and may also read "level", "mod.&lt;ability&gt;",
/// "score.&lt;ability&gt;", "stat.&lt;name&gt;" and "prof.&lt;target&gt;" (the proficiency bonus for
/// a skill, save, "weapons", "armor" or "dc") of the creature it is about.
/// </summary>
public sealed class SheetFormulas
{
    /// <summary>The formulas the game asks for, and the names each is handed.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Known = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        // an ability score's modifier; this one is about a number, not a creature
        ["abilityModifier"] = new[] { "score" },
        // what proficiency adds: the rank's bonus and whether it adds the level, or the per-level table
        ["proficiency"] = new[] { "rankBonus", "addsLevel", "proficient", "tableBonus" },
        ["attack"] = new[] { "ability", "proficiency", "bonus" },
        ["damage"] = new[] { "ability", "bonus" },
        ["armorClass"] = new[] { "armor", "ability", "proficiency" },
        ["check"] = new[] { "ability", "proficiency" },
        ["save"] = new[] { "ability", "proficiency" },
        ["dc"] = new[] { "base", "ability", "proficiency", "bonus" },
        ["passive"] = new[] { "base", "modifier" },
        // HP: the first level from the class's hit die, the race's and class's bonus HP and the HP ability; each level after
        ["hpFirstLevel"] = new[] { "hitDie", "bonus", "ability" },
        ["hpPerLevel"] = new[] { "hitDie", "ability" },
        // damage of a type after the creature's "resist.<type>", "weak.<type>" and "immune.<type>" stats
        ["damageTaken"] = new[] { "amount", "resist", "weak", "immune" },
    };

    private static readonly string[] Prefixes = { "mod.", "score.", "stat.", "prof.", "trait." };

    private readonly Dictionary<string, Formula> _formulas = new(StringComparer.Ordinal);

    public Formula? Of(string id) => _formulas.GetValueOrDefault(id);

    public static SheetFormulas Read(ContentNode? found)
    {
        var formulas = new SheetFormulas();
        if (found is not ContentNode node)
        {
            return formulas;
        }
        node.RequireObject("is an object of formulas");
        foreach (KeyValuePair<string, ContentNode> member in node.Members())
        {
            if (!Known.TryGetValue(member.Key, out string[]? names))
            {
                throw member.Value.Fail($"unknown formula; the game asks for {string.Join(", ", Known.Keys)}");
            }
            Formula formula = Formula.Parse(member.Value.AsText(2000), out string error) ?? throw member.Value.Fail(error);
            bool aboutACreature = member.Key != "abilityModifier";
            foreach (string name in formula.Names)
            {
                bool known = names.Contains(name) || (aboutACreature && (name == "level" || Prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal) && name.Length > p.Length)));
                if (!known)
                {
                    throw member.Value.Fail($"unknown name \"{name}\"; it can use {string.Join(", ", names)}"
                        + (aboutACreature ? ", level, mod.<ability>, score.<ability>, stat.<name>, prof.<target>, trait.<weapon trait>" : ""));
                }
            }
            formulas._formulas[member.Key] = formula;
        }
        return formulas;
    }
}
