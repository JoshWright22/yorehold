using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// Where a chapter plays its cutscenes: its triggers (onEnter and onFlag), endings.cleared,
/// onWipe.cutscene and winCondition.cutscene. It changes only those in the chapter.json it is
/// given at save, so it can sit on top of what Encounters mode writes.
/// </summary>
public sealed class CutsceneHooks
{
    public sealed record Trigger
    {
        public string Id { get; init; } = "";
        /// <summary>Empty = when the chapter starts.</summary>
        public List<string> When { get; init; } = new();
        /// <summary>As written in the file.</summary>
        public string Dialogue { get; init; } = "";
        public string Cutscene { get; init; } = "";
        public string Extra { get; init; } = "";

        public Trigger Copy() => this with { When = When.ToList() };
    }

    public sealed record Problem(string Text, bool Error = true);

    private sealed class State
    {
        public List<Trigger> Triggers = new();
        public string Cleared = "";
        public string Wipe = "";
        public string Win = "";
        public bool HasWin;

        public State Copy() => new() { Triggers = Triggers.Select(t => t.Copy()).ToList(), Cleared = Cleared, Wipe = Wipe, Win = Win, HasWin = HasWin };

        public bool SameTriggers(State other) => Triggers.Count == other.Triggers.Count
            && Triggers.Zip(other.Triggers).All(p => p.First.Id == p.Second.Id && p.First.When.SequenceEqual(p.Second.When)
                && p.First.Dialogue == p.Second.Dialogue && p.First.Cutscene == p.Second.Cutscene && p.First.Extra == p.Second.Extra);

        public bool Same(State other) => SameTriggers(other) && Cleared == other.Cleared && Wipe == other.Wipe && Win == other.Win && HasWin == other.HasWin;
    }

    private readonly History _history;
    private bool _loaded;
    private Func<string, bool> _exists = _ => false;
    private State _state = new();
    private State _saved = new();

    public CutsceneHooks(History history)
    {
        _history = history;
    }

    public bool Loaded => _loaded;
    public string Folder { get; private set; } = "";
    public IReadOnlyList<Trigger> Triggers => _state.Triggers;
    public string Cleared => _state.Cleared;
    public string Wipe => _state.Wipe;
    public bool HasWin => _state.HasWin;
    public string Win => _state.Win;

    /// <summary>exists says if there is a file at a package path (saved or only open), to resolve names the way the game does.</summary>
    public bool Load(string chapterJson, string folder, Func<string, bool>? exists, out string error)
    {
        if (Read(chapterJson, out error) is not State state)
        {
            return false;
        }
        _state = state;
        _saved = state.Copy();
        Folder = folder;
        _exists = exists ?? (_ => false);
        _loaded = true;
        return true;
    }

    /// <summary>chapterJson with these fields as they are here; one this doesn't change keeps its text.</summary>
    public string ApplyTo(string chapterJson)
    {
        if (!_loaded || Read(chapterJson, out _) is not State there || there.Same(_state))
        {
            return chapterJson;
        }
        // Read parsed it, so it is an object
        JsonObject j = JsonNode.Parse(chapterJson)!.AsObject();
        if (!there.SameTriggers(_state))
        {
            var triggers = new JsonArray();
            foreach (Trigger trigger in _state.Triggers)
            {
                var t = new JsonObject { ["id"] = trigger.Id };
                if (trigger.When.Count > 0)
                {
                    t["when"] = CreateJson.Texts(trigger.When);
                }
                if (trigger.Dialogue.Length > 0)
                {
                    t["dialogue"] = trigger.Dialogue;
                }
                if (trigger.Cutscene.Length > 0)
                {
                    t["cutscene"] = trigger.Cutscene;
                }
                CreateJson.AddExtra(t, trigger.Extra);
                triggers.Add(t);
            }
            if (triggers.Count == 0)
            {
                j.Remove("triggers");
            }
            else
            {
                j["triggers"] = triggers;
            }
        }
        // one key inside an object: set, or taken out along with an object left empty
        void Put(string name, string key, string was, string now)
        {
            if (was == now)
            {
                return;
            }
            if (now.Length > 0)
            {
                if (j[name] is not JsonObject)
                {
                    j[name] = new JsonObject();
                }
                j[name]![key] = now;
            }
            else if (j[name] is JsonObject inside)
            {
                inside.Remove(key);
                if (inside.Count == 0)
                {
                    j.Remove(name);
                }
            }
        }
        Put("endings", "cleared", there.Cleared, _state.Cleared);
        Put("onWipe", "cutscene", there.Wipe, _state.Wipe);
        if (there.HasWin)
        {
            Put("winCondition", "cutscene", there.Win, _state.Win);
        }
        return CreateJson.Write(j);
    }

