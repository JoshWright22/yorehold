using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// Dialogue mode of Create: one conversation file and the commands that change it. It draws
/// nothing. It reads and writes the dialogue format the game plays (CONTENT.md), writing back
/// fields it has no tool for as they were. Every command goes on the history it was given, which
/// the whole open package shares.
/// </summary>
public sealed class DialogueEditor
{
    public const int MaxId = 64;
    public const int MaxSpeaker = 64;
    public const int MaxLine = 4000;
    public const int MaxReply = 1000;
    public const int MaxAction = 200;

    /// <summary>What a conversation can name, from the chapter and package it is in.</summary>
    public sealed class Catalog
    {
        /// <summary>What a check can roll: skills, then abilities.</summary>
        public List<string> Skills { get; init; } = new();
        /// <summary>Ids of the chapter's NPCs who can join.</summary>
        public SortedSet<string> Companions { get; init; } = new(StringComparer.Ordinal);
        /// <summary>The NPC who uses this file can join the party.</summary>
        public bool Companion { get; set; }
    }

    public sealed record Flags
    {
        public List<string> Set { get; init; } = new();
        public List<string> Clear { get; init; } = new();
        public List<string> Actions { get; init; } = new();

        public bool Same(Flags other) => Set.SequenceEqual(other.Set) && Clear.SequenceEqual(other.Clear) && Actions.SequenceEqual(other.Actions);

        public Flags Copy() => new() { Set = Set.ToList(), Clear = Clear.ToList(), Actions = Actions.ToList() };
    }

    public sealed record Choice
    {
        public string Id { get; init; } = "";
        public string Text { get; init; } = "";
        /// <summary>A node id; empty ends the conversation. Unused with a check.</summary>
        public string Next { get; init; } = "";
        public List<string> Require { get; init; } = new();
        public List<string> Forbid { get; init; } = new();
        public Flags Flags { get; init; } = new();
        public DialogueCheck? Check { get; init; }
        /// <summary>Fields the editor has no tool for, as a JSON object; empty = none.</summary>
        public string Extra { get; init; } = "";

        public Choice Copy() => this with { Require = Require.ToList(), Forbid = Forbid.ToList(), Flags = Flags.Copy() };
    }

    public sealed record Node
    {
        public string Id { get; init; } = "";
        public string Speaker { get; init; } = "";
        public string Text { get; init; } = "";
        /// <summary>On entering it.</summary>
        public Flags Flags { get; init; } = new();
        public List<Choice> Choices { get; init; } = new();
        public string Extra { get; init; } = "";

        public Node Copy() => this with { Flags = Flags.Copy(), Choices = Choices.Select(c => c.Copy()).ToList() };
    }

    /// <summary>Error false: worth a look, but the file still saves and plays.</summary>
    public sealed record Problem(string Text, bool Error = true);

    // What an undo step puts back.
    private sealed class State
    {
        public string Id = "";
        public string Start = "";
        public List<Node> Nodes = new();
        public string Extra = "";

        public State Copy() => new() { Id = Id, Start = Start, Nodes = Nodes.Select(n => n.Copy()).ToList(), Extra = Extra };
    }

    private static readonly string[] FileFields = { "id", "start", "nodes" };
    private static readonly string[] NodeFields = { "id", "speaker", "text", "set", "clear", "do", "choices" };
    private static readonly string[] ChoiceFields = { "id", "text", "next", "require", "forbid", "set", "clear", "do", "check" };

    private readonly History _history;
    private bool _loaded;
    private State _state = new();
    private Catalog _catalog = new();

    public DialogueEditor(History history)
    {
        _history = history;
    }

    public bool Loaded => _loaded;
    public string Id => _state.Id;
    public string Start => _state.Start;
    public IReadOnlyList<Node> Nodes => _state.Nodes;
    public Catalog Names => _catalog;

