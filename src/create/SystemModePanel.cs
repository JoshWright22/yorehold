using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// System mode of Create, a layout over a SystemEditor: the ruleset's sections on the left, the
/// picked one as JSON in the middle (taken as soon as the game's reader takes the whole file), and
/// the bench on the right, which plays the system as it is on screen: the chance a roll passes
/// for each modifier against each DC, and a hero of a class against some creatures, fought out
/// many times by the AI. A rule is judged on the bench, not guessed at.
/// </summary>
public partial class SystemModePanel : HBoxContainer
{
    private static readonly int[] Modifiers = { 0, 2, 4, 6, 8, 10 };
    private static readonly int[] Dcs = { 10, 12, 15, 18, 20, 25 };
    private const int DuelFights = 20;

    private CreatePackage? _package;
    private SystemEditor? _editor;
    private ToolColumn _sections = null!;
    private Label _title = null!;
    private TextEdit _text = null!;
    private Label _said = null!;
    private ToolColumn _bench = null!;
    private Label _error = null!;
    private string _section = "basics";
    private string _shownText = "";
    private string _kind = CheckRules.Attack;
    private string _class = "";
    private string _creature = "";
    private int _level = 1;
    private int _count = 1;
    private Task<FightForecast>? _duel;
    private string _duelOf = "";
    private int _changesSeen = -1;
    private (List<string> Classes, List<string> Creatures) _choices = (new(), new());
    private int _choicesAt = -1;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 0);
        _error = new Label { ThemeTypeVariation = "WarnLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, Visible = false, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        AddChild(_error);

        _sections = new ToolColumn { CustomMinimumSize = new Vector2(220, 0) };
        AddChild(Framed(_sections, 220));

        var middle = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        middle.AddThemeConstantOverride("separation", 6);
        AddChild(Framed(middle, 0, true));
        _title = new Label { ThemeTypeVariation = "TitleLabel" };
        middle.AddChild(_title);
        middle.AddChild(new Label
        {
            Text = "The section's keys as JSON. Each change is taken once the game reads the whole system; until then it says why not.",
            ThemeTypeVariation = "DimLabel",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        _text = new TextEdit { SizeFlagsVertical = SizeFlags.ExpandFill, WrapMode = TextEdit.LineWrappingMode.None };
        _text.AddThemeFontOverride("font", GetThemeFont("font", "NumberLabel"));
        _text.AddThemeFontSizeOverride("font_size", 13);
        _text.TextChanged += Typed;
        _text.FocusExited += () => _package?.History.BreakMerge();
        middle.AddChild(_text);
        _said = new Label { ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        middle.AddChild(_said);

        _bench = new ToolColumn();
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(380, 0) };
        _bench.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_bench);
        AddChild(Framed(scroll, 380));
    }

    /// <summary>The package whose system is shown. Called every frame.</summary>
    public void Present(CreatePackage package)
    {
        _package = package;
        SystemEditor? editor = package.SystemEditor();
        if (!ReferenceEquals(editor, _editor))
        {
            _editor = editor;
            _changesSeen = -1;
            _duel = null;
            _sections.Invalidate();
            _bench.Invalidate();
        }
        _error.Visible = editor == null;
        _error.Text = package.SystemError.Length > 0 ? package.SystemError : "No system to edit.";
        foreach (Node child in GetChildren())
        {
            if (child != _error && child is Control part)
            {
                part.Visible = editor != null;
            }
        }
        if (editor == null)
        {
            return;
        }
        if (!editor.Sections.Any(s => s.Id == _section) && _section != SystemEditor.Other)
        {
            _section = editor.Sections.FirstOrDefault()?.Id ?? SystemEditor.Other;
        }
        BuildSections(editor, package);
        // an undo, a copy or a blank start puts the file's own text back in the box
        if (_changesSeen != package.History.Changes && !_text.HasFocus())
        {
            _changesSeen = package.History.Changes;
            ShowSection(editor);
        }
        BuildBench(editor, package);
    }

    private void BuildSections(SystemEditor editor, CreatePackage package)
    {
        string own = package.HasOwnSystem ? "the package's own" : "the game's, until a change is saved";
        _sections.Build(string.Join(",", editor.Sections.Select(s => s.Id)) + "|" + own, () =>
        {
            _sections.Heading(editor.Rules.Name.Length > 0 ? editor.Rules.Name : "System");
            _sections.Live(() => editor.Path + "\n" + (package.HasOwnSystem ? "the package's own" : "the game's, until a change is saved"));
            _sections.Gap();
            foreach (SystemEditor.Section section in editor.Sections)
            {
                string id = section.Id;
                _sections.Toggle(section.Name, () => _section == id, () => Pick(editor, id));
            }
            _sections.Toggle("Other", () => _section == SystemEditor.Other, () => Pick(editor, SystemEditor.Other));
            _sections.Gap(12);
            _sections.Heading("Start from");
            _sections.Act("A blank system", () =>
            {
                _said.Text = editor.StartBlank(out string error) ? "Started from the blank system in create/system.json." : "Blank system refused: " + error;
            });
            foreach ((string folder, string name) in package.Systems())
            {
                _sections.Act("A copy of " + name, () =>
                {
                    package.CopySystem(folder);
                    _said.Text = package.Status;
                });
            }
        });
    }

    private void Pick(SystemEditor editor, string id)
    {
        _section = id;
        _package?.History.BreakMerge();
        ShowSection(editor);
    }

    private void ShowSection(SystemEditor editor)
    {
        _title.Text = editor.NameOf(_section).ToUpperInvariant();
        _shownText = editor.SectionText(_section);
        _text.Text = _shownText;
        _said.Text = "Reads.";
        _said.AddThemeColorOverride("font_color", Palette.Leaf);
    }

    private void Typed()
    {
        if (_editor == null || _text.Text == _shownText)
        {
            return;
        }
        if (_editor.SetSection(_section, _text.Text, out string error))
        {
            _shownText = _text.Text;
            _changesSeen = _package?.History.Changes ?? -1;
            _said.Text = "Reads.";
            _said.AddThemeColorOverride("font_color", Palette.Leaf);
        }
        else
        {
            _said.Text = "Not taken: " + error;
            _said.AddThemeColorOverride("font_color", Palette.Red);
        }
    }

    // ---------------------------------------------------------------- the bench

    private void BuildBench(SystemEditor editor, CreatePackage package)
    {
        (List<string> classes, List<string> creatures) = Choices(package);
        if (!classes.Contains(_class))
        {
            _class = classes.FirstOrDefault() ?? "";
        }
        if (!creatures.Contains(_creature))
        {
            _creature = creatures.FirstOrDefault() ?? "";
        }
        string kinds = string.Join(",", editor.Rules.Checks.Kinds.Keys.OrderBy(k => k, StringComparer.Ordinal));
        _bench.Build($"{kinds}|{_kind}|{classes.Count}|{creatures.Count}", () =>
        {
            _bench.Heading("Bench");
            _bench.Dim("The system as it is on screen, saved or not.");
            _bench.Gap();
            _bench.Heading("Chance to pass");
            HBoxContainer kindRow = _bench.Row();
            foreach (string kind in editor.Rules.Checks.Kinds.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                string id = kind;
                ToolColumn.Narrow(_bench.Toggle(kind, () => _kind == id, () => { _kind = id; _bench.Invalidate(); }, kindRow), 76);
            }
            Label table = _bench.Live(() => OddsTable(editor), "NumberLabel");
            table.AutowrapMode = TextServer.AutowrapMode.Off;
            table.AddThemeFontSizeOverride("font_size", 12);
            _bench.Gap(12);

            _bench.Heading("Duel");
            _bench.Dim($"One hero against the creatures on an open floor, played out {DuelFights} times by the AI.");
            Picker("Hero", classes, () => _class, id => _class = id);
            Stepper("Level", () => _level, n => _level = Math.Clamp(n, 1, 20));
            Picker("Against", creatures, () => _creature, id => _creature = id);
            Stepper("How many", () => _count, n => _count = Math.Clamp(n, 1, 6));
            _bench.Act("Play it out", () => StartDuel(package), () => _class.Length > 0 && _creature.Length > 0 && (_duel == null || _duel.IsCompleted));
            _bench.Live(DuelLine);
        });
    }

    // read again only after a save or an undo, not every frame
    private (List<string> Classes, List<string> Creatures) Choices(CreatePackage package)
    {
        if (_choicesAt != package.History.Changes)
        {
            _choicesAt = package.History.Changes;
            _choices = package.BenchChoices();
        }
        return _choices;
    }

    private string OddsTable(SystemEditor editor)
    {
        if (!editor.Rules.Checks.Kinds.TryGetValue(_kind, out CheckKind? kind))
        {
            return "";
        }
        double[,] table = SystemBench.PassTable(kind, Modifiers, Dcs);
        var lines = new List<string> { "mod" + string.Concat(Dcs.Select(dc => $"DC{dc}".PadLeft(6))) };
        for (int m = 0; m < Modifiers.Length; m++)
        {
            string row = (Modifiers[m] >= 0 ? "+" : "") + Modifiers[m];
            lines.Add(row.PadRight(3) + string.Concat(Enumerable.Range(0, Dcs.Length).Select(d => $"{Math.Round(table[m, d] * 100)}%".PadLeft(6))));
        }
        return string.Join("\n", lines);
    }

    private void Picker(string label, List<string> ids, Func<string> now, Action<string> pick)
    {
        HBoxContainer row = _bench.Row();
        row.AddChild(new Label { Text = label, ThemeTypeVariation = "DimLabel", CustomMinimumSize = new Vector2(90, 0) });
        var box = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FocusMode = FocusModeEnum.None };
        foreach (string id in ids)
        {
            box.AddItem(id);
        }
        box.Selected = Math.Max(0, ids.IndexOf(now()));
        box.ItemSelected += index => pick(ids[(int)index]);
        row.AddChild(box);
    }

    private void Stepper(string label, Func<int> now, Action<int> set)
    {
        HBoxContainer row = _bench.Row();
        row.AddChild(new Label { Text = label, ThemeTypeVariation = "DimLabel", CustomMinimumSize = new Vector2(90, 0) });
        ToolColumn.Narrow(_bench.Act("-", () => set(now() - 1), null, row));
        Label shown = _bench.Live(() => now().ToString(), "NumberLabel", row);
        shown.AutowrapMode = TextServer.AutowrapMode.Off;
        shown.CustomMinimumSize = new Vector2(36, 0);
        shown.HorizontalAlignment = HorizontalAlignment.Center;
        ToolColumn.Narrow(_bench.Act("+", () => set(now() + 1), null, row));
    }

    private void StartDuel(CreatePackage package)
    {
        string scratch = Path.Combine(Path.GetTempPath(), "yorehold-bench");
        Func<FightForecast>? duel = package.BenchDuel(_class, _level, Enumerable.Repeat(_creature, _count).ToList(), DuelFights, scratch);
        if (duel == null)
        {
            return;
        }
        _duelOf = $"A level {_level} {_class} against {(_count == 1 ? "a" : _count.ToString())} {_creature}";
        _duel = Task.Run(duel);
    }

    private string DuelLine()
    {
        if (_duel == null)
        {
            return "Not played yet.";
        }
        if (!_duel.IsCompleted)
        {
            return _duelOf + ": playing...";
        }
        if (_duel.IsFaulted)
        {
            return _duelOf + ": could not play it: " + (_duel.Exception?.InnerException?.Message ?? "unknown");
        }
        FightForecast result = _duel.Result;
        string line = $"{_duelOf}: {result.Summary()}" + (result.Unfinished > 0 ? $"; {result.Unfinished} never ended" : "");
        return result.Verdict() is { Length: > 0 } verdict ? $"{line}\n{verdict}" : line;
    }

    // a column with its 1 px edge and some room inside
    private static Control Framed(Control inside, int width, bool expand = false)
    {
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(width, 0), SizeFlagsHorizontal = expand ? SizeFlags.ExpandFill : SizeFlags.Fill };
        var box = new StyleBoxFlat { BgColor = Palette.Ink, BorderColor = Palette.Iron, BorderWidthRight = 1 };
        box.SetContentMarginAll(10);
        panel.AddThemeStyleboxOverride("panel", box);
        panel.AddChild(inside);
        return panel;
    }
}
