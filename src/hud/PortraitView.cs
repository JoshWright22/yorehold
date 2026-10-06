using Godot;

namespace Yorehold;

/// <summary>
/// A creature's face on a card. Until there is portrait art it is the creature's token: the same
/// disc and initial as on the map, so a card is easy to match to who it is.
/// </summary>
public partial class PortraitView : Control
{
    [Export] public Color Back { get; set; } = Palette.Night;

    private string _name = "";
    private Color _color = Palette.Smoke;
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
        Color color = _down ? Palette.Slate : _color;
        DrawCircle(middle, r, Palette.Ink);
        DrawCircle(middle, r * 0.9f, color);
        if (_name.Length > 0)
        {
            Font font = ThemeDB.FallbackFont;
            int size = Mathf.Max(8, (int)(r * 1.1f));
            string initial = _name[..1];
            Vector2 measure = font.GetStringSize(initial, HorizontalAlignment.Left, -1, size);
            DrawString(font, middle + new Vector2(-measure.X / 2, font.GetAscent(size) / 2 - 2), initial, HorizontalAlignment.Left, -1, size,
                Palette.Ink);
        }
        if (_down)
        {
            float x = r * 0.6f;
            DrawLine(middle + new Vector2(-x, -x), middle + new Vector2(x, x), Palette.Red, 3);
            DrawLine(middle + new Vector2(-x, x), middle + new Vector2(x, -x), Palette.Red, 3);
        }
    }
}