    /// <summary>Only a file the game would load: what it refuses is refused here too, with its reason.</summary>
    public bool Load(string text, out string error, Catalog? catalog = null)
    {
        error = "";
        try
        {
            // the game's own reader first, so nothing it refuses opens here
            ContentNode j = ContentNode.Parse("dialogue", text);
            Rules.Dialogue.Read(j);
            var state = new State { Id = j.At("id").AsText(), Start = j.At("start").AsText(), Extra = CreateJson.ExtraOf(j, FileFields) };
            foreach (ContentNode n in j.At("nodes").Items())
            {
                var choices = new List<Choice>();
                foreach (ContentNode c in n.Get("choices")?.Items() ?? Array.Empty<ContentNode>())
                {
                    DialogueCheck? check = c.Get("check") is ContentNode k
                        ? new DialogueCheck(k.At("skill").AsText(), k.At("difficulty").AsInt(), k.At("success").AsText(), k.At("failure").AsText())
                        : null;
                    choices.Add(new Choice
                    {
                        Id = c.At("id").AsText(),
                        Text = c.At("text").AsText(),
                        Next = c.Text("next", ""),
                        Require = c.Texts("require"),
                        Forbid = c.Texts("forbid"),
                        Flags = FlagsFrom(c),
                        Check = check,
                        Extra = CreateJson.ExtraOf(c, ChoiceFields),
                    });
                }
                state.Nodes.Add(new Node
                {
                    Id = n.At("id").AsText(),
                    Speaker = n.Text("speaker", ""),
                    Text = n.Text("text", ""),
                    Flags = FlagsFrom(n),
                    Choices = choices,
                    Extra = CreateJson.ExtraOf(n, NodeFields),
                });
            }
            _state = state;
            _catalog = catalog ?? new Catalog();
            _loaded = true;
            return true;
        }
        catch (ContentException problem)
        {
            error = problem.Message;
            return false;
        }
    }

    /// <summary>A new conversation with one empty node, "start".</summary>
    public void Create(string id, Catalog? catalog = null)
    {
        _state = new State { Id = id.Length == 0 ? "conversation" : id, Start = "start" };
        _state.Nodes.Add(new Node { Id = "start" });
        _catalog = catalog ?? new Catalog();
        _loaded = true;
    }

    public string ToJson()
    {
        if (!_loaded)
        {
            return "{}";
        }
        var j = new JsonObject { ["id"] = _state.Id, ["start"] = _state.Start };
        CreateJson.AddExtra(j, _state.Extra);
        var nodes = new JsonArray();
        foreach (Node node in _state.Nodes)
        {
            var n = new JsonObject { ["id"] = node.Id, ["speaker"] = node.Speaker, ["text"] = node.Text };
            WriteFlags(n, node.Flags);
            if (node.Choices.Count > 0)
            {
                var choices = new JsonArray();
                foreach (Choice choice in node.Choices)
                {
                    var c = new JsonObject { ["id"] = choice.Id, ["text"] = choice.Text };
                    if (choice.Check == null)
                    {
                        c["next"] = choice.Next;
                    }
                    if (choice.Require.Count > 0)
                    {
                        c["require"] = CreateJson.Texts(choice.Require);
                    }
                    if (choice.Forbid.Count > 0)
                    {
                        c["forbid"] = CreateJson.Texts(choice.Forbid);
                    }
                    WriteFlags(c, choice.Flags);
                    if (choice.Check is DialogueCheck check)
                    {
                        c["check"] = new JsonObject { ["skill"] = check.Skill, ["difficulty"] = check.Difficulty, ["success"] = check.Success, ["failure"] = check.Failure };
                    }
                    CreateJson.AddExtra(c, choice.Extra);
                    choices.Add(c);
                }
                n["choices"] = choices;
            }
            CreateJson.AddExtra(n, node.Extra);
            nodes.Add(n);
        }
        j["nodes"] = nodes;
        return CreateJson.Write(j);
    }

    /// <summary>As the game reads it; null (and why) while something in it would be refused.</summary>
    public Dialogue? Dialogue(out string error)
    {
        error = "";
        if (!_loaded)
        {
            error = "nothing is open";
            return null;
        }
        try
        {
            return Rules.Dialogue.Read(ContentNode.Parse("dialogue", ToJson()));
        }
        catch (ContentException problem)
        {
            error = problem.Message;
            return null;
        }
    }

    public Dialogue? Dialogue() => Dialogue(out _);

    public int? Find(string node)
    {
        int at = _state.Nodes.FindIndex(n => n.Id == node);
        return at < 0 ? null : at;
    }

