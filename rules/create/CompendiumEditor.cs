using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// Compendium mode of Create: a package's definition files (items, creatures, classes, AI
/// profiles, kits and the ruleset's spells, races, backgrounds and feats) and the commands that
/// change them. It draws nothing. What each kind has is data: a FormSchema each, read from
/// create/compendium.json, so a new field is a line in that file. Fields a form doesn't list are
/// written back as they were. Every command goes on the history it was given.
/// </summary>
public sealed class CompendiumEditor
{
    public const int MaxId = 64;

    public sealed record Kind(FormSchema Form, string Reader, bool Ruleset);

    public sealed record Entry(string Kind, string Id, string Path, JsonObject Value)
    {
        public Entry Copy() => this with { Value = (JsonObject)Value.DeepClone() };
    }

    /// <summary>Error false: worth a look, but the file still saves.</summary>
    public sealed record Problem(string Text, bool Error = true);

    private readonly History _history;
    private List<Kind> _kinds = new();
    private Dictionary<string, List<string>> _options = new(StringComparer.Ordinal);
    // what an undo step puts back
    private List<Entry> _state = new();
    // path to the text on disk, in the editor's form
    private readonly Dictionary<string, string> _saved = new(StringComparer.Ordinal);

    public CompendiumEditor(History history)
    {
        _history = history;
    }

    public IReadOnlyList<Kind> Kinds => _kinds;
    public IReadOnlyList<Entry> Entries => _state;

    public Kind? KindOf(string id) => _kinds.Find(k => k.Form.Id == id);

    /// <summary>Ids are lowercase letters, digits, - and _.</summary>
    public static bool ValidId(string id) => ContentIds.IsId(id);

    /// <summary>The forms file (create/compendium.json). Clears the entries.</summary>
    public bool SetKinds(string text, out string error)
    {
        error = "";
        JsonNode? j;
        try
        {
            j = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            j = null;
        }
        if (j is not JsonObject o || o["kinds"] is not JsonArray list)
        {
            error = "the forms file is an object with a list of kinds";
            return false;
        }
        var kinds = new List<Kind>();
        foreach (JsonNode? k in list)
        {
            if (FormSchema.Read(k, out error) is not FormSchema form)
            {
                return false;
            }
            if (!ValidId(form.Folder))
            {
                error = form.Id + ": folder is one plain folder name";
                return false;
            }
            if (kinds.Any(other => other.Form.Id == form.Id))
            {
                error = form.Id + ": listed twice";
                return false;
            }
            string reader = FormJson.IsString(k!["reader"], out string? r) ? r : "";
            bool ruleset = k["ruleset"]?.GetValueKind() == JsonValueKind.True;
            kinds.Add(new Kind(form, reader, ruleset));
        }
        _kinds = kinds;
        _state = new List<Entry>();
        _saved.Clear();
        return true;
    }

    /// <summary>Named lists the forms offer (abilities, skills, the game's own ids). The entries here are added to the list named after their kind's folder.</summary>
    public void SetOptions(Dictionary<string, List<string>> options) => _options = options;

    public Dictionary<string, List<string>> Options()
    {
        var lists = _options.ToDictionary(p => p.Key, p => p.Value.ToList(), StringComparer.Ordinal);
        foreach (Entry entry in _state)
        {
            if (KindOf(entry.Kind) is Kind k)
            {
                if (!lists.TryGetValue(k.Form.Folder, out List<string>? list))
                {
                    lists[k.Form.Folder] = list = new List<string>();
                }
                if (!list.Contains(entry.Id))
                {
                    list.Add(entry.Id);
                }
            }
        }
        foreach (List<string> list in lists.Values)
        {
            list.Sort(StringComparer.Ordinal);
        }
        return lists;
    }

    /// <summary>A file read from the package. False (and why) when it isn't a JSON object; it is then left out.</summary>
    public bool AddFile(string kind, string path, string text, out string error)
    {
        error = "";
        JsonNode? value;
        try
        {
            value = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            value = null;
        }
        if (KindOf(kind) == null || value is not JsonObject o)
        {
            error = path + (KindOf(kind) != null ? ": isn't a JSON object" : $": no form for \"{kind}\"");
            return false;
        }
        var entry = new Entry(kind, ContentFiles.Stem(path), path, o);
        if (Find(path) is int found)
        {
            _state[found] = entry;
        }
        else
        {
            _state.Add(entry);
        }
        // compared in the editor's own form, so a hand-written file isn't rewritten until it is changed
        _saved[path] = ToJson(Find(path)!.Value);
        return true;
    }

