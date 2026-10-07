using System;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The story graph in the middle of Story mode: a box per node with its kind's colour down the
/// left, links as arrows between them, a faint grid. Left on a node picks and drags it, left on a
/// link picks it, left on nothing or right drags the view. While linking, a line follows the pointer.
/// </summary>
public partial class StoryGraphView : Control
{
    private const float GridStep = 40;

    public event Action<int>? NodeClicked;
    public event Action<int>? LinkClicked;
    public event Action? BackgroundClicked;

    public StoryEditor? Editor { get; set; }
    public int? Node { get; set; }
    public int? Link { get; set; }
    public bool Linking { get; set; }
    public Vector2 Pan { get; set; } = new(20, 20);

    private int? _dragging;
    private Vector2 _grab;
    private bool _panning;

    public static Color ColorOf(StoryEditor.Kind kind) => kind switch
    {
        StoryEditor.Kind.Scene => Palette.Blue,
        StoryEditor.Kind.Encounter => Palette.Red,
        StoryEditor.Kind.Dialogue => Palette.Leaf,
        StoryEditor.Kind.Quest => Palette.Amber,
        _ => Palette.Orchid,
    };

    public override void _Ready()
    {
        ClipContents = true;
        MouseFilter = MouseFilterEnum.Stop;
        FocusMode = FocusModeEnum.Click;
    }

    /// <summary>Brings a node into view near the top left.</summary>
    public void Show(int node)
    {
        if (Editor != null && node >= 0 && node < Editor.Nodes.Count)
        {
            Pan = new Vector2(120, 100) - Editor.Nodes[node].At.ToGodot();
            QueueRedraw();
        }
    }

    private Rect2 RectOf(StoryEditor.Node node) => new(Pan + node.At.ToGodot(), new Vector2(StoryEditor.NodeWidth, StoryEditor.NodeHeight));

