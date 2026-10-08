namespace Yorehold.Rules;

public sealed class ActiveCondition
{
    public string Id { get; init; } = "";
    /// <summary>-1 = until something ends it.</summary>
    public int RoundsLeft { get; set; } = -1;
    /// <summary>How strongly, for conditions that stack by value (Frightened 2).</summary>
    public int Value { get; init; } = 1;
}

/// <summary>
/// What a sheet attacks with. Nothing held is an unarmed strike: 1 damage, on the system's attack
/// ability, as is a weapon whose ability is empty.
/// </summary>
public record Weapon(string Damage, string AttackAbility = "", int Hands = 1, IReadOnlyList<string>? Traits = null)
{
    public bool Has(string trait) => Traits?.Contains(trait) == true;
}

/// <summary>Where a creature at 0 HP stands with death saves, when the ruleset uses them.</summary>
public sealed class DeathState
{
    /// <summary>False: it dies as soon as it drops (most monsters).</summary>
    public bool Saves { get; set; } = true;
    public int Successes { get; set; }
    public int Failures { get; set; }
    public bool Stable { get; set; }
    public bool Dead { get; set; }
    /// <summary>With a dying track: how near death, and what earlier brushes with it left.</summary>
    public int Dying { get; set; }
    public int Wounded { get; set; }

    /// <summary>Back to no successes or failures, keeping whether it rolls saves at all and its wounds.</summary>
    public void Clear()
    {
        Successes = 0;
        Failures = 0;
        Dying = 0;
        Stable = false;
        Dead = false;
    }
}

/// <summary>
/// A creature's numbers while it plays: stats, HP, conditions, proficiencies and resources. It
/// holds raw data; whatever is derived (modifiers, AC, DCs) is worked out from it and a Ruleset
/// each time, so nothing goes stale.
/// </summary>
public sealed partial class CharacterSheet
{
    /// <summary>For AddCondition: last as long as the condition's own file says.</summary>
    public const int DefinedDuration = EffectStep.DefinedDuration;

    /// <summary>Hands a creature has; held items share them.</summary>
    public const int HandCount = 2;

    private Weapon? _weapon;

    public string Name { get; set; } = "";
    /// <summary>The race's name, for the sheet.</summary>
    public string Ancestry { get; set; } = "";
    /// <summary>The class names, "Fighter / Rogue" for a character with levels in both.</summary>
    public string ClassName { get; set; } = "";
    /// <summary>The first class's hit die, "1d10".</summary>
    public string HitDie { get; set; } = "";
    public string Notes { get; set; } = "";
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

    /// <summary>
    /// What it strikes with: the weapon held in the main hand, else one set by hand (tests, sheets
    /// without gear), else nothing, which is an unarmed strike.
    /// </summary>
    public Weapon? Weapon
    {
        get
        {
            Item? held = WeaponItem;
            return held != null ? new Weapon(held.Definition.Damage, held.Definition.AttackAbility, held.Hands, held.Definition.Traits) : _weapon;
        }
        set => _weapon = value;
    }

    /// <summary>The equipped main-hand item, if there is one.</summary>
    public Item? WeaponItem => Inventory.Find(i => i.Equipped && i.Slot == "mainHand");

    public DeathState Death { get; } = new();
    /// <summary>Experience so far; levels come from it (see AddXp).</summary>
    public int Xp { get; set; }

    /// <summary>Spell ids it can cast, in the order they are listed; the prepared ones are among them.</summary>
    public List<string> Spells { get; } = new();
    /// <summary>Spell ids a prepared caster may prepare; empty for anyone else.</summary>
    public List<string> Preparable { get; } = new();
    public int PrepareLimit { get; set; }
    public List<string> Prepared { get; } = new();
    /// <summary>The player's arrangement of their actions on the bars; a rebuild keeps it.</summary>
    public Hotbar Hotbar { get; private set; } = new();

    public List<Item> Inventory { get; } = new();
    /// <summary>In copper. Coins weigh nothing.</summary>
    public int Coins { get; set; }

