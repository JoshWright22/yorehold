using System;
using Godot;

namespace Yorehold;

/// <summary>
/// One square on the hotbar: an action's icon, the key that picks it and what it costs, or empty.
/// An action can be dragged onto it from the spell book or another slot, and dragged off it onto
/// the map to take it off the bar.
/// </summary>
public partial class ActionSlot : TipButton
{
    /// <summary>What a drag from a slot carries: "slot:" and its place.</summary>
    public const string SlotDrag = "slot:";
    /// <summary>What a drag from the spell book carries: "action:" and the action's id.</summary>
    public const string ActionDrag = "action:";

    public string ActionId { get; private set; } = "";
    public bool Usable { get; private set; }
    /// <summary>Its place on the bars, 0 first.</summary>
    public int Index { get; set; }

    /// <summary>Something was dropped on it: a slot's or a book's drag text.</summary>
    public event Action<ActionSlot, string>? Dropped;
    /// <summary>Its action was dragged away and let go somewhere that isn't a slot.</summary>
    public event Action<ActionSlot>? DraggedOff;

    // the scene has all of these
    private ActionIcon _icon = null!;
    private Label _key = null!;
    private PipsView _cost = null!;
    private Panel _armed = null!;
    private bool _dragFromHere;

    public override void _Ready()
    {
        base._Ready();
        _icon = GetNode<ActionIcon>("Icon");
        _key = GetNode<Label>("Key");
        _cost = GetNode<PipsView>("Cost");
        _armed = GetNode<Panel>("Armed");
    }

    /// <summary>usable false greys it out; it can still be pressed, which says why.</summary>
    public void Show(string id, string shape, string name, string key, int cost, bool usable, bool armed, Texture2D? picture = null)
    {
        ActionId = id;
        Usable = usable;
        _icon.Visible = true;
        _icon.Show(shape, name.Length > 0 ? name[..1] : "?", picture);
        _icon.Grey(!usable);
        _key.Text = key;
        // a greyed action's cost shows as spent pips, not as see-through ones
        _cost.Visible = true;
        _cost.Show(cost, usable ? cost : 0);
        _armed.Visible = armed;
        SetMeta("words", "Bar: " + name); // input scripts press a slot by its action's name, apart from the spell book's
    }

    /// <summary>A slot with nothing on it, waiting for something to be dragged there.</summary>
    public void ShowEmpty(string key)
    {
        ActionId = "";
        Usable = false;
        _icon.Visible = false;
        _cost.Visible = false;
        _armed.Visible = false;
        _key.Text = key;
        SetMeta("words", "");
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (ActionId.Length == 0)
        {
            return default;
        }
        CancelHold();
        _dragFromHere = true;
        SetDragPreview(Preview(_icon));
        return SlotDrag + Index;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        return data.VariantType == Variant.Type.String && (data.AsString().StartsWith(SlotDrag) || data.AsString().StartsWith(ActionDrag));
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        Dropped?.Invoke(this, data.AsString());
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd && _dragFromHere)
        {
            _dragFromHere = false;
            if (!GetViewport().GuiIsDragSuccessful())
            {
                DraggedOff?.Invoke(this);
            }
        }
    }

    /// <summary>The icon that follows the pointer while an action is dragged.</summary>
    public static Control Preview(ActionIcon from)
    {
        var frame = new Panel { Size = new Vector2(48, 48), ThemeTypeVariation = "TurnFrame", MouseFilter = MouseFilterEnum.Ignore };
        ActionIcon icon = from.Copy();
        icon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        icon.OffsetLeft = icon.OffsetTop = 8;
        icon.OffsetRight = icon.OffsetBottom = -8;
        frame.AddChild(icon);
        // the preview sits with its middle on the pointer
        var holder = new Control { MouseFilter = MouseFilterEnum.Ignore };
        frame.Position = new Vector2(-24, -24);
        holder.AddChild(frame);
        return holder;
    }
}
