using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// A conversation drawn as the design's graph: each line a box, its replies in the next column
/// (a reply that rolls titled with its skill and DC in blue), and where each reply goes in the
/// column after: another line, or the end. A line reached twice is drawn once, its arrows meeting
/// there. Laid out from the start line every frame, so it never needs saving. Left picks a box,
/// a double click on a line opens it in the form, right or middle drags the view.
/// </summary>
public partial class DialogueGraphView : Control
{
    private const float BoxWidth = 188;
    private const float BoxHeight = 62;
    private const float ColumnGap = 48;
    private const float RowGap = 14;

    public event Action<int>? LinePicked;
    public event Action<int, int>? ReplyPicked;
    public event Action<int>? LineOpened;

    public DialogueEditor? Editor { get; set; }
    public int Picked { get; set; }
    public int? PickedReply { get; set; }
    public Vector2 Pan { get; set; } = new(16, 16);

    private readonly List<Box> _boxes = new();
    private readonly List<(Rect2 From, Rect2 To, Color Color, string Words)> _arrows = new();
    private readonly List<(Rect2 From, string Words, Color Color)> _stubs = new();
    private bool _panning;

    private sealed record Box(Rect2 Rect, string Head, string Tag, string Text, Color HeadColor, int Line, int Reply, bool End);

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Process(double delta)
    {
        if (IsVisibleInTree())
        {
            QueueRedraw();
        }
    }

    // Lays the conversation out: lines and replies by how far they are from the start.
    private void Lay()
    {
        _boxes.Clear();
        _arrows.Clear();
        _stubs.Clear();
        if (Editor == null || Editor.Nodes.Count == 0)
        {
            return;
        }
        IReadOnlyList<DialogueEditor.Node> nodes = Editor.Nodes;
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < nodes.Count; i++)
        {
            index.TryAdd(nodes[i].Id, i);
        }
        var placed = new Dictionary<int, Rect2>();
        var down = new Dictionary<int, float>();
        var queue = new Queue<(int Line, int Column, float Top)>();
        int start = index.TryGetValue(Editor.Start, out int s) ? s : 0;
        queue.Enqueue((start, 0, 0));
        int replies = 0, checks = 0, ends = 0;

        Rect2 Place(int column, float top)
        {
            float y = Math.Max(down.GetValueOrDefault(column), top);
            down[column] = y + BoxHeight + RowGap;
            return new Rect2(column * (BoxWidth + ColumnGap), y, BoxWidth, BoxHeight);
        }

        void PlaceLine(int line, int column, float top)
        {
            if (placed.ContainsKey(line))
            {
                return;
            }
            DialogueEditor.Node node = nodes[line];
            Rect2 rect = Place(column, top);
            placed[line] = rect;
            _boxes.Add(new Box(rect, node.Speaker.Length > 0 ? node.Speaker : "Narration", $"L{line + 1}", node.Text, Palette.Bone, line, -1, false));
            for (int c = 0; c < node.Choices.Count; c++)
            {
                DialogueEditor.Choice choice = node.Choices[c];
                bool rolls = choice.Check != null;
                string head = rolls ? $"{TurnWords.Capital(choice.Check!.Skill)} DC {choice.Check.Difficulty}" : $"Reply {c + 1}";
                string tag = rolls ? $"C{++checks}" : $"R{++replies}";
                Rect2 reply = Place(column + 1, rect.Position.Y);
                _boxes.Add(new Box(reply, head, tag, choice.Text, rolls ? Palette.Blue : Palette.Ash, line, c, false));
                _arrows.Add((rect, reply, Palette.Slate, ""));
                var targets = rolls
                    ? new List<(string Next, string Words, Color Color)> { (choice.Check!.Success, "pass", Palette.Blue), (choice.Check.Failure, "fail", Palette.Red) }
                    : new List<(string Next, string Words, Color Color)> { (choice.Next, "", Palette.Slate) };
                foreach ((string next, string words, Color color) in targets)
                {
                    if (next.Length > 0 && index.TryGetValue(next, out int to))
                    {
                        if (!placed.ContainsKey(to))
                        {
                            PlaceLine(to, column + 2, reply.Position.Y);
                        }
                        // back to a line already drawn to the left: a short stub naming it, not a line across the graph
                        if (placed[to].Position.X <= reply.Position.X)
                        {
                            _stubs.Add((reply, $"→ L{to + 1}" + (words.Length > 0 ? $" ({words})" : ""), color));
                        }
                        else
                        {
                            _arrows.Add((reply, placed[to], color, words));
                        }
                    }
                    else
                    {
                        Rect2 end = Place(column + 2, reply.Position.Y);
                        _boxes.Add(new Box(end, "Ends conversation", $"E{++ends}", "", Palette.Ash, -1, -1, true));
                        _arrows.Add((reply, end, color, words));
                    }
                }
            }
        }

