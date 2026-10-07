using Godot;

namespace Yorehold;

/// <summary>
/// A creature's face on a card: its picture filling a 1:1 square in the middle of the card, or
/// without one its token, the same disc and initial as on the map, so a card is easy to match to
/// who it is. Contain (conversations) instead shows the whole picture standing, at its own shape.
/// </summary>
public partial class PortraitView : Control
{
    [Export] public Color Back { get; set; } = Palette.Iron;
    /// <summary>The whole picture standing on the bottom edge, as a conversation shows someone, instead of filling the card.</summary>
    [Export] public bool Contain { get; set; }
    /// <summary>Mirrored, so someone on the right of the screen faces the one on the left.</summary>
    [Export] public bool Flip { get; set; }

    private string _name = "";
    private Color _color = Palette.Smoke;
    private bool _down;
    private Texture2D? _picture;

    public override void _Ready()
    {
        // the pictures are small pixel art, drawn large
        TextureFilter = TextureFilterEnum.Nearest;
    }

    public void Show(string name, Color color, bool down, Texture2D? picture = null)
    {
        if (name == _name && Palette.Nearest(color) == _color && down == _down && picture == _picture)
        {
            return;
        }
        _name = name;
        // token colours come from content, so they land on the palette here like on the map
        _color = Palette.Nearest(color);
        _down = down;
        _picture = picture;
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
        if (!Contain)
        {
            // a face is always 1:1: the biggest square in the middle of whatever box it is given
            float side = Mathf.Floor(Mathf.Min(Size.X, Size.Y));
            DrawSetTransform(((Size - new Vector2(side, side)) / 2).Floor(), 0, Vector2.One);
            DrawFace(new Vector2(side, side));
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
            return;
        }
        DrawFace(Size);
    }

    private void DrawFace(Vector2 size)
    {
        DrawRect(new Rect2(Vector2.Zero, size), Back);
        Vector2 middle = size / 2;
        float r = Mathf.Min(size.X, size.Y) * 0.42f;
        if (Contain)
        {
            // standing on the bottom edge like a picture would, not floating in the middle
            middle = new Vector2(size.X / 2, size.Y - r - 4);
        }
        Color color = _down ? Palette.Slate : _color;
        if (_picture != null && Contain)
        {
            Vector2 whole = _picture.GetSize();
            float fit = Mathf.Min(size.X / whole.X, size.Y / whole.Y);
            Vector2 drawn = whole * fit;
            var at = new Rect2(new Vector2((size.X - drawn.X) / 2, size.Y - drawn.Y), drawn);
            if (Flip)
            {
                DrawSetTransform(new Vector2(size.X, 0), 0, new Vector2(-1, 1));
            }
            DrawTextureRect(_picture, at, false, _down ? Palette.Slate : Colors.White);
            DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        }
        else if (_picture != null)
        {
            // the picture covers the card: its middle is kept and what sticks out is cut off
            Vector2 whole = _picture.GetSize();
            float scale = Mathf.Max(size.X / whole.X, size.Y / whole.Y);
            Vector2 part = size / scale;
            DrawTextureRectRegion(_picture, new Rect2(Vector2.Zero, size), new Rect2((whole - part) / 2, part), _down ? Palette.Slate : Colors.White);
            // the token's colour along the bottom ties the card to the disc on the map
            DrawRect(new Rect2(0, size.Y - 3, size.X, 3), color);
        }
        else
        {
            DrawCircle(middle, r, Palette.Ink);
            DrawCircle(middle, r * 0.9f, color);
        }
        if (_picture == null && _name.Length > 0)
        {
            Font font = ThemeDB.FallbackFont;
            int letter = Mathf.Max(8, (int)(r * 1.1f));
            string initial = _name[..1];
            Vector2 measure = font.GetStringSize(initial, HorizontalAlignment.Left, -1, letter);
            DrawString(font, middle + new Vector2(-measure.X / 2, font.GetAscent(letter) / 2 - 2), initial, HorizontalAlignment.Left, -1, letter,
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
