using Godot;

namespace Yorehold;

/// <summary>A row of round pips, some of them spent: actions left, what an action costs, a reaction.</summary>
public partial class PipsView : Control
{
    [Export] public Color Full { get; set; } = Palette.Leaf;
    [Export] public Color Spent { get; set; } = Palette.Iron;
    [Export] public Color Rim { get; set; } = Palette.Ink;
    [Export] public float PipSize { get; set; } = 16;
    [Export] public float Gap { get; set; } = 4;
    /// <summary>Square boxes, as the hotbar's action, bonus action and reaction marks are drawn.</summary>
    [Export] public bool Square { get; set; }

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
            if (Square)
            {
                // a filled box while it is there to use, an empty outline once spent
                var box = new Rect2(at - new Vector2(r, r), PipSize, PipSize);
                DrawRect(box, i < _filled ? Full : Palette.Ink);
                DrawRect(box, i < _filled ? Full : Palette.Slate, false, 1);
                continue;
            }
            DrawCircle(at, r, Rim);
            DrawCircle(at, r - 1.5f, i < _filled ? Full : Spent);
        }
    }
}
