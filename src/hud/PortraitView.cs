using Godot;

namespace Yorehold;

/// <summary>
/// A creature's face on a card. Until there is portrait art it is the creature's token: the same
/// disc and initial as on the map, so a card is easy to match to who it is.
/// </summary>
public partial class PortraitView : Control
{
    [Export] public Color Back { get; set; } = new(0.11f, 0.1f, 0.12f);

    private string _name = "";
    private Color _color = Colors.Gray;
    private bool _down;

    public void Show(string name, Color color, bool down)
    {
        if (name == _name && color == _color && down == _down)
        {
            return;
        }
        _name = name;
        _color = color;
        _down = down;
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
        DrawRect(new Rect2(Vector2.Zero, Size), Back);
        Vector2 middle = Size / 2;
        float r = Mathf.Min(Size.X, Size.Y) * 0.42f;
        Color color = _down ? new Color(0.33f, 0.33f, 0.36f) : _color;
        DrawCircle(middle, r, new Color(0.05f, 0.05f, 0.06f));
        DrawCircle(middle, r * 0.9f, color);
        if (_name.Length > 0)
        {
            Font font = ThemeDB.FallbackFont;
            int size = Mathf.Max(8, (int)(r * 1.1f));
            string initial = _name[..1];
            Vector2 measure = font.GetStringSize(initial, HorizontalAlignment.Left, -1, size);
            DrawString(font, middle + new Vector2(-measure.X / 2, font.GetAscent(size) / 2 - 2), initial, HorizontalAlignment.Left, -1, size,
                new Color(0.08f, 0.08f, 0.1f));
        }
        if (_down)
        {
            float x = r * 0.6f;
            DrawLine(middle + new Vector2(-x, -x), middle + new Vector2(x, x), new Color(0.6f, 0.15f, 0.12f), 3);
            DrawLine(middle + new Vector2(-x, x), middle + new Vector2(x, -x), new Color(0.6f, 0.15f, 0.12f), 3);
        }
    }
}
