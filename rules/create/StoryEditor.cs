using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// Story mode of Create: the package's story.json, a graph of scene, encounter, dialogue, quest and
/// ending nodes with links and the writer's notes, and the commands on it. The game doesn't play
/// it; its nodes point at the chapters, fights, conversations, quests and endings that do.
/// Suggestions come from those files and from the graph, and the writer takes or turns down each
/// one. Fields it has no tool for are written back as they were. Every command goes on the history
/// it was given, which the whole open package shares.
/// </summary>
public sealed class StoryEditor
{
    public const int Format = 1;
    public const float NodeWidth = 190, NodeHeight = 52;
    private const int MaxName = 64, MaxTitle = 200, MaxText = 4000, MaxNodes = 2000, MaxSteps = 50, MaxXp = 1000000, MinMap = 8, MaxMap = 200;
    // a scene's proposed map: this size, and this much more for each fight linked from it
    public static readonly (int Width, int Height) MapBase = (24, 16), MapPerFight = (8, 4);

    public enum Kind
    {
        Scene,
        Encounter,
        Dialogue,
        Quest,
        Ending,
    }

    public static IReadOnlyList<Kind> Kinds { get; } = new[] { Kind.Scene, Kind.Encounter, Kind.Dialogue, Kind.Quest, Kind.Ending };

    public sealed record Node
    {
        public string Id { get; init; } = "";
        public Kind Kind { get; init; } = Kind.Scene;
        public string Title { get; init; } = "";
        /// <summary>The writer's notes.</summary>
        public string Text { get; init; } = "";
        /// <summary>Where it sits on the graph.</summary>
        public Vector2 At { get; init; }
        /// <summary>Chapter folder in the package; empty = not made yet.</summary>
        public string Chapter { get; init; } = "";
        /// <summary>What it stands for in that chapter: the group, the dialogue file, the quest id or the ending cutscene. Scenes have none.</summary>
        public string Ref { get; init; } = "";
        /// <summary>Encounter and quest.</summary>
        public int? Xp { get; init; }
        /// <summary>Quest.</summary>
        public List<string> Steps { get; init; } = new();
        /// <summary>A scene with no chapter yet: the map it should get; 0 = none.</summary>
        public int MapWidth { get; init; }
        public int MapHeight { get; init; }
        public string Extra { get; init; } = "";

        public Node Copy() => this with { Steps = Steps.ToList() };
    }

    public sealed record Link
    {
        public string From { get; init; } = "";
        public string To { get; init; } = "";
        /// <summary>What takes the story along it.</summary>
        public string Text { get; init; } = "";
        /// <summary>Flags it needs.</summary>
        public List<string> When { get; init; } = new();
        public string Extra { get; init; } = "";

        public Link Copy() => this with { When = When.ToList() };
    }

    /// <summary>What the package has for the graph to point at, read by CreatePackage.</summary>
    public sealed class Catalog
    {
        public sealed record Group(string Id, int Xp, int Creatures);

        public sealed record Quest(string Id, string Title);

        public sealed class Chapter
        {
            public string Folder = "";
            public string Id = "";
            public string Title = "";
            public List<Group> Groups = new();
            /// <summary>Package paths.</summary>
            public List<string> Dialogues = new();
            public List<Quest> Quests = new();
            /// <summary>The cutscene played when it is cleared; package path.</summary>
            public string Ending = "";
        }

        public List<Chapter> Chapters { get; init; } = new();
        /// <summary>There is an adventure.json.</summary>
        public bool Adventure { get; set; }
        /// <summary>Chapter ids, from and to.</summary>
        public List<(string From, string To)> Travel { get; init; } = new();

        public Chapter? ChapterOf(string folder) => Chapters.Find(c => c.Folder == folder);
    }

    public sealed record Suggestion(string Key, string Text);

    /// <summary>Error false: worth a look, but the file still saves.</summary>
    public sealed record Problem(string Text, bool Error = true);

    private sealed class State
    {
        public List<Node> Nodes = new();
        public List<Link> Links = new();
        public List<string> Dismissed = new();
        public string Extra = "";

        public State Copy() => new() { Nodes = Nodes.Select(n => n.Copy()).ToList(), Links = Links.Select(l => l.Copy()).ToList(), Dismissed = Dismissed.ToList(), Extra = Extra };
    }

    private readonly History _history;
    private bool _loaded;
    private State _state = new();
    private Catalog _catalog = new();

    public StoryEditor(History history)
    {
        _history = history;
    }

    public bool Loaded => _loaded;
    public IReadOnlyList<Node> Nodes => _state.Nodes;
    public IReadOnlyList<Link> Links => _state.Links;
    public IReadOnlyList<string> Dismissed => _state.Dismissed;
    public Catalog Names => _catalog;

