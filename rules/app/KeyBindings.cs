namespace Yorehold.Rules;

/// <summary>One thing a key can do, as ui/keys.json lists it.</summary>
public sealed record KeyAction(string Id, string Name, string Group, string Description, IReadOnlyList<string> Defaults);

/// <summary>
/// Which keys do what. The actions and their shipped keys come from ui/keys.json; what the player
/// changed is kept in the settings file as only the differences, so a new default in a later
/// version reaches everyone who never touched that action. Keys are names ("C", "F5", "Kp Add")
/// and mean nothing here; the screen turns them into real keys.
/// </summary>
public sealed class KeyBindings
{
    private readonly List<KeyAction> _actions = new();
    private readonly Dictionary<string, List<string>> _keys = new();

    public IReadOnlyList<KeyAction> Actions => _actions;

    /// <summary>
    /// ui/keys.json: {"actions": [{"id", "name", "group", "description", "keys": [...]}]}.
    /// Two actions may not ship with the same key.
    /// </summary>
    public static KeyBindings Read(ContentNode node)
    {
        node.RequireObject("key bindings");
        node.Only("actions");
        var bindings = new KeyBindings();
        var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ContentNode entry in node.At("actions").Items())
        {
            entry.RequireObject("a key action");
            entry.Only("id", "name", "group", "description", "keys");
            string id = entry.At("id").AsId();
            if (bindings._keys.ContainsKey(id))
            {
                throw entry.Fail("id", $"\"{id}\" is listed twice");
            }
            List<string> keys = entry.Texts("keys");
            foreach (string key in keys)
            {
                if (key.Length == 0)
                {
                    throw entry.Fail("keys", "a key name is empty");
                }
                if (taken.TryGetValue(key, out string? other))
                {
                    throw entry.Fail("keys", $"\"{key}\" is already the key for \"{other}\"");
                }
                taken[key] = id;
            }
            bindings._actions.Add(new KeyAction(id, entry.Name("name", id), entry.Text("group", "Game"), entry.Text("description", ""), keys));
            bindings._keys[id] = new List<string>(keys);
        }
        return bindings;
    }

    public KeyAction? Action(string id) => _actions.Find(a => a.Id == id);

    /// <summary>The keys an action has now; none for an id that isn't listed.</summary>
    public IReadOnlyList<string> Keys(string id)
    {
        return _keys.TryGetValue(id, out List<string>? keys) ? keys : Array.Empty<string>();
    }

    /// <summary>"C", "Left, A" or "none", for a label.</summary>
    public string KeysText(string id)
    {
        IReadOnlyList<string> keys = Keys(id);
        return keys.Count == 0 ? "none" : string.Join(", ", keys);
    }

    /// <summary>The player has moved this action off its shipped keys.</summary>
    public bool Changed(string id)
    {
        return Action(id) is KeyAction action && !Same(action.Defaults, Keys(id));
    }

    /// <summary>The action a key belongs to, null when it is free.</summary>
    public string? ActionFor(string key)
    {
        foreach (KeyAction action in _actions)
        {
            if (_keys[action.Id].Any(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase)))
            {
                return action.Id;
            }
        }
        return null;
    }

    /// <summary>
    /// Makes key the one key for an action. Whichever other action had it loses it, and its id
    /// comes back so the screen can say so. False for an action that isn't listed or an empty key.
    /// </summary>
    public bool Bind(string id, string key, out string? taken)
    {
        taken = null;
        if (!_keys.ContainsKey(id) || key.Length == 0)
        {
            return false;
        }
        string? owner = ActionFor(key);
        if (owner != null && owner != id)
        {
            _keys[owner].RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            taken = owner;
        }
        _keys[id] = new List<string> { key };
        return true;
    }

    /// <summary>
    /// Back to the shipped keys. A shipped key another action has taken since stays with that
    /// action, so nothing is ever on two.
    /// </summary>
    public void Reset(string id)
    {
        if (Action(id) is not KeyAction action)
        {
            return;
        }
        _keys[id] = new List<string>();
        foreach (string key in action.Defaults)
        {
            if (ActionFor(key) == null)
            {
                _keys[id].Add(key);
            }
        }
    }

    public void ResetAll()
    {
        foreach (KeyAction action in _actions)
        {
            _keys[action.Id] = new List<string>(action.Defaults);
        }
    }

    /// <summary>What differs from the shipped keys, for the settings file.</summary>
    public Dictionary<string, List<string>> Overrides()
    {
        var changed = new Dictionary<string, List<string>>();
        foreach (KeyAction action in _actions)
        {
            if (Changed(action.Id))
            {
                changed[action.Id] = new List<string>(_keys[action.Id]);
            }
        }
        return changed;
    }

    /// <summary>
    /// Puts the settings file's differences on top of the shipped keys. Actions it names that no
    /// longer exist are skipped, and a key named for two actions goes to the first.
    /// </summary>
    public void Apply(IReadOnlyDictionary<string, List<string>> overrides)
    {
        ResetAll();
        // emptied first, so a key one changed action gave up is free for the one that took it
        foreach (KeyAction action in _actions)
        {
            if (overrides.ContainsKey(action.Id))
            {
                _keys[action.Id] = new List<string>();
            }
        }
        foreach (KeyAction action in _actions)
        {
            if (!overrides.TryGetValue(action.Id, out List<string>? keys))
            {
                continue;
            }
            foreach (string key in keys)
            {
                string? owner = ActionFor(key);
                if (owner != null && !overrides.ContainsKey(owner))
                {
                    // the player gave this key away from an action they left alone otherwise
                    _keys[owner].RemoveAll(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
                    owner = null;
                }
                if (owner == null && key.Length > 0)
                {
                    _keys[action.Id].Add(key);
                }
            }
        }
    }

    private static bool Same(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        return a.Count == b.Count && a.Zip(b).All(pair => string.Equals(pair.First, pair.Second, StringComparison.OrdinalIgnoreCase));
    }
}
