using System.Collections.Generic;
using Godot;

namespace Yorehold;

/// <summary>Short words that rise from a spot on the map and fade: "Trap!", damage, why a click did nothing.</summary>
public partial class FloatersView : Node2D
{
    [Export] public float Seconds { get; set; } = 1.4f;
    [Export] public int FontSize { get; set; } = 22;

    private sealed class Floater
    {
        public Vector2 At;
        public string Text = "";
        public Color Color;
        public float Scale = 1;
        public float Age;
    }

    private readonly List<Floater> _floaters = new();

    /// <summary>scale makes the words bigger than FontSize, for numbers that matter.</summary>
    public void Add(Vector2 at, string text, Color color, float scale = 1)
    {
        // two from the same spot at once would sit on top of each other: the later one starts higher
        foreach (Floater other in _floaters)
        {
            if (other.Age < 0.4f && other.At.DistanceTo(at) < 12)
            {
                at.Y -= FontSize * 1.3f;
            }
        }
        _floaters.Add(new Floater { At = at, Text = text, Color = color, Scale = scale });
    }

    public void Clear()
    {
        _floaters.Clear();
    }

    public override void _Process(double delta)
    {
        foreach (Floater f in _floaters)
        {
            f.Age += (float)delta;
        }
        _floaters.RemoveAll(f => f.Age > Seconds);
        QueueRedraw();
    }

    public override void _Draw()
    {
        Font font = ThemeDB.FallbackFont;
        foreach (Floater f in _floaters)
        {
            Color color = f.Color;
            color.A = Mathf.Clamp(1.5f - f.Age / Seconds * 1.5f, 0, 1);
            int fontSize = Mathf.RoundToInt(FontSize * f.Scale);
            Vector2 size = font.GetStringSize(f.Text, HorizontalAlignment.Left, -1, fontSize);
            Vector2 at = f.At + new Vector2(-size.X / 2, -30 - f.Age * 40);
            DrawStringOutline(font, at, f.Text, HorizontalAlignment.Left, -1, fontSize, 6, new Color(0, 0, 0, color.A));
            DrawString(font, at, f.Text, HorizontalAlignment.Left, -1, fontSize, color);
        }
    }
}
