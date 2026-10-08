using System.Collections.Generic;
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

    public static string Health(World world, CharacterSheet sheet)
    {
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
    public static string ActionMeta(World world, ActionDefinition action, int cost)
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
            range = action.Range <= 1 ? $"Reach, {side}" : $"{action.Range * world.Rules.FeetPerSquare} ft, {side}";
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