    /// <summary>Entries of a kind, by id.</summary>
    public List<int> Of(string kind)
    {
        return Enumerable.Range(0, _state.Count).Where(i => _state[i].Kind == kind)
            .OrderBy(i => _state[i].Id, StringComparer.Ordinal).ThenBy(i => _state[i].Path, StringComparer.Ordinal).ToList();
    }

    public int? Find(string path)
    {
        int at = _state.FindIndex(e => e.Path == path);
        return at < 0 ? null : at;
    }

    /// <summary>
    /// A new entry in folder (the kind's folder in the package, or a ruleset's), from the form's
    /// defaults. An empty id gets "new-kind" with a number if that is taken.
    /// </summary>
    public int? Add(string kind, string folder, string id = "")
    {
        if (KindOf(kind) is not Kind k || folder.Length == 0)
        {
            return null;
        }
        if (id.Length == 0)
        {
            id = FreeId(folder, "new-" + kind);
        }
        string path = $"{folder}/{id}.json";
        if (!ValidId(id) || Find(path) != null || _saved.ContainsKey(path))
        {
            return null;
        }
        var entry = new Entry(kind, id, path, k.Form.Blank(id));
        Edit("Add " + id, () => _state.Add(entry.Copy()));
        return _state.Count - 1;
    }

    /// <summary>
    /// Entries brought in whole (an import from Foundry), as one change Undo takes back: each of a
    /// kind this editor has, into the folder given for it. Returns the paths that were already
    /// taken or of no kind here, which are left as they were.
    /// </summary>
    public List<string> AddAll(IEnumerable<(string Kind, string Folder, JsonObject Value)> entries)
    {
        var added = new List<Entry>();
        var skipped = new List<string>();
        foreach ((string kind, string folder, JsonObject value) in entries)
        {
            string id = FormJson.IsString(value["id"], out string? named) ? named : "";
            string path = $"{folder}/{id}.json";
            if (KindOf(kind) == null || folder.Length == 0 || !ValidId(id) || Find(path) != null || _saved.ContainsKey(path) || added.Any(e => e.Path == path))
            {
                skipped.Add(path);
                continue;
            }
            added.Add(new Entry(kind, id, path, (JsonObject)value.DeepClone()));
        }
        if (added.Count > 0)
        {
            Edit($"Import {added.Count}", () => _state.AddRange(added.Select(e => e.Copy())));
        }
        return skipped;
    }

    /// <summary>A copy of an entry beside it, under a new id.</summary>
    public int? Copy(int index, string id = "")
    {
        if (index < 0 || index >= _state.Count)
        {
            return null;
        }
        Entry from = _state[index];
        string folder = FolderOf(from.Path);
        if (id.Length == 0)
        {
            id = FreeId(folder, from.Id + "-copy");
        }
        string path = $"{folder}/{id}.json";
        if (KindOf(from.Kind) is not Kind k || !ValidId(id) || Find(path) != null || _saved.ContainsKey(path))
        {
            return null;
        }
        Entry entry = from.Copy() with { Id = id, Path = path };
        if (k.Form.IdKey.Length > 0)
        {
            entry.Value[k.Form.IdKey] = id;
        }
        Edit("Copy " + from.Id, () => _state.Add(entry.Copy()));
        return _state.Count - 1;
    }

    /// <summary>Typed text for a field; false (and why) if it can't be that field's value. Typing in one box is one undo step until EndTyping.</summary>
    public bool SetField(int index, string key, string text, out string error)
    {
        error = "";
        if (index < 0 || index >= _state.Count)
        {
            return false;
        }
        if (KindOf(_state[index].Kind)?.Form.Field(key) is not FormField field)
        {
            error = key + " isn't on this form";
            return false;
        }
        return field.Parse(text, out JsonNode? value, out error) && SetValue(index, key, value);
    }

    public bool SetField(int index, string key, string text) => SetField(index, key, text, out _);

    /// <summary>Null takes the key out.</summary>
    public bool SetValue(int index, string key, JsonNode? value)
    {
        if (index < 0 || index >= _state.Count)
        {
            return false;
        }
        // the id follows the file name, so it isn't changed here
        if (KindOf(_state[index].Kind) is not Kind k || k.Form.Field(key) is not FormField field || key == k.Form.IdKey)
        {
            return false;
        }
        JsonObject entry = _state[index].Value;
        bool had = entry.TryGetPropertyValue(key, out JsonNode? now);
        if ((value == null && !had) || (had && JsonNode.DeepEquals(now, value)))
        {
            return true;
        }
        JsonNode? copy = value?.DeepClone();
        string path = _state[index].Path;
        Edit($"Change {_state[index].Id} {field.Title}", () =>
        {
            JsonObject target = _state[index].Value;
            if (copy == null)
            {
                target.Remove(key);
            }
            else
            {
                // a new key goes at the end, ones already there stay where they were
                target[key] = copy.DeepClone();
            }
        }, $"compendium:{path}:{key}");
        return true;
    }

