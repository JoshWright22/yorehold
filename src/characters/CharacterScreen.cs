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
    private Control _root = null!;
    private PortraitView _face = null!;
    private VBoxContainer _steps = null!;
    private Button _portraitButton = null!;
    private PortraitCropView _crop = null!;

    private void OpenPortrait()
    {
        if (_draft == null)
        {
            return;
        }
        CharacterChoices choices = _draft.Choices;
        _crop.Open(_world?.Files ?? App.Content(), choices.Name, choices.Portrait, choices.PortraitFocus, (picture, focus) =>
        {
            choices.Portrait = picture;
            choices.PortraitFocus = focus;
            Changed();
        });
    }
    private Label _faceName = null!;
    private Label _faceLine = null!;
    private DataPanel _library = null!;
    private string _libraryShown = "";

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
    // seats whose player makes their character once the adventure starts, and whether that is under way
    private readonly SortedSet<int> _makeAtStart = new();
    private bool _starting;
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
        _root = GetNode<Control>("Root");
        // the character large between the steps and the sheet: who is being made, not just numbers
        var faceColumn = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) };
        faceColumn.AddThemeConstantOverride("separation", 8);
        _face = new PortraitView { CustomMinimumSize = new Vector2(220, 260), MouseFilter = Control.MouseFilterEnum.Ignore };
        _faceName = new Label { ThemeTypeVariation = "TitleLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center };
        _faceName.AddThemeFontSizeOverride("font_size", 22);
        _faceLine = new Label { ThemeTypeVariation = "DimLabel", AutowrapMode = TextServer.AutowrapMode.WordSmart, HorizontalAlignment = HorizontalAlignment.Center };
        faceColumn.AddChild(_face);
        faceColumn.AddChild(_faceName);
        faceColumn.AddChild(_faceLine);
        // the player's own picture for the hero, and where it is cut
        _portraitButton = new Button { Text = "Choose portrait and crop", FocusMode = Control.FocusModeEnum.None, Visible = false };
        _portraitButton.Pressed += OpenPortrait;
        faceColumn.AddChild(_portraitButton);
        // over the whole screen, in the screens' theme (this layer has no themed root of its own)
        _crop = new PortraitCropView { Name = "Portrait", Theme = GD.Load<Theme>("res://scenes/hud/hud-theme.tres") };
        AddChild(_crop);
        _crop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _root.AddChild(faceColumn);
        _root.MoveChild(faceColumn, 1);
        // the steps down the left, as the design lists them: number, name, what was picked
        _steps = new VBoxContainer { CustomMinimumSize = new Vector2(184, 0) };
        _steps.AddThemeConstantOverride("separation", 4);
        _root.AddChild(_steps);
        _root.MoveChild(_steps, 0);
        _root.AddThemeConstantOverride("separation", 12);
        GetNode<Control>("Root/Left").CustomMinimumSize = new Vector2(370, 0);
        _library = GetNode<DataPanel>("Library");
        _library.ClosePressed += Close;
        _library.SourcePicked += id =>
        {
            Showing = id == "party" ? View.Party : View.Characters;
            Changed();
        };
        _library.ActionPressed += LibraryAction;
        Visible = false;
    }

    // opened from the title's Characters: there is no adventure, so no lobby to switch to
    private bool _libraryOnly;

    /// <summary>Opens on the library or the seats, reading the library folder afresh.</summary>
    public void Open(World world, string folder, View view, bool libraryOnly = false)
    {
        _libraryOnly = libraryOnly;
        _world = world;
        _rules = world.Rules;
        _compendium = world.Chapter.Compendium;
        _folder = folder;
        _noticeText = "";
        _dice = new Rng((ulong)Time.GetTicksUsec());
        Load();
        _seats.Clear();
        _makeAtStart.Clear();
        _starting = false;
        for (int i = 0; i < world.Chapter.Party.Count; i++)
        {
            // what the world plays now, so opening and starting again changes nothing
            PartyPick? now = i < world.PartyPicks.Count ? world.PartyPicks[i] : null;
            _seats.Add(now == null ? null : _entries.Find(e => e.FileName == now.Library && !e.Retired));
            // a new adventure's seats make their characters once it starts (Josh, 10/7), unless one is seated already
            if (view == View.Party && _seats[i] == null && _compendium.Classes.Count > 0)
            {
                _makeAtStart.Add(i);
            }
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

    public override void _ExitTree()
    {
        ChatPanel.Current?.StepAside("characters", false);
    }

    // Centred on the screen less the chat column when it is out, and no wider than what is left.
    private void MakeRoom()
    {
        float room = ChatPanel.Current?.Open == true ? ChatPanel.Width : 0;
        float left = _root.GetViewport().GetVisibleRect().Size.X - room;
        foreach ((Control panel, float half) in new[] { (_root, 620f), ((Control)_library, 560f) })
        {
            float fits = Mathf.Min(half, left / 2 - 16);
            panel.OffsetLeft = -fits - room / 2;
            panel.OffsetRight = fits - room / 2;
        }
    }

    public override void _Process(double delta)
    {
        ChatPanel.Current?.StepAside("characters", Visible);
        if (Visible)
        {
            MakeRoom();
        }
        if (_dirty && Visible)
        {
            Redraw();
        }
        if (Visible && Showing == View.Characters)
        {
            FillLibrary();
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
            // leaving creation goes back to the lobby, where the seat can take a ready character instead
            _draft = null;
            _starting = false;
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
        Clear(_steps);
        _steps.Visible = false;
        _portraitButton.Visible = false;
        Clear(_body);
        Clear(_buttons);
        _problem.Text = "";
        _root.Visible = Showing != View.Characters;
        _library.Visible = Showing == View.Characters;
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
        ShowFace();
        _problem.Visible = _problem.Text.Length > 0;
        _notice.Text = _noticeText;
        _notice.Visible = _noticeText.Length > 0;
        _notice.ThemeTypeVariation = _noticeBad ? "WarnLabel" : "DimLabel";
    }

    // The draft's or the picked seat's character: its picture from the content or an art pack by
    // race and class, its name and what it is. The game draws none of its own: without a picture
    // it is the disc and initial the map uses.
    private void ShowFace()
    {
        CharacterChoices? choices = Showing == View.Draft ? _draft?.Choices
            : Showing == View.Party && _seat < _seats.Count ? _seats[_seat]?.Choices : null;
        string name, race, classId;
        int level;
        if (choices != null)
        {
            name = choices.Name.Trim().Length > 0 ? choices.Name.Trim() : "New character";
            race = choices.Race;
            classId = choices.Levels.Count > 0 ? choices.Levels[^1].ClassId : "";
            level = choices.Level;
        }
        else if (Showing == View.Party && _world != null && _seat < _world.Chapter.Party.Count)
        {
            PartyMember member = _world.Chapter.Party[_seat];
            bool making = _makeAtStart.Contains(_seat);
            name = making ? $"Seat {_seat + 1}" : member.Name;
            race = "";
            classId = making ? "" : member.ClassId;
            level = _world.Chapter.Level;
        }
        else
        {
            return;
        }
        ContentFiles? files = _world?.Files;
        Texture2D? picture = null;
        foreach (string path in new[] { $"portraits/{race}-{classId}.png", $"portraits/{classId}.png", $"portraits/{race}.png" })
        {
            if (!path.Contains("/-") && !path.EndsWith("-.png", StringComparison.Ordinal) && !path.EndsWith("/.png", StringComparison.Ordinal)
                && PlayerArt.Texture(files, path) is Texture2D found)
            {
                picture = found;
                break;
            }
        }
        // a picture the player picked wins, cut where they chose
        PictureFocus? focus = null;
        if (choices is { Portrait.Length: > 0 } && PlayerArt.Texture(files, choices.Portrait) is Texture2D own)
        {
            picture = own;
            focus = choices.PortraitFocus;
        }
        _face.Show(name, Palette.Leather, false, picture, focus);
        _faceName.Text = name;
        string raceName = race.Length > 0 ? char.ToUpperInvariant(race[0]) + race[1..] + " " : "";
        // a system without levels or a class to ask for (Fate) names neither
        string levelWord = _rules.Advancement == "none" || !_rules.Sheet.Shows("level") ? "" : $"Level {level} ";
        string classWord = _rules.Creation.AsksClass ? ClassName(classId) : "";
        _faceLine.Text = classId.Length > 0 ? $"{levelWord}{raceName}{classWord}".Trim() : "picks once the adventure starts";
    }

    private void ViewTabs()
    {
        Toggle(_tabs, "Characters", Showing == View.Characters, () => Showing = View.Characters);
        Toggle(_tabs, "Lobby", Showing == View.Party, () => Showing = View.Party);
    }

    // ---------------------------------------------------------------- the library

    private void DrawCharacters()
    {
        _libraryShown = "";
        FillLibrary();
    }

    // The library as a data panel: the characters as rows, the graveyard and those away as tabs,
    // and the picked one's stat block on the right with New character and Level up under it.
    private void FillLibrary()
    {
        _library.SetHead("Characters", _noticeText);
        _library.SetSources(_libraryOnly ? Array.Empty<(string, string)>() : new[] { ("library", "Characters"), ("party", "Lobby") }, "library");
        _library.SetTabs(new[] { "All", "Ready", "Away", "Graveyard" });
        _library.SetChips(new[] { "Can level up" });
        _library.SetColumns(new DataColumn[] { new("Name", 150), new("Class", 120), new("Level", 50, true), new("XP", 60, true), new("State", 80) });
        var rows = new List<DataRow>();
        foreach (LibraryEntry entry in _entries)
        {
            string state = entry.Retired ? "Graveyard" : entry.Away.Length > 0 ? "Away" : "Ready";
            var tags = new HashSet<string> { state };
            if (CanLevel(entry))
            {
                tags.Add("Can level up");
            }
            string classes = string.Join(" / ", entry.Choices.Levels.Select(l => ClassName(l.ClassId)).Distinct());
            rows.Add(new DataRow
            {
                Key = entry.Path,
                Cells = new[] { entry.Choices.Name, classes, entry.Choices.Level.ToString(), entry.Choices.Xp.ToString(), state },
                Sort = new IComparable?[] { entry.Choices.Name, classes, entry.Choices.Level, entry.Choices.Xp, state },
                Tags = tags,
                Dim = entry.Retired,
            });
        }
        _library.SetRows(rows);
        _library.SetFoot($"{_entries.Count(e => !e.Retired)} in the library, {_entries.Count(e => e.Retired)} in the graveyard");

        int picked = _entries.FindIndex(e => e.Path == _library.Picked);
        if (picked >= 0)
        {
            _pick = picked;
        }
        LibraryEntry? chosen = picked >= 0 ? _entries[picked] : null;
        string signature = (chosen?.Path ?? "") + "|" + _entries.Count + "|" + _noticeText;
        if (signature == _libraryShown)
        {
            return;
        }
        _libraryShown = signature;
        string page;
        string note = "";
        if (chosen == null)
        {
            page = SheetPage.Text("No character picked", "No characters yet. Each one can go into any adventure. Make one with New character.");
        }
        else if (Carried(chosen) is CharacterSheet sheet)
        {
            page = SheetPage.Build(_rules, _compendium, sheet, chosen.Choices);
            note = chosen.Retired ? "In the graveyard: kept to look at, never played again."
                : chosen.Away.Length > 0 ? "Away in an adventure until it ends."
                : !CanLevel(chosen) ? "Levels up when its XP reaches the next level." : "";
        }
        else
        {
            CharacterBuild.Build(_rules, _compendium, chosen.Choices, out string problem);
            page = new BookPage().Title(chosen.Choices.Name).Rule().Warn(problem.Length == 0 ? "This character can't be built here." : problem).ToString();
        }
        var actions = new List<DataAction>
        {
            new("new", "New character", _compendium.Classes.Count > 0, "There are no classes to pick from."),
            new("level", "Level up", chosen != null && CanLevel(chosen), note.Length > 0 ? note : "Pick a character first."),
            new("close", "Close (Esc)"),
        };
        _library.SetEntry(page, actions, "");
        if (note.Length > 0)
        {
            _library.ShowWarning(note);
        }
    }

    private bool CanLevel(LibraryEntry entry)
    {
        // a new level the experience allows, or the picks a level gained in play still owes
        return !entry.Retired && entry.Away.Length == 0
            && (_rules.LevelForXp(entry.Choices.Xp) > entry.Choices.Level || CharacterDraft.OwesPicks(_rules, _compendium, entry.Choices))
            && CharacterBuild.Build(_rules, _compendium, entry.Choices) != null;
    }

    private void LibraryAction(string id)
    {
        LibraryEntry? chosen = _pick < _entries.Count && _entries[_pick].Path == _library.Picked ? _entries[_pick] : null;
        switch (id)
        {
            case "new":
                NewCharacter(View.Characters);
                break;
            case "level" when chosen != null:
                _draftBack = View.Characters;
                _draft = CharacterDraft.OwesPicks(_rules, _compendium, chosen.Choices)
                    ? CharacterDraft.FillLevel(_rules, _compendium, chosen.Choices)
                    : CharacterDraft.LevelUp(_rules, _compendium, chosen.Choices);
                Showing = View.Draft;
                break;
            case "close":
                Close();
                return;
        }
        Changed();
    }

    private void ShowCarried(LibraryEntry entry)
    {
        if (Carried(entry) is CharacterSheet sheet)
        {
            _sheet.ShowSheet(_rules, _compendium, sheet, entry.Choices, "");
        }
    }

    // A library file keeps its own gear, which the sheet built from choices doesn't know.
    private CharacterSheet? Carried(LibraryEntry entry)
    {
        CharacterSheet? sheet = CharacterBuild.Build(_rules, _compendium, entry.Choices);
        if (sheet == null)
        {
            return null;
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
        return sheet;
    }

    private void NewCharacter(View back)
    {
        _draftBack = back;
        _draft = new CharacterDraft(_rules, _compendium);
        Showing = View.Draft;
    }

    // ---------------------------------------------------------------- the seats

    // The lobby: a seat per hero the chapter has, each a player's (yours, until play goes online).
    // A seat makes its character once the adventure starts, takes the chapter's ready-made hero, or
    // takes one of the player's characters, which is how someone joining late brings theirs.
    private void DrawParty()
    {
        Chapter chapter = _world!.Chapter;
        _title.Text = "Lobby";
        ViewTabs();
        Heading(_body, $"{chapter.Title}: level {chapter.Level}, {chapter.Party.Count} seats");
        Dim(_body, "Each player picks who they play. A seat set to make one goes into character creation as the adventure starts.");
        for (int i = 0; i < chapter.Party.Count; i++)
        {
            int index = i;
            _body.AddChild(SeatCard(chapter, i, () => _seat = index));
        }
        Dim(_body, $"Who takes seat {_seat + 1}?");
        if (_compendium.Classes.Count > 0)
        {
            Toggle(_body, "Make one when we start", _makeAtStart.Contains(_seat), () =>
            {
                _makeAtStart.Add(_seat);
                _seats[_seat] = null;
            });
        }
        Toggle(_body, "Ready-made: " + chapter.Party[_seat].Name, _seats[_seat] == null && !_makeAtStart.Contains(_seat), () =>
        {
            _makeAtStart.Remove(_seat);
            _seats[_seat] = null;
        });
        foreach (LibraryEntry entry in _entries)
        {
            bool here = _seats[_seat]?.Path == entry.Path;
            bool elsewhere = !here && _seats.Any(s => s?.Path == entry.Path);
            if (entry.Retired || entry.Away.Length > 0 || elsewhere)
            {
                continue;
            }
            LibraryEntry taken = entry;
            Toggle(_body, $"{entry.Choices.Name}  (level {entry.Choices.Level})", here, () =>
            {
                _makeAtStart.Remove(_seat);
                _seats[_seat] = taken;
            });
        }

        if (_makeAtStart.Contains(_seat))
        {
            _sheet.ShowText($"Seat {_seat + 1}", $"Makes a level {chapter.Level} character once the adventure starts: origin, class and scores, skills and feats.\n\n"
                + "Escape during it comes back here, where the seat can take a ready character instead.");
        }
        else if (_seats[_seat] is LibraryEntry chosen)
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
        // leaving is drawn in red, starting in amber, as the design's lobby foot has them
        Button leave = Push(_buttons, "Leave lobby", true, Close);
        leave.AddThemeStyleboxOverride("normal", Outline(Palette.Red));
        leave.AddThemeStyleboxOverride("hover", Outline(Palette.Rose));
        leave.AddThemeColorOverride("font_color", Palette.Red);
        leave.AddThemeColorOverride("font_hover_color", Palette.Rose);
        Button start = Push(_buttons, "Start adventure", true, Start);
        start.ThemeTypeVariation = "EndTurnButton";
        start.AddThemeFontSizeOverride("font_size", 16);
    }

    private static StyleBoxFlat Outline(Color line)
    {
        var box = new StyleBoxFlat { BgColor = Palette.Ink, BorderColor = line };
        box.SetBorderWidthAll(1);
        box.SetContentMarginAll(8);
        return box;
    }

    // A seat as the design's lobby draws it: its number, the hero's face, who plays it and what
    // they bring, and where it stands, the picked seat outlined in amber.
    private Button SeatCard(Chapter chapter, int seat, Action pick)
    {
        LibraryEntry? seated = _seats[seat];
        bool making = _makeAtStart.Contains(seat);
        string hero = making ? "A new hero"
            : seated != null ? $"{seated.Choices.Name} · {ClassName(seated.Choices.Levels[^1].ClassId)}, level {seated.Choices.Level}"
            : $"{chapter.Party[seat].Name} · {ClassName(chapter.Party[seat].ClassId)}, level {chapter.Level}";
        string how = making ? "Makes a character when we start" : seated != null ? "From your characters" : "Ready-made for this adventure";
        var card = new Button { ToggleMode = true, FocusMode = Control.FocusModeEnum.None, CustomMinimumSize = new Vector2(0, 84), ThemeTypeVariation = "PickButton" };
        card.SetPressedNoSignal(seat == _seat);
        card.Pressed += () =>
        {
            pick();
            Changed();
        };
        var row = new HBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 12);
        card.AddChild(row);
        row.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        row.OffsetLeft = 12;
        row.OffsetTop = 6;
        row.OffsetRight = -12;
        row.OffsetBottom = -6;
        var number = new Label { Text = (seat + 1).ToString(), ThemeTypeVariation = "NumberLabel", VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        row.AddChild(number);
        var face = new PortraitView { CustomMinimumSize = new Vector2(54, 72), MouseFilter = Control.MouseFilterEnum.Ignore };
        string name = making ? "?" : seated?.Choices.Name ?? chapter.Party[seat].Name;
        face.Show(name, Palette.Leather, false, null);
        row.AddChild(face);
        var lines = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore, Alignment = BoxContainer.AlignmentMode.Center };
        lines.AddThemeConstantOverride("separation", 0);
        row.AddChild(lines);
        var player = new Label { Text = "You", ThemeTypeVariation = "TitleLabel", MouseFilter = Control.MouseFilterEnum.Ignore };
        player.AddThemeFontSizeOverride("font_size", 15);
        player.AddThemeColorOverride("font_color", Palette.Bone);
        lines.AddChild(player);
        var heroLine = new Label { Text = hero, MouseFilter = Control.MouseFilterEnum.Ignore, ClipText = true };
        heroLine.AddThemeFontSizeOverride("font_size", 13);
        heroLine.AddThemeColorOverride("font_color", Palette.Bone);
        lines.AddChild(heroLine);
        var howLine = new Label { Text = how, ThemeTypeVariation = "DimLabel", MouseFilter = Control.MouseFilterEnum.Ignore };
        howLine.AddThemeFontSizeOverride("font_size", 12);
        lines.AddChild(howLine);
        var state = new Label { Text = making ? "CHOOSING" : "READY", ThemeTypeVariation = "CapsLabel", VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
        state.AddThemeFontSizeOverride("font_size", 10);
        state.AddThemeColorOverride("font_color", making ? Palette.Ash : Palette.Blue);
        row.AddChild(state);
        return card;
    }

    private void Start()
    {
        // the seats that make their characters do so first, one after another, then the chapter starts with them
        if (_makeAtStart.Count > 0)
        {
            _starting = true;
            _seat = _makeAtStart.Min;
            Notice($"Seat {_seat + 1}: make the character for this seat. Escape goes back to the lobby.", false);
            NewCharacter(View.Party);
            Changed();
            return;
        }
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
            _title.Text = d.Steps[d.Step].Name;
            _steps.Visible = true;
            _portraitButton.Visible = true;
            for (int i = 0; i < d.Steps.Count; i++)
            {
                // an earlier step can always be reopened, the next one once this one is done
                bool open = i <= d.Step || (i == d.Step + 1 && d.StepDone(d.Step));
                int step = i;
                Button tab = Toggle(_steps, $"{i + 1}   {d.Steps[i].Name}", i == d.Step, () => d.Step = step);
                tab.Disabled = !open;
                tab.Alignment = HorizontalAlignment.Left;
                tab.CustomMinimumSize = new Vector2(0, 40);
                // what this step has picked so far, dim on the right
                var picked = new Label { Text = Picked(d, i), ThemeTypeVariation = "DimLabel", HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore, ClipText = true };
                picked.AddThemeFontSizeOverride("font_size", 12);
                tab.AddChild(picked);
                picked.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                picked.OffsetLeft = 96;
                picked.OffsetRight = -10;
            }
        }

        // the parts of this step, in the order the rules system lists them
        IReadOnlyList<string> parts = d.LevellingUp ? new[] { "skills", "feats" } : d.Steps[d.Step].Parts;
        foreach (string part in parts)
        {
            switch (part)
            {
                case "name":
                    NamePart(d);
                    break;
                case "fields" when _rules.Fields.Count > 0:
                    FieldsPart(d);
                    break;
                case "options":
                    // the system's own kinds of pick (a heritage), each from those open to this character
                    foreach (OptionKind kind in _rules.OptionKinds)
                    {
                        List<string> open = d.OptionIds(kind.Id);
                        if (open.Count == 0)
                        {
                            continue;
                        }
                        Heading(_body, kind.Name);
                        string id = kind.Id;
                        Grid(open, o => _compendium.Options[o].Name, o => d.Choices.Options.GetValueOrDefault(id) == o, 3, o => d.SetOption(id, o));
                    }
                    break;
                case "race" when _compendium.Races.Count > 0:
                    Heading(_body, _rules.Creation.NameOf("race"));
                    Grid(d.RaceIds(), id => _compendium.Races[id].Name, id => id == d.Choices.Race, 4, d.SetRace);
                    break;
                case "background" when _compendium.Backgrounds.Count > 0:
                    Heading(_body, _rules.Creation.NameOf("background"));
                    Grid(d.BackgroundIds(), id => _compendium.Backgrounds[id].Name, id => id == d.Choices.Background, 3, d.SetBackground);
                    break;
                case "class":
                    Heading(_body, _rules.Creation.NameOf("class"));
                    ClassPicker(d);
                    break;
                case "scores":
                    ScoresPart(d);
                    break;
                case "skills":
                    SkillsPart(d, className);
                    break;
                case "feats" when !parts.Contains("skills"):
                    SkillsPart(d, className);
                    break;
            }
        }

        _problem.Text = d.StepProblem(d.Step);
        ShowDraftSide(d);
        // Back and Next say where they go, as the design's foot does; the way forward is amber
        if (!d.LevellingUp && d.Step > 0)
        {
            Push(_buttons, $"Back: {d.Steps[d.Step - 1].Name}", true, () => d.Step--);
        }
        else
        {
            Push(_buttons, "Cancel (Esc)", true, Back);
        }
        bool last = d.LevellingUp || d.Step == d.Steps.Count - 1;
        Button forward = last
            ? Push(_buttons, d.LevellingUp ? "Level up" : "Finish", d.Finished(), FinishDraft)
            : Push(_buttons, $"Next: {d.Steps[d.Step + 1].Name}", d.StepDone(d.Step), () => d.Step++);
        forward.ThemeTypeVariation = "EndTurnButton";
        forward.AddThemeFontSizeOverride("font_size", 16);
    }

    private void NamePart(CharacterDraft d)
    {
        Heading(_body, "Name");
        var name = new LineEdit { Text = d.Choices.Name, MaxLength = 64, PlaceholderText = "Type a name", CustomMinimumSize = new Vector2(0, 36) };
        name.TextChanged += text =>
        {
            d.SetName(text);
            ShowDraftSide(d);
        };
        _body.AddChild(name);
    }

    // The ruleset's fields (Fate's high concept, trouble, aspects): a line to type in for each.
    private void FieldsPart(CharacterDraft d)
    {
        foreach (FieldDefinition field in _rules.Fields)
        {
            Heading(_body, field.Required ? field.Name : field.Name + " (optional)");
            List<string> lines = d.Choices.Fields.TryGetValue(field.Id, out List<string>? had) ? had : new List<string>();
            for (int i = 0; i < field.Count; i++)
            {
                int line = i;
                var edit = new LineEdit
                {
                    Text = line < lines.Count ? lines[line] : "", MaxLength = FieldDefinition.MostLetters,
                    PlaceholderText = field.Hint.Length > 0 ? field.Hint : "Type a phrase", CustomMinimumSize = new Vector2(0, 32),
                };
                edit.TextChanged += text =>
                {
                    d.SetField(field.Id, line, text);
                    ShowDraftSide(d);
                };
                _body.AddChild(edit);
            }
        }
    }

    private void ScoresPart(CharacterDraft d)
    {
        string method = d.Choices.ScoreMethod;
        string scores = _rules.Creation.NameOf("scores");
        Heading(_body, method switch
        {
            "pointBuy" => $"{scores}: {d.PointsLeft()} of {_rules.ScoreMethods.PointBudget} points left",
            "roll" => $"{scores}: press Roll again to reroll",
            "boosts" => $"{scores}: {d.BoostsLeft()} of {_rules.ScoreMethods.BoostCount} boosts left, + gives one",
            _ => $"{scores}: + and - swap two",
        });
        var methods = new HBoxContainer();
        methods.AddThemeConstantOverride("separation", 6);
        _body.AddChild(methods);
        // only the ways the rules system offers
        foreach ((string id, string label) in new[] { ("boosts", "Boosts"), ("array", "Standard array"), ("pointBuy", "Point buy"), ("roll", "Roll") }
            .Where(m => _rules.Creation.ScoreMethods.Contains(m.Item1)))
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

    // skills to train and feats to take, as a level brings them
    private void SkillsPart(CharacterDraft d, string className)
    {
        if (d.SkillPicks() > 0)
        {
            Heading(_body, $"Skills: train {d.SkillPicks()}");
            List<string> picked = d.Picked("skills");
            Grid(d.SkillOptions.ToList(), id => _rules.Skill(id)?.Name ?? id, picked.Contains, 3, d.ToggleSkill);
        }
        foreach (string kind in d.FeatKinds())
        {
            Heading(_body, _rules.FeatKindName(kind));
            List<string> picked = d.Picked("feats");
            List<string> options = d.FeatOptions(kind);
            if (options.Count == 0)
            {
                Dim(_body, "None the character can take yet.");
            }
            Grid(options, id => _compendium.Feats[id].Name, picked.Contains, 2, d.PickFeat);
        }
        foreach (string kind in d.LevelOptionKinds())
        {
            // the system's own picks at this level (an archetype, a subclass)
            Heading(_body, _rules.OptionKinds.Find(k => k.Id == kind)?.Name ?? kind);
            List<string> open = d.LevelOptionIds(kind);
            if (open.Count == 0)
            {
                Dim(_body, "None the character can take yet.");
            }
            List<string> chosen = d.Picked("options");
            Grid(open, id => _compendium.Options[id].Name, chosen.Contains, 2, d.PickLevelOption);
        }
        (int boosts, int step) = d.LevelBoosts();
        if (boosts > 0)
        {
            // the level's raises: each a different ability, by the class table's step
            List<string> raised = d.Picked("boosts");
            // the picks named, so the same one twice (+2 to one) shows
            string taken = raised.Count == 0 ? "" : ": " + string.Join(", ", raised.Select(id => _rules.Ability(id)?.Name ?? id));
            Heading(_body, $"{_rules.Creation.NameOf("scores")}: raise {boosts} by {step} each{taken}");
            Grid(_rules.Abilities.Select(a => a.Id).ToList(), id => _rules.Ability(id)?.Name ?? id, raised.Contains, 3, d.ToggleBoost);
        }
        if (d.SkillPicks() == 0 && d.FeatKinds().Count == 0 && boosts == 0 && d.LevelOptionKinds().Count == 0)
        {
            Dim(_body, $"Nothing to pick at this {className} level.");
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
    // What a step has picked, for the step list: the species, class or background's name, the name typed.
    private string Picked(CharacterDraft d, int step)
    {
        foreach (string part in d.Steps[step].Parts)
        {
            string picked = part switch
            {
                "name" => d.Choices.Name.Trim(),
                "race" => d.Choices.Race.Length > 0 && _compendium.Races.TryGetValue(d.Choices.Race, out var race) ? race.Name : "",
                "background" => d.Choices.Background.Length > 0 && _compendium.Backgrounds.TryGetValue(d.Choices.Background, out var background) ? background.Name : "",
                "class" => d.Choices.Levels.Count > 0 ? ClassName(d.Choices.Levels[^1].ClassId) : "",
                _ => "",
            };
            if (picked.Length > 0)
            {
                return picked;
            }
        }
        return "";
    }

    private void ShowDraftSide(CharacterDraft d)
    {
        _sheet.ShowSheet(_rules, _compendium, d.Sheet, d.Choices, d.Problem);
        // the face card follows the name as it is typed
        ShowFace();
        _problem.Text = d.StepProblem(d.Step);
        _problem.Visible = _problem.Text.Length > 0;
        foreach (Node child in _buttons.GetChildren())
        {
            if (child is Button next && next.Text.StartsWith("Next", StringComparison.Ordinal))
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
            App.FilesWritten();
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
                _makeAtStart.Remove(_seat);
            }
        }
        if (forParty && _starting)
        {
            // on to the next seat that makes one, or into the adventure when they all have
            Start();
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
