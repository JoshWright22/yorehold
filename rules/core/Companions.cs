using System.Text.Json.Nodes;

namespace Yorehold.Rules;

public enum CompanionJoin
{
    Joined,
    /// <summary>A member already.</summary>
    Already,
    /// <summary>No definition with that id.</summary>
    Unknown,
    /// <summary>Approval is below their joinAt.</summary>
    LowApproval,
    /// <summary>The ruleset's limit or party limit is reached.</summary>
    Full,
}

/// <summary>
/// Approval and membership for every companion the party has met, kept for the whole adventure.
/// Who they are on the map is the World's.
/// </summary>
public sealed class Companions
{
    private const int Bound = 1000000;

    private readonly List<CompanionDefinition> _definitions = new();
    private readonly SortedDictionary<string, int> _approval = new(StringComparer.Ordinal);
    private readonly List<string> _members = new();
    private readonly SortedSet<(string Id, string Flag)> _counted = new();

    public IReadOnlyList<CompanionDefinition> Definitions => _definitions;
    /// <summary>In the order they joined.</summary>
    public IReadOnlyList<string> Members => _members;

    public void Clear()
    {
        _definitions.Clear();
        _approval.Clear();
        _members.Clear();
        _counted.Clear();
    }

    /// <summary>Becomes a copy of another roster: definitions, approval, members and the flags counted.</summary>
    public void CopyFrom(Companions other)
    {
        Clear();
        _definitions.AddRange(other._definitions);
        foreach (KeyValuePair<string, int> entry in other._approval)
        {
            _approval[entry.Key] = entry.Value;
        }
        _members.AddRange(other._members);
        _counted.UnionWith(other._counted);
    }

    /// <summary>Adds a definition or replaces one with the same id. Approval starts at its own the first time and is kept after that.</summary>
    public void Define(CompanionDefinition definition)
    {
        _approval.TryAdd(definition.Id, definition.Approval);
        int same = _definitions.FindIndex(d => d.Id == definition.Id);
        if (same >= 0)
        {
            _definitions[same] = definition;
        }
        else
        {
            _definitions.Add(definition);
        }
    }

    public CompanionDefinition? Definition(string id) => _definitions.Find(d => d.Id == id);

    /// <summary>0 for an unknown id.</summary>
    public int Approval(string id) => _approval.GetValueOrDefault(id);

    public bool Member(string id) => _members.Contains(id);

    /// <summary>Sets approval within the ruleset's range. A member who ends at or below leaveAt leaves; true when that happened.</summary>
    public bool SetApproval(CompanionRules rules, string id, int value)
    {
        CompanionDefinition? d = Definition(id);
        if (d == null)
        {
            return false;
        }
        int now = Math.Clamp(value, rules.ApprovalMin, rules.ApprovalMax);
        _approval[id] = now;
        return d.LeaveAt is int leave && now <= leave && Leave(id);
    }

    public bool Adjust(CompanionRules rules, string id, int delta)
    {
        long sum = (long)Approval(id) + delta;
        return SetApproval(rules, id, (int)Math.Clamp(sum, -Bound, Bound));
    }

    /// <summary>others: how many in the party aren't companions (the heroes).</summary>
    public CompanionJoin CanJoin(CompanionRules rules, string id, int others)
    {
        CompanionDefinition? d = Definition(id);
        if (d == null)
        {
            return CompanionJoin.Unknown;
        }
        if (Member(id))
        {
            return CompanionJoin.Already;
        }
        if (Approval(id) < d.JoinAt)
        {
            return CompanionJoin.LowApproval;
        }
        int count = _members.Count;
        if ((rules.Limit > 0 && count >= rules.Limit) || (rules.PartyLimit > 0 && others + count >= rules.PartyLimit))
        {
            return CompanionJoin.Full;
        }
        return CompanionJoin.Joined;
    }

    public CompanionJoin Join(CompanionRules rules, string id, int others)
    {
        CompanionJoin result = CanJoin(rules, id, others);
        if (result == CompanionJoin.Joined)
        {
            _members.Add(id);
        }
        return result;
    }

    /// <summary>False if they weren't a member.</summary>
    public bool Leave(string id) => _members.Remove(id);

    /// <summary>
    /// The story's flags as they are now: each definition's flags that are set and weren't counted
    /// before change approval once. Returns the ids of members who left because of it.
    /// </summary>
    public List<string> FlagsSet(CompanionRules rules, IReadOnlySet<string> flags)
    {
        var left = new List<string>();
        foreach (CompanionDefinition d in _definitions.ToList())
        {
            foreach (KeyValuePair<string, int> flag in d.Flags)
            {
                if (flags.Contains(flag.Key) && _counted.Add((d.Id, flag.Key)) && Adjust(rules, d.Id, flag.Value))
                {
                    left.Add(d.Id);
                }
            }
        }
        return left;
    }

    /// <summary>The C++ client's shape: definitions, approval, members and the flags counted.</summary>
    public JsonObject ToJson()
    {
        var definitions = new JsonArray();
        foreach (CompanionDefinition d in _definitions)
        {
            var j = new JsonObject { ["id"] = d.Id, ["approval"] = d.Approval, ["joinAt"] = d.JoinAt };
            if (d.LeaveAt is int leave)
            {
                j["leaveAt"] = leave;
            }
            if (d.Flags.Count > 0)
            {
                var flags = new JsonObject();
                foreach (KeyValuePair<string, int> flag in d.Flags)
                {
                    flags[flag.Key] = flag.Value;
                }
                j["flags"] = flags;
            }
            definitions.Add(j);
        }
        var approval = new JsonObject();
        foreach (KeyValuePair<string, int> entry in _approval)
        {
            approval[entry.Key] = entry.Value;
        }
        var counted = new JsonArray();
        foreach ((string id, string flag) in _counted)
        {
            counted.Add(new JsonArray(id, flag));
        }
        return new JsonObject
        {
            ["definitions"] = definitions,
            ["approval"] = approval,
            ["members"] = new JsonArray(_members.Select(m => (JsonNode)JsonValue.Create(m)!).ToArray()),
            ["counted"] = counted,
        };
    }

    public static Companions Read(ContentNode node)
    {
        node.RequireObject("companions are an object");
        var c = new Companions();
        foreach (ContentNode d in node.Get("definitions")?.Items() ?? Array.Empty<ContentNode>())
        {
            CompanionDefinition read = CompanionDefinition.Read(d, d.At("id").AsText());
            if (read.Id.Length == 0 || c.Definition(read.Id) != null)
            {
                throw d.Fail("id", "a companion needs an id of its own");
            }
            c.Define(read);
        }
        foreach (KeyValuePair<string, ContentNode> entry in node.Get("approval")?.Members() ?? Array.Empty<KeyValuePair<string, ContentNode>>())
        {
            if (c.Definition(entry.Key) == null)
            {
                throw entry.Value.Fail($"approval for an unknown companion {entry.Key}");
            }
            c._approval[entry.Key] = entry.Value.AsInt(-Bound, Bound);
        }
        foreach (string id in node.Texts("members"))
        {
            if (c.Definition(id) == null || c.Member(id))
            {
                throw node.Fail("members", $"unknown or repeated member {id}");
            }
            c._members.Add(id);
        }
        foreach (ContentNode entry in node.Get("counted")?.Items() ?? Array.Empty<ContentNode>())
        {
            string[] pair = entry.Items().Select(n => n.AsText()).ToArray();
            if (pair.Length != 2)
            {
                throw entry.Fail("is [companion, flag]");
            }
            c._counted.Add((pair[0], pair[1]));
        }
        return c;
    }
}
