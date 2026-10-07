using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>One line of the problems list: the file it is about, what is wrong, and whether it stops play.</summary>
public sealed record CreateProblem(string Path, string Message, bool Error);

/// <summary>
/// The package open in Create: opening or making one, the chapter being worked on, its Map and
/// Encounters editors, one undo history for all of it, saving and the problems list. It draws
/// nothing; the Create screen is a layout over it. The game's own content sits under the package,
/// as when it is played, so its kits, creatures, AI profiles and items can be placed.
/// </summary>
public sealed partial class CreatePackage
{
    // One file a save writes: where, what, and what marks it saved once written.
    private sealed record Changed(string Path, string Text, Action Done);

    // One chapter's map and what it was when last read or written.
    private sealed class MapTab
    {
        public MapTab(History history) => Editor = new MapEditor(history);

        public MapEditor Editor { get; }
        /// <summary>Inside the package: "chapters/keep/map.json".</summary>
        public string Path = "";
        /// <summary>The JSON as last read or written, in the editor's form.</summary>
        public string Saved = "";
    }

    private sealed class EncountersTab
    {
        public EncountersTab(History history) => Editor = new EncountersEditor(history);

        public EncountersEditor Editor { get; }
        public string Path = "";
        public string Saved = "";
    }

    private readonly string _gameAssets;
    // its undo steps point into the editors below: empty it before dropping any of them
    private readonly History _history = new();
    private readonly SortedDictionary<string, MapTab> _maps = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> _mapErrors = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, EncountersTab> _encounters = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> _encounterErrors = new(StringComparer.Ordinal);
    private List<CreateProblem> _onDisk = new();

    /// <summary>gameAssets is the game's own content folder, laid under every package.</summary>
    public CreatePackage(string gameAssets)
    {
        _gameAssets = gameAssets;
    }

    /// <summary>Players' art packs, laid between the game's content and the package in a playtest.</summary>
    public List<string> ArtFolders { get; } = new();

