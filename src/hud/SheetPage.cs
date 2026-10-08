using System.Collections.Generic;
using System.Linq;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// A character as a stat block on a book page: who they are, AC, HP and speed, the scores in a
/// row, then trained skills, feats, uses, conditions and gear. The sheet panel, the library and
/// the character screens all show it.
/// </summary>
public static class SheetPage
{
    public static string Build(Ruleset rules, Compendium compendium, CharacterSheet sheet, CharacterChoices? choices, bool carrying = true)
    {
        var page = new BookPage().Title(sheet.Name.Trim().Length == 0 ? "New character" : sheet.Name).Sub(Who(compendium, sheet, choices, rules)).Rule();
        SheetLayout layout = rules.Sheet;
        // the system says which parts there are and in what order; a rule closes the top block and the scores
        bool ruled = false;
        foreach (string section in layout.Order)
        {
            bool block = section is "vitals" or "level" or "scores";
            if (!block && !ruled)
            {
                page.Rule();
                ruled = true;
            }
            switch (section)
            {
                case "vitals":
                    string hp = $"{System.Math.Max(0, sheet.Hp)} / {sheet.MaxHp}" + (sheet.TempHp > 0 ? $" (+{sheet.TempHp})" : "");
                    // armour class where the system uses it, then its own defences (Fate's Defend, a Reflex defence)
                    var vitals = new List<(string, string)>();
                    if (rules.Checks.Kind(CheckRules.Attack).DefenceId == DefenceDefinition.ArmorClass)
                    {
                        vitals.Add((layout.NameOf("ac"), sheet.ArmorClass(rules).ToString()));
                    }
                    vitals.AddRange(rules.Defences.Select(d => (d.Name, sheet.Defence(rules, d.Id).ToString())));
                    vitals.Add((layout.NameOf("hp"), hp));
                    vitals.Add((layout.NameOf("speed"), $"{sheet.SpeedFeet} ft"));
                    page.Stats(vitals.ToArray());
                    break;
                case "tracks":
                    // "Stress 2/3, Mild consequence 1/1"
                    page.Stat(layout.NameOf("tracks"), string.Join(", ", sheet.Tracks.Where(t => t.Max > 0).Select(t => $"{t.Name} {t.Value}/{t.Max}")));
                    break;
                case "fields":
                    // each field under its own name: "High concept  Wizard of the north"
                    foreach (FieldDefinition field in rules.Fields)
                    {
                        page.Stat(field.Name, string.Join("; ", sheet.Fields.GetValueOrDefault(field.Id, new List<string>()).Where(l => l.Trim().Length > 0)));
                    }
                    break;
                case "level":
                    int xp = choices != null ? System.Math.Max(choices.Xp, sheet.Xp) : sheet.Xp;
                    string next = sheet.Level - 1 < rules.XpForLevel.Count ? $" of {rules.XpForLevel[sheet.Level - 1]}" : "";
                    page.Stats((layout.NameOf("hitDie"), sheet.HitDie), (layout.NameOf("xp"), $"{xp}{next}"));
                    break;
                case "scores":
                    page.Rule();
                    page.Table(rules.Abilities.Select(a => a.Id.ToUpperInvariant()).ToList(),
                        rules.Abilities.Select(a => sheet.AbilityScore(a.Id).ToString())
                            .Concat(rules.Abilities.Select(a => $"({SheetView.Signed(sheet.AbilityModifier(rules, a.Id))})")).ToList());
                    page.Rule();
                    ruled = true;
                    break;
                case "saves":
                    page.Stat(layout.NameOf("saves"), string.Join(", ", SheetLayout.Saves(rules, sheet).Select(s => $"{s.Name} {SheetView.Signed(s.Modifier)}")));
                    break;
                case "skills":
                    page.Stat(layout.NameOf("skills"), string.Join(", ", rules.Skills.Where(s => Trained(rules, sheet, s.Id)).Select(s => $"{s.Name} {SheetView.Signed(sheet.CheckModifier(rules, s.Id))}")));
                    break;
                case "defences":
                    page.Stat(layout.NameOf("defences"), string.Join(", ", SheetLayout.Defences(sheet)));
                    break;
                case "weapon" when sheet.WeaponItem is Item weapon:
                    page.Stat(layout.NameOf("weapon"), $"{weapon.Name}, {DiceText.Fill(weapon.Definition.Damage, name => sheet.Named(rules, name))}");
                    break;
                case "feats":
                    page.Stat(layout.NameOf("feats"), string.Join(", ", Feats(compendium, choices).Select(f => f.Name)));
                    break;
                case "uses":
                    page.Stat(layout.NameOf("uses"), string.Join(", ", sheet.Resources.Select(r => $"{Words(r.Key)} {r.Value.Current}/{r.Value.Max}")));
                    break;
                case "conditions":
                    page.Stat(layout.NameOf("conditions"), string.Join(", ", sheet.Conditions.Select(c => rules.Condition(c.Id)?.Name ?? c.Id)));
                    break;
                case "carrying" when carrying:
                    var gear = sheet.Inventory.Select(i => (i.Quantity > 1 ? $"{i.Name} x{i.Quantity}" : i.Name) + (i.Equipped ? (i.Held ? " (in hand)" : " (worn)") : "")).ToList();
                    if (sheet.Coins > 0)
                    {
                        gear.Add(Coins.Text(sheet.Coins));
                    }
                    page.Stat(layout.NameOf("carrying"), string.Join(", ", gear));
                    break;
            }
        }
        return page.ToString();
    }

