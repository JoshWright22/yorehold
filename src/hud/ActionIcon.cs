using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// An action's picture on the hotbar and in the spell book: the creator's icon when the content or
/// an art pack has one, else its first letter. The game draws no icons of its own.
/// </summary>
public partial class ActionIcon : Control
{
    [Export] public Color Ink { get; set; } = Palette.Sand;

    /// <summary>Greyed: the letter in slate, a picture dimmed.</summary>
    public void Grey(bool grey)
    {
        Color ink = grey ? Palette.Slate : Palette.Sand;
        if (ink != Ink)
        {
            Ink = ink;
            QueueRedraw();
        }
    }

    private string _letter = "";
    private Texture2D? _picture;

    /// <summary>The creator's icon for an action, icons/&lt;action id&gt;.png in the content or an art pack; null when there is none.</summary>
    public static Texture2D? PictureOf(World world, string action) => PlayerArt.Texture(world.Files, $"icons/{action}.png");

    /// <summary>A new icon drawn the same, for the one that follows the pointer in a drag.</summary>
    public ActionIcon Copy()
    {
        var copy = new ActionIcon { Ink = Ink, MouseFilter = MouseFilterEnum.Ignore };
        copy.Show(_letter, _picture);
        return copy;
    }

    /// <summary>picture, when there is one, is drawn instead of the letter.</summary>
    public void Show(string letter, Texture2D? picture = null)
    {
        if (letter == _letter && picture == _picture)
        {
            return;
        }
        _letter = letter;
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
        if (_picture != null)
        {
            DrawTextureRect(_picture, new Rect2(Vector2.Zero, Size), false, Ink == Palette.Slate ? Palette.Smoke : Colors.White);
            return;
        }
        Font font = ThemeDB.FallbackFont;
        int size = Mathf.Max(8, (int)(Size.Y * 0.6f));
        Vector2 measure = font.GetStringSize(_letter, HorizontalAlignment.Left, -1, size);
        DrawString(font, new Vector2((Size.X - measure.X) / 2, Size.Y / 2 + font.GetAscent(size) / 2 - 2), _letter, HorizontalAlignment.Left, -1, size, Ink);
    }
}
