namespace Yorehold.Rules;

/// <summary>
/// System mode's part of the open package: the ruleset.json its chapters play by, and the bench
/// that plays it. A package that has no ruleset.json of its own starts from the game's one of that
/// system; saving then writes the package's own over it, so the rest of the system (its classes,
/// creatures and spells) still comes from the game.
/// </summary>
public sealed partial class CreatePackage
{
    private SystemEditor? _system;
    private string _systemError = "";

    public string SystemError => _systemError;

    /// <summary>The ruleset folder the package's first chapter plays by ("rulesets/dnd5e").</summary>
    public string SystemFolder
    {
        get
        {
            if (Manifest == null)
            {
                return RulesFolder.Default;
            }
            var files = new ContentFiles(PackagePath);
            foreach (string chapter in Manifest.Chapters)
            {
                if (ReadObject(files, chapter + "/chapter.json")?["ruleset"] is { } named && FormJson.IsString(named, out string? ruleset) && ContentFiles.IsContentPath(ruleset))
                {
                    return ruleset;
                }
            }
            return RulesFolder.Default;
        }
    }

    /// <summary>True once the package has a ruleset.json of its own on disk.</summary>
    public bool HasOwnSystem => IsOpen && new ContentFiles(PackagePath).Exists(SystemFolder + "/ruleset.json");

    /// <summary>The system's ruleset.json, read the first time it is asked for. Null if it can't be, and SystemError says why.</summary>
    public SystemEditor? SystemEditor()
    {
        if (Manifest == null)
        {
            return null;
        }
        if (_system != null || _systemError.Length > 0)
        {
            return _system;
        }
        var game = new ContentFiles(_gameAssets);
        var editor = new SystemEditor(_history);
        if (!game.Exists("create/system.json"))
        {
            _systemError = "create/system.json: missing";
            return null;
        }
        if (!editor.SetSections(game.ReadText("create/system.json"), out string error))
        {
            _systemError = "create/system.json: " + error;
            return null;
        }
        string path = SystemFolder + "/ruleset.json";
        ContentFiles from = HasOwnSystem ? new ContentFiles(PackagePath) : game;
        if (!from.Exists(path))
        {
            _systemError = path + ": there is no such system";
            return null;
        }
        if (!editor.Open(path, from.ReadText(path), out error))
        {
            _systemError = error;
            return null;
        }
        // the game's file when the package has none: its first change saves the package's own
        _system = editor;
        return _system;
    }

    /// <summary>Another of the game's systems, taken whole to change from. False with Status saying why.</summary>
    public bool CopySystem(string folder)
    {
        var game = new ContentFiles(_gameAssets);
        string path = folder + "/ruleset.json";
        if (SystemEditor() is not SystemEditor editor || !game.Exists(path))
        {
            Status = $"There is no system at {folder}.";
            return false;
        }
        if (!editor.CopyOf(game.ReadText(path), out string error))
        {
            Status = $"{path} can't be copied: {error}";
            return false;
        }
        Status = $"Copied {path}";
        return true;
    }

    /// <summary>The classes and creatures the bench can pick from: the system's and the package's own.</summary>
    public (List<string> Classes, List<string> Creatures) BenchChoices()
    {
        ContentFiles files = PlayFiles();
        string system = SystemFolder;
        List<string> Ids(string kind) => files.List(system + "/" + kind).Concat(files.List(kind))
            .Select(ContentFiles.Stem).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
        return (Ids("classes"), Ids("creatures"));
    }

    /// <summary>
    /// The bench's duel in the system as it is on screen, saved or not: the ruleset is written to
    /// scratch, a folder of the bench's own laid over the package. Safe to run off the main thread;
    /// it reads only what it is given here.
    /// </summary>
    public Func<FightForecast>? BenchDuel(string heroClass, int level, IReadOnlyList<string> creatures, int fights, string scratch)
    {
        if (SystemEditor() is not SystemEditor editor)
        {
            return null;
        }
        string system = SystemFolder;
        string text = editor.ToJson();
        var layers = new List<string> { _gameAssets };
        layers.AddRange(ArtFolders);
        if (!IsGameContent)
        {
            layers.Add(PackagePath);
        }
        return () =>
        {
            string file = System.IO.Path.Combine(scratch, system.Replace('/', System.IO.Path.DirectorySeparatorChar), "ruleset.json");
            WriteFile(file, text);
            return SystemBench.Duel(() =>
            {
                var files = new ContentFiles(layers[0]);
                foreach (string layer in layers.Skip(1))
                {
                    files.Add(layer);
                }
                return files;
            }, system, heroClass, level, creatures, fights, scratch);
        };
    }

    private void CloseSystem()
    {
        _system = null;
        _systemError = "";
    }

    private bool SystemToSave(List<Changed> changed)
    {
        if (_system == null || !_system.Changed)
        {
            return true;
        }
        SystemEditor editor = _system;
        changed.Add(new Changed(editor.Path, editor.ToJson(), editor.MarkSaved));
        return true;
    }
}
