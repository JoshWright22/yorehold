using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The chat, on the right edge of every screen (Josh, 10/7): a narrow tab with the unread count
/// while it is put away, and a column of rooms (Global on the title; a party's room joins it in an
/// online game) when it is pulled out. The tab, T, or Escape in the box slide it out and back. It
/// is one layer over the menus and the game, so the same rooms carry on from screen to screen.
/// </summary>
public partial class ChatPanel : CanvasLayer
{
    public const float Width = 320;
    public const float TabWidth = 26;
    public const float Top = 70;
    public const float Bottom = 200;
    private const double SlideSeconds = 0.15;

    private readonly List<ChatRoom> _rooms = new();
    private int _room;
    private int _shownChanges = -1;
    private int _shownRoom = -1;

    private Control _frame = null!;
    private Button _tab = null!;
    private HBoxContainer _roomTabs = null!;
    private RichTextLabel _lines = null!;
    private LineEdit _box = null!;
    private Label _closed = null!;
    private Tween? _slide;

    /// <summary>Pulled out.</summary>
    public bool Open { get; private set; }
    /// <summary>The box has the keys: nothing else on screen should read them as shortcuts.</summary>
    public bool Typing => _box.HasFocus();

    public override void _Ready()
    {
        Layer = 20;
        _frame = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _frame.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        _frame.OffsetTop = Top;
        _frame.OffsetBottom = -Bottom;
        AddChild(_frame);

        _tab = new Button { Text = "C\nH\nA\nT", FocusMode = Control.FocusModeEnum.None, ClipText = true };
        _tab.AddThemeFontSizeOverride("font_size", 12);
        _tab.AddThemeStyleboxOverride("normal", Box(Palette.Ink, Palette.Iron));
        _tab.AddThemeStyleboxOverride("hover", Box(Palette.Dusk, Palette.Slate));
        _tab.AddThemeStyleboxOverride("pressed", Box(Palette.Dusk, Palette.Straw));
        _tab.Pressed += Toggle;
        _tab.TooltipText = "Chat (" + App.KeyHint("chat") + ")";
        _frame.AddChild(_tab);

        var body = new PanelContainer();
        body.AddThemeStyleboxOverride("panel", Box(Palette.Ink, Palette.Iron, 10));
        body.Position = new Vector2(TabWidth - 1, 0);
        body.Size = new Vector2(Width, 10);
        _frame.AddChild(body);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 6);
        body.AddChild(rows);

        var head = new HBoxContainer();
        head.AddChild(new Label { Text = "CHAT", ThemeTypeVariation = "CapsLabel", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center });
        _roomTabs = new HBoxContainer();
        _roomTabs.AddThemeConstantOverride("separation", 2);
        head.AddChild(_roomTabs);
        var hide = new Button { Text = "Hide", FocusMode = Control.FocusModeEnum.None, ThemeTypeVariation = "TabButton" };
        hide.Pressed += Toggle;
        head.AddChild(hide);
        rows.AddChild(head);

