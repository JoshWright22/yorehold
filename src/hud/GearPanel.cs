using System;
using System.Collections.Generic;
using System.Linq;
using Yorehold.Rules;

namespace Yorehold;

public enum ItemOrderKind
{
    Equip,
    Unequip,
    Use,
    Give,
    Take,
    TakeCoins,
    TakeAll,
    Put,
    Buy,
    Sell,
}

/// <summary>Something the gear panel asks the world to do; the play screen does it.</summary>
public sealed record ItemOrder(ItemOrderKind Kind, int Hero, int Item = -1, int Target = -1, int Pile = -1, int Npc = -1);

/// <summary>
/// The gear panel (I): a hero's pack, or a pile or a shop beside them, as a data panel. Tabs are
/// the kinds of item, chips narrow it to what is worn, magic or usable, and the entry on the right
/// is the item's page with what can be done with it. It only reads the World; what is pressed goes
/// out as an ItemOrder.
/// </summary>
public sealed class GearPanel
{
    public const string Pack = "pack";
    private const string CoinsKey = "coins";

    private static readonly string[] Tabs = { "All", "Weapons", "Armour", "Usable", "Other" };
    private static readonly string[] Chips = { "Worn", "Magic", "Usable" };
    private static readonly DataColumn[] PackColumns =
    {
        new("Name", 150), new("Slot", 84), new("Qty", 34, true), new("Wt", 44, true), new("Value", 74, true),
    };
    // a chest or shop beside the pack: two narrow lists
    private static readonly DataColumn[] TradeColumns =
    {
        new("Name", 110), new("Qty", 30, true), new("Value", 70, true),
    };
    private const string Mine = "mine:";

    private readonly DataPanel _view;
    // what the rows stand for as of the last refresh: a row's key to its item's place in the list
    private readonly Dictionary<string, int> _places = new();
    // the same for the pack, when it is the list beside a chest or shop
    private readonly Dictionary<string, int> _mine = new();
    private int _hero;
    private int _pile = -1;
    private int _npc = -1;

    public GearPanel(DataPanel view)
    {
        _view = view;
        _view.ActionPressed += Act;
        _view.SourcePicked += picked => Source = picked;
    }

    public event Action<ItemOrder>? Ordered;

    /// <summary>"pack", "pile:2" or "shop:0".</summary>
    public string Source { get; set; } = Pack;

    public void Refresh(World world, int hero)
    {
        _hero = hero;
        CharacterSheet sheet = world.Creatures[hero].Sheet;
        var sources = new List<(string Id, string Label)> { (Pack, "Pack") };
        if (!world.Fighting)
        {
            for (int i = 0; i < world.Piles.Count; i++)
            {
                Pile pile = world.Piles[i];
                Cell at = world.CellOf(hero);
                // the open chest stays open once emptied, so things can still be put in it
                if ((!pile.Empty || Source == $"pile:{i}") && !world.PileLocked(i) && Math.Abs(pile.At.X - at.X) <= 1 && Math.Abs(pile.At.Y - at.Y) <= 1)
                {
                    sources.Add(($"pile:{i}", pile.Name));
                }
            }
            for (int npc = 0; npc < world.Merchants.Count; npc++)
            {
                if (world.CanTrade(hero, npc))
                {
                    sources.Add(($"shop:{npc}", world.Chapter.Npcs[npc].Name));
                }
            }
        }
        if (sources.All(s => s.Id != Source))
        {
            Source = Pack;
        }
        if (Source != Pack)
        {
            // the pack is the list beside the chest or shop, so it needs no button of its own
            sources.RemoveAt(0);
        }
        _view.SetSources(sources, Source);
        _view.SetTabs(Tabs);
        _view.SetChips(Chips);
        _pile = Source.StartsWith("pile:") ? int.Parse(Source[5..]) : -1;
        _npc = Source.StartsWith("shop:") ? int.Parse(Source[5..]) : -1;

        if (_npc >= 0)
        {
            Merchant shop = world.Merchants[_npc]!;
            string name = world.Chapter.Npcs[_npc].Name;
            _view.SetHead(name, $"buy from {name}, sell from {sheet.Name}'s pack");
            _view.SetColumns(TradeColumns);
            _view.SetRows(Rows(world, shop.Inventory, item => shop.BuyPrice(item), 0, _places, ""));
            _view.SetOther($"{sheet.Name}'s pack", "For sale", Rows(world, sheet.Inventory, item => shop.SellPrice(item), 0, _mine, Mine));
        }
        else if (_pile >= 0)
        {
            Pile pile = world.Piles[_pile];
            _view.SetHead(pile.Name, $"take from it, or put things from {sheet.Name}'s pack in");
            _view.SetColumns(TradeColumns);
            _view.SetRows(Rows(world, pile.Items, item => item.Value, pile.Coins, _places, ""));
            _view.SetOther($"{sheet.Name}'s pack", $"In {pile.Name}", Rows(world, sheet.Inventory, item => item.Value, 0, _mine, Mine));
        }
        else
        {
            _view.SetHead($"{sheet.Name}'s gear", world.Fighting ? "changing gear costs an action on their turn" : "");
            _view.SetColumns(PackColumns);
            _view.SetRows(Rows(world, sheet.Inventory, item => item.Value, 0, _places, ""));
            _view.SetOther("", "", null);
        }
        ShowEntry(world);
        _view.SetFoot(Foot(world, sheet));
    }

