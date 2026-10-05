namespace Yorehold.Rules;

/// <summary>How one run of an effect is set up: who does it, to whom, with which rules and dice.</summary>
public sealed record EffectContext(Ruleset Rules, Rng Random)
{
    /// <summary>Who does it, as the host numbers its creatures.</summary>
    public int Self { get; set; } = -1;
    /// <summary>Who it was aimed at.</summary>
    public List<int> Targets { get; set; } = new();
    /// <summary>What it is called, for modifiers that name no id.</summary>
    public string Source { get; set; } = "";
    /// <summary>Empty for something done now. With an event ("turnStart"...), only the steps waiting for it run.</summary>
    public string Event { get; set; } = "";
    /// <summary>0 = the level on the doer's sheet.</summary>
    public int Level { get; set; }
    /// <summary>The slot it was cast from, for steps that scale by slot.</summary>
    public int Slot { get; set; }
    /// <summary>The doer's DC, for saves and checks that ask for "caster".</summary>
    public int Dc { get; set; } = 10;
    /// <summary>Set on the context handed to the host's Damage for a critical hit.</summary>
    public bool CriticalDamage { get; set; }
}

public enum EffectEventKind
{
    Attack,
    Check,
    Save,
    Damage,
    Heal,
    TempHp,
    ConditionAdded,
    ConditionRemoved,
    /// <summary>One that was listening for what just happened (being hit, attacking, healed).</summary>
    ConditionEnded,
    Modifier,
    Move,
    Resource,
    Summon,
    Light,
    Surface,
    Flag,
    Choice,
}

/// <summary>One thing a step did, for the game to show.</summary>
public sealed class EffectEvent
{
    public EffectEventKind Kind { get; init; }
    /// <summary>Who it happened to (for a roll: who it was made against, or who made the save).</summary>
    public int Who { get; init; } = -1;
    public int By { get; init; } = -1;
    public RollResult Roll { get; init; } = new();
    /// <summary>Damage dealt, HP gained, the condition's value, the resource change, the choice made.</summary>
    public int Amount { get; init; }
    /// <summary>The armour class or DC rolled against.</summary>
    public int Dc { get; init; }
    public bool Success { get; init; }
    public bool Critical { get; init; }
    /// <summary>The damage took them to 0.</summary>
    public bool Dropped { get; init; }
    /// <summary>The damage type, condition, resource, flag, ability rolled...</summary>
    public string Id { get; init; } = "";
    /// <summary>A modifier: the name RemoveCondition takes it off by.</summary>
    public string Tracked { get; init; } = "";
}

public sealed class EffectResult
{
    public List<EffectEvent> Events { get; } = new();
}

/// <summary>
/// What an effect needs from the game. Sheets are changed directly; anything that touches the map
/// goes through here, and a host that leaves a hook alone simply does not support that step.
/// </summary>
public abstract class EffectHost
{
    /// <summary>A creature's sheet; null if there is none (the step skips it).</summary>
    public abstract CharacterSheet? Sheet(int who);

    /// <summary>Who "area", "allies" or "enemies" means for this effect.</summary>
    public abstract List<int> Group(string which, EffectContext context);

    /// <summary>What an attack has to reach. A game with flanking and cover adds them here.</summary>
    public virtual int ArmorClass(int who, EffectContext context)
    {
        return Sheet(who)?.ArmorClass(context.Rules) ?? 0;
    }

    public virtual bool HasFlag(int who, string flag, EffectContext context)
    {
        return Sheet(who)?.HasFlag(context.Rules, flag) ?? false;
    }

    /// <summary>Takes damage of a type off someone and returns what was dealt. A game with resistances overrides it.</summary>
    public virtual int Damage(int who, int amount, string type, EffectContext context)
    {
        Sheet(who)?.TakeDamage(amount);
        return amount;
    }

    /// <summary>Spends (negative) or restores a resource. False if the sheet has none by that name.</summary>
    public virtual bool Resource(int who, string id, int change, EffectContext context)
    {
        CharacterSheet? sheet = Sheet(who);
        if (sheet == null || !sheet.Resources.TryGetValue(id, out Resource? found))
        {
            return false;
        }
        sheet.Resources[id] = found with { Current = Math.Clamp(found.Current + change, 0, found.Max) };
        return true;
    }

    public virtual bool Move(int who, string how, int squares, EffectContext context) => false;
    public virtual bool Summon(string creature, int count, int rounds, EffectContext context) => false;
    public virtual bool Light(int who, float radius, int rounds, EffectContext context) => false;
    public virtual bool Surface(string id, float size, int rounds, EffectContext context) => false;
    public virtual bool Flag(string name, bool set, EffectContext context) => false;

    /// <summary>Which of the named options a "choose" step takes.</summary>
    public virtual int Choose(IReadOnlyList<string> options, EffectContext context) => 0;
}
