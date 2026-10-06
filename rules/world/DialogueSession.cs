namespace Yorehold.Rules;

/// <summary>What one reply did: where it went and the check it rolled, if any.</summary>
public sealed record DialogueResult(string From, string Choice, string To, RollResult? Roll, bool Passed);

/// <summary>
/// A conversation going on: the node it is at, the story flags as it changes them, and the "do"
/// actions reached. The caller rolls checks, so the dice stay the World's. Like the C++ client's
/// DialogueSession: a skill check passes on total >= difficulty, natural 1 and 20 change nothing.
/// </summary>
public sealed class DialogueSession
{
    private readonly SortedSet<string> _flags;
    private readonly List<string> _actions = new();
    private string _current = "";

    public DialogueSession(Dialogue dialogue, IEnumerable<string> flags)
    {
        Dialogue = dialogue;
        _flags = new SortedSet<string>(flags, StringComparer.Ordinal);
        Enter(dialogue.Start);
    }

    public Dialogue Dialogue { get; }
    public IReadOnlySet<string> Flags => _flags;
    public DialogueNode? Current => _current.Length == 0 ? null : Dialogue.Node(_current);
    /// <summary>No node, or a node with no replies: the last line.</summary>
    public bool Finished => Current is not DialogueNode node || node.Choices.Count == 0;

    /// <summary>The replies the flags allow at the current node.</summary>
    public List<DialogueChoice> Choices()
    {
        return Current?.Choices.Where(Available).ToList() ?? new List<DialogueChoice>();
    }

    /// <summary>
    /// Picks a reply. A hidden or unknown one, or a check with no roller, changes nothing and gives
    /// null. The roll happens before any flag changes.
    /// </summary>
    public DialogueResult? Choose(string id, Func<string, RollResult>? rollCheck = null)
    {
        DialogueNode? node = Current;
        DialogueChoice? choice = node?.Choices.Find(c => c.Id == id);
        if (node == null || choice == null || !Available(choice))
        {
            return null;
        }
        string to = choice.Next;
        RollResult? roll = null;
        bool passed = false;
        if (choice.Check != null)
        {
            if (rollCheck == null)
            {
                return null;
            }
            roll = rollCheck(choice.Check.Skill);
            passed = roll.Total >= choice.Check.Difficulty;
            to = passed ? choice.Check.Success : choice.Check.Failure;
        }
        string from = _current;
        Apply(choice.Changes);
        Enter(to);
        return new DialogueResult(from, choice.Id, to, roll, passed);
    }

    /// <summary>The "do" actions reached since the last call, in order.</summary>
    public List<string> TakeActions()
    {
        List<string> taken = new(_actions);
        _actions.Clear();
        return taken;
    }

    public void Close() => _current = "";

    private bool Available(DialogueChoice choice)
    {
        return choice.Require.All(_flags.Contains) && !choice.Forbid.Any(_flags.Contains);
    }

    private void Apply(DialogueChanges changes)
    {
        foreach (string flag in changes.Clear)
        {
            _flags.Remove(flag);
        }
        _flags.UnionWith(changes.Set);
        _actions.AddRange(changes.Actions);
    }

    private void Enter(string id)
    {
        _current = id;
        if (Current is DialogueNode node)
        {
            Apply(node.Changes);
        }
    }
}
