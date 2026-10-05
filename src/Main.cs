using Godot;

namespace Yorehold;

/// <summary>The start scene. It holds the play screen for now; the title and menus come with P11.</summary>
public partial class Main : Node
{
    public override void _Ready()
    {
        GD.Print($"Yorehold {Rules.Version.Text} ready");
    }
}