    /// <summary>How many replies lead to a node, from anywhere.</summary>
    public int LinksTo(string node)
    {
        int links = 0;
        foreach (Choice c in _state.Nodes.SelectMany(n => n.Choices))
        {
            if (c.Check is DialogueCheck check)
            {
                links += (check.Success == node ? 1 : 0) + (check.Failure == node ? 1 : 0);
            }
            else if (c.Next == node)
            {
                links++;
            }
        }
        return links;
    }

    /// <summary>Ids are 1 to 64 characters with no spaces.</summary>
    public static bool ValidId(string id) => id.Length > 0 && id.Length <= MaxId && id.All(c => c > ' ');

    public bool SetId(string id)
    {
        if (!_loaded || !ValidId(id) || id == _state.Id)
        {
            return false;
        }
        Edit("Rename conversation", () => _state.Id = id, "dialogue-id");
        return true;
    }

    public bool SetStart(int node)
    {
        if (!HasNode(node) || _state.Nodes[node].Id == _state.Start)
        {
            return false;
        }
        Edit("Start at " + _state.Nodes[node].Id, () => _state.Start = _state.Nodes[node].Id);
        return true;
    }

    // ---------------------------------------------------------------- nodes

    /// <summary>An empty id gets the next free "node-N".</summary>
    public int? AddNode(string id = "")
    {
        if (!_loaded || _state.Nodes.Count >= 4096 || (id.Length > 0 && (!ValidId(id) || Find(id) != null)))
        {
            return null;
        }
        if (id.Length == 0)
        {
            id = FreeNodeId("node");
        }
        Edit("Add node", () =>
        {
            // most of a conversation is one person talking
            string speaker = _state.Nodes.Count == 0 ? "" : _state.Nodes[^1].Speaker;
            _state.Nodes.Add(new Node { Id = id, Speaker = speaker });
        });
        return _state.Nodes.Count - 1;
    }

    /// <summary>Replies that led to it end the conversation instead. The last node can't go.</summary>
    public bool RemoveNode(int node)
    {
        if (!HasNode(node) || _state.Nodes.Count == 1)
        {
            return false;
        }
        string gone = _state.Nodes[node].Id;
        Edit("Remove node " + gone, () =>
        {
            _state.Nodes.RemoveAt(node);
            Retarget(gone, "");
            if (_state.Start == gone)
            {
                _state.Start = _state.Nodes[0].Id;
            }
        });
        return true;
    }

    /// <summary>Replies, checks and the start that named it follow the new id.</summary>
    public bool RenameNode(int node, string id)
    {
        if (!HasNode(node) || !ValidId(id) || Find(id) != null)
        {
            return false;
        }
        Edit("Rename node", () =>
        {
            string was = _state.Nodes[node].Id;
            _state.Nodes[node] = _state.Nodes[node] with { Id = id };
            if (_state.Start == was)
            {
                _state.Start = id;
            }
            Retarget(was, id);
        }, $"node-id-{node}");
        return true;
    }

    public bool SetSpeaker(int node, string speaker)
    {
        if (!HasNode(node) || speaker.Length > MaxSpeaker || _state.Nodes[node].Speaker == speaker)
        {
            return false;
        }
        Edit("Change speaker", () => _state.Nodes[node] = _state.Nodes[node] with { Speaker = speaker }, $"speaker-{node}");
        return true;
    }

    public bool SetText(int node, string text)
    {
        if (!HasNode(node) || text.Length > MaxLine || _state.Nodes[node].Text == text)
        {
            return false;
        }
        Edit("Change line", () => _state.Nodes[node] = _state.Nodes[node] with { Text = text }, $"line-{node}");
        return true;
    }

    public bool SetNodeFlags(int node, Flags flags)
    {
        if (!HasNode(node) || !ValidChanges(flags) || _state.Nodes[node].Flags.Same(flags))
        {
            return false;
        }
        Flags copy = flags.Copy();
        Edit("Change node flags", () => _state.Nodes[node] = _state.Nodes[node] with { Flags = copy }, $"node-flags-{node}");
        return true;
    }

    // ---------------------------------------------------------------- replies

