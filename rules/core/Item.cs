using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// One entry in an inventory: an item file's numbers, how many there are and whether it is worn
/// or held. It keeps its own copy of the file, so a character carries it into adventures that
/// don't have that file.
/// </summary>
public sealed class Item
{
    public Item(ItemDefinition definition, int quantity = -1)
    {
        Definition = definition;
        Quantity = quantity < 0 ? definition.Quantity : quantity;
        Value = definition.Value;
    }

    public ItemDefinition Definition { get; }
    public int Quantity { get; set; }
    public bool Equipped { get; set; }
    /// <summary>The slot it is held in instead of its file's own: a one-handed weapon held in the off hand. "" for its own.</summary>
    public string HeldIn { get; set; } = "";
    /// <summary>Copper. A merchant may ask another price than the file's.</summary>
    public int Value { get; set; }

    public string Id => Definition.Id;
    public string Name => Definition.Name;
    public string Slot => HeldIn.Length > 0 ? HeldIn : Definition.Slot;

    /// <summary>Whether it can be worn or held in that slot: its own, or a one-handed main-hand item in the off hand.</summary>
    public bool CanGoIn(string slot) => slot == Definition.Slot || (slot == "offHand" && Definition.Slot == "mainHand" && Definition.Hands <= 1);
    public int Hands => Definition.Hands;
    public double Weight => Definition.Weight;
    public bool Magic => Definition.Magic;
    public int Supplies => Definition.Supplies;
    public ActionDefinition? Use => Definition.Use;
    /// <summary>Worn or held in a hand ("mainHand", "offHand"): it needs hands free.</summary>
    public bool Held => Slot.EndsWith("Hand", StringComparison.Ordinal);

    public Item Copy()
    {
        return new Item(Definition, Quantity) { Equipped = Equipped, Value = Value, HeldIn = HeldIn };
    }

    /// <summary>The same shape the C++ client saved, so its library files read here and back.</summary>
    public JsonObject ToJson()
    {
        var modifiers = new JsonArray();
        foreach (Modifier m in Definition.Modifiers)
        {
            JsonObject entry = ContentParts.ModifierJson(m);
            modifiers.Add(entry);
        }
        var j = new JsonObject
        {
            ["id"] = Id,
            ["name"] = Name,
            ["slot"] = Definition.Slot,
            ["damage"] = Definition.Damage,
            ["attackAbility"] = Definition.AttackAbility,
            ["hands"] = Hands,
            ["weight"] = Weight,
            ["value"] = Value,
            ["quantity"] = Quantity,
            ["magic"] = Magic,
            ["equipped"] = Equipped,
            ["heldIn"] = HeldIn,
            ["modifiers"] = modifiers,
        };
        if (Definition.Description.Length > 0)
        {
            j["description"] = Definition.Description;
        }
        if (Definition.DamageType.Length > 0)
        {
            j["damageType"] = Definition.DamageType;
        }
        if (Definition.Traits.Count > 0)
        {
            j["traits"] = new JsonArray(Definition.Traits.Select(t => (JsonNode?)t).ToArray());
        }
        if (Supplies > 0)
        {
            j["supplies"] = Supplies;
        }
        if (Definition.UseJson.Length > 0)
        {
            j["use"] = JsonNode.Parse(Definition.UseJson);
        }
        return j;
    }

    /// <summary>An inventory entry: an item file's fields plus "equipped".</summary>
    public static Item Read(ContentNode node)
    {
        ItemDefinition definition = ItemDefinition.Read(node);
        return new Item(definition, node.Int("quantity", 1, 0, 1000000))
        {
            Equipped = node.Bool("equipped", false),
            Value = node.Int("value", 0, 0),
            HeldIn = node.Text("heldIn", "", 32),
        };
    }
}
