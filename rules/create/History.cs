namespace Yorehold.Rules;

/// <summary>
/// Undo and redo for Create, one for the whole open package. Each edit is a pair of callbacks that
/// apply it and take it back; they hold everything they need (the old and the new values) and
/// never read what the editor holds when they run. A merge key joins edits that follow each
/// other with the same key, so one brush stroke or one typed word undoes as a whole; BreakMerge
/// ends the stroke. Groups join several edits into one step.
/// </summary>
public sealed class History
{
    private sealed class Entry
    {
        public string Label = "";
        public readonly List<(Action Redo, Action Undo)> Steps = new();
    }

    private const int Discarded = -1;

    private readonly List<Entry> _entries = new();
    private readonly int _limit;
    private int _position;
    // Discarded when the saved state can't be reached any more (undone past, then branched or trimmed)
    private int _savedPosition;
    private string _mergeKey = "";
    private Entry _group = new();
    private int _groupDepth;
    private bool _running;

    public History(int limit = 200)
    {
        if (limit < 1)
        {
            throw new ArgumentException("History needs room for at least one edit");
        }
        _limit = limit;
    }

    public int Count => _entries.Count;
    /// <summary>Goes up with every edit, undo, redo and clear, so a screen can tell something happened since it last looked.</summary>
    public int Changes { get; private set; }
    public bool CanUndo => _position > 0 && _groupDepth == 0;
    public bool CanRedo => _position < _entries.Count && _groupDepth == 0;
    /// <summary>For the toolbar: "Paint tiles". Empty with nothing to undo.</summary>
    public string UndoLabel => CanUndo ? _entries[_position - 1].Label : "";
    public string RedoLabel => CanRedo ? _entries[_position].Label : "";
    /// <summary>The package differs from what MarkSaved last saw.</summary>
    public bool Dirty => _savedPosition != _position;

    public void MarkSaved() => _savedPosition = _position;

    public void BreakMerge() => _mergeKey = "";

    /// <summary>Applies redo now and records it.</summary>
    public void Perform(string label, Action redo, Action undo, string mergeKey = "")
    {
        if (_running)
        {
            throw new InvalidOperationException("Cannot record edits while undoing or redoing");
        }
        redo();
        Add(label, redo, undo, mergeKey);
    }

    /// <summary>Records an edit that has already been made.</summary>
    public void Record(string label, Action redo, Action undo, string mergeKey = "")
    {
        Add(label, redo, undo, mergeKey);
    }

    public void BeginGroup(string label)
    {
        if (_running)
        {
            throw new InvalidOperationException("Cannot record edits while undoing or redoing");
        }
        if (_groupDepth++ == 0)
        {
            _group = new Entry { Label = label };
        }
    }

    public void EndGroup()
    {
        if (_groupDepth == 0)
        {
            throw new InvalidOperationException("EndGroup without BeginGroup");
        }
        if (--_groupDepth > 0 || _group.Steps.Count == 0)
        {
            return;
        }
        Entry entry = _group;
        _group = new Entry();
        Push(entry, "");
    }

    public bool Undo()
    {
        if (!CanUndo || _running)
        {
            return false;
        }
        _running = true;
        try
        {
            Entry entry = _entries[_position - 1];
            for (int i = entry.Steps.Count - 1; i >= 0; i--)
            {
                entry.Steps[i].Undo();
            }
        }
        finally
        {
            _running = false;
        }
        _position--;
        _mergeKey = "";
        Changes++;
        return true;
    }

    public bool Redo()
    {
        if (!CanRedo || _running)
        {
            return false;
        }
        _running = true;
        try
        {
            foreach ((Action redo, _) in _entries[_position].Steps)
            {
                redo();
            }
        }
        finally
        {
            _running = false;
        }
        _position++;
        _mergeKey = "";
        Changes++;
        return true;
    }

    /// <summary>Forgets every step. The editors the steps pointed into can go after this.</summary>
    public void Clear()
    {
        if (_running)
        {
            throw new InvalidOperationException("Cannot clear history while undoing or redoing");
        }
        _entries.Clear();
        _group = new Entry();
        _groupDepth = 0;
        _mergeKey = "";
        _savedPosition = _position == _savedPosition ? 0 : Discarded;
        _position = 0;
        Changes++;
    }

    private void Add(string label, Action redo, Action undo, string mergeKey)
    {
        if (_running)
        {
            throw new InvalidOperationException("Cannot record edits while undoing or redoing");
        }
        if (_groupDepth > 0)
        {
            _group.Steps.Add((redo, undo));
            return;
        }
        var entry = new Entry { Label = label };
        entry.Steps.Add((redo, undo));
        Push(entry, mergeKey);
    }

    private void Push(Entry entry, string mergeKey)
    {
        Changes++;
        if (_position < _entries.Count)
        {
            // a new edit after undoing drops the redo branch, and any saved state on it
            if (_savedPosition != Discarded && _savedPosition > _position)
            {
                _savedPosition = Discarded;
            }
            _entries.RemoveRange(_position, _entries.Count - _position);
            _mergeKey = "";
        }
        // never merged into the saved entry, or Dirty would miss the change
        if (mergeKey.Length > 0 && mergeKey == _mergeKey && _position > 0 && _savedPosition != _position)
        {
            _entries[^1].Steps.AddRange(entry.Steps);
            return;
        }
        _entries.Add(entry);
        _position++;
        _mergeKey = mergeKey;
        if (_entries.Count > _limit)
        {
            _entries.RemoveAt(0);
            _position--;
            if (_savedPosition != Discarded)
            {
                _savedPosition = _savedPosition == 0 ? Discarded : _savedPosition - 1;
            }
        }
    }
}
