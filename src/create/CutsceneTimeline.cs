using System;
using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The bar under Cutscene mode's preview: a block per step from when it starts to when it ends,
/// steps that run at once in rows under each other, and a line where the preview is. A click on a
/// block picks that step; a click or drag on the bar jumps the preview there.
/// </summary>
public partial class CutsceneTimeline : Control
{
    private const int Rows = 3;

    /// <summary>A step's block was clicked.</summary>
    public event Action<int>? Picked;
    /// <summary>The bar was clicked or dragged on, at this many seconds.</summary>
    public event Action<double>? Scrubbed;

    private IReadOnlyList<CutsceneEditor.Step> _steps = Array.Empty<CutsceneEditor.Step>();
    private List<CutsceneEditor.Span> _spans = new();
    private double _length = 1;
    private double _time;
    private int _picked;
    private bool _scrubbing;
    private readonly List<(Rect2 Block, int Step)> _blocks = new();

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0, 64);
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
    }

    public static Color ColorOf(CutsceneEditor.Kind kind) => kind switch
    {
        CutsceneEditor.Kind.Camera => Palette.Blue,
        CutsceneEditor.Kind.Caption => Palette.Olive,
        CutsceneEditor.Kind.Title => Palette.Rust,
        CutsceneEditor.Kind.Fade => Palette.Plum,
        CutsceneEditor.Kind.Bars => Palette.Iron,
        CutsceneEditor.Kind.Event => Palette.Teal,
        _ => Palette.Slate,
    };

    public void Show(IReadOnlyList<CutsceneEditor.Step> steps, List<CutsceneEditor.Span> spans, double length, double time, int picked)
    {
        _steps = steps;
        _spans = spans;
        _length = Math.Max(length, 1);
        _time = time;
        _picked = picked;
        QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left } button)
        {
            if (button.Pressed)
            {
                foreach ((Rect2 block, int step) in _blocks)
                {
                    if (block.HasPoint(button.Position))
                    {
                        Picked?.Invoke(step);
                        AcceptEvent();
                        return;
                    }
                }
                _scrubbing = true;
                Scrubbed?.Invoke(TimeAt(button.Position.X));
            }
            else
            {
                _scrubbing = false;
            }
            AcceptEvent();
        }
        else if (@event is InputEventMouseMotion motion && _scrubbing)
        {
            Scrubbed?.Invoke(TimeAt(motion.Position.X));
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Ink);
        _blocks.Clear();
        float X(double t) => 4 + (float)(t / _length) * (Size.X - 8);
        var rowEnd = new double[Rows];
        Array.Fill(rowEnd, -1);
        float rowHeight = (Size.Y - 8) / Rows;
        Font font = GetThemeDefaultFont();
        for (int i = 0; i < _steps.Count && i < _spans.Count; i++)
        {
            int row = 0;
            while (row + 1 < Rows && rowEnd[row] > _spans[i].Start + 1e-9)
            {
                row++;
            }
            rowEnd[row] = Math.Max(_spans[i].End, _spans[i].Start + _length * 0.01);
            float x0 = X(_spans[i].Start), x1 = Math.Max(X(_spans[i].End), x0 + 4);
            var block = new Rect2(x0, 4 + row * rowHeight, x1 - x0 - 1, rowHeight - 2);
            DrawRect(block, ColorOf(_steps[i].Kind));
            if (i == _picked)
            {
                DrawRect(block, Palette.Straw, false, 2);
            }
            string name = CutsceneEditor.KindName(_steps[i].Kind);
            if (block.Size.X > font.GetStringSize(name, HorizontalAlignment.Left, -1, 12).X + 8)
            {
                DrawString(font, block.Position + new Vector2(4, block.Size.Y / 2 + 4), name, HorizontalAlignment.Left, -1, 12, Palette.Bone);
            }
            _blocks.Add((block, i));
        }
        float at = X(Math.Min(_time, _length));
        DrawLine(new Vector2(at, 0), new Vector2(at, Size.Y), Palette.Bone, 2);
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Iron, false, 1);
    }

    private double TimeAt(float x) => Math.Clamp((x - 4) / Math.Max(1, Size.X - 8), 0, 1) * _length;
}
