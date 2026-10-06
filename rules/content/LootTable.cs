namespace Yorehold.Rules;

public record LootEntry(string Item, double Chance = 1, int Quantity = 1);

/// <summary>What is found on a creature or in a container besides what it plainly holds.</summary>
public class LootTable
{
    /// <summary>Dice or a number as text, in copper. Empty = none.</summary>
    public string Coins { get; init; } = "";
    public List<LootEntry> Items { get; init; } = new();

    public bool IsEmpty => Coins.Length == 0 && Items.Count == 0;

    public static LootTable Read(ContentNode node)
    {
        node.RequireObject("is an object with coins and items");
        node.Only("coins", "items");
        string coins = "";
        if (node.Get("coins") is ContentNode coinsNode)
        {
            if (coinsNode.IsWhole && coinsNode.AsInt() >= 0 && coinsNode.AsInt() <= 100000000)
            {
                coins = coinsNode.AsInt().ToString();
            }
            else if (coinsNode.IsString && DiceText.IsValid(coinsNode.AsText()))
            {
                coins = coinsNode.AsText();
            }
            else
            {
                throw coinsNode.Fail("is a number from 0 or dice like \"2d6\"");
            }
            if (coins == "0")
            {
                coins = "";
            }
        }

        var items = new List<LootEntry>();
        if (node.Get("items") is ContentNode list)
        {
            if (!list.IsArray || list.Count > 1000)
            {
                throw list.Fail("is a list of item ids or entries");
            }
            foreach (ContentNode entry in list.Items())
            {
                LootEntry item;
                if (entry.IsString)
                {
                    item = new LootEntry(entry.AsText());
                }
                else if (entry.IsObject)
                {
                    entry.Only("item", "chance", "quantity");
                    item = new LootEntry(entry.At("item").AsText(), entry.Number("chance", 1, 0, 1), entry.Int("quantity", 1, 1, 1000));
                }
                else
                {
                    throw entry.Fail("is an item id or an object with one");
                }
                if (item.Item.Length == 0 || item.Item.Length > 64)
                {
                    throw entry.Fail("is an item id");
                }
                items.Add(item);
            }
        }
        return new LootTable { Coins = coins, Items = items };
    }
}
