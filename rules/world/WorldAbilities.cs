namespace Yorehold.Rules;

/// <summary>A spell the AI would cast, where, and what it reckons it is worth this turn.</summary>
public sealed record AbilityChoice(ActionDefinition Action, int? Target, Cell? At, float Value);

// How the creatures the game plays use their spells: every one it could cast from where it stands,
// aimed every way that makes sense, scored by what it would likely do, against a plain strike.
public sealed partial class World
{
    /// <summary>
    /// The spell worth casting most from where the creature stands, or null when none does
    /// anything useful. Value is per action spent, in about the HP it is worth.
    /// </summary>
    public AbilityChoice? PickAbility(int me)
    {
        AbilityChoice? best = null;
        foreach (ActionDefinition action in ActionsOf(me))
        {
            if (SpellOf(action) is not SpellDefinition spell || !CanUse(me, action, out _))
            {
                continue;
            }
            foreach ((int? target, Cell? at) in Aims(me, action))
            {
                List<int> hit = action.Area != null ? CreaturesIn(me, action, at ?? CellOf(target ?? me)) : new List<int> { target ?? me };
                float value = hit.Sum(who => WorthOn(me, action, who));
                // slots are kept for when they matter, and a new concentration spell drops the old one
                value -= spell.Spends.Count > 0 ? 1 : spell.Level * 1.5f;
                if (spell.Concentration && Creatures[me].Concentration.Active)
                {
                    value -= 3;
                }
                value /= Math.Max(1, ActionCost(me, action));
                if (value > 0.5f && (best == null || value > best.Value))
                {
                    best = new AbilityChoice(action, target, at, value);
                }
            }
        }
        return best;
    }

    /// <summary>What a strike is worth this turn, per action: the chance to hit times the weapon's average, if someone is in reach or close.</summary>
    public float StrikeWorth(int me)
    {
        ActionDefinition? strike = FindAction(StrikeAction);
        if (strike == null || !CanUse(me, strike, out _))
        {
            return 0;
        }
        float best = 0;
        CharacterSheet sheet = Creatures[me].Sheet;
        float damage = Average(sheet.DamageDice(Rules));
        foreach (int foe in Foes(me))
        {
            float reach = Adjacent(me, foe) ? 1 : Grid.Distance(CellOf(me), CellOf(foe)) <= MovementLeft + 1.01f ? 0.9f : 0;
            best = Math.Max(best, reach * HitChance(me, foe) * Math.Max(1, damage));
        }
        return best;
    }

    /// <summary>
    /// A caster with nobody in reach of its spells: the nearest square it can walk to this turn
    /// with a foe in range and in sight of a spell worth more there than walking up and swinging.
    /// Null when one is in range already, or no spell is worth the walk.
    /// </summary>
    public Cell? ApproachToCast(int me)
    {
        if (PickAbility(me) != null)
        {
            return null;
        }
        CharacterSheet sheet = Creatures[me].Sheet;
        float swing = Math.Max(1, Average(sheet.DamageDice(Rules)));
        var aimed = new List<(ActionDefinition Action, int Foe)>();
        foreach (ActionDefinition action in ActionsOf(me))
        {
            if (SpellOf(action) is not SpellDefinition spell || action.Target == ActionTarget.Self || action.Side == ActionSide.Ally
                || action.Range <= 1 || !CanUse(me, action, out _))
            {
                continue;
            }
            foreach (int foe in Foes(me))
            {
                float worth = WorthOn(me, action, foe) - (spell.Spends.Count > 0 ? 1 : spell.Level * 1.5f);
                if (worth > 0.5f && worth > HitChance(me, foe) * swing)
                {
                    aimed.Add((action, foe));
                }
            }
        }
        if (aimed.Count == 0)
        {
            return null;
        }
        Cell? best = null;
        float bestCost = float.MaxValue;
        foreach ((Cell cell, float cost) in ReachableCells())
        {
            if (cost >= bestCost)
            {
                continue;
            }
            foreach ((ActionDefinition action, int foe) in aimed)
            {
                Cell there = CellOf(foe);
                if (Grid.Distance(cell, there) <= action.Range + 0.01f && Sight.LineOfSight(Grid.Center(cell), Grid.Center(there), Map.Walls))
                {
                    best = cell;
                    bestCost = cost;
                    break;
                }
            }
        }
        return best;
    }

