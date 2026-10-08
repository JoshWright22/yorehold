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
    /// <summary>Attacks the doer made earlier this turn, for the ruleset's attack penalty.</summary>
    public int AttacksMade { get; set; }
    /// <summary>Set on the context handed to the host's Damage for a critical hit.</summary>
    public bool CriticalDamage { get; set; }
    /// <summary>Set on the context handed to the host's Damage when a step aims at one track.</summary>
    public string Track { get; set; } = "";
}

public enum EffectEventKind
{
    Attack,
    Check,
    /// <summary>The other side's roll in an opposed attack or contest; Amount is what it came to.</summary>
    Defence,
    /// <summary>A granted trigger went off (Id is its name).</summary>
    Triggered,
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
    /// <summary>A roll: which of the system's outcomes it was ("hit", "criticalFailure").</summary>
    public string Outcome { get; init; } = "";
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
    /// <summary>From a secret action: its dice aren't shown.</summary>
    public bool Secret { get; set; }
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
    public virtual int ArmorClass(int who, EffectContext context, string defence = "")
    {
        return Sheet(who)?.Defence(context.Rules, defence.Length > 0 ? defence : context.Rules.Checks.Kind(CheckRules.Attack).DefenceId) ?? 0;
    }

    public virtual bool HasFlag(int who, string flag, EffectContext context)
    {
        return Sheet(who)?.HasFlag(context.Rules, flag) ?? false;
    }

    /// <summary>Takes damage of a type off someone and returns what was dealt. A game with resistances overrides it.</summary>
    public virtual int Damage(int who, int amount, string type, EffectContext context)
    {
        CharacterSheet? sheet = Sheet(who);
        if (sheet == null)
        {
            return 0;
        }
        int dealt = sheet.DamageAfterDefences(context.Rules, amount, type);
        sheet.TakeDamage(dealt, context.Rules, context.CriticalDamage, context.Track);
        return dealt;
    }

    /// <summary>Spends (negative) or restores a resource. False if the sheet has none by that name.</summary>
    public virtual bool Resource(int who, string id, int change, EffectContext context)
    {
        CharacterSheet? sheet = Sheet(who);
        if (sheet != null && sheet.AdjustTrack(id, change))
        {
            // one of the system's tracks: Fate's recovering from a consequence
            return true;
        }
        if (sheet == null || !sheet.Resources.TryGetValue(id, out Resource? found))
        {
            return false;
        }
        sheet.Resources[id] = found with { Current = Math.Clamp(found.Current + change, 0, found.Max) };
        return true;
    }

    public virtual bool Move(int who, string how, int squares, EffectContext context) => false;
    /// <summary>Whether target stands within reach squares of who, for attacks that need it.</summary>
    public virtual bool InReach(int who, int target, int reach) => true;
    /// <summary>Squares between two creatures; 1 here, where nobody stands anywhere.</summary>
    public virtual double Distance(int a, int b) => 1;
    /// <summary>Conditions the place gives an attacker for one roll at target (unseen in the dark); none here.</summary>
    public virtual IEnumerable<string> PlaceConditions(int attacker, int target) => Enumerable.Empty<string>();
    /// <summary>An attack by attacker is about to hit who: true if who reacted (Shield), so the roll is read again.</summary>
    public virtual bool BeforeHit(int who, int attacker, EffectContext context, int margin = 0) => false;
    public virtual bool Summon(string creature, int count, int rounds, EffectContext context) => false;
    public virtual bool Light(int who, float radius, int rounds, EffectContext context) => false;
    public virtual bool Surface(string id, float size, int rounds, EffectContext context) => false;
    public virtual bool Flag(string name, bool set, EffectContext context) => false;

    /// <summary>Which of the named options a "choose" step takes.</summary>
    public virtual int Choose(IReadOnlyList<string> options, EffectContext context) => 0;
}