    /// <summary>Not undone: it is what the files say, and it changes as the other modes save.</summary>
    public void SetCatalog(Catalog catalog) => _catalog = catalog;

    public static string KindName(Kind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>The field Ref is written as: "group", "dialogue", "quest", "cutscene"; empty for a scene.</summary>
    public static string RefField(Kind kind) => kind switch
    {
        Kind.Encounter => "group",
        Kind.Dialogue => "dialogue",
        Kind.Quest => "quest",
        Kind.Ending => "cutscene",
        _ => "",
    };

    public bool Load(string text, out string error)
    {
        error = "";
        bool Fail(string why, out string message)
        {
            message = why;
            return false;
        }
        try
        {
            ContentNode j = ContentNode.Parse("story.json", text);
            if (!j.IsObject)
            {
                return Fail("a story file is a JSON object", out error);
            }
            if (j.Get("format") is ContentNode format && (!format.IsWhole || format.AsInt() < 1))
            {
                return Fail("format has to be a whole number from 1", out error);
            }
            if (j.Int("format", Format) > Format)
            {
                return Fail($"made by a newer version of the game (format {j.Int("format", Format)})", out error);
            }
            var state = new State { Extra = CreateJson.ExtraOf(j, new[] { "format", "nodes", "links", "dismissed" }) };
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ContentNode n in j.Get("nodes")?.Items() ?? Array.Empty<ContentNode>())
            {
                if (!n.IsObject || n.Get("id") is not ContentNode idNode || !idNode.IsString)
                {
                    return Fail("every node needs an id", out error);
                }
                string id = idNode.AsText();
                if (!ContentIds.IsId(id))
                {
                    return Fail($"node id \"{id}\" has to be a-z, 0-9, - and _", out error);
                }
                if (!ids.Add(id))
                {
                    return Fail("two nodes are called " + id, out error);
                }
                if (KindFrom(n.Text("kind", "")) is not Kind kind)
                {
                    return Fail(id + ": kind has to be scene, encounter, dialogue, quest or ending", out error);
                }
                string title = n.Text("title", ""), notes = n.Text("text", "");
                if (title.Length > MaxTitle || notes.Length > MaxText)
                {
                    return Fail(id + ": the title or notes are too long", out error);
                }
                Vector2 at = default;
                if (n.Get("at") is ContentNode where)
                {
                    double[] xy = where.Items().Select(p => p.AsNumber()).ToArray();
                    if (xy.Length != 2)
                    {
                        return Fail(id + ": at is two numbers", out error);
                    }
                    at = new Vector2((float)xy[0], (float)xy[1]);
                }
                string field = RefField(kind);
                int? xp = null;
                if (n.Get("xp") is ContentNode x)
                {
                    if (!x.IsWhole || x.AsInt() < 0 || x.AsInt() > MaxXp)
                    {
                        return Fail(id + ": xp is a whole number from 0", out error);
                    }
                    xp = x.AsInt();
                }
                int width = 0, height = 0;
                if (n.Get("map") is ContentNode map)
                {
                    width = map.At("width").AsInt();
                    height = map.At("height").AsInt();
                    if (width < MinMap || height < MinMap || width > MaxMap || height > MaxMap)
                    {
                        return Fail(id + ": a map is 8 to 200 each way", out error);
                    }
                }
                var known = new List<string> { "id", "kind", "title", "text", "at", "chapter", "xp", "steps", "map" };
                if (field.Length > 0)
                {
                    known.Add(field);
                }
                state.Nodes.Add(new Node
                {
                    Id = id,
                    Kind = kind,
                    Title = title,
                    Text = notes,
                    At = at,
                    Chapter = n.Text("chapter", ""),
                    Ref = field.Length > 0 ? n.Text(field, "") : "",
                    Xp = xp,
                    Steps = n.Texts("steps"),
                    MapWidth = width,
                    MapHeight = height,
                    Extra = CreateJson.ExtraOf(n, known.ToArray()),
                });
            }
            var pairs = new HashSet<(string, string)>();
            foreach (ContentNode l in j.Get("links")?.Items() ?? Array.Empty<ContentNode>())
            {
                string from = l.At("from").AsText(), to = l.At("to").AsText();
                if (!ids.Contains(from) || !ids.Contains(to))
                {
                    return Fail($"a link from {from} to {to} names a node that isn't there", out error);
                }
                if (from == to || !pairs.Add((from, to)))
                {
                    return Fail($"the link from {from} to {to} is there twice or goes nowhere", out error);
                }
                List<string> when = l.Texts("when");
                if (!ValidFlags(when))
                {
                    return Fail($"the link from {from} to {to} has an empty or repeated flag", out error);
                }
                state.Links.Add(new Link { From = from, To = to, Text = l.Text("text", ""), When = when, Extra = CreateJson.ExtraOf(l, new[] { "from", "to", "text", "when" }) });
            }
            state.Dismissed = j.Texts("dismissed");
            _state = state;
            _loaded = true;
            return true;
        }
        catch (ContentException problem)
        {
            error = problem.Message;
            return false;
        }
    }