    // rows for a list of items; places gets each row's key to the item's place in the list.
    // prefix tells the pack's rows apart from a chest's when both are shown.
    private List<DataRow> Rows(World world, List<Item> items, Func<Item, int> price, int coins, Dictionary<string, int> places, string prefix)
    {
        places.Clear();
        bool trading = prefix.Length > 0 || _pile >= 0 || _npc >= 0;
        var rows = new List<DataRow>();
        var seen = new Dictionary<string, int>();
        for (int i = 0; i < items.Count; i++)
        {
            Item item = items[i];
            int n = seen.GetValueOrDefault(item.Id);
            seen[item.Id] = n + 1;
            string key = $"{prefix}{item.Id}#{n}";
            places[key] = i;
            int cost = price(item);
            string name = item.Name + (item.Equipped ? "  (worn)" : "");
            rows.Add(new DataRow
            {
                Key = key,
                // side by side there is room for what a trade needs: what, how many, worth
                Cells = trading
                    ? new[] { name, item.Quantity.ToString(), cost > 0 ? Coins.Text(cost) : "-" }
                    : new[] { name, SlotName(item), item.Quantity.ToString(), Pounds(item.Weight * item.Quantity), cost > 0 ? Coins.Text(cost) : "-" },
                Sort = trading
                    ? new IComparable?[] { item.Name, item.Quantity, cost }
                    : new IComparable?[] { item.Name, SlotName(item), item.Quantity, item.Weight * item.Quantity, cost },
                Tags = TagsOf(item),
                Search = item.Definition.Description,
            });
        }
        if (coins > 0)
        {
            places[CoinsKey] = -1;
            rows.Insert(0, new DataRow
            {
                Key = CoinsKey,
                Cells = new[] { "Coins", "", Coins.Text(coins) },
                Sort = new IComparable?[] { "", 0, coins },
                Tags = new HashSet<string> { "Other" },
            });
        }
        return rows;
    }

    private void ShowEntry(World world)
    {
        CharacterSheet sheet = world.Creatures[_hero].Sheet;
        if (_mine.TryGetValue(_view.Picked, out int mine))
        {
            Item own = sheet.Inventory[mine];
            var first = new List<DataAction>();
            if (_pile >= 0)
            {
                first.Add(new DataAction("put", $"Put in {world.Piles[_pile].Name}"));
            }
            else if (_npc >= 0)
            {
                Merchant shop = world.Merchants[_npc]!;
                bool can = shop.CanSell(sheet, mine, out string why);
                first.Add(new DataAction($"sell:{_npc}", $"Sell for {Coins.Text(Math.Max(0, shop.SellPrice(own)))}", can, why));
            }
            // the rest of what the pack's own page offers, but selling elsewhere is for the shop's own panel
            first.AddRange(PackActions(world, sheet, mine, own).Where(a => !a.Id.StartsWith("sell:")));
            _view.SetEntry(Page(world, own, own.Value), first, "");
            return;
        }
        if (!_places.TryGetValue(_view.Picked, out int index))
        {
            string empty = _npc >= 0 ? "Sold out." : _pile >= 0 ? "Nothing left." : $"{sheet.Name} carries nothing.";
            _view.SetEntry(new BookPage().Note(empty).ToString(), Array.Empty<DataAction>(), "");
            return;
        }
        var actions = new List<DataAction>();
        if (index < 0)
        {
            int coins = world.Piles[_pile].Coins;
            _view.SetEntry(new BookPage().Title("Coins").Sub(Coins.Text(coins)).Rule().Text("Coins weigh nothing.").ToString(),
                new[] { new DataAction("takecoins", "Take coins"), new DataAction("takeall", "Take all") }, "");
            return;
        }

        Item item;
        if (_npc >= 0)
        {
            Merchant shop = world.Merchants[_npc]!;
            item = shop.Inventory[index];
            bool can = shop.CanBuy(sheet, index, world.Rules, out string why);
            actions.Add(new DataAction("buy", $"Buy for {Coins.Text(Math.Max(0, shop.BuyPrice(item)))}", can, why));
        }
        else if (_pile >= 0)
        {
            item = world.Piles[_pile].Items[index];
            bool room = !item.Magic || sheet.RoomForMagic(world.Rules, item.Quantity);
            actions.Add(new DataAction("take", "Take", room, room ? "" : world.MagicLimitText(_hero)));
            actions.Add(new DataAction("takeall", "Take all"));
        }
        else
        {
            item = sheet.Inventory[index];
            actions.AddRange(PackActions(world, sheet, index, item));
        }
        _view.SetEntry(Page(world, item, _npc >= 0 ? world.Merchants[_npc]!.BuyPrice(item) : item.Value), actions, "");
    }

