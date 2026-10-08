using System;
using System.Collections.Generic;
using System.Linq;
using Yorehold.Rules;

namespace Yorehold;

public enum SpellOrderKind
{
    Prepare,
    Cast,
    Aim,
}

/// <summary>Something the spell panel asks for; the play screen does it. Prepare carries the whole new list.</summary>
public sealed record SpellOrder(SpellOrderKind Kind, int Hero, string Spell = "", int Target = -1, IReadOnlyList<string>? Prepared = null);

/// <summary>
/// The spell panel (K): a hero's spells as a data panel. Tabs are the spell levels and focus spells,
/// chips narrow it to what can be cast now, concentration, areas or help, and the entry is the
/// spell's page with Prepare, Cast or Use under it. Slots, focus, free hands and what the hero holds
/// in place are in the foot. It only reads the World; what is pressed goes out as a SpellOrder.
/// </summary>
public sealed class SpellPanel
{
    private static readonly string[] Chips = { "Castable", "Concentration", "Area", "Helps" };
    private static readonly DataColumn[] Columns =
    {
        new("Name", 130), new("Lvl", 30, true), new("Cost", 38, true), new("Range", 54, true), new("Area", 74), new("Status", 92),
    };

    // the hero's other actions sit in the book too, so they can be dragged onto the bars as well
    private const string ActionKey = "action:";

    private readonly DataPanel _view;
    private int _hero;
    // what the hero had prepared at the last refresh, for a Prepare press to work from
    private List<string> _prepared = new();

    public SpellPanel(DataPanel view)
    {
        _view = view;
        _view.Grid = true;
        _view.ActionPressed += Act;
        _view.SourcePicked += id => HeroPicked?.Invoke(int.Parse(id));
    }

    public event Action<SpellOrder>? Ordered;
    public event Action<int>? HeroPicked;