    /// <summary>An empty graph, for a package that has no story.json yet.</summary>
    public void Create()
    {
        _state = new State();
        _loaded = true;
    }

    public string ToJson()
    {
        if (!_loaded)
        {
            return "{}";
        }
        var j = new JsonObject { ["format"] = Format };
        var nodes = new JsonArray();
        foreach (Node node in _state.Nodes)
        {
            var n = new JsonObject { ["id"] = node.Id, ["kind"] = KindName(node.Kind), ["title"] = node.Title };
            if (node.Text.Length > 0)
            {
                n["text"] = node.Text;
            }
            n["at"] = new JsonArray((long)Math.Round(node.At.X, MidpointRounding.AwayFromZero), (long)Math.Round(node.At.Y, MidpointRounding.AwayFromZero));
            if (node.Chapter.Length > 0)
            {
                n["chapter"] = node.Chapter;
            }
            if (RefField(node.Kind).Length > 0 && node.Ref.Length > 0)
            {
                n[RefField(node.Kind)] = node.Ref;
            }
            if (node.Xp is int xp)
            {
                n["xp"] = xp;
            }
            if (node.Steps.Count > 0)
            {
                n["steps"] = CreateJson.Texts(node.Steps);
            }
            if (node.MapWidth > 0)
            {
                n["map"] = new JsonObject { ["width"] = node.MapWidth, ["height"] = node.MapHeight };
            }
            CreateJson.AddExtra(n, node.Extra);
            nodes.Add(n);
        }
        j["nodes"] = nodes;
        var links = new JsonArray();
        foreach (Link link in _state.Links)
        {
            var l = new JsonObject { ["from"] = link.From, ["to"] = link.To };
            if (link.Text.Length > 0)
            {
                l["text"] = link.Text;
            }
            if (link.When.Count > 0)
            {
                l["when"] = CreateJson.Texts(link.When);
            }
            CreateJson.AddExtra(l, link.Extra);
            links.Add(l);
        }
        j["links"] = links;
        if (_state.Dismissed.Count > 0)
        {
            j["dismissed"] = CreateJson.Texts(_state.Dismissed);
        }
        CreateJson.AddExtra(j, _state.Extra);
        return CreateJson.Write(j);
    }

    public int? Find(string node)
    {
        int at = _state.Nodes.FindIndex(n => n.Id == node);
        return at < 0 ? null : at;
    }

    public int? FindLink(string from, string to)
    {
        int at = _state.Links.FindIndex(l => l.From == from && l.To == to);
        return at < 0 ? null : at;
    }

    /// <summary>What a node is called on the graph and in messages.</summary>
    public static string NameOf(Node node) => node.Title.Length == 0 ? node.Id : node.Title;

    // ---------------------------------------------------------------- commands

    /// <summary>Ids are a-z, 0-9, - and _, unique in the graph. An empty id gets "kind-N".</summary>
    public int? AddNode(Kind kind, Vector2 at, string id = "")
    {
        if (!_loaded || _state.Nodes.Count >= MaxNodes || !float.IsFinite(at.X) || !float.IsFinite(at.Y))
        {
            return null;
        }
        if (id.Length == 0)
        {
            id = FreeId(KindName(kind));
        }
        if (!ContentIds.IsId(id) || Find(id) != null)
        {
            return null;
        }
        Edit("Add " + KindName(kind), () => _state.Nodes.Add(new Node { Id = id, Kind = kind, At = at }));
        return _state.Nodes.Count - 1;
    }

    /// <summary>Its links go with it.</summary>
    public bool RemoveNode(int node)
    {
        if (!HasNode(node))
        {
            return false;
        }
        Edit("Remove " + NameOf(_state.Nodes[node]), () =>
        {
            string id = _state.Nodes[node].Id;
            _state.Links.RemoveAll(l => l.From == id || l.To == id);
            _state.Nodes.RemoveAt(node);
        });
        return true;
    }

