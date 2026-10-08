namespace Yorehold.Rules;

/// <summary>
/// One of ruleset.json's "tracks": a count of something harm uses up, like 5e's hit points or
/// Fate's stress boxes and consequence slots. Damage runs through a system's tracks in the order
/// listed; a point of a track takes Absorbs of it; whatever no track takes puts the creature down.
/// A system without tracks has one, its HP.
/// </summary>
public sealed class TrackDefinition
{
    /// <summary>What Clears may name besides the ruleset's rests: the end of a fight.</summary>
    public const string FightEnd = "fightEnd";

    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    /// <summary>Points, from the creature's sheet names (level, mod.con, stat.stress...).</summary>
    public Formula Max { get; init; } = Formula.Parse("1", out _)!;
    /// <summary>Damage one point takes; a consequence slot is 1 point that takes 2, 4 or 6.</summary>
    public Formula Absorbs { get; init; } = Formula.Parse("1", out _)!;
    /// <summary>Healing fills it back up; false for a consequence, which only Clears ends.</summary>
    public bool Heals { get; init; } = true;
    /// <summary>Damage aimed at another track still reaches it (Fate Core's consequences take physical and mental harm).</summary>
    public bool Shared { get; init; }
    /// <summary>Events that fill it again: "fightEnd" or rest ids.</summary>
    public List<string> Clears { get; init; } = new();

    public static TrackDefinition Read(ContentNode node)
    {
        node.RequireObject("a track is an object with an id, a name and a max");
        node.Only("id", "name", "max", "absorbs", "heals", "shared", "clears");
        string id = node.At("id").AsId();
        return new TrackDefinition
        {
            Id = id,
            Name = node.Text("name", id, 64),
            Max = FormulaAt(node.At("max")),
            Absorbs = node.Get("absorbs") is ContentNode absorbs ? FormulaAt(absorbs) : Formula.Parse("1", out _)!,
            Heals = node.Bool("heals", true),
            Shared = node.Bool("shared", false),
            Clears = node.Names("clears"),
        };
    }

    private static Formula FormulaAt(ContentNode node)
    {
        string text = node.IsString ? node.AsText(2000) : node.AsInt().ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Formula.Parse(text, out string error) ?? throw node.Fail(error);
    }
}

/// <summary>A track on one sheet: its points now and what they were worked out to be.</summary>
public sealed class TrackSlot
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public int Max { get; init; }
    public int Absorbs { get; init; } = 1;
    public bool Heals { get; init; } = true;
    public bool Shared { get; init; }
    public List<string> Clears { get; init; } = new();
    public int Value { get; set; }

    /// <summary>The damage it can still take.</summary>
    public int Room => Value * Absorbs;
}
