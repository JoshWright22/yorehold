using System;
using System.Collections.Generic;
using System.Linq;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The sheet panel (C): a hero's scores, skills, feats, uses and conditions as rows by type, and on
/// the right their stat block with the picked row spelled out under it. The heroes are the
/// sources in its head; picking one selects them.
/// </summary>
public sealed class SheetPanel
{
    private static readonly string[] Tabs = { "All", "Abilities", "Skills", "Feats", "Uses", "Conditions" };
    private static readonly string[] Chips = { "Trained" };
    private static readonly DataColumn[] Columns =
    {
        new("Name", 150), new("Type", 76), new("Rank", 80), new("Score", 50, true), new("Bonus", 50, true),
    };

    private readonly DataPanel _view;
    private readonly Dictionary<string, (string Title, string Words)> _details = new();

    public SheetPanel(DataPanel view)
    {
        _view = view;
        _view.SourcePicked += id => HeroPicked?.Invoke(int.Parse(id));
    }

    /// <summary>A hero's name in the head was pressed.</summary>
    public event Action<int>? HeroPicked;

    public void Refresh(World world, int hero)
    {
        WorldCreature c = world.Creatures[hero];
        CharacterSheet sheet = c.Sheet;
        Ruleset rules = world.Rules;
        Compendium compendium = world.Chapter.Compendium;
        _view.SetHead(sheet.Name, SheetPage.Who(compendium, sheet, c.Choices));
        _view.SetSources(Enumerable.Range(0, world.HeroCount).Select(i => (i.ToString(), world.Creatures[i].Sheet.Name)).ToList(), hero.ToString());
        _view.SetTabs(Tabs);
        _view.SetChips(Chips);
        _view.SetColumns(Columns);

        _details.Clear();
        var rows = new List<DataRow>();
        void Add(string key, string tab, string name, string rank, IComparable? score, string scoreText, int? bonus, bool trained, string title, string words)
        {
            var tags = new HashSet<string> { tab };
            if (trained)
            {
                tags.Add("Trained");
            }
            rows.Add(new DataRow
            {
                Key = key,
                Cells = new[] { name, Singular(tab), rank, scoreText, bonus is int b ? SheetView.Signed(b) : "" },
                Sort = new IComparable?[] { name, tab, rank, score, bonus },
                Tags = tags,
                Search = words,
            });
            _details[key] = (title, words);
        }

        foreach (AbilityDefinition a in rules.Abilities)
        {
            int score = sheet.AbilityScore(a.Id);
            int modifier = sheet.AbilityModifier(rules, a.Id);
            bool save = SheetPage.Trained(rules, sheet, a.Id);
            string rank = save ? "save" : "";
            Add("a:" + a.Id, "Abilities", a.Name, rank, score, score.ToString(), modifier, save, a.Name,
                $"{a.Name} {score} adds {SheetView.Signed(modifier)} to its checks." + (save ? $" {sheet.Name} is trained in its saves: {SheetView.Signed(modifier + sheet.ProficiencyModifier(rules, a.Id))}." : ""));
        }
        foreach (SkillDefinition s in rules.Skills)
        {
            bool trained = SheetPage.Trained(rules, sheet, s.Id);
            string rank = sheet.ProficiencyRank(rules, s.Id);
            rank = rank.Length == 0 ? (trained ? "trained" : "") : rules.ProficiencyRanks.Find(r => r.Id == rank)?.Name ?? rank;
            int bonus = sheet.CheckModifier(rules, s.Id);
            Add("s:" + s.Id, "Skills", s.Name, rank, null, s.Ability.ToUpperInvariant(), bonus, trained, s.Name,
                $"{s.Name} checks add {s.Ability.ToUpperInvariant()} ({SheetView.Signed(sheet.AbilityModifier(rules, s.Ability))})"
                + (trained ? $" and {SheetView.Signed(sheet.ProficiencyModifier(rules, s.Id))} for being {rank}" : "") + $": {SheetView.Signed(bonus)} in all.");
        }
        foreach (FeatDefinition f in SheetPage.Feats(compendium, c.Choices))
        {
            Add("f:" + f.Id, "Feats", f.Name, f.Kind, null, "", null, false, f.Name, f.Description);
        }
        foreach (KeyValuePair<string, Resource> r in sheet.Resources)
        {
            Add("u:" + r.Key, "Uses", SheetPage.Words(r.Key), "", r.Value.Current, $"{r.Value.Current}/{r.Value.Max}", null, false, SheetPage.Words(r.Key),
                $"{r.Value.Current} of {r.Value.Max} left. Rests bring them back.");
        }
        foreach ((ConditionDefinition d, ActiveCondition active) in HudText.Conditions(world, sheet))
        {
            string left = active.RoundsLeft > 0 ? $"{active.RoundsLeft} rd" : "";
            Add("c:" + d.Id, "Conditions", d.Name, left, active.Value, active.Value > 1 ? active.Value.ToString() : "", null, false, d.Name, d.Description);
        }
        _view.SetRows(rows);

        string stats = SheetPage.Build(rules, compendium, sheet, c.Choices);
        string picked = "";
        if (_details.TryGetValue(_view.Picked, out (string Title, string Words) detail))
        {
            picked = new BookPage().Rule().Heading(detail.Title).Text(detail.Words).ToString();
        }
        _view.SetEntry(stats + picked, Array.Empty<DataAction>(), "");
        _view.SetFoot(world.Fighting ? "" : $"{Coins.Text(sheet.Coins)}   Carrying {sheet.CarriedWeight():0.#} of {sheet.CarryCapacity(rules):0} lb");
    }

    private static string Singular(string tab) => tab switch
    {
        "Abilities" => "Ability",
        "Skills" => "Skill",
        "Feats" => "Feat",
        "Uses" => "Use",
        "Conditions" => "Condition",
        _ => tab,
    };
}