    /// <summary>Links follow the new id.</summary>
    public bool RenameNode(int node, string id)
    {
        if (!HasNode(node) || !ContentIds.IsId(id))
        {
            return false;
        }
        if (_state.Nodes[node].Id == id)
        {
            return true;
        }
        if (Find(id) != null)
        {
            return false;
        }
        Edit("Rename node", () =>
        {
            string old = _state.Nodes[node].Id;
            for (int i = 0; i < _state.Links.Count; i++)
            {
                Link l = _state.Links[i];
                _state.Links[i] = l with { From = l.From == old ? id : l.From, To = l.To == old ? id : l.To };
            }
            _state.Nodes[node] = _state.Nodes[node] with { Id = id };
        }, $"id:{node}");
        return true;
    }

    /// <summary>A drag is one undo step until EndTyping.</summary>
    public bool MoveNode(int node, Vector2 at)
    {
        if (!HasNode(node) || !float.IsFinite(at.X) || !float.IsFinite(at.Y))
        {
            return false;
        }
        at = new Vector2(MathF.Round(at.X), MathF.Round(at.Y));
        if (_state.Nodes[node].At != at)
        {
            Edit("Move " + NameOf(_state.Nodes[node]), () => _state.Nodes[node] = _state.Nodes[node] with { At = at }, $"move:{node}");
        }
        return true;
    }

    /// <summary>Changing the kind clears what it pointed at, since a group isn't a quest.</summary>
    public bool SetKind(int node, Kind kind)
    {
        if (!HasNode(node))
        {
            return false;
        }
        if (_state.Nodes[node].Kind == kind)
        {
            return true;
        }
        Edit("Make it a " + KindName(kind), () =>
        {
            Node n = _state.Nodes[node];
            _state.Nodes[node] = n with
            {
                Kind = kind,
                Ref = "",
                Xp = kind is Kind.Encounter or Kind.Quest ? n.Xp : null,
                Steps = kind == Kind.Quest ? n.Steps : new List<string>(),
                MapWidth = kind == Kind.Scene ? n.MapWidth : 0,
                MapHeight = kind == Kind.Scene ? n.MapHeight : 0,
            };
        });
        return true;
    }

    public bool SetTitle(int node, string title)
    {
        if (!HasNode(node) || title.Length > MaxTitle)
        {
            return false;
        }
        if (_state.Nodes[node].Title != title)
        {
            Edit("Title", () => _state.Nodes[node] = _state.Nodes[node] with { Title = title }, $"title:{node}");
        }
        return true;
    }

    public bool SetText(int node, string text)
    {
        if (!HasNode(node) || text.Length > MaxText)
        {
            return false;
        }
        if (_state.Nodes[node].Text != text)
        {
            Edit("Notes", () => _state.Nodes[node] = _state.Nodes[node] with { Text = text }, $"text:{node}");
        }
        return true;
    }

    /// <summary>A chapter folder the catalog has, or "". A new chapter clears Ref.</summary>
    public bool SetChapter(int node, string chapter)
    {
        if (!HasNode(node) || (chapter.Length > 0 && _catalog.ChapterOf(chapter) == null))
        {
            return false;
        }
        if (_state.Nodes[node].Chapter == chapter)
        {
            return true;
        }
        Edit("Chapter", () =>
        {
            Node n = _state.Nodes[node];
            // a scene that has its chapter has its map too
            _state.Nodes[node] = n with { Chapter = chapter, Ref = "", MapWidth = chapter.Length > 0 ? 0 : n.MapWidth, MapHeight = chapter.Length > 0 ? 0 : n.MapHeight };
        });
        return true;
    }

    public bool SetRef(int node, string reference)
    {
        if (!HasNode(node) || RefField(_state.Nodes[node].Kind).Length == 0 || reference.Length > 400)
        {
            return false;
        }
        if (_state.Nodes[node].Ref != reference)
        {
            Edit(RefField(_state.Nodes[node].Kind), () => _state.Nodes[node] = _state.Nodes[node] with { Ref = reference });
        }
        return true;
    }

    public bool SetXp(int node, int? xp)
    {
        if (!HasNode(node) || xp is < 0 or > MaxXp)
        {
            return false;
        }
        if (xp != null && _state.Nodes[node].Kind is not (Kind.Encounter or Kind.Quest))
        {
            return false;
        }
        if (_state.Nodes[node].Xp != xp)
        {
            Edit("XP", () => _state.Nodes[node] = _state.Nodes[node] with { Xp = xp }, $"xp:{node}");
        }
        return true;
    }

    public bool SetSteps(int node, IReadOnlyList<string> steps)
    {
        if (!HasNode(node) || _state.Nodes[node].Kind != Kind.Quest || steps.Count > MaxSteps || steps.Any(s => s.Length == 0 || s.Length > MaxTitle))
        {
            return false;
        }
        if (!_state.Nodes[node].Steps.SequenceEqual(steps))
        {
            List<string> copy = steps.ToList();
            Edit("Quest steps", () => _state.Nodes[node] = _state.Nodes[node] with { Steps = copy.ToList() }, $"steps:{node}");
        }
        return true;
    }