    // what can be done with an item in the hero's own pack: wear it, use it, hand it on or sell it
    private List<DataAction> PackActions(World world, CharacterSheet sheet, int index, Item item)
    {
        var actions = new List<DataAction>();
        if (item.Slot.Length > 0)
        {
            bool on = !item.Equipped;
            bool can = world.CanEquip(_hero, index, on, out string why);
            string label = on ? (item.Held ? "Take up" : "Put on") : (item.Held ? "Put away" : "Take off");
            actions.Add(new DataAction(on ? "equip" : "unequip", label, can, why.Length > 0 ? why : "Not now."));
        }
        if (item.Use != null)
        {
            List<int> targets = world.ConsumeTargets(_hero, index);
            foreach (int target in targets)
            {
                actions.Add(new DataAction($"use:{target}", target == _hero ? "Use" : $"Use on {world.Creatures[target].Sheet.Name}"));
            }
            if (targets.Count == 0)
            {
                world.CanConsume(_hero, index, _hero, out string why);
                actions.Add(new DataAction("use", "Use", false, why));
            }
        }
        if (!world.Fighting)
        {
            for (int other = 0; other < world.HeroCount; other++)
            {
                if (other != _hero && !world.Creatures[other].Sheet.Down)
                {
                    bool room = !item.Magic || world.Creatures[other].Sheet.RoomForMagic(world.Rules, item.Quantity);
                    actions.Add(new DataAction($"give:{other}", $"Give to {world.Creatures[other].Sheet.Name}", room, room ? "" : world.MagicLimitText(other)));
                }
            }
            for (int npc = 0; npc < world.Merchants.Count; npc++)
            {
                if (world.CanTrade(_hero, npc))
                {
                    Merchant shop = world.Merchants[npc]!;
                    bool can = shop.CanSell(sheet, index, out string why);
                    actions.Add(new DataAction($"sell:{npc}", $"Sell to {world.Chapter.Npcs[npc].Name} for {Coins.Text(Math.Max(0, shop.SellPrice(item)))}", can, why));
                }
            }
        }
        return actions;
    }

    /// <summary>An item's page: what it is, its numbers, what using it does and its description.</summary>
    public static string Page(World world, Item item, int price)
    {
        ItemDefinition d = item.Definition;
        var what = new List<string> { Kind(item) };
        if (item.Slot.Length > 0 && item.Slot != "armor")
        {
            what.Add(SlotName(item).ToLowerInvariant());
        }
        if (item.Held && item.Hands > 1)
        {
            what.Add($"{item.Hands} hands");
        }
        if (item.Magic)
        {
            what.Add("magic");
        }
        if (item.Equipped)
        {
            what.Add(item.Held ? "in hand" : "worn");
        }
        var page = new BookPage().Title(item.Name).Sub(string.Join(", ", what)).Rule();
        if (d.Damage.Length > 0)
        {
            string ability = d.AttackAbility.Length > 0 ? d.AttackAbility : world.Rules.Roles.AttackAbility;
            // dice that read the wielder (versatile) show both ways they can come out
            string low = DiceText.Fill(d.Damage, _ => 0);
            string high = DiceText.Fill(d.Damage, _ => 1);
            string dice = low == high ? low : $"{low} or {high}";
            page.Stat("Damage", ability.Length > 0 ? $"{dice} ({ability.ToUpperInvariant()})" : dice);
        }
        foreach (Modifier m in d.Modifiers)
        {
            string stat = m.Stat == "ac" ? "Armour Class" : m.Stat;
            string value = m.Op switch
            {
                ModifierOp.Override => $"{m.Value}",
                ModifierOp.Multiply => $"x{m.Value}",
                ModifierOp.Max => $"at least {m.Value}",
                ModifierOp.Min => $"at most {m.Value}",
                _ => SheetView.Signed((int)m.Value),
            };
            page.Stat(stat, value);
        }
        page.Stats(("Weight", Pounds(item.Weight) + " lb"), ("Value", price > 0 ? Coins.Text(price) : "none"), ("Count", item.Quantity > 1 ? item.Quantity.ToString() : ""));
        if (item.Use is ActionDefinition use)
        {
            page.Gap().Heading("Use");
            foreach (string line in HudText.ActionMeta(world, use, use.Cost).Split('\n'))
            {
                int colon = line.IndexOf(':');
                page.Stat(line[..colon], line[(colon + 2)..]);
            }
            if (use.Effect.Save.Ability.Length > 0)
            {
                page.Stat("Save", $"{use.Effect.Save.Ability.ToUpperInvariant()} DC {use.Effect.Save.Dc}");
            }
            foreach (string line in BookPage.EffectLines(use.Effect))
            {
                page.Text(line + ".");
            }
        }
        if (d.Supplies > 0)
        {
            page.Stat("Camp supplies", d.Supplies.ToString());
        }
        if (d.Description.Length > 0)
        {
            page.Rule().Text(d.Description);
        }
        return page.ToString();
    }

