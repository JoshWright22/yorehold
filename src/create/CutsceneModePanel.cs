using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Yorehold.Rules;
using Kind = Yorehold.Rules.CutsceneEditor.Kind;

namespace Yorehold;

/// <summary>
/// Cutscene mode of Create, a layout over a CutsceneEditor like the C++ client's: which file along
/// the top, the steps on the left, the preview, timeline and where the cutscene plays in the
/// middle, the picked step on the right. Space plays and stops the preview, Delete takes the picked
/// step out. Typing in a box is one undo step until the box is left.
/// </summary>
public partial class CutsceneModePanel : VBoxContainer
{
    // the screen the preview frames, as the game's window is by default
    private const float ScreenWidth = 1280, ScreenHeight = 720;

    private static readonly Kind[] AllKinds = { Kind.Camera, Kind.Caption, Kind.Title, Kind.Pause, Kind.Fade, Kind.Bars, Kind.Event };

    private CreatePackage? _package;
    private CutsceneEditor? _editor;
    private string _chapter = "\u0000";
    private List<string> _listed = new();
    private Label _file = null!;
    private Label _count = null!;
    private Button _back = null!;
    private Button _on = null!;
    private Label _error = null!;
    private HBoxContainer _body = null!;
    private ToolColumn _steps = null!;
    private ToolColumn _step = null!;
    private ToolColumn _hooks = null!;
    private EditorMapView _view = null!;
    private CutsceneTimeline _timeline = null!;
    private Button _whole = null!;
    private Button _play = null!;
    private Label _time = null!;
    private Label _seeing = null!;