    public void EndTyping() => _history.BreakMerge();

    /// <summary>The file as it is written.</summary>
    public string ToJson(int index) => index >= 0 && index < _state.Count ? CreateJson.Write(_state[index].Value) + "\n" : "";

    /// <summary>Errors are what the game would refuse; warnings are names the lists don't know.</summary>
    public List<Problem> Problems(int index)
    {
        var found = new List<Problem>();
        if (index < 0 || index >= _state.Count)
        {
            return found;
        }
        Entry entry = _state[index];
        if (KindOf(entry.Kind) is not Kind k)
        {
            return new List<Problem> { new("no form for " + entry.Kind) };
        }
        Dictionary<string, List<string>> lists = Options();
        if (!ValidId(entry.Id))
        {
            found.Add(new Problem("the file name is lowercase letters, digits, - and _"));
        }
        if (k.Form.IdKey.Length > 0 && (!FormJson.IsString(entry.Value[k.Form.IdKey], out string? id) || id != entry.Id))
        {
            found.Add(new Problem($"{k.Form.IdKey} has to be the file name, \"{entry.Id}\""));
        }
        var warnings = new List<string>();
        found.AddRange(k.Form.Problems(entry.Value, lists, warnings).Select(t => new Problem(t)));
        // the game's own reader has the last word on what loads
        if (found.Count == 0 && k.Reader.Length > 0)
        {
            string why = ReaderProblem(k.Reader, entry, lists);
            if (why.Length > 0)
            {
                found.Add(new Problem(why));
            }
        }
        found.AddRange(warnings.Select(t => new Problem(t, false)));
        return found;
    }

    /// <summary>What the next save writes: entries whose text differs from the file on disk.</summary>
    public List<int> Changed()
    {
        return Enumerable.Range(0, _state.Count).Where(i => !_saved.TryGetValue(_state[i].Path, out string? saved) || saved != ToJson(i)).ToList();
    }

    public void MarkSaved(int index)
    {
        if (index >= 0 && index < _state.Count)
        {
            _saved[_state[index].Path] = ToJson(index);
        }
    }

    // What the game's own reader says about a file: "" when it would load it.
    private static string ReaderProblem(string reader, Entry entry, Dictionary<string, List<string>> lists)
    {
        try
        {
            ContentNode node = ContentNode.Parse(entry.Path, CreateJson.Write(entry.Value));
            switch (reader)
            {
                case "item":
                    ItemDefinition.Read(node);
                    break;
                case "class":
                    ClassDefinition.Read(node);
                    break;
                case "creature":
                    CreatureDefinition.Read(node);
                    break;
                case "kit":
                    Kit.Read(node);
                    break;
                case "spell":
                    SpellDefinition.Read(node);
                    break;
                case "race":
                    RaceDefinition.Read(node);
                    break;
                case "background":
                    BackgroundDefinition.Read(node);
                    break;
                case "feat":
                    FeatDefinition.Read(node);
                    break;
                case "option":
                    OptionDefinition.Read(node);
                    break;
                case "ai":
                    // a profile may start from any other: the built-in ones stand in for those in
                    // files, since only the shape of this one is being checked
                    List<string> named = lists.GetValueOrDefault("ai") ?? new List<string>();
                    AiProfile.Read(node, name => AiProfile.Preset(name) ?? (named.Contains(name) ? AiProfile.Preset("cunning") : null));
                    break;
            }
            return "";
        }
        catch (ContentException problem)
        {
            return problem.Message;
        }
    }

    private static string FolderOf(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? "" : path[..slash];
    }

    private string FreeId(string folder, string start)
    {
        string id = start;
        for (int n = 2; Find($"{folder}/{id}.json") != null || _saved.ContainsKey($"{folder}/{id}.json"); n++)
        {
            id = $"{start}-{n}";
        }
        return id;
    }

    private void Edit(string label, Action change, string mergeKey = "")
    {
        List<Entry> before = _state.Select(e => e.Copy()).ToList();
        change();
        List<Entry> after = _state.Select(e => e.Copy()).ToList();
        _history.Record(label, () => _state = after.Select(e => e.Copy()).ToList(), () => _state = before.Select(e => e.Copy()).ToList(), mergeKey);
    }
}