    /// <summary>An empty text gets "..." so the file stays valid.</summary>
    public int? AddChoice(int node, string text = "")
    {
        if (!HasNode(node) || _state.Nodes[node].Choices.Count >= 128 || text.Length > MaxReply)
        {
            return null;
        }
        List<Choice> choices = _state.Nodes[node].Choices;
        string id = "";
        for (int n = choices.Count + 1; id.Length == 0; n++)
        {
            if (choices.All(c => c.Id != $"reply-{n}"))
            {
                id = $"reply-{n}";
            }
        }
        Edit("Add reply", () => _state.Nodes[node].Choices.Add(new Choice { Id = id, Text = text.Length == 0 ? "..." : text }));
        return _state.Nodes[node].Choices.Count - 1;
    }

    public bool RemoveChoice(int node, int choice)
    {
        if (ChoiceOf(node, choice) == null)
        {
            return false;
        }
        Edit("Remove reply", () => _state.Nodes[node].Choices.RemoveAt(choice));
        return true;
    }

    /// <summary>One place up (-1) or down (1).</summary>
    public bool MoveChoice(int node, int choice, int by)
    {
        if (ChoiceOf(node, choice) == null || (by != -1 && by != 1))
        {
            return false;
        }
        int to = choice + by;
        if (to < 0 || to >= _state.Nodes[node].Choices.Count)
        {
            return false;
        }
        Edit("Move reply", () =>
        {
            List<Choice> list = _state.Nodes[node].Choices;
            (list[choice], list[to]) = (list[to], list[choice]);
        });
        return true;
    }

    public bool SetChoiceId(int node, int choice, string id)
    {
        if (ChoiceOf(node, choice) == null || !ValidId(id) || _state.Nodes[node].Choices.Any(c => c.Id == id))
        {
            return false;
        }
        Change("Rename reply", node, choice, c => c with { Id = id }, $"reply-id-{node}-{choice}");
        return true;
    }

    public bool SetChoiceText(int node, int choice, string text)
    {
        if (ChoiceOf(node, choice) is not Choice c || text.Length == 0 || text.Length > MaxReply || c.Text == text)
        {
            return false;
        }
        Change("Change reply", node, choice, c => c with { Text = text }, $"reply-text-{node}-{choice}");
        return true;
    }

    /// <summary>A node id, or "" to end the conversation. Not for a reply with a check.</summary>
    public bool SetNext(int node, int choice, string next)
    {
        if (ChoiceOf(node, choice) is not Choice c || c.Check != null || c.Next == next || (next.Length > 0 && Find(next) == null))
        {
            return false;
        }
        Change(next.Length == 0 ? "Reply ends it" : "Reply goes to " + next, node, choice, c => c with { Next = next });
        return true;
    }

    /// <summary>A new node that the reply leads to (or the check's empty way), as one undo step.</summary>
    public int? Branch(int node, int choice)
    {
        if (ChoiceOf(node, choice) == null || _state.Nodes.Count >= 4096)
        {
            return null;
        }
        string id = FreeNodeId("node");
        Edit("Add node " + id, () =>
        {
            _state.Nodes.Add(new Node { Id = id, Speaker = _state.Nodes[node].Speaker });
            Choice c = _state.Nodes[node].Choices[choice];
            if (c.Check is not DialogueCheck check)
            {
                c = c with { Next = id };
            }
            else if (check.Success.Length == 0 || check.Failure.Length > 0)
            {
                c = c with { Check = check with { Success = id } };
            }
            else
            {
                c = c with { Check = check with { Failure = id } };
            }
            _state.Nodes[node].Choices[choice] = c;
        });
        return _state.Nodes.Count - 1;
    }

    public bool SetConditions(int node, int choice, IReadOnlyList<string> require, IReadOnlyList<string> forbid)
    {
        if (ChoiceOf(node, choice) is not Choice c || !ValidFlags(require) || !ValidFlags(forbid) || require.Intersect(forbid).Any()
            || (c.Require.SequenceEqual(require) && c.Forbid.SequenceEqual(forbid)))
        {
            return false;
        }
        List<string> needs = require.ToList(), hides = forbid.ToList();
        Change("Change reply conditions", node, choice, c => c with { Require = needs.ToList(), Forbid = hides.ToList() }, $"reply-conditions-{node}-{choice}");
        return true;
    }

