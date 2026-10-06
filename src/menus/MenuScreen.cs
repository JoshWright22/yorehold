using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>What the title's entries ask Main for.</summary>
public enum MenuOrder
{
    Continue,
    NewAdventure,
    QuickStart,
    LoadSave,
    Characters,
    Create,
    Resume,
    QuitToTitle,
    Exit,
}

/// <summary>
/// The menus around the game: the title, the pause list over a game, and the load, settings and
/// credits screens. The title and the pause list are a contents page, entries down the left and
/// what the picked one is on the right; the other three are data panels. It only shows and asks:
/// Ordered goes to Main, which starts, loads and quits.
/// </summary>
public partial class MenuScreen : CanvasLayer
{
    public enum Page
    {
        None,
        Title,
        Pause,
        Load,
        Settings,
        Credits,
    }

    private sealed record Entry(string Id, string Label, string Fact, bool Enabled, string Why);

    /// <summary>An entry was picked; the text is the save's path for LoadSave.</summary>
    public event Action<MenuOrder, string>? Ordered;

    public Page Showing { get; private set; } = Page.None;
    public bool IsOpen => Showing != Page.None;
    /// <summary>Why a save can't be loaded right now (in a fight); empty when it can.</summary>
    public string LoadRefusal { get; set; } = "";

    // the scene has all of these
    private ColorRect _back = null!;
    private Control _title = null!;
    private Label _name = null!;
    private Label _sub = null!;
    private Label _heading = null!;
    private VBoxContainer _entries = null!;
    private RichTextLabel _page = null!;
    private Label _notice = null!;
    private Label _foot = null!;
    private LoadPanel _load = null!;
    private SettingsPanel _settings = null!;
    private CreditsPanel _credits = null!;

    private readonly List<Entry> _list = new();
    private readonly List<Button> _buttons = new();
    private Page _home = Page.Title;
    private int _picked;
    private string _listShown = "";
    private string _pageShown = "";
    private List<SaveSummary> _saves = new();
    private string _contentLine = "";
    private Adventure? _adventure;

    public override void _Ready()
    {
        _back = GetNode<ColorRect>("Back");
        _title = GetNode<Control>("Title");
        _name = GetNode<Label>("Title/Name");
        _sub = GetNode<Label>("Title/Sub");
        _heading = GetNode<Label>("Title/Heading");
        _entries = GetNode<VBoxContainer>("Title/Entries");
        _page = GetNode<RichTextLabel>("Title/Page/Text");
        _notice = GetNode<Label>("Title/Notice");
        _foot = GetNode<Label>("Title/Foot");
        _load = new LoadPanel(GetNode<DataPanel>("Load"));
        _settings = new SettingsPanel(GetNode<DataPanel>("Settings"));
        _credits = new CreditsPanel(GetNode<DataPanel>("Credits"));
        _load.View.ClosePressed += Back;
        _settings.View.ClosePressed += Back;
        _credits.View.ClosePressed += Back;
        _load.LoadPressed += path => Ordered?.Invoke(MenuOrder.LoadSave, path);
        _load.Changed += ReadSaves;
        Open(Page.None);
    }

    /// <summary>Shows a page; None puts the menus away.</summary>
    public void Open(Page page)
    {
        if (page is Page.Title or Page.Pause)
        {
            _home = page;
            _picked = -1;
            _listShown = "";
            ReadSaves();
            ReadContent();
        }
        if (page == Page.Load)
        {
            ReadSaves();
            _load.View.Reset();
        }
        if (page == Page.Settings && Showing != Page.Settings)
        {
            _settings.Opened();
        }
        Showing = page;
        Visible = page != Page.None;
        _title.Visible = page is Page.Title or Page.Pause;
        _load.View.Visible = page == Page.Load;
        _settings.View.Visible = page == Page.Settings;
        _credits.View.Visible = page == Page.Credits;
        // the title has the screen to itself; over a game the map shows through, darkened toward ink
        _back.Color = _home == Page.Title ? Palette.Ink : Palette.Faded(Palette.Ink, 0.82f);
        Say("");
    }

    /// <summary>A line under the list, like why a save wouldn't load.</summary>
    public void Say(string notice)
    {
        _notice.Text = notice;
    }

    public override void _Process(double delta)
    {
        switch (Showing)
        {
            case Page.Title:
            case Page.Pause:
                FillList();
                break;
            case Page.Load:
                _load.Refresh(_saves, LoadRefusal);
                break;
            case Page.Settings:
                _settings.Refresh();
                break;
            case Page.Credits:
                _credits.Refresh();
                break;
        }
    }