    /// <summary>A separate sheet with the same numbers, for asking "what if" without touching this one.</summary>
    public CharacterSheet Copy()
    {
        var copy = new CharacterSheet
        {
            Name = Name, Ancestry = Ancestry, ClassName = ClassName, HitDie = HitDie, Notes = Notes,
            Level = Level, Hp = Hp, TempHp = TempHp, DcAbility = DcAbility, _weapon = _weapon, Xp = Xp,
            PrepareLimit = PrepareLimit, Coins = Coins,
        };
        copy.Spells.AddRange(Spells);
        copy.Preparable.AddRange(Preparable);
        copy.Prepared.AddRange(Prepared);
        copy.Inventory.AddRange(Inventory.Select(i => i.Copy()));
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

    /// <summary>Squares moved in a turn: the speed, less for someone carrying too much (see Encumbrance).</summary>
    public int SpeedSquares(Ruleset rules)
    {
        int squares = SpeedFeet / Math.Max(1, rules.FeetPerSquare);
        return Encumbrance(rules) switch
        {
            2 => 0,
            1 => squares > 0 ? Math.Max(1, (int)(squares * (float)rules.EncumberedSpeed)) : 0,
            _ => squares,
        };
    }

    /// <summary>Pounds carried, worn or not. Coins weigh nothing.</summary>
    public float CarriedWeight() => Inventory.Sum(i => (float)i.Weight * i.Quantity);

    /// <summary>The system's carrying ability (else its first) times the ruleset's pounds per point.</summary>
    public float CarryCapacity(Ruleset rules)
    {
        string strength = rules.Roles.CarryAbility.Length > 0 ? rules.Roles.CarryAbility
            : rules.Abilities.Count == 0 ? "" : rules.Abilities[0].Id;
        return AbilityScore(strength) * rules.CarryPerStrength;
    }

    /// <summary>How weighed down: 0 free, 1 slowed, 2 can't move, by the ruleset's shares of capacity.</summary>
    public int Encumbrance(Ruleset rules)
    {
        float capacity = CarryCapacity(rules);
        if (capacity <= 0)
        {
            return 0;
        }
        float share = CarriedWeight() / capacity;
        if (rules.ImmobileAt > 0 && share > rules.ImmobileAt)
        {
            return 2;
        }
        return rules.EncumberedAt > 0 && share > rules.EncumberedAt ? 1 : 0;
    }

    /// <summary>Magic items carried, worn or not, each of a stack.</summary>
    public int MagicItems() => Inventory.Where(i => i.Magic).Sum(i => Math.Max(1, i.Quantity));

    /// <summary>Whether more magic items still fit under the ruleset's limit (always, where it has none).</summary>
    public bool RoomForMagic(Ruleset rules, int more = 1) => rules.MagicItemLimit <= 0 || MagicItems() + more <= rules.MagicItemLimit;

    public int ArmorClass(Ruleset rules)
    {
        // "ac" holds armour: its base is the unarmoured AC, armour overrides it, shields add.
        int ability = rules.ArmorClassAbility.Length == 0 ? 0 : AbilityModifier(rules, rules.ArmorClassAbility);
        int proficiency = rules.ProficiencyRanks.Count == 0 ? 0 : ProficiencyModifier(rules, "armor");
        return Counted(rules, "armorClass", Stats.Integer("ac") + ability + proficiency,
            ("armor", Stats.Integer("ac")), ("ability", ability), ("proficiency", proficiency));
    }

    // A number the system has its own formula for, or the game's own count of it. The formula is
    // handed the names it is documented with and can read the rest of the sheet (SheetFormulas).
    private int Counted(Ruleset rules, string formula, int own, params (string Name, int Value)[] given)
    {
        if (rules.Formulas.Of(formula) is not Formula counted)
        {
            return own;
        }
        return counted.Whole(name =>
        {
            foreach ((string key, int value) in given)
            {
                if (key == name)
                {
                    return value;
                }
            }
            if (name == "level")
            {
                return Level;
            }
            int dot = name.IndexOf('.');
            if (dot < 0)
            {
                return null;
            }
            string of = name[(dot + 1)..];
            return name[..dot] switch
            {
                "mod" => AbilityModifier(rules, of),
                "score" => AbilityScore(of),
                "stat" => Stats.Integer(of),
                // "proficiency" itself can't ask for another proficiency: it would never end
                "prof" => formula == "proficiency" ? null : ProficiencyModifier(rules, of),
                "trait" => Weapon?.Has(of) == true ? 1 : 0,
                _ => null,
            };
        });
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
            bool proficient = Proficiencies.Contains(target);
            return Counted(rules, "proficiency", proficient ? rules.ProficiencyBonus(Level) : 0,
                ("rankBonus", 0), ("addsLevel", 0), ("proficient", proficient ? 1 : 0), ("tableBonus", rules.ProficiencyBonus(Level)));
        }
        ProficiencyRank? rank = rules.Rank(ProficiencyRank(rules, target));
        return Counted(rules, "proficiency", rules.ProficiencyBonus(Level, rank?.Id ?? ""),
            ("rankBonus", rank?.Bonus ?? 0), ("addsLevel", rank is { AddsLevel: true } ? 1 : 0),
            ("proficient", rank != null && rank.Id != rules.UntrainedRank ? 1 : 0), ("tableBonus", rules.ProficiencyBonus(Level)));
    }

