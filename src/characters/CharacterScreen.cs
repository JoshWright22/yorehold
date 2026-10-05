using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The player's characters over the play screen, in three views. Characters: the library and its
/// graveyard, with New character and Level up. New adventure: one seat per hero the chapter has,
/// each holding its ready-made hero until a library character or a new one takes it; Start plays
/// the chapter again with them. Making a character goes Origin, Class and scores, Skills and feats,
/// with the sheet rebuilt on the right after every click. Every rule is CharacterDraft's and
/// CharacterBuild's; this only shows them.
/// </summary>
public partial class CharacterScreen : CanvasLayer
{
    public enum View
    {
        Characters,
        Party,
        Draft,
    }

    /// <summary>Start was pressed: one pick per seat, null for the ready-made hero.</summary>
    public event Action<List<PartyPick?>>? StartPressed;
    public event Action? Closed;

    // the scene has all of these
    private Label _title = null!;
    private HBoxContainer _tabs = null!;
    private VBoxContainer _body = null!;
    private Label _problem = null!;
    private Label _notice = null!;
    private HBoxContainer _buttons = null!;
    private SheetView _sheet = null!;

    private World? _world;
    private Ruleset _rules = new();
    private Compendium _compendium = new();
    private string _folder = "";
    private List<LibraryEntry> _entries = new();
    private int _pick;
    private CharacterDraft? _draft;
    private View _draftBack = View.Characters;
    private Rng _dice = new(1);
    private readonly List<LibraryEntry?> _seats = new();
    private int _seat;
    private string _noticeText = "";
    private bool _noticeBad;
    private bool _dirty;

    public View Showing { get; private set; } = View.Characters;
    public bool IsOpen => Visible;

    public override void _Ready()
    {
        _title = GetNode<Label>("Root/Left/Rows/Title");
        _tabs = GetNode<HBoxContainer>("Root/Left/Rows/Tabs");
        _body = GetNode<VBoxContainer>("Root/Left/Rows/Scroll/Body");
        _problem = GetNode<Label>("Root/Left/Rows/Problem");
        _notice = GetNode<Label>("Root/Left/Rows/Notice");
        _buttons = GetNode<HBoxContainer>("Root/Left/Rows/Buttons");
        _sheet = GetNode<SheetView>("Root/Sheet");
        Visible = false;
    }

    /// <summary>Opens on the library or the seats, reading the library folder afresh.</summary>
    public void Open(World world, string folder, View view)
    {
        _world = world;
        _rules = world.Rules;
        _compendium = world.Chapter.Compendium;
        _folder = folder;
        _noticeText = "";
        _dice = new Rng((ulong)Time.GetTicksUsec());
        Load();
        _seats.Clear();
        for (int i = 0; i < world.Chapter.Party.Count; i++)
        {
            // what the world plays now, so opening and starting again changes nothing
            PartyPick? now = i < world.PartyPicks.Count ? world.PartyPicks[i] : null;
            _seats.Add(now == null ? null : _entries.Find(e => e.FileName == now.Library && !e.Retired));
        }
        _seat = 0;
        Showing = view;
        Visible = true;
        Redraw();
    }

    public void Close()
    {
        Visible = false;
        _draft = null;
        Closed?.Invoke();
    }

