namespace Yorehold.Rules;

public enum SpellHands
{
    Free,
    Ignored,
}

public enum ConcentrationDamage
{
    Save,
    Breaks,
    Ignored,
}

/// <summary>spellcasting.json: how a ruleset casts. Every field is optional.</summary>
public class SpellRules
{
    public SpellHands Hands { get; init; }
    public string SlotPrefix { get; init; } = "slots-";
    public bool Upcast { get; init; } = true;
    public List<string> PrepareAfter { get; init; } = new();
    public ConcentrationDamage OnDamage { get; init; }
    public string SaveAbility { get; init; } = "con";
    public int MinimumDc { get; init; } = 10;
    public double DamageShare { get; init; } = 0.5;
    public bool EndsWhenDown { get; init; } = true;

    public static SpellRules Read(ContentNode node)
    {
        node.RequireObject("spell rules are a JSON object");
        node.Only("hands", "slotPrefix", "upcast", "prepareAfter", "concentration");
        SpellHands hands = node.Text("hands", "free") switch
        {
            "free" => SpellHands.Free,
            "ignored" => SpellHands.Ignored,
            _ => throw node.Fail("hands", "is \"free\" or \"ignored\""),
        };
        string prefix = node.Text("slotPrefix", "slots-");
        if (prefix.Length == 0 || prefix.Length > 60)
        {
            throw node.Fail("slotPrefix", "is a name of 1 to 60 characters");
        }
        var after = new List<string>();
        if (node.Get("prepareAfter") is ContentNode list && !list.IsNull)
        {
            if (!list.IsArray)
            {
                throw list.Fail("is a list of rest ids");
            }
            after = node.Texts("prepareAfter");
        }

        ConcentrationDamage onDamage = ConcentrationDamage.Save;
        string ability = "con";
        int minimumDc = 10;
        double share = 0.5;
        bool endsWhenDown = true;
        if (node.Get("concentration") is ContentNode c)
        {
            c.RequireObject("is an object");
            c.Only("onDamage", "ability", "minimumDc", "damageShare", "endsWhenDown");
            onDamage = c.Text("onDamage", "save") switch
            {
                "save" => ConcentrationDamage.Save,
                "breaks" => ConcentrationDamage.Breaks,
                "ignored" => ConcentrationDamage.Ignored,
                _ => throw c.Fail("onDamage", "is \"save\", \"breaks\" or \"ignored\""),
            };
            ability = c.Text("ability", "con", 64);
            minimumDc = c.Int("minimumDc", 10, -1000, 1000);
            share = c.Number("damageShare", 0.5, 0, 100);
            endsWhenDown = c.Bool("endsWhenDown", true);
        }
        return new SpellRules
        {
            Hands = hands,
            SlotPrefix = prefix,
            Upcast = node.Bool("upcast", true),
            PrepareAfter = after,
            OnDamage = onDamage,
            SaveAbility = ability,
            MinimumDc = minimumDc,
            DamageShare = share,
            EndsWhenDown = endsWhenDown,
        };
    }

    public void Check(Ruleset rules, string file)
    {
        // Characters are built with the ruleset's prefix, so the two have to agree.
        if (SlotPrefix != rules.Roles.SlotPrefix)
        {
            throw new ContentException(file, "slotPrefix", $"is \"{SlotPrefix}\" but the ruleset's roles.slotPrefix is \"{rules.Roles.SlotPrefix}\"");
        }
        if (OnDamage == ConcentrationDamage.Save && rules.Ability(SaveAbility) == null)
        {
            throw new ContentException(file, "concentration.ability", $"unknown ability \"{SaveAbility}\"");
        }
    }
}