    public override void _Input(InputEvent @event)
    {
        // a key being bound is taken before anything else can read it as a shortcut
        if (Showing == Page.Settings && _settings.Capturing && @event is InputEventKey { Pressed: true, Echo: false } key)
        {
            _settings.Captured(key.Keycode);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!IsOpen || @event is not InputEventKey { Pressed: true } key)
        {
            return;
        }
        // nothing under the menus hears a key while they are up
        GetViewport().SetInputAsHandled();
        if (key.Echo && key.Keycode is not (Key.Up or Key.Down))
        {
            return;
        }
        switch (key.Keycode)
        {
            case Key.Escape:
                if (Showing == Page.Pause)
                {
                    Ordered?.Invoke(MenuOrder.Resume, "");
                }
                else if (Showing != Page.Title)
                {
                    Back();
                }
                break;
            case Key.Up when _title.Visible:
                Step(-1);
                break;
            case Key.Down when _title.Visible:
                Step(1);
                break;
            case Key.Enter or Key.KpEnter when _title.Visible:
                Press(_picked);
                break;
            case Key.Enter or Key.KpEnter when Showing == Page.Load:
                _load.LoadPicked();
                break;
        }
    }

    private void Back()
    {
        Open(_home);
    }

    private void ReadSaves()
    {
        _saves = SaveSummary.List(Places.SavesFolder(), App.Content());
    }

    private void ReadContent()
    {
        ContentFiles files = App.Content();
        try
        {
            _adventure = Adventure.Load(files);
        }
        catch (ContentException)
        {
            _adventure = null; // the title still opens; starting says what is wrong
        }
        try
        {
            ContentPackage package = ContentPackage.Load(files);
            int chapters = _adventure?.ChapterFolders.Count ?? package.Chapters.Count;
            _contentLine = $"{package.Name}: {Count(chapters, "chapter")}, {Count(files.List("classes").Count, "class")}, "
                + $"{Count(files.List("creatures").Count, "creature")}, {Count(files.List("items").Count, "item")}";
        }
        catch (ContentException error)
        {
            _contentLine = "Content couldn't be loaded: " + error.Message;
        }
    }

