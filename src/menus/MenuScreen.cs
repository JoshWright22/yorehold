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
        Adventures,
    }

    // Under: one of Play's choices, shown smaller under it while Play is open
    private sealed record Entry(string Id, string Label, string Fact, bool Enabled, string Why, bool Under = false);

    // the title's Play is open: its choices (continue, new, quick, load, characters) show under it
    private bool _playOpen;

    /// <summary>An entry was picked; the text is the save's path for LoadSave.</summary>
    public event Action<MenuOrder, string>? Ordered;

    public Page Showing { get; private set; } = Page.None;
    /// <summary>The world being played, for where the pause page says the party is.</summary>
    public Func<World?>? Playing { get; set; }
    public bool IsOpen => Showing != Page.None;
    /// <summary>Why a save can't be loaded right now (in a fight); empty when it can.</summary>
    public string LoadRefusal { get; set; } = "";

    // the scene has all of these
    private ColorRect _back = null!;
    private Control _title = null!;
    private Label _name = null!;
    private Label _sub = null!;
    private Font _subSpaced = null!;
    private Font _subPlain = null!;
    private BannerView _banner = null!;
    private TextureRect _logo = null!;
    private Button _creditsLink = null!;
    private VBoxContainer _entries = null!;
    private RichTextLabel _page = null!;
    private Label _notice = null!;
    private Label _foot = null!;
    private Label _online = null!;
    private LoadPanel _load = null!;
    private SettingsPanel _settings = null!;
    private CreditsPanel _credits = null!;
    private AdventuresPanel _adventures = null!;

    private readonly List<Entry> _list = new();
    private readonly List<Button> _buttons = new();
    private Page _home = Page.Title;
    private int _picked;
    private string _listShown = "";
    private string _pageShown = "";
    private List<SaveSummary> _saves = new();
    private string _contentLine = "";
    private Adventure? _adventure;
    private int _syncChangesSeen;

    public override void _Ready()
    {
        _back = GetNode<ColorRect>("Back");
        _title = GetNode<Control>("Title");
        _name = GetNode<Label>("Title/Name");
        _sub = GetNode<Label>("Title/Sub");
        // the wordmark and the tagline are spaced out, as the design sets them
        Spaced(_name, 10);
        Spaced(_sub, 2);
        _subSpaced = _sub.GetThemeFont("font");
        _subPlain = ((FontVariation)_subSpaced).BaseFont;
        _banner = GetNode<BannerView>("Banner");
        _logo = GetNode<TextureRect>("Title/Logo");
        _creditsLink = GetNode<Button>("Title/Credits");
        _creditsLink.Pressed += () => Open(Page.Credits);
        _entries = GetNode<VBoxContainer>("Title/Entries");
        _page = GetNode<RichTextLabel>("Title/Page/Text");
        _notice = GetNode<Label>("Title/Notice");
        _foot = GetNode<Label>("Title/Foot");
        _online = GetNode<Label>("Title/Online");
        _load = new LoadPanel(GetNode<DataPanel>("Load"));
        _settings = new SettingsPanel(GetNode<Control>("Settings"));
        _credits = new CreditsPanel(GetNode<DataPanel>("Credits"));
        _adventures = new AdventuresPanel(GetNode<DataPanel>("Adventures"));
        _pages = new Control[] { GetNode<Control>("Load"), GetNode<Control>("Credits"), GetNode<Control>("Adventures") };
        _adventures.View.ClosePressed += Back;
        _adventures.StartPressed += package => Ordered?.Invoke(MenuOrder.NewAdventure, package);
        _adventures.CoverPicked += (files, cover) => _banner.Pin(files, cover);
        _load.View.ClosePressed += Back;
        _settings.Closed += Back;
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
        if (page == Page.Adventures && Showing != Page.Adventures)
        {
            _adventures.Read();
        }
        Showing = page;
        Visible = page != Page.None;
        _title.Visible = page is Page.Title or Page.Pause;
        _load.View.Visible = page == Page.Load;
        _settings.View.Visible = page == Page.Settings;
        _credits.View.Visible = page == Page.Credits;
        _adventures.View.Visible = page == Page.Adventures;
        // the title stands on the players' art, the adventures on the picked one's cover; paused,
        // the world stays in view beside the band (not under a see-through veil, which would put
        // the map in colours off the palette)
        if (page == Page.Title)
        {
            _banner.Read();
        }
        _banner.Visible = page is Page.Title or Page.Adventures;
        _back.Visible = page != Page.Pause;
        _back.Color = Palette.Ink;
        _creditsLink.Visible = page == Page.Title;
        // paused, the card would sit on the hotbar and only say what the buttons say; on the title it
        // only says where pictures come from, while there are none
        GetNode<Control>("Title/Page").Visible = page != Page.Pause && !(page == Page.Title && _banner.HasPictures);
        Texture2D? logo = GameScreen.Logo();
        _logo.Texture = logo;
        _logo.Visible = logo != null && page == Page.Title;
        Say("");
    }

    /// <summary>A line under the list, like why a save wouldn't load.</summary>
    public void Say(string notice)
    {
        _notice.Text = notice;
    }

    private Control[] _pages = System.Array.Empty<Control>();

    // The pages leave the chat column its room when it is out: the options page ends at it, the
    // book-like pages centre on what is left.
    private void MakeRoom()
    {
        float room = ChatPanel.Current?.Open == true ? ChatPanel.Width : 0;
        _settings.View.OffsetRight = -room;
        float half = Mathf.Min(480, (GetViewport().GetVisibleRect().Size.X - room) / 2 - 16);
        foreach (Control page in _pages)
        {
            page.OffsetLeft = -half - room / 2;
            page.OffsetRight = half - room / 2;
        }
    }

    public override void _Process(double delta)
    {
        MakeRoom();
        // a save may have arrived from another device, or been finished there
        if (App.Sync.LocalChanges != _syncChangesSeen)
        {
            _syncChangesSeen = App.Sync.LocalChanges;
            ReadSaves();
        }
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
            case Page.Adventures:
                _adventures.Refresh();
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
            case Key.Escape when Showing == Page.Title && _playOpen:
                _playOpen = false;
                _picked = 0;
                break;
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
            // the keys the title's buttons show
            case Key.P or Key.E or Key.O when Showing == Page.Title && !key.CtrlPressed && !key.AltPressed:
                string id = key.Keycode == Key.P ? "play" : key.Keycode == Key.E ? "create" : "settings";
                int at = _list.FindIndex(e => e.Id == id);
                if (at >= 0)
                {
                    _picked = at;
                    Press(at);
                }
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

    // Where the party is: the adventure, the chapter and the round of a fight.
    private string PausedWhere()
    {
        if (Playing?.Invoke() is not World world)
        {
            return "";
        }
        var parts = new List<string>();
        if (world.Adventure is Adventure adventure && adventure.Title.Length > 0)
        {
            parts.Add(adventure.Title);
        }
        parts.Add(world.Chapter.Title);
        if (world.Fighting)
        {
            parts.Add($"Round {Math.Max(1, world.Encounter!.Round)}");
        }
        return string.Join(" · ", parts.Where(p => p.Length > 0));
    }

    // How long ago the game last saved itself.
    private string LastSaved()
    {
        if (Autosave() is not SaveSummary save || save.Written == default)
        {
            return "Not saved yet";
        }
        TimeSpan ago = DateTime.Now - save.Written;
        string when = ago.TotalMinutes < 1 ? "just now" : ago.TotalHours < 1 ? $"{(int)ago.TotalMinutes} min ago"
            : ago.TotalDays < 1 ? $"{(int)ago.TotalHours} h ago" : save.Written.ToString("d MMM, HH:mm");
        return $"Last saved {when}";
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
            _list.Add(new Entry("settings", "Options", "", true, ""));
            _list.Add(new Entry("load", "Load", Count(_saves.Count, "save"), _saves.Count > 0, "There is no save yet."));
            _list.Add(new Entry("quit", "Save and quit", "to main menu", true, ""));
            return;
        }
        // Josh, 10/7: four buttons; what playing can start opens under Play
        // the facts on the four are their keys, as on the design's title
        _list.Add(new Entry("play", "Play", "P", true, ""));
        if (_playOpen)
        {
            SaveSummary? save = Autosave();
            bool loadable = save != null && save.Problem.Length == 0;
            _list.Add(new Entry("continue", "Continue", loadable ? save!.ChapterTitle : "no save", loadable,
                save == null ? "There is no save yet." : "The save can't be read: " + save.Problem, true));
            _list.Add(new Entry("new", "New adventure", "pick one", true, "", true));
            _list.Add(new Entry("quick", "Quick start", "ready-made party", true, "", true));
            _list.Add(new Entry("load", "Load", Count(_saves.Count, "save"), _saves.Count > 0, "There is no save yet.", true));
            _list.Add(new Entry("characters", "Characters", LibraryFact(), true, "", true));
        }
        _list.Add(new Entry("create", "Edit", "E", true, ""));
        _list.Add(new Entry("settings", "Options", "O", true, ""));
        _list.Add(new Entry("exit", "Exit", "Alt F4", true, ""));
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
        _name.Visible = paused || !_logo.Visible;
        _sub.Text = paused ? PausedWhere() : GameScreen.Sizes.Tagline.ToUpperInvariant();
        // the tagline is spaced capitals; where the party is reads as a plain line
        _sub.AddThemeFontOverride("font", paused ? _subPlain : _subSpaced);
        _foot.Text = paused ? LastSaved() : App.Online.Status;
        _online.Text = paused ? "" : $"Version {Rules.Version.Text}";
        if (_picked < 0 || _picked >= _list.Count)
        {
            _picked = _list.FindIndex(e => e.Enabled);
        }

        // the rows are only made again when other entries are listed; a changed fact or label is
        // written into the row there, so a click that lands while the page settles isn't lost
        string signature = Showing + "|" + string.Join("|", _list.Select(e => e.Id));
        if (signature == _listShown)
        {
            for (int i = 0; i < _buttons.Count; i++)
            {
                Entry entry = _list[i];
                _buttons[i].Text = entry.Label;
                GameScreen.SetRight(_buttons[i], entry.Fact);
                GameScreen.SetEnabled(_buttons[i], entry.Enabled);
            }
        }
        else
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
                Button button = entry.Under ? GameScreen.UnderButton(entry.Label, entry.Fact) : GameScreen.BigButton(entry.Label, entry.Fact);
                GameScreen.SetEnabled(button, entry.Enabled);
                button.MouseEntered += () => _picked = index;
                button.Pressed += () => Press(index);
                _entries.AddChild(button);
                _buttons.Add(button);
            }
        }
        for (int i = 0; i < _buttons.Count; i++)
        {
            GameScreen.SetPicked(_buttons[i], i == _picked);
        }

        // the title's card only says where its pictures come from; the buttons say the rest
        string page = Showing == Page.Title ? NoArtPage()
            : _picked >= 0 && _picked < _list.Count ? PageFor(_list[_picked]) : "";
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
    {
        if (index < 0 || index >= _list.Count)
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
            case "play":
                _playOpen = !_playOpen;
                BuildList();
                // opened, the first choice that can be taken is picked: Continue, or New adventure
                _picked = _playOpen ? Math.Max(0, _list.FindIndex(e => e.Under && e.Enabled)) : 0;
                break;
            case "continue": Ordered?.Invoke(MenuOrder.Continue, Places.SaveFile()); break;
            case "new": Open(Page.Adventures); break;
            case "quick": Ordered?.Invoke(MenuOrder.QuickStart, ""); break;
            case "characters": Ordered?.Invoke(MenuOrder.Characters, ""); break;
            case "create": Ordered?.Invoke(MenuOrder.Create, ""); break;
            case "resume": Ordered?.Invoke(MenuOrder.Resume, ""); break;
            case "quit": Ordered?.Invoke(MenuOrder.QuitToTitle, ""); break;
            case "exit": Ordered?.Invoke(MenuOrder.Exit, ""); break;
            case "load": Open(Page.Load); break;
            case "settings": Open(Page.Settings); break;
        }
    }

    private static void Spaced(Label label, int pixels) =>
        label.AddThemeFontOverride("font", new FontVariation { BaseFont = label.GetThemeFont("font"), SpacingGlyph = pixels });

    private string NoArtPage() =>
        new BookPage().Title("No player art yet").Rule()
            .Text($"Pictures from your installed art packs show here and change every {_banner.Seconds:0} seconds. Add packs in Options.")
            .ToString();

    // What the picked entry is, as a book page: the save for Continue, the adventure for a new one.
    private string PageFor(Entry entry)
    {
        var page = new BookPage().Title(entry.Label);
        switch (entry.Id)
        {
            case "play":
                page.Sub("continue, start or load an adventure").Rule()
                    .Text("Continue where the party left off, start a new adventure with your characters, or load an older save.");
                break;
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
                page.Sub("camera, display, game, account and keys").Rule();
                page.Stat("Lighting", SettingsPanel.LightingWords[Math.Clamp(App.Settings.Lighting, 0, 3)]);
                page.Stat("Pan speed", App.Settings.PanSpeed.ToString("0"));
                page.Stat("Keys changed", App.Keys.Overrides().Count.ToString());
                page.Stat("Account", SettingsPanel.AccountWord());
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
