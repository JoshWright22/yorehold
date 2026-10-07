using System;
using Godot;

namespace Yorehold;

/// <summary>
/// One spell or action in the spell book's grid: its icon with the name under it and a small mark
/// in the corner (its level, or that it is prepared). It can be dragged onto a hotbar slot.
/// </summary>
public partial class IconTile : Button
{
    public static readonly Vector2 TileSize = new(74, 72);

    public string Key { get; private set; } = "";
    /// <summary>What a drag from it carries; "" and it can't be dragged.</summary>
    public string Drag { get; set; } = "";

    private readonly ActionIcon _icon = new() { MouseFilter = MouseFilterEnum.Ignore };
    private readonly Label _name = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        ClipText = true,
        MouseFilter = MouseFilterEnum.Ignore,
        ThemeTypeVariation = "CellLabel",
    };
    private readonly Label _badge = new() { MouseFilter = MouseFilterEnum.Ignore, ThemeTypeVariation = "KeyLabel" };

    public IconTile()
    {
        ToggleMode = true;
        FocusMode = FocusModeEnum.None;
        CustomMinimumSize = TileSize;
        ThemeTypeVariation = "TileButton";
        AddChild(_icon);
        AddChild(_name);
        AddChild(_badge);
        _icon.Position = new Vector2((TileSize.X - 36) / 2, 7);
        _icon.Size = new Vector2(36, 36);
        _name.Position = new Vector2(3, 47);
        _name.Size = new Vector2(TileSize.X - 6, 18);
        _name.AddThemeFontSizeOverride("font_size", 11);
        _badge.Position = new Vector2(TileSize.X - 18, 2);
        _badge.Size = new Vector2(16, 14);
    }

    public void Show(string key, string name, Texture2D? picture, string badge, bool dim, bool picked)
    {
        Key = key;
        _icon.Show(name.Length > 0 ? name[..1] : "?", picture);
        _icon.Grey(dim);
        _name.Text = name;
        _name.ThemeTypeVariation = dim ? "DimLabel" : "CellLabel";
        _badge.Text = badge;
        SetPressedNoSignal(picked);
        SetMeta("words", name); // input scripts press it by its name
        TooltipText = "";
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (Drag.Length == 0)
        {
            return default;
        }
        SetDragPreview(ActionSlot.Preview(_icon));
        return Drag;
    }
}
