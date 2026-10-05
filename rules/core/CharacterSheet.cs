namespace Yorehold.Rules;

public sealed class ActiveCondition
{
    public string Id { get; init; } = "";
    /// <summary>-1 = until something ends it.</summary>
    public int RoundsLeft { get; set; } = -1;
    /// <summary>How strongly, for conditions that stack by value (Frightened 2).</summary>
    public int Value { get; init; } = 1;
}

/// <summary>What a sheet attacks with. Nothing held is an unarmed strike: 1 damage, on strength.</summary>
public record Weapon(string Damage, string AttackAbility = "str", int Hands = 1);

/// <summary>Where a creature at 0 HP stands with death saves, when the ruleset uses them.</summary>
public sealed class DeathState
{
    /// <summary>False: it dies as soon as it drops (most monsters).</summary>
    public bool Saves { get; set; } = true;
    public int Successes { get; set; }
    public int Failures { get; set; }
    public bool Stable { get; set; }
    public bool Dead { get; set; }

    /// <summary>Back to no successes or failures, keeping whether it rolls saves at all.</summary>
    public void Clear()
    {
        Successes = 0;
        Failures = 0;
        Stable = false;
        Dead = false;
    }
}

/// <summary>
/// A creature's numbers while it plays: stats, HP, conditions, proficiencies and resources. It
/// holds raw data; whatever is derived (modifiers, AC, DCs) is worked out from it and a Ruleset
/// each time, so nothing goes stale.
/// </summary>
public sealed class CharacterSheet
{
    /// <summary>For AddCondition: last as long as the condition's own file says.</summary>
    public const int DefinedDuration = EffectStep.DefinedDuration;

    public string Name { get; set; } = "";
    public int Level { get; set; } = 1;
    /// <summary>Abilities by id, plus "maxHp", "ac", "speed" (feet), "attack", "damage" and "dc".</summary>
    public StatBlock Stats { get; } = new();
    public int Hp { get; set; }
    public int TempHp { get; set; }
    public SortedDictionary<string, Resource> Resources { get; } = new(StringComparer.Ordinal);
    public List<ActiveCondition> Conditions { get; } = new();
    /// <summary>Skill ids, ability ids (saves), "weapons", "armor", "dc".</summary>
    public HashSet<string> Proficiencies { get; } = new();
    /// <summary>The same targets with a rank each; a rank here wins over the plain list.</summary>
    public Dictionary<string, string> ProficiencyRanks { get; } = new();
    /// <summary>Empty: a DC with no ability bonus.</summary>
    public string DcAbility { get; set; } = "";
    public Weapon? Weapon { get; set; }
    public DeathState Death { get; } = new();
    /// <summary>Experience so far. Levels come from it once characters are built from choices (P7).</summary>
    public int Xp { get; set; }

    /// <summary>A separate sheet with the same numbers, for asking "what if" without touching this one.</summary>
    public CharacterSheet Copy()
    {
        var copy = new CharacterSheet
        {
            Name = Name, Level = Level, Hp = Hp, TempHp = TempHp, DcAbility = DcAbility, Weapon = Weapon, Xp = Xp,
        };
        foreach (KeyValuePair<string, float> stat in Stats.Bases)
        {
            copy.Stats.SetBase(stat.Key, stat.Value);
        }
        foreach (AppliedModifier applied in Stats.Modifiers)
        {
            copy.Stats.AddModifier(applied.Modifier, applied.Source);
        }
        foreach (KeyValuePair<string, Resource> resource in Resources)
        {
            copy.Resources[resource.Key] = resource.Value;
        }
        foreach (ActiveCondition active in Conditions)
        {
            copy.Conditions.Add(new ActiveCondition { Id = active.Id, RoundsLeft = active.RoundsLeft, Value = active.Value });
        }
        copy.Proficiencies.UnionWith(Proficiencies);
        foreach (KeyValuePair<string, string> rank in ProficiencyRanks)
        {
            copy.ProficiencyRanks[rank.Key] = rank.Value;
        }
        copy.Death.Saves = Death.Saves;
        copy.Death.Successes = Death.Successes;
        copy.Death.Failures = Death.Failures;
        copy.Death.Stable = Death.Stable;
        copy.Death.Dead = Death.Dead;
        return copy;
    }

