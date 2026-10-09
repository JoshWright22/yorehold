using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>Compendium mode's part of the open package: one editor over every definition file, read the first time it is asked for.</summary>
public sealed partial class CreatePackage
{
    private CompendiumEditor? _compendium;
    private string _compendiumError = "";
    private readonly List<string> _compendiumSkipped = new();
    private readonly Dictionary<string, string> _compendiumFolders = new(StringComparer.Ordinal);

    /// <summary>Where a new entry of each kind goes; a kind without one can't be added to.</summary>
    public IReadOnlyDictionary<string, string> CompendiumFolders => _compendiumFolders;

    public string CompendiumError => _compendiumError;

    /// <summary>
    /// The package's definitions: the root's and the chapters' own folders, and the ruleset folders
    /// (the game's own place for one and any a chapter names). Null if the forms file can't be read.
    /// </summary>
    public CompendiumEditor? CompendiumEditor()
    {
        if (Manifest == null)
        {
            return null;
        }
        if (_compendium != null)
        {
            return _compendium;
        }
        if (_compendiumError.Length > 0)
        {
            return null;
        }
        var game = new ContentFiles(_gameAssets);
        var files = new ContentFiles(PackagePath);
        var editor = new CompendiumEditor(_history);
        if (!game.Exists("create/compendium.json"))
        {
            _compendiumError = "create/compendium.json: missing";
            return null;
        }
        if (!editor.SetKinds(game.ReadText("create/compendium.json"), out string error))
        {
            _compendiumError = "create/compendium.json: " + error;
            return null;
        }

        // ruleset folders: the game's own place for one, and any a chapter names that has a ruleset.json
        var rulesets = new List<string>();
        void AddRuleset(string folder)
        {
            if (files.Exists(folder + "/ruleset.json") && !rulesets.Contains(folder))
            {
                rulesets.Add(folder);
            }
        }
        AddRuleset(RulesFolder.Default);
        foreach (string chapter in Manifest.Chapters)
        {
            if (ReadObject(files, chapter + "/chapter.json")?["ruleset"] is { } named && FormJson.IsString(named, out string? ruleset) && ContentFiles.IsContentPath(ruleset))
            {
                AddRuleset(chapter + "/" + ruleset);
                AddRuleset(ruleset);
            }
        }

        // the files, from the package root, the chapters' own folders and the ruleset folders
        _compendiumSkipped.Clear();
        _compendiumFolders.Clear();
        foreach (CompendiumEditor.Kind kind in editor.Kinds)
        {
            var folders = new List<string>();
            if (kind.Ruleset)
            {
                folders.AddRange(rulesets.Select(r => r + "/" + kind.Form.Folder));
                if (rulesets.Count > 0)
                {
                    _compendiumFolders[kind.Form.Id] = rulesets[0] + "/" + kind.Form.Folder;
                }
            }
            else
            {
                // a system's own classes, items and creatures, then the package's and its chapters'
                folders.AddRange(rulesets.Select(r => r + "/" + kind.Form.Folder));
                folders.Add(kind.Form.Folder);
                folders.AddRange(Manifest.Chapters.Select(c => c + "/" + kind.Form.Folder));
                _compendiumFolders[kind.Form.Id] = kind.Form.Folder;
            }
            foreach (string path in folders.SelectMany(files.List))
            {
                if (!editor.AddFile(kind.Form.Id, path, files.ReadText(path), out error))
                {
                    _compendiumSkipped.Add(error);
                }
            }
        }

        // what the forms offer: the ruleset's abilities and skills, the built-in AI profiles and the
        // game's own ids of each kind (the package's are added by the editor)
        var options = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        Ruleset? rules = null;
        foreach (ContentFiles where in new[] { files, game })
        {
            string path = (where == files && rulesets.Count > 0 ? rulesets[0] : RulesFolder.Default) + "/ruleset.json";
            if (rules == null && where.Exists(path))
            {
                try
                {
                    rules = Ruleset.Read(ContentNode.Read(where, path));
                }
                catch (ContentException)
                {
                }
            }
        }
        if (rules != null)
        {
            options["abilities"] = rules.Abilities.Select(a => a.Id).ToList();
            options["skills"] = rules.Skills.Select(s => s.Id).ToList();
            // what a feat's kind can be: the system's own
            options["featKinds"] = rules.FeatKinds.Select(k => k.Id).ToList();
            // what an option's kind can be: the system's own (a heritage, a subclass)
            options["optionKinds"] = rules.OptionKinds.Select(k => k.Id).ToList();
        }
        options.TryAdd("optionKinds", new List<string>());
        options.TryAdd("featKinds", FeatDefinition.Kinds.ToList());
        // what a class feature, feat or creature can grant: the system's actions, reactions and triggers
        string system = rulesets.FirstOrDefault() ?? RulesFolder.Default;
        options["grants"] = new[] { "actions", "reactions", "triggers" }
            .SelectMany(folder => files.List(system + "/" + folder).Concat(game.List(system + "/" + folder)))
            .Select(ContentFiles.Stem).Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
        options["ai"] = new List<string> { "mindless", "animal", "cunning", "tactical" };
        foreach (CompendiumEditor.Kind kind in editor.Kinds)
        {
            if (!options.TryGetValue(kind.Form.Folder, out List<string>? list))
            {
                options[kind.Form.Folder] = list = new List<string>();
            }
            // the game's own: in its system's folder, and at the root for anything older
            string[] gameFolders = kind.Ruleset
                ? new[] { RulesFolder.Default + "/" + kind.Form.Folder }
                : new[] { RulesFolder.Default + "/" + kind.Form.Folder, kind.Form.Folder };
            foreach (string path in gameFolders.SelectMany(game.List))
            {
                string id = ContentFiles.Stem(path);
                if (!list.Contains(id))
                {
                    list.Add(id);
                }
            }
        }
        editor.SetOptions(options);
        _compendium = editor;
        return _compendium;
    }

