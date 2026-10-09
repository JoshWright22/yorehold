using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>The words on cards and tooltips, put together from what the rules say.</summary>
public static class HudText
{
    /// <summary>The conditions on a sheet that have a file, with how strong and how long.</summary>
    public static List<(ConditionDefinition Definition, ActiveCondition Active)> Conditions(World world, CharacterSheet sheet)
    {
        var found = new List<(ConditionDefinition, ActiveCondition)>();
        foreach (ActiveCondition active in sheet.Conditions)
        {
            if (world.Rules.Condition(active.Id) is ConditionDefinition definition)
            {
                found.Add((definition, active));
            }
        }
        return found;
    }

    /// <summary>A system's tracks as marks, one group per track, filled for each point left: "●●○  ●  ●  ○" (Fate's stress, then its consequences).</summary>
    public static string TrackBoxes(CharacterSheet sheet) =>
        string.Join("  ", sheet.Tracks.Where(t => t.Max > 0).Select(t => new string('●', Math.Max(0, t.Value)) + new string('○', Math.Max(0, t.Max - t.Value))));

    public static string Health(World world, CharacterSheet sheet)
    {
        if (sheet.Tracks.Count > 0)
        {
            // a system with tracks says each by name: "Stress 2/3, Mild consequence 1/1"
            string tracks = string.Join(", ", sheet.Tracks.Where(t => t.Max > 0).Select(t => $"{t.Name} {t.Value}/{t.Max}"));
            string id = world.Rules.Checks.Kind(CheckRules.Attack).DefenceId;
            return $"{tracks}    {world.Rules.DefenceName(id)} {sheet.Defence(world.Rules, id)}";
        }
        Ruleset rules = world.Rules;
        string text = $"{rules.Sheet.NameOf("hp")} {System.Math.Max(0, sheet.Hp)} / {sheet.MaxHp}";
        if (sheet.TempHp > 0)
        {
            text += $" (+{sheet.TempHp})";
        }
        string defence = rules.Checks.Kind(CheckRules.Attack).DefenceId;
        return $"{text}    {rules.DefenceName(defence)} {sheet.Defence(rules, defence)}";
    }

    /// <summary>One line per condition: its name, value and rounds left, then what its file says it does.</summary>
    public static string ConditionLines(World world, CharacterSheet sheet)
    {
        var text = new StringBuilder();
        foreach ((ConditionDefinition definition, ActiveCondition active) in Conditions(world, sheet))
        {
            if (text.Length > 0)
            {
                text.Append('\n');
            }
            text.Append(definition.Name);
            if (active.Value > 1)
            {
                text.Append(' ').Append(active.Value);
            }
            if (active.RoundsLeft > 0)
            {
                text.Append(active.RoundsLeft == 1 ? " (1 round)" : $" ({active.RoundsLeft} rounds)");
            }
            if (definition.Description.Length > 0)
            {
                text.Append(": ").Append(definition.Description);
            }
        }
        return text.ToString();
    }

    /// <summary>What an action costs and where it can be aimed, for its tooltip.</summary>
    public static string ActionMeta(World world, ActionDefinition action, int cost, int who = -1)
    {
        TurnWords words = world.Rules.Words;
        string price = TurnWords.Capital(action.CostsBonus ? words.Bonus : words.Cost(cost));
        if (action.EndsTurn)
        {
            price += ", ends the turn";
        }
        string range;
        if (action.Target == ActionTarget.Self)
        {
            range = "Self";
        }
        else
        {
            string side = action.Side switch
            {
                ActionSide.Enemy => "an enemy",
                ActionSide.Ally => "an ally",
                _ => "anyone",
            };
            if (action.Target == ActionTarget.Point)
            {
                side = "a square";
            }
            // an attack with the weapon in hand reaches as far as that weapon does
            int reach = who >= 0 ? world.RangeOf(who, action) : action.Range;
            int feet = world.Rules.FeetPerSquare;
            // a weapon with a long range reads as its normal and its long, "80/320 ft"
            ItemDefinition? weapon = who >= 0 && action.WeaponRange ? world.Creatures[who].Sheet.WeaponItem?.Definition : null;
            string far = weapon != null && weapon.LongRange > weapon.Range ? $"{weapon.Range * feet}/{weapon.LongRange * feet} ft" : $"{reach * feet} ft";
            range = reach <= 1 ? $"Reach, {side}" : $"{far}, {side}";
        }
        return $"Cost: {price}\nRange: {range}";
    }

    /// <summary>Whether an action makes an attack roll, so there is a chance to hit to show.</summary>
    public static bool RollsAttack(ActionDefinition action) => RollsAttack(action.Effect.Steps);

    private static bool RollsAttack(List<EffectStep> steps)
    {
        foreach (EffectStep step in steps)
        {
            if ((step.Kind == EffectKind.Roll && step.How == "attack") || RollsAttack(step.Steps))
            {
                return true;
            }
        }
        return false;
    }
}
