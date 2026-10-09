using System;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>What a screen of gear cells hears from them: a drop, a pick and a double-click.</summary>
public interface IGearCells
{
    /// <summary>An item dragged from one place ("h0" a hero's, "stock" a chest's or shop's) onto another, a slot on a hero's doll or not.</summary>
    void Moved(string from, int item, string to, string toSlot);
    /// <summary>One click on an item.</summary>
    void Picked(string from, int item);
    /// <summary>A double-click on an item.</summary>
    void Opened(string from, int item);
}

/// <summary>One square of gear: a slot on a hero's doll, a place in a bag or a shop's shelf, empty or holding an item.</summary>
public partial class GearCell : Control
{
    private const string Drag = "gear:";
    private readonly IGearCells _owner;
    private readonly string _place;
    private readonly string _slot;
    private readonly int _item;
    private readonly ActionIcon _icon = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _count = new() { ThemeTypeVariation = "NumberLabel", MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Right };
    private Label? _price;
    private bool _dim;
    private bool _picked;

    /// <summary>place: "h0" for a hero's gear, "stock" for a chest's or shop's; slot: the doll slot, "" in a bag; item: its index, -1 for empty.</summary>
    public GearCell(IGearCells owner, string place, string slot, int item, float size = 40)
    {
        _owner = owner;
        _place = place;
        _slot = slot;
        _item = item;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(_icon);
        _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _icon.OffsetLeft = _icon.OffsetTop = 8;
        _icon.OffsetRight = _icon.OffsetBottom = -8;
        _count.AddThemeFontSizeOverride("font_size", 11);
        _count.AddThemeColorOverride("font_color", Palette.Bone);
        AddChild(_count);
        _count.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight);
        _count.OffsetLeft = -24;
        _count.OffsetTop = 1;
        _count.OffsetRight = -3;
        _count.OffsetBottom = 16;
    }

    /// <summary>An item in it: its picture or first letter, how many when more than one; dim when a search leaves it out.</summary>
    public void Show(Item item, Texture2D? picture, bool matches)
    {
        _icon.Show(item.Name.Length > 0 ? item.Name[..1].ToUpperInvariant() : "?", picture);
        _icon.Grey(!matches);
        _count.Text = item.Quantity > 1 ? item.Quantity.ToString() : "";
        _dim = !matches;
        TooltipText = item.Name + (item.Equipped ? " (worn)" : "");
        QueueRedraw();
    }

    /// <summary>A price under the picture (a shop's shelf), in red when the buyer can't pay it.</summary>
    public void Price(string text, bool tooDear)
    {
        if (_price == null)
        {
            _price = new Label { ThemeTypeVariation = "NumberLabel", MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Center };
            _price.AddThemeFontSizeOverride("font_size", 11);
            AddChild(_price);
            _price.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomWide);
            _price.OffsetTop = -17;
            _price.OffsetBottom = -2;
            _icon.OffsetBottom = -18;
        }
        _price.Text = text;
        _price.AddThemeColorOverride("font_color", tooDear ? Palette.Red : Palette.Ash);
    }

    /// <summary>The item shown on the page beside the grid: outlined in amber.</summary>
    public void Pick(bool picked)
    {
        _picked = picked;
        QueueRedraw();
    }

    /// <summary>An empty slot on the doll: the slot's first letter, faint.</summary>
    public void Empty(string name)
    {
        _icon.Show(name[..1], null);
        _icon.Grey(true);
        _dim = true;
    }

    public override void _Draw()
    {
        bool full = _item >= 0;
        DrawRect(new Rect2(Vector2.Zero, Size), full && !_dim ? Palette.Dusk : Palette.Night);
        DrawRect(new Rect2(Vector2.Zero, Size), _picked ? Palette.Straw : full && !_dim ? Palette.Slate : Palette.Iron, false, 1);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_item < 0 || @event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press)
        {
            return;
        }
        if (press.DoubleClick)
        {
            _owner.Opened(_place, _item);
        }
        else
        {
            _owner.Picked(_place, _item);
        }
        AcceptEvent();
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (_item < 0)
        {
            return default;
        }
        var ghost = new ActionIcon { Size = new Vector2(28, 28), MouseFilter = MouseFilterEnum.Ignore };
        ghost.Show(TooltipText.Length > 0 ? TooltipText[..1] : "?", null);
        SetDragPreview(ghost);
        return $"{Drag}{_place}:{_item}";
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        data.VariantType == Variant.Type.String && data.AsString().StartsWith(Drag, StringComparison.Ordinal);

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        string[] parts = data.AsString()[Drag.Length..].Split(':');
        if (parts.Length == 2 && int.TryParse(parts[1], out int item))
        {
            _owner.Moved(parts[0], item, _place, _slot);
        }
    }
}