        PlaceLine(start, 0, 0);
        // lines nothing leads to, under the rest, so every line can be picked
        float bottom = down.Values.DefaultIfEmpty(0).Max() + RowGap * 2;
        for (int i = 0; i < nodes.Count; i++)
        {
            if (!placed.ContainsKey(i))
            {
                PlaceLine(i, 0, bottom);
            }
        }
    }

    public override void _Draw()
    {
        Lay();
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Night);
        // the faint grid the design draws under the boxes
        for (float x = Pan.X % 24; x < Size.X; x += 24)
        {
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), Palette.Ink, 1);
        }
        for (float y = Pan.Y % 24; y < Size.Y; y += 24)
        {
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), Palette.Ink, 1);
        }
        Font font = GetThemeDefaultFont();
        Font mono = GetThemeFont("font", "NumberLabel");
        Font bold = GetThemeFont("font", "TitleLabel");
        foreach ((Rect2 from, Rect2 to, Color color, string words) in _arrows)
        {
            Vector2 a = Pan + new Vector2(from.End.X, from.Position.Y + from.Size.Y / 2);
            Vector2 b = Pan + new Vector2(to.Position.X, to.Position.Y + to.Size.Y / 2);
            float mid = a.X + ColumnGap / 2;
            DrawPolyline(new[] { a, new Vector2(mid, a.Y), new Vector2(mid, b.Y), b }, color, 1);
            DrawColoredPolygon(new[] { b, b + new Vector2(-6, -4), b + new Vector2(-6, 4) }, color);
            if (words.Length > 0)
            {
                // in the gap after the bend, short enough to fit before the box
                DrawString(font, new Vector2(mid + 2, b.Y - 4), words, HorizontalAlignment.Left, -1, 10, color);
            }
        }
        int stubRow = 0;
        Rect2? lastStub = null;
        foreach ((Rect2 from, string words, Color color) in _stubs)
        {
            stubRow = lastStub == from ? stubRow + 1 : 0;
            lastStub = from;
            Vector2 a = Pan + new Vector2(from.End.X, from.Position.Y + 18 + stubRow * 14);
            DrawLine(a, a + new Vector2(10, 0), color, 1);
            DrawString(font, a + new Vector2(13, 4), words, HorizontalAlignment.Left, -1, 11, color == Palette.Slate ? Palette.Ash : color);
        }
        foreach (Box box in _boxes)
        {
            var rect = new Rect2(Pan + box.Rect.Position, box.Rect.Size);
            bool picked = box.Line == Picked && (box.Reply < 0 ? PickedReply == null : PickedReply == box.Reply) && !box.End;
            DrawRect(rect, Palette.Ink);
            DrawRect(rect, picked ? Palette.Straw : Palette.Iron, false, picked ? 2 : 1);
            DrawString(bold, rect.Position + new Vector2(8, 16), box.Head, HorizontalAlignment.Left, rect.Size.X - 44, 12, box.HeadColor);
            DrawString(mono, rect.Position + new Vector2(rect.Size.X - 32, 16), box.Tag, HorizontalAlignment.Right, 26, 11, Palette.Ash);
            // the words, two lines at most
            string text = box.Text.Replace('\n', ' ');
            DrawMultilineString(font, rect.Position + new Vector2(8, 33), text, HorizontalAlignment.Left, rect.Size.X - 16, 12, 2, Palette.Sand,
                TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Mandatory);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Right or MouseButton.Middle } drag:
                _panning = drag.Pressed;
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _panning:
                Pan += motion.Relative;
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                Pan += new Vector2(0, wheel.ButtonIndex == MouseButton.WheelUp ? 40 : -40);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
                Box? hit = _boxes.LastOrDefault(b => new Rect2(Pan + b.Rect.Position, b.Rect.Size).HasPoint(press.Position));
                if (hit != null && !hit.End)
                {
                    if (hit.Reply >= 0)
                    {
                        ReplyPicked?.Invoke(hit.Line, hit.Reply);
                    }
                    else if (press.DoubleClick)
                    {
                        LineOpened?.Invoke(hit.Line);
                    }
                    else
                    {
                        LinePicked?.Invoke(hit.Line);
                    }
                }
                AcceptEvent();
                break;
        }
    }
}
