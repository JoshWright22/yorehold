namespace Yorehold.Rules;

/// <summary>
/// A shop as it stands in play: a finite purse, price multipliers and what is left in stock. It
/// buys at the item's value times the buy multiplier rounded up and pays the sell multiplier
/// rounded down, one item at a time. An item worth nothing has no price either way.
/// </summary>
public sealed class Merchant
{
    public int Coins { get; set; }
    public double BuyMultiplier { get; init; } = 1;
    public double SellMultiplier { get; init; } = 0.5;
    public List<Item> Inventory { get; } = new();

    public static Merchant From(MerchantDefinition definition, Compendium compendium)
    {
        var merchant = new Merchant { Coins = definition.Coins, BuyMultiplier = definition.BuyMultiplier, SellMultiplier = definition.SellMultiplier };
        foreach (MerchantStock stock in definition.Stock)
        {
            // MerchantDefinition.Read checked the ids
            merchant.Inventory.Add(new Item(compendium.Item(stock.Item)!, stock.Quantity) { Value = stock.Value });
        }
        return merchant;
    }

    /// <summary>What the merchant asks for one; -1 for no offer.</summary>
    public int BuyPrice(Item item) => Price(item, BuyMultiplier, true);

    /// <summary>What the merchant pays for one; -1 for no offer.</summary>
    public int SellPrice(Item item) => Price(item, SellMultiplier, false);

    public bool CanBuy(CharacterSheet buyer, int item, Ruleset rules, out string why)
    {
        why = "";
        if (item < 0 || item >= Inventory.Count || Inventory[item].Quantity <= 0)
        {
            why = "That item is out of stock.";
            return false;
        }
        int cost = BuyPrice(Inventory[item]);
        if (cost < 0)
        {
            why = "That item has no selling price.";
        }
        else if (buyer.Coins < cost)
        {
            why = "Not enough coins.";
        }
        else if (Coins < 0 || Coins > int.MaxValue - cost)
        {
            why = "The merchant's purse is full.";
        }
        else if (Inventory[item].Magic && !buyer.RoomForMagic(rules))
        {
            why = "Give up a magic item first.";
        }
        return why.Length == 0;
    }

    public bool CanSell(CharacterSheet seller, int item, out string why)
    {
        why = "";
        if (item < 0 || item >= seller.Inventory.Count || seller.Inventory[item].Quantity <= 0)
        {
            why = "That item is no longer carried.";
            return false;
        }
        int cost = SellPrice(seller.Inventory[item]);
        if (seller.Inventory[item].Equipped)
        {
            why = "Put that item away before selling it.";
        }
        else if (Inventory.Count >= 10000)
        {
            why = "The merchant has no room for more stock.";
        }
        else if (cost < 0)
        {
            why = "The merchant won't buy that item.";
        }
        else if (Coins < cost)
        {
            why = "The merchant hasn't enough coins.";
        }
        else if (seller.Coins < 0 || seller.Coins > int.MaxValue - cost)
        {
            why = "Your purse is full.";
        }
        return why.Length == 0;
    }

    public bool Buy(CharacterSheet buyer, int item, Ruleset rules)
    {
        if (!CanBuy(buyer, item, rules, out _))
        {
            return false;
        }
        Item stock = Inventory[item];
        int cost = BuyPrice(stock);
        Item bought = stock.Copy();
        bought.Quantity = 1;
        bought.Equipped = false;
        buyer.Inventory.Add(bought);
        if (--stock.Quantity == 0)
        {
            Inventory.RemoveAt(item);
        }
        buyer.Coins -= cost;
        Coins += cost;
        return true;
    }

    public bool Sell(CharacterSheet seller, int item)
    {
        if (!CanSell(seller, item, out _))
        {
            return false;
        }
        int cost = SellPrice(seller.Inventory[item]);
        Item sold = seller.Inventory[item].Copy();
        sold.Quantity = 1;
        sold.Equipped = false;
        Inventory.Add(sold);
        seller.RemoveItem(item);
        seller.Coins += cost;
        Coins -= cost;
        return true;
    }

    private static int Price(Item item, double multiplier, bool roundUp)
    {
        double value = item.Value * multiplier;
        if (item.Value <= 0 || !double.IsFinite(value) || value <= 0 || value > int.MaxValue)
        {
            return -1;
        }
        int result = (int)(roundUp ? Math.Ceiling(value) : Math.Floor(value));
        return result > 0 ? result : -1;
    }
}