    public int AbilityScore(string ability) => Stats.Integer(ability);
    public int AbilityModifier(Ruleset rules, string ability) => rules.AbilityModifier(AbilityScore(ability));
    public int MaxHp => Stats.Integer("maxHp");
    public int SpeedFeet => Stats.Integer("speed");
    public bool Down => Hp <= 0;

    public int SpeedSquares(Ruleset rules)
    {
        return SpeedFeet / Math.Max(1, rules.FeetPerSquare);
    }

    public int ArmorClass(Ruleset rules)
    {
        // "ac" holds armour: its base is the unarmoured AC, armour overrides it, shields add.
        int ability = rules.ArmorClassAbility.Length == 0 ? 0 : AbilityModifier(rules, rules.ArmorClassAbility);
        return Stats.Integer("ac") + ability + (rules.ProficiencyRanks.Count == 0 ? 0 : ProficiencyModifier(rules, "armor"));
    }

    public string ProficiencyRank(Ruleset rules, string target)
    {
        if (ProficiencyRanks.TryGetValue(target, out string? rank))
        {
            return rank;
        }
        return Proficiencies.Contains(target) ? rules.ProficientRank : rules.UntrainedRank;
    }

    public int ProficiencyModifier(Ruleset rules, string target)
    {
        // A ruleset without ranks uses its per-level table and ignores any ranks on the sheet.
        if (rules.ProficiencyRanks.Count == 0)
        {
            return Proficiencies.Contains(target) ? rules.ProficiencyBonus(Level) : 0;
        }
        return rules.ProficiencyBonus(Level, ProficiencyRank(rules, target));
    }

    /// <summary>What its saves and checks are rolled against, with its own DC ability unless one is named.</summary>
    public int DifficultyClass(Ruleset rules, string ability = "")
    {
        string used = ability.Length == 0 ? DcAbility : ability;
        int modifier = rules.Ability(used) != null ? AbilityModifier(rules, used) : 0;
        return rules.BaseDc + modifier + ProficiencyModifier(rules, "dc") + Stats.Integer("dc");
    }

    /// <summary>For an ability or a skill; a skill adds its proficiency.</summary>
    public int CheckModifier(Ruleset rules, string abilityOrSkill)
    {
        SkillDefinition? skill = rules.Skill(abilityOrSkill);
        if (skill != null)
        {
            return AbilityModifier(rules, skill.Ability) + ProficiencyModifier(rules, skill.Id);
        }
        return AbilityModifier(rules, abilityOrSkill);
    }

    public int SaveModifier(Ruleset rules, string ability)
    {
        return AbilityModifier(rules, ability) + ProficiencyModifier(rules, ability);
    }

    /// <summary>What a check against this sheet is rolled to beat.</summary>
    public int PassiveScore(Ruleset rules, string abilityOrSkill)
    {
        return rules.PassiveBase + CheckModifier(rules, abilityOrSkill);
    }

    public int InitiativeModifier(Ruleset rules)
    {
        return rules.InitiativeAbility.Length == 0 ? 0 : AbilityModifier(rules, rules.InitiativeAbility);
    }

    public RollResult RollCheck(Ruleset rules, string abilityOrSkill, Advantage advantage, Rng random)
    {
        return Dice.RollD20(CheckModifier(rules, abilityOrSkill), advantage, random);
    }

    public RollResult RollSave(Ruleset rules, string ability, Advantage advantage, Rng random)
    {
        return Dice.RollD20(SaveModifier(rules, ability), advantage, random);
    }

    public int AttackModifier(Ruleset rules)
    {
        return AbilityModifier(rules, Weapon?.AttackAbility ?? "str") + ProficiencyModifier(rules, "weapons") + Stats.Integer("attack");
    }

    /// <summary>The weapon's dice with the ability and "damage" bonus on the end: "1d8+3".</summary>
    public string DamageDice(Ruleset rules)
    {
        int bonus = AbilityModifier(rules, Weapon?.AttackAbility ?? "str") + Stats.Integer("damage");
        string dice = Weapon != null && Weapon.Damage.Length > 0 ? Weapon.Damage : "1";
        if (bonus != 0)
        {
            dice += (bonus > 0 ? "+" : "") + bonus;
        }
        return dice;
    }