    public ContentPackage? Manifest { get; private set; }
    public bool IsOpen => Manifest != null;
    /// <summary>The folder that is open; empty when nothing is.</summary>
    public string PackagePath { get; private set; } = "";
    /// <summary>The chapter folder whose map and encounters are being edited ("chapters/keep").</summary>
    public string Chapter { get; private set; } = "";
    public IReadOnlyList<string> Chapters => Manifest?.Chapters ?? new List<string>();
    /// <summary>One history for the whole package: every mode's edits go on it.</summary>
    public History History => _history;
    /// <summary>The last open, save, undo or redo, in a few words.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            _status = value;
            _statusAt = _history.Changes;
        }
    }

    /// <summary>Status still says what was done last: nothing was edited, undone or redone since.</summary>
    public bool StatusCurrent => _statusAt == _history.Changes;

    private string _status = "";
    private int _statusAt;
    /// <summary>True when the package is the game's own content folder.</summary>
    public bool IsGameContent => IsOpen && SameFolder(PackagePath, _gameAssets);

    /// <summary>"chapters/goblin-keep" is "goblin-keep".</summary>
    public static string Leaf(string folder)
    {
        int slash = folder.LastIndexOf('/');
        return slash < 0 ? folder : folder[(slash + 1)..];
    }

    /// <summary>Opens a package folder. Afterwards IsOpen says whether it worked and Status why not.</summary>
    public void Open(string path)
    {
        Close();
        PackagePath = path.Replace('\\', '/');
        if (path.Length == 0 || !Directory.Exists(path))
        {
            Status = $"Couldn't open {path}: there is no folder there";
            return;
        }
        if (!File.Exists(System.IO.Path.Combine(path, "content.json")))
        {
            Status = $"Couldn't open {path}: it has no content.json";
            return;
        }
        try
        {
            Manifest = ContentPackage.Load(new ContentFiles(path));
        }
        catch (ContentException problem)
        {
            Status = problem.Message;
            return;
        }
        Chapter = Manifest.DefaultChapter.Length > 0 ? Manifest.DefaultChapter : Manifest.Chapters.FirstOrDefault() ?? "";
        Status = $"Opened {Manifest.Name}";
        CheckFiles();
    }

    /// <summary>Drops the open package and everything about it, unsaved edits too.</summary>
    public void Close()
    {
        CloseVoice();
        _history.Clear();
        // what is opened next starts saved, whatever the last package was left as
        _history.MarkSaved();
        CloseDialogues();
        CloseCompendium();
        CloseCutscenes();
        CloseStory();
        _encounters.Clear();
        _encounterErrors.Clear();
        _maps.Clear();
        _mapErrors.Clear();
        _onDisk = new List<CreateProblem>();
        Manifest = null;
        PackagePath = "";
        Chapter = "";
        Status = "";
    }

    /// <summary>
    /// Writes a new adventure (a manifest, one chapter with one hero and an empty map) into a
    /// folder of its own under root and opens it. False, with Status saying why, when it can't.
    /// The chapter folder is named after the package, so it never stands in for one of the
    /// game's own chapters when the package is played over them.
    /// </summary>
    public bool New(string root)
    {
        string id = "new-adventure";
        for (int n = 2; Directory.Exists(System.IO.Path.Combine(root, id)); n++)
        {
            id = $"new-adventure-{n}";
        }
        string folder = System.IO.Path.Combine(root, id);
        string chapter = "chapters/" + id;
        var manifest = new JsonObject
        {
            ["format"] = "yorehold.content",
            ["version"] = 1,
            ["name"] = "New adventure",
            ["kind"] = "adventure",
            ["id"] = id,
            ["revision"] = 0,
            ["requires"] = new JsonArray(),
            ["defaultChapter"] = chapter,
            ["chapters"] = new JsonArray(chapter),
        };
        var chapterFile = new JsonObject
        {
            ["id"] = id,
            ["title"] = "Chapter one",
            ["map"] = "map.json",
            ["level"] = 1,
            ["party"] = new JsonArray(new JsonObject { ["name"] = "Hero", ["class"] = "fighter", ["color"] = new JsonArray(180, 82, 82), ["at"] = new JsonArray(2, 2) }),
            ["encounters"] = new JsonArray(),
        };
        // BlankMap writes an object
        JsonObject map = JsonNode.Parse(Yorehold.Rules.MapEditor.BlankMap("Chapter one", 24, 16))!.AsObject();
        map["markers"]!["partyStart"] = new JsonArray(2, 2);
        try
        {
            WriteFile(System.IO.Path.Combine(folder, "content.json"), CreateJson.Write(manifest));
            WriteFile(System.IO.Path.Combine(folder, chapter, "chapter.json"), CreateJson.Write(chapterFile));
            WriteFile(System.IO.Path.Combine(folder, chapter, "map.json"), CreateJson.Write(map));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Status = $"Couldn't make {folder}: {error.Message}";
            return false;
        }
        Open(folder);
        return IsOpen;
    }

    public void SelectChapter(string folder)
    {
        if (Manifest != null && Manifest.Chapters.Contains(folder))
        {
            Chapter = folder;
        }
    }

    /// <summary>The chapter's map, read the first time it is asked for. Null if it can't be, and MapError says why.</summary>
    public MapEditor? MapEditor() => MapTabOf(Chapter)?.Editor;

    public string MapError => _mapErrors.GetValueOrDefault(Chapter, "");

    /// <summary>The chapter's encounters, the same way. They stand on its map, so they need it too.</summary>
    public EncountersEditor? EncountersEditor() => EncountersTabOf(Chapter)?.Editor;

    public string EncountersError => _encounterErrors.GetValueOrDefault(Chapter, MapError);

    /// <summary>The game's content with the package on top, the way a playtest reads it.</summary>
    public ContentFiles PlayFiles()
    {
        var files = new ContentFiles(_gameAssets);
        foreach (string pack in ArtFolders)
        {
            files.Add(pack);
        }
        if (IsOpen && !IsGameContent)
        {
            files.Add(PackagePath);
        }
        return files;
    }

    public void Undo()
    {
        string label = _history.UndoLabel;
        if (_history.Undo())
        {
            Status = "Undid: " + label;
        }
    }

    public void Redo()
    {
        string label = _history.RedoLabel;
        if (_history.Redo())
        {
            Status = "Redid: " + label;
        }
    }

    /// <summary>
    /// Writes every changed map and chapter back to its file. Everything is checked before anything
    /// is written, so a refused file doesn't leave the others saved around it, and a map or a
    /// chapter the game would refuse is never written. False with Status saying why.
    /// </summary>
    public bool Save()
    {
        if (Manifest == null)
        {
            return false;
        }
        var changed = new List<Changed>();
        foreach ((string chapter, MapTab tab) in _maps)
        {
            string text = tab.Editor.ToJson();
            if (text == tab.Saved)
            {
                continue;
            }
            try
            {
                GameMap.Read(ContentNode.Parse(tab.Path, text), new Dictionary<string, Kit>());
            }
            catch (ContentException problem)
            {
                Status = $"{tab.Path} not saved: {problem.Message}";
                return false;
            }
            changed.Add(new Changed(tab.Path, text, () => tab.Saved = text));
        }
        // a chapter.json is Encounters mode's groups with Cutscene mode's triggers and endings on top
        foreach (string chapter in _encounters.Keys.Union(_hooks.Keys).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList())
        {
            EncountersTab? tab = _encounters.GetValueOrDefault(chapter);
            CutsceneHooks? hooks = _hooks.GetValueOrDefault(chapter);
            string own = tab?.Editor.ToJson() ?? "";
            bool groupsChanged = tab != null && own != tab.Saved;
            if (tab != null)
            {
                bool mapChanged = _maps.TryGetValue(chapter, out MapTab? map) && map.Editor.ToJson() != map.Saved;
                // a wall painted over a creature counts too, though only the map changed
                if ((groupsChanged || mapChanged) && tab.Editor.Problems().FirstOrDefault(p => p.Error) is Yorehold.Rules.EncountersEditor.Problem wrong)
                {
                    Status = $"{Leaf(chapter)} not saved: {wrong.Text}";
                    return false;
                }
            }
            bool hooksChanged = hooks?.Changed == true;
            if (!groupsChanged && !hooksChanged)
            {
                continue;
            }
            if (hooks?.Problems().FirstOrDefault(p => p.Error) is CutsceneHooks.Problem broken)
            {
                Status = $"{Leaf(chapter)} not saved: {broken.Text}";
                return false;
            }
            string text = own;
            if (tab == null)
            {
                var package = new ContentFiles(PackagePath);
                if (!package.Exists(chapter + "/chapter.json"))
                {
                    Status = chapter + "/chapter.json can't be read";
                    return false;
                }
                text = package.ReadText(chapter + "/chapter.json");
            }
            if (hooks != null)
            {
                text = hooks.ApplyTo(text);
            }
            changed.Add(new Changed(chapter + "/chapter.json", text, () =>
            {
                if (tab != null)
                {
                    tab.Saved = own;
                }
                hooks?.MarkSaved();
            }));
        }
        if (!CutscenesToSave(changed) || !DialoguesToSave(changed) || !StoryToSave(changed) || !CompendiumToSave(changed))
        {
            return false;
        }
        VoicesToSave(changed);
        foreach ((string path, string text, Action done) in changed)
        {
            try
            {
                WriteFile(System.IO.Path.Combine(PackagePath, path), text);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Status = $"{path} not saved: {error.Message}";
                return false;
            }
            done();
        }
        _history.MarkSaved();
        // saved groups, conversations and endings change what the story graph can point at
        StaleStory();
        Status = changed.Count == 0 ? "Nothing to save" : changed.Count == 1 ? "Saved 1 file" : $"Saved {changed.Count} files";
        if (changed.Count > 0)
        {
            CheckFiles();
        }
        return true;
    }

    /// <summary>
    /// What is wrong or worth a look: the editors as they are now (saved or not), then the
    /// package's files as they are on disk, played over the game's content, as of the last open or save.
    /// </summary>
    public List<CreateProblem> Problems()
    {
        var found = new List<CreateProblem>();
        if (Manifest == null)
        {
            return found;
        }
        if (Manifest.Name.Length == 0)
        {
            found.Add(new CreateProblem("content.json", "The package has no name", false));
        }
        foreach ((string chapter, string why) in _mapErrors)
        {
            found.Add(new CreateProblem(chapter, why, true));
        }
        foreach ((string chapter, MapTab tab) in _maps)
        {
            found.AddRange(tab.Editor.Problems().Select(line => new CreateProblem(tab.Path, $"{Leaf(chapter)}: {line}", false)));
        }
        foreach ((string chapter, string why) in _encounterErrors)
        {
            found.Add(new CreateProblem(chapter, why, true));
        }
        foreach ((string chapter, EncountersTab tab) in _encounters)
        {
            found.AddRange(tab.Editor.Problems().Select(p => new CreateProblem(tab.Path, $"{Leaf(chapter)}: {p.Text}", p.Error)));
        }
        DialogueProblems(found);
        VoiceProblems(found);
        CutsceneProblems(found);
        StoryProblems(found);
        CompendiumProblems(found);
        found.AddRange(_onDisk);
        return found;
    }

    /// <summary>Reads the package's files again as the game would play them, for Problems.</summary>
    public void CheckFiles()
    {
        _onDisk = new List<CreateProblem>();
        if (Manifest == null)
        {
            return;
        }
        try
        {
            Manifest.Validate(PlayFiles(), new ContentFiles(PackagePath));
        }
        catch (ContentException problem)
        {
            _onDisk.Add(new CreateProblem(PackagePath, "On disk: " + problem.Message, true));
        }
    }

    private MapTab? MapTabOf(string chapter)
    {
        if (Manifest == null || chapter.Length == 0)
        {
            return null;
        }
        if (_maps.TryGetValue(chapter, out MapTab? found))
        {
            return found;
        }
        if (_mapErrors.ContainsKey(chapter))
        {
            return null;
        }
        MapTab? Fail(string why)
        {
            _mapErrors[chapter] = why;
            return null;
        }

        // The game's own kits can be placed as well as the package's: the editor writes whole
        // objects into map.json, so the saved map doesn't need the kit files.
        var kits = new SortedDictionary<string, Kit>(StringComparer.Ordinal);
        ContentFiles package = new(PackagePath);
        ReadKits(new ContentFiles(_gameAssets), "kits", kits);
        ReadKits(package, "kits", kits);
        ReadKits(package, chapter + "/kits", kits);

        // chapter.json names the map; looked for in the chapter folder first, then at the root, as the game does
        string name = "map.json";
        try
        {
            if (package.Exists(chapter + "/chapter.json") && ContentNode.Read(package, chapter + "/chapter.json") is { IsObject: true } j)
            {
                name = j.Text("map", "map.json");
            }
        }
        catch (ContentException)
        {
            // a chapter.json that doesn't parse leaves the map at its usual name; the encounters say what is wrong with it
        }
        if (!ContentFiles.IsContentPath(name))
        {
            return Fail($"{chapter}/chapter.json: the map path has to stay inside the package");
        }
        string path = chapter + "/" + name;
        if (!package.Exists(path) && package.Exists(name))
        {
            path = name;
        }
        if (!package.Exists(path))
        {
            return Fail($"{path} is missing");
        }
        var tab = new MapTab(_history) { Path = path };
        if (!tab.Editor.Load(package.ReadText(path), out string error, kits))
        {
            return Fail($"{path}: {error}");
        }
        // compared in the editor's own form, so a hand-written map isn't rewritten until it is changed
        tab.Saved = tab.Editor.ToJson();
        _maps[chapter] = tab;
        return tab;
    }

    private EncountersTab? EncountersTabOf(string chapter)
    {
        if (Manifest == null || chapter.Length == 0)
        {
            return null;
        }
        if (_encounters.TryGetValue(chapter, out EncountersTab? found))
        {
            return found;
        }
        if (_encounterErrors.ContainsKey(chapter) || MapTabOf(chapter) is not MapTab map)
        {
            return null; // the map's own error says why
        }
        var package = new ContentFiles(PackagePath);
        string path = chapter + "/chapter.json";
        if (!package.Exists(path))
        {
            _encounterErrors[chapter] = $"{path} is missing";
            return null;
        }

        // What can be placed: the game's own creatures, AI profiles and items under the package's
        // and the chapter's, which is what a played package sees. A file that doesn't load stops
        // the reading where it is; the problems list says which.
        var compendium = new Compendium();
        ContentFiles all = PlayFiles();
        try
        {
            compendium.Load(all, "");
            compendium.Load(all, chapter);
        }
        catch (ContentException)
        {
        }

        var tab = new EncountersTab(_history) { Path = path };
        // the map as it is being drawn, so a wall painted a moment ago already counts
        MapEditor walls = map.Editor;
        if (!tab.Editor.Load(package.ReadText(path), out string error, Yorehold.Rules.EncountersEditor.Catalog.From(compendium), cell => walls.Map().Walkable(cell)))
        {
            _encounterErrors[chapter] = $"{path}: {error}";
            return null;
        }
        tab.Saved = tab.Editor.ToJson();
        _encounters[chapter] = tab;
        return tab;
    }

    // Kit files in a folder, by id. A broken one is left out: the package's own checks report it.
    private static void ReadKits(ContentFiles files, string folder, SortedDictionary<string, Kit> kits)
    {
        foreach (string path in files.List(folder))
        {
            try
            {
                kits[ContentFiles.Stem(path)] = Kit.Read(ContentNode.Read(files, path));
            }
            catch (ContentException)
            {
            }
        }
    }

    // Written next to the file and moved over it, so a failed write never leaves half a file.
    private static void WriteFile(string path, string text)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); // every path here has a folder part
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, text);
        File.Move(temporary, path, true);
    }

    private static bool SameFolder(string a, string b)
    {
        static string Clean(string p) => System.IO.Path.GetFullPath(p).TrimEnd('/', '\\');
        return a.Length > 0 && b.Length > 0 && string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);
    }
}
