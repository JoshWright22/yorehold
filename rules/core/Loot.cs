namespace Yorehold.Rules;

/// <summary>What a roll on a loot table found: coins in copper, and item ids with how many.</summary>
public sealed class LootRoll
{
    public int Coins { get; set; }
    public List<(string Item, int Quantity)> Items { get; } = new();
}

/// <summary>Who has what: rolling loot tables and turning what they found into inventory entries.</summary>
public static class Loot
{
    public static LootRoll Roll(LootTable table, Rng random)
    {
        var found = new LootRoll();
        if (table.Coins.Length > 0)
        {
            found.Coins = Math.Max(0, Dice.Roll(table.Coins, random).Total);
        }
        foreach (LootEntry entry in table.Items)
        {
            // a certain entry takes no roll, so adding one doesn't move the dice of the others
            if (entry.Chance < 1 && random.Range(0, 9999) >= (int)((float)entry.Chance * 10000))
            {
                continue;
            }
            found.Items.Add((entry.Item, entry.Quantity));
        }
        return found;
    }

    /// <summary>Inventory entries for what a roll found, one per entry with its count. Ids the compendium lacks are skipped.</summary>
    public static List<Item> Items(Compendium compendium, IEnumerable<(string Item, int Quantity)> found)
    {
        var items = new List<Item>();
        foreach ((string id, int quantity) in found)
        {
            if (compendium.Item(id) is ItemDefinition definition)
            {
                items.Add(new Item(definition, quantity));
            }
        }
        return items;
    }

    /// <summary>
    /// Puts an item into an inventory unworn. Things only carried stack with the same thing
    /// already there; anything worn or held stays its own entry.
    /// </summary>
    public static void AddTo(CharacterSheet sheet, Item item)
    {
        item.Equipped = false;
        if (item.Slot.Length == 0)
        {
            Item? same = sheet.Inventory.Find(have => have.Slot.Length == 0 && have.Id == item.Id && have.Value == item.Value
                && have.Definition.Name == item.Definition.Name);
            if (same != null)
            {
                same.Quantity += item.Quantity;
                return;
            }
        }
        sheet.Inventory.Add(item);
    }
}
