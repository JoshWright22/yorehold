namespace Yorehold.Rules;

public record MerchantStock(string Item, int Quantity, int Value);

/// <summary>What an NPC sells: a finite purse, price multipliers and stock.</summary>
public class MerchantDefinition
{
    /// <summary>Copper.</summary>
    public int Coins { get; init; }
    public double BuyMultiplier { get; init; } = 1;
    public double SellMultiplier { get; init; } = 0.5;
    public List<MerchantStock> Stock { get; init; } = new();

    public static MerchantDefinition Read(ContentNode node, Compendium compendium)
    {
        node.RequireObject("is an object");
        node.Only("coins", "buyMultiplier", "sellMultiplier", "stock");
        double buy = node.Number("buyMultiplier", 1);
        double sell = node.Number("sellMultiplier", 0.5);
        if (buy <= 0 || sell < 0 || sell > buy)
        {
            throw node.Fail("multipliers need 0 <= sellMultiplier <= buyMultiplier and buyMultiplier above 0");
        }
        var stock = new List<MerchantStock>();
        if (node.Get("stock") is ContentNode list)
        {
            if (!list.IsArray || list.Count > 10000)
            {
                throw list.Fail("is a list");
            }
            foreach (ContentNode row in list.Items())
            {
                row.RequireObject("stock entries are objects");
                row.Only("item", "quantity", "value");
                string id = row.At("item").AsText();
                ItemDefinition item = compendium.Item(id) ?? throw row.Fail("item", $"unknown item \"{id}\"");
                // Quantity and price default to the item file's.
                int quantity = row.Int("quantity", item.Quantity, 0, 1000000);
                if (quantity < 1)
                {
                    throw row.Fail("quantity", "is a whole number from 1 to 1000000");
                }
                stock.Add(new MerchantStock(id, quantity, row.Int("value", item.Value, 0)));
            }
        }
        return new MerchantDefinition { Coins = node.Int("coins", 0, 0), BuyMultiplier = buy, SellMultiplier = sell, Stock = stock };
    }
}

/// <summary>An NPC who can join the party, and how approval moves.</summary>
public class CompanionDefinition
{
    private const int Bound = 1000000; // far past any range a ruleset needs

    public string Id { get; init; } = "";
    /// <summary>Where approval starts.</summary>
    public int Approval { get; init; }
    /// <summary>The least approval they join with.</summary>
    public int JoinAt { get; init; }
    /// <summary>A member at or below it leaves. Null = only when sent away.</summary>
    public int? LeaveAt { get; init; }
    /// <summary>Story flag to approval change, counted once the first time the flag is set.</summary>
    public List<KeyValuePair<string, int>> Flags { get; init; } = new();

    public static CompanionDefinition Read(ContentNode node, string id)
    {
        node.RequireObject("is an object");
        var flags = new List<KeyValuePair<string, int>>();
        if (node.Get("flags") is ContentNode list)
        {
            foreach (KeyValuePair<string, ContentNode> member in list.RequireObject("is an object of flag: change").Members())
            {
                if (member.Key.Length == 0)
                {
                    throw member.Value.Fail("flags can't be empty");
                }
                flags.Add(new KeyValuePair<string, int>(member.Key, member.Value.AsInt()));
            }
        }
        return new CompanionDefinition
        {
            Id = id,
            Approval = node.Int("approval", 0, -Bound, Bound),
            JoinAt = node.Int("joinAt", 0, -Bound, Bound),
            LeaveAt = node.Get("leaveAt") is ContentNode leave && !leave.IsNull ? leave.AsInt(-Bound, Bound) : null,
            Flags = flags,
        };
    }
}

/// <summary>Someone the party can talk to.</summary>
public class ChapterNpc
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public ContentColor Color { get; init; } = new(200, 180, 140);
    public Cell At { get; init; }
    /// <summary>Content path of their conversation.</summary>
    public string Dialogue { get; init; } = "";
    /// <summary>Their sheet, from the compendium.</summary>
    public string Creature { get; init; } = "commoner";
    /// <summary>Story flags set when the party attacks them, and when they die.</summary>
    public List<string> Attacked { get; init; } = new();
    public List<string> Killed { get; init; } = new();
    public ContentNode? Ai { get; init; }
    public MerchantDefinition? Merchant { get; init; }
    public CompanionDefinition? Companion { get; init; }
}
