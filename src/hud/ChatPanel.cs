using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The chat, a column down the right edge of every screen (Josh, 10/7; the design draws it open):
/// tabs along its top (the play screen's Combat log, a party's room in an online game, Global), the
/// lines, and a box with Send at its foot. Put away, it leaves a narrow tab with the unread count.
/// The tab, the arrow, T, or Escape in the box slide it out and back. It is one layer over the menus
/// and the game, so the same rooms carry on from screen to screen.
/// </summary>
public partial class ChatPanel : CanvasLayer
{
    public const float Width = 320;
    public const float TabWidth = 32;
    // where the put-away tab hangs on the edge
    private const float TabTop = 96;
    private const float TabHeight = 136;

    /// <summary>The one chat column, for the play screen to show its log in.</summary>
    public static ChatPanel? Current { get; private set; }

    private readonly List<ChatRoom> _rooms = new();
    // the picked tab: -1 the combat log, else a room
    private int _room;
    private int _shownChanges = -1;
    private int _shownRoom = -2;
    private LogPanel? _log;

    private Control _frame = null!;
    private Button _tab = null!;
    private HBoxContainer _roomTabs = null!;
    private RichTextLabel _lines = null!;
    private RichTextLabel _logLines = null!;
    private LineEdit _box = null!;
    private Button _send = null!;
    private Label _closed = null!;
    private Tween? _slide;

    /// <summary>Pulled out.</summary>
    public bool Open { get; private set; } = true;
    /// <summary>The box has the keys: nothing else on screen should read them as shortcuts.</summary>
    public bool Typing => _box.HasFocus();