    public override void _GuiInput(InputEvent @event)
    {
        if (Editor == null)
        {
            return;
        }
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } press:
            {
                GrabFocus();
                int? over = NodeAt(press.Position);
                if (over is int node)
                {
                    bool linking = Linking;
                    NodeClicked?.Invoke(node);
                    if (!linking)
                    {
                        _dragging = node;
                        _grab = press.Position - (Pan + Editor.Nodes[node].At.ToGodot());
                        Editor.EndTyping();
                    }
                }
                else if (LinkAt(press.Position) is int link)
                {
                    LinkClicked?.Invoke(link);
                }
                else
                {
                    _panning = true;
                    BackgroundClicked?.Invoke();
                }
                AcceptEvent();
                break;
            }
            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                _panning = true;
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left or MouseButton.Right }:
                if (_dragging != null)
                {
                    Editor.EndTyping();
                }
                _dragging = null;
                _panning = false;
                AcceptEvent();
                break;
            case InputEventMouseMotion motion:
                if (_dragging is int dragged && dragged < Editor.Nodes.Count)
                {
                    Editor.MoveNode(dragged, (motion.Position - _grab - Pan).ToRules());
                }
                else if (_panning)
                {
                    Pan += motion.Relative;
                }
                QueueRedraw();
                break;
        }
    }

    public override void _Process(double delta)
    {
        if (IsVisibleInTree())
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Ink);
        // a faint grid, so moving the view reads as moving
        Color grid = Palette.Faded(Palette.Iron, 0.5f);
        for (float x = Mathf.PosMod(Pan.X, GridStep); x < Size.X; x += GridStep)
        {
            DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), grid, 1);
        }
        for (float y = Mathf.PosMod(Pan.Y, GridStep); y < Size.Y; y += GridStep)
        {
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), grid, 1);
        }
        if (Editor == null)
        {
            return;
        }
        Font font = GetThemeDefaultFont();

        // links under the nodes, each with an arrowhead where it arrives
        for (int i = 0; i < Editor.Links.Count; i++)
        {
            (Vector2 from, Vector2 to)? ends = Ends(i);
            if (ends is not (Vector2 from, Vector2 to))
            {
                continue;
            }
            bool picked = Link == i;
            Color color = picked ? Palette.Straw : Editor.Links[i].When.Count == 0 ? Palette.Smoke : Palette.Amber;
            DrawLine(from, to, color, picked ? 3 : 2);
            Vector2 back = (to - from).Normalized();
            Vector2 across = new(-back.Y, back.X);
            DrawLine(to, to - back * 12 + across * 6, color, 2);
            DrawLine(to, to - back * 12 - across * 6, color, 2);
            if (Editor.Links[i].Text.Length > 0)
            {
                Vector2 middle = (from + to) / 2;
                DrawString(font, middle + new Vector2(-80, -8), Editor.Links[i].Text, HorizontalAlignment.Center, 160, 12, Palette.Ash);
            }
        }

        for (int i = 0; i < Editor.Nodes.Count; i++)
        {
            StoryEditor.Node node = Editor.Nodes[i];
            Rect2 r = RectOf(node);
            if (!r.Intersects(new Rect2(Vector2.Zero, Size)))
            {
                continue;
            }
            DrawRect(r, Palette.Ink);
            DrawRect(new Rect2(r.Position, new Vector2(6, r.Size.Y)), ColorOf(node.Kind));
            DrawRect(r, Node == i ? Palette.Straw : Palette.Slate, false, Node == i ? 2 : 1);
            DrawString(font, r.Position + new Vector2(12, 20), StoryEditor.NameOf(node), HorizontalAlignment.Left, r.Size.X - 18, 14, Palette.Bone);
            string under = StoryEditor.KindName(node.Kind);
            if (node.Ref.Length > 0)
            {
                under += ": " + node.Ref[(node.Ref.LastIndexOf('/') + 1)..];
            }
            else if (node.Kind == StoryEditor.Kind.Scene && node.Chapter.Length > 0)
            {
                under += ": " + CreatePackage.Leaf(node.Chapter);
            }
            if (node.Xp is int xp)
            {
                under += $", {xp} xp";
            }
            DrawString(font, r.Position + new Vector2(12, 40), under, HorizontalAlignment.Left, r.Size.X - 18, 12, Palette.Smoke);
        }

        if (Linking && Node is int start && start < Editor.Nodes.Count)
        {
            Vector2 mouse = GetLocalMousePosition();
            DrawLine(EdgeOf(RectOf(Editor.Nodes[start]), mouse), mouse, Palette.Straw, 2);
        }
        string help = Linking ? "Click the node the link goes to, Escape stops" : "Drag nodes to move them, drag the background to look around";
        DrawString(font, new Vector2(10, Size.Y - 10), help, HorizontalAlignment.Left, -1, 12, Palette.Smoke);
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Iron, false, 1);
    }

    private (Vector2, Vector2)? Ends(int link)
    {
        if (Editor == null || Editor.Find(Editor.Links[link].From) is not int a || Editor.Find(Editor.Links[link].To) is not int b)
        {
            return null;
        }
        Rect2 ra = RectOf(Editor.Nodes[a]), rb = RectOf(Editor.Nodes[b]);
        Vector2 ca = ra.GetCenter(), cb = rb.GetCenter();
        // two links between the same pair sit a little apart
        bool both = Editor.FindLink(Editor.Links[link].To, Editor.Links[link].From) != null;
        Vector2 d = cb - ca;
        Vector2 side = both && d.Length() > 0 ? new Vector2(-d.Y, d.X).Normalized() * 5 : Vector2.Zero;
        return (EdgeOf(ra, cb) + side, EdgeOf(rb, ca) + side);
    }

    private int? NodeAt(Vector2 at)
    {
        if (Editor == null)
        {
            return null;
        }
        // the last drawn is on top
        for (int i = Editor.Nodes.Count - 1; i >= 0; i--)
        {
            if (RectOf(Editor.Nodes[i]).HasPoint(at))
            {
                return i;
            }
        }
        return null;
    }

    private int? LinkAt(Vector2 at)
    {
        if (Editor == null)
        {
            return null;
        }
        for (int i = 0; i < Editor.Links.Count; i++)
        {
            if (Ends(i) is (Vector2 from, Vector2 to) && Geometry2D.GetClosestPointToSegment(at, from, to).DistanceTo(at) < 6)
            {
                return i;
            }
        }
        return null;
    }

    // Where the line from the middle of box toward a point leaves it.
    private static Vector2 EdgeOf(Rect2 box, Vector2 toward)
    {
        Vector2 c = box.GetCenter();
        Vector2 d = toward - c;
        if (Math.Abs(d.X) < 1e-3f && Math.Abs(d.Y) < 1e-3f)
        {
            return c;
        }
        float sx = Math.Abs(d.X) > 1e-3f ? box.Size.X / 2 / Math.Abs(d.X) : 1e9f;
        float sy = Math.Abs(d.Y) > 1e-3f ? box.Size.Y / 2 / Math.Abs(d.Y) : 1e9f;
        return c + d * Math.Min(sx, sy);
    }
}