    /// <summary>0 by 0 for none; otherwise 8 to 200 each way.</summary>
    public bool SetMapSize(int node, int width, int height)
    {
        if (!HasNode(node) || _state.Nodes[node].Kind != Kind.Scene)
        {
            return false;
        }
        bool none = width == 0 && height == 0;
        if (!none && (width < MinMap || height < MinMap || width > MaxMap || height > MaxMap))
        {
            return false;
        }
        if (_state.Nodes[node].MapWidth != width || _state.Nodes[node].MapHeight != height)
        {
            Edit("Map size", () => _state.Nodes[node] = _state.Nodes[node] with { MapWidth = width, MapHeight = height }, $"map:{node}");
        }
        return true;
    }

    /// <summary>Not to itself, and only one link each way between two nodes.</summary>
    public int? AddLink(int from, int to)
    {
        if (!HasNode(from) || !HasNode(to) || from == to)
        {
            return null;
        }
        string a = _state.Nodes[from].Id, b = _state.Nodes[to].Id;
        if (FindLink(a, b) != null)
        {
            return null;
        }
        Edit($"Link {NameOf(_state.Nodes[from])} to {NameOf(_state.Nodes[to])}", () => _state.Links.Add(new Link { From = a, To = b }));
        return _state.Links.Count - 1;
    }

    public bool RemoveLink(int link)
    {
        if (!HasLink(link))
        {
            return false;
        }
        Edit("Remove link", () => _state.Links.RemoveAt(link));
        return true;
    }

    public bool SetLinkText(int link, string text)
    {
        if (!HasLink(link) || text.Length > MaxTitle)
        {
            return false;
        }
        if (_state.Links[link].Text != text)
        {
            Edit("Link text", () => _state.Links[link] = _state.Links[link] with { Text = text }, $"link:{link}");
        }
        return true;
    }

    public bool SetLinkWhen(int link, IReadOnlyList<string> when)
    {
        if (!HasLink(link) || !ValidFlags(when))
        {
            return false;
        }
        if (!_state.Links[link].When.SequenceEqual(when))
        {
            List<string> copy = when.ToList();
            Edit("Link flags", () => _state.Links[link] = _state.Links[link] with { When = copy.ToList() }, $"when:{link}");
        }
        return true;
    }

    public void EndTyping() => _history.BreakMerge();

    /// <summary>The options Ref has for a node, from the catalog and its chapter.</summary>
    public List<string> RefOptions(int node)
    {
        var found = new List<string>();
        if (!HasNode(node))
        {
            return found;
        }
        Node n = _state.Nodes[node];
        void Add(string option)
        {
            if (option.Length > 0 && !found.Contains(option))
            {
                found.Add(option);
            }
        }
        foreach (Catalog.Chapter c in _catalog.Chapters.Where(c => n.Chapter.Length == 0 || c.Folder == n.Chapter))
        {
            switch (n.Kind)
            {
                case Kind.Encounter:
                    c.Groups.ForEach(g => Add(g.Id));
                    break;
                case Kind.Dialogue:
                    c.Dialogues.ForEach(Add);
                    break;
                case Kind.Quest:
                    c.Quests.ForEach(q => Add(q.Id));
                    break;
                case Kind.Ending:
                    Add(c.Ending);
                    break;
            }
        }
        return found;
    }

    // ---------------------------------------------------------------- suggestions

