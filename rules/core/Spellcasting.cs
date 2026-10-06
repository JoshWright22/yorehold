namespace Yorehold.Rules;

/// <summary>Slots, hands and what one casting costs. The C++ framework's Spell.h functions.</summary>
public static class Spellcasting
{
    /// <summary>
    /// The slot level a casting would spend: 0 for a cantrip or a spell that spends resources
    /// instead, else the lowest level from the spell's own that still has a slot (only its own
    /// without upcasting). wanted above 0 asks for that level exactly. Null if there is none.
    /// </summary>
    public static int? SlotFor(CharacterSheet caster, SpellDefinition spell, SpellRules rules, int wanted = 0)
    {
        if (spell.Level <= 0 || spell.Spends.Count > 0)
        {
            return 0;
        }
        bool Left(int level) => caster.Resources.TryGetValue(rules.SlotPrefix + level, out Resource? slot) && slot.Current > 0;
        if (wanted > 0)
        {
            return wanted >= spell.Level && (wanted == spell.Level || rules.Upcast) && Left(wanted) ? wanted : null;
        }
        int highest = rules.Upcast ? 20 : spell.Level;
        for (int level = spell.Level; level <= highest; level++)
        {
            if (Left(level))
            {
                return level;
            }
        }
        return null;
    }

    /// <summary>The caster has a slot (or what the spell spends) and, where the rules ask, the hands free. why is short: "needs 1 focus".</summary>
    public static bool CanCast(CharacterSheet caster, SpellDefinition spell, SpellRules rules, out string why)
    {
        why = "";
        if (rules.Hands == SpellHands.Free && caster.FreeHands < spell.Hands)
        {
            why = spell.Hands == 1 ? "needs a free hand" : $"needs {spell.Hands} free hands";
            return false;
        }
        foreach (KeyValuePair<string, int> spend in spell.Spends)
        {
            if (!caster.Resources.TryGetValue(spend.Key, out Resource? pool) || pool.Current < spend.Value)
            {
                why = $"needs {spend.Value} {spend.Key}";
                return false;
            }
        }
        if (SlotFor(caster, spell, rules) == null)
        {
            why = "no spell slot left";
            return false;
        }
        return true;
    }

    /// <summary>Spends one casting: the slot of that level (0 spends none) or the spell's resources. False with nothing spent if any is short.</summary>
    public static bool SpendCasting(CharacterSheet caster, SpellDefinition spell, SpellRules rules, int slot)
    {
        if (spell.Spends.Count > 0)
        {
            if (spell.Spends.Any(s => !caster.Resources.TryGetValue(s.Key, out Resource? pool) || pool.Current < s.Value))
            {
                return false;
            }
            foreach (KeyValuePair<string, int> spend in spell.Spends)
            {
                Resource pool = caster.Resources[spend.Key];
                caster.Resources[spend.Key] = pool with { Current = pool.Current - spend.Value };
            }
            return true;
        }
        return SpendSlot(caster, rules, slot);
    }

    /// <summary>Takes one slot of that level off the sheet. Level 0 spends nothing. False if there was none.</summary>
    public static bool SpendSlot(CharacterSheet caster, SpellRules rules, int slot)
    {
        if (slot <= 0)
        {
            return true;
        }
        string id = rules.SlotPrefix + slot;
        if (!caster.Resources.TryGetValue(id, out Resource? left) || left.Current <= 0)
        {
            return false;
        }
        caster.Resources[id] = left with { Current = left.Current - 1 };
        return true;
    }

    /// <summary>The save's DC on taking damage: the larger of the floor and a share of the damage, rounded down.</summary>
    public static int ConcentrationDc(SpellRules rules, int damage)
    {
        return Math.Max(rules.MinimumDc, (int)Math.Floor(Math.Max(0, damage) * rules.DamageShare));
    }

    /// <summary>The check a concentrating caster makes on taking damage.</summary>
    public static ConcentrationCheck CheckConcentration(CharacterSheet caster, Ruleset rules, SpellRules spells, int damage, Rng random)
    {
        var check = new ConcentrationCheck();
        if (damage <= 0 || spells.OnDamage == ConcentrationDamage.Ignored)
        {
            return check;
        }
        if (spells.OnDamage == ConcentrationDamage.Breaks)
        {
            check.Kept = false;
            return check;
        }
        check.Rolled = true;
        check.Dc = ConcentrationDc(spells, damage);
        check.Roll = caster.RollSave(rules, spells.SaveAbility, Advantage.None, random);
        check.Kept = check.Roll.Total >= check.Dc;
        return check;
    }
}

public sealed class ConcentrationCheck
{
    /// <summary>A save was made; Roll and Dc say how it went.</summary>
    public bool Rolled { get; set; }
    public bool Kept { get; set; } = true;
    public RollResult Roll { get; set; } = new();
    public int Dc { get; set; }
}

/// <summary>
/// What a caster holds in place by concentrating: the conditions and modifiers one casting left
/// on creatures. One spell at a time; it ends when another begins, when damage breaks it or when
/// nothing is left to hold.
/// </summary>
public sealed class Concentration
{
    public sealed record Hold(int Who, string Id);

    /// <summary>Empty when not concentrating.</summary>
    public string Spell { get; private set; } = "";
    public List<Hold> Holds { get; } = new();

    public bool Active => Spell.Length > 0;

    /// <summary>What a save kept.</summary>
    public static Concentration Restore(string spell, IEnumerable<Hold> holds)
    {
        var made = new Concentration { Spell = spell };
        made.Holds.AddRange(holds);
        return made;
    }

    /// <summary>Starts concentrating on what result left behind.</summary>
    public static Concentration Begin(string spell, EffectResult result)
    {
        var made = new Concentration { Spell = spell };
        foreach (EffectEvent e in result.Events)
        {
            string id = e.Kind == EffectEventKind.ConditionAdded ? e.Id : e.Kind == EffectEventKind.Modifier ? e.Tracked : "";
            var hold = new Hold(e.Who, id);
            if (id.Length > 0 && !made.Holds.Contains(hold))
            {
                made.Holds.Add(hold);
            }
        }
        return made;
    }

    /// <summary>Takes everything it held off the sheets and stops. Returns what came off.</summary>
    public List<Hold> End(Func<int, CharacterSheet?> sheets)
    {
        var removed = new List<Hold>();
        foreach (Hold hold in Holds)
        {
            if (sheets(hold.Who) is CharacterSheet sheet && sheet.HasCondition(hold.Id))
            {
                sheet.RemoveCondition(hold.Id);
                removed.Add(hold);
            }
        }
        Spell = "";
        Holds.Clear();
        return removed;
    }

    /// <summary>Forgets holds no longer on their sheets and stops once none are left. True while still concentrating.</summary>
    public bool Tidy(Func<int, CharacterSheet?> sheets)
    {
        Holds.RemoveAll(hold => sheets(hold.Who) is not CharacterSheet sheet || !sheet.HasCondition(hold.Id));
        if (Holds.Count == 0)
        {
            Spell = "";
        }
        return Active;
    }
}
