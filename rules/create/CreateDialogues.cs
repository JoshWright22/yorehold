using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>Dialogue mode's part of the open package: the chapter's conversation files and one editor per file opened.</summary>
public sealed partial class CreatePackage
{
    private sealed class DialogueTab
    {
        public DialogueTab(History history) => Editor = new DialogueEditor(history);

        public DialogueEditor Editor { get; }
        public string Path = "";
        /// <summary>Empty for a file made here and not saved yet.</summary>
        public string Saved = "";
    }

    private readonly SortedDictionary<string, DialogueTab> _dialogues = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, string> _dialogueErrors = new(StringComparer.Ordinal);

    /// <summary>The conversation open in Dialogue mode; empty for none.</summary>
    public string DialoguePath { get; private set; } = "";

    /// <summary>The chapter's conversations: its dialogue folder, what its chapter.json names, the package's declared ones and new ones not saved yet.</summary>
    public List<string> DialogueFiles() => DialogueFilesOf(Chapter);

    public List<string> DialogueFilesOf(string folder)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        if (Manifest == null || folder.Length == 0)
        {
            return new List<string>();
        }
        var files = new ContentFiles(PackagePath);
        foreach (string path in files.List(folder + "/dialogue"))
        {
            found.Add(path);
        }
        // what chapter.json names, in the chapter folder first and then at the root, as the game finds it
        void Named(JsonNode? entry, string key)
        {
            if (entry is not JsonObject o || o[key] is not JsonValue v || !v.TryGetValue(out string? path) || !ContentFiles.IsContentPath(path))
            {
                return;
            }
            if (files.Exists(folder + "/" + path))
            {
                found.Add(folder + "/" + path);
            }
            else if (files.Exists(path))
            {
                found.Add(path);
            }
        }
        if (ReadObject(files, folder + "/chapter.json") is JsonObject j)
        {
            // without its own, a chapter uses the package's dialogue/surrender.json if there is one
            Named(j.ContainsKey("surrender") ? j : new JsonObject { ["surrender"] = "dialogue/surrender.json" }, "surrender");
            Named(j["winCondition"], "dialogue");
            foreach (string list in new[] { "npcs", "triggers" })
            {
                foreach (JsonNode? entry in j[list] as JsonArray ?? new JsonArray())
                {
                    Named(entry, "dialogue");
                }
            }
            foreach (JsonNode? e in j["encounters"] as JsonArray ?? new JsonArray())
            {
                Named(e, "surrender");
                foreach (JsonNode? p in (e as JsonObject)?["creatures"] as JsonArray ?? new JsonArray())
                {
                    Named(p, "surrender");
                }
            }
        }
        foreach (string path in Manifest.Dialogues.Where(files.Exists))
        {
            found.Add(path);
        }
        foreach ((string path, DialogueTab tab) in _dialogues)
        {
            if (tab.Saved.Length == 0 && path.StartsWith(folder + "/", StringComparison.Ordinal))
            {
                found.Add(path);
            }
        }
        return found.ToList();
    }

    /// <summary>Picks the conversation Dialogue mode shows. True if it can be edited.</summary>
    public bool OpenDialogue(string path)
    {
        DialoguePath = path;
        return DialogueEditor() != null;
    }

    /// <summary>The open conversation, read the first time it is asked for. Null if it can't be, and DialogueError says why.</summary>
    public DialogueEditor? DialogueEditor()
    {
        if (Manifest == null || DialoguePath.Length == 0)
        {
            return null;
        }
        if (_dialogues.TryGetValue(DialoguePath, out DialogueTab? found))
        {
            return found.Editor;
        }
        if (_dialogueErrors.ContainsKey(DialoguePath))
        {
            return null;
        }
        var files = new ContentFiles(PackagePath);
        if (!files.Exists(DialoguePath))
        {
            _dialogueErrors[DialoguePath] = DialoguePath + ": missing";
            return null;
        }
        var tab = new DialogueTab(_history) { Path = DialoguePath };
        if (!tab.Editor.Load(files.ReadText(DialoguePath), out string error, DialogueCatalog(DialoguePath)))
        {
            _dialogueErrors[DialoguePath] = $"{DialoguePath}: {error}";
            return null;
        }
        // compared in the editor's own form, so a hand-written file isn't rewritten until it is changed
        tab.Saved = tab.Editor.ToJson();
        _dialogues[DialoguePath] = tab;
        return tab.Editor;
    }

    public string DialogueError => _dialogueErrors.GetValueOrDefault(DialoguePath, "");

    /// <summary>Starts another conversation in the chapter's dialogue folder and opens it; it is written at the next save. Empty if there is no chapter.</summary>
    public string NewDialogue()
    {
        if (Manifest == null || Chapter.Length == 0)
        {
            return "";
        }
        var files = new ContentFiles(PackagePath);
        string id = "conversation";
        for (int n = 2; files.Exists($"{Chapter}/dialogue/{id}.json") || _dialogues.ContainsKey($"{Chapter}/dialogue/{id}.json"); n++)
        {
            id = $"conversation-{n}";
        }
        string path = $"{Chapter}/dialogue/{id}.json";
        var tab = new DialogueTab(_history) { Path = path };
        tab.Editor.Create(id, DialogueCatalog(path));
        _dialogues[path] = tab;
        _dialogueErrors.Remove(path);
        OpenDialogue(path);
        return path;
    }

    /// <summary>What a conversation can name: the chapter's ruleset's skills and abilities, and who can join.</summary>
    public DialogueEditor.Catalog DialogueCatalog(string path)
    {
        var catalog = new DialogueEditor.Catalog();
        ContentFiles all = PlayFiles();
        JsonObject chapter = ReadObject(all, Chapter + "/chapter.json") ?? new JsonObject();

        // what a check can roll comes from the chapter's ruleset, found the way the game finds it;
        // one that can't be read leaves the list empty and the package's own checks say why
        string named = chapter["ruleset"] is JsonValue r && r.TryGetValue(out string? name) ? name : RulesFolder.Default;
        foreach (string where in new[] { $"{Chapter}/{named}/ruleset.json", named + "/ruleset.json", $"{Chapter}/{named}", named })
        {
            if (!all.Exists(where))
            {
                continue;
            }
            try
            {
                Ruleset rules = Ruleset.Read(ContentNode.Read(all, where));
                catalog.Skills.AddRange(rules.Skills.Select(s => s.Id));
                catalog.Skills.AddRange(rules.Abilities.Select(a => a.Id));
            }
            catch (ContentException)
            {
            }
            break;
        }

        // who can join, and whether the one talking in this file is one of them
        foreach (JsonNode? npc in chapter["npcs"] as JsonArray ?? new JsonArray())
        {
            if (npc is not JsonObject o || o["id"] is not JsonValue idValue || !idValue.TryGetValue(out string? id))
            {
                continue;
            }
            if (!o.ContainsKey("companion") && !o.ContainsKey("approvalStart") && !o.ContainsKey("approvalJoinThreshold"))
            {
                continue;
            }
            catalog.Companions.Add(id);
            string file = o["dialogue"] is JsonValue d && d.TryGetValue(out string? f) ? f : "";
            if (file.Length > 0 && (path == Chapter + "/" + file || path == file))
            {
                catalog.Companion = true;
            }
        }
        return catalog;
    }

    private void CloseDialogues()
    {
        _dialogues.Clear();
        _dialogueErrors.Clear();
        DialoguePath = "";
    }

    private bool DialoguesToSave(List<Changed> changed)
    {
        foreach ((string path, DialogueTab tab) in _dialogues)
        {
            string text = tab.Editor.ToJson();
            if (text == tab.Saved)
            {
                continue;
            }
            if (tab.Editor.Problems().FirstOrDefault(p => p.Error) is Yorehold.Rules.DialogueEditor.Problem wrong)
            {
                Status = $"{Leaf(path)} not saved: {wrong.Text}";
                return false;
            }
            changed.Add(new Changed(tab.Path, text, () => tab.Saved = text));
        }
        return true;
    }

    private void DialogueProblems(List<CreateProblem> found)
    {
        foreach ((string path, string why) in _dialogueErrors)
        {
            found.Add(new CreateProblem(path, why, true));
        }
        foreach ((string path, DialogueTab tab) in _dialogues)
        {
            found.AddRange(tab.Editor.Problems().Select(p => new CreateProblem(path, $"{Leaf(path)}: {p.Text}", p.Error)));
        }
    }

    // A JSON file as an object, or null if it is missing or isn't one.
    private static JsonObject? ReadObject(ContentFiles files, string path)
    {
        if (!files.Exists(path))
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(files.ReadText(path)) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