    public override void _Process(double delta)
    {
        if (_dirty && Visible)
        {
            Redraw();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || @event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        if (key.Keycode == Key.Escape)
        {
            Back();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Enter && Showing == View.Party)
        {
            Start();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Load()
    {
        var problems = new List<string>();
        _entries = CharacterLibrary.List(_folder, problems);
        if (problems.Count > 0)
        {
            Notice($"Couldn't read {problems.Count} character file{(problems.Count == 1 ? "" : "s")}: {problems[0]}", true);
        }
        _pick = Math.Clamp(_pick, 0, Math.Max(0, _entries.Count - 1));
    }

    private void Notice(string text, bool bad)
    {
        _noticeText = text;
        _noticeBad = bad;
    }

    private void Back()
    {
        if (Showing == View.Draft)
        {
            _draft = null;
            Showing = _draftBack;
            Redraw();
            return;
        }
        Close();
    }

    // Everything on the left is made again after each click; the click itself only changes the
    // draft or the picks, so a button never frees itself while it is still answering.
    private void Changed()
    {
        _dirty = true;
    }

    private void Redraw()
    {
        _dirty = false;
        Clear(_tabs);
        Clear(_body);
        Clear(_buttons);
        _problem.Text = "";
        switch (Showing)
        {
            case View.Characters:
                DrawCharacters();
                break;
            case View.Party:
                DrawParty();
                break;
            case View.Draft:
                DrawDraft();
                break;
        }
        _problem.Visible = _problem.Text.Length > 0;
        _notice.Text = _noticeText;
        _notice.Visible = _noticeText.Length > 0;
        _notice.ThemeTypeVariation = _noticeBad ? "WarnLabel" : "DimLabel";
    }

    private void ViewTabs()
    {
        Toggle(_tabs, "Characters", Showing == View.Characters, () => Showing = View.Characters);
        Toggle(_tabs, "New adventure", Showing == View.Party, () => Showing = View.Party);
    }

    // ---------------------------------------------------------------- the library

    private void DrawCharacters()
    {
        _title.Text = "Characters";
        ViewTabs();
        for (int i = 0; i < _entries.Count; i++)
        {
            LibraryEntry entry = _entries[i];
            string text = $"{entry.Choices.Name}  (level {entry.Choices.Level})";
            if (entry.Retired)
            {
                text += "  graveyard";
            }
            else if (entry.Away.Length > 0)
            {
                text += "  away";
            }
            int index = i;
            Toggle(_body, text, i == _pick, () => _pick = index);
        }
        if (_entries.Count == 0)
        {
            Dim(_body, "No characters yet. Each one can go into any adventure.");
        }

        LibraryEntry? chosen = _pick < _entries.Count ? _entries[_pick] : null;
        CharacterSheet? sheet = null;
        string problem = "";
        if (chosen != null)
        {
            sheet = CharacterBuild.Build(_rules, _compendium, chosen.Choices, out problem);
            _sheet.ShowSheet(_rules, _compendium, sheet, chosen.Choices, problem);
            if (sheet != null)
            {
                ShowCarried(chosen);
            }
        }
        else
        {
            _sheet.ShowText("No character picked", "Make one with New character.");
        }
        bool canLevel = chosen != null && !chosen.Retired && chosen.Away.Length == 0 && sheet != null
            && _rules.LevelForXp(chosen.Choices.Xp) > chosen.Choices.Level;
        if (chosen is { Retired: false } && chosen.Away.Length > 0)
        {
            Dim(_body, "Away in an adventure until it ends.");
        }
        else if (chosen is { Retired: true })
        {
            Dim(_body, "In the graveyard: kept to look at, never played again.");
        }
        else if (chosen != null && !canLevel)
        {
            Dim(_body, "Levels up when its XP reaches the next level.");
        }

        Push(_buttons, "New character", _compendium.Classes.Count > 0, () => NewCharacter(View.Characters));
        Push(_buttons, "Level up", canLevel, () =>
        {
            _draftBack = View.Characters;
            _draft = CharacterDraft.LevelUp(_rules, _compendium, chosen!.Choices);
            Showing = View.Draft;
        });
        Push(_buttons, "Close (Esc)", true, Close);
    }

    // A library file keeps its own gear, which the sheet built from choices doesn't know.
    private void ShowCarried(LibraryEntry entry)
    {
        CharacterSheet? sheet = CharacterBuild.Build(_rules, _compendium, entry.Choices);
        if (sheet == null)
        {
            return;
        }
        for (int i = sheet.Inventory.Count - 1; i >= 0; i--)
        {
            sheet.TakeOut(i);
        }
        foreach (Item item in entry.Inventory)
        {
            Item copy = item.Copy();
            bool worn = copy.Equipped;
            copy.Equipped = false;
            sheet.Inventory.Add(copy);
            if (worn)
            {
                sheet.Equip(sheet.Inventory.Count - 1);
            }
        }
        sheet.Coins = entry.Coins;
        _sheet.ShowSheet(_rules, _compendium, sheet, entry.Choices, "");
    }

    private void NewCharacter(View back)
    {
        _draftBack = back;
        _draft = new CharacterDraft(_rules, _compendium);
        Showing = View.Draft;
    }

    // ---------------------------------------------------------------- the seats

    private void DrawParty()
    {
        Chapter chapter = _world!.Chapter;
        _title.Text = "New adventure";
        ViewTabs();
        Heading(_body, $"{chapter.Title}: level {chapter.Level}, {chapter.Party.Count} heroes");
        for (int i = 0; i < chapter.Party.Count; i++)
        {
            LibraryEntry? seated = _seats[i];
            string who = seated != null ? $"{seated.Choices.Name}  (level {seated.Choices.Level})"
                : $"{chapter.Party[i].Name}  (ready-made {ClassName(chapter.Party[i].ClassId)})";
            int index = i;
            Toggle(_body, $"{i + 1}. {who}", i == _seat, () => _seat = index);
        }
        Dim(_body, $"Who takes seat {_seat + 1}?");
        Toggle(_body, "Ready-made: " + chapter.Party[_seat].Name, _seats[_seat] == null, () => _seats[_seat] = null);
        foreach (LibraryEntry entry in _entries)
        {
            bool here = _seats[_seat]?.Path == entry.Path;
            bool elsewhere = !here && _seats.Any(s => s?.Path == entry.Path);
            if (entry.Retired || entry.Away.Length > 0 || elsewhere)
            {
                continue;
            }
            LibraryEntry taken = entry;
            Toggle(_body, $"{entry.Choices.Name}  (level {entry.Choices.Level})", here, () => _seats[_seat] = taken);
        }
        Push(_body, "New character for this seat", _compendium.Classes.Count > 0, () => NewCharacter(View.Party));

        if (_seats[_seat] is LibraryEntry chosen)
        {
            CharacterSheet? sheet = CharacterBuild.Build(_rules, _compendium, chosen.Choices, out string problem);
            _sheet.ShowSheet(_rules, _compendium, sheet, chosen.Choices, problem);
            if (sheet != null)
            {
                ShowCarried(chosen);
            }
        }
        else
        {
            PartyMember member = chapter.Party[_seat];
            _sheet.ShowText(member.Name, $"Level {chapter.Level} {ClassName(member.ClassId)}\n\nReady-made for this adventure: its scores are rolled when the adventure starts.");
        }
        Push(_buttons, "Close (Esc)", true, Close);
        Push(_buttons, "Start (Enter)", true, Start);
    }

    private void Start()
    {
        var picks = _seats.Select(s => s == null ? null : new PartyPick(s.Choices, s.Inventory, s.FileName, s.Coins)).ToList();
        Visible = false;
        StartPressed?.Invoke(picks);
        Closed?.Invoke();
    }

    private string ClassName(string id) => _compendium.Class(id)?.Name ?? id;

    // ---------------------------------------------------------------- making and levelling

    private void DrawDraft()
    {
        CharacterDraft d = _draft!;
        string className = ClassName(d.Choices.Levels[^1].ClassId);
        if (d.LevellingUp)
        {
            _title.Text = $"{d.Choices.Name} reaches level {d.Choices.Level}";
            Heading(_body, "Class for this level");
            ClassPicker(d);
        }
        else
        {
            _title.Text = "New character";
            for (int i = 0; i < CharacterDraft.StepNames.Length; i++)
            {
                // an earlier step can always be reopened, the next one once this one is done
                bool open = i <= d.Step || (i == d.Step + 1 && d.StepDone(d.Step));
                int step = i;
                Button tab = Toggle(_tabs, $"{i + 1}. {CharacterDraft.StepNames[i]}", i == d.Step, () => d.Step = step);
                tab.Disabled = !open;
            }
        }

        if (d.Step == 0 && !d.LevellingUp)
        {
            Heading(_body, "Name");
            var name = new LineEdit { Text = d.Choices.Name, MaxLength = 64, PlaceholderText = "Type a name", CustomMinimumSize = new Vector2(0, 36) };
            name.TextChanged += text =>
            {
                d.SetName(text);
                ShowDraftSide(d);
            };
            _body.AddChild(name);
            if (_compendium.Races.Count > 0)
            {
                Heading(_body, "Race");
                Grid(d.RaceIds(), id => _compendium.Races[id].Name, id => id == d.Choices.Race, 4, d.SetRace);
            }
            if (_compendium.Backgrounds.Count > 0)
            {
                Heading(_body, "Background");
                Grid(d.BackgroundIds(), id => _compendium.Backgrounds[id].Name, id => id == d.Choices.Background, 3, d.SetBackground);
            }
        }
        else if (d.Step == 1 && !d.LevellingUp)
        {
            Heading(_body, "Class");
            ClassPicker(d);
            string method = d.Choices.ScoreMethod;
            Heading(_body, method switch
            {
                "pointBuy" => $"Ability scores: {d.PointsLeft()} of {_rules.ScoreMethods.PointBudget} points left",
                "roll" => "Ability scores: press Roll again to reroll",
                _ => "Ability scores: + and - swap two scores",
            });
            var methods = new HBoxContainer();
            methods.AddThemeConstantOverride("separation", 6);
            _body.AddChild(methods);
            foreach ((string id, string label) in new[] { ("array", "Standard array"), ("pointBuy", "Point buy"), ("roll", "Roll") })
            {
                Toggle(methods, label, method == id, () =>
                {
                    if (d.Choices.ScoreMethod != id)
                    {
                        d.SetMethod(id, _dice);
                    }
                    else if (id == "roll")
                    {
                        d.Reroll(_dice);
                    }
                });
            }
            foreach (AbilityDefinition ability in _rules.Abilities)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 6);
                _body.AddChild(row);
                int score = d.Choices.Scores.GetValueOrDefault(ability.Id);
                row.AddChild(new Label { Text = ability.Name, CustomMinimumSize = new Vector2(150, 0) });
                row.AddChild(new Label { Text = $"{score}  ({SheetView.Signed(_rules.AbilityModifier(score))})", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
                if (method != "roll")
                {
                    string id = ability.Id;
                    Push(row, "-", d.CanLower(id), () => d.Lower(id)).CustomMinimumSize = new Vector2(34, 30);
                    Push(row, "+", d.CanRaise(id), () => d.Raise(id)).CustomMinimumSize = new Vector2(34, 30);
                }
            }
        }
        else
        {
            if (d.SkillPicks() > 0)
            {
                Heading(_body, $"Skills: train {d.SkillPicks()}");
                List<string> picked = d.Picked("skills");
                Grid(d.SkillOptions.ToList(), id => _rules.Skill(id)?.Name ?? id, picked.Contains, 3, d.ToggleSkill);
            }
            foreach (string kind in d.FeatKinds())
            {
                Heading(_body, char.ToUpperInvariant(kind[0]) + kind[1..] + " feat");
                List<string> picked = d.Picked("feats");
                List<string> options = d.FeatOptions(kind);
                if (options.Count == 0)
                {
                    Dim(_body, "None the character can take yet.");
                }
                Grid(options, id => _compendium.Feats[id].Name, picked.Contains, 2, d.PickFeat);
            }
            if (d.SkillPicks() == 0 && d.FeatKinds().Count == 0)
            {
                Dim(_body, $"Nothing to pick at this {className} level.");
            }
        }

        _problem.Text = d.StepProblem(d.Step);
        ShowDraftSide(d);
        Push(_buttons, "Cancel (Esc)", true, Back);
        if (!d.LevellingUp && d.Step > 0)
        {
            Push(_buttons, "Back", true, () => d.Step--);
        }
        bool last = d.LevellingUp || d.Step == CharacterDraft.StepNames.Length - 1;
        if (!last)
        {
            Push(_buttons, "Next", d.StepDone(d.Step), () => d.Step++);
        }
        else
        {
            Push(_buttons, d.LevellingUp ? "Level up" : "Finish", d.Finished(), FinishDraft);
        }
    }

    private void ClassPicker(CharacterDraft d)
    {
        string current = d.Choices.Levels[^1].ClassId;
        Grid(d.ClassIds(), ClassName, id => id == current, 3, id =>
        {
            if (id != current)
            {
                d.SetClass(id);
            }
        });
    }

    // the sheet and what is missing, without making the name box again while it is typed in
    private void ShowDraftSide(CharacterDraft d)
    {
        _sheet.ShowSheet(_rules, _compendium, d.Sheet, d.Choices, d.Problem);
        _problem.Text = d.StepProblem(d.Step);
        _problem.Visible = _problem.Text.Length > 0;
        foreach (Node child in _buttons.GetChildren())
        {
            if (child is Button { Text: "Next" } next)
            {
                next.Disabled = !d.StepDone(d.Step);
            }
            else if (child is Button { Text: "Finish" } finish)
            {
                finish.Disabled = !d.Finished();
            }
        }
    }

    private void FinishDraft()
    {
        if (_draft == null || !_draft.Finished() || _draft.Sheet == null)
        {
            return;
        }
        bool levelling = _draft.LevellingUp;
        LibraryEntry entry;
        if (levelling && _pick < _entries.Count)
        {
            entry = _entries[_pick];
        }
        else
        {
            // a new character starts with its class's and background's gear
            entry = new LibraryEntry();
            entry.Inventory.AddRange(_draft.Sheet.Inventory.Select(i => i.Copy()));
        }
        entry.Choices = _draft.Choices.Copy();
        entry.Choices.Name = entry.Choices.Name.Trim();
        // made for an adventure written for a higher level: it brings the XP to level up to it
        bool forParty = _draftBack == View.Party;
        int level = _world?.Chapter.Level ?? 1;
        if (forParty && !levelling && level > 1 && _rules.XpForLevel.Count > 0)
        {
            entry.Choices.Xp = _rules.XpForLevel[Math.Min(level - 2, _rules.XpForLevel.Count - 1)];
        }
        _draft = null;
        Showing = _draftBack;
        try
        {
            CharacterLibrary.Write(_folder, entry);
            Notice(levelling ? $"{entry.Choices.Name} is now level {entry.Choices.Level}." : $"Saved {entry.Choices.Name}.", false);
        }
        catch (Exception error) when (error is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            Notice($"Couldn't save {entry.Choices.Name}: {error.Message}", true);
        }
        Load();
        int found = _entries.FindIndex(e => e.Path == entry.Path);
        if (found >= 0)
        {
            _pick = found;
            if (forParty)
            {
                _seats[_seat] = _entries[found];
            }
        }
    }

    // ---------------------------------------------------------------- small pieces

    private static void Clear(Node node)
    {
        foreach (Node child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    private static void Heading(Container into, string text)
    {
        into.AddChild(new Label { Text = text, ThemeTypeVariation = "TitleLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    private static void Dim(Container into, string text)
    {
        into.AddChild(new Label { Text = text, ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    private Button Push(Container into, string text, bool enabled, Action pressed)
    {
        var button = new Button
        {
            Text = text,
            Disabled = !enabled,
            FocusMode = Control.FocusModeEnum.None,
            CustomMinimumSize = new Vector2(0, 36),
            SizeFlagsHorizontal = into is HBoxContainer ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill,
        };
        button.Pressed += () =>
        {
            pressed();
            Changed();
        };
        into.AddChild(button);
        return button;
    }

    private Button Toggle(Container into, string text, bool on, Action pressed)
    {
        Button button = Push(into, text, true, pressed);
        button.ToggleMode = true;
        button.SetPressedNoSignal(on);
        button.ThemeTypeVariation = "PickButton";
        button.ClipText = true;
        return button;
    }

    // A grid of toggles, columns across.
    private void Grid(List<string> ids, Func<string, string> name, Func<string, bool> on, int columns, Action<string> pressed)
    {
        var grid = new GridContainer { Columns = columns };
        grid.AddThemeConstantOverride("h_separation", 6);
        grid.AddThemeConstantOverride("v_separation", 6);
        _body.AddChild(grid);
        foreach (string id in ids)
        {
            Button button = Toggle(grid, name(id), on(id), () => pressed(id));
            button.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            button.CustomMinimumSize = new Vector2(0, 34);
        }
    }
}
