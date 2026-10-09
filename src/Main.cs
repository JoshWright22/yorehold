using Godot;

namespace Yorehold;

/// <summary>
/// The start scene: the title, and under it whichever screen the title started, the game or
/// Create. It owns the flow between them and nothing else. "-- --screen play" (or "--chapter")
/// on the command line goes straight into the game and "--screen create" into Create, which is
/// how screenshot runs skip the title.
/// </summary>
public partial class Main : Node
{
    [Export] public PackedScene? PlayScene { get; set; }
    [Export] public PackedScene? CreateScene { get; set; }

    private MenuScreen _menus = null!; // the scene has it
    private PlayScreen? _play;
    private CreateScreen? _create;

    public override void _Ready()
    {
        GD.Print($"Yorehold {Rules.Version.Text} ready");
        App.Load();
        _menus = GetNode<MenuScreen>("Menus");
        _menus.Ordered += Order;
        _menus.Playing = () => _play?.World;
        // one chat over every screen, so it carries on from the title into the game
        AddChild(new ChatPanel { Name = "Chat" });

        string screen = "title";
        string import = "";
        string[] args = OS.GetCmdlineUserArgs();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i] == "--screen")
            {
                screen = args[i + 1];
            }
            else if (args[i] == "--import")
            {
                // a book read, built and opened in Create with no review, for check runs
                import = args[i + 1];
                screen = "create";
            }
            else if (args[i] == "--chapter" && screen == "title")
            {
                screen = "play";
            }
        }
        switch (screen)
        {
            case "play":
                Play(PlayScreen.StartKind.Quick, "");
                break;
            case "create":
                OpenCreate();
                if (import.Length > 0)
                {
                    _create?.Import(import, build: true);
                }
                break;
            default:
                _menus.Open(MenuScreen.Page.Title);
                break;
        }
    }

    public override void _Process(double delta)
    {
        App.UpdateOnline(delta);
        // the game waits while a menu is over it
        if (_play != null)
        {
            _play.ProcessMode = _menus.IsOpen ? ProcessModeEnum.Disabled : ProcessModeEnum.Inherit;
            _menus.LoadRefusal = _play.LoadRefusal;
        }
        else
        {
            _menus.LoadRefusal = "";
        }
    }

    private void Order(MenuOrder order, string text)
    {
        switch (order)
        {
            case MenuOrder.Continue:
                // a save from an adventure in a package plays from that package again
                Play(PlayScreen.StartKind.Continue, text, Rules.SaveSummary.Read(text).Package);
                break;
            case MenuOrder.NewAdventure:
                // text is the picked adventure's package, "" for the game's own
                Play(PlayScreen.StartKind.Party, "", text);
                break;
            case MenuOrder.QuickStart:
                Play(PlayScreen.StartKind.Quick, "");
                break;
            case MenuOrder.Characters:
                Play(PlayScreen.StartKind.Library, "");
                break;
            case MenuOrder.LoadSave:
                string package = Rules.SaveSummary.Read(text).Package;
                if (_play == null || _play.Package != package)
                {
                    Play(PlayScreen.StartKind.Continue, text, package);
                }
                else if (_play.LoadFrom(text))
                {
                    _menus.Open(MenuScreen.Page.None);
                }
                else
                {
                    _menus.Open(MenuScreen.Page.Pause);
                    _menus.Say("That save couldn't be loaded. The log says why.");
                }
                break;
            case MenuOrder.Create:
                OpenCreate();
                break;
            case MenuOrder.Resume:
                _menus.Open(MenuScreen.Page.None);
                break;
            case MenuOrder.QuitToTitle:
                _play?.SaveNow();
                ToTitle("");
                break;
            case MenuOrder.Exit:
                GetTree().Quit();
                break;
        }
    }

    private void Play(PlayScreen.StartKind kind, string save, string package = "")
    {
        ClosePlay();
        if (PlayScene == null)
        {
            return;
        }
        _play = PlayScene.Instantiate<PlayScreen>();
        _play.Start = kind;
        _play.StartSave = save;
        if (package.Length > 0 && System.IO.Directory.Exists(package))
        {
            // an adventure made in Create or imported: its package over the game's content, from its first chapter
            Rules.ContentFiles files = App.Content();
            files.Add(package);
            _play.Content = files;
            _play.Package = package;
            try
            {
                _play.ChapterFolder = Rules.Adventure.Load(files).ChapterFolders[0];
                // the content sets for the system it plays join under the adventure, which keeps the last word
                _play.Content = App.ContentFor(_play.ChapterFolder, package);
            }
            catch (Rules.ContentException error)
            {
                GD.PushWarning($"The adventure in {package} can't be read: {error.Message}");
            }
        }
        _play.PauseAsked += () => _menus.Open(MenuScreen.Page.Pause);
        // deferred: it is asked from inside the play screen, which this frees
        _play.TitleAsked += () => Callable.From(() => ToTitle("")).CallDeferred();
        AddChild(_play);
        MoveChild(_play, 0); // under the menus, so they draw over it and hear keys first
        _menus.Open(MenuScreen.Page.None);
        if (kind == PlayScreen.StartKind.Continue && _play.LoadProblem.Length > 0)
        {
            // the chapter is there but the save isn't in it: say so on the title and don't strand them in a fresh game
            ToTitle("The save couldn't be loaded: " + _play.LoadProblem);
        }
    }

    private void OpenCreate()
    {
        ClosePlay();
        if (CreateScene == null)
        {
            _menus.Open(MenuScreen.Page.Title);
            _menus.Say("Create isn't in this build yet.");
            return;
        }
        _create = CreateScene.Instantiate<CreateScreen>();
        // deferred: both are asked from inside Create, which they free or hide
        _create.Closed += () => Callable.From(() => ToTitle("")).CallDeferred();
        _create.PlaytestAsked += (files, chapter) => Callable.From(() => Playtest(files, chapter)).CallDeferred();
        AddChild(_create);
        MoveChild(_create, 0);
        _menus.Open(MenuScreen.Page.None);
    }

    // A chapter of the package open in Create, played with nothing saved; Escape goes back to Create.
    private void Playtest(Rules.ContentFiles files, string chapter)
    {
        if (_create == null || PlayScene == null)
        {
            return;
        }
        _create.Visible = false;
        _create.ProcessMode = ProcessModeEnum.Disabled;
        _play = PlayScene.Instantiate<PlayScreen>();
        _play.Content = files;
        _play.ChapterFolder = chapter;
        _play.Playtest = true;
        _play.PauseAsked += () => Callable.From(EndPlaytest).CallDeferred();
        AddChild(_play);
        MoveChild(_play, 0);
    }

    private void EndPlaytest()
    {
        if (_play != null)
        {
            RemoveChild(_play);
            _play.QueueFree();
            _play = null;
        }
        if (_create != null)
        {
            _create.Visible = true;
            _create.ProcessMode = ProcessModeEnum.Inherit;
        }
    }

    private void ToTitle(string notice)
    {
        ClosePlay();
        _menus.Open(MenuScreen.Page.Title);
        _menus.Say(notice);
    }

    private void ClosePlay()
    {
        if (_play != null)
        {
            RemoveChild(_play);
            _play.QueueFree();
            _play = null;
        }
        if (_create != null)
        {
            RemoveChild(_create);
            _create.QueueFree();
            _create = null;
        }
    }
}
