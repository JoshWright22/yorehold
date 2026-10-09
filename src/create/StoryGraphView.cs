using System;
using System.Linq;
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
        Font bold = GetThemeFont("font", "TitleLabel");

        // links under the nodes, routed at right angles as the design draws them, an arrowhead
        // where each arrives and its words beside the bend
        for (int i = 0; i < Editor.Links.Count; i++)
        {
            if (Route(i) is not Vector2[] route)
            {
                continue;
            }
            bool picked = Link == i;
            Color color = picked ? Palette.Straw : Editor.Links[i].When.Count == 0 ? Palette.Smoke : Palette.Amber;
            DrawPolyline(route, color, picked ? 2 : 1);
            Vector2 to = route[^1];
            Vector2 back = (to - route[^2]).Normalized();
            Vector2 across = new(-back.Y, back.X);
            DrawColoredPolygon(new[] { to, to - back * 9 + across * 5, to - back * 9 - across * 5 }, color);
            if (Editor.Links[i].Text.Length > 0)
            {
                Vector2 bend = (route[1] + route[2]) / 2;
                bool upright = Mathf.IsEqualApprox(route[1].X, route[2].X);
                Vector2 at = upright ? bend + new Vector2(-150, 4) : bend + new Vector2(-75, -6);
                DrawString(font, at, Editor.Links[i].Text, upright ? HorizontalAlignment.Right : HorizontalAlignment.Center, upright ? 144 : 150, 12, Palette.Ash);
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
            DrawRect(r, Palette.Dusk);
            DrawRect(r, Node == i ? Palette.Straw : Palette.Slate, false, Node == i ? 2 : 1);
            DrawString(bold, r.Position + new Vector2(10, 18), StoryEditor.NameOf(node), HorizontalAlignment.Left, r.Size.X - 20, 13, Palette.Bone);
            string under = node.Ref.Length > 0 ? node.Ref[(node.Ref.LastIndexOf('/') + 1)..]
                : node.Kind == StoryEditor.Kind.Scene && node.Chapter.Length > 0 ? CreatePackage.Leaf(node.Chapter) : "";
            DrawString(font, r.Position + new Vector2(10, 34), under, HorizontalAlignment.Left, r.Size.X - 20, 12, Palette.Ash);
            // its kind at the foot, coloured, with the XP a fight gives
            string kind = StoryEditor.KindName(node.Kind).ToLowerInvariant() + (node.Xp is int xp ? $" · {xp} xp" : "");
            DrawString(font, r.Position + new Vector2(10, r.Size.Y - 6), kind, HorizontalAlignment.Right, r.Size.X - 20, 11, ColorOf(node.Kind));
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
            if (Route(i) is Vector2[] route && Enumerable.Range(0, route.Length - 1).Any(k => Geometry2D.GetClosestPointToSegment(at, route[k], route[k + 1]).DistanceTo(at) < 6))
            {
                return i;
            }
        }
        return null;
    }

    // A link's way between its boxes at right angles: out of the side facing the other box and in
    // at the other's, bending halfway. Two links between the same pair sit a little apart.
    private Vector2[]? Route(int link)
    {
        if (Editor == null || Editor.Find(Editor.Links[link].From) is not int a || Editor.Find(Editor.Links[link].To) is not int b)
        {
            return null;
        }
        Rect2 ra = RectOf(Editor.Nodes[a]), rb = RectOf(Editor.Nodes[b]);
        float apart = Editor.FindLink(Editor.Links[link].To, Editor.Links[link].From) != null ? 6 : 0;
        if (rb.Position.X >= ra.End.X || rb.End.X <= ra.Position.X)
        {
            // side by side: out of the right (or left) edge, in at the other's facing edge
            bool right = rb.Position.X >= ra.End.X;
            var from = new Vector2(right ? ra.End.X : ra.Position.X, ra.GetCenter().Y + apart);
            var to = new Vector2(right ? rb.Position.X : rb.End.X, rb.GetCenter().Y + apart);
            float middle = (from.X + to.X) / 2;
            return new[] { from, new Vector2(middle, from.Y), new Vector2(middle, to.Y), to };
        }
        // one above the other: out of the bottom (or top), in at the other's facing edge
        bool down = rb.Position.Y >= ra.GetCenter().Y;
        var top = new Vector2(ra.GetCenter().X + apart, down ? ra.End.Y : ra.Position.Y);
        var end = new Vector2(rb.GetCenter().X + apart, down ? rb.Position.Y : rb.End.Y);
        float half = (top.Y + end.Y) / 2;
        return new[] { top, new Vector2(top.X, half), new Vector2(end.X, half), end };
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