    /// <summary>Something here differs from the last load or MarkSaved.</summary>
    public bool Changed => _loaded && !_state.Same(_saved);

    public void MarkSaved() => _saved = _state.Copy();

    /// <summary>The package path a name in chapter.json stands for.</summary>
    public string Resolve(string named)
    {
        if (!ContentFiles.IsContentPath(named))
        {
            return named;
        }
        string local = Folder + "/" + named;
        return _exists(local) ? local : named;
    }

    /// <summary>The name to write in chapter.json for a package path.</summary>
    public string NameFor(string path) => path.StartsWith(Folder + "/", StringComparison.Ordinal) ? path[(Folder.Length + 1)..] : path;

    public bool Plays(string named, string path) => named.Length > 0 && Resolve(named) == path;

    /// <summary>Every cutscene the chapter names, as package paths.</summary>
    public List<string> Named()
    {
        var found = new List<string>();
        foreach (string name in new[] { _state.Cleared, _state.Wipe, _state.Win }.Concat(_state.Triggers.Select(t => t.Cutscene)))
        {
            string path = Resolve(name);
            if (name.Length > 0 && !found.Contains(path))
            {
                found.Add(path);
            }
        }
        return found;
    }

    /// <summary>A trigger that plays path, with a free id ("cutscene-N").</summary>
    public int? AddTrigger(string path, IReadOnlyList<string>? when = null)
    {
        List<string> flags = when?.ToList() ?? new List<string>();
        if (!_loaded || path.Length == 0 || !ValidFlags(flags) || _state.Triggers.Count >= 1000)
        {
            return null;
        }
        string id = "";
        for (int n = 1; id.Length == 0; n++)
        {
            if (_state.Triggers.All(t => t.Id != $"cutscene-{n}"))
            {
                id = $"cutscene-{n}";
            }
        }
        Edit("Add trigger " + id, () => _state.Triggers.Add(new Trigger { Id = id, When = flags.ToList(), Cutscene = NameFor(path) }));
        return _state.Triggers.Count - 1;
    }

    /// <summary>One that also opens a conversation keeps it and only stops playing the cutscene.</summary>
    public bool RemoveTrigger(int trigger)
    {
        if (!HasTrigger(trigger))
        {
            return false;
        }
        Edit("Remove trigger " + _state.Triggers[trigger].Id, () =>
        {
            if (_state.Triggers[trigger].Dialogue.Length > 0)
            {
                _state.Triggers[trigger] = _state.Triggers[trigger] with { Cutscene = "" };
            }
            else
            {
                _state.Triggers.RemoveAt(trigger);
            }
        });
        return true;
    }

    /// <summary>Ids use a-z, 0-9, - and _ and are unique in the chapter.</summary>
    public bool SetTriggerId(int trigger, string id)
    {
        if (!HasTrigger(trigger) || !ContentIds.IsId(id) || _state.Triggers.Any(t => t.Id == id))
        {
            return false;
        }
        Edit("Rename trigger", () => _state.Triggers[trigger] = _state.Triggers[trigger] with { Id = id }, $"trigger-id-{trigger}");
        return true;
    }

    public bool SetTriggerWhen(int trigger, IReadOnlyList<string> when)
    {
        if (!HasTrigger(trigger) || !ValidFlags(when) || _state.Triggers[trigger].When.SequenceEqual(when))
        {
            return false;
        }
        List<string> flags = when.ToList();
        Edit("Change trigger flags", () => _state.Triggers[trigger] = _state.Triggers[trigger] with { When = flags.ToList() }, $"trigger-when-{trigger}");
        return true;
    }

    /// <summary>A package path, or "" for none.</summary>
    public bool SetCleared(string path)
    {
        string name = path.Length == 0 ? "" : NameFor(path);
        if (!_loaded || _state.Cleared == name)
        {
            return false;
        }
        Edit(name.Length == 0 ? "No cutscene when cleared" : $"Play {name} when cleared", () => _state.Cleared = name);
        return true;
    }

