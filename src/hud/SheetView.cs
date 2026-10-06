using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// A character sheet: who they are, HP, AC, speed and XP, the scores, trained skills, feats and
/// uses, and what they carry. The character screens show the sheet a draft or a library file
/// builds; the play screen shows a hero's live one.
/// </summary>
public partial class SheetView : PanelContainer
{
    // the scene has all of these
    private Label _name = null!;
    private Label _who = null!;
    private Label _numbers = null!;
    private GridContainer _scores = null!;
    private Label _skills = null!;
    private Label _feats = null!;
    private Label _uses = null!;
    private Label _carrying = null!;
    private Label _problem = null!;

    public override void _Ready()
    {
        _name = GetNode<Label>("Rows/Name");
        _who = GetNode<Label>("Rows/Who");
        _numbers = GetNode<Label>("Rows/Numbers");
        _scores = GetNode<GridContainer>("Rows/Scores");
        _skills = GetNode<Label>("Rows/Skills");
        _feats = GetNode<Label>("Rows/Feats");
        _uses = GetNode<Label>("Rows/Uses");
        _carrying = GetNode<Label>("Rows/Carrying");
        _problem = GetNode<Label>("Rows/Problem");
    }

    /// <summary>Just a name and a few lines, for a seat whose hero is rolled when the adventure starts.</summary>
    public void ShowText(string title, string text)
    {
        _name.Text = title;
        _who.Text = text;
        foreach (Label label in new[] { _numbers, _skills, _feats, _uses, _carrying, _problem })
        {
            label.Visible = false;
        }
        _scores.Visible = false;
        _who.Visible = true;
    }

    /// <summary>
    /// A built sheet, or the reason there is none. choices gives the background and feats by name;
    /// null for a sheet without them.
    /// </summary>
    public void ShowSheet(Ruleset rules, Compendium compendium, CharacterSheet? sheet, CharacterChoices? choices, string problem, bool carrying = true)
    {
        string name = sheet?.Name ?? choices?.Name ?? "";
        _name.Text = name.Trim().Length == 0 ? "New character" : name;
        _problem.Visible = sheet == null;
        _problem.Text = problem.Length == 0 ? "Not ready yet." : Capital(problem);
        foreach (Control part in new Control[] { _who, _numbers, _scores, _skills, _feats, _uses, _carrying })
        {
            part.Visible = sheet != null;
        }
        if (sheet == null)
        {
            return;
        }

        string who = $"Level {sheet.Level}";
        if (sheet.Ancestry.Length > 0)
        {
            who += " " + sheet.Ancestry;
        }
        if (sheet.ClassName.Length > 0)
        {
            who += " " + sheet.ClassName;
        }
        if (choices != null && compendium.Backgrounds.TryGetValue(choices.Background, out BackgroundDefinition? background))
        {
            who += ", " + background.Name;
        }
        _who.Text = who;
        int xp = choices != null ? System.Math.Max(choices.Xp, sheet.Xp) : sheet.Xp;
        string next = sheet.Level - 1 < rules.XpForLevel.Count ? $" of {rules.XpForLevel[sheet.Level - 1]}" : "";
        _numbers.Text = $"HP {System.Math.Max(0, sheet.Hp)} / {sheet.MaxHp}    AC {sheet.ArmorClass(rules)}    Speed {sheet.SpeedFeet} ft\nXP {xp}{next}";

        while (_scores.GetChildCount() < rules.Abilities.Count)
        {
            _scores.AddChild(new Label { CustomMinimumSize = new Vector2(110, 0) });
        }
        for (int i = 0; i < _scores.GetChildCount(); i++)
        {
            var label = (Label)_scores.GetChild(i);
            label.Visible = i < rules.Abilities.Count;
            if (label.Visible)
            {
                string id = rules.Abilities[i].Id;
                label.Text = $"{id.ToUpperInvariant()} {sheet.AbilityScore(id)} ({Signed(sheet.AbilityModifier(rules, id))})";
            }
        }

        var skills = new List<string>();
        foreach (SkillDefinition skill in rules.Skills)
        {
            bool ranked = sheet.ProficiencyRanks.TryGetValue(skill.Id, out string? rank) && rank != rules.UntrainedRank;
            if (sheet.Proficiencies.Contains(skill.Id) || ranked)
            {
                skills.Add($"{skill.Name} {Signed(sheet.CheckModifier(rules, skill.Id))}");
            }
        }
        Line(_skills, "Skills", skills);

        var feats = new List<string>();
        if (choices != null)
        {
            IEnumerable<string> ids = (compendium.Races.TryGetValue(choices.Race, out RaceDefinition? race) ? race.Feats : new List<string>())
                .Concat(compendium.Backgrounds.TryGetValue(choices.Background, out BackgroundDefinition? b) ? b.Feats : new List<string>())
                .Concat(choices.Levels.SelectMany(l => l.Picked("feats")));
            feats.AddRange(ids.Select(id => compendium.Feats.TryGetValue(id, out FeatDefinition? feat) ? feat.Name : id));
        }
        Line(_feats, "Feats", feats);

        // "second-wind" reads as "Second wind 1/1"
        var uses = sheet.Resources.Select(r => $"{Capital(r.Key.Replace('-', ' '))} {r.Value.Current}/{r.Value.Max}").ToList();
        Line(_uses, "Uses", uses);

        var gear = sheet.Inventory.Select(i => (i.Quantity > 1 ? $"{i.Name} x{i.Quantity}" : i.Name) + (i.Equipped ? " (worn)" : "")).ToList();
        if (sheet.Coins > 0)
        {
            gear.Add(Coins.Text(sheet.Coins));
        }
        Line(_carrying, "Carrying", gear);
        _carrying.Visible = carrying && gear.Count > 0;
    }

    private static void Line(Label label, string title, List<string> names)
    {
        label.Text = $"{title}: {string.Join(", ", names)}";
        label.Visible = names.Count > 0;
    }

    public static string Signed(int n) => n >= 0 ? "+" + n : n.ToString();

    private static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
