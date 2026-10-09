using System;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>One square of the party inventory: a slot on a hero's doll or a place in their bag, empty or holding an item.</summary>
public partial class GearCell : Control
{
    private const string Drag = "gear:";
    private readonly PartyGearView _view;
    private readonly int _hero;
    private readonly string _slot;
    private readonly int _item;
    private readonly ActionIcon _icon = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _count = new() { ThemeTypeVariation = "NumberLabel", MouseFilter = MouseFilterEnum.Ignore, HorizontalAlignment = HorizontalAlignment.Right };
    private bool _dim;

    public GearCell(PartyGearView view, int hero, string slot, int item)
    {
        _view = view;
        _hero = hero;
        _slot = slot;
        _item = item;
        CustomMinimumSize = new Vector2(40, 40);
        MouseFilter = MouseFilterEnum.Stop;
        AddChild(_icon);
        _icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _icon.OffsetLeft = _icon.OffsetTop = 8;
        _icon.OffsetRight = _icon.OffsetBottom = -8;
        _count.AddThemeFontSizeOverride("font_size", 11);
        _count.AddThemeColorOverride("font_color", Palette.Bone);
        AddChild(_count);
        _count.SetAnchorsAndOffsetsPreset(LayoutPreset.BottomRight);
        _count.OffsetLeft = -24;
        _count.OffsetTop = -16;
        _count.OffsetRight = -3;
        _count.OffsetBottom = 0;
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
        DrawRect(new Rect2(Vector2.Zero, Size), full && !_dim ? Palette.Slate : Palette.Iron, false, 1);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_item >= 0 && @event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true, DoubleClick: true })
        {
            _view.Pressed(_hero, _item);
            AcceptEvent();
        }
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
        return $"{Drag}{_hero}:{_item}";
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data) =>
        data.VariantType == Variant.Type.String && data.AsString().StartsWith(Drag, StringComparison.Ordinal);

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        string[] parts = data.AsString()[Drag.Length..].Split(':');
        if (parts.Length == 2 && int.TryParse(parts[0], out int hero) && int.TryParse(parts[1], out int item))
        {
            _view.Dropped(hero, item, _hero, _slot);
        }
    }
}