    // Who and where an action could be aimed at from here.
    private IEnumerable<(int? Target, Cell? At)> Aims(int me, ActionDefinition action)
    {
        switch (action.Target)
        {
            case ActionTarget.Self:
                yield return (me, null);
                break;
            case ActionTarget.Creature:
                for (int t = 0; t < Creatures.Count; t++)
                {
                    if (ValidTarget(me, action, t))
                    {
                        yield return (t, null);
                    }
                }
                break;
            case ActionTarget.Point:
                // on each foe: a burst on them, a cone or line toward them
                foreach (int foe in Foes(me))
                {
                    if (ValidAim(me, action, CellOf(foe), out _))
                    {
                        yield return (null, CellOf(foe));
                    }
                }
                break;
        }
    }

    private IEnumerable<int> Foes(int me)
    {
        for (int i = 0; i < Creatures.Count; i++)
        {
            if (Creatures[i].Team != Creatures[me].Team && Creatures[i].Team != 2 && !Creatures[i].Sheet.Down && OrderIndex(i) is int index
                && !Encounter!.Order[index].Out)
            {
                yield return i;
            }
        }
    }

    // About the HP an action's steps are worth landing on who: damage to a foe is good, to a
    // friend bad; healing counts what is missing; conditions and modifiers help or hinder by side.
    private float WorthOn(int me, ActionDefinition action, int who)
    {
        bool friend = Creatures[who].Team == Creatures[me].Team;
        CharacterSheet sheet = Creatures[who].Sheet;
        float sure = action.Effect.Save.Ability.Length > 0 ? 0.6f : 1;
        bool buffs = action.Side == ActionSide.Ally;
        float worth = 0;
        void Walk(List<EffectStep> steps, float chance)
        {
            foreach (EffectStep step in steps)
            {
                if (step.Target is "self" && who != me)
                {
                    continue;
                }
                float odds = step.OnSave == OnSave.Half ? chance + (1 - chance) * 0.5f : chance;
                switch (step.Kind)
                {
                    case EffectKind.Damage:
                        float damage = Math.Min(Average(step.Amount), Math.Max(1, sheet.Hp + sheet.TempHp)) * odds;
                        worth += friend ? -1.5f * damage : damage;
                        break;
                    case EffectKind.Heal:
                        if (friend)
                        {
                            worth += Math.Min(Average(step.Amount), sheet.MaxHp - Math.Max(0, sheet.Hp)) + (sheet.Down ? 8 : 0);
                        }
                        break;
                    case EffectKind.TempHp:
                        worth += friend ? Average(step.Amount) * 0.5f : 0;
                        break;
                    case EffectKind.Condition:
                    case EffectKind.Modifier:
                        if (step.Kind == EffectKind.Condition && (step.Remove || sheet.HasCondition(step.Id)))
                        {
                            break;
                        }
                        worth += (friend == buffs ? 3 : -3) * odds;
                        break;
                    case EffectKind.Move:
                        worth += friend ? 0 : 1 * odds;
                        break;
                }
                Walk(step.Steps, step.Kind == EffectKind.Roll ? 0.6f : odds);
                foreach (EffectOption option in step.Options)
                {
                    Walk(option.Steps, odds);
                }
            }
        }
        Walk(action.Effect.Steps, sure);
        return worth;
    }

    private static float Average(string dice)
    {
        DiceExpression? parsed = DiceExpression.Parse(dice);
        return parsed == null ? 0 : (parsed.Minimum() + parsed.Maximum()) / 2f;
    }
}
