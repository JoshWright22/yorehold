namespace Yorehold.Rules;

/// <summary>One item file: items/longsword.json.</summary>
public class ItemDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>Where it is worn or held ("mainHand", "armor"...). Empty = it can only be carried.</summary>
    public string Slot { get; init; } = "";
    public string Damage { get; init; } = "";
    /// <summary>Empty = the system's attack ability (ruleset roles).</summary>
    public string AttackAbility { get; init; } = "";
    public int Hands { get; init; } = 1;
    /// <summary>A weapon's traits ("finesse", "agile"), which the system's formulas read as trait.&lt;name&gt;.</summary>
    public List<string> Traits { get; init; } = new();
    /// <summary>What a weapon's Strike deals ("slashing"), for resistances and weaknesses; empty = untyped.</summary>
    public string DamageType { get; init; } = "";
    /// <summary>Pounds.</summary>
    public double Weight { get; init; }
    /// <summary>Copper.</summary>
    public int Value { get; init; }
    public int Quantity { get; init; } = 1;
    public bool Magic { get; init; }
    /// <summary>Camp supply points each unit is worth; 0 = not food.</summary>
    public int Supplies { get; init; }
    public List<Modifier> Modifiers { get; init; } = new();
    /// <summary>What using it up does, for consumables.</summary>
    public ActionDefinition? Use { get; init; }
    /// <summary>The "use" object as written, so a carried item is saved the way it was read.</summary>
    public string UseJson { get; init; } = "";

    public static ItemDefinition Read(ContentNode node)
    {
        node.RequireObject("an item is a JSON object");
        string id = node.At("id").AsId();
        string name = node.Text("name", id);
        string slot = node.Text("slot", "");
        int hands = node.Int("hands", 1, 0, 4);
        string damage = node.Text("damage", "");
        if (damage.Length > 0 && !DiceText.IsValid(damage))
        {
            throw node.Fail("damage", $"bad damage dice \"{damage}\"");
        }

        ActionDefinition? use = null;
        if (node.Get("use") is ContentNode useNode)
        {
            if (!useNode.IsObject || slot.Length > 0)
            {
                throw useNode.Fail("needs an object on a carried-only item (one with no slot)");
            }
            use = ActionDefinition.Read(useNode, new ActionDefinition.Defaults(id, name, hands, General: false));
            if (use.Effect.IsEmpty || use.CostsHands || use.EndsTurn || use.Readies.Length > 0)
            {
                throw useNode.Fail("needs effects, a numeric cost, and cannot end a turn or ready an action");
            }
        }
        return new ItemDefinition
        {
            Id = id,
            Name = name,
            Description = node.Text("description", ""),
            Slot = slot,
            Damage = damage,
            AttackAbility = node.Text("attackAbility", ""),
            Traits = node.Names("traits"),
            DamageType = node.Text("damageType", "", 64),
            Hands = hands,
            Weight = node.Number("weight", 0, 0),
            Value = node.Int("value", 0),
            Quantity = node.Int("quantity", 1, 0),
            Magic = node.Bool("magic", false),
            Supplies = node.Int("supplies", 0, 0, 10000),
            Modifiers = ContentParts.ModifiersFrom(node, strict: false),
            Use = use,
            UseJson = node.Get("use") is ContentNode written ? written.Raw() : "",
        };
    }
}
