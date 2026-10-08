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
    public AbilityChoice? PickAbility(int me, bool spellsOnly = false)
    {
        AbilityChoice? best = null;
        foreach (ActionDefinition action in ActionsOf(me))
        {
            if (!CanUse(me, action, out _))
            {
                continue;
            }
            if (SpellOf(action) is not SpellDefinition spell)
            {
                // not a spell: a guard (Raise a Shield, Dodge) by the harm it saves, or a trick aimed
                // at someone that rolls no attack (Demoralize) by what it does to them
                // a guard only once a foe is beside it: from across the room it walks in instead
                bool engaged = Foes(me).Any(f => Adjacent(me, f));
                float guard = spellsOnly || !engaged ? 0 : GuardWorth(me, action);
                if (guard > 0.5f && (best == null || guard > best.Value))
                {
                    best = new AbilityChoice(action, me, null, guard);
                }
                if (!spellsOnly && action.Target == ActionTarget.Creature && action.Id != StrikeAction)
                {
                    foreach ((int? target, Cell? _) in Aims(me, action))
                    {
                        if (target is not int at || at == me || AttackWorth(me, action, at) > 0)
                        {
                            continue;
                        }
                        float trick = WorthOn(me, action, at) / Math.Max(1, ActionCost(me, action));
                        if (trick > 0.5f && (best == null || trick > best.Value))
                        {
                            best = new AbilityChoice(action, at, null, trick);
                        }
                    }
                }
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

    /// <summary>
    /// What an action that puts a condition on its doer is worth as a guard, per action: the
    /// damage foes who can get to it this round are expected to deal now, less what they would
    /// with the condition on, counted by the system's own odds (a shield's +2 AC, Dodge's
    /// disadvantage). 0 for anything else, or when it has the condition already.
    /// </summary>
    public float GuardWorth(int me, ActionDefinition action)
    {
        CharacterSheet sheet = Creatures[me].Sheet;
        if (action.Target != ActionTarget.Self || action.EndsTurn || action.Id == StrikeAction || action.Id == EndTurnAction)
        {
            return 0;
        }
        List<string> gained = action.Effect.Steps
            .Where(s => s.Kind == EffectKind.Condition && !s.Remove && Rules.Condition(s.Id) != null && !sheet.HasCondition(s.Id))
            .Select(s => s.Id).ToList();
        if (gained.Count == 0)
        {
            return 0;
        }
        CharacterSheet guarded = sheet.Copy();
        foreach (string id in gained)
        {
            guarded.AddCondition(Rules, id);
        }
        CheckKind attack = Rules.Checks.Kind(CheckRules.Attack);
        float saved = 0;
        foreach (int foe in Foes(me))
        {
            CharacterSheet other = Creatures[foe].Sheet;
            float distance = Grid.Distance(CellOf(me), CellOf(foe));
            float reach = distance <= 1.01f ? 1 : distance <= other.SpeedSquares(Rules) + 1.01f ? 0.5f : 0;
            if (reach == 0 || DiceExpression.Parse(other.DamageDice(Rules)) is not DiceExpression damage)
            {
                continue;
            }
            int bonus = other.AttackModifier(Rules);
            double now = attack.ExpectedDamage(attack.Odds(bonus, sheet.AttackDefence(Rules), other.AttackAdvantage(Rules, sheet)), damage, Rules.Checks.CriticalDamage);
            double then = attack.ExpectedDamage(attack.Odds(bonus, guarded.AttackDefence(Rules), other.AttackAdvantage(Rules, guarded)), damage, Rules.Checks.CriticalDamage);
            saved += reach * (float)Math.Max(0, now - then);
        }
        // guarding only puts the fight off; striking ends it, so a guard has to save clearly more
        const float PutsOff = 0.75f;
        return PutsOff * saved / Math.Max(1, ActionCost(me, action));
    }

    /// <summary>
    /// The attack it should make on target from where it stands: of every non-spell action it can
    /// use that rolls attacks at a creature (the system's strike, a monster's Multiattack, a
    /// granted strike), the one with the most expected damage per action. Null when none reaches.
    /// </summary>
    public ActionDefinition? BestAttack(int me, int target)
    {
        ActionDefinition? best = null;
        float bestWorth = 0;
        foreach (ActionDefinition action in ActionsOf(me))
        {
            if (action.Target != ActionTarget.Creature || SpellOf(action) != null || !CanUse(me, action, out _) || !ValidTarget(me, action, target))
            {
                continue;
            }
            float worth = AttackWorth(me, action, target) / Math.Max(1, ActionCost(me, action));
            // the system's own strike wins a tie, so nothing changes where it is the only attack
            if (worth > bestWorth + 0.01f || (action.Id == StrikeAction && worth >= bestWorth - 0.01f && worth > 0))
            {
                best = action;
                bestWorth = worth;
            }
        }
        return best;
    }

    /// <summary>
    /// What an action's attack rolls at target are worth on average: each attack's expected
    /// damage by the system's odds and critical rule, repeats counted, extra damage dice under a
    /// hit added at the hit chance. 0 for an action that rolls no attack.
    /// </summary>
    public float AttackWorth(int me, ActionDefinition action, int target)
    {
        CharacterSheet sheet = Creatures[me].Sheet;
        CheckKind attack = Rules.Checks.Kind(CheckRules.Attack);
        Dictionary<string, double> odds = AttackOdds(me, target, action.Id);
        double hit = attack.Outcomes.Where(o => o.Passes).Sum(o => odds.GetValueOrDefault(o.Id));
        float worth = 0;
        void Walk(List<EffectStep> steps, int times, bool underAttack)
        {
            foreach (EffectStep step in steps)
            {
                if (step.Kind == EffectKind.Repeat)
                {
                    Walk(step.Steps, times * Math.Max(1, (int)Average(step.Amount)), underAttack);
                    continue;
                }
                if (step.Kind == EffectKind.Roll && step.How == "attack")
                {
                    Walk(step.Steps, times, true);
                    continue;
                }
                if (step.Kind == EffectKind.Damage && underAttack)
                {
                    DiceExpression? dice = DiceExpression.Parse(step.Amount == "weapon" ? sheet.DamageDice(Rules) : step.Amount);
                    if (dice != null)
                    {
                        // the weapon's damage gets the system's critical; extra dice count at the hit chance
                        worth += times * (step.Amount == "weapon"
                            ? (float)attack.ExpectedDamage(odds, dice, Rules.Checks.CriticalDamage)
                            : (float)(hit * Math.Max(0, dice.Average())));
                    }
                }
                Walk(step.Steps, times, underAttack);
            }
        }
        Walk(action.Effect.Steps, 1, false);
        return worth;
    }

    /// <summary>
    /// About the HP a condition on who is worth to me's side for a round, by the system's own
    /// odds: on a foe, the damage it then deals me less, the damage I then deal it more, and its
    /// whole turn when the condition stops it acting; on a friend, the same the other way round
    /// against its nearest foe. 0 when none of that changes (a condition only flags something).
    /// </summary>
    public float ConditionWorth(int me, int who, string condition)
    {
        if (Rules.Condition(condition) is not ConditionDefinition definition || me < 0 || who < 0)
        {
            return 0;
        }
        bool friend = Creatures[who].Team == Creatures[me].Team;
        int? other = friend ? Foes(me).OrderBy(f => Grid.Distance(CellOf(who), CellOf(f))).Cast<int?>().FirstOrDefault() : me;
        if (other is not int them)
        {
            return 0;
        }
        CharacterSheet before = Creatures[who].Sheet;
        CharacterSheet after = before.Copy();
        after.AddCondition(Rules, condition);
        CharacterSheet theirs = Creatures[them].Sheet;
        CheckKind attack = Rules.Checks.Kind(CheckRules.Attack);
        double Dealt(CharacterSheet by, CharacterSheet at) =>
            DiceExpression.Parse(by.DamageDice(Rules)) is DiceExpression dice
                ? attack.ExpectedDamage(attack.Odds(by.AttackModifier(Rules), at.AttackDefence(Rules), by.AttackAdvantage(Rules, at)), dice, Rules.Checks.CriticalDamage)
                : 0;
        // who deals them, and they deal who, before and after
        double outNow = Dealt(before, theirs), outThen = definition.HasFlag("cantAct") ? 0 : Dealt(after, theirs);
        double inNow = Dealt(theirs, before), inThen = Dealt(theirs, after);
        // for a friend: more out and less in is good; for a foe: less out (at us) and more in (from us)
        double worth = friend ? (outThen - outNow) + (inNow - inThen) : (outNow - outThen) + (inThen - inNow);
        return (float)worth;
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
            float swing = HitChance(me, foe) * Math.Max(1, damage);
            if (reach == 1 && BestAttack(me, foe) is ActionDefinition attack)
            {
                // in reach: what its best attack is worth by the system's odds, per action
                swing = Math.Max(swing, AttackWorth(me, attack, foe) / Math.Max(1, ActionCost(me, attack)));
            }
            best = Math.Max(best, reach * swing);
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
        if (PickAbility(me, spellsOnly: true) != null)
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
                        if (step.Remove || sheet.HasCondition(step.Id))
                        {
                            break;
                        }
                        // what it does under the system's odds, where that can be counted; else a plain guess
                        float counted = ConditionWorth(me, who, step.Id);
                        worth += (counted != 0 ? counted : friend == buffs ? 3 : -3) * odds;
                        break;
                    case EffectKind.Modifier:
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
