namespace Yorehold.Rules.Tests;

/// <summary>Small rulesets, sheets and a host for the rules tests.</summary>
public static class RulesTesting
{
    public static Ruleset Rules(string json)
    {
        return Ruleset.Read(TestContent.Json(json, "ruleset.json"));
    }

    public static Effect Effect(string json)
    {
        return Yorehold.Rules.Effect.Read(TestContent.Json(json));
    }

    /// <summary>Tens across the board, AC 12, 30 feet and 100 HP: every modifier is 0.</summary>
    public static CharacterSheet Plain(string name)
    {
        var sheet = new CharacterSheet { Name = name, Hp = 100 };
        foreach (string ability in new[] { "str", "dex", "con", "wis" })
        {
            sheet.Stats.SetBase(ability, 10);
        }
        sheet.Stats.SetBase("ac", 12);
        sheet.Stats.SetBase("speed", 30);
        sheet.Stats.SetBase("maxHp", 100);
        return sheet;
    }
}

/// <summary>Four creatures: 0 and 1 on one side, 2 and 3 on the other. Everything a step asks of the map is written down.</summary>
public sealed class TableHost : EffectHost
{
    public List<CharacterSheet> Sheets { get; } = new();
    public List<int> Area { get; set; } = new();
    public List<string> Calls { get; } = new();
    public string DamageType { get; private set; } = "";
    public bool CriticalDamage { get; private set; }
    public int Pick { get; set; }

    public TableHost()
    {
        foreach (string name in new[] { "Ana", "Bo", "Gik", "Mog" })
        {
            Sheets.Add(RulesTesting.Plain(name));
        }
    }

    public override CharacterSheet? Sheet(int who)
    {
        return who >= 0 && who < Sheets.Count ? Sheets[who] : null;
    }

    public override List<int> Group(string which, EffectContext context)
    {
        if (which == "area")
        {
            return Area;
        }
        return Enumerable.Range(0, Sheets.Count).Where(i => (i / 2 == context.Self / 2) == (which == "allies")).ToList();
    }

    public override int Damage(int who, int amount, string type, EffectContext context)
    {
        DamageType = type;
        CriticalDamage = context.CriticalDamage;
        return base.Damage(who, amount, type, context);
    }

    public override bool Move(int who, string how, int squares, EffectContext context)
    {
        Calls.Add($"move {who} {how} {squares}");
        return true;
    }

    public override bool Summon(string creature, int count, int rounds, EffectContext context)
    {
        Calls.Add($"summon {creature} {count} {rounds}");
        return true;
    }

    public override bool Light(int who, float radius, int rounds, EffectContext context)
    {
        Calls.Add($"light {who} {(int)radius} {rounds}");
        return true;
    }

    public override bool Surface(string id, float size, int rounds, EffectContext context)
    {
        Calls.Add($"surface {id} {(int)size} {rounds}");
        return true;
    }

    public override bool Flag(string name, bool set, EffectContext context)
    {
        Calls.Add((set ? "set " : "clear ") + name);
        return true;
    }

    public override int Choose(IReadOnlyList<string> options, EffectContext context) => Pick;
}

/// <summary>A host that supports nothing beyond one sheet.</summary>
public sealed class BareHost : EffectHost
{
    public CharacterSheet One { get; } = RulesTesting.Plain("Solo");

    public override CharacterSheet? Sheet(int who) => who == 0 ? One : null;
    public override List<int> Group(string which, EffectContext context) => new();
}