        _lines = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectionEnabled = true,
        };
        _lines.AddThemeFontSizeOverride("normal_font_size", 14);
        _lines.AddThemeColorOverride("default_color", Palette.Sand);
        rows.AddChild(_lines);

        _closed = new Label { ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        rows.AddChild(_closed);
        _box = new LineEdit { PlaceholderText = "Say something", MaxLength = ChatRoom.Longest, ClearButtonEnabled = false };
        _box.TextSubmitted += Send;
        rows.AddChild(_box);

        _frame.Resized += Lay;
        Lay();
        Rooms(new ChatRoom("global", "Global", App.Online));
    }

    /// <summary>The rooms to show: Global, and a party's room in an online game. The first is picked.</summary>
    public void Rooms(params ChatRoom[] rooms)
    {
        _rooms.Clear();
        _rooms.AddRange(rooms);
        _room = 0;
        _shownRoom = -1;
        foreach (Node old in _roomTabs.GetChildren())
        {
            old.QueueFree();
        }
        if (_rooms.Count < 2)
        {
            return; // one room needs no tabs
        }
        for (int i = 0; i < _rooms.Count; i++)
        {
            int index = i;
            var tab = new Button { Text = _rooms[i].Title, ToggleMode = true, ThemeTypeVariation = "TabButton", FocusMode = Control.FocusModeEnum.None };
            tab.Pressed += () => _room = index;
            _roomTabs.AddChild(tab);
        }
    }

    public void Toggle()
    {
        Open = !Open;
        _slide?.Kill();
        _slide = CreateTween();
        _slide.TweenProperty(_frame, "offset_left", Open ? -(Width + TabWidth - 1) : -TabWidth, SlideSeconds)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        if (Open)
        {
            _box.GrabFocus();
        }
        else
        {
            _box.ReleaseFocus();
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Echo: false } && !Typing && App.Pressed(@event, "chat"))
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Input(InputEvent @event)
    {
        // Escape in the box puts the chat away, before a menu reads it as Back
        if (Typing && @event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        for (int i = 0; i < _rooms.Count; i++)
        {
            _rooms[i].Update(delta, Open && i == _room);
        }
        if (_rooms.Count == 0)
        {
            return;
        }
        ChatRoom room = _rooms[_room];
        if (Open)
        {
            room.Seen();
        }
        int unread = _rooms.Sum(r => r.Unread);
        _tab.Text = unread > 0 ? $"{unread}\n\nC\nH\nA\nT" : "C\nH\nA\nT";
        _tab.AddThemeColorOverride("font_color", unread > 0 ? Palette.Straw : Palette.Ash);
        for (int i = 0; i < _roomTabs.GetChildCount(); i++)
        {
            var tab = _roomTabs.GetChild<Button>(i);
            tab.SetPressedNoSignal(i == _room);
            tab.Text = _rooms[i].Title + (_rooms[i].Unread > 0 ? $" {_rooms[i].Unread}" : "");
        }
        _closed.Text = room.Closed;
        _closed.Visible = room.Closed.Length > 0;
        _box.Editable = room.Closed.Length == 0;
        if (room.Changes != _shownChanges || _room != _shownRoom)
        {
            _shownChanges = room.Changes;
            _shownRoom = _room;
            _lines.Text = string.Join("\n", room.Lines.Select(Line));
        }
    }

    // a player's line: the time faint, the name in white (gold for one's own), the words in grey;
    // the game's own lines in blue, with no name
    private static string Line(ChatLine line)
    {
        string time = $"[color={Palette.Hex(Palette.Slate)}]{line.At:HH:mm}[/color] ";
        string text = line.Text.Replace("[", "[lb]");
        if (line.System)
        {
            return time + $"[color={Palette.Hex(Palette.Sky)}]{text}[/color]";
        }
        string name = line.Name.Replace("[", "[lb]");
        return time + $"[color={Palette.Hex(line.Mine ? Palette.Straw : Palette.Bone)}]{name}[/color]  {text}";
    }

    private void Send(string text)
    {
        if (_rooms.Count == 0)
        {
            return;
        }
        if (_rooms[_room].Send(text, out string why))
        {
            _box.Clear();
        }
        else if (why.Length > 0)
        {
            _rooms[_room].Say(why);
        }
    }

    // the tab stays on the edge; the column lies past it, off screen until pulled out
    private void Lay()
    {
        float height = _frame.Size.Y;
        _tab.Position = Vector2.Zero;
        _tab.Size = new Vector2(TabWidth, Mathf.Min(140, height));
        var body = _frame.GetChild<Control>(1);
        body.Size = new Vector2(Width, height);
        if (_slide == null)
        {
            _frame.OffsetLeft = -TabWidth;
        }
    }

    private static StyleBoxFlat Box(Color fill, Color line, float margin = 4)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = line };
        box.SetBorderWidthAll(1);
        box.SetContentMarginAll(margin);
        return box;
    }
}
