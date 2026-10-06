using Godot;

namespace Yorehold;

/// <summary>A row of round pips, some of them spent: actions left, what an action costs, a reaction.</summary>
public partial class PipsView : Control
{
    [Export] public Color Full { get; set; } = Palette.Leaf;
    [Export] public Color Spent { get; set; } = Palette.Night;
    [Export] public Color Rim { get; set; } = Palette.Ink;
    [Export] public float PipSize { get; set; } = 16;
    [Export] public float Gap { get; set; } = 4;

    private int _total;
    private int _filled;

    public void Show(int total, int filled)
    {
        if (total == _total && filled == _filled)
        {
            return;
        }
        _total = total;
        _filled = filled;
        CustomMinimumSize = new Vector2(total * PipSize + Mathf.Max(0, total - 1) * Gap, PipSize);
        QueueRedraw();
    }

    public override void _Draw()
    {
        float r = PipSize / 2;
        for (int i = 0; i < _total; i++)
        {
            var at = new Vector2(r + i * (PipSize + Gap), Size.Y / 2);
            DrawCircle(at, r, Rim);
            DrawCircle(at, r - 1.5f, i < _filled ? Full : Spent);
        }
    }
}
