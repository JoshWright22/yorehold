using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// One hero's gear as the design draws it, for the party inventory and the chest or shop screen:
/// name and class line, what they wear and hold round their portrait, their armour class and
/// weapon, the bag as a grid of gear cells, and the weight they carry.
/// </summary>
public static class HeroGearColumn
{
    /// <summary>The doll's slots: item slot ids down the portrait's left and right, with their names.</summary>
    public static readonly (string Id, string Name)[] LeftSlots = { ("head", "Head"), ("cloak", "Cloak"), ("armor", "Body"), ("hands", "Hands"), ("feet", "Feet") };
    public static readonly (string Id, string Name)[] RightSlots = { ("neck", "Neck"), ("ring", "Ring"), ("mainHand", "Main hand"), ("offHand", "Off hand") };
    public const int BagColumns = 6;
    public const float Width = 284;

    /// <summary>The place name its cells carry: "h" and the hero's index.</summary>
    public static string Place(int hero) => "h" + hero;

    /// <summary>A hero's column. marked outlines it in amber; bagTitle and bagNote head the bag (PACK, "Drop here to buy").</summary>
    public static Control Build(World world, int hero, IGearCells owner, bool marked, string search, int bagRows, string bagTitle = "", string bagNote = "")
    {
        CharacterSheet sheet = world.Creatures[hero].Sheet;
        var column = new PanelContainer { CustomMinimumSize = new Vector2(Width, 0) };
        column.AddThemeStyleboxOverride("panel", Box(Palette.Ink, marked ? Palette.Straw : Palette.Iron, 8));
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 8);
        column.AddChild(rows);

        var head = new HBoxContainer();
        var name = new Label { Text = sheet.Name, ThemeTypeVariation = "TitleLabel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        name.AddThemeFontSizeOverride("font_size", 18);
        name.AddThemeColorOverride("font_color", Palette.Bone);
        head.AddChild(name);
        head.AddChild(Dim(sheet.ClassName.Length > 0 ? $"{sheet.ClassName}, level {sheet.Level}" : sheet.Ancestry, 12));
        rows.AddChild(head);

        // what is worn and held, round the portrait
        var doll = new HBoxContainer();
        doll.AddThemeConstantOverride("separation", 8);
        doll.AddChild(SlotColumn(world, hero, owner, LeftSlots, search));
        var frame = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        frame.AddThemeStyleboxOverride("panel", Box(Palette.Night, Palette.Iron, 6));
        var portrait = new PortraitView { CustomMinimumSize = new Vector2(0, 200), MouseFilter = Control.MouseFilterEnum.Ignore };
        portrait.Show(sheet.Name, world.Tokens.Tokens[hero].Color.ToGodot(), sheet.Down, Portraits.Of(world, hero), Portraits.FocusOf(world, hero));
        frame.AddChild(portrait);
        doll.AddChild(frame);
        doll.AddChild(SlotColumn(world, hero, owner, RightSlots, search));
        rows.AddChild(doll);

        // the numbers a player looks for before a fight
        var stats = new HBoxContainer();
        stats.AddThemeConstantOverride("separation", 4);
        string defence = world.Rules.Checks.Kind(CheckRules.Attack).DefenceId;
        stats.AddChild(Stat(world.Rules.DefenceName(defence), sheet.Defence(world.Rules, defence).ToString(), true));
        int attack = sheet.AttackModifier(world.Rules);
        stats.AddChild(Stat(sheet.WeaponItem?.Name ?? "Unarmed", $"{(attack >= 0 ? "+" : "")}{attack} · {sheet.DamageDice(world.Rules)}", false));
        rows.AddChild(stats);

        if (bagTitle.Length > 0)
        {
            var bagHead = new HBoxContainer();
            var title = new Label { Text = bagTitle.ToUpperInvariant(), ThemeTypeVariation = "CapsLabel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            title.AddThemeFontSizeOverride("font_size", 10);
            bagHead.AddChild(title);
            bagHead.AddChild(Dim(bagNote, 12));
            rows.AddChild(bagHead);
        }
        // the bag: what is carried and not worn, then empty cells to drop into
        var bag = new GridContainer { Columns = BagColumns };
        bag.AddThemeConstantOverride("h_separation", 4);
        bag.AddThemeConstantOverride("v_separation", 4);
        var shownSlots = LeftSlots.Concat(RightSlots).Select(s => s.Id).ToHashSet();
        var carried = sheet.Inventory.Select((item, index) => (Item: item, Index: index))
            .Where(p => !p.Item.Equipped || !shownSlots.Contains(p.Item.Slot)).ToList();
        int cells = Math.Max(BagColumns * bagRows, (carried.Count + BagColumns - 1) / BagColumns * BagColumns);
        for (int i = 0; i < cells; i++)
        {
            var cell = new GearCell(owner, Place(hero), "", i < carried.Count ? carried[i].Index : -1);
            bag.AddChild(cell);
            if (i < carried.Count)
            {
                Fill(cell, world, carried[i].Item, search);
            }
        }
        rows.AddChild(bag);
        rows.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });

        // how much they carry of what they can
        var weight = new HBoxContainer();
        weight.AddThemeConstantOverride("separation", 8);
        var caps = new Label { Text = "WEIGHT", ThemeTypeVariation = "CapsLabel", VerticalAlignment = VerticalAlignment.Center };
        caps.AddThemeFontSizeOverride("font_size", 10);
        weight.AddChild(caps);
        float carriedWeight = sheet.CarriedWeight();
        float most = sheet.CarryCapacity(world.Rules);
        var bar = new ProgressBar { MaxValue = Math.Max(1, most), Value = Math.Min(most, carriedWeight), ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, 4), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
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

    private static VBoxContainer SlotColumn(World world, int hero, IGearCells owner, (string Id, string Name)[] slots, string search)
    {
        CharacterSheet sheet = world.Creatures[hero].Sheet;
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 4);
        foreach ((string id, string name) in slots)
        {
            int index = sheet.Inventory.FindIndex(i => i.Equipped && i.Slot == id);
            var cell = new GearCell(owner, Place(hero), id, index) { TooltipText = name };
            if (index >= 0)
            {
                Fill(cell, world, sheet.Inventory[index], search);
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

    /// <summary>An item into a cell, with its picture from content or an art pack when there is one.</summary>
    public static void Fill(GearCell cell, World world, Item item, string search)
    {
        string words = search.Trim();
        bool matches = words.Length == 0 || item.Name.Contains(words, StringComparison.OrdinalIgnoreCase);
        cell.Show(item, PlayerArt.Texture(world.Files, $"icons/items/{item.Id}.png"), matches);
    }

    // a small box: a caps heading over a number (AC 16) or a line (+5 · 1d8+3)
    private static Control Stat(string heading, string value, bool big)
    {
        var box = new PanelContainer { SizeFlagsHorizontal = big ? Control.SizeFlags.Fill : Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(big ? 52 : 0, 40) };
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

    /// <summary>A dim line of text at a size.</summary>
    public static Label Dim(string text, int size)
    {
        var label = new Label { Text = text, ThemeTypeVariation = "DimLabel", VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", size);
        return label;
    }

    /// <summary>A flat box with a 1 px line.</summary>
    public static StyleBoxFlat Box(Color fill, Color line, float margin)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = line };
        box.SetBorderWidthAll(1);
        box.SetContentMarginAll(margin);
        return box;
    }

    /// <summary>A plain outlined button, as Close and Send are drawn.</summary>
    public static Button Outlined(string text, int fontSize = 14)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, ThemeTypeVariation = "MainButton" };
        button.AddThemeStyleboxOverride("normal", Box(Palette.Ink, Palette.Slate, 6));
        button.AddThemeStyleboxOverride("hover", Box(Palette.Dusk, Palette.Ash, 6));
        button.AddThemeStyleboxOverride("pressed", Box(Palette.Dusk, Palette.Ash, 6));
        button.AddThemeStyleboxOverride("disabled", Box(Palette.Ink, Palette.Iron, 6));
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
        {
            button.AddThemeColorOverride(state, Palette.Bone);
        }
        button.AddThemeColorOverride("font_disabled_color", Palette.Slate);
        button.AddThemeFontSizeOverride("font_size", fontSize);
        return button;
    }

    /// <summary>The design's main action: filled amber with dark words.</summary>
    public static Button Amber(string text)
    {
        var button = new Button { Text = text, FocusMode = Control.FocusModeEnum.None, ThemeTypeVariation = "MainButton" };
        button.AddThemeStyleboxOverride("normal", Box(Palette.Straw, Palette.Straw, 10));
        button.AddThemeStyleboxOverride("hover", Box(Palette.Amber, Palette.Amber, 10));
        button.AddThemeStyleboxOverride("pressed", Box(Palette.Amber, Palette.Amber, 10));
        button.AddThemeStyleboxOverride("disabled", Box(Palette.Ink, Palette.Iron, 10));
        foreach (string state in new[] { "font_color", "font_hover_color", "font_pressed_color" })
        {
            button.AddThemeColorOverride(state, Palette.Night);
        }
        button.AddThemeColorOverride("font_disabled_color", Palette.Slate);
        button.AddThemeFontSizeOverride("font_size", 16);
        return button;
    }

    /// <summary>The bar's tab (Inventory, Spells, Character): bold, an amber line under the open one.</summary>
    public static Button TopTab(string text, bool on, Action press)
    {
        var tab = new Button { Text = text, ToggleMode = true, FocusMode = Control.FocusModeEnum.None, ThemeTypeVariation = "MainButton", SizeFlagsVertical = Control.SizeFlags.Fill };
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
}
