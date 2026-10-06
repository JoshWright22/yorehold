using Godot;

namespace Yorehold;

/// <summary>
/// A condition as a small square with its first two letters, until conditions get icons. What it
/// does is in the card's tooltip.
/// </summary>
public partial class ConditionBadge : Control
{
    [Export] public Color Back { get; set; } = new(0.3f, 0.2f, 0.34f);
    [Export] public Color Ink { get; set; } = new(0.96f, 0.92f, 0.84f);

    private string _letters = "";

    public ConditionBadge()
    {
        CustomMinimumSize = new Vector2(18, 14);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(string name)
    {
        string letters = name.Length <= 2 ? name : name[..2];
        if (letters != _letters)
        {
            _letters = letters;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Back);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, 0.6f), false, 1);
        Font font = ThemeDB.FallbackFont;
        const int size = 10;
        Vector2 measure = font.GetStringSize(_letters, HorizontalAlignment.Left, -1, size);
        DrawString(font, new Vector2((Size.X - measure.X) / 2, Size.Y / 2 + font.GetAscent(size) / 2 - 1), _letters, HorizontalAlignment.Left, -1, size, Ink);
    }
}