    /// <summary>Conditions can force advantage or disadvantage on attacks; both together cancel out.</summary>
    public Advantage AttackAdvantage(Ruleset rules)
    {
        bool advantage = false;
        bool disadvantage = false;
        foreach (ActiveCondition active in Conditions)
        {
            ConditionDefinition? definition = rules.Condition(active.Id);
            if (definition != null)
            {
                advantage |= definition.AdvantageOnAttacks;
                disadvantage |= definition.DisadvantageOnAttacks;
            }
        }
        if (advantage == disadvantage)
        {
            return Advantage.None;
        }
        return advantage ? Advantage.Advantage : Advantage.Disadvantage;
    }

    /// <summary>Damage burns temporary HP first. True if this took it to 0.</summary>
    public bool TakeDamage(int amount)
    {
        amount = Math.Max(0, amount);
        bool wasUp = Hp > 0;
        int absorbed = Math.Min(TempHp, amount);
        TempHp -= absorbed;
        Hp = Math.Max(0, Hp - (amount - absorbed));
        return wasUp && Hp == 0;
    }

    /// <summary>Nothing ordinary heals the dead; anyone it gets up starts their death saves afresh.</summary>
    public void Heal(int amount)
    {
        if (Death.Dead)
        {
            return;
        }
        Hp = Math.Min(MaxHp, Hp + Math.Max(0, amount));
        if (Hp > 0)
        {
            Death.Clear();
        }
    }

    /// <summary>What a Strike costs: one action per hand the weapon needs when the ruleset says so.</summary>
    public int StrikeCost(Ruleset rules)
    {
        return rules.StrikeCostsHands && Weapon != null ? Math.Clamp(Weapon.Hands, 1, Math.Max(1, rules.ActionsPerTurn)) : 1;
    }

    /// <summary>Damage with the ruleset's death rules: a hit on someone already down costs death saves.</summary>
    public bool TakeDamage(int amount, Ruleset rules, bool critical = false)
    {
        SyncDeath(rules);
        bool wasDown = Down;
        int harm = Math.Max(0, amount - TempHp);
        bool dropped = TakeDamage(amount);
        DeathRules rule = rules.Death;
        if (rule.Enabled && wasDown && harm > 0 && Death.Saves && !Death.Dead)
        {
            if (Death.Stable)
            {
                Death.Successes = 0;
                Death.Failures = 0;
            }
            Death.Stable = false;
            Death.Failures = Math.Min(rule.Failures, Death.Failures + (critical ? rule.CriticalDamageFailures : rule.DamageFailures));
            Death.Dead = Death.Failures >= rule.Failures;
        }
        SyncDeath(rules);
        return dropped;
    }

    /// <summary>Puts HP, the death state and the ruleset's downed, dying, stable and dead conditions in agreement.</summary>
    public void SyncDeath(Ruleset rules)
    {
        DeathRules rule = rules.Death;
        if (!rule.Enabled)
        {
            return;
        }
        if (Death.Dead)
        {
            Hp = 0;
            Death.Stable = false;
        }
        if (Hp > 0)
        {
            Death.Clear();
        }
        else if (!Death.Saves)
        {
            Death.Dead = true;
            Death.Stable = false;
        }
        void Condition(string id, bool active)
        {
            if (id.Length == 0)
            {
                return;
            }
            if (active && !HasCondition(id))
            {
                AddCondition(rules, id);
            }
            else if (!active && HasCondition(id))
            {
                RemoveCondition(id);
            }
        }
        Condition(rule.DownedCondition, Down && !Death.Dead);
        Condition(rule.DyingCondition, Down && !Death.Dead && !Death.Stable);
        Condition(rule.StableCondition, Down && !Death.Dead && Death.Stable);
        Condition(rule.DeadCondition, Death.Dead);
    }

    /// <summary>A plain d20 for someone dying, when the ruleset has death saves. Null when there is nothing to roll.</summary>
    public RollResult? RollDeathSave(Ruleset rules, Rng random)
    {
        SyncDeath(rules);
        DeathRules rule = rules.Death;
        if (!rule.Enabled || !Down || !Death.Saves || Death.Stable || Death.Dead)
        {
            return null;
        }
        RollResult result = Dice.RollD20(0, Advantage.None, random);
        if (result.Natural20 && rule.NaturalTwentyHp > 0)
        {
            Heal(rule.NaturalTwentyHp);
        }
        else if (result.Natural1 || result.Total < rule.SaveDc)
        {
            Death.Failures = Math.Min(rule.Failures, Death.Failures + (result.Natural1 ? rule.NaturalOneFailures : 1));
            Death.Dead = Death.Failures >= rule.Failures;
        }
        else
        {
            Death.Successes = Math.Min(rule.Successes, Death.Successes + 1);
            Death.Stable = Death.Successes >= rule.Successes;
        }
        SyncDeath(rules);
        return result;
    }