    public bool SetWipe(string path)
    {
        string name = path.Length == 0 ? "" : NameFor(path);
        if (!_loaded || _state.Wipe == name)
        {
            return false;
        }
        Edit(name.Length == 0 ? "No cutscene on a wipe" : $"Play {name} on a wipe", () => _state.Wipe = name);
        return true;
    }

    /// <summary>Only while the chapter has a winCondition.</summary>
    public bool SetWin(string path)
    {
        string name = path.Length == 0 ? "" : NameFor(path);
        if (!_loaded || !_state.HasWin || _state.Win == name)
        {
            return false;
        }
        Edit(name.Length == 0 ? "No cutscene on a win" : $"Play {name} on a win", () => _state.Win = name);
        return true;
    }

    public void EndTyping() => _history.BreakMerge();

    public List<Problem> Problems()
    {
        var found = new List<Problem>();
        if (!_loaded)
        {
            return found;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Trigger trigger in _state.Triggers)
        {
            if (!ContentIds.IsId(trigger.Id))
            {
                found.Add(new Problem($"trigger {trigger.Id}: ids use a-z, 0-9, - and _"));
            }
            else if (!ids.Add(trigger.Id))
            {
                found.Add(new Problem($"two triggers are called {trigger.Id}, so only one of them is remembered as fired", false));
            }
            if (trigger.Dialogue.Length == 0 && trigger.Cutscene.Length == 0)
            {
                found.Add(new Problem($"trigger {trigger.Id} plays nothing"));
            }
            if (!ValidFlags(trigger.When))
            {
                found.Add(new Problem($"trigger {trigger.Id}: flags are 1 to 64 characters, each once"));
            }
        }
        // a path the game can't find stops the chapter loading
        void Missing(string name, string what)
        {
            if (name.Length == 0)
            {
                return;
            }
            if (!ContentFiles.IsContentPath(name))
            {
                found.Add(new Problem($"{what}: {name} has to be a path inside the package"));
            }
            else if (!_exists(Resolve(name)))
            {
                found.Add(new Problem($"{what}: there is no {name}"));
            }
        }
        Missing(_state.Cleared, "endings.cleared");
        Missing(_state.Wipe, "onWipe");
        Missing(_state.Win, "winCondition");
        foreach (Trigger trigger in _state.Triggers)
        {
            Missing(trigger.Cutscene, "trigger " + trigger.Id);
        }
        return found;
    }

    private static State? Read(string chapterJson, out string error)
    {
        error = "";
        try
        {
            ContentNode j = ContentNode.Parse("chapter.json", chapterJson);
            j.RequireObject("a chapter is a JSON object");
            var state = new State();
            if (j.Get("triggers") is ContentNode triggers)
            {
                if (!triggers.IsArray)
                {
                    throw triggers.Fail("must be an array");
                }
                foreach (ContentNode t in triggers.Items())
                {
                    t.RequireObject("each trigger is an object");
                    state.Triggers.Add(new Trigger
                    {
                        Id = t.At("id").AsText(),
                        When = t.Texts("when"),
                        Dialogue = t.Text("dialogue", ""),
                        Cutscene = t.Text("cutscene", ""),
                        Extra = CreateJson.ExtraOf(t, new[] { "id", "when", "dialogue", "cutscene" }),
                    });
                }
            }
            string Inside(string name, string key) => j.Get(name) is ContentNode o && o.IsObject ? o.Text(key, "") : "";
            state.Cleared = Inside("endings", "cleared");
            state.Wipe = Inside("onWipe", "cutscene");
            state.HasWin = j.Get("winCondition") is ContentNode w && w.IsObject;
            state.Win = Inside("winCondition", "cutscene");
            return state;
        }
        catch (ContentException problem)
        {
            error = problem.Message;
            return null;
        }
    }

    private static bool ValidFlags(IReadOnlyList<string> flags) => flags.All(f => f.Length > 0 && f.Length <= CutsceneEditor.MaxName) && flags.Distinct().Count() == flags.Count;

    private bool HasTrigger(int trigger) => _loaded && trigger >= 0 && trigger < _state.Triggers.Count;

    private void Edit(string label, Action change, string mergeKey = "")
    {
        State before = _state.Copy();
        change();
        State after = _state.Copy();
        _history.Record(label, () => _state = after.Copy(), () => _state = before.Copy(), mergeKey);
    }
}
