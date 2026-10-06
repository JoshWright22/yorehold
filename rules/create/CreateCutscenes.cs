namespace Yorehold.Rules;

/// <summary>Cutscene mode's part of the open package: the chapter's cutscene files, one editor per file opened and one hooks part per chapter.</summary>
public sealed partial class CreatePackage
{
    private sealed class CutsceneTab
    {
        public CutsceneTab(History history) => Editor = new CutsceneEditor(history);

        public CutsceneEditor Editor { get; }
        public string Path = "";
        /// <summary>Empty for a file made here and not saved yet.</summary>
        public string Saved = "";
    }

    private readonly SortedDictionary<string, CutsceneTab> _cutscenes = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> _cutsceneErrors = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, CutsceneHooks> _hooks = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> _hookErrors = new(StringComparer.Ordinal);

    /// <summary>The cutscene open in Cutscene mode; empty for none.</summary>
    public string CutscenePath { get; private set; } = "";

    public string CutsceneError => _cutsceneErrors.GetValueOrDefault(CutscenePath, "");

    /// <summary>The chapter's triggers and endings, read the first time they are asked for.</summary>
    public CutsceneHooks? CutsceneHooks()
    {
        if (Manifest == null || Chapter.Length == 0)
        {
            return null;
        }
        if (_hooks.TryGetValue(Chapter, out CutsceneHooks? found))
        {
            return found;
        }
        if (_hookErrors.ContainsKey(Chapter))
        {
            return null;
        }
        var files = new ContentFiles(PackagePath);
        string path = Chapter + "/chapter.json";
        if (!files.Exists(path))
        {
            _hookErrors[Chapter] = path + ": missing";
            return null;
        }
        var hooks = new CutsceneHooks(_history);
        if (!hooks.Load(files.ReadText(path), Chapter, p => files.Exists(p) || _cutscenes.ContainsKey(p), out string error))
        {
            _hookErrors[Chapter] = $"{path}: {error}";
            return null;
        }
        _hooks[Chapter] = hooks;
        return hooks;
    }

    /// <summary>The chapter's cutscenes: its cutscenes folder, what its chapter.json plays, the package's declared ones and new ones not saved yet.</summary>
    public List<string> CutsceneFiles()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        if (Manifest == null || Chapter.Length == 0)
        {
            return new List<string>();
        }
        var files = new ContentFiles(PackagePath);
        foreach (string path in files.List(Chapter + "/cutscenes"))
        {
            found.Add(path);
        }
        foreach (string path in CutsceneHooks()?.Named().Where(files.Exists) ?? Enumerable.Empty<string>())
        {
            found.Add(path);
        }
        foreach (string path in Manifest.Cutscenes.Where(files.Exists))
        {
            found.Add(path);
        }
        foreach ((string path, CutsceneTab tab) in _cutscenes)
        {
            if (tab.Saved.Length == 0 && path.StartsWith(Chapter + "/", StringComparison.Ordinal))
            {
                found.Add(path);
            }
        }
        return found.ToList();
    }

    public bool OpenCutscene(string path)
    {
        CutscenePath = path;
        return CutsceneEditor() != null;
    }

    /// <summary>The open cutscene, read the first time it is asked for. Null if it can't be, and CutsceneError says why.</summary>
    public CutsceneEditor? CutsceneEditor()
    {
        if (Manifest == null || CutscenePath.Length == 0)
        {
            return null;
        }
        if (_cutscenes.TryGetValue(CutscenePath, out CutsceneTab? found))
        {
            return found.Editor;
        }
        if (_cutsceneErrors.ContainsKey(CutscenePath))
        {
            return null;
        }
        var files = new ContentFiles(PackagePath);
        if (!files.Exists(CutscenePath))
        {
            _cutsceneErrors[CutscenePath] = CutscenePath + ": missing";
            return null;
        }
        var tab = new CutsceneTab(_history) { Path = CutscenePath };
        if (!tab.Editor.Load(files.ReadText(CutscenePath), out string error))
        {
            _cutsceneErrors[CutscenePath] = $"{CutscenePath}: {error}";
            return null;
        }
        // compared in the editor's own form, so a hand-written file isn't rewritten until it is changed
        tab.Saved = tab.Editor.ToJson();
        _cutscenes[CutscenePath] = tab;
        return tab.Editor;
    }

    /// <summary>Starts another cutscene in the chapter's cutscenes folder and opens it; it is written at the next save.</summary>
    public string NewCutscene()
    {
        if (Manifest == null || Chapter.Length == 0)
        {
            return "";
        }
        var files = new ContentFiles(PackagePath);
        string id = "cutscene";
        for (int n = 2; files.Exists($"{Chapter}/cutscenes/{id}.json") || _cutscenes.ContainsKey($"{Chapter}/cutscenes/{id}.json"); n++)
        {
            id = $"cutscene-{n}";
        }
        string path = $"{Chapter}/cutscenes/{id}.json";
        var tab = new CutsceneTab(_history) { Path = path };
        tab.Editor.Create();
        _cutscenes[path] = tab;
        _cutsceneErrors.Remove(path);
        OpenCutscene(path);
        return path;
    }

    private void CloseCutscenes()
    {
        _cutscenes.Clear();
        _cutsceneErrors.Clear();
        _hooks.Clear();
        _hookErrors.Clear();
        CutscenePath = "";
    }

    private bool CutscenesToSave(List<Changed> changed)
    {
        foreach ((string path, CutsceneTab tab) in _cutscenes)
        {
            string text = tab.Editor.ToJson();
            if (text == tab.Saved)
            {
                continue;
            }
            if (tab.Editor.Problems().FirstOrDefault(p => p.Error) is Yorehold.Rules.CutsceneEditor.Problem wrong)
            {
                Status = $"{Leaf(path)} not saved: {wrong.Text}";
                return false;
            }
            changed.Add(new Changed(tab.Path, text, () => tab.Saved = text));
        }
        return true;
    }

    private void CutsceneProblems(List<CreateProblem> found)
    {
        foreach ((string path, string why) in _cutsceneErrors)
        {
            found.Add(new CreateProblem(path, why, true));
        }
        foreach ((string path, CutsceneTab tab) in _cutscenes)
        {
            found.AddRange(tab.Editor.Problems().Select(p => new CreateProblem(path, $"{Leaf(path)}: {p.Text}", p.Error)));
        }
        foreach ((string chapter, string why) in _hookErrors)
        {
            found.Add(new CreateProblem(chapter, why, true));
        }
        foreach ((string chapter, CutsceneHooks hooks) in _hooks)
        {
            found.AddRange(hooks.Problems().Select(p => new CreateProblem(chapter + "/chapter.json", $"{Leaf(chapter)}: {p.Text}", p.Error)));
        }
    }
}