    /// <summary>Brings the dead back with amount HP (0 or more than the most = full). False if it wasn't dead.</summary>
    public bool Revive(Ruleset rules, int amount)
    {
        if (!Death.Dead)
        {
            return false;
        }
        Death.Clear();
        Hp = amount <= 0 ? MaxHp : Math.Min(amount, MaxHp);
        Hp = Math.Max(Hp, 1);
        if (rules.Death.DeadCondition.Length > 0)
        {
            RemoveCondition(rules.Death.DeadCondition);
        }
        SyncDeath(rules);
        return true;
    }

    /// <summary>
    /// Healing after a win or a rest, as a Recovery says. The dead get nothing and the downed only
    /// when it revives them. Returns the HP gained.
    /// </summary>
    public int Recover(Ruleset rules, Recovery recovery, Rng random)
    {
        if (Death.Dead || (Down && !recovery.ReviveDowned))
        {
            return 0;
        }
        int before = Math.Max(0, Hp);
        int amount;
        switch (recovery.Kind)
        {
            case RecoveryKind.Full:
                amount = MaxHp;
                break;
            case RecoveryKind.Fraction:
                amount = (int)Math.Ceiling(MaxHp * recovery.Fraction);
                break;
            case RecoveryKind.Flat:
                amount = recovery.Amount;
                break;
            case RecoveryKind.HitDice:
            {
                // The class's own die comes with characters built from choices (P7).
                int dice = recovery.Amount > 0 ? recovery.Amount : Math.Max(1, Level);
                int bonus = rules.HitDieAbility.Length == 0 ? 0 : AbilityModifier(rules, rules.HitDieAbility) * dice;
                RollResult roll = Dice.Roll($"{dice}d{rules.DefaultHitDie}{(bonus < 0 ? "" : "+")}{bonus}", random);
                amount = Math.Max(1, roll.Total); // resting always helps a little
                break;
            }
            default:
                return 0;
        }
        Heal(amount);
        SyncDeath(rules);
        return Hp - before;
    }

    /// <summary>
    /// Applies a condition for a number of rounds (-1 = until something ends it; left out = the
    /// definition's own). One already there is refreshed, kept if it lasts longer, or raised by
    /// value, as its stacking says. Conditions it removes come off.
    /// </summary>
    public void AddCondition(Ruleset rules, string id, int rounds = DefinedDuration, int value = 1)
    {
        ConditionDefinition? definition = rules.Condition(id);
        if (rounds == DefinedDuration)
        {
            rounds = definition?.Duration ?? -1;
        }
        value = Math.Max(1, value);
        if (definition != null)
        {
            ActiveCondition? old = Conditions.Find(c => c.Id == id);
            if (old != null && definition.Stacking == ConditionStacking.Longest
                && (old.RoundsLeft < 0 || (rounds >= 0 && old.RoundsLeft > rounds)))
            {
                rounds = old.RoundsLeft;
            }
            value = definition.Stacking == ConditionStacking.Value ? Math.Min(definition.MaxValue, value + (old?.Value ?? 0)) : 1;
        }
        RemoveCondition(id); // whatever was there is replaced by what was just worked out
        Conditions.Add(new ActiveCondition { Id = id, RoundsLeft = rounds, Value = value });
        if (definition == null)
        {
            return;
        }
        AddConditionModifiers(definition, value);
        foreach (string other in definition.Removes)
        {
            if (other != id)
            {
                RemoveCondition(other);
            }
        }
    }

    public void RemoveCondition(string id)
    {
        Conditions.RemoveAll(c => c.Id == id);
        Stats.RemoveSource(ConditionSource(id));
    }

