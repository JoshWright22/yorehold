using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// A chest or a shop beside a hero, as the design draws it: the hero's gear on the left
/// (HeroGearColumn), what the chest holds or the shop sells as a grid in the middle with its
/// prices, and the picked item's page on the right with what can be done with it. Dragging from
/// the grid to the pack takes or buys one; from the pack to the grid puts or sells it.
/// </summary>
public partial class TradeView : Control, IGearCells
{
    private const string Stock = "stock";
    private static readonly string[] Kinds = { "All", "Weapons", "Armour", "Usable", "Other" };

    public event Action<ItemOrder>? Ordered;
    public event Action? Closed;

    private World? _world;
    private int _hero;
    private int _pile = -1;
    private int _npc = -1;
    private string _kind = "All";
    // the item on the page: where it is ("stock" or "h0") and its index there
    private string _pickedPlace = "";
    private int _picked = -1;
    private string _shown = "";

    private Label _what = null!;
    private Label _name = null!;
    private Label _hint = null!;
    private Label _coins = null!;
    private HBoxContainer _body = null!;
    private Label _said = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        var floor = new ColorRect { Color = Palette.Night, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(floor);
        floor.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var bar = new PanelContainer();
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Ink, BorderColor = Palette.Iron, BorderWidthBottom = 1,
            ContentMarginLeft = 16, ContentMarginRight = 12 });
        AddChild(bar);
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        bar.OffsetBottom = 44;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        bar.AddChild(row);
        _what = new Label { ThemeTypeVariation = "CapsLabel", VerticalAlignment = VerticalAlignment.Center };
        _what.AddThemeFontSizeOverride("font_size", 10);
        row.AddChild(_what);
        _name = new Label { ThemeTypeVariation = "TitleLabel", VerticalAlignment = VerticalAlignment.Center };
        _name.AddThemeFontSizeOverride("font_size", 20);
        _name.AddThemeColorOverride("font_color", Palette.Bone);
        row.AddChild(_name);
        _hint = HeroGearColumn.Dim("", 13);
        _hint.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _hint.ClipText = true;
        row.AddChild(_hint);
        var coinsHead = new Label { Text = "YOUR COINS", ThemeTypeVariation = "CapsLabel", VerticalAlignment = VerticalAlignment.Center };
        coinsHead.AddThemeFontSizeOverride("font_size", 10);
        row.AddChild(coinsHead);
        _coins = new Label { ThemeTypeVariation = "NumberLabel", VerticalAlignment = VerticalAlignment.Center };
        _coins.AddThemeFontSizeOverride("font_size", 16);
        _coins.AddThemeColorOverride("font_color", Palette.Bone);
        row.AddChild(_coins);
        Button close = HeroGearColumn.Outlined("Close  Esc");
        close.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        close.Pressed += () => Closed?.Invoke();
        row.AddChild(close);

        _body = new HBoxContainer();
        _body.AddThemeConstantOverride("separation", 8);
        AddChild(_body);
        _body.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _body.OffsetLeft = 8;
        _body.OffsetTop = 52;
        _body.OffsetRight = -8;
        _body.OffsetBottom = -8;
        _said = new Label { ThemeTypeVariation = "WarnLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
    }

    /// <summary>Opens on a source: "pile:2" or "shop:0". Returns false when there is none such.</summary>
    public void Open(string source)
    {
        _pile = source.StartsWith("pile:", StringComparison.Ordinal) ? int.Parse(source[5..]) : -1;
        _npc = source.StartsWith("shop:", StringComparison.Ordinal) ? int.Parse(source[5..]) : -1;
        _picked = -1;
        _pickedPlace = "";
        _kind = "All";
        _said.Text = "";
        _shown = "";
    }

    /// <summary>Why the rules said no, under the page.</summary>
    public void Say(string text)
    {
        _said.Text = text;
        _shown = "";
    }

    /// <summary>Whether the hero can still reach the chest or shop; the screen closes when they can't.</summary>
    public static bool Reachable(World world, int hero, string source)
    {
        if (world.Fighting)
        {
            return false;
        }
        if (source.StartsWith("pile:", StringComparison.Ordinal) && int.TryParse(source[5..], out int pile) && pile < world.Piles.Count)
        {
            Cell at = world.CellOf(hero);
            Pile p = world.Piles[pile];
            return !world.PileLocked(pile) && Math.Abs(p.At.X - at.X) <= 1 && Math.Abs(p.At.Y - at.Y) <= 1;
        }
        return source.StartsWith("shop:", StringComparison.Ordinal) && int.TryParse(source[5..], out int npc) && npc < world.Merchants.Count && world.CanTrade(hero, npc);
    }

    private List<Item> StockItems(World world) => _npc >= 0 ? world.Merchants[_npc]!.Inventory : world.Piles[_pile].Items;

    public void Refresh(World world, int hero)
    {
        _world = world;
        _hero = hero;
        CharacterSheet sheet = world.Creatures[hero].Sheet;
        bool shop = _npc >= 0;
        _what.Text = shop ? "SHOP" : "CHEST";
        _name.Text = shop ? world.Chapter.Npcs[_npc].Name : world.Piles[_pile].Name;
        _hint.Text = shop ? "Drag to buy or sell one." : "Drag to take or put one.";
        _coins.Text = Coins.Text(sheet.Coins);
        List<Item> stock = StockItems(world);
        int coins = shop ? 0 : world.Piles[_pile].Coins;
        string shown = $"{hero}|{_kind}|{_pickedPlace}:{_picked}|{_said.Text}|{Size.X:0}|{coins}|" + PartyGearView.Signature(world) + "|"
            + string.Join(",", stock.Select(i => $"{i.Id}x{i.Quantity}"));
        if (shown == _shown)
        {
            return;
        }
        _shown = shown;
        // the refusal line is kept across rebuilds: out of the old page before it goes
        _said.GetParent()?.RemoveChild(_said);
        foreach (Node old in _body.GetChildren())
        {
            _body.RemoveChild(old);
            old.QueueFree();
        }
        _body.AddChild(HeroGearColumn.Build(world, hero, this, false, "", 6, "Pack", shop ? "Drop here to buy" : "Drop here to take"));
        _body.AddChild(StockPanel(world, stock, coins));
        _body.AddChild(PagePanel(world));
    }

    private Control StockPanel(World world, List<Item> stock, int coins)
    {
        bool shop = _npc >= 0;
        CharacterSheet sheet = world.Creatures[_hero].Sheet;
        int columns = Size.X > 1180 ? 8 : 6;
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(columns * 52 + (columns - 1) * 4 + 18, 0) };
        panel.AddThemeStyleboxOverride("panel", HeroGearColumn.Box(Palette.Ink, Palette.Iron, 8));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 8);
        panel.AddChild(rows);
        var head = new HBoxContainer();
        var title = new Label { Text = shop ? "Shop stock" : "In it", ThemeTypeVariation = "TitleLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 18);
        title.AddThemeColorOverride("font_color", Palette.Bone);
        head.AddChild(title);
        head.AddChild(HeroGearColumn.Dim(stock.Count == 1 ? "1 stack" : $"{stock.Count} stacks", 12));
        rows.AddChild(head);

        // the kinds of thing on the shelf, each with how many, the open one in amber
        var chips = new HFlowContainer();
        chips.AddThemeConstantOverride("h_separation", 4);
        chips.AddThemeConstantOverride("v_separation", 4);
        foreach (string kind in Kinds)
        {
            int count = kind == "All" ? stock.Count : stock.Count(i => GearPanel.KindTag(i) == kind);
            if (count == 0 && kind != "All")
            {
                continue;
            }
            var chip = new Button { Text = $"{kind}  {count}", ToggleMode = true, FocusMode = FocusModeEnum.None, ThemeTypeVariation = "MainButton" };
            StyleBoxFlat off = HeroGearColumn.Box(Palette.Ink, Palette.Slate, 2);
            StyleBoxFlat on = HeroGearColumn.Box(Palette.Dusk, Palette.Straw, 2);
            off.ContentMarginLeft = off.ContentMarginRight = on.ContentMarginLeft = on.ContentMarginRight = 8;
            chip.AddThemeStyleboxOverride("normal", off);
            chip.AddThemeStyleboxOverride("hover", on);
            chip.AddThemeStyleboxOverride("pressed", on);
            chip.AddThemeStyleboxOverride("hover_pressed", on);
            chip.AddThemeColorOverride("font_color", Palette.Bone);
            chip.AddThemeColorOverride("font_pressed_color", Palette.Straw);
            chip.AddThemeColorOverride("font_hover_pressed_color", Palette.Straw);
            chip.AddThemeFontSizeOverride("font_size", 12);
            chip.SetPressedNoSignal(kind == _kind);
            string picked = kind;
            chip.Pressed += () =>
            {
                _kind = picked;
                _shown = "";
            };
            chips.AddChild(chip);
        }
        rows.AddChild(chips);

        var grid = new GridContainer { Columns = columns };
        grid.AddThemeConstantOverride("h_separation", 4);
        grid.AddThemeConstantOverride("v_separation", 4);
        var shelf = stock.Select((item, index) => (Item: item, Index: index)).Where(p => _kind == "All" || GearPanel.KindTag(p.Item) == _kind).ToList();
        int cells = Math.Max(columns * 6, (shelf.Count + columns - 1) / columns * columns);
        for (int i = 0; i < cells; i++)
        {
            var cell = new GearCell(this, Stock, "", i < shelf.Count ? shelf[i].Index : -1, 52);
            grid.AddChild(cell);
            if (i >= shelf.Count)
            {
                continue;
            }
            Item item = shelf[i].Item;
            HeroGearColumn.Fill(cell, world, item, "");
            cell.Pick(_pickedPlace == Stock && _picked == shelf[i].Index);
            int price = shop ? world.Merchants[_npc]!.BuyPrice(item) : item.Value;
            cell.Price(price > 0 ? Coins.Text(price) : "-", shop && price > sheet.Coins);
        }
        rows.AddChild(grid);
        rows.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        if (!shop && coins > 0)
        {
            Button take = HeroGearColumn.Outlined($"Take {Coins.Text(coins)}", 13);
            take.Pressed += () => Ordered?.Invoke(new ItemOrder(ItemOrderKind.TakeCoins, _hero, Pile: _pile));
            rows.AddChild(take);
        }
        var note = new PanelContainer();
        note.AddThemeStyleboxOverride("panel", HeroGearColumn.Box(Palette.Night, Palette.Iron, 8));
        var words = new Label { Text = shop ? "Drop items from your pack here to sell them. Prices in red cost more than you have."
            : "Drop items from your pack here to put them in.", ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        words.AddThemeFontSizeOverride("font_size", 12);
        note.AddChild(words);
        rows.AddChild(note);
        return panel;
    }

    // the picked item's page, and what can be done with it
    private Control PagePanel(World world)
    {
        bool shop = _npc >= 0;
        CharacterSheet sheet = world.Creatures[_hero].Sheet;
        var panel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        panel.AddThemeStyleboxOverride("panel", HeroGearColumn.Box(Palette.Ink, Palette.Iron, 16));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 8);
        panel.AddChild(rows);
        List<Item> stock = StockItems(world);
        Item? item = _pickedPlace == Stock && _picked >= 0 && _picked < stock.Count ? stock[_picked]
            : _pickedPlace.Length > 0 && _pickedPlace != Stock && _picked >= 0 && _picked < sheet.Inventory.Count ? sheet.Inventory[_picked] : null;
        if (item == null)
        {
            Label empty = HeroGearColumn.Dim(shop ? "Pick something on the shelf or in the pack to see it here." : "Pick something to see it here.", 13);
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            rows.AddChild(empty);
            rows.AddChild(_said);
            return panel;
        }
        bool mine = _pickedPlace != Stock;
        int price = shop ? (mine ? world.Merchants[_npc]!.SellPrice(item) : world.Merchants[_npc]!.BuyPrice(item)) : item.Value;
        var page = new RichTextLabel { BbcodeEnabled = true, FitContent = true, ScrollActive = false, ThemeTypeVariation = "PageText",
            SizeFlagsVertical = SizeFlags.ExpandFill, Text = GearPanel.Page(world, item, price) };
        rows.AddChild(page);
        rows.AddChild(_said);
        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 8);
        if (!mine && shop)
        {
            bool can = world.Merchants[_npc]!.CanBuy(sheet, _picked, world.Rules, out string why);
            Button buy = HeroGearColumn.Amber($"Buy for {Coins.Text(Math.Max(0, price))}");
            buy.Disabled = !can;
            buy.TooltipText = why;
            buy.CustomMinimumSize = new Vector2(200, 44);
            int index = _picked;
            buy.Pressed += () => Ordered?.Invoke(new ItemOrder(ItemOrderKind.Buy, _hero, index, Npc: _npc));
            buttons.AddChild(buy);
        }
        else if (!mine)
        {
            Button take = HeroGearColumn.Amber("Take");
            take.CustomMinimumSize = new Vector2(140, 44);
            int index = _picked;
            take.Pressed += () => Ordered?.Invoke(new ItemOrder(ItemOrderKind.Take, _hero, index, Pile: _pile));
            buttons.AddChild(take);
            Button all = HeroGearColumn.Outlined("Take all");
            all.Pressed += () => Ordered?.Invoke(new ItemOrder(ItemOrderKind.TakeAll, _hero, Pile: _pile));
            buttons.AddChild(all);
        }
        else if (shop)
        {
            bool can = world.Merchants[_npc]!.CanSell(sheet, _picked, out string why);
            Button sell = HeroGearColumn.Amber($"Sell for {Coins.Text(Math.Max(0, price))}");
            sell.Disabled = !can;
            sell.TooltipText = why;
            sell.CustomMinimumSize = new Vector2(200, 44);
            int index = _picked;
            sell.Pressed += () => Ordered?.Invoke(new ItemOrder(ItemOrderKind.Sell, _hero, index, Npc: _npc));
            buttons.AddChild(sell);
        }
        else
        {
            Button put = HeroGearColumn.Amber($"Put in {world.Piles[_pile].Name}");
            put.CustomMinimumSize = new Vector2(200, 44);
            int index = _picked;
            put.Pressed += () => Ordered?.Invoke(new ItemOrder(ItemOrderKind.Put, _hero, index, Pile: _pile));
            buttons.AddChild(put);
        }
        rows.AddChild(buttons);
        return panel;
    }

    public void Moved(string from, int item, string to, string toSlot)
    {
        if (_world == null || item < 0)
        {
            return;
        }
        _said.Text = "";
        bool shop = _npc >= 0;
        if (from == Stock && to != Stock)
        {
            Ordered?.Invoke(shop ? new ItemOrder(ItemOrderKind.Buy, _hero, item, Npc: _npc) : new ItemOrder(ItemOrderKind.Take, _hero, item, Pile: _pile));
        }
        else if (from != Stock && to == Stock)
        {
            Ordered?.Invoke(shop ? new ItemOrder(ItemOrderKind.Sell, _hero, item, Npc: _npc) : new ItemOrder(ItemOrderKind.Put, _hero, item, Pile: _pile));
        }
        else if (from != Stock && to != Stock)
        {
            // within the pack: wear it or take it off, as on the inventory screen
            Item moved = _world.Creatures[_hero].Sheet.Inventory[item];
            if (toSlot.Length > 0 && !moved.Equipped)
            {
                Ordered?.Invoke(new ItemOrder(ItemOrderKind.Equip, _hero, item));
            }
            else if (toSlot.Length == 0 && moved.Equipped)
            {
                Ordered?.Invoke(new ItemOrder(ItemOrderKind.Unequip, _hero, item));
            }
        }
        _picked = -1;
        _pickedPlace = "";
        _shown = "";
    }

    public void Picked(string from, int item)
    {
        _pickedPlace = from;
        _picked = item;
        _said.Text = "";
        _shown = "";
    }

    // a double-click takes or buys from the shelf, puts or sells from the pack
    public void Opened(string from, int item) => Moved(from, item, from == Stock ? HeroGearColumn.Place(_hero) : Stock, "");
}
