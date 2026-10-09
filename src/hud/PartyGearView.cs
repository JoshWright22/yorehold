using System;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The party's inventory (I), as the design draws it: a column for each hero (HeroGearColumn)
/// beside a strip of their portraits. Items are dragged between slots, bags and heroes, or
/// double-clicked to put on, take off or use. It only reads the World; what is done goes out as an
/// ItemOrder.
/// </summary>
public partial class PartyGearView : Control, IGearCells
{
    private const int BagRows = 5;

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
            row.AddChild(HeroGearColumn.TopTab(label, tab.Length == 0, () =>
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
        _said = new Label { ThemeTypeVariation = "DimLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center,
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
        Button close = HeroGearColumn.Outlined("Close  " + App.KeyHint("gear"));
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
        string shown = hero + "|" + _search.Text + "|" + Signature(world);
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
            _columns.AddChild(HeroGearColumn.Build(world, i, this, i == hero, _search.Text, BagRows));
        }
    }

    /// <summary>What the party carries and how they stand, to tell when the columns must be made again.</summary>
    public static string Signature(World world) => string.Join(";", Enumerable.Range(0, world.HeroCount).Select(i =>
    {
        CharacterSheet sheet = world.Creatures[i].Sheet;
        return $"{sheet.Hp}/{sheet.MaxHp}/{sheet.Down}/{sheet.Coins}:" + string.Join(",", sheet.Inventory.Select(item => $"{item.Id}x{item.Quantity}{(item.Equipped ? "*" : "")}"));
    }));

    // a hero on the left strip: their portrait and HP, the one shown on the bar outlined in amber
    private Control StripCard(World world, int hero)
    {
        CharacterSheet sheet = world.Creatures[hero].Sheet;
        var card = new PanelContainer { CustomMinimumSize = new Vector2(56, 76), MouseFilter = MouseFilterEnum.Ignore };
        card.AddThemeStyleboxOverride("panel", HeroGearColumn.Box(Palette.Ink, hero == _hero ? Palette.Straw : Palette.Iron, 4));
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

    private static int HeroOf(string place) => place.StartsWith('h') && int.TryParse(place[1..], out int hero) ? hero : -1;

    public void Moved(string from, int item, string to, string toSlot)
    {
        int fromHero = HeroOf(from);
        int toHero = HeroOf(to);
        if (_world == null || item < 0 || fromHero < 0 || toHero < 0 || fromHero >= _world.HeroCount || toHero >= _world.HeroCount)
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

    public void Picked(string from, int item)
    {
        int hero = HeroOf(from);
        if (_world != null && hero >= 0 && hero < _world.HeroCount && item < _world.Creatures[hero].Sheet.Inventory.Count)
        {
            Item it = _world.Creatures[hero].Sheet.Inventory[item];
            _said.Text = $"{it.Name}: {Coins.Text(it.Value)}, {it.Weight * it.Quantity:0.#} lb" + (it.Definition.Description.Length > 0 ? ". " + it.Definition.Description : "");
        }
    }

    // A double-click: on with what can be worn, off with what is, used if it is used up.
    public void Opened(string from, int item)
    {
        int hero = HeroOf(from);
        if (_world == null || hero < 0 || item < 0)
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
        Ordered?.Invoke(new ItemOrder(kind, hero, item, kind == ItemOrderKind.Use ? hero : -1));
        _shown = "";
    }
}
