using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The party's inventory (I), as the design draws it: a column for each hero with what they wear
/// and hold round their portrait, their armour class and weapon, the bag as a grid and the weight
/// they carry. Items are dragged between slots, bags and heroes, or double-clicked to put on, take
/// off or use. It only reads the World; what is done goes out as an ItemOrder.
/// </summary>
public partial class PartyGearView : Control
{
    /// <summary>The doll's slots: item slot ids down the portrait's left and right, with their names.</summary>
    private static readonly (string Id, string Name)[] LeftSlots = { ("head", "Head"), ("cloak", "Cloak"), ("armor", "Body"), ("hands", "Hands"), ("feet", "Feet") };
    private static readonly (string Id, string Name)[] RightSlots = { ("neck", "Neck"), ("ring", "Ring"), ("mainHand", "Main hand"), ("offHand", "Off hand") };
    private const int BagColumns = 6;
    private const int BagRows = 5;
    private const float Cell = 40;
    private const float ColumnWidth = 284;

    public event Action<ItemOrder>? Ordered;
    public event Action? Closed;
    /// <summary>Another tab along the top: "Spells" or "Sheet".</summary>
    public event Action<string>? TabPicked;

    private World? _world;
    private int _hero;
    private string _shown = "";
    private Label _coins = null!;
    private Label _said = null!;
    private LineEdit _search = null!;
    private VBoxContainer _strip = null!;
    private HBoxContainer _columns = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        var floor = new ColorRect { Color = Palette.Night, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(floor);
        floor.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var bar = new PanelContainer();
        bar.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Palette.Ink, BorderColor = Palette.Iron, BorderWidthBottom = 1 });
        AddChild(bar);
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        bar.OffsetBottom = 44;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 0);
        bar.AddChild(row);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(12, 0) });
        foreach ((string label, string tab) in new[] { ("Inventory", ""), ("Spells", "Spells"), ("Character", "Sheet") })
        {
            row.AddChild(TopTab(label, tab.Length == 0, () =>
            {
                if (tab.Length > 0)
                {
                    TabPicked?.Invoke(tab);
                }
            }));
        }
        row.AddChild(new Control { CustomMinimumSize = new Vector2(16, 0) });
        _search = new LineEdit { PlaceholderText = "Search all packs", CustomMinimumSize = new Vector2(200, 30), SizeFlagsVertical = SizeFlags.ShrinkCenter, ClearButtonEnabled = true };
        _search.TextChanged += _ => _shown = "";
        row.AddChild(_search);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(16, 0) });
        _said = new Label { Text = "", ThemeTypeVariation = "DimLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center,
            ClipText = true, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        row.AddChild(_said);
        var coinsHead = new Label { Text = "PARTY COINS", ThemeTypeVariation = "CapsLabel", VerticalAlignment = VerticalAlignment.Center };
        coinsHead.AddThemeFontSizeOverride("font_size", 10);
        row.AddChild(coinsHead);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });
        _coins = new Label { ThemeTypeVariation = "NumberLabel", VerticalAlignment = VerticalAlignment.Center };
        _coins.AddThemeFontSizeOverride("font_size", 16);
        _coins.AddThemeColorOverride("font_color", Palette.Bone);
        row.AddChild(_coins);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(16, 0) });
        Button close = Outlined("Close  " + App.KeyHint("gear"));
        close.SizeFlagsVertical = SizeFlags.ShrinkCenter;
        close.Pressed += () => Closed?.Invoke();
        row.AddChild(close);
        row.AddChild(new Control { CustomMinimumSize = new Vector2(12, 0) });

        _strip = new VBoxContainer { Position = new Vector2(8, 52) };
        _strip.AddThemeConstantOverride("separation", 8);
        AddChild(_strip);
        var scroll = new ScrollContainer { VerticalScrollMode = ScrollContainer.ScrollMode.Disabled };
        AddChild(scroll);
        scroll.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        scroll.OffsetLeft = 72;
        scroll.OffsetTop = 52;
        scroll.OffsetRight = -8;
        scroll.OffsetBottom = -8;
        _columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _columns.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_columns);
    }

    /// <summary>A line in the bar: why the rules said no, or what was done.</summary>
    public void Say(string text) => _said.Text = text;

    public void Refresh(World world, int hero)
    {
        _world = world;
        _hero = hero;
        int coins = Enumerable.Range(0, world.HeroCount).Sum(i => world.Creatures[i].Sheet.Coins);
        _coins.Text = Coins.Text(coins);
        if (_said.Text.Length == 0)
        {
            _said.Text = "Drag items between slots and heroes; double-click to wear or use.";
        }
        // made again only when what they show changes, so a drag isn't lost to a rebuild
        string shown = hero + "|" + _search.Text + "|" + string.Join(";", Enumerable.Range(0, world.HeroCount).Select(i =>
        {
            CharacterSheet sheet = world.Creatures[i].Sheet;
            return $"{sheet.Hp}/{sheet.MaxHp}/{sheet.Down}:" + string.Join(",", sheet.Inventory.Select(item => $"{item.Id}x{item.Quantity}{(item.Equipped ? "*" : "")}"));
        }));
        if (shown == _shown)
        {
            return;
        }
        _shown = shown;
        foreach (Node old in _strip.GetChildren().Concat(_columns.GetChildren()))
        {
            old.GetParent().RemoveChild(old);
            old.QueueFree();
        }
        for (int i = 0; i < world.HeroCount; i++)
        {
            _strip.AddChild(StripCard(world, i));
            _columns.AddChild(Column(world, i));
        }
    }

    // a hero on the left strip: their portrait and HP, the one shown on the bar outlined in amber
    private Control StripCard(World world, int hero)
    {
        CharacterSheet sheet = world.Creatures[hero].Sheet;
        var card = new PanelContainer { CustomMinimumSize = new Vector2(56, 76), MouseFilter = MouseFilterEnum.Ignore };
        card.AddThemeStyleboxOverride("panel", Box(Palette.Ink, hero == _hero ? Palette.Straw : Palette.Iron, 4));
        var rows = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        rows.AddThemeConstantOverride("separation", 2);
        card.AddChild(rows);
        var portrait = new PortraitView { CustomMinimumSize = new Vector2(46, 48), MouseFilter = MouseFilterEnum.Ignore };
        rows.AddChild(portrait);
        portrait.Show(sheet.Name, world.Tokens.Tokens[hero].Color.ToGodot(), sheet.Down, Portraits.Of(world, hero), Portraits.FocusOf(world, hero));
        var hp = new Label { Text = $"{Math.Max(0, sheet.Hp)}/{sheet.MaxHp}", ThemeTypeVariation = "NumberLabel", HorizontalAlignment = HorizontalAlignment.Center };
        hp.AddThemeFontSizeOverride("font_size", 12);
        hp.AddThemeColorOverride("font_color", sheet.Hp * 4 <= sheet.MaxHp ? Palette.Red : Palette.Bone);
        rows.AddChild(hp);
        return card;
    }

    private Control Column(World world, int hero)
    {
        CharacterSheet sheet = world.Creatures[hero].Sheet;
        var column = new PanelContainer { CustomMinimumSize = new Vector2(ColumnWidth, 0) };
        column.AddThemeStyleboxOverride("panel", Box(Palette.Ink, hero == _hero ? Palette.Straw : Palette.Iron, 8));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 8);
        column.AddChild(rows);

        var head = new HBoxContainer();
        var name = new Label { Text = sheet.Name, ThemeTypeVariation = "TitleLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        name.AddThemeFontSizeOverride("font_size", 18);
        name.AddThemeColorOverride("font_color", Palette.Bone);
        head.AddChild(name);
        var line = new Label { Text = sheet.ClassName.Length > 0 ? $"{sheet.ClassName}, level {sheet.Level}" : sheet.Ancestry, ThemeTypeVariation = "DimLabel",
            VerticalAlignment = VerticalAlignment.Center };
        line.AddThemeFontSizeOverride("font_size", 12);
        head.AddChild(line);
        rows.AddChild(head);

        // what is worn and held, round the portrait
        var doll = new HBoxContainer();
        doll.AddThemeConstantOverride("separation", 8);
        doll.AddChild(SlotColumn(world, hero, LeftSlots));
        var frame = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", Box(Palette.Night, Palette.Iron, 6));
        var portrait = new PortraitView { CustomMinimumSize = new Vector2(0, 200), MouseFilter = MouseFilterEnum.Ignore };
        portrait.Show(sheet.Name, world.Tokens.Tokens[hero].Color.ToGodot(), sheet.Down, Portraits.Of(world, hero), Portraits.FocusOf(world, hero));
        frame.AddChild(portrait);
        doll.AddChild(frame);
        doll.AddChild(SlotColumn(world, hero, RightSlots));
        rows.AddChild(doll);

        // the numbers a player looks for before a fight
        var stats = new HBoxContainer();
        stats.AddThemeConstantOverride("separation", 4);
        string defence = world.Rules.Checks.Kind(CheckRules.Attack).DefenceId;
        stats.AddChild(Stat(world.Rules.DefenceName(defence), sheet.Defence(world.Rules, defence).ToString(), true));
        int attack = sheet.AttackModifier(world.Rules);
        stats.AddChild(Stat(sheet.WeaponItem?.Name ?? "Unarmed", $"{(attack >= 0 ? "+" : "")}{attack} · {sheet.DamageDice(world.Rules)}", false));
        rows.AddChild(stats);

        // the bag: what is carried and not worn, then empty cells to drop into
        var bag = new GridContainer { Columns = BagColumns };
        bag.AddThemeConstantOverride("h_separation", 4);
        bag.AddThemeConstantOverride("v_separation", 4);
        var shownSlots = LeftSlots.Concat(RightSlots).Select(s => s.Id).ToHashSet();
        var carried = sheet.Inventory.Select((item, index) => (Item: item, Index: index))
            .Where(p => !p.Item.Equipped || !shownSlots.Contains(p.Item.Slot)).ToList();
        int cells = Math.Max(BagColumns * BagRows, (carried.Count + BagColumns - 1) / BagColumns * BagColumns);
        for (int i = 0; i < cells; i++)
        {
            var cell = new GearCell(this, hero, "", i < carried.Count ? carried[i].Index : -1);
            bag.AddChild(cell);
            if (i < carried.Count)
            {
                Fill(cell, world, carried[i].Item);
            }
        }
        rows.AddChild(bag);
        rows.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        // how much they carry of what they can
        var weight = new HBoxContainer();
        weight.AddThemeConstantOverride("separation", 8);
        var title = new Label { Text = "WEIGHT", ThemeTypeVariation = "CapsLabel", VerticalAlignment = VerticalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 10);
        weight.AddChild(title);
        float carriedWeight = sheet.CarriedWeight();
        float most = sheet.CarryCapacity(world.Rules);
        var bar = new ProgressBar { MaxValue = Math.Max(1, most), Value = Math.Min(most, carriedWeight), ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 4), SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = carriedWeight > most ? Palette.Red : Palette.Bone });
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = Palette.Iron });
        weight.AddChild(bar);
        var pounds = new Label { Text = $"{carriedWeight:0.#} / {most:0.#} lb", ThemeTypeVariation = "NumberLabel" };
        pounds.AddThemeFontSizeOverride("font_size", 12);
        pounds.AddThemeColorOverride("font_color", Palette.Bone);
        weight.AddChild(pounds);
        rows.AddChild(weight);
        return column;
    }

    private VBoxContainer SlotColumn(World world, int hero, (string Id, string Name)[] slots)
    {
        CharacterSheet sheet = world.Creatures[hero].Sheet;
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        foreach ((string id, string name) in slots)
        {
            int index = sheet.Inventory.FindIndex(i => i.Equipped && i.Slot == id);
            var cell = new GearCell(this, hero, id, index) { TooltipText = name };
            if (index >= 0)
            {
                Fill(cell, world, sheet.Inventory[index]);
            }
            else
            {
                // an empty slot says what goes there, faintly
                cell.Empty(name);
            }
            column.AddChild(cell);
        }
        return column;
    }

    private void Fill(GearCell cell, World world, Item item)
    {
        string words = _search.Text.Trim();
        bool matches = words.Length == 0 || item.Name.Contains(words, StringComparison.OrdinalIgnoreCase);
        cell.Show(item, PlayerArt.Texture(world.Files, $"icons/items/{item.Id}.png"), matches);
    }

    // A drop: an item from one hero's slot or bag onto another place.
    internal void Dropped(int fromHero, int item, int toHero, string toSlot)
    {
        if (_world == null || item < 0 || fromHero >= _world.HeroCount || toHero >= _world.HeroCount)
        {
            return;
        }
        _said.Text = "";
        Item moved = _world.Creatures[fromHero].Sheet.Inventory[item];
        if (fromHero != toHero)
        {
            Ordered?.Invoke(new ItemOrder(ItemOrderKind.Give, fromHero, item, toHero));
        }
        else if (toSlot.Length > 0 && !moved.Equipped)
        {
            Ordered?.Invoke(new ItemOrder(ItemOrderKind.Equip, fromHero, item));
        }
        else if (toSlot.Length == 0 && moved.Equipped)
        {
            Ordered?.Invoke(new ItemOrder(ItemOrderKind.Unequip, fromHero, item));
        }
        _shown = "";
    }

    // A double-click: on with what can be worn, off with what is, used if it is used up.
    internal void Pressed(int hero, int item)
    {
        if (_world == null || item < 0)
        {
            return;
        }
        _said.Text = "";
        Item it = _world.Creatures[hero].Sheet.Inventory[item];
        ItemOrderKind kind = it.Equipped ? ItemOrderKind.Unequip : it.Slot.Length > 0 ? ItemOrderKind.Equip : ItemOrderKind.Use;
        if (kind == ItemOrderKind.Use && it.Use == null)
        {
            _said.Text = $"{it.Name} isn't used like that.";
            return;
        }
        Ordered?.Invoke(new ItemOrder(kind, hero, item, -1));
        _shown = "";
    }

    private Button TopTab(string text, bool on, Action press)
    {
        var tab = new Button { Text = text, ToggleMode = true, FocusMode = FocusModeEnum.None, ThemeTypeVariation = "MainButton", SizeFlagsVertical = SizeFlags.Fill };
        var off = new StyleBoxFlat { BgColor = Palette.Ink };
        off.SetContentMarginAll(12);
        var under = new StyleBoxFlat { BgColor = Palette.Ink, BorderColor = Palette.Straw, BorderWidthBottom = 2 };
        under.SetContentMarginAll(12);
        tab.AddThemeStyleboxOverride("normal", off);
        tab.AddThemeStyleboxOverride("hover", off);
        tab.AddThemeStyleboxOverride("pressed", under);
        tab.AddThemeStyleboxOverride("hover_pressed", under);
        tab.AddThemeColorOverride("font_color", Palette.Ash);
        tab.AddThemeColorOverride("font_hover_color", Palette.Bone);
        tab.AddThemeColorOverride("font_pressed_color", Palette.Bone);
        tab.AddThemeColorOverride("font_hover_pressed_color", Palette.Bone);
        tab.AddThemeFontSizeOverride("font_size", 15);
        tab.SetPressedNoSignal(on);
        tab.Pressed += () =>
        {
            tab.SetPressedNoSignal(on);
            press();
        };
        return tab;
    }

    private static Button Outlined(string text)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None, ThemeTypeVariation = "MainButton" };
        button.AddThemeStyleboxOverride("normal", Box(Palette.Ink, Palette.Slate, 6));
        button.AddThemeStyleboxOverride("hover", Box(Palette.Dusk, Palette.Ash, 6));
        button.AddThemeStyleboxOverride("pressed", Box(Palette.Dusk, Palette.Ash, 6));
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
        {
            button.AddThemeColorOverride(state, Palette.Bone);
        }
        button.AddThemeFontSizeOverride("font_size", 14);
        return button;
    }

    // a small box: a caps heading over a number (AC 16) or a line (+5 · 1d8+3)
    private static Control Stat(string heading, string value, bool big)
    {
        var box = new PanelContainer { SizeFlagsHorizontal = big ? SizeFlags.Fill : SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(big ? 52 : 0, 40) };
        box.AddThemeStyleboxOverride("panel", Box(Palette.Night, Palette.Iron, 6));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 0);
        box.AddChild(rows);
        var head = new Label { Text = heading.ToUpperInvariant(), ThemeTypeVariation = "CapsLabel", ClipText = true,
            HorizontalAlignment = big ? HorizontalAlignment.Center : HorizontalAlignment.Left };
        head.AddThemeFontSizeOverride("font_size", 9);
        rows.AddChild(head);
        var text = new Label { Text = value, ThemeTypeVariation = "NumberLabel", HorizontalAlignment = big ? HorizontalAlignment.Center : HorizontalAlignment.Left };
        text.AddThemeFontSizeOverride("font_size", big ? 20 : 13);
        text.AddThemeColorOverride("font_color", Palette.Bone);
        rows.AddChild(text);
        return box;
    }

    internal static StyleBoxFlat Box(Color fill, Color line, float margin)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = line };
        box.SetBorderWidthAll(1);
        box.SetContentMarginAll(margin);
        return box;
    }
}