    private void Act(string id)
    {
        int index = _mine.TryGetValue(_view.Picked, out int own) ? own : _places.GetValueOrDefault(_view.Picked, -1);
        string[] parts = id.Split(':');
        int number = parts.Length > 1 && int.TryParse(parts[1], out int n) ? n : -1;
        ItemOrder? order = parts[0] switch
        {
            "equip" => new ItemOrder(ItemOrderKind.Equip, _hero, index),
            "unequip" => new ItemOrder(ItemOrderKind.Unequip, _hero, index),
            "use" => new ItemOrder(ItemOrderKind.Use, _hero, index, number < 0 ? _hero : number),
            "give" => new ItemOrder(ItemOrderKind.Give, _hero, index, number),
            "sell" => new ItemOrder(ItemOrderKind.Sell, _hero, index, Npc: number),
            "take" => new ItemOrder(ItemOrderKind.Take, _hero, index, Pile: _pile),
            "put" => new ItemOrder(ItemOrderKind.Put, _hero, index, Pile: _pile),
            "takecoins" => new ItemOrder(ItemOrderKind.TakeCoins, _hero, Pile: _pile),
            "takeall" => new ItemOrder(ItemOrderKind.TakeAll, _hero, Pile: _pile),
            "buy" => new ItemOrder(ItemOrderKind.Buy, _hero, index, Npc: _npc),
            _ => null,
        };
        if (order != null)
        {
            Ordered?.Invoke(order);
        }
    }

    /// <summary>Says what the world refused, under the entry.</summary>
    public void Refused(string why)
    {
        _view.ShowWarning(why);
    }

    private static string Foot(World world, CharacterSheet sheet)
    {
        string weight = $"Carrying {Pounds(sheet.CarriedWeight())} of {Pounds(sheet.CarryCapacity(world.Rules))} lb";
        int weighed = sheet.Encumbrance(world.Rules);
        if (weighed > 0)
        {
            weight += weighed == 2 ? " (can't move)" : " (slowed)";
        }
        string magic = world.Rules.MagicItemLimit > 0 ? $"   Magic items {sheet.MagicItems()} of {world.Rules.MagicItemLimit}" : "";
        return $"{sheet.Name}: {Coins.Text(sheet.Coins)}   {weight}   Hands {sheet.HandsInUse()} of {CharacterSheet.HandCount}{magic}";
    }

    private static HashSet<string> TagsOf(Item item)
    {
        var tags = new HashSet<string> { Kind(item) switch { "Weapon" => "Weapons", "Armour" or "Shield" => "Armour", "Consumable" => "Usable", _ => "Other" } };
        if (item.Equipped)
        {
            tags.Add("Worn");
        }
        if (item.Magic)
        {
            tags.Add("Magic");
        }
        if (item.Use != null)
        {
            tags.Add("Usable");
        }
        return tags;
    }

    private static string Kind(Item item)
    {
        if (item.Definition.Damage.Length > 0)
        {
            return "Weapon";
        }
        if (item.Definition.Modifiers.Any(m => m.Stat == "ac"))
        {
            return item.Held ? "Shield" : "Armour";
        }
        if (item.Use != null)
        {
            return "Consumable";
        }
        return item.Slot.Length > 0 ? "Gear" : "Item";
    }

    // "mainHand" reads "Main hand"
    private static string SlotName(Item item)
    {
        if (item.Slot.Length == 0)
        {
            return "";
        }
        var words = new System.Text.StringBuilder();
        foreach (char c in item.Slot)
        {
            if (char.IsUpper(c))
            {
                words.Append(' ');
            }
            words.Append(char.ToLowerInvariant(c));
        }
        string text = words.ToString();
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static string Pounds(double weight) => weight.ToString(weight < 10 && weight % 1 != 0 ? "0.#" : "0");
}
