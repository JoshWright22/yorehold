using Godot;

namespace Yorehold;

/// <summary>One square on the hotbar: an action's icon, the key that picks it and what it costs.</summary>
public partial class ActionSlot : TipButton
{
    public string ActionId { get; private set; } = "";
    public bool Usable { get; private set; }

    // the scene has all of these
    private ActionIcon _icon = null!;
    private Label _key = null!;
    private PipsView _cost = null!;
    private Panel _armed = null!;

    public override void _Ready()
    {
        base._Ready();
        _icon = GetNode<ActionIcon>("Icon");
        _key = GetNode<Label>("Key");
        _cost = GetNode<PipsView>("Cost");
        _armed = GetNode<Panel>("Armed");
    }

    /// <summary>usable false greys it out; it can still be pressed, which says why.</summary>
    public void Show(string id, string shape, string name, string key, int cost, bool usable, bool armed)
    {
        ActionId = id;
        Usable = usable;
        _icon.Show(shape, name.Length > 0 ? name[..1] : "?");
        _icon.Grey(!usable);
        _key.Text = key;
        // a greyed action's cost shows as spent pips, not as see-through ones
        _cost.Show(cost, usable ? cost : 0);
        _armed.Visible = armed;
    }
}