    /// <summary>What its saves and checks are rolled against, with its own DC ability unless one is named.</summary>
    public int DifficultyClass(Ruleset rules, string ability = "")
    {
        string used = ability.Length == 0 ? DcAbility : ability;
        int modifier = rules.Ability(used) != null ? AbilityModifier(rules, used) : 0;
        int proficiency = ProficiencyModifier(rules, "dc");
        return Counted(rules, "dc", rules.BaseDc + modifier + proficiency + Stats.Integer("dc"),
            ("base", rules.BaseDc), ("ability", modifier), ("proficiency", proficiency), ("bonus", Stats.Integer("dc")));
    }

    /// <summary>For an ability or a skill; a skill adds its proficiency.</summary>
    public int CheckModifier(Ruleset rules, string abilityOrSkill)
    {
        SkillDefinition? skill = rules.Skill(abilityOrSkill);
        int ability = AbilityModifier(rules, skill?.Ability ?? abilityOrSkill);
        int proficiency = skill != null ? ProficiencyModifier(rules, skill.Id) : 0;
        return Counted(rules, "check", ability + proficiency, ("ability", ability), ("proficiency", proficiency));
    }

    /// <summary>A save by the system's save id (rolled with its ability) or by an ability.</summary>
    public int SaveModifier(Ruleset rules, string ability)
    {
        int modifier = AbilityModifier(rules, rules.SaveOf(ability)?.Ability ?? ability);
        int proficiency = ProficiencyModifier(rules, ability);
        return Counted(rules, "save", modifier + proficiency, ("ability", modifier), ("proficiency", proficiency));
    }

    /// <summary>What a check against this sheet is rolled to beat.</summary>
    public int PassiveScore(Ruleset rules, string abilityOrSkill)
    {
        int modifier = CheckModifier(rules, abilityOrSkill);
        return Counted(rules, "passive", rules.PassiveBase + modifier, ("base", rules.PassiveBase), ("modifier", modifier));
    }

    public int InitiativeModifier(Ruleset rules)
    {
        return rules.Roles.Initiative.Length == 0 ? 0 : CheckModifier(rules, rules.Roles.Initiative);
    }

    public RollResult RollCheck(Ruleset rules, string abilityOrSkill, Advantage advantage, Rng random)
    {
        return rules.Checks.Kind(CheckRules.Check).Roll(CheckModifier(rules, abilityOrSkill), advantage, random);
    }

    public RollResult RollSave(Ruleset rules, string ability, Advantage advantage, Rng random)
    {
        return rules.Checks.Kind(CheckRules.Save).Roll(SaveModifier(rules, ability), advantage, random);
    }

    /// <summary>The weapon's own ability, else the system's attack ability.</summary>
    public string AttackAbility(Ruleset rules)
    {
        return Weapon is { AttackAbility.Length: > 0 } weapon ? weapon.AttackAbility : rules.Roles.AttackAbility;
    }

    public int AttackModifier(Ruleset rules)
    {
        int ability = AbilityModifier(rules, AttackAbility(rules));
        int proficiency = ProficiencyModifier(rules, "weapons");
        return Counted(rules, "attack", ability + proficiency + Stats.Integer("attack"),
            ("ability", ability), ("proficiency", proficiency), ("bonus", Stats.Integer("attack")));
    }