    /// <summary>
    /// A modifier of its own that lasts some rounds (-1 = until removed), tracked like a condition
    /// under the id: it counts down with the rounds and RemoveCondition(id) takes it off.
    /// </summary>
    public void AddModifier(string id, Modifier modifier, int rounds = -1)
    {
        ActiveCondition? old = Conditions.Find(c => c.Id == id);
        if (old != null)
        {
            old.RoundsLeft = rounds;
        }
        else
        {
            Conditions.Add(new ActiveCondition { Id = id, RoundsLeft = rounds });
        }
        Stats.AddModifier(modifier, ConditionSource(id));
    }

    public bool HasCondition(string id) => Conditions.Exists(c => c.Id == id);

    /// <summary>0 = doesn't have it.</summary>
    public int ConditionValue(string id) => Conditions.Find(c => c.Id == id)?.Value ?? 0;

    /// <summary>One of its conditions carries this flag ("cantAct", "cantMove", or any the content defines).</summary>
    public bool HasFlag(Ruleset rules, string flag)
    {
        return Conditions.Exists(c => rules.Condition(c.Id)?.HasFlag(flag) ?? false);
    }

    /// <summary>
    /// Something happened to it ("damage", "turnStart", "rest"...): every condition that ends on
    /// that comes off. Returns their ids.
    /// </summary>
    public List<string> ConditionEvent(Ruleset rules, string name)
    {
        List<string> ended = Conditions.Where(c => rules.Condition(c.Id)?.EndsOn(name) ?? false).Select(c => c.Id).ToList();
        foreach (string id in ended)
        {
            RemoveCondition(id);
        }
        return ended;
    }

    /// <summary>
    /// Once per round: counts down timed conditions, lets values decay and, given dice, rolls the
    /// saves that end conditions. Returns the ids that ended.
    /// </summary>
    public List<string> EndRound(Ruleset rules, Rng? random = null)
    {
        var ended = new List<string>();
        var weaker = new List<(string Id, int Value)>(); // conditions whose value dropped, and what is left
        foreach (ActiveCondition active in Conditions)
        {
            ConditionDefinition? definition = rules.Condition(active.Id);
            bool timedOut = false;
            if (active.RoundsLeft > 0)
            {
                active.RoundsLeft--;
                timedOut = active.RoundsLeft == 0;
            }
            if (timedOut)
            {
                ended.Add(active.Id);
            }
            else if (definition == null)
            {
                continue;
            }
            else if (definition.Decay > 0 && active.Value <= definition.Decay)
            {
                ended.Add(active.Id);
            }
            else if (definition.SaveAbility.Length > 0 && random != null
                && RollSave(rules, definition.SaveAbility, Advantage.None, random).Total >= definition.SaveDc)
            {
                ended.Add(active.Id);
            }
            else if (definition.Decay > 0)
            {
                weaker.Add((active.Id, active.Value - definition.Decay));
            }
        }
        foreach (string id in ended)
        {
            RemoveCondition(id);
        }
        foreach ((string id, int value) in weaker)
        {
            // Put back at its lower value with the time it had left, so its modifiers follow.
            ActiveCondition? found = Conditions.Find(c => c.Id == id);
            ConditionDefinition? definition = rules.Condition(id);
            if (found == null || definition == null)
            {
                continue;
            }
            RemoveCondition(id);
            Conditions.Add(new ActiveCondition { Id = id, RoundsLeft = found.RoundsLeft, Value = value });
            AddConditionModifiers(definition, value);
        }
        return ended;
    }

    /// <summary>Counts down timed conditions only, for sheets used without a ruleset.</summary>
    public void EndRound()
    {
        var expired = new List<string>();
        foreach (ActiveCondition active in Conditions)
        {
            if (active.RoundsLeft > 0 && --active.RoundsLeft == 0)
            {
                expired.Add(active.Id);
            }
        }
        foreach (string id in expired)
        {
            RemoveCondition(id);
        }
    }

    private static string ConditionSource(string id) => "condition:" + id;

    // A condition's modifiers at a value, tagged so removing the condition takes them off again.
    private void AddConditionModifiers(ConditionDefinition definition, int value)
    {
        foreach (Modifier modifier in definition.Modifiers)
        {
            Modifier applied = definition.PerValue && modifier.Op == ModifierOp.Add
                ? modifier with { Value = (float)modifier.Value * value }
                : modifier;
            Stats.AddModifier(applied, ConditionSource(definition.Id));
        }
    }
}