    /// <summary>What the files and the graph suggest, without the ones turned down.</summary>
    public List<Suggestion> Suggestions()
    {
        var found = new List<Suggestion>();
        if (!_loaded)
        {
            return found;
        }
        void Offer(string key, string text)
        {
            if (!_state.Dismissed.Contains(key))
            {
                found.Add(new Suggestion(key, text));
            }
        }
        bool Has(Kind kind, string chapter, string reference) => _state.Nodes.Any(n => n.Kind == kind && n.Ref == reference && (kind == Kind.Dialogue || n.Chapter == chapter));

        // what the package has that the graph doesn't show yet
        var dialogues = new HashSet<string>(StringComparer.Ordinal);
        foreach (Catalog.Chapter c in _catalog.Chapters)
        {
            string name = c.Title.Length == 0 ? c.Id : c.Title;
            if (SceneOf(c.Folder) == null)
            {
                Offer("scene|" + c.Folder, "Add a scene for " + name);
            }
            foreach (Catalog.Group g in c.Groups.Where(g => !Has(Kind.Encounter, c.Folder, g.Id)))
            {
                Offer($"encounter|{c.Folder}|{g.Id}", $"Add the fight {g.Id} in {name}");
            }
            foreach (string d in c.Dialogues)
            {
                if (dialogues.Add(d) && !Has(Kind.Dialogue, c.Folder, d))
                {
                    Offer($"dialogue|{c.Folder}|{d}", "Add the conversation " + ContentFiles.Stem(d));
                }
            }
            foreach (Catalog.Quest q in c.Quests.Where(q => !Has(Kind.Quest, c.Folder, q.Id)))
            {
                Offer($"quest|{c.Folder}|{q.Id}", "Add the quest " + (q.Title.Length == 0 ? q.Id : q.Title));
            }
            if (c.Ending.Length > 0 && !Has(Kind.Ending, c.Folder, c.Ending))
            {
                Offer("ending|" + c.Folder, "Add the ending of " + name);
            }
        }

        // what the graph itself proposes
        for (int i = 0; i < _state.Nodes.Count; i++)
        {
            Node n = _state.Nodes[i];
            Catalog.Chapter? chapter = _catalog.ChapterOf(n.Chapter);
            if (n.Kind == Kind.Encounter && chapter != null)
            {
                foreach (Catalog.Group g in chapter.Groups.Where(g => g.Id == n.Ref && g.Creatures > 0 && n.Xp != g.Xp))
                {
                    Offer($"xp|{n.Id}|{g.Xp}", $"XP for {NameOf(n)}: {g.Xp} from creature levels");
                }
            }
            if (n.Kind == Kind.Quest && n.Steps.Count == 0)
            {
                int after = _state.Links.Count(l => l.From == n.Id);
                if (after > 0)
                {
                    Offer("steps|" + n.Id, $"Steps for {NameOf(n)} from the {after} {(after == 1 ? "node" : "nodes")} after it");
                }
            }
            if (n.Kind == Kind.Scene && n.Chapter.Length == 0 && n.MapWidth == 0)
            {
                (int w, int h) = MapFor(i);
                Offer("map|" + n.Id, $"Map for {NameOf(n)}: {w} by {h}");
            }
        }
        return found;
    }

    public bool Accept(string key)
    {
        if (!_loaded || Suggestions().Find(s => s.Key == key) is not Suggestion found)
        {
            return false;
        }
        return Take(found);
    }

    /// <summary>Every suggestion shown now, as one undo step. How many were taken.</summary>
    public int AcceptAll()
    {
        if (!_loaded)
        {
            return 0;
        }
        int taken = 0;
        _history.BeginGroup("Take all suggestions");
        // taking some brings up others (a fight added shows its XP), so a few rounds
        for (int round = 0; round < 4; round++)
        {
            List<Suggestion> shown = Suggestions();
            if (shown.Count == 0)
            {
                break;
            }
            taken += shown.Count(Take);
        }
        _history.EndGroup();
        return taken;
    }

    /// <summary>Turned down for good: saved in the file, so it doesn't come back.</summary>
    public bool Dismiss(string key)
    {
        if (!_loaded || Suggestions().All(s => s.Key != key))
        {
            return false;
        }
        Edit("Turn down a suggestion", () => _state.Dismissed.Add(key));
        return true;
    }

    public bool RestoreDismissed()
    {
        if (!_loaded || _state.Dismissed.Count == 0)
        {
            return false;
        }
        Edit("Bring back suggestions", () => _state.Dismissed.Clear());
        return true;
    }

    public List<Problem> Problems()
    {
        var found = new List<Problem>();
        if (!_loaded)
        {
            return found;
        }
        void Warn(string text) => found.Add(new Problem(text, false));
        var linked = new HashSet<string>(_state.Links.SelectMany(l => new[] { l.From, l.To }), StringComparer.Ordinal);
        for (int i = 0; i < _state.Nodes.Count; i++)
        {
            Node n = _state.Nodes[i];
            if (n.Title.Length == 0)
            {
                Warn(n.Id + " has no title");
            }
            if (n.Chapter.Length > 0 && _catalog.ChapterOf(n.Chapter) == null)
            {
                Warn($"{n.Id}: the package has no chapter {n.Chapter}");
            }
            else if (n.Ref.Length > 0 && !RefOptions(i).Contains(n.Ref))
            {
                Warn($"{n.Id}: {RefField(n.Kind)} {n.Ref}" + (n.Chapter.Length == 0 ? " isn't in the package" : $" isn't in {n.Chapter}"));
            }
            if (_state.Nodes.Count > 1 && !linked.Contains(n.Id))
            {
                Warn(n.Id + " isn't linked to anything");
            }
            if (n.Kind == Kind.Ending && _state.Links.Find(l => l.From == n.Id) is Link onward)
            {
                Warn($"{n.Id} is an ending but the story goes on to {onward.To}");
            }
        }
        // a link between two scenes is travel, and the game only travels where adventure.json says
        if (_catalog.Adventure)
        {
            foreach (Link l in _state.Links)
            {
                Node a = _state.Nodes[Find(l.From)!.Value], b = _state.Nodes[Find(l.To)!.Value]; // links only name nodes that are there
                Catalog.Chapter? from = _catalog.ChapterOf(a.Chapter), to = _catalog.ChapterOf(b.Chapter);
                if (a.Kind != Kind.Scene || b.Kind != Kind.Scene || from == null || to == null || from == to)
                {
                    continue;
                }
                if (!_catalog.Travel.Contains((from.Id, to.Id)))
                {
                    Warn($"adventure.json has no way from {from.Id} to {to.Id} for the link {l.From} to {l.To}");
                }
            }
        }
        return found;
    }

