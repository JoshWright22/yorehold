using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// Items that belong to the party rather than one hero: the camp chest. Carried-only items stack
/// with the same thing already in it; whatever comes in is put away. Supplies for rests are
/// counted here and on the heroes' packs.
/// </summary>
public sealed class Stash
{
    public List<Item> Items { get; } = new();

    public bool Empty => Items.Count == 0;

    public void Clear() => Items.Clear();

    public void Add(Item item)
    {
        item.Equipped = false;
        if (item.Quantity < 1)
        {
            return;
        }
        if (item.Slot.Length == 0 && item.Use == null)
        {
            foreach (Item have in Items)
            {
                if (SameThing(have, item))
                {
                    have.Quantity += item.Quantity;
                    return;
                }
            }
        }
        Items.Add(item);
    }

    /// <summary>Takes count units of an entry, all of it when count is 0 or more than there are. Null when there is no such entry.</summary>
    public Item? Take(int index, int count = 1)
    {
        if (index < 0 || index >= Items.Count)
        {
            return null;
        }
        Item have = Items[index];
        if (count <= 0 || count >= have.Quantity)
        {
            Items.RemoveAt(index);
            return have;
        }
        Item part = have.Copy();
        part.Quantity = count;
        have.Quantity -= count;
        return part;
    }

    /// <summary>Supply points in the stash.</summary>
    public int Supplies => Items.Sum(i => i.Supplies * Math.Max(0, i.Quantity));

    public JsonObject ToJson()
    {
        return new JsonObject { ["items"] = new JsonArray(Items.Select(i => (JsonNode)i.ToJson()).ToArray()) };
    }

    public static Stash Read(ContentNode node)
    {
        node.RequireObject("a stash is {\"items\": [...]}");
        var stash = new Stash();
        foreach (ContentNode entry in node.Get("items")?.Items() ?? Array.Empty<ContentNode>())
        {
            Item item = Item.Read(entry);
            if (item.Id.Length == 0 || item.Quantity < 1)
            {
                throw entry.Fail("a stash item needs an id and a quantity of at least 1");
            }
            item.Equipped = false;
            stash.Items.Add(item);
        }
        return stash;
    }

    /// <summary>Supply points in the stash and the packs (what isn't worn).</summary>
    public static int SupplyPoints(Stash stash, IEnumerable<CharacterSheet> sheets)
    {
        return stash.Supplies + sheets.Sum(s => s.Inventory.Where(i => !i.Equipped).Sum(i => i.Supplies * Math.Max(0, i.Quantity)));
    }

    /// <summary>
    /// Uses up at least points supply points: the stash first, then each sheet in order, the least
    /// valuable units first. Units are whole, so a cost that doesn't divide evenly takes a little
    /// more. Nothing changes and it returns false if there aren't enough.
    /// </summary>
    public static bool SpendSupplies(Stash stash, IReadOnlyList<CharacterSheet> sheets, int points)
    {
        if (points <= 0)
        {
            return true;
        }
        if (SupplyPoints(stash, sheets) < points)
        {
            return false;
        }
        var plan = new List<(int Sheet, int Index)>();
        void PickFrom(List<Item> list, int sheet)
        {
            List<int> order = Enumerable.Range(0, list.Count)
                .Where(i => list[i].Supplies > 0 && !list[i].Equipped && list[i].Quantity > 0)
                .OrderBy(i => list[i].Supplies) // stable, like the C++ stable_sort
                .ToList();
            foreach (int i in order)
            {
                for (int n = 0; n < list[i].Quantity && points > 0; n++)
                {
                    plan.Add((sheet, i));
                    points -= list[i].Supplies;
                }
            }
        }
        PickFrom(stash.Items, -1);
        for (int s = 0; s < sheets.Count && points > 0; s++)
        {
            PickFrom(sheets[s].Inventory, s);
        }
        // from the back, so the indexes stay good
        foreach ((int sheet, int index) in plan.OrderBy(p => p.Sheet).ThenByDescending(p => p.Index))
        {
            if (sheet < 0)
            {
                stash.Take(index, 1);
            }
            else
            {
                sheets[sheet].RemoveItem(index);
            }
        }
        return true;
    }

    // Two entries are the same thing when everything but the count matches.
    private static bool SameThing(Item a, Item b)
    {
        Item x = a.Copy(), y = b.Copy();
        x.Quantity = y.Quantity = 1;
        x.Equipped = y.Equipped = false;
        return x.ToJson().ToJsonString() == y.ToJson().ToJsonString();
    }
}
