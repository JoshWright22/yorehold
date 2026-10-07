using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The sheet panel (C), laid out like a character sheet on a virtual tabletop: the face, name and
/// XP across the top, then what matters most in a fight in big boxes (HP, AC, initiative, speed),
/// the ability scores as big modifiers with the score under them, the skills down the left and
/// features, uses, conditions and gear on tabs to the right. The heroes are buttons in the head;
/// picking one selects them. It only reads the World.
/// </summary>
public partial class SheetPanel : PanelContainer
{
    private static readonly string[] Tabs = { "Features", "Uses", "Conditions", "Gear" };

    /// <summary>A hero's name in the head was pressed.</summary>
    public event Action<int>? HeroPicked;
    public event Action? ClosePressed;

    private PortraitView _portrait = null!;
    private Label _name = null!;
    private Label _who = null!;
    private ProgressBar _xp = null!;
    private Label _xpText = null!;
    private HBoxContainer _sources = null!;
    private Label _hp = null!;
    private Label _hpSub = null!;
    private ProgressBar _hpBar = null!;
    private readonly Dictionary<string, (Label Value, Label Sub)> _vitals = new();
    private HBoxContainer _abilities = null!;
    private VBoxContainer _skills = null!;
    private HBoxContainer _tabs = null!;
    private RichTextLabel _page = null!;
    private Label _foot = null!;
    private string _tab = Tabs[0];
    private string _sourcesShown = "";
    private string _abilitiesShown = "";
    private string _skillsShown = "";
    private string _pageShown = "";

    public override void _Ready()
    {
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 10);
        AddChild(rows);