    public void Refresh(World world, int hero, bool heroTurn)
    {
        _hero = hero;
        WorldCreature c = world.Creatures[hero];
        CharacterSheet sheet = c.Sheet;
        _prepared = sheet.Prepared.ToList();
        List<SpellDefinition> spells = SpellsOf(world, sheet);

        string sub = c.Concentration.Active ? $"concentrating on {SpellName(world, c.Concentration.Spell)}"
            : sheet.Preparable.Count > 0 ? $"prepares {sheet.PrepareLimit} of {sheet.Preparable.Count}"
            : spells.Count > 0 ? $"knows {spells.Count} {(spells.Count == 1 ? "spell" : "spells")}" : "";
        _view.SetHead($"{sheet.Name}'s spells", sub);
        _view.SetSources(Enumerable.Range(0, world.HeroCount).Select(i => (i.ToString(), world.Creatures[i].Sheet.Name)).ToList(), hero.ToString());
        List<ActionDefinition> others = world.ActionsOf(hero).Where(a => world.SpellOf(a) == null && a.Id != world.EndTurnAction).ToList();
        var tabs = new List<string> { "All" };
        tabs.AddRange(spells.Where(s => s.Spends.Count == 0).Select(s => s.Level).Distinct().Order().Select(LevelTab));
        if (spells.Any(s => s.Spends.Count > 0))
        {
            tabs.Add("Focus");
        }
        if (others.Count > 0)
        {
            tabs.Add("Actions");
        }
        _view.SetTabs(tabs);
        _view.SetChips(Chips);
        _view.SetColumns(Columns);

        var rows = new List<DataRow>();
        foreach (SpellDefinition spell in spells)
        {
            ActionDefinition a = spell.Action;
            bool known = sheet.Spells.Contains(spell.Id);
            bool castable = known && Spellcasting.CanCast(sheet, spell, world.SpellRules, out _);
            string status = c.Concentration.Spell == spell.Id ? "holding"
                : sheet.Prepared.Contains(spell.Id) ? "prepared"
                : known ? "known" : "not prepared";
            var tags = new HashSet<string> { spell.Spends.Count > 0 ? "Focus" : LevelTab(spell.Level) };
            if (castable)
            {
                tags.Add("Castable");
            }
            if (spell.Concentration)
            {
                tags.Add("Concentration");
            }
            if (a.Area != null)
            {
                tags.Add("Area");
            }
            if (a.Side == ActionSide.Ally)
            {
                tags.Add("Helps");
            }
            int cost = world.ActionCost(hero, a);
            // a cone or line starts at the caster, so it has no range of its own
            bool fromSelf = a.Target == ActionTarget.Self || (a.Area?.Directed ?? false);
            int range = fromSelf ? 0 : Math.Max(1, a.Range) * world.Rules.FeetPerSquare;
            rows.Add(new DataRow
            {
                Key = spell.Id,
                Cells = new[]
                {
                    a.Name,
                    spell.Spends.Count > 0 ? "F" : spell.Level == 0 ? "C" : spell.Level.ToString(),
                    cost.ToString(),
                    fromSelf ? "self" : range <= world.Rules.FeetPerSquare ? "touch" : $"{range} ft",
                    AreaText(world, a.Area),
                    status,
                },
                Sort = new IComparable?[] { a.Name, spell.Spends.Count > 0 ? 100 : spell.Level, cost, range, AreaText(world, a.Area), status },
                Tags = tags,
                Search = a.Description,
                Dim = !castable,
                Section = spell.Spends.Count > 0 ? "Focus" : LevelTab(spell.Level),
                Picture = ActionIcon.PictureOf(world, a.Id),
                // a prepared caster's spells waiting on the list say so in the corner
                Badge = c.Concentration.Spell == spell.Id ? "C" : sheet.Preparable.Count > 0 && sheet.Prepared.Contains(spell.Id) ? "P" : "",
                Drag = known ? ActionSlot.ActionDrag + a.Id : "",
            });
        }
        foreach (ActionDefinition a in others)
        {
            int cost = world.ActionCost(hero, a);
            rows.Add(new DataRow
            {
                Key = ActionKey + a.Id,
                Cells = new[] { a.Name, "", cost.ToString(), "", AreaText(world, a.Area), "" },
                Tags = new HashSet<string> { "Actions" },
                Search = a.Description,
                Section = "Actions",
                Picture = ActionIcon.PictureOf(world, a.Id),
                Drag = ActionSlot.ActionDrag + a.Id,
            });
        }
        _view.SetRows(rows);
        ShowEntry(world, spells, heroTurn);
        _view.SetFoot(Foot(world, c));
    }

    // Everything on the hero's list: what they know, then what they could prepare, cantrips first.
    private static List<SpellDefinition> SpellsOf(World world, CharacterSheet sheet)
    {
        return sheet.Spells.Concat(sheet.Preparable).Distinct()
            .Select(world.FindSpell).OfType<SpellDefinition>()
            .OrderBy(s => s.Spends.Count > 0 ? 1 : s.Level == 0 ? 0 : 2).ThenBy(s => s.Level)
            .ToList();
    }

