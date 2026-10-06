namespace Yorehold.Rules;

/// <summary>One surface file: rulesets/yorehold/surfaces/fire.json.</summary>
public class SurfaceDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public List<string> Effects { get; init; } = new();
    /// <summary>Rounds it stays; 0 = until something ends it.</summary>
    public int Duration { get; init; }
    public List<string> Ends { get; init; } = new();
    public bool IsSpellEffect { get; init; }

    // The shipped files also carry numbers the rules don't use yet (damage, slipping, what puts
    // it out). They are kept as written so nothing is lost when surfaces get their rules.
    public string DamagePerRound { get; init; } = "";
    public string DamageType { get; init; } = "";
    public string SaveAbility { get; init; } = "";
    public int SaveDc { get; init; } = 10;
    public string OnSave { get; init; } = "";
    public bool Slows { get; init; }
    public double Speed { get; init; } = 1;
    public bool Slips { get; init; }
    public string SlipAbility { get; init; } = "";
    public int SlipDc { get; init; } = 10;
    public List<string> Extinguishes { get; init; } = new();
    public List<string> ExtinguishedBy { get; init; } = new();

    public static SurfaceDefinition Read(ContentNode node)
    {
        node.RequireObject("a surface is a JSON object");
        string id = node.At("id").AsName();
        string damage = node.Text("damagePerRound", "");
        if (damage.Length > 0 && !DiceText.IsValid(damage))
        {
            throw node.Fail("damagePerRound", "is dice like \"1d6\"");
        }
        string onSave = node.Text("onSave", "");
        if (onSave.Length > 0 && onSave != "full" && onSave != "half" && onSave != "none")
        {
            throw node.Fail("onSave", "is \"full\", \"half\" or \"none\"");
        }
        string saveAbility = "";
        int saveDc = 10;
        if (node.Get("save") is ContentNode save)
        {
            save.RequireObject("needs an ability and a dc");
            saveAbility = save.At("ability").AsName();
            saveDc = save.Int("dc", 10, -1000, 1000);
        }
        return new SurfaceDefinition
        {
            Id = id,
            Name = node.Name("name", id),
            Description = node.Text("description", ""),
            Effects = node.Texts("effects"),
            Duration = node.Int("duration", 0, 0, 100000),
            Ends = node.Names("ends"),
            IsSpellEffect = node.Bool("isSpellEffect", false),
            DamagePerRound = damage,
            DamageType = node.Text("damageType", ""),
            SaveAbility = saveAbility,
            SaveDc = saveDc,
            OnSave = onSave,
            Slows = node.Bool("slows", false),
            Speed = node.Number("speed", 1, 0, 1),
            Slips = node.Bool("slips", false),
            SlipAbility = node.Text("slipAbility", ""),
            SlipDc = node.Int("slipDc", 10, -1000, 1000),
            Extinguishes = node.Names("extinguishes"),
            ExtinguishedBy = node.Names("extinguishedBy"),
        };
    }
}