        // the head: face, name, who they are and how far to the next level
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 14);
        rows.AddChild(head);
        _portrait = new PortraitView { CustomMinimumSize = new Vector2(92, 92), MouseFilter = MouseFilterEnum.Ignore };
        head.AddChild(_portrait);
        var named = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, Alignment = BoxContainer.AlignmentMode.Center };
        named.AddThemeConstantOverride("separation", 2);
        head.AddChild(named);
        _name = new Label { ThemeTypeVariation = "TitleLabel" };
        _name.AddThemeFontSizeOverride("font_size", 30);
        named.AddChild(_name);
        _who = new Label { ThemeTypeVariation = "CellLabel" };
        _who.AddThemeFontSizeOverride("font_size", 15);
        named.AddChild(_who);
        var xpRow = new HBoxContainer();
        xpRow.AddThemeConstantOverride("separation", 8);
        named.AddChild(xpRow);
        xpRow.AddChild(new Label { Text = "XP", ThemeTypeVariation = "CapsLabel" });
        _xp = new ProgressBar { CustomMinimumSize = new Vector2(220, 8), ShowPercentage = false, ThemeTypeVariation = "XpBar", SizeFlagsVertical = SizeFlags.ShrinkCenter };
        xpRow.AddChild(_xp);
        _xpText = new Label { ThemeTypeVariation = "NumberLabel" };
        xpRow.AddChild(_xpText);
        var right = new VBoxContainer();
        head.AddChild(right);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        buttons.AddThemeConstantOverride("separation", 2);
        right.AddChild(buttons);
        _sources = new HBoxContainer();
        _sources.AddThemeConstantOverride("separation", 2);
        buttons.AddChild(_sources);
        var close = new Button { Text = "Close", FocusMode = FocusModeEnum.None };
        close.Pressed += () => ClosePressed?.Invoke();
        buttons.AddChild(close);

        // what matters in a fight, biggest first
        var vitals = new HBoxContainer();
        vitals.AddThemeConstantOverride("separation", 8);
        rows.AddChild(vitals);
        VBoxContainer hpBox = Box(vitals, "Hit points", 230);
        _hp = Big(hpBox, 34);
        _hpBar = new ProgressBar { CustomMinimumSize = new Vector2(0, 8), ShowPercentage = false, ThemeTypeVariation = "HpBar" };
        hpBox.AddChild(_hpBar);
        _hpSub = Small(hpBox);
        foreach (string vital in new[] { "Armor class", "Initiative", "Speed", "Proficiency", "Hit die" })
        {
            VBoxContainer box = Box(vitals, vital, 0);
            box.GetParent<Control>().SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _vitals[vital] = (Big(box, vital is "Armor class" ? 34 : 26), Small(box));
        }

        _abilities = new HBoxContainer();
        _abilities.AddThemeConstantOverride("separation", 8);
        rows.AddChild(_abilities);

        // skills down the left, the rest on tabs to the right
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 10);
        rows.AddChild(body);
        var skillCard = new PanelContainer { ThemeTypeVariation = "PageCard", CustomMinimumSize = new Vector2(300, 0) };
        body.AddChild(skillCard);
        var skillScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        skillCard.AddChild(skillScroll);
        _skills = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _skills.AddThemeConstantOverride("separation", 0);
        skillScroll.AddChild(_skills);
        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        side.AddThemeConstantOverride("separation", 6);
        body.AddChild(side);
        _tabs = new HBoxContainer();
        _tabs.AddThemeConstantOverride("separation", 2);
        side.AddChild(_tabs);
        foreach (string tab in Tabs)
        {
            var button = new Button { Text = tab, ToggleMode = true, ThemeTypeVariation = "TabButton", FocusMode = FocusModeEnum.None };
            button.SetPressedNoSignal(tab == _tab);
            button.Pressed += () =>
            {
                _tab = tab;
                foreach (Button other in _tabs.GetChildren().OfType<Button>())
                {
                    other.SetPressedNoSignal(other.Text == _tab);
                }
            };
            _tabs.AddChild(button);
        }
        var pageCard = new PanelContainer { ThemeTypeVariation = "PageCard", SizeFlagsVertical = SizeFlags.ExpandFill };
        side.AddChild(pageCard);
        _page = new RichTextLabel { ThemeTypeVariation = "PageText", BbcodeEnabled = true, ScrollActive = true };
        pageCard.AddChild(_page);

        _foot = new Label { ThemeTypeVariation = "DimLabel" };
        rows.AddChild(_foot);
    }

    // A box with a small-caps label over what goes in it.
    private static VBoxContainer Box(Container parent, string label, float width)
    {
        var card = new PanelContainer { ThemeTypeVariation = "PageCard", CustomMinimumSize = new Vector2(width, 0) };
        parent.AddChild(card);
        var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        box.AddThemeConstantOverride("separation", 2);
        card.AddChild(box);
        box.AddChild(new Label { Text = label.ToUpperInvariant(), ThemeTypeVariation = "CapsLabel", HorizontalAlignment = HorizontalAlignment.Center });
        return box;
    }

    private static Label Big(Container box, int size)
    {
        var label = new Label { ThemeTypeVariation = "NumberLabel", HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", Palette.Bone);
        box.AddChild(label);
        return label;
    }

    private static Label Small(Container box)
    {
        var label = new Label { ThemeTypeVariation = "DimLabel", HorizontalAlignment = HorizontalAlignment.Center };
        box.AddChild(label);
        return label;
    }

    public void Refresh(World world, int hero)
    {
        WorldCreature c = world.Creatures[hero];
        CharacterSheet sheet = c.Sheet;
        Ruleset rules = world.Rules;
        Compendium compendium = world.Chapter.Compendium;

        _portrait.Show(sheet.Name, world.Tokens.Tokens[hero].Color.ToGodot(), sheet.Down, Portraits.Of(world, hero));
        _name.Text = sheet.Name;
        _who.Text = SheetPage.Who(compendium, sheet, c.Choices);
        int xp = c.Choices != null ? Math.Max(c.Choices.Xp, sheet.Xp) : sheet.Xp;
        int next = sheet.Level - 1 < rules.XpForLevel.Count ? rules.XpForLevel[sheet.Level - 1] : 0;
        _xp.Visible = next > 0;
        _xp.MaxValue = Math.Max(1, next);
        _xp.Value = Math.Min(xp, next);
        _xpText.Text = next > 0 ? $"{xp} / {next}" : xp.ToString();
        ShowSources(world, hero);

        _hp.Text = $"{Math.Max(0, sheet.Hp)} / {sheet.MaxHp}";
        _hpBar.MaxValue = Math.Max(1, sheet.MaxHp);
        _hpBar.Value = Math.Max(0, sheet.Hp);
        _hpSub.Text = sheet.TempHp > 0 ? $"+{sheet.TempHp} temporary" : sheet.Down ? "down" : "";
        Vital("Armor class", sheet.ArmorClass(rules).ToString(), "");
        int initiative = sheet.AbilityModifier(rules, "dex");
        Vital("Initiative", SheetView.Signed(initiative), "dex");
        Vital("Speed", $"{sheet.SpeedFeet}", "feet");
        Vital("Proficiency", SheetView.Signed(rules.ProficiencyBonus(sheet.Level)), "bonus");
        Vital("Hit die", sheet.HitDie, $"level {sheet.Level}");

        ShowAbilities(rules, sheet);
        ShowSkills(rules, sheet);
        ShowPage(world, rules, compendium, c, sheet);
        _foot.Text = world.Fighting ? "" : $"{Coins.Text(sheet.Coins)}   Carrying {sheet.CarriedWeight():0.#} of {sheet.CarryCapacity(rules):0} lb";
    }

    private void Vital(string name, string value, string sub)
    {
        (Label v, Label s) = _vitals[name];
        v.Text = value;
        s.Text = sub;
    }

    private void ShowSources(World world, int hero)
    {
        string signature = string.Join("|", Enumerable.Range(0, world.HeroCount).Select(i => world.Creatures[i].Sheet.Name)) + "#" + hero;
        if (signature == _sourcesShown)
        {
            return;
        }
        _sourcesShown = signature;
        Clear(_sources);
        for (int i = 0; i < world.HeroCount && world.HeroCount > 1; i++)
        {
            int who = i;
            var button = new Button { Text = world.Creatures[i].Sheet.Name, ToggleMode = true, ThemeTypeVariation = "TabButton", FocusMode = FocusModeEnum.None };
            button.SetPressedNoSignal(i == hero);
            button.Pressed += () => HeroPicked?.Invoke(who);
            _sources.AddChild(button);
        }
    }

    // One box per ability: the modifier big, the score under it, the save beside a mark when trained.
    private void ShowAbilities(Ruleset rules, CharacterSheet sheet)
    {
        var parts = rules.Abilities.Select(a =>
        {
            int modifier = sheet.AbilityModifier(rules, a.Id);
            bool trained = SheetPage.Trained(rules, sheet, a.Id);
            int save = modifier + (trained ? sheet.ProficiencyModifier(rules, a.Id) : 0);
            return (a.Name, Modifier: SheetView.Signed(modifier), Score: sheet.AbilityScore(a.Id).ToString(), Save: (trained ? "● " : "") + "save " + SheetView.Signed(save), trained);
        }).ToList();
        string signature = string.Join("|", parts.Select(p => $"{p.Name}{p.Modifier}{p.Score}{p.Save}"));
        if (signature == _abilitiesShown)
        {
            return;
        }
        _abilitiesShown = signature;
        Clear(_abilities);
        foreach (var part in parts)
        {
            VBoxContainer box = Box(_abilities, part.Name, 0);
            box.GetParent<Control>().SizeFlagsHorizontal = SizeFlags.ExpandFill;
            Big(box, 30).Text = part.Modifier;
            var score = Small(box);
            score.Text = part.Score;
            score.ThemeTypeVariation = "NumberLabel";
            var save = Small(box);
            save.Text = part.Save;
            if (part.trained)
            {
                save.AddThemeColorOverride("font_color", Palette.Straw);
            }
        }
    }

    // Saving throws are on the ability boxes; the skills list each one with its total, trained ones marked.
    private void ShowSkills(Ruleset rules, CharacterSheet sheet)
    {
        var parts = rules.Skills.Select(s =>
        {
            bool trained = SheetPage.Trained(rules, sheet, s.Id);
            string rank = sheet.ProficiencyRank(rules, s.Id);
            rank = rank.Length == 0 ? "" : rules.ProficiencyRanks.Find(r => r.Id == rank)?.Name ?? rank;
            return (s.Name, Ability: s.Ability.ToUpperInvariant(), Bonus: SheetView.Signed(sheet.CheckModifier(rules, s.Id)), trained, rank);
        }).ToList();
        string signature = string.Join("|", parts.Select(p => $"{p.Name}{p.Bonus}{p.trained}{p.rank}"));
        if (signature == _skillsShown)
        {
            return;
        }
        _skillsShown = signature;
        Clear(_skills);
        _skills.AddChild(new Label { Text = "SKILLS", ThemeTypeVariation = "CapsLabel" });
        foreach (var part in parts)
        {
            var row = new HBoxContainer { CustomMinimumSize = new Vector2(0, 22), TooltipText = part.rank };
            row.AddThemeConstantOverride("separation", 6);
            var mark = new Label { Text = part.trained ? "●" : "○", ThemeTypeVariation = part.trained ? "CellLabel" : "DimLabel", CustomMinimumSize = new Vector2(12, 0) };
            if (part.trained)
            {
                mark.AddThemeColorOverride("font_color", Palette.Straw);
            }
            row.AddChild(mark);
            var name = new Label { Text = part.Name, ThemeTypeVariation = part.trained ? "" : "CellLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
            row.AddChild(name);
            row.AddChild(new Label { Text = part.Ability, ThemeTypeVariation = "DimLabel", CustomMinimumSize = new Vector2(34, 0) });
            var bonus = new Label { Text = part.Bonus, ThemeTypeVariation = "NumberLabel", HorizontalAlignment = HorizontalAlignment.Right, CustomMinimumSize = new Vector2(30, 0) };
            if (part.trained)
            {
                bonus.AddThemeColorOverride("font_color", Palette.Bone);
            }
            row.AddChild(bonus);
            _skills.AddChild(row);
        }
    }

    private void ShowPage(World world, Ruleset rules, Compendium compendium, WorldCreature c, CharacterSheet sheet)
    {
        var page = new BookPage();
        switch (_tab)
        {
            case "Features":
                if (sheet.WeaponItem is Item weapon)
                {
                    page.Heading("Weapon").Stat(weapon.Name, weapon.Definition.Damage).Gap();
                }
                List<FeatDefinition> feats = SheetPage.Feats(compendium, c.Choices);
                if (feats.Count == 0)
                {
                    page.Note($"{sheet.Name} has no feats.");
                }
                foreach (FeatDefinition f in feats)
                {
                    page.Heading(f.Name + (f.Kind.Length > 0 ? $" ({f.Kind})" : "")).Text(f.Description).Gap();
                }
                break;
            case "Uses":
                if (sheet.Resources.Count == 0)
                {
                    page.Note("Nothing that runs out.");
                }
                foreach (KeyValuePair<string, Rules.Resource> r in sheet.Resources)
                {
                    string pips = r.Value.Max <= 12 ? new string('●', Math.Max(0, r.Value.Current)) + new string('○', Math.Max(0, r.Value.Max - r.Value.Current)) : "";
                    page.Stat(SheetPage.Words(r.Key), $"{r.Value.Current} of {r.Value.Max}  {pips}");
                }
                page.Gap().Note("Rests bring them back.");
                break;
            case "Conditions":
                var conditions = HudText.Conditions(world, sheet).ToList();
                if (conditions.Count == 0)
                {
                    page.Note("No conditions.");
                }
                foreach ((ConditionDefinition d, ActiveCondition active) in conditions)
                {
                    string left = active.RoundsLeft > 0 ? $", {active.RoundsLeft} rounds left" : "";
                    page.Heading(d.Name + (active.Value > 1 ? $" {active.Value}" : "") + left).Text(d.Description).Gap();
                }
                break;
            default:
                if (sheet.Inventory.Count == 0)
                {
                    page.Note("Carrying nothing.");
                }
                foreach (Item item in sheet.Inventory)
                {
                    string state = item.Equipped ? (item.Held ? "in hand" : "worn") : "";
                    page.Stat(item.Quantity > 1 ? $"{item.Name} x{item.Quantity}" : item.Name, state);
                }
                break;
        }
        string text = page.ToString();
        if (text != _pageShown)
        {
            _pageShown = text;
            _page.Text = text;
        }
    }

    private static void Clear(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }
}
