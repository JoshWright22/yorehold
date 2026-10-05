using Godot;

namespace Yorehold;

public partial class Main : Node2D
{
    public override void _Ready()
    {
        GD.Print($"Yorehold {Rules.Version.Text} ready");
    }
}