    private void ShowEntry(World world, List<SpellDefinition> spells, bool heroTurn)
    {
        WorldCreature c = world.Creatures[_hero];
        CharacterSheet sheet = c.Sheet;
        SpellDefinition? spell = spells.Find(s => s.Id == _view.Picked);
        if (_view.Picked.StartsWith(ActionKey) && world.FindAction(_view.Picked[ActionKey.Length..]) is ActionDefinition other)
        {
            int cost = world.ActionCost(_hero, other);
            var page = new BookPage().Title(other.Name).Sub("Action").Rule()
                .Stats(("Cost", cost == 1 ? "1 action" : $"{cost} actions"));
            if (other.Description.Length > 0)
            {
                page.Rule().Text(other.Description);
            }
            page.Gap().Note("Drag it onto a slot of the bar to use it from there.");
            _view.SetEntry(page.ToString(), Array.Empty<DataAction>(), "");
            return;
        }
        if (spell == null)
        {
            _view.SetEntry(new BookPage().Note($"{sheet.Name} has no spells.").ToString(), Array.Empty<DataAction>(), "");
            return;
        }
        var actions = new List<DataAction>();
        bool known = sheet.Spells.Contains(spell.Id);
        if (sheet.Preparable.Contains(spell.Id))
        {
            bool on = !sheet.Prepared.Contains(spell.Id);
            List<string> wanted = on ? sheet.Prepared.Append(spell.Id).ToList() : sheet.Prepared.Where(id => id != spell.Id).ToList();
            bool can = world.CanPrepare(_hero, wanted, out string why);
            actions.Add(new DataAction(on ? "prepare" : "unprepare", on ? "Prepare" : "Unprepare", can, why));
        }
        if (known && world.Fighting)
        {
            string why = "";
            bool mine = heroTurn && world.CurrentCreature == _hero;
            bool can = mine && world.CanUse(_hero, spell.Action, out why);
            actions.Add(new DataAction("aim", "Use now", can, !mine ? "Not their turn." : Sentence(why)));
        }
        else if (known)
        {
            List<int> targets = world.CastTargets(_hero, spell.Id);
            foreach (int target in targets)
            {
                actions.Add(new DataAction($"cast:{target}", $"Cast on {world.Creatures[target].Sheet.Name}"));
            }
            if (targets.Count == 0)
            {
                world.CanCast(_hero, spell.Id, _hero, out string why);
                actions.Add(new DataAction("cast", "Cast", false, why));
            }
        }
        _view.SetEntry(Page(world, _hero, spell), actions, "");
    }

    /// <summary>A spell's page: its level and kind, what it costs, where it goes, what it does, and why it can't be cast now.</summary>
    public static string Page(World world, int hero, SpellDefinition spell)
    {
        CharacterSheet sheet = world.Creatures[hero].Sheet;
        ActionDefinition a = spell.Action;
        string kind = spell.Spends.Count > 0 ? "Focus spell" : spell.Level == 0 ? "Cantrip" : $"Level {spell.Level} spell";
        if (spell.Concentration)
        {
            kind += ", concentration";
        }
        var page = new BookPage().Title(a.Name).Sub(kind).Rule();
        int cost = world.ActionCost(hero, a);
        string spends = spell.Spends.Count > 0 ? string.Join(", ", spell.Spends.Select(s => $"{s.Value} {SheetPage.Words(s.Key).ToLowerInvariant()}"))
            : spell.Level > 0 ? $"a level {spell.Level} slot{(world.SpellRules.Upcast ? " or higher" : "")}" : "nothing";
        page.Stats(("Cost", cost == 1 ? "1 action" : $"{cost} actions"), ("Hands", spell.Hands.ToString()));
        page.Stat("Spends", spends);
        string side = a.Side switch
        {
            ActionSide.Enemy => "an enemy",
            ActionSide.Ally => "an ally",
            _ => "anyone",
        };
        string target = a.Target switch
        {
            ActionTarget.Self => a.Area != null ? $"everyone around the caster ({side})" : "the caster",
            ActionTarget.Point => $"a square, hitting {(a.Side == ActionSide.Any ? "everyone" : side.Replace("an ", "") + "s")} in the area",
            _ => side,
        };
        page.Stat("Target", target);
        if (a.Target != ActionTarget.Self && !(a.Area?.Directed ?? false))
        {
            page.Stat("Range",a.Range <= 1 ? "touch" : $"{a.Range * world.Rules.FeetPerSquare} ft");
        }
        page.Stat("Area", AreaText(world, a.Area));
        if (a.Effect.Save.Ability.Length > 0)
        {
            page.Stat("Save", $"{a.Effect.Save.Ability.ToUpperInvariant()} against DC {sheet.DifficultyClass(world.Rules)}");
        }
        List<string> lines = BookPage.EffectLines(a.Effect);
        if (lines.Count > 0)
        {
            page.Gap().Heading("Effect");
            foreach (string line in lines)
            {
                page.Text(line + ".");
            }
        }
        if (a.Description.Length > 0)
        {
            page.Rule().Text(a.Description);
        }
        if (!sheet.Spells.Contains(spell.Id))
        {
            page.Gap().Note("Not prepared: it can't be cast until it is.");
        }
        else if (!Spellcasting.CanCast(sheet, spell, world.SpellRules, out string why))
        {
            page.Gap().Warn(Sentence(why));
        }
        return page.ToString();
    }