    public override void _Ready()
    {
        Current = this;
        Layer = 20;
        _frame = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _frame.SetAnchorsPreset(Control.LayoutPreset.RightWide);
        AddChild(_frame);

        _tab = new Button { Text = "<\n\nC\nH\nA\nT", FocusMode = Control.FocusModeEnum.None, ClipText = true, ThemeTypeVariation = "MainButton" };
        _tab.AddThemeFontSizeOverride("font_size", 12);
        _tab.AddThemeStyleboxOverride("normal", Box(Palette.Ink, Palette.Iron));
        _tab.AddThemeStyleboxOverride("hover", Box(Palette.Dusk, Palette.Slate));
        _tab.AddThemeStyleboxOverride("pressed", Box(Palette.Dusk, Palette.Straw));
        _tab.AddThemeColorOverride("font_hover_color", Palette.Bone);
        _tab.Pressed += Toggle;
        _tab.TooltipText = "Chat (" + App.KeyHint("chat") + ")";
        _frame.AddChild(_tab);

        var body = new PanelContainer();
        body.AddThemeStyleboxOverride("panel", Column());
        body.Position = new Vector2(TabWidth, 0);
        body.Size = new Vector2(Width, 10);
        _frame.AddChild(body);
        var rows = new VBoxContainer();
        rows.AddThemeConstantOverride("separation", 0);
        body.AddChild(rows);

        // the tabs, underlined in amber when picked, and the arrow that puts the column away
        var head = new HBoxContainer { CustomMinimumSize = new Vector2(0, 44) };
        head.AddThemeConstantOverride("separation", 0);
        _roomTabs = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _roomTabs.AddThemeConstantOverride("separation", 4);
        head.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });
        head.AddChild(_roomTabs);
        var hide = new Button { Text = ">", FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(32, 32),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, TooltipText = "Put the chat away" };
        hide.AddThemeStyleboxOverride("normal", Box(Palette.Ink, Palette.Iron));
        hide.AddThemeStyleboxOverride("hover", Box(Palette.Dusk, Palette.Slate));
        hide.AddThemeStyleboxOverride("pressed", Box(Palette.Dusk, Palette.Slate));
        hide.Pressed += Toggle;
        head.AddChild(hide);
        head.AddChild(new Control { CustomMinimumSize = new Vector2(8, 0) });
        rows.AddChild(head);
        rows.AddChild(new ColorRect { Color = Palette.Iron, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore });

        var lines = new MarginContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
        {
            lines.AddThemeConstantOverride(side, 12);
        }
        rows.AddChild(lines);
        _lines = Lines();
        lines.AddChild(_lines);
        _logLines = Lines();
        _logLines.Visible = false;
        lines.AddChild(_logLines);

        rows.AddChild(new ColorRect { Color = Palette.Iron, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore });
        var foot = new MarginContainer();
        foreach (string side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
        {
            foot.AddThemeConstantOverride(side, 8);
        }
        rows.AddChild(foot);
        var footRows = new VBoxContainer();
        footRows.AddThemeConstantOverride("separation", 4);
        foot.AddChild(footRows);
        _closed = new Label { ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        // the layer sits outside the screens' theme, so the dim line says its own size and colour
        _closed.AddThemeFontSizeOverride("font_size", 12);
        _closed.AddThemeColorOverride("font_color", Palette.Ash);
        footRows.AddChild(_closed);
        var send = new HBoxContainer();
        send.AddThemeConstantOverride("separation", 4);
        footRows.AddChild(send);
        _box = new LineEdit { MaxLength = ChatRoom.Longest, ClearButtonEnabled = false, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 34) };
        _box.AddThemeFontSizeOverride("font_size", 13);
        _box.TextSubmitted += Send;
        send.AddChild(_box);
        _send = new Button { Text = "Send", FocusMode = Control.FocusModeEnum.None, ThemeTypeVariation = "MainButton", CustomMinimumSize = new Vector2(56, 34) };
        _send.AddThemeStyleboxOverride("normal", Box(Palette.Ink, Palette.Slate, 8));
        _send.AddThemeStyleboxOverride("hover", Box(Palette.Dusk, Palette.Ash, 8));
        _send.AddThemeStyleboxOverride("pressed", Box(Palette.Dusk, Palette.Ash, 8));
        _send.AddThemeStyleboxOverride("disabled", Box(Palette.Ink, Palette.Iron, 8));
        _send.AddThemeColorOverride("font_color", Palette.Bone);
        _send.AddThemeColorOverride("font_hover_color", Palette.Bone);
        _send.AddThemeColorOverride("font_disabled_color", Palette.Slate);
        _send.AddThemeFontSizeOverride("font_size", 14);
        _send.Pressed += () => Send(_box.Text);
        send.AddChild(_send);

        _frame.Resized += Lay;
        Lay();
        Rooms(new ChatRoom("global", "Global", App.Online));
    }

    public override void _ExitTree()
    {
        if (Current == this)
        {
            Current = null;
        }
    }

    private static RichTextLabel Lines()
    {
        var lines = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollFollowing = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SelectionEnabled = true,
        };
        lines.AddThemeFontSizeOverride("normal_font_size", 13);
        lines.AddThemeColorOverride("default_color", Palette.Sand);
        return lines;
    }

    /// <summary>The rooms to show: Global, and a party's room in an online game. The first is picked, unless the log is.</summary>
    public void Rooms(params ChatRoom[] rooms)
    {
        _rooms.Clear();
        _rooms.AddRange(rooms);
        _room = _log != null ? -1 : 0;
        _shownRoom = -2;
        MakeTabs();
    }

    /// <summary>The play screen's log as the chat's first tab (picked when it comes), or null to take it away.</summary>
    public void ShowLog(LogPanel? log)
    {
        if (_log != null)
        {
            _log.Mirror = null;
        }
        _log = log;
        _logLines.Clear();
        if (log != null)
        {
            log.Mirror = _logLines;
        }
        _room = log != null ? -1 : Mathf.Clamp(_room, 0, Mathf.Max(0, _rooms.Count - 1));
        _shownRoom = -2;
        MakeTabs();
    }

    private void MakeTabs()
    {
        foreach (Node old in _roomTabs.GetChildren())
        {
            _roomTabs.RemoveChild(old);
            old.QueueFree();
        }
        if (_log != null)
        {
            _roomTabs.AddChild(TabFor("Combat log", -1));
        }
        for (int i = 0; i < _rooms.Count; i++)
        {
            _roomTabs.AddChild(TabFor(_rooms[i].Title, i));
        }
    }

    // a tab: its name, bold in white with an amber underline when picked, grey otherwise
    private Button TabFor(string title, int index)
    {
        var tab = new Button { Text = title, ToggleMode = true, FocusMode = Control.FocusModeEnum.None, ThemeTypeVariation = "MainButton",
            SizeFlagsVertical = Control.SizeFlags.Fill };
        var off = new StyleBoxFlat { BgColor = Palette.Ink };
        off.SetContentMarginAll(8);
        var on = new StyleBoxFlat { BgColor = Palette.Ink, BorderColor = Palette.Straw, BorderWidthBottom = 2 };
        on.SetContentMarginAll(8);
        tab.AddThemeStyleboxOverride("normal", off);
        tab.AddThemeStyleboxOverride("hover", off);
        tab.AddThemeStyleboxOverride("pressed", on);
        tab.AddThemeStyleboxOverride("hover_pressed", on);
        tab.AddThemeColorOverride("font_color", Palette.Ash);
        tab.AddThemeColorOverride("font_hover_color", Palette.Bone);
        tab.AddThemeColorOverride("font_pressed_color", Palette.Bone);
        tab.AddThemeColorOverride("font_hover_pressed_color", Palette.Bone);
        tab.AddThemeFontSizeOverride("font_size", 14);
        tab.SetMeta("room", index);
        tab.Pressed += () => _room = index;
        return tab;
    }

    private readonly HashSet<string> _aside = new();
    private bool _reopen;

    /// <summary>
    /// A wide screen (options, character creation, the inventory, a shop) puts the column away while
    /// it is open, as the design shows them, and it comes back when the last such screen closes.
    /// Cheap to call every frame: nothing moves unless the answer changes.
    /// </summary>
    public void StepAside(string screen, bool aside)
    {
        bool before = _aside.Count > 0;
        if (aside)
        {
            _aside.Add(screen);
        }
        else
        {
            _aside.Remove(screen);
        }
        bool now = _aside.Count > 0;
        if (before == now)
        {
            return;
        }
        if (now && Open)
        {
            _reopen = true;
            Slide(false, false);
        }
        else if (!now && _reopen && !Open)
        {
            _reopen = false;
            Slide(true, false);
        }
        else if (!now)
        {
            _reopen = false;
        }
    }

    public void Toggle() => Slide(!Open, true);

    // Out or away; a pull by the player puts the keys in the box, a step aside doesn't.
    private void Slide(bool open, bool focus)
    {
        Open = open;
        _slide?.Kill();
        _slide = CreateTween();
        _slide.TweenProperty(_frame, "offset_left", Open ? -(Width + TabWidth) : -TabWidth, System.Math.Max(0.001, Motion.Seconds(Motion.Look.ChatSeconds)))
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _tab.Visible = !Open;
        if (Open && focus)
        {
            _box.GrabFocus();
        }
        else if (!Open)
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
        // Escape in the box leaves it (and puts the chat away), before a menu reads it as Back
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
        if (_room < 0 && _log == null || _room >= _rooms.Count)
        {
            _room = 0;
        }
        bool log = _room < 0;
        ChatRoom? room = log ? null : _rooms[_room];
        if (Open)
        {
            room?.Seen();
        }
        int unread = _rooms.Sum(r => r.Unread);
        _tab.Text = unread > 0 ? $"<\n\n{unread}\n\nC\nH\nA\nT" : "<\n\nC\nH\nA\nT";
        _tab.AddThemeColorOverride("font_color", unread > 0 ? Palette.Blue : Palette.Bone);
        foreach (Button tab in _roomTabs.GetChildren().OfType<Button>())
        {
            int index = (int)tab.GetMeta("room");
            tab.SetPressedNoSignal(index == _room);
            tab.Text = index < 0 ? "Combat log" : _rooms[index].Title + (_rooms[index].Unread > 0 ? $"  {_rooms[index].Unread}" : "");
        }
        _lines.Visible = !log;
        _logLines.Visible = log;
        // the log is the game's own lines: it can be read, not written to
        string closed = log ? "" : room!.Closed;
        _closed.Text = closed;
        _closed.Visible = closed.Length > 0;
        _box.Editable = !log && closed.Length == 0;
        _send.Disabled = !_box.Editable;
        _box.PlaceholderText = log ? "The combat log is read-only" : $"Message {room!.Title.ToLowerInvariant()} chat";
        if (room != null && (room.Changes != _shownChanges || _room != _shownRoom))
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
        if (_room < 0 || _room >= _rooms.Count || !_box.Editable)
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

    // the tab stays on the edge; the column lies past it, pulled out or off screen
    private void Lay()
    {
        float height = _frame.Size.Y;
        _tab.Position = new Vector2(0, TabTop);
        _tab.Size = new Vector2(TabWidth, Mathf.Min(TabHeight, height));
        var body = _frame.GetChild<Control>(1);
        body.Size = new Vector2(Width, height);
        if (_slide == null)
        {
            _frame.OffsetLeft = Open ? -(Width + TabWidth) : -TabWidth;
            _tab.Visible = !Open;
        }
    }

    // the column: the panel colour with a 1 px line down its left side
    private static StyleBoxFlat Column() => new() { BgColor = Palette.Ink, BorderColor = Palette.Iron, BorderWidthLeft = 1 };

    private static StyleBoxFlat Box(Color fill, Color line, float margin = 4)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = line };
        box.SetBorderWidthAll(1);
        box.SetContentMarginAll(margin);
        return box;
    }
}
