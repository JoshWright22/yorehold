namespace Yorehold.Rules;

/// <summary>
/// ruleset.json's "sheet": which parts of a character sheet the system shows, in its order, and
/// what it calls them. Fate has no hit die and calls its HP "Stress"; PF2e shows Fortitude,
/// Reflex and Will. The screens draw what this lists and nothing else.
/// </summary>
public sealed class SheetLayout
{
    /// <summary>The parts a sheet can have: vitals (AC, HP, speed), level (hit die, XP), the scores, then the lists.</summary>
    public static readonly string[] Sections =
    {
        "vitals", "tracks", "level", "scores", "saves", "skills", "defences", "weapon", "feats", "uses", "conditions", "carrying",
    };

    // the labels inside those parts a system may rename
    private static readonly Dictionary<string, string> DefaultNames = new(StringComparer.Ordinal)
    {
        ["ac"] = "AC", ["hp"] = "HP", ["tracks"] = "Tracks", ["speed"] = "Speed", ["hitDie"] = "Hit die", ["xp"] = "XP",
        ["saves"] = "Saves", ["skills"] = "Skills", ["defences"] = "Defences", ["weapon"] = "Weapon",
        ["feats"] = "Feats", ["uses"] = "Uses", ["conditions"] = "Conditions", ["carrying"] = "Carrying",
    };

    public List<string> Order { get; init; } = new(Sections);
    public Dictionary<string, string> Names { get; init; } = new(DefaultNames, StringComparer.Ordinal);

    public bool Shows(string section) => Order.Contains(section);

    public string NameOf(string label) => Names.GetValueOrDefault(label, label);

    /// <summary>
    /// The saves a sheet lists: every one of the system's own saves (Fortitude +7), or where saves
    /// are abilities, those it is trained in.
    /// </summary>
    public static List<(string Name, int Modifier)> Saves(Ruleset rules, CharacterSheet sheet)
    {
        if (rules.Saves.Count > 0)
        {
            return rules.Saves.Select(s => (s.Name.Length > 0 ? s.Name : s.Id, sheet.SaveModifier(rules, s.Id))).ToList();
        }
        return rules.Abilities.Where(a => sheet.Proficiencies.Contains(a.Id)
                || (sheet.ProficiencyRanks.TryGetValue(a.Id, out string? rank) && rank != rules.UntrainedRank))
            .Select(a => (a.Id.ToUpperInvariant(), sheet.SaveModifier(rules, a.Id))).ToList();
    }

    /// <summary>Resistances, weaknesses and immunities by damage type: "resists fire 5", "weak to cold 5", "immune to poison".</summary>
    public static List<string> Defences(CharacterSheet sheet)
    {
        IEnumerable<string> stats = sheet.Stats.Bases.Keys.Concat(sheet.Stats.Modifiers.Select(m => m.Modifier.Stat)).Distinct().OrderBy(s => s, StringComparer.Ordinal);
        var lines = new List<string>();
        foreach (string stat in stats)
        {
            int value = sheet.Stats.Integer(stat);
            int dot = stat.IndexOf('.');
            if (value <= 0 || dot < 0)
            {
                continue;
            }
            string type = stat[(dot + 1)..];
            switch (stat[..dot])
            {
                case "resist":
                    lines.Add($"resists {type} {value}");
                    break;
                case "weak":
                    lines.Add($"weak to {type} {value}");
                    break;
                case "immune":
                    lines.Add($"immune to {type}");
                    break;
            }
        }
        return lines;
    }

    public static SheetLayout Read(ContentNode? found)
    {
        if (found is not ContentNode node)
        {
            return new SheetLayout();
        }
        node.RequireObject("is an object with sections and names");
        node.Only("sections", "names");
        List<string> order = new(Sections);
        if (node.Get("sections") is ContentNode list)
        {
            order = node.Names("sections");
            if (order.Count == 0 || order.Distinct().Count() != order.Count)
            {
                throw list.Fail("lists each section once");
            }
            if (order.FirstOrDefault(s => !Sections.Contains(s)) is string unknown)
            {
                throw list.Fail($"unknown section \"{unknown}\"; sections are {string.Join(", ", Sections)}");
            }
        }
        var names = new Dictionary<string, string>(DefaultNames, StringComparer.Ordinal);
        foreach (KeyValuePair<string, ContentNode> member in node.Get("names")?.Members() ?? Enumerable.Empty<KeyValuePair<string, ContentNode>>())
        {
            if (!DefaultNames.ContainsKey(member.Key))
            {
                throw member.Value.Fail($"unknown label; labels are {string.Join(", ", DefaultNames.Keys)}");
            }
            names[member.Key] = member.Value.AsText(32);
        }
        return new SheetLayout { Order = order, Names = names };
    }
}