    private void Act(string id)
    {
        string[] parts = id.Split(':');
        string spell = _view.Picked;
        int number = parts.Length > 1 && int.TryParse(parts[1], out int n) ? n : _hero;
        SpellOrder? order = parts[0] switch
        {
            "prepare" or "unprepare" => new SpellOrder(SpellOrderKind.Prepare, _hero, spell, Prepared: Toggled(parts[0] == "prepare", spell)),
            "cast" => new SpellOrder(SpellOrderKind.Cast, _hero, spell, number),
            "aim" => new SpellOrder(SpellOrderKind.Aim, _hero, spell),
            _ => null,
        };
        if (order != null)
        {
            Ordered?.Invoke(order);
        }
    }

    // the list as it would be with this one added or taken off; the world checks it
    private List<string> Toggled(bool on, string spell)
    {
        return _prepared.Where(s => s != spell).Concat(on ? new[] { spell } : Array.Empty<string>()).ToList();
    }

    /// <summary>Says what the world refused, under the entry.</summary>
    public void Refused(string why)
    {
        _view.ShowWarning(why);
    }

    private static string Foot(World world, WorldCreature c)
    {
        CharacterSheet sheet = c.Sheet;
        var parts = new List<string>();
        var slots = new List<string>();
        for (int level = 1; level <= 9; level++)
        {
            if (sheet.Resources.TryGetValue(world.SpellRules.SlotPrefix + level, out Resource? slot) && slot.Max > 0)
            {
                slots.Add($"{Ordinal(level)} {slot.Current}/{slot.Max}");
            }
        }
        if (slots.Count > 0)
        {
            parts.Add("Slots " + string.Join("  ", slots));
        }
        string focusId = world.Rules.Roles.Focus;
        if (focusId.Length > 0 && sheet.Resources.TryGetValue(focusId, out Resource? focus))
        {
            string name = char.ToUpperInvariant(focusId[0]) + focusId[1..].Replace('-', ' ');
            parts.Add($"{name} {focus.Current}/{focus.Max}");
        }
        if (world.SpellRules.Hands == SpellHands.Free)
        {
            parts.Add($"Free hands {sheet.FreeHands} of {CharacterSheet.HandCount}");
        }
        if (sheet.Preparable.Count > 0)
        {
            parts.Add($"Prepared {sheet.Prepared.Count} of {sheet.PrepareLimit}" + (c.MayPrepare ? "" : " (after a long rest)"));
        }
        if (c.Concentration.Active)
        {
            parts.Add($"Holding {SpellName(world, c.Concentration.Spell)}");
        }
        return parts.Count > 0 ? string.Join("     ", parts) : $"{sheet.Name} casts no spells.";
    }

    private static string AreaText(World world, ActionArea? area)
    {
        if (area == null)
        {
            return "";
        }
        int feet = (int)Math.Round(area.Size * world.Rules.FeetPerSquare);
        return area.Shape switch
        {
            AreaShape.Burst => $"{feet} ft burst",
            AreaShape.Cone => $"{feet} ft cone",
            AreaShape.Line => $"{feet} ft line",
            _ => $"{feet} ft square",
        };
    }

    private static string LevelTab(int level) => level == 0 ? "Cantrips" : $"Level {level}";

    private static string Ordinal(int n) => n switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{n}th",
    };

    private static string SpellName(World world, string id) => world.FindSpell(id)?.Action.Name ?? id;

    private static string Sentence(string why) => why.Length == 0 ? "" : char.ToUpperInvariant(why[0]) + why[1..] + (why.EndsWith('.') ? "" : ".");
}