    /// <summary>A page with just a name and some lines, for a seat whose hero is rolled when the adventure starts.</summary>
    public static string Text(string title, string text)
    {
        var page = new BookPage().Title(title).Rule();
        foreach (string line in text.Split('\n'))
        {
            page.Text(line);
        }
        return page.ToString();
    }

    /// <summary>"Level 1 Human Fighter, Soldier".</summary>
    public static string Who(Compendium compendium, CharacterSheet sheet, CharacterChoices? choices, Ruleset? rules = null)
    {
        // a system without levels or classes doesn't name them (Fate)
        string who = rules?.Advancement == "none" ? "" : $"Level {sheet.Level}";
        if (sheet.Ancestry.Length > 0)
        {
            who += " " + sheet.Ancestry;
        }
        // the system's own picks after the race: "Dwarf (Forge dwarf)"
        List<string> picked = choices?.Options.Values.Where(compendium.Options.ContainsKey).Select(id => compendium.Options[id].Name).ToList() ?? new List<string>();
        if (picked.Count > 0)
        {
            who += $" ({string.Join(", ", picked)})";
        }
        if (sheet.ClassName.Length > 0 && rules?.Creation.AsksClass != false)
        {
            who += " " + sheet.ClassName;
        }
        who = who.Trim();
        if (choices != null && compendium.Backgrounds.TryGetValue(choices.Background, out BackgroundDefinition? background))
        {
            who += ", " + background.Name;
        }
        return who;
    }

    /// <summary>The feats a character has from race, background and picks, in that order.</summary>
    public static List<FeatDefinition> Feats(Compendium compendium, CharacterChoices? choices)
    {
        if (choices == null)
        {
            return new List<FeatDefinition>();
        }
        IEnumerable<string> ids = (compendium.Races.TryGetValue(choices.Race, out RaceDefinition? race) ? race.Feats : new List<string>())
            .Concat(compendium.Backgrounds.TryGetValue(choices.Background, out BackgroundDefinition? b) ? b.Feats : new List<string>())
            .Concat(choices.Levels.SelectMany(l => l.Picked("feats")));
        return ids.Select(id => compendium.Feats.TryGetValue(id, out FeatDefinition? feat) ? feat : new FeatDefinition { Id = id, Name = id }).ToList();
    }

    public static bool Trained(Ruleset rules, CharacterSheet sheet, string id)
    {
        bool ranked = sheet.ProficiencyRanks.TryGetValue(id, out string? rank) && rank != rules.UntrainedRank;
        return sheet.Proficiencies.Contains(id) || ranked;
    }

    // "second-wind" reads "Second wind"
    public static string Words(string id)
    {
        string words = id.Replace('-', ' ');
        return words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