    private SaveSummary? Autosave()
    {
        string path = Places.SaveFile();
        return _saves.Find(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    private void BuildList()
    {
        _list.Clear();
        if (Showing == Page.Pause)
        {
            _list.Add(new Entry("resume", "Resume", "Esc", true, ""));
            _list.Add(new Entry("settings", "Settings", "", true, ""));
            _list.Add(new Entry("load", "Load", Count(_saves.Count, "save"), _saves.Count > 0, "There is no save yet."));
            _list.Add(new Entry("quit", "Save and quit to title", "", true, ""));
            return;
        }
        SaveSummary? save = Autosave();
        bool loadable = save != null && save.Problem.Length == 0;
        _list.Add(new Entry("continue", "Continue", loadable ? save!.ChapterTitle : "no save", loadable,
            save == null ? "There is no save yet." : "The save can't be read: " + save.Problem));
        _list.Add(new Entry("new", "New adventure", _adventure?.Title ?? "", true, ""));
        _list.Add(new Entry("quick", "Quick start", "ready-made party", true, ""));
        _list.Add(new Entry("load", "Load", Count(_saves.Count, "save"), _saves.Count > 0, "There is no save yet."));
        _list.Add(new Entry("characters", "Characters", LibraryFact(), true, ""));
        _list.Add(new Entry("create", "Create", "the editor", true, ""));
        _list.Add(new Entry("settings", "Settings", "", true, ""));
        _list.Add(new Entry("credits", "Credits", "", true, ""));
        _list.Add(new Entry("exit", "Exit", "", true, ""));
    }

    private static string Count(int count, string what)
    {
        string many = what.EndsWith("s", StringComparison.Ordinal) ? what + "es" : what + "s";
        return count == 0 ? $"no {many}" : count == 1 ? $"1 {what}" : $"{count} {many}";
    }

    private static string LibraryFact()
    {
        try
        {
            List<LibraryEntry> entries = CharacterLibrary.List(Places.CharactersFolder());
            int ready = entries.Count(e => !e.Retired);
            return ready == 0 ? "none yet" : $"{ready} made";
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private void FillList()
    {
        BuildList();
        bool paused = Showing == Page.Pause;
        _name.Text = paused ? "PAUSED" : "YOREHOLD";
        _sub.Text = paused ? "The adventure waits." : $"Version {Rules.Version.Text}";
        _heading.Text = paused ? "GAME" : "CONTENTS";
        _foot.Text = _contentLine;
        if (_picked < 0 || _picked >= _list.Count)
        {
            _picked = _list.FindIndex(e => e.Enabled);
        }

        string signature = Showing + "|" + string.Join("|", _list.Select(e => $"{e.Id}={e.Label}={e.Fact}={e.Enabled}"));
        if (signature != _listShown)
        {
            _listShown = signature;
            foreach (Button old in _buttons)
            {
                _entries.RemoveChild(old);
                old.QueueFree();
            }
            _buttons.Clear();
            for (int i = 0; i < _list.Count; i++)
            {
                int index = i;
                Entry entry = _list[i];
                var button = new Button
                {
                    Text = entry.Label,
                    ToggleMode = true,
                    Alignment = HorizontalAlignment.Left,
                    CustomMinimumSize = new Vector2(0, 30),
                    ThemeTypeVariation = i % 2 == 0 ? "RowButton" : "RowOddButton",
                    FocusMode = Control.FocusModeEnum.None,
                };
                if (!entry.Enabled)
                {
                    button.AddThemeColorOverride("font_color", Palette.Slate);
                }
                var fact = new Label
                {
                    Text = entry.Fact,
                    ThemeTypeVariation = "DimLabel",
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    ClipText = true,
                    TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                };
                fact.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                fact.OffsetLeft = 150;
                fact.OffsetRight = -8;
                button.AddChild(fact);
                button.MouseEntered += () => _picked = index;
                button.Pressed += () => Press(index);
                _entries.AddChild(button);
                _buttons.Add(button);
            }
        }
        for (int i = 0; i < _buttons.Count; i++)
        {
            _buttons[i].SetPressedNoSignal(i == _picked);
        }

        string page = _picked >= 0 && _picked < _list.Count ? PageFor(_list[_picked]) : "";
        if (page != _pageShown)
        {
            _pageShown = page;
            _page.Text = page;
        }
    }

    private void Step(int by)
    {
        if (_list.Count > 0)
        {
            _picked = ((_picked < 0 ? 0 : _picked + by) + _list.Count) % _list.Count;
        }
    }

    private void Press(int index)
    {        if (index < 0 || index >= _list.Count)
        {
            return;
        }
        Entry entry = _list[index];
        _picked = index;
        if (!entry.Enabled)
        {
            Say(entry.Why);
            return;
        }
        switch (entry.Id)
        {
            case "continue": Ordered?.Invoke(MenuOrder.Continue, Places.SaveFile()); break;
            case "new": Ordered?.Invoke(MenuOrder.NewAdventure, ""); break;
            case "quick": Ordered?.Invoke(MenuOrder.QuickStart, ""); break;
            case "characters": Ordered?.Invoke(MenuOrder.Characters, ""); break;
            case "create": Ordered?.Invoke(MenuOrder.Create, ""); break;
            case "resume": Ordered?.Invoke(MenuOrder.Resume, ""); break;
            case "quit": Ordered?.Invoke(MenuOrder.QuitToTitle, ""); break;
            case "exit": Ordered?.Invoke(MenuOrder.Exit, ""); break;
            case "load": Open(Page.Load); break;
            case "settings": Open(Page.Settings); break;
            case "credits": Open(Page.Credits); break;
        }
    }

    // What the picked entry is, as a book page: the save for Continue, the adventure for a new one.
    private string PageFor(Entry entry)
    {
        var page = new BookPage().Title(entry.Label);
        switch (entry.Id)
        {
            case "continue":
                if (Autosave() is SaveSummary save)
                {
                    LoadPanel.Write(page, save);
                }
                else
                {
                    page.Sub("no save yet").Rule().Text("The game saves itself after a fight, a rest, a door, a conversation and on the way to the next chapter.");
                }
                break;
            case "new":
                if (_adventure != null)
                {
                    page.Sub(_adventure.Title).Rule();
                    page.Stats(("Levels", $"{_adventure.MinLevel} to {_adventure.MaxLevel}"), ("Party", _adventure.RecommendedPartySize.ToString()),
                        ("Chapters", _adventure.ChapterFolders.Count.ToString()));
                    page.Text(_adventure.Description).Gap();
                }
                else
                {
                    page.Rule();
                }
                page.Text("Each seat takes one of your own characters or the chapter's ready-made hero. A character who dies on the way goes to the graveyard.");
                if (Autosave() != null)
                {
                    page.Gap().Warn("Starting again writes over the save once the game next saves.");
                }
                break;
            case "quick":
                page.Sub("the ready-made party").Rule().Text("Starts the first chapter with the heroes it comes with. Nothing to pick.");
                break;
            case "load":
                page.Sub(Count(_saves.Count, "save")).Rule().Text("Every save on this machine with where the party was and who was in it. The game keeps the save before the last one as a backup.");
                break;
            case "characters":
                page.Sub("the library").Rule().Text("The characters you have made, who is away on an adventure and who lies in the graveyard. Make new ones and spend their levels here.");
                break;
            case "create":
                page.Sub("the editor").Rule().Text("Draw maps and place encounters for your own adventures. It edits the same files the game plays.");
                if (App.Settings.LastCreatePackage.Length > 0)
                {
                    page.Gap().Stat("Last opened", App.Settings.LastCreatePackage);
                }
                break;
            case "settings":
                page.Sub("camera, display, game and keys").Rule();
                page.Stat("Lighting", SettingsPanel.LightingWords[Math.Clamp(App.Settings.Lighting, 0, 3)]);
                page.Stat("Pan speed", App.Settings.PanSpeed.ToString("0"));
                page.Stat("Keys changed", App.Keys.Overrides().Count.ToString());
                break;
            case "credits":
                page.Sub("who made what").Rule().Text("The game, the engine it runs on and every library inside it, each with its licence.");
                break;
            case "exit":
                page.Sub("close the game").Rule().Text("Nothing is lost: the game saves as it goes.");
                break;
            case "resume":
                page.Sub("back to the map").Rule().Text("Escape does the same.");
                break;
            case "quit":
                page.Sub("leave this adventure for now").Rule().Text("Saves if the party is between fights, then goes back to the title. In a fight the last save stands.");
                break;
        }
        return page.ToString();
    }
}