    // ---------------------------------------------------------------- helpers

    private static Kind? KindFrom(string name) => Kinds.Cast<Kind?>().FirstOrDefault(k => KindName(k!.Value) == name);

    private static bool ValidFlags(IReadOnlyList<string> flags) => flags.All(f => f.Length > 0 && f.Length <= MaxName) && flags.Distinct().Count() == flags.Count;

    // An id made from any text: lowercase, with - for anything else.
    private static string IdFrom(string text)
    {
        var id = new System.Text.StringBuilder();
        foreach (char c in text)
        {
            char lower = c is >= 'A' and <= 'Z' ? (char)(c - 'A' + 'a') : c;
            if (lower is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
            {
                id.Append(lower);
            }
            else if (id.Length > 0 && id[^1] != '-')
            {
                id.Append('-');
            }
        }
        string made = id.ToString().TrimEnd('-');
        return made.Length > MaxName - 4 ? made[..(MaxName - 4)] : made;
    }

    private bool HasNode(int node) => _loaded && node >= 0 && node < _state.Nodes.Count;

    private bool HasLink(int link) => _loaded && link >= 0 && link < _state.Links.Count;

    private string FreeId(string start)
    {
        if (start.Length == 0)
        {
            start = "node";
        }
        if (Find(start) == null)
        {
            return start;
        }
        for (int n = 2; ; n++)
        {
            if (Find($"{start}-{n}") == null)
            {
                return $"{start}-{n}";
            }
        }
    }

    // The node standing for this chapter's scene.
    private int? SceneOf(string chapter)
    {
        if (chapter.Length == 0)
        {
            return null;
        }
        int at = _state.Nodes.FindIndex(n => n.Kind == Kind.Scene && n.Chapter == chapter);
        return at < 0 ? null : at;
    }

    // Where a node added for chapter goes: in a column beside that chapter's scene, or in a new column.
    private Vector2 PlaceFor(string chapter)
    {
        float down = NodeHeight + 22;
        if (SceneOf(chapter) is int scene)
        {
            // to the right of the scene, so each link from it can be told apart
            Vector2 top = _state.Nodes[scene].At;
            float x = top.X + NodeWidth + 60;
            float? bottom = null;
            foreach (Node node in _state.Nodes.Where(n => Math.Abs(n.At.X - x) < NodeWidth && n.At.Y > top.Y - NodeHeight))
            {
                bottom = Math.Max(bottom ?? node.At.Y, node.At.Y);
            }
            return new Vector2(x, bottom is float b ? b + down : top.Y);
        }
        // a new column to the right of everything
        return new Vector2(_state.Nodes.Count == 0 ? 0 : _state.Nodes.Max(n => n.At.X) + NodeWidth + 50, 0);
    }

    // The map a scene with no chapter is offered: a room to start in, and more room for each fight that happens there.
    private (int Width, int Height) MapFor(int node)
    {
        int fights = _state.Links.Count(l => l.From == _state.Nodes[node].Id && Find(l.To) is int to && _state.Nodes[to].Kind == Kind.Encounter);
        return (Math.Min(MaxMap, MapBase.Width + MapPerFight.Width * fights), Math.Min(MaxMap, MapBase.Height + MapPerFight.Height * fights));
    }

    // Applies one suggestion to the state, with no history.
    private bool Apply(string key)
    {
        string[] part = key.Split('|');
        string what = part[0];
        void LinkFromScene(string chapter, string to)
        {
            if (SceneOf(chapter) is int scene && FindLink(_state.Nodes[scene].Id, to) == null)
            {
                _state.Links.Add(new Link { From = _state.Nodes[scene].Id, To = to });
            }
        }
        bool AddFor(Kind kind, string chapter, string reference, string title, string start)
        {
            if (_state.Nodes.Count >= MaxNodes)
            {
                return false;
            }
            string id = FreeId(IdFrom(start).Length == 0 ? KindName(kind) : IdFrom(start));
            var node = new Node { Kind = kind, Id = id, Title = title.Length > MaxTitle ? title[..MaxTitle] : title, Chapter = chapter, Ref = reference, At = PlaceFor(chapter) };
            _state.Nodes.Add(node);
            if (kind != Kind.Scene)
            {
                LinkFromScene(chapter, id);
            }
            return true;
        }

        if (what == "scene" && part.Length == 2)
        {
            if (_catalog.ChapterOf(part[1]) is not Catalog.Chapter c || SceneOf(c.Folder) != null)
            {
                return false;
            }
            if (!AddFor(Kind.Scene, c.Folder, "", c.Title.Length == 0 ? c.Id : c.Title, c.Id))
            {
                return false;
            }
            string id = _state.Nodes[^1].Id;
            // travel in adventure.json becomes links between the scenes it joins
            foreach ((string from, string to) in _catalog.Travel)
            {
                foreach (Catalog.Chapter other in _catalog.Chapters)
                {
                    if (other.Folder == c.Folder || SceneOf(other.Folder) is not int scene)
                    {
                        continue;
                    }
                    string otherId = _state.Nodes[scene].Id;
                    if (from == c.Id && to == other.Id && FindLink(id, otherId) == null)
                    {
                        _state.Links.Add(new Link { From = id, To = otherId });
                    }
                    if (from == other.Id && to == c.Id && FindLink(otherId, id) == null)
                    {
                        _state.Links.Add(new Link { From = otherId, To = id });
                    }
                }
            }
            return true;
        }
        if (what == "encounter" && part.Length == 3)
        {
            return _catalog.ChapterOf(part[1]) != null && AddFor(Kind.Encounter, part[1], part[2], part[2], part[2]);
        }
        if (what == "dialogue" && part.Length == 3)
        {
            return AddFor(Kind.Dialogue, part[1], part[2], ContentFiles.Stem(part[2]), ContentFiles.Stem(part[2]));
        }
        if (what == "quest" && part.Length == 3)
        {
            return _catalog.ChapterOf(part[1]) is Catalog.Chapter c && c.Quests.Find(q => q.Id == part[2]) is Catalog.Quest q
                && AddFor(Kind.Quest, c.Folder, q.Id, q.Title.Length == 0 ? q.Id : q.Title, q.Id);
        }
        if (what == "ending" && part.Length == 2)
        {
            return _catalog.ChapterOf(part[1]) is Catalog.Chapter c && c.Ending.Length > 0 && AddFor(Kind.Ending, c.Folder, c.Ending, "Ending", c.Id + "-ending");
        }

        if (part.Length < 2 || Find(part[1]) is not int node)
        {
            return false;
        }
        Node n = _state.Nodes[node];
        if (what == "xp" && part.Length == 3 && n.Kind == Kind.Encounter)
        {
            if (!int.TryParse(part[2], NumberStyles.None, CultureInfo.InvariantCulture, out int xp) || xp > MaxXp)
            {
                return false;
            }
            _state.Nodes[node] = n with { Xp = xp };
            return true;
        }
        if (what == "steps" && n.Kind == Kind.Quest)
        {
            var steps = new List<string>();
            foreach (Link l in _state.Links.Where(l => l.From == n.Id))
            {
                if (steps.Count < MaxSteps && Find(l.To) is int to)
                {
                    string name = NameOf(_state.Nodes[to]);
                    steps.Add(name.Length > MaxTitle ? name[..MaxTitle] : name);
                }
            }
            if (steps.Count == 0)
            {
                return false;
            }
            _state.Nodes[node] = n with { Steps = steps };
            return true;
        }
        if (what == "map" && n.Kind == Kind.Scene && n.Chapter.Length == 0)
        {
            (int w, int h) = MapFor(node);
            _state.Nodes[node] = n with { MapWidth = w, MapHeight = h };
            return true;
        }
        return false;
    }

    // Apply as one undo step; nothing is recorded if it can't be done.
    private bool Take(Suggestion suggestion)
    {
        State before = _state.Copy();
        if (!Apply(suggestion.Key))
        {
            _state = before;
            return false;
        }
        State after = _state.Copy();
        _history.Record(suggestion.Text, () => _state = after.Copy(), () => _state = before.Copy());
        return true;
    }

    private void Edit(string label, Action change, string mergeKey = "")
    {
        State before = _state.Copy();
        change();
        State after = _state.Copy();
        _history.Record(label, () => _state = after.Copy(), () => _state = before.Copy(), mergeKey);
    }
}