    public bool SetChoiceFlags(int node, int choice, Flags flags)
    {
        if (ChoiceOf(node, choice) is not Choice c || !ValidChanges(flags) || c.Flags.Same(flags))
        {
            return false;
        }
        Flags copy = flags.Copy();
        Change("Change reply flags", node, choice, c => c with { Flags = copy.Copy() }, $"reply-flags-{node}-{choice}");
        return true;
    }

    /// <summary>Adding a check moves next to its success; taking it off moves the success back.</summary>
    public bool SetCheck(int node, int choice, DialogueCheck? check)
    {
        if (ChoiceOf(node, choice) is not Choice c || c.Check == check)
        {
            return false;
        }
        if (check != null && (check.Skill.Length == 0 || check.Difficulty < 0 || check.Difficulty > 100000
            || (check.Success.Length > 0 && Find(check.Success) == null) || (check.Failure.Length > 0 && Find(check.Failure) == null)))
        {
            return false;
        }
        bool had = c.Check != null;
        Change(check != null ? "Change check" : "Remove check", node, choice, c =>
        {
            if (check != null && c.Check == null)
            {
                return c with { Check = check.Success.Length == 0 ? check with { Success = c.Next } : check, Next = "" };
            }
            if (check == null)
            {
                return c with { Next = c.Check!.Success, Check = null }; // check == null here means it had one: equal ones returned above
            }
            return c with { Check = check };
        }, check != null && had ? $"check-{node}-{choice}" : "");
        return true;
    }

    /// <summary>Typing in a box is one undo step until this is called.</summary>
    public void EndTyping() => _history.BreakMerge();

    // ---------------------------------------------------------------- problems

    /// <summary>The "do" actions the game carries out (WorldTalk).</summary>
    public static IReadOnlyList<string> Actions { get; } = new[] { "recruit", "dismiss", "approve", "release", "kill", "fight" };

    /// <summary>What is wrong with an action: "" when nothing is, else why. error says if the game would do nothing with it.</summary>
    public static string ActionProblem(string action, Catalog catalog, out bool error)
    {
        bool wrong = true;
        string why = "";
        string[] w = action.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (action.Length > MaxAction)
        {
            why = "an action is at most 200 characters";
        }
        else if (w.Length == 0)
        {
            why = "an action can't be blank";
        }
        else if (w[0] is "release" or "kill" or "fight")
        {
            if (w.Length != 1)
            {
                why = w[0] + " takes nothing after it";
            }
        }
        else if (w[0] is "recruit" or "dismiss")
        {
            if (w.Length != 1)
            {
                why = w[0] + " takes nothing after it, it is always the one being talked to";
            }
            else if (!catalog.Companion)
            {
                wrong = false;
                why = w[0] + " only works for an NPC with a companion entry, and this file isn't one's";
            }
        }
        else if (w[0] == "approve")
        {
            if ((w.Length != 2 && w.Length != 3) || ApprovalChange(w[^1]) == null)
            {
                why = "approve takes a number, or a companion and a number: approve 5, approve wren -3";
            }
            else if (w.Length == 3 && !catalog.Companions.Contains(w[1]))
            {
                wrong = false;
                why = $"approve names {w[1]}, who isn't a companion in this chapter";
            }
            else if (w.Length == 2 && !catalog.Companion)
            {
                wrong = false;
                why = "approve without a name changes the one being talked to, who can't join here";
            }
        }
        else
        {
            wrong = false;
            why = $"the game does nothing with \"{action}\"";
        }
        error = why.Length > 0 && wrong;
        return why;
    }

    /// <summary>"+5", "-3" or "2", as the game reads them.</summary>
    public static int? ApprovalChange(string number)
    {
        if (number.StartsWith('+'))
        {
            number = number[1..];
        }
        if (number.Length == 0 || number.StartsWith('+'))
        {
            return null;
        }
        return int.TryParse(number, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out int value) ? value : null;
    }

