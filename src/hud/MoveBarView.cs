using Godot;

namespace Yorehold;

/// <summary>
/// Movement left this turn as a bar. The part a move under the pointer would use up is drawn
/// paler, so the cost shows before the click.
/// </summary>
public partial class MoveBarView : Control
{
    [Export] public Color Fill { get; set; } = new(0.86f, 0.7f, 0.36f);
    [Export] public Color Preview { get; set; } = new(0.98f, 0.95f, 0.85f);
    [Export] public Color Back { get; set; } = new(0.03f, 0.03f, 0.035f);
    [Export] public Color Rim { get; set; } = new(0.3f, 0.25f, 0.18f);

    private float _max = 1;
    private float _left;
    private float _cost;

    public void Show(float max, float left, float cost)
    {
        if (max == _max && left == _left && cost == _cost)
        {
            return;
        }
        _max = Mathf.Max(1, max);
        _left = Mathf.Clamp(left, 0, _max);
        _cost = Mathf.Clamp(cost, 0, _left);
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var box = new Rect2(Vector2.Zero, Size);
        DrawRect(box, Back);
        float inner = Size.X - 2;
        float kept = inner * (_left - _cost) / _max;
        float used = inner * _cost / _max;
        DrawRect(new Rect2(1, 1, kept, Size.Y - 2), Fill);
        if (used > 0)
        {
            DrawRect(new Rect2(1 + kept, 1, used, Size.Y - 2), Preview);
        }
        // a tick per square, so the bar reads as squares and not as a smooth meter
        for (int i = 1; i < (int)_max; i++)
        {
            float x = 1 + inner * i / _max;
            DrawLine(new Vector2(x, 1), new Vector2(x, Size.Y - 1), new Color(0, 0, 0, 0.45f), 1);
        }
        DrawRect(box, Rim, false, 1);
    }
}