    /// <summary>
    /// One of the game's own entries of a kind, by id, as it is on disk: from the system the
    /// package plays first, then the game's root folder for the kind. Null when there is none.
    /// </summary>
    public JsonObject? GameEntry(string kindId, string id)
    {
        if (_compendium?.KindOf(kindId) is not Yorehold.Rules.CompendiumEditor.Kind kind || !Yorehold.Rules.CompendiumEditor.ValidId(id))
        {
            return null;
        }
        var game = new ContentFiles(_gameAssets);
        foreach (string folder in new[] { SystemFolder + "/" + kind.Form.Folder, RulesFolder.Default + "/" + kind.Form.Folder, kind.Form.Folder })
        {
            if (ReadObject(game, $"{folder}/{id}.json") is JsonObject found)
            {
                return found;
            }
        }
        return null;
    }

    private void CloseCompendium()
    {
        _compendium = null;
        _compendiumError = "";
        _compendiumSkipped.Clear();
        _compendiumFolders.Clear();
    }

    private bool CompendiumToSave(List<Changed> changed)
    {
        if (_compendium == null)
        {
            return true;
        }
        CompendiumEditor editor = _compendium;
        foreach (int entry in editor.Changed())
        {
            if (editor.Problems(entry).FirstOrDefault(p => p.Error) is Yorehold.Rules.CompendiumEditor.Problem wrong)
            {
                Status = $"{editor.Entries[entry].Path} not saved: {wrong.Text}";
                return false;
            }
            string path = editor.Entries[entry].Path;
            string text = editor.ToJson(entry);
            int index = entry;
            changed.Add(new Changed(path, text, () => editor.MarkSaved(index)));
        }
        return true;
    }

    private void CompendiumProblems(List<CreateProblem> found)
    {
        if (_compendiumError.Length > 0)
        {
            found.Add(new CreateProblem("create/compendium.json", _compendiumError, true));
        }
        found.AddRange(_compendiumSkipped.Select(why => new CreateProblem(why, why, true)));
        if (_compendium != null)
        {
            for (int i = 0; i < _compendium.Entries.Count; i++)
            {
                CompendiumEditor.Entry entry = _compendium.Entries[i];
                found.AddRange(_compendium.Problems(i).Select(p => new CreateProblem(entry.Path, $"{entry.Id}: {p.Text}", p.Error)));
            }
        }
    }
}