    public List<Problem> Problems()
    {
        var found = new List<Problem>();
        if (!_loaded)
        {
            return found;
        }
        if (Dialogue(out string why) == null)
        {
            found.Add(new Problem("the game would refuse it: " + why));
        }

        // what the start can lead to; the rest is never heard
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var next = new Stack<string>();
        next.Push(_state.Start);
        while (next.Count > 0)
        {
            string id = next.Pop();
            if (Find(id) is not int at || !reached.Add(id))
            {
                continue;
            }
            foreach (Choice c in _state.Nodes[at].Choices)
            {
                next.Push(c.Check?.Success ?? c.Next);
                if (c.Check != null)
                {
                    next.Push(c.Check.Failure);
                }
            }
        }

        void ActionsOf(Flags flags, string where)
        {
            foreach (string action in flags.Actions)
            {
                string problem = ActionProblem(action, _catalog, out bool error);
                if (problem.Length > 0)
                {
                    found.Add(new Problem($"{where}: {problem}", error));
                }
            }
        }
        foreach (Node node in _state.Nodes)
        {
            if (!reached.Contains(node.Id))
            {
                found.Add(new Problem(node.Id + " can't be reached from the start", false));
            }
            if (node.Text.Length == 0)
            {
                found.Add(new Problem(node.Id + " has no line", false));
            }
            ActionsOf(node.Flags, node.Id);
            foreach (Choice c in node.Choices)
            {
                string where = node.Id + "/" + c.Id;
                ActionsOf(c.Flags, where);
                if (c.Check != null && _catalog.Skills.Count > 0 && !_catalog.Skills.Contains(c.Check.Skill))
                {
                    found.Add(new Problem($"{where} checks {c.Check.Skill}, which the ruleset doesn't have", false));
                }
            }
        }
        return found;
    }

    // ---------------------------------------------------------------- helpers

    // Names a file can set, clear, need or forbid: no blanks or repeats.
    private static bool ValidFlags(IReadOnlyList<string> flags) => flags.All(f => f.Length > 0 && f.Length <= MaxId) && flags.Distinct().Count() == flags.Count;

    private static bool ValidChanges(Flags flags)
    {
        return ValidFlags(flags.Set) && ValidFlags(flags.Clear) && !flags.Set.Intersect(flags.Clear).Any()
            && flags.Actions.All(a => a.Length > 0 && a.Length <= MaxAction);
    }

    private static Flags FlagsFrom(ContentNode j) => new() { Set = j.Texts("set"), Clear = j.Texts("clear"), Actions = j.Texts("do") };

    private static void WriteFlags(JsonObject j, Flags flags)
    {
        if (flags.Set.Count > 0)
        {
            j["set"] = CreateJson.Texts(flags.Set);
        }
        if (flags.Clear.Count > 0)
        {
            j["clear"] = CreateJson.Texts(flags.Clear);
        }
        if (flags.Actions.Count > 0)
        {
            j["do"] = CreateJson.Texts(flags.Actions);
        }
    }

    private bool HasNode(int node) => _loaded && node >= 0 && node < _state.Nodes.Count;

    private Choice? ChoiceOf(int node, int choice)
    {
        return HasNode(node) && choice >= 0 && choice < _state.Nodes[node].Choices.Count ? _state.Nodes[node].Choices[choice] : null;
    }

    private string FreeNodeId(string start)
    {
        for (int n = _state.Nodes.Count + 1; ; n++)
        {
            if (Find($"{start}-{n}") == null)
            {
                return $"{start}-{n}";
            }
        }
    }

    // Every reply and check that went to from goes to to instead ("" ends the conversation there).
    private void Retarget(string from, string to)
    {
        foreach (Node n in _state.Nodes)
        {
            for (int i = 0; i < n.Choices.Count; i++)
            {
                Choice c = n.Choices[i];
                if (c.Next == from)
                {
                    c = c with { Next = to };
                }
                if (c.Check is DialogueCheck check)
                {
                    c = c with { Check = check with { Success = check.Success == from ? to : check.Success, Failure = check.Failure == from ? to : check.Failure } };
                }
                n.Choices[i] = c;
            }
        }
    }

    private void Change(string label, int node, int choice, Func<Choice, Choice> change, string mergeKey = "")
    {
        Edit(label, () => _state.Nodes[node].Choices[choice] = change(_state.Nodes[node].Choices[choice]), mergeKey);
    }

    // Runs change and records it with what was there before and after.
    private void Edit(string label, Action change, string mergeKey = "")
    {
        State before = _state.Copy();
        change();
        State after = _state.Copy();
        _history.Record(label, () => _state = after.Copy(), () => _state = before.Copy(), mergeKey);
    }
}