    private int _picked;
    private int? _trigger;
    private double _now;
    private bool _playing;
    private bool _overview;
    private Vector2 _start;
    private string _hint = "";
    private CutsceneEditor.Frame _frame = new();

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 0);
        var bar = new PanelContainer();
        AddChild(bar);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 3);
        bar.AddChild(row);
        _back = Small(row, "<", () => Step(-1));
        _file = new Label { CustomMinimumSize = new Vector2(320, 0), ClipText = true };
        row.AddChild(_file);
        _on = Small(row, ">", () => Step(1));
        _count = new Label { ThemeTypeVariation = "DimLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(_count);
        Small(row, "New cutscene", () =>
        {
            _package?.NewCutscene();
            _chapter = "\u0000";
        }).CustomMinimumSize = new Vector2(130, 26);

        _error = new Label { ThemeTypeVariation = "WarnLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false };
        AddChild(_error);
        _body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("separation", 0);
        AddChild(_body);
        _steps = Column(_body, 240);

        // the middle: the preview, its controls, the timeline and when the file plays
        var middle = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddChild(middle);
        var stack = new VBoxContainer();
        stack.AddThemeConstantOverride("separation", 4);
        middle.AddChild(stack);
        var head = new HBoxContainer();
        stack.AddChild(head);
        _seeing = new Label { ThemeTypeVariation = "DimLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        head.AddChild(_seeing);
        _whole = new Button { Text = "Whole map", ToggleMode = true, ThemeTypeVariation = "ChipButton", FocusMode = FocusModeEnum.None };
        _whole.Pressed += () => _overview = _whole.ButtonPressed;
        head.AddChild(_whole);
        var frame = new AspectRatioContainer { Ratio = ScreenWidth / ScreenHeight, CustomMinimumSize = new Vector2(0, 250) };
        stack.AddChild(frame);
        _view = new EditorMapView { Locked = true, Grid = false, Walls = false };
        frame.AddChild(_view);
        _view.Pressed += Aim;
        _view.DrawOver += DrawOverlay;
        var controls = new HBoxContainer();
        controls.AddThemeConstantOverride("separation", 4);
        stack.AddChild(controls);
        _play = Small(controls, "Play", PlayOrStop);
        _play.CustomMinimumSize = new Vector2(70, 26);
        Small(controls, "|<", () =>
        {
            _now = 0;
            _playing = false;
        }).CustomMinimumSize = new Vector2(36, 26);
        _time = new Label { CustomMinimumSize = new Vector2(110, 0) };
        controls.AddChild(_time);
        controls.AddChild(new Label { Text = "Space plays, click the bar to jump", ThemeTypeVariation = "DimLabel", ClipText = true, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _timeline = new CutsceneTimeline();
        stack.AddChild(_timeline);
        _timeline.Picked += step =>
        {
            Pick(step);
            _playing = false;
        };
        _timeline.Scrubbed += seconds =>
        {
            _now = seconds;
            _playing = false;
        };
        var lower = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, SizeFlagsVertical = SizeFlags.ExpandFill };
        stack.AddChild(lower);
        _hooks = new ToolColumn { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        lower.AddChild(_hooks);

        _step = Column(_body, 280);
    }

    /// <summary>The package whose chapter's cutscenes are shown. Called every frame.</summary>
    public void Present(CreatePackage package)
    {
        _package = package;
        if (package.PackagePath + "|" + package.Chapter != _chapter)
        {
            _chapter = package.PackagePath + "|" + package.Chapter;
            _listed = package.CutsceneFiles();
            if (!_listed.Contains(package.CutscenePath))
            {
                package.OpenCutscene(_listed.FirstOrDefault() ?? "");
            }
        }
        string path = package.CutscenePath;
        _file.Text = path.Length == 0 ? "No cutscenes yet" : path.StartsWith(package.Chapter + "/", StringComparison.Ordinal) ? path[(package.Chapter.Length + 1)..] : path;
        _count.Text = _listed.Count == 0 ? "" : _listed.Count == 1 ? "   1 file" : $"   {_listed.Count} files";
        _back.Disabled = _on.Disabled = _listed.Count < 2;

        CutsceneEditor? editor = package.CutsceneEditor();
        GameMap? map = package.MapEditor()?.Map();
        if (!ReferenceEquals(editor, _editor))
        {
            _editor = editor;
            _picked = 0;
            _trigger = null;
            _now = 0;
            _playing = false;
            _hint = "";
            _steps.Invalidate();
            _step.Invalidate();
            _hooks.Invalidate();
            // the preview starts from the middle of the map; in play it starts where the party is
            _start = map == null ? new Vector2(ScreenWidth / 2, ScreenHeight / 2) : new Vector2(map.Width, map.Height) * GameMap.CellSize / 2;
        }
        _body.Visible = editor != null;
        _error.Visible = editor == null;
        _error.Text = package.Chapter.Length == 0 ? "This package has no chapter to make cutscenes for."
            : path.Length == 0 ? "This chapter has no cutscenes. New cutscene starts one in its cutscenes folder."
            : package.CutsceneError.Length > 0 ? package.CutsceneError : "This file can't be read.";
        if (editor == null)
        {
            return;
        }
        editor.SetBounds(map == null ? null : (0, 0, map.Width * GameMap.CellSize, map.Height * GameMap.CellSize));

        // an undo can take away the step that was picked
        if (_picked >= editor.Steps.Count)
        {
            _picked = Math.Max(0, editor.Steps.Count - 1);
        }
        double length = editor.Length();
        _now = Math.Clamp(_now, 0, length);
        List<CutsceneEditor.Span> spans = editor.Spans();
        _timeline.Show(editor.Steps, spans, length, _now, _picked);
        _time.Text = $"{CutsceneEditor.Shown(_now)} / {CutsceneEditor.Shown(length)} s";
        _play.Text = _playing ? "Stop" : "Play";
        _whole.SetPressedNoSignal(_overview);
        _seeing.Text = _overview ? "Whole map, the box is what the camera sees" : "What the players see";
        ShowPreview(editor, map);

        _steps.Build(string.Join("|", editor.Steps.Select((s, i) => $"{i == _picked}{Summary(s)}{s.Wait}{CutsceneEditor.Shown(spans[i].Start)}")), () => BuildSteps(editor));
        _step.Build(editor.Steps.Count == 0 ? "none" : $"{_picked}|{editor.Steps[_picked].Kind}|{editor.Steps.Count}", () => BuildStep(editor));
        CutsceneHooks? hooks = package.CutsceneHooks();
        List<int> mine = hooks == null ? new List<int>() : Enumerable.Range(0, hooks.Triggers.Count).Where(i => hooks.Plays(hooks.Triggers[i].Cutscene, path)).ToList();
        if (_trigger is int t && !mine.Contains(t))
        {
            _trigger = null;
        }
        _hooks.Build(hooks == null ? "none" : $"{path}|{string.Join(",", mine.Select(i => hooks.Triggers[i].Id))}|{_trigger}|{hooks.HasWin}", () => BuildHooks(hooks, path, mine));
    }

    public override void _Process(double delta)
    {
        if (_playing && _editor != null && IsVisibleInTree())
        {
            _now += delta;
            if (_now >= _editor.Length())
            {
                _now = _editor.Length();
                _playing = false;
            }
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_editor == null || !IsVisibleInTree() || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        if (key.Keycode == Key.Space)
        {
            PlayOrStop();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Delete && _editor.Steps.Count > 0)
        {
            _editor.RemoveStep(_picked);
            GetViewport().SetInputAsHandled();
        }
    }

    private void PlayOrStop()
    {
        if (_editor == null)
        {
            return;
        }
        if (!_playing && _now >= _editor.Length())
        {
            _now = 0;
        }
        _playing = !_playing;
    }

    private void Pick(int step)
    {
        if (_editor == null || step < 0 || step >= _editor.Steps.Count)
        {
            return;
        }
        _picked = step;
        _now = _editor.Spans()[step].Start;
        _hint = "";
    }

    private void Step(int by)
    {
        if (_package == null || _listed.Count < 2)
        {
            return;
        }
        int at = Math.Max(0, _listed.IndexOf(_package.CutscenePath));
        _package.OpenCutscene(_listed[((at + by) % _listed.Count + _listed.Count) % _listed.Count]);
    }

    private static ToolColumn Column(Container into, float width)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0), SizeFlagsVertical = SizeFlags.ExpandFill };
        into.AddChild(panel);
        var box = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        panel.AddChild(box);
        var column = new ToolColumn { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddChild(column);
        return column;
    }

    private static Button Small(HBoxContainer row, string text, Action press)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(28, 26) };
        button.Pressed += press;
        row.AddChild(button);
        return button;
    }

    // What the list says about a step.
    private static string Summary(CutsceneEditor.Step step) => step.Kind switch
    {
        Kind.Pause => $"pause {CutsceneEditor.Shown(step.Seconds)} s",
        Kind.Camera => $"camera {CutsceneEditor.Shown(step.X)}, {CutsceneEditor.Shown(step.Y)}" + (step.Zoom > 0 ? " x" + CutsceneEditor.Shown(step.Zoom) : ""),
        Kind.Caption => step.Text.Length == 0 ? "caption (no line)" : $"\"{step.Text}\"",
        Kind.Title => step.Text.Length == 0 ? "title (no line)" : $"title \"{step.Text}\"",
        Kind.Fade => step.Color.A == 0 ? "fade in" : $"fade to {step.Color.R} {step.Color.G} {step.Color.B}",
        Kind.Bars => step.On ? "bars in" : "bars out",
        Kind.Event => "event " + step.Text,
        _ => "",
    };

    private static string Capital(string name) => name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];

    // ---------------------------------------------------------------- the preview

    private void ShowPreview(CutsceneEditor editor, GameMap? map)
    {
        if (map != null)
        {
            _view.ShowMap(map, () => _package?.PlayFiles());
        }
        _frame = editor.FrameAt(_now, _start.ToRules(), 1);
        Vector2 size = _view.Size;
        if (size.X <= 0 || size.Y <= 0)
        {
            return;
        }
        if (_overview && map != null)
        {
            Vector2 world = new Vector2(map.Width, map.Height) * GameMap.CellSize;
            float fit = Math.Min(size.X / Math.Max(world.X, 1), size.Y / Math.Max(world.Y, 1));
            _view.Look(world / 2 - size / 2 / fit, fit);
        }
        else
        {
            // which part of the world fills the box: what a 1280 wide screen shows at the frame's zoom
            float scale = size.X * _frame.Zoom / ScreenWidth;
            _view.Look(_frame.Camera.ToGodot() - size / 2 / scale, scale);
        }
        _view.QueueRedraw();
    }

    private void DrawOverlay(EditorMapView view)
    {
        if (_editor == null)
        {
            return;
        }
        Vector2 size = view.Size;
        if (_overview)
        {
            // the camera's frame, and every camera step's target
            Vector2 seen = new Vector2(ScreenWidth, ScreenHeight) / _frame.Zoom;
            view.DrawRect(new Rect2(view.ToLocal(_frame.Camera.ToGodot() - seen / 2), seen * view.Zoom), Palette.Straw, false, 2);
            for (int i = 0; i < _editor.Steps.Count; i++)
            {
                if (_editor.Steps[i].Kind == Kind.Camera)
                {
                    view.DrawCircle(view.ToLocal(new Vector2((float)_editor.Steps[i].X, (float)_editor.Steps[i].Y)), i == _picked ? 6 : 4, i == _picked ? Palette.Straw : Palette.Ash);
                }
            }
            return;
        }
        // the overlay, drawn the way the play screen draws it, on a screen of the preview's size
        float px = size.X / ScreenWidth;
        ContentColor fade = _frame.Fade;
        if (fade.A > 0)
        {
            // the fade is always to palette ink, as in play
            view.DrawRect(new Rect2(Vector2.Zero, size), Palette.Faded(Palette.Ink, fade.A / 255f));
        }
        float t = Math.Clamp(_frame.Bars, 0, 1);
        float bar = size.Y * 0.11f * (t < 0.5f ? 2 * t * t : 1 - MathF.Pow(-2 * t + 2, 2) / 2);
        if (bar > 0)
        {
            view.DrawRect(new Rect2(0, 0, size.X, bar), Palette.Ink);
            view.DrawRect(new Rect2(0, size.Y - bar, size.X, bar), Palette.Ink);
        }
        Font font = view.GetThemeDefaultFont();
        foreach (CutsceneText line in _frame.Lines)
        {
            int fontSize = line.Title ? Math.Max(12, (int)(40 * px)) : Math.Max(11, (int)(22 * px));
            Color color = Palette.Faded(line.Title ? Palette.Straw : Palette.Bone, line.Alpha);
            float y = line.Title ? size.Y * 0.5f : size.Y - Math.Max(bar, size.Y * 0.08f) - 40 * px;
            view.DrawString(font, new Vector2(0, y + 1), line.Text, HorizontalAlignment.Center, size.X, fontSize, Palette.Faded(Palette.Ink, line.Alpha));
            view.DrawString(font, new Vector2(0, y), line.Text, HorizontalAlignment.Center, size.X, fontSize, color);
        }
    }

    // A click on the preview aims the picked camera step there.
    private void Aim(Cell at, MouseButton button)
    {
        if (_editor == null || button != MouseButton.Left || _picked >= _editor.Steps.Count || _editor.Steps[_picked].Kind != Kind.Camera)
        {
            return;
        }
        Vector2 world = _view.ToWorld(_view.GetLocalMousePosition());
        _editor.EndTyping();
        _editor.SetStep(_picked, _editor.Steps[_picked] with { X = Math.Round(world.X), Y = Math.Round(world.Y) }, "aim");
        _editor.EndTyping();
        // show where it ends up
        _now = _editor.Spans()[_picked].End;
        _playing = false;
    }

    // ---------------------------------------------------------------- the steps

    private void BuildSteps(CutsceneEditor editor)
    {
        List<CutsceneEditor.Span> spans = editor.Spans();
        _steps.Heading("Steps");
        _steps.Dim($"{editor.Steps.Count} {(editor.Steps.Count == 1 ? "step" : "steps")}, {CutsceneEditor.Shown(editor.Length())} s");
        for (int i = 0; i < editor.Steps.Count; i++)
        {
            int index = i;
            // steps that start with the one before are pulled in, so it reads as "at the same time"
            string indent = i > 0 && !editor.Steps[i - 1].Wait ? "      " : "";
            Button row = _steps.Toggle($"{indent}{CutsceneEditor.Shown(spans[i].Start),5}  {Summary(editor.Steps[i])}", () => _picked == index, () =>
            {
                Pick(index);
                _playing = false;
            });
            row.Icon = MapModePanel.Swatch(CutsceneTimeline.ColorOf(editor.Steps[i].Kind));
        }
        _steps.Gap();
        _steps.Dim("Add after the picked one");
        var grid = new GridContainer { Columns = 4 };
        grid.AddThemeConstantOverride("h_separation", 3);
        grid.AddThemeConstantOverride("v_separation", 3);
        _steps.AddChild(grid);
        foreach (Kind kind in AllKinds)
        {
            _steps.Act(Capital(CutsceneEditor.KindName(kind)), () =>
            {
                if (editor.AddStep(kind, editor.Steps.Count == 0 ? null : _picked) is int added)
                {
                    Pick(added);
                }
            }, null, grid);
        }
        HBoxContainer moves = _steps.Row();
        _steps.Act("Up", () =>
        {
            if (editor.MoveStep(_picked, -1))
            {
                _picked--;
            }
        }, () => _picked > 0, moves);
        _steps.Act("Down", () =>
        {
            if (editor.MoveStep(_picked, 1))
            {
                _picked++;
            }
        }, () => _picked + 1 < editor.Steps.Count, moves);
        HBoxContainer more = _steps.Row();
        _steps.Act("Copy", () =>
        {
            if (editor.CopyStep(_picked) is int copied)
            {
                _picked = copied;
            }
        }, () => editor.Steps.Count > 0, more);
        _steps.Act("Remove", () => editor.RemoveStep(_picked), () => editor.Steps.Count > 0, more);
    }

    // ---------------------------------------------------------------- the picked step

    private void BuildStep(CutsceneEditor editor)
    {
        if (editor.Steps.Count == 0)
        {
            _step.Dim("No steps yet. Add one on the left.");
            return;
        }
        int index = _picked;
        CutsceneEditor.Step Now() => editor.Steps[Math.Min(index, editor.Steps.Count - 1)];
        void Change(CutsceneEditor.Step changed, string field, string why)
        {
            _hint = editor.SetStep(index, changed, field) || changed == Now() ? "" : why;
        }
        // a button press is its own undo step, apart from any typing before or after it
        void Click(CutsceneEditor.Step changed)
        {
            editor.EndTyping();
            editor.SetStep(index, changed, "click");
            editor.EndTyping();
        }
        static bool Number(string text, out double value) => double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

        CutsceneEditor.Step step = Now();
        _step.Heading($"Step {index + 1}: {Capital(CutsceneEditor.KindName(step.Kind))}");
        _step.Live(() =>
        {
            List<CutsceneEditor.Span> spans = editor.Spans();
            return index < spans.Count ? $"from {CutsceneEditor.Shown(spans[index].Start)} s to {CutsceneEditor.Shown(spans[index].End)} s" : "";
        });
        HBoxContainer seconds = _step.Row();
        Label secondsTitle = _step.Dim(step.Kind == Kind.Pause ? "Waits (s)" : "Seconds", seconds);
        secondsTitle.CustomMinimumSize = new Vector2(90, 0);
        _step.Field(() => CutsceneEditor.Shown(Now().Seconds), typed =>
        {
            if (!Number(typed, out double value))
            {
                _hint = "Seconds are a number from 0 to 600.";
            }
            else if (Math.Abs(value - Now().Seconds) > 1e-9)
            {
                Change(Now() with { Seconds = value }, "seconds", "Seconds are a number from 0 to 600.");
            }
        }, editor.EndTyping, "0", seconds);
        _step.Toggle("The next step waits for it", () => Now().Wait, () => Click(Now() with { Wait = !Now().Wait }), null, "ChipButton");
        _step.Gap();

        switch (step.Kind)
        {
            case Kind.Camera:
            {
                HBoxContainer looks = _step.Row();
                _step.Dim("Looks at", looks).CustomMinimumSize = new Vector2(90, 0);
                _step.Field(() => CutsceneEditor.Shown(Now().X), typed =>
                {
                    if (Number(typed, out double x) && Math.Abs(x - Now().X) > 1e-3)
                    {
                        Change(Now() with { X = x }, "camera", "The camera's place is two numbers, in world units.");
                    }
                }, editor.EndTyping, "x", looks);
                _step.Field(() => CutsceneEditor.Shown(Now().Y), typed =>
                {
                    if (Number(typed, out double y) && Math.Abs(y - Now().Y) > 1e-3)
                    {
                        Change(Now() with { Y = y }, "camera", "The camera's place is two numbers, in world units.");
                    }
                }, editor.EndTyping, "y", looks);
                HBoxContainer zoom = _step.Row();
                _step.Dim("Zoom", zoom).CustomMinimumSize = new Vector2(90, 0);
                _step.Field(() => CutsceneEditor.Shown(Now().Zoom), typed =>
                {
                    if (!Number(typed, out double z))
                    {
                        _hint = "Zoom is 0 to 64; 0 keeps the zoom it had.";
                    }
                    else if (Math.Abs(z - Now().Zoom) > 1e-6)
                    {
                        Change(Now() with { Zoom = z }, "zoom", "Zoom is 0 to 64; 0 keeps the zoom it had.");
                    }
                }, editor.EndTyping, "0", zoom);
                _step.Dim("0 keeps it", zoom);
                var names = new List<string> { "" };
                names.AddRange(CutsceneEditor.Eases.Where(e => e != "inOutCubic"));
                HBoxContainer ease = _step.Row();
                _step.Dim("Ease", ease).CustomMinimumSize = new Vector2(90, 0);
                ToolColumn.Narrow(_step.Act("<", () => Click(Now() with { Ease = StepName(names, Now().Ease, -1) }), null, ease), 28);
                Label shown = _step.Live(() => Now().Ease.Length == 0 ? "inOutCubic" : Now().Ease, "", ease);
                shown.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                shown.HorizontalAlignment = HorizontalAlignment.Center;
                shown.AutowrapMode = TextServer.AutowrapMode.Off;
                ToolColumn.Narrow(_step.Act(">", () => Click(Now() with { Ease = StepName(names, Now().Ease, 1) }), null, ease), 28);
                _step.Gap();
                _step.Dim("Click the preview to aim it there.");
                break;
            }
            case Kind.Caption:
            case Kind.Title:
                _step.Dim(step.Kind == Kind.Title ? "Title" : "Line");
                _step.Field(() => Now().Text, typed => Change(Now() with { Text = typed }, "line", "A line is at most 2000 characters."), editor.EndTyping, "what it says");
                _step.Act(step.Kind == Kind.Title ? "Make caption" : "Make title", () => Click(Now() with { Kind = Now().Kind == Kind.Title ? Kind.Caption : Kind.Title }));
                break;
            case Kind.Fade:
            {
                _step.Dim("Fades to colour (r g b, then how solid). Play always fades to ink; the alpha is what counts.");
                HBoxContainer parts = _step.Row();
                for (int c = 0; c < 4; c++)
                {
                    int part = c;
                    byte Of(ContentColor color) => part switch { 0 => color.R, 1 => color.G, 2 => color.B, _ => color.A };
                    _step.Field(() => Of(Now().Color).ToString(CultureInfo.InvariantCulture), typed =>
                    {
                        if (!int.TryParse(typed.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value > 255)
                        {
                            _hint = "Each part is a whole number from 0 to 255.";
                            return;
                        }
                        ContentColor now = Now().Color;
                        if (value == Of(now))
                        {
                            return;
                        }
                        byte b = (byte)value;
                        ContentColor changed = part switch { 0 => now with { R = b }, 1 => now with { G = b }, 2 => now with { B = b }, _ => now with { A = b } };
                        Change(Now() with { Color = changed }, "color-" + part, "Each part is a whole number from 0 to 255.");
                    }, editor.EndTyping, "0", parts);
                }
                HBoxContainer quick = _step.Row();
                _step.Act("To black", () => Click(Now() with { Color = new ContentColor(0, 0, 0, 255) }), null, quick);
                _step.Act("Back in", () => Click(Now() with { Color = Now().Color with { A = 0 } }), null, quick);
                break;
            }
            case Kind.Bars:
            {
                HBoxContainer way = _step.Row();
                _step.Toggle("Bars in", () => Now().On, () => Click(Now() with { On = true }), way, "TabButton").Alignment = HorizontalAlignment.Center;
                _step.Toggle("Bars out", () => !Now().On, () => Click(Now() with { On = false }), way, "TabButton").Alignment = HorizontalAlignment.Center;
                break;
            }
            case Kind.Event:
                _step.Dim("Event name");
                _step.Field(() => Now().Text, typed => Change(Now() with { Text = typed }, "event", "An event needs a name."), editor.EndTyping, "finished");
                _step.Dim("The game knows: " + string.Join(", ", CutsceneEditor.Events));
                break;
            case Kind.Pause:
                _step.Dim("Nothing happens for that long.");
                break;
        }
        _step.Gap();
        _step.Live(() => _hint, "WarnLabel");
    }

    // ---------------------------------------------------------------- when it plays

    private void BuildHooks(CutsceneHooks? hooks, string path, List<int> mine)
    {
        if (hooks == null)
        {
            _hooks.Dim(_package?.Chapter.Length > 0 ? "The chapter's chapter.json can't be read, so when this plays can't be set here." : "");
            return;
        }
        _hooks.Heading("Plays");
        _hooks.Dim("When the chapter plays this file: its triggers and endings in chapter.json.");

        // the chapter's own moments, as switches
        HBoxContainer moments = _hooks.Row();
        void Moment(string text, Func<string> now, Func<string, bool> set, Func<bool> enabled)
        {
            Button button = _hooks.Toggle(text, () => hooks.Plays(now(), path), () =>
            {
                hooks.EndTyping();
                set(hooks.Plays(now(), path) ? "" : path);
            }, moments, "TabButton");
            button.Alignment = HorizontalAlignment.Center;
            button.Disabled = !enabled();
        }
        Moment("Chapter cleared", () => hooks.Cleared, hooks.SetCleared, () => true);
        Moment("Party wiped", () => hooks.Wipe, hooks.SetWipe, () => true);
        Moment("Chapter won", () => hooks.Win, hooks.SetWin, () => hooks.HasWin);
        // another file taking a moment over is worth saying
        _hooks.Live(() =>
        {
            string others = "";
            if (hooks.Cleared.Length > 0 && !hooks.Plays(hooks.Cleared, path))
            {
                others += $"cleared plays {hooks.Cleared}  ";
            }
            if (hooks.Wipe.Length > 0 && !hooks.Plays(hooks.Wipe, path))
            {
                others += $"wiped plays {hooks.Wipe}  ";
            }
            if (hooks.Win.Length > 0 && !hooks.Plays(hooks.Win, path))
            {
                others += $"won plays {hooks.Win}";
            }
            return others.Length == 0 ? "" : "Now " + others;
        });

        foreach (int i in mine)
        {
            int index = i;
            _hooks.Toggle("Trigger " + hooks.Triggers[i].Id, () => _trigger == index, () => _trigger = _trigger == index ? null : index);
            _hooks.Live(() =>
            {
                if (index >= hooks.Triggers.Count)
                {
                    return "";
                }
                CutsceneHooks.Trigger t = hooks.Triggers[index];
                string when = t.When.Count == 0 ? "when the chapter starts" : $"once {string.Join(", ", t.When)} {(t.When.Count == 1 ? "is" : "are")} set";
                return "      " + when + (t.Dialogue.Length == 0 ? "" : ", after " + t.Dialogue);
            });
        }
        if (_trigger is int picked)
        {
            HBoxContainer fields = _hooks.Row();
            _hooks.Field(() => picked < hooks.Triggers.Count ? hooks.Triggers[picked].Id : "", typed =>
            {
                bool fine = picked >= hooks.Triggers.Count || typed == hooks.Triggers[picked].Id || hooks.SetTriggerId(picked, typed);
                _hint = fine ? "" : "A trigger id is a-z, 0-9, - and _, and not used yet.";
            }, hooks.EndTyping, "id", fields);
            _hooks.Field(() => picked < hooks.Triggers.Count ? string.Join(", ", hooks.Triggers[picked].When) : "", typed =>
            {
                List<string> flags = typed.Split(',').Select(f => f.Trim()).Where(f => f.Length > 0).ToList();
                bool fine = picked >= hooks.Triggers.Count || flags.SequenceEqual(hooks.Triggers[picked].When) || hooks.SetTriggerWhen(picked, flags);
                _hint = fine ? "" : "Flags are 1 to 64 characters, each once.";
            }, hooks.EndTyping, "flags, none = at the start", fields);
        }
        HBoxContainer buttons = _hooks.Row();
        _hooks.Act("Add trigger", () =>
        {
            hooks.EndTyping();
            _trigger = hooks.AddTrigger(path);
        }, null, buttons);
        _hooks.Act("Remove trigger", () =>
        {
            if (_trigger is int t)
            {
                hooks.EndTyping();
                hooks.RemoveTrigger(t);
                _trigger = null;
            }
        }, () => _trigger != null, buttons);
    }

    // The name step places along from current in names, round at the ends.
    private static string StepName(IReadOnlyList<string> names, string current, int step)
    {
        int index = Math.Max(0, names.ToList().IndexOf(current));
        return names[((index + step) % names.Count + names.Count) % names.Count];
    }
}
