using System;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>A recorded line's words along its length: green heard, red heard unsurely, amber timed by guess.</summary>
public partial class VoiceStrip : Control
{
    private VoiceLine? _line;
    private float _flagBelow = 0.6f;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 26);
    }

    public static Color ColorOf(VoiceWord word, float flagBelow) => !word.Matched ? Palette.Amber : word.Confidence < flagBelow ? Palette.Red : Palette.Leaf;

    public void Show(VoiceLine? line, float flagBelow)
    {
        if (!ReferenceEquals(line, _line) || flagBelow != _flagBelow)
        {
            _line = line;
            _flagBelow = flagBelow;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Ink);
        if (_line == null || _line.Words.Count == 0)
        {
            return;
        }
        double length = Math.Max(0.01, _line.Words[^1].End);
        foreach (VoiceWord word in _line.Words)
        {
            float from = (float)(word.Start / length) * Size.X, to = (float)(word.End / length) * Size.X;
            DrawRect(new Rect2(from + 1, 3, Math.Max(2, to - from - 2), Size.Y - 6), Palette.Faded(ColorOf(word, _flagBelow), 0.6f));
        }
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Leather, false, 1);
    }
}