    /// <summary>The weapon's dice with the ability and "damage" bonus on the end: "1d8+3".</summary>
    public string DamageDice(Ruleset rules)
    {
        int ability = AbilityModifier(rules, AttackAbility(rules));
        int bonus = Counted(rules, "damage", ability + Stats.Integer("damage"), ("ability", ability), ("bonus", Stats.Integer("damage")));
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

    /// <summary>
    /// Damage of a type after resistances, weaknesses and immunities: the sheet's "resist.&lt;type&gt;",
    /// "weak.&lt;type&gt;" and "immune.&lt;type&gt;" stats (and the ".all" ones), through the system's
    /// damageTaken formula, by default amount less resistance plus weakness, nothing when immune.
    /// </summary>
    public int DamageAfterDefences(Ruleset rules, int amount, string type)
    {
        if (type.Length == 0 || amount <= 0)
        {
            return amount;
        }
        int resist = Stats.Integer("resist." + type) + Stats.Integer("resist.all");
        int weak = Stats.Integer("weak." + type) + Stats.Integer("weak.all");
        int immune = Stats.Integer("immune." + type) + Stats.Integer("immune.all") > 0 ? 1 : 0;
        if (rules.Formulas.Of("damageTaken") is Formula own)
        {
            return Math.Max(0, own.Whole(name => name switch
            {
                "amount" => amount, "resist" => resist, "weak" => weak, "immune" => immune, _ => null,
            }));
        }
        return immune > 0 ? 0 : Math.Max(0, amount - resist + weak);
    }

    /// <summary>Damage with the ruleset's death rules: a hit on someone already down costs death saves.</summary>
    public bool TakeDamage(int amount, Ruleset rules, bool critical = false)
    {
        SyncDeath(rules);
        bool wasDown = Down;
        int harm = Math.Max(0, amount - TempHp);
        bool dropped = TakeDamage(amount);
        DeathRules rule = rules.Death;
        if (rule.Enabled && rule.Track is DyingTrack track && Death.Saves && !Death.Dead && Down && (harm > 0 || !wasDown))
        {
            // dropping starts the dying value; a hit while down raises it
            Func<string, double?> names = name => name switch
            {
                "dying" => Death.Dying,
                "wounded" => Death.Wounded,
                "critical" => critical ? 1 : 0,
                _ => null,
            };
            Death.Dying = wasDown && !Death.Stable ? Death.Dying + track.Damage.Whole(names) : track.Start.Whole(names);
            Death.Stable = false;
            Death.Dead = track.Dead.Whole(names) != 0;
            SyncDeath(rules);
            return dropped;
        }
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
        if (rule.Track != null && rule.DyingCondition.Length > 0 && HasCondition(rule.DyingCondition))
        {
            // the dying condition shows the value, as a track system writes it ("Dying 2")
            AddCondition(rules, rule.DyingCondition, value: Math.Max(1, Death.Dying));
        }
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
        if (rule.Track is DyingTrack track)
        {
            Func<string, double?> names = name => name switch { "dying" => Death.Dying, "wounded" => Death.Wounded, _ => null };
            int dc = track.Dc.Whole(names);
            CheckKind kind = rules.Checks.Kind(track.RollKind);
            RollResult roll = kind.Roll(0, Advantage.None, random);
            Death.Dying = Math.Max(0, Death.Dying + track.Change.GetValueOrDefault(kind.Resolve(roll, dc).Id));
            if (track.Dead.Whole(names) != 0)
            {
                Death.Dead = true;
            }
            else if (Death.Dying == 0)
            {
                Death.Stable = true;
                Death.Wounded += track.WoundedStep;
            }
            SyncDeath(rules);
            return roll;
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
                int bonus = rules.Roles.HpAbility.Length == 0 ? 0 : AbilityModifier(rules, rules.Roles.HpAbility) * dice;
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
                && rules.Checks.Passes(CheckRules.Save, RollSave(rules, definition.SaveAbility, Advantage.None, random), definition.SaveDc))
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

    // ---------------------------------------------------------------- gear

    public int HandsInUse()
    {
        return Inventory.Where(i => i.Equipped && i.Held).Sum(i => Math.Max(0, i.Hands));
    }

    public int FreeHands => HandCount - HandsInUse();

    /// <summary>
    /// Puts an item on, taking off whatever was in its slot. Held items share the hands: taking up
    /// something that needs more than are free puts the other held items away, the last listed
    /// first, so a greatsword leaves no hand for a shield.
    /// </summary>
    public bool Equip(int index)
    {
        if (index < 0 || index >= Inventory.Count || Inventory[index].Slot.Length == 0 || Inventory[index].Equipped)
        {
            return false;
        }
        Item item = Inventory[index];
        for (int i = 0; i < Inventory.Count; i++)
        {
            if (Inventory[i].Equipped && Inventory[i].Slot == item.Slot)
            {
                Unequip(i);
            }
        }
        if (item.Held)
        {
            for (int i = Inventory.Count - 1; i >= 0 && HandsInUse() + Math.Max(0, item.Hands) > HandCount; i--)
            {
                if (Inventory[i].Equipped && Inventory[i].Held)
                {
                    Unequip(i);
                }
            }
        }
        item.Equipped = true;
        foreach (Modifier modifier in item.Definition.Modifiers)
        {
            Stats.AddModifier(modifier, ItemSource(item, index));
        }
        return true;
    }

    public void Unequip(int index)
    {
        if (index < 0 || index >= Inventory.Count || !Inventory[index].Equipped)
        {
            return;
        }
        Inventory[index].Equipped = false;
        Stats.RemoveSource(ItemSource(Inventory[index], index));
    }

    /// <summary>
    /// Adds an item; one with a free slot is put on at once, so the first weapon listed stays in
    /// hand. Carried-only items stack with what is there already.
    /// </summary>
    public void GiveItem(ItemDefinition definition, int quantity = -1, bool wear = true)
    {
        var item = new Item(definition, quantity);
        if (item.Slot.Length == 0)
        {
            Item? same = Inventory.Find(i => i.Id == item.Id && i.Slot.Length == 0 && i.Value == item.Value);
            if (same != null)
            {
                same.Quantity += item.Quantity;
                return;
            }
        }
        Inventory.Add(item);
        if (wear && item.Slot.Length > 0 && !Inventory.Take(Inventory.Count - 1).Any(i => i.Equipped && i.Slot == item.Slot))
        {
            Equip(Inventory.Count - 1);
        }
    }

    /// <summary>Takes one unit of an item that isn't worn, keeping the worn items' modifiers right.</summary>
    public bool RemoveItem(int index)
    {
        if (index < 0 || index >= Inventory.Count || Inventory[index].Equipped || Inventory[index].Quantity < 1)
        {
            return false;
        }
        if (--Inventory[index].Quantity > 0)
        {
            return true;
        }
        TakeOut(index);
        return true;
    }

    /// <summary>Takes a whole entry out, worn or not, and hands it back unworn.</summary>
    public Item TakeOut(int index)
    {
        // modifier sources carry the index, so everything after it is taken off and put back on
        var worn = new List<bool>();
        for (int i = index; i < Inventory.Count; i++)
        {
            worn.Add(Inventory[i].Equipped);
            Unequip(i);
        }
        Item taken = Inventory[index];
        Inventory.RemoveAt(index);
        for (int i = 1; i < worn.Count; i++)
        {
            if (worn[i])
            {
                Equip(index + i - 1);
            }
        }
        Hp = Math.Min(Hp, MaxHp);
        return taken;
    }

    // ---------------------------------------------------------------- levels, resources and spells

    /// <summary>Adds experience; the level follows it up (never down).</summary>
    public void AddXp(Ruleset rules, int amount)
    {
        Xp += amount;
        Level = Math.Max(Level, rules.LevelForXp(Xp));
    }

    /// <summary>Refills resources by name: "*" is all of them, "slots-*" every one starting so. Returns the points that came back.</summary>
    public int RestoreResources(IEnumerable<string> names)
    {
        List<string> list = names.ToList();
        int restored = 0;
        foreach (string id in Resources.Keys.ToList())
        {
            Resource resource = Resources[id];
            bool named = list.Any(name => name.EndsWith('*') ? id.StartsWith(name[..^1], StringComparison.Ordinal) : id == name);
            if (!named || resource.Current >= resource.Max)
            {
                continue;
            }
            restored += resource.Max - resource.Current;
            Resources[id] = resource with { Current = resource.Max };
        }
        return restored;
    }

    /// <summary>
    /// A prepared caster makes ids its prepared spells: at least one, all preparable, no more than
    /// its limit. why says what is wrong when it can't.
    /// </summary>
    public bool Prepare(IReadOnlyList<string> ids, out string why)
    {
        why = "";
        if (ids.Count == 0)
        {
            why = "keep at least one spell prepared";
            return false;
        }
        if (ids.Count > PrepareLimit)
        {
            why = $"prepares at most {PrepareLimit} {(PrepareLimit == 1 ? "spell" : "spells")}";
            return false;
        }
        for (int i = 0; i < ids.Count; i++)
        {
            if (!Preparable.Contains(ids[i]))
            {
                why = $"can't prepare \"{ids[i]}\"";
                return false;
            }
            if (ids.Take(i).Contains(ids[i]))
            {
                why = $"\"{ids[i]}\" is listed twice";
                return false;
            }
        }
        // what it always knows never overlaps what it may prepare (see CharacterBuild)
        Spells.RemoveAll(Preparable.Contains);
        Spells.AddRange(ids);
        Prepared.Clear();
        Prepared.AddRange(ids);
        return true;
    }

    /// <summary>
    /// Takes everything the choices decide from a freshly built sheet and keeps what was lived
    /// through: HP lost, temporary HP, conditions, gear, death saves and resources spent. HP stays
    /// within the new maximum. Modifiers from "build:" sources (feats, features) are swapped for
    /// the new sheet's.
    /// </summary>
    public void AdoptBuild(CharacterSheet built)
    {
        Name = built.Name;
        Ancestry = built.Ancestry;
        ClassName = built.ClassName;
        Level = built.Level;
        Xp = Math.Max(Xp, built.Xp);
        HitDie = built.HitDie;
        Notes = built.Notes;
        foreach (KeyValuePair<string, float> stat in built.Stats.Bases)
        {
            Stats.SetBase(stat.Key, stat.Value);
        }
        foreach (string source in Stats.Modifiers.Select(m => m.Source).Where(s => s.StartsWith("build:", StringComparison.Ordinal)).Distinct().ToList())
        {
            Stats.RemoveSource(source);
        }
        foreach (AppliedModifier applied in built.Stats.Modifiers.Where(m => m.Source.StartsWith("build:", StringComparison.Ordinal)))
        {
            Stats.AddModifier(applied.Modifier, applied.Source);
        }
        Proficiencies.Clear();
        Proficiencies.UnionWith(built.Proficiencies);
        ProficiencyRanks.Clear();
        foreach (KeyValuePair<string, string> rank in built.ProficiencyRanks)
        {
            ProficiencyRanks[rank.Key] = rank.Value;
        }
        DcAbility = built.DcAbility;
        // prepared spells are the player's: keep those still on the list, up to the new count
        var keep = new List<string>();
        foreach (string id in Prepared)
        {
            if (keep.Count < built.PrepareLimit && built.Preparable.Contains(id) && !keep.Contains(id))
            {
                keep.Add(id);
            }
        }
        Preparable.Clear();
        Preparable.AddRange(built.Preparable);
        PrepareLimit = built.PrepareLimit;
        List<string> prepared = keep.Count == 0 ? built.Prepared.ToList() : keep;
        Prepared.Clear();
        Prepared.AddRange(prepared);
        Spells.Clear();
        Spells.AddRange(built.Spells.Where(id => !built.Prepared.Contains(id)));
        Spells.AddRange(Prepared);
        foreach (KeyValuePair<string, Resource> resource in built.Resources)
        {
            int current = Resources.TryGetValue(resource.Key, out Resource? live) ? Math.Clamp(live.Current, 0, resource.Value.Max) : resource.Value.Current;
            Resources[resource.Key] = new Resource(current, resource.Value.Max);
        }
        Hp = Math.Min(Hp, MaxHp);
    }

    private static string ItemSource(Item item, int index) => $"item:{item.Id}#{index}";

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
