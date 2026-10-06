using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Create, the in-game editor. With nothing open it is a list of the packages on this machine (the
/// ones made here and the game's own) to open or a new one to make. With one open: the modes along
/// the top with the chapter being worked on, the mode in the middle, the problems list on the
/// right, and Undo, Redo, Save, Playtest and Close along the bottom. Ctrl+Z, Ctrl+Y (or
/// Ctrl+Shift+Z) and Ctrl+S work unless a text box has the keys. Every rule is CreatePackage's.
/// </summary>
public partial class CreateScreen : Control
{
    private enum Mode
    {
        Map,
        Encounters,
        Dialogue,
        Compendium,
    }

    private static readonly DataColumn[] Columns = { new("Package", 220), new("Kind", 90), new("Chapters", 70, true) };

    /// <summary>Close was pressed: back to the title.</summary>
    public event Action? Closed;
    /// <summary>Playtest was pressed: play this chapter from these files, then come back.</summary>
    public event Action<ContentFiles, string>? PlaytestAsked;

    private CreatePackage _package = null!;
    private DataPanel _start = null!;
    private Control _editor = null!;
    private MapModePanel _map = null!;
    private EncountersModePanel _encounters = null!;
    private DialogueModePanel _dialogue = null!;
    private Button _mapTab = null!;
    private Button _encountersTab = null!;
    private Button _dialogueTab = null!;
    private CompendiumModePanel _compendium = null!;
    private Button _compendiumTab = null!;
    private Label _name = null!;
    private Button _chapter = null!;
    private RichTextLabel _problems = null!;
    private Button _undo = null!;
    private Button _redo = null!;
    private Button _save = null!;
    private Label _status = null!;
    private Mode _mode = Mode.Map;
    private List<(string Path, ContentPackage? Package, string Problem, bool Game)> _found = new();
    private string _startNote = "";
    private double _problemsAge = 99;
    private string _problemsShown = "";
    private bool _leaveAsked;

    public CreatePackage Package => _package;

    public override void _Ready()
    {
        _package = new CreatePackage(Places.GameContent());
        _start = GetNode<DataPanel>("Start");
        _editor = GetNode<Control>("Editor");
        _map = GetNode<MapModePanel>("Editor/Body/Modes/Map");
        _encounters = GetNode<EncountersModePanel>("Editor/Body/Modes/Encounters");
        _mapTab = GetNode<Button>("Editor/Top/Row/Map");
        _encountersTab = GetNode<Button>("Editor/Top/Row/Encounters");
        _dialogue = GetNode<DialogueModePanel>("Editor/Body/Modes/Dialogue");
        _dialogueTab = GetNode<Button>("Editor/Top/Row/Dialogue");
        _compendium = GetNode<CompendiumModePanel>("Editor/Body/Modes/Compendium");
        _compendiumTab = GetNode<Button>("Editor/Top/Row/Compendium");
        _name = GetNode<Label>("Editor/Top/Row/Name");
        _chapter = GetNode<Button>("Editor/Top/Row/Chapter");
        _problems = GetNode<RichTextLabel>("Editor/Body/Problems/Rows/Text");
        _undo = GetNode<Button>("Editor/Bar/Row/Undo");
        _redo = GetNode<Button>("Editor/Bar/Row/Redo");
        _save = GetNode<Button>("Editor/Bar/Row/Save");
        _status = GetNode<Label>("Editor/Bar/Row/Status");

        _mapTab.Pressed += () => _mode = Mode.Map;
        _encountersTab.Pressed += () => _mode = Mode.Encounters;
        _dialogueTab.Pressed += () => _mode = Mode.Dialogue;
        _compendiumTab.Pressed += () => _mode = Mode.Compendium;
        _chapter.Pressed += NextChapter;
        _undo.Pressed += () => _package.Undo();
        _redo.Pressed += () => _package.Redo();
        _save.Pressed += Save;
        GetNode<Button>("Editor/Bar/Row/Playtest").Pressed += Playtest;
        GetNode<Button>("Editor/Bar/Row/Close").Pressed += Leave;
        _start.ActionPressed += StartAction;
        _start.ClosePressed += () => Closed?.Invoke();
        FindPackages();
    }

    /// <summary>Opens a package folder, or says on the list why it can't.</summary>
    public void Open(string path)
    {
        _package.Open(path);
        Opened();
    }

    public override void _Process(double delta)
    {
        bool open = _package.IsOpen;
        _start.Visible = !open;
        _editor.Visible = open;
        if (!open)
        {
            FillStart();
            return;
        }

        _mapTab.SetPressedNoSignal(_mode == Mode.Map);
        _encountersTab.SetPressedNoSignal(_mode == Mode.Encounters);
        _dialogueTab.SetPressedNoSignal(_mode == Mode.Dialogue);
        _compendiumTab.SetPressedNoSignal(_mode == Mode.Compendium);
        _compendium.Visible = _mode == Mode.Compendium;
        _map.Visible = _mode == Mode.Map;
        _encounters.Visible = _mode == Mode.Encounters;
        _dialogue.Visible = _mode == Mode.Dialogue;
        _name.Text = _package.Manifest!.Name + (_package.IsGameContent ? "   the game's own content" : "");
        _chapter.Text = "Chapter: " + (_package.Chapter.Length == 0 ? "none" : CreatePackage.Leaf(_package.Chapter));
        _chapter.Disabled = _package.Chapters.Count < 2;
        if (_mode == Mode.Map)
        {
            _map.Present(_package.MapEditor(), _package.MapError);
        }
        else if (_mode == Mode.Dialogue)
        {
            _dialogue.Present(_package);
        }
        else if (_mode == Mode.Compendium)
        {
            _compendium.Present(_package);
        }
        else
        {
            EncountersEditor? encounters = _package.EncountersEditor();
            _encounters.Present(encounters, _package.MapEditor()?.Map(), _package.EncountersError);
        }

        History history = _package.History;
        _undo.Disabled = !history.CanUndo;
        _redo.Disabled = !history.CanRedo;
        _undo.TooltipText = history.UndoLabel;
        _redo.TooltipText = history.RedoLabel;
        _save.Text = history.Dirty ? "Save *" : "Save";
        // what the last save or undo did, or what the next undo would take back
        _status.Text = _leaveAsked ? "Unsaved changes. Close again to leave without saving."
            : _package.Status.Length > 0 && _package.StatusCurrent ? _package.Status
            : history.CanUndo ? "Last: " + history.UndoLabel
            : _package.PackagePath;

        _problemsAge += delta;
        if (_problemsAge >= 1)
        {
            _problemsAge = 0;
            ShowProblems();
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        if (!_package.IsOpen)
        {
            if (key.Keycode == Key.Escape)
            {
                Closed?.Invoke();
                GetViewport().SetInputAsHandled();
            }
            else if (key.Keycode is Key.Enter or Key.KpEnter && _start.Picked.Length > 0)
            {
                Open(_start.Picked);
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        bool control = key.CtrlPressed || Input.IsKeyPressed(Key.Ctrl);
        bool shift = key.ShiftPressed || Input.IsKeyPressed(Key.Shift);
        bool handled = true;
        if (control && key.Keycode == Key.Z)
        {
            if (shift)
            {
                _package.Redo();
            }
            else
            {
                _package.Undo();
            }
        }
        else if (control && key.Keycode == Key.Y)
        {
            _package.Redo();
        }
        else if (control && key.Keycode == Key.S)
        {
            Save();
        }
        else if (key.Keycode == Key.Escape)
        {
            Leave();
        }
        else
        {
            handled = false;
        }
        if (handled)
        {
            GetViewport().SetInputAsHandled();
        }
    }

    private void Opened()
    {
        _leaveAsked = false;
        _problemsAge = 99;
        if (_package.IsOpen)
        {
            _mode = Mode.Map;
            if (App.Settings.LastCreatePackage != _package.PackagePath)
            {
                App.Settings.LastCreatePackage = _package.PackagePath;
                App.Save();
            }
        }
        else
        {
            _startNote = _package.Status;
            FindPackages();
        }
    }

    private void Save()
    {
        _leaveAsked = false;
        _package.Save();
        _problemsAge = 99;
    }

    private void Playtest()
    {
        if (_package.Chapter.Length == 0)
        {
            return;
        }
        // the playtest reads the files, so what is on screen is saved first
        if (_package.History.Dirty && !_package.Save())
        {
            return;
        }
        PlaytestAsked?.Invoke(_package.PlayFiles(), _package.Chapter);
    }

    // Unsaved work needs Close pressed twice.
    private void Leave()
    {
        if (_package.History.Dirty && !_leaveAsked)
        {
            _leaveAsked = true;
            return;
        }
        _package.Close();
        _startNote = "";
        _leaveAsked = false;
        FindPackages();
    }

    private void NextChapter()
    {
        IReadOnlyList<string> chapters = _package.Chapters;
        if (chapters.Count < 2)
        {
            return;
        }
        int at = chapters.ToList().IndexOf(_package.Chapter);
        _package.SelectChapter(chapters[(at + 1) % chapters.Count]);
    }

    private void ShowProblems()
    {
        List<CreateProblem> problems = _package.Problems();
        var text = new StringBuilder();
        if (problems.Count == 0)
        {
            text.Append($"[color={Palette.Hex(Palette.Leaf)}]Nothing wrong.[/color]");
        }
        foreach (CreateProblem problem in problems)
        {
            Color color = problem.Error ? Palette.Red : Palette.Amber;
            text.Append($"[color={Palette.Hex(color)}]{(problem.Error ? "Error" : "Look")}[/color]  {BookPage.Escape(problem.Message)}\n");
        }
        string shown = text.ToString();
        if (shown != _problemsShown)
        {
            _problemsShown = shown;
            _problems.Text = shown;
        }
    }

    // ---------------------------------------------------------------- the list of packages

    private void FindPackages()
    {
        var found = new List<(string, ContentPackage?, string, bool)>();
        void Add(string path, bool game)
        {
            path = path.Replace('\\', '/');
            if (found.Any(f => string.Equals(Path.GetFullPath(f.Item1), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            try
            {
                found.Add((path, ContentPackage.Load(new ContentFiles(path)), "", game));
            }
            catch (ContentException problem)
            {
                found.Add((path, null, problem.Message, game));
            }
        }
        string root = Places.CreateFolder();
        if (Directory.Exists(root))
        {
            foreach (string folder in Directory.GetDirectories(root).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (File.Exists(Path.Combine(folder, "content.json")))
                {
                    Add(folder, false);
                }
            }
        }
        string last = App.Settings.LastCreatePackage;
        if (last.Length > 0 && File.Exists(Path.Combine(last, "content.json")))
        {
            Add(last, false);
        }
        Add(Places.GameContent(), true);
        _found = found;
    }

    private void FillStart()
    {
        _start.SetHead("Create", "maps, encounters and conversations for your own adventures");
        _start.SetSources(Array.Empty<(string, string)>(), "");
        _start.SetTabs(new[] { "All", "Made here", "The game's" });
        _start.SetChips(Array.Empty<string>());
        _start.SetColumns(Columns);
        var rows = new List<DataRow>();
        foreach ((string path, ContentPackage? package, string problem, bool game) in _found)
        {
            string name = package == null ? Path.GetFileName(path.TrimEnd('/')) : package.Name.Length > 0 ? package.Name : Path.GetFileName(path.TrimEnd('/'));
            rows.Add(new DataRow
            {
                Key = path,
                Cells = new[] { name, package == null ? "can't be read" : package.Kind, package?.Chapters.Count.ToString() ?? "" },
                Sort = new IComparable?[] { name, null, package?.Chapters.Count ?? -1 },
                Tags = new HashSet<string> { game ? "The game's" : "Made here" },
                Search = path,
                Dim = package == null,
            });
        }
        _start.SetRows(rows);

        var page = new BookPage();
        var actions = new List<DataAction>();
        (string Path, ContentPackage? Package, string Problem, bool Game) picked = _found.FirstOrDefault(f => f.Path == _start.Picked);
        if (picked.Path == null)
        {
            page.Title("Nothing made yet").Rule().Text("New adventure makes a folder with one chapter, one hero and an empty map to draw on.");
        }
        else if (picked.Package == null)
        {
            page.Title(Path.GetFileName(picked.Path.TrimEnd('/'))).Sub("can't be opened").Rule().Warn(picked.Problem).Gap().Note(picked.Path);
            actions.Add(new DataAction("open", "Open", false, "Its content.json can't be read: " + picked.Problem));
        }
        else
        {
            ContentPackage package = picked.Package;
            page.Title(package.Name.Length > 0 ? package.Name : package.Id).Sub(picked.Game ? "the game's own content" : (package.Kind.Length > 0 ? package.Kind : "package")).Rule();
            page.Stats(("Id", package.Id.Length > 0 ? package.Id : "none"), ("Revision", package.Revision.ToString()), ("Chapters", package.Chapters.Count.ToString()));
            if (package.Chapters.Count > 0)
            {
                page.Heading("Chapters");
                foreach (string chapter in package.Chapters)
                {
                    page.Entry(CreatePackage.Leaf(chapter), chapter == package.DefaultChapter ? "where play starts" : chapter);
                }
            }
            page.Gap().Note(picked.Path);
            if (picked.Game)
            {
                page.Gap().Warn("Saving writes into the game's own files.");
            }
            actions.Add(new DataAction("open", "Open"));
        }
        actions.Add(new DataAction("new", "New adventure"));
        actions.Add(new DataAction("back", "Back to title"));
        _start.SetEntry(page.ToString(), actions, _startNote);
        _start.SetFoot(Places.CreateFolder());
    }

    private void StartAction(string id)
    {
        switch (id)
        {
            case "open":
                Open(_start.Picked);
                break;
            case "new":
                _package.New(Places.CreateFolder());
                Opened();
                break;
            case "back":
                Closed?.Invoke();
                break;
        }
    }
}
