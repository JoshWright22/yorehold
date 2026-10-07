using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// Stage four of story import: an outline into a package's normal content files. Entries already
/// have the files' shapes, so most of the work is writing each one where it goes and turning ids
/// into paths. The rest is the map: rooms are laid side by side on the grid from the places and
/// their links, walls round each, a door kit where a link crosses a wall, and the fights, chests,
/// people and party set inside their rooms. The same outline always gives the same files.
/// What the game can't play yet goes to import/report.json and onto the story graph.
/// </summary>
public sealed class OutlineBuilder
{
    /// <summary>One line of import/report.json: what happened to an entry that didn't go in as written.</summary>
    public sealed record ReportLine(string Entry, string Text);

    public const string ReportFile = "report.json";
    public const int XpPerVictory = 50;

    private readonly Outline _source;
    // the outline being built: the source with its system's numbers made the game's
    private Outline _outline;
    private readonly string _importFolder;
    private readonly ContentFiles _game;
    private readonly Compendium _gameCompendium = new();
    private readonly List<ReportLine> _report = new();
    // outline creature and item ids to the ids the package uses (the game's own when it has one by that name)
    private readonly Dictionary<string, string> _ids = new(StringComparer.Ordinal);
    // the book's pictures in use: where each goes in the package, and its cleared bytes when its paper was taken out
    private readonly SortedDictionary<string, (string Target, byte[]? Cleared)> _pictures = new(StringComparer.Ordinal);
    private string _package = "";

    public OutlineBuilder(Outline outline, string importFolder, ContentFiles game)
    {
        _source = outline;
        _outline = outline;
        _importFolder = importFolder;
        _game = game;
        _gameCompendium.Load(game, "");
    }

    public IReadOnlyList<ReportLine> Report => _report;

    /// <summary>Make the plain paper round a figure (a hero, creature or person) see-through. On by default.</summary>
    public bool ClearPaper { get; init; } = true;

    /// <summary>
    /// Writes the package into folder (made if missing) and checks it loads. Problems are what
    /// stopped it: names the game doesn't know, or the built package failing its own check.
    /// </summary>
    public List<string> Build(string folder)
    {
        _package = folder;
        _report.Clear();
        _outline = _source;
        if (_outline.System.Length > 0)
        {
            // the book's numbers become the game's before anything is written
            try
            {
                SystemTable table = SystemTable.Load(_game, _outline.System);
                Ruleset rules = RulesFolder.Load(_game).Rules;
                _outline = table.Apply(_outline, s => rules.Skill(s) != null || rules.Ability(s) != null, _report);
            }
            catch (ContentException error)
            {
                return new List<string> { $"the book's system \"{_outline.System}\": {error.Message}" };
            }
        }
        List<string> problems = _outline.Check(_gameCompendium);
        if (problems.Count > 0)
        {
            return problems;
        }
        Directory.CreateDirectory(folder);
        MatchGameEntries();
        WriteDefinitions();

        List<OutlineEntry> chapters = _outline.OfKind(OutlineKind.Chapter).ToList();
        var chapterIds = chapters.Count > 0 ? chapters.Select(c => c.Id).ToList() : new List<string> { "chapter-one" };
        var transitions = new JsonArray();
        var layouts = new Dictionary<string, Layout>();
        foreach (string chapter in chapterIds)
        {
            layouts[chapter] = new Layout(PlacesOf(chapter), LinksWithin(chapter), _report);
        }
        foreach (OutlineEntry link in _outline.OfKind(OutlineKind.Link))
        {
            string from = ChapterOf(link.Text("from")), to = ChapterOf(link.Text("to"));
            if (from != to)
            {
                // a way out of one chapter into another: a marker on each side, joined by a transition
                layouts[from].Exits.Add(("exit-" + link.Id, link.Text("from")));
                layouts[to].Exits.Add(("entry-" + link.Id, link.Text("to")));
                transitions.Add(new JsonObject { ["from"] = from, ["exitMarker"] = "exit-" + link.Id, ["to"] = to, ["entryMarker"] = "entry-" + link.Id });
            }
        }
        foreach (string chapter in chapterIds)
        {
            WriteChapter(chapter, chapters.FirstOrDefault(c => c.Id == chapter), layouts[chapter]);
        }
        WriteManifests(chapterIds, transitions);
        WriteStory(chapterIds);
        CopyPictures();
        WriteReport();
        return Check(chapterIds);
    }

    // ---------------------------------------------------------------- creatures, items, pictures

    private void MatchGameEntries()
    {
        foreach (OutlineEntry e in _outline.OfKind(OutlineKind.Creature))
        {
            string name = e.Text("name", e.Id);
            CreatureDefinition? same = _gameCompendium.Creatures.Values.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
            _ids[e.Id] = same?.Id ?? e.Id;
            if (same != null && e.Picture.Length == 0)
            {
                _report.Add(new ReportLine(e.Id, $"uses the game's own {same.Name} ({same.Id}) in place of the book's numbers"));
            }
        }
        foreach (OutlineEntry e in _outline.OfKind(OutlineKind.Item))
        {
            string name = e.Text("name", e.Id);
            ItemDefinition? same = _gameCompendium.Items.Values.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
            _ids[e.Id] = same?.Id ?? e.Id;
            if (same != null)
            {
                _report.Add(new ReportLine(e.Id, $"uses the game's own {same.Name} ({same.Id})"));
            }
        }
    }

    private string IdOf(string id) => _ids.GetValueOrDefault(id, id);

    private string Picture(OutlineEntry entry)
    {
        if (entry.Picture.Length == 0)
        {
            return "";
        }
        string source = Path.Combine(_importFolder, entry.Picture.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(source))
        {
            _report.Add(new ReportLine(entry.Id, $"its picture {entry.Picture} is not in the import folder"));
            return "";
        }
        if (_pictures.TryGetValue(entry.Picture, out (string Target, byte[]? Cleared) known))
        {
            return known.Target;
        }
        byte[]? cleared = ClearPaper && entry.Kind is OutlineKind.Hero or OutlineKind.Creature or OutlineKind.Npc
            ? PaperGround.Clear(File.ReadAllBytes(source))
            : null;
        string target = cleared != null ? Path.ChangeExtension(entry.Picture, ".png") : entry.Picture;
        _pictures[entry.Picture] = (target, cleared);
        return target;
    }

    private void WriteDefinitions()
    {
        foreach (OutlineEntry e in _outline.OfKind(OutlineKind.Creature))
        {
            string picture = Picture(e);
            if (IdOf(e.Id) != e.Id && picture.Length == 0)
            {
                continue;
            }
            // a game creature with the book's picture is written as the book's, so the picture has somewhere to go
            _ids[e.Id] = e.Id;
            JsonObject data = Copy(e.Data);
            data["id"] = e.Id;
            if (picture.Length > 0)
            {
                var token = data["token"] as JsonObject ?? new JsonObject();
                token["image"] = picture;
                data["token"] = token;
            }
            WriteJson($"creatures/{e.Id}.json", data);
        }
        foreach (OutlineEntry e in _outline.OfKind(OutlineKind.Item).Where(e => IdOf(e.Id) == e.Id))
        {
            JsonObject data = Copy(e.Data);
            data["id"] = e.Id;
            WriteJson($"items/{e.Id}.json", data);
        }
    }

    // ---------------------------------------------------------------- chapters

    private string FirstChapter => _outline.OfKind(OutlineKind.Chapter).FirstOrDefault()?.Id ?? "chapter-one";

    private string ChapterOf(string placeId)
    {
        OutlineEntry? place = _outline.Find(placeId);
        return place == null || place.Chapter.Length == 0 ? FirstChapter : place.Chapter;
    }

    private string EntryChapter(OutlineEntry e) => e.Text("place") is { Length: > 0 } place ? ChapterOf(place) : e.Chapter.Length > 0 ? e.Chapter : FirstChapter;

    private List<OutlineEntry> PlacesOf(string chapter) => _outline.OfKind(OutlineKind.Place).Where(p => ChapterOf(p.Id) == chapter).ToList();

    private List<OutlineEntry> LinksWithin(string chapter) => _outline.OfKind(OutlineKind.Link)
        .Where(l => ChapterOf(l.Text("from")) == chapter && ChapterOf(l.Text("to")) == chapter).ToList();

    private void WriteChapter(string id, OutlineEntry? chapter, Layout layout)
    {
        string folder = "chapters/" + id;
        var j = new JsonObject
        {
            ["id"] = id,
            ["title"] = chapter?.Text("title") is { Length: > 0 } title ? title : _outline.Title,
            ["map"] = "map.json",
        };
        var intro = chapter?.Texts("intro") ?? new List<string>();
        if (layout.Start is OutlineEntry start)
        {
            // the first room's passage is read as the party arrives
            intro.AddRange(start.Texts("readAloud").Where(t => !intro.Contains(t)));
        }
        j["intro"] = Texts(intro);
        j["xpPerVictory"] = XpPerVictory;
        if (chapter?.Data["level"] is JsonNode level)
        {
            j["level"] = level.DeepClone();
        }
        if (chapter?.Texts("completeWhen") is { Count: > 0 } complete)
        {
            j["completeWhen"] = Texts(complete);
        }

        var party = new JsonArray();
        List<OutlineEntry> heroes = _outline.OfKind(OutlineKind.Hero).Take(4).ToList();
        foreach (OutlineEntry hero in _outline.OfKind(OutlineKind.Hero).Skip(4))
        {
            _report.Add(new ReportLine(hero.Id, "a party has at most four seats; left out"));
        }
        foreach (OutlineEntry hero in heroes)
        {
            var seat = new JsonObject { ["name"] = hero.Text("name"), ["class"] = hero.Text("class") };
            if (hero.Data["color"] is JsonNode color)
            {
                seat["color"] = color.DeepClone();
            }
            seat["at"] = Cell(layout.Take(layout.Start?.Id ?? ""));
            string picture = hero.Text("image") is { Length: > 0 } image ? image : Picture(hero);
            if (picture.Length > 0)
            {
                seat["image"] = picture;
            }
            party.Add(seat);
        }
        if (heroes.Count == 0)
        {
            party.Add(new JsonObject { ["name"] = "Hero", ["class"] = "fighter", ["at"] = Cell(layout.Take(layout.Start?.Id ?? "")) });
            _report.Add(new ReportLine("", "the book names no heroes; one fighter seat stands in"));
        }
        j["party"] = party;

        var npcs = new JsonArray();
        var dialogues = new SortedSet<string>(StringComparer.Ordinal);
        foreach (OutlineEntry npc in _outline.OfKind(OutlineKind.Npc).Where(n => EntryChapter(n) == id))
        {
            var n = new JsonObject { ["id"] = npc.Id, ["name"] = npc.Text("name") };
            string creature = npc.Text("creature") is { Length: > 0 } c ? IdOf(c) : "commoner";
            if (Picture(npc) is { Length: > 0 } face)
            {
                // the picture needs a creature to carry it: the NPC's own, made from the one it names
                creature = NpcCreature(npc, creature, face);
            }
            n["creature"] = creature;
            if (npc.Data["color"] is JsonNode color)
            {
                n["color"] = color.DeepClone();
            }
            n["at"] = Cell(layout.Take(npc.Text("place")));
            string talk = npc.Text("dialogue");
            if (talk.Length == 0)
            {
                talk = npc.Id + "-says";
                WriteJson($"{folder}/dialogue/{talk}.json", new JsonObject
                {
                    ["id"] = talk,
                    ["start"] = "hello",
                    ["nodes"] = new JsonArray(new JsonObject { ["id"] = "hello", ["speaker"] = npc.Text("name"), ["text"] = "..." }),
                });
                _report.Add(new ReportLine(npc.Id, "has nothing to say in the book; a one-line conversation stands in"));
            }
            else
            {
                dialogues.Add(talk);
            }
            n["dialogue"] = $"dialogue/{talk}.json";
            if (npc.Data["merchant"] is JsonNode merchant)
            {
                n["merchant"] = merchant.DeepClone();
            }
            npcs.Add(n);
        }
        j["npcs"] = npcs;

        var containers = new JsonArray();
        foreach (OutlineEntry chest in _outline.OfKind(OutlineKind.Container).Where(c => EntryChapter(c) == id))
        {
            Cell at = layout.Take(chest.Text("place"));
            List<string> items = chest.Texts("items").Select(IdOf).ToList();
            int coins = chest.Data["coins"]?.GetValue<int>() ?? 0;
            if (chest.Data["locked"]?.GetValue<bool>() == true || chest.Text("key").Length > 0 || chest.Data["check"] != null)
            {
                // a locked chest is a map object: a lid that opens with the key or the check
                var contents = new JsonObject();
                foreach (string item in items)
                {
                    contents[item] = (contents[item]?.GetValue<int>() ?? 0) + 1;
                }
                if (coins > 0)
                {
                    contents["coins"] = coins;
                }
                var lid = new JsonObject { ["kit"] = "locked-chest", ["at"] = Cell(at), ["name"] = chest.Text("name", "Chest"), ["contents"] = contents };
                LockOf(chest, lid);
                layout.Objects.Add(lid);
                continue;
            }
            var c = new JsonObject { ["id"] = chest.Id, ["name"] = chest.Text("name", "Chest"), ["at"] = Cell(at), ["items"] = Texts(items) };
            if (coins > 0)
            {
                c["coins"] = coins;
            }
            containers.Add(c);
        }
        j["containers"] = containers;

        var encounters = new JsonArray();
        foreach (OutlineEntry fight in _outline.OfKind(OutlineKind.Encounter).Where(f => EntryChapter(f) == id))
        {
            var creatures = new JsonArray();
            foreach (JsonNode? c in fight.Data["creatures"] as JsonArray ?? new JsonArray())
            {
                string creature = IdOf(c!["creature"]!.GetValue<string>());
                string name = c["name"]?.GetValue<string>() ?? "";
                int count = c["count"]?.GetValue<int>() ?? 1;
                for (int i = 0; i < count; i++)
                {
                    var placed = new JsonObject { ["creature"] = creature, ["at"] = Cell(layout.TakeFar(fight.Text("place"))) };
                    if (name.Length > 0)
                    {
                        placed["name"] = count > 1 ? $"{name} {i + 1}" : name;
                    }
                    creatures.Add(placed);
                }
            }
            var group = new JsonObject { ["id"] = fight.Id, ["creatures"] = creatures };
            if (fight.Text("text") is { Length: > 0 } text)
            {
                group["text"] = text;
            }
            if (fight.Texts("set") is { Count: > 0 } set)
            {
                group["set"] = Texts(set);
            }
            encounters.Add(group);
        }
        j["encounters"] = encounters;

        var triggers = new JsonArray();
        // a room's passage is read out the first time a hero steps in: an area sets a flag, a trigger on it shows the words
        foreach (OutlineEntry place in PlacesOf(id).Where(p => p != layout.Start && p.Texts("readAloud").Count > 0))
        {
            string flag = "entered_" + place.Id.Replace('-', '_');
            (int x, int y, int w, int h) = layout.Room(place.Id);
            layout.Areas.Add(new JsonObject { ["id"] = "room-" + place.Id, ["area"] = new JsonArray(x, y, w, h), ["set"] = new JsonArray(flag) });
            string talk = "read-" + place.Id;
            WriteJson($"{folder}/dialogue/{talk}.json", new JsonObject
            {
                ["id"] = talk,
                ["start"] = "read",
                ["nodes"] = new JsonArray(new JsonObject { ["id"] = "read", ["speaker"] = place.Text("name"), ["text"] = string.Join("\n\n", place.Texts("readAloud")) }),
            });
            triggers.Add(new JsonObject { ["id"] = talk, ["when"] = new JsonArray(flag), ["dialogue"] = $"dialogue/{talk}.json" });
        }
        foreach (OutlineEntry trigger in _outline.OfKind(OutlineKind.Trigger).Where(t => EntryChapter(t) == id))
        {
            dialogues.Add(trigger.Text("dialogue"));
            var t = new JsonObject { ["id"] = trigger.Id, ["dialogue"] = $"dialogue/{trigger.Text("dialogue")}.json" };
            if (trigger.Texts("when") is { Count: > 0 } when)
            {
                t["when"] = Texts(when);
            }
            triggers.Add(t);
        }
        if (triggers.Count > 0)
        {
            j["triggers"] = triggers;
        }
        // conversations no one starts are kept with the first chapter, for the writer to hook up
        if (id == FirstChapter)
        {
            foreach (OutlineEntry talk in _outline.OfKind(OutlineKind.Dialogue).Where(d => !IsStarted(d.Id)))
            {
                dialogues.Add(talk.Id);
                _report.Add(new ReportLine(talk.Id, "nobody starts this conversation yet"));
            }
        }
        foreach (string talk in dialogues)
        {
            JsonObject data = Copy(_outline.Find(talk)!.Data);
            data["id"] = talk;
            WriteJson($"{folder}/dialogue/{talk}.json", data);
        }

        List<OutlineEntry> quests = _outline.OfKind(OutlineKind.Quest).Where(q => (q.Chapter.Length > 0 ? q.Chapter : FirstChapter) == id).ToList();
        if (quests.Count > 0)
        {
            var list = new JsonArray();
            foreach (OutlineEntry quest in quests)
            {
                JsonObject data = Copy(quest.Data);
                data["id"] = quest.Id;
                list.Add(data);
            }
            WriteJson($"{folder}/quests.json", new JsonObject { ["quests"] = list });
            j["quests"] = "quests.json";
        }
        WriteJson($"{folder}/chapter.json", j);
        WriteJson($"{folder}/map.json", layout.Map(j["title"]!.GetValue<string>()));
        foreach (string lost in layout.Unreached())
        {
            _report.Add(new ReportLine(lost, "no walk from the start reaches it"));
        }
    }

    private bool IsStarted(string dialogue) =>
        _outline.Entries.Any(e => e.Kind is OutlineKind.Npc or OutlineKind.Trigger && e.Text("dialogue") == dialogue);

    private string NpcCreature(OutlineEntry npc, string creature, string face)
    {
        JsonObject data;
        if (_outline.Find(creature) is { Kind: OutlineKind.Creature } own)
        {
            data = Copy(own.Data);
        }
        else
        {
            data = (JsonObject)JsonNode.Parse(_game.ReadText($"creatures/{creature}.json"))!;
        }
        data["id"] = npc.Id;
        data["name"] = npc.Text("name");
        var token = data["token"] as JsonObject ?? new JsonObject();
        token["image"] = face;
        data["token"] = token;
        WriteJson($"creatures/{npc.Id}.json", data);
        return npc.Id;
    }

    // a link's or chest's key and check, on a door or lid object
    private void LockOf(OutlineEntry entry, JsonObject thing)
    {
        if (entry.Text("key") is { Length: > 0 } key)
        {
            thing["tags"] = new JsonArray("key:" + IdOf(key));
        }
        if (entry.Data["check"] is JsonObject check)
        {
            thing["lock"] = new JsonObject { ["dc"] = check["difficulty"]!.DeepClone(), ["skill"] = check["skill"]!.DeepClone() };
        }
        else
        {
            thing["lock"] = new JsonObject { ["dc"] = 0 };
        }
    }

    // ---------------------------------------------------------------- manifests, story graph, report

    private void WriteManifests(List<string> chapters, JsonArray transitions)
    {
        OutlineEntry? adventure = _outline.OfKind(OutlineKind.Adventure).FirstOrDefault();
        string title = adventure?.Text("title") is { Length: > 0 } t ? t : _outline.Title.Length > 0 ? _outline.Title : "Imported adventure";
        string id = Slug(title);
        var folders = new JsonArray(chapters.Select(c => (JsonNode?)("chapters/" + c)).ToArray());
        WriteJson("content.json", new JsonObject
        {
            ["format"] = "yorehold.content",
            ["version"] = 1,
            ["name"] = title,
            ["kind"] = "adventure",
            ["id"] = id,
            ["revision"] = 1,
            ["ruleset"] = "yorehold@1.0",
            ["requires"] = new JsonArray(),
            ["defaultChapter"] = "chapters/" + chapters[0],
            ["chapters"] = folders,
        });
        var manifest = new JsonObject
        {
            ["format"] = "yorehold.adventure",
            ["version"] = 1,
            ["id"] = id,
            ["title"] = title,
            ["description"] = adventure?.Text("description") ?? "",
            ["chapters"] = folders.DeepClone(),
            ["transitions"] = transitions,
        };
        if (adventure?.Data["level"] is JsonNode level)
        {
            manifest["minLevel"] = level.DeepClone();
        }
        WriteJson("adventure.json", manifest);
    }

    private void WriteStory(List<string> chapters)
    {
        var nodes = new JsonArray();
        var links = new JsonArray();
        for (int c = 0; c < chapters.Count; c++)
        {
            string chapter = chapters[c];
            string scene = "scene-" + chapter;
            // what the game can't play yet is kept on the chapter's scene, so the writer sees it next to the rest
            var notes = new List<string>();
            foreach (OutlineEntry note in _outline.OfKind(OutlineKind.Note).Where(n => EntryChapter(n) == chapter))
            {
                notes.Add(note.Text("why") is { Length: > 0 } why ? $"{note.Text("text")} ({why})" : note.Text("text"));
            }
            nodes.Add(new JsonObject
            {
                ["id"] = scene, ["kind"] = "scene", ["title"] = _outline.Find(chapter)?.Text("title") is { Length: > 0 } t ? t : chapter,
                ["text"] = string.Join("\n\n", notes), ["at"] = new JsonArray(c * 480, 0), ["chapter"] = "chapters/" + chapter,
            });
            if (c > 0)
            {
                links.Add(new JsonObject { ["from"] = "scene-" + chapters[c - 1], ["to"] = scene });
            }
            int row = 1;
            foreach (OutlineEntry fight in _outline.OfKind(OutlineKind.Encounter).Where(f => EntryChapter(f) == chapter))
            {
                nodes.Add(new JsonObject { ["id"] = "fight-" + fight.Id, ["kind"] = "encounter", ["title"] = Label(_outline.Find(fight.Text("place"))!),
                    ["at"] = new JsonArray(c * 480, row++ * 90), ["chapter"] = "chapters/" + chapter, ["group"] = fight.Id });
                links.Add(new JsonObject { ["from"] = scene, ["to"] = "fight-" + fight.Id });
            }
            foreach (OutlineEntry npc in _outline.OfKind(OutlineKind.Npc).Where(n => EntryChapter(n) == chapter && n.Text("dialogue").Length > 0))
            {
                nodes.Add(new JsonObject { ["id"] = "talk-" + npc.Id, ["kind"] = "dialogue", ["title"] = npc.Text("name"),
                    ["at"] = new JsonArray(c * 480 + 220, row++ * 90), ["chapter"] = "chapters/" + chapter, ["dialogue"] = $"dialogue/{npc.Text("dialogue")}.json" });
                links.Add(new JsonObject { ["from"] = scene, ["to"] = "talk-" + npc.Id });
            }
            foreach (OutlineEntry quest in _outline.OfKind(OutlineKind.Quest).Where(q => (q.Chapter.Length > 0 ? q.Chapter : FirstChapter) == chapter))
            {
                nodes.Add(new JsonObject { ["id"] = "quest-" + quest.Id, ["kind"] = "quest", ["title"] = quest.Text("title"),
                    ["at"] = new JsonArray(c * 480 + 220, row++ * 90), ["chapter"] = "chapters/" + chapter, ["quest"] = quest.Id });
                links.Add(new JsonObject { ["from"] = scene, ["to"] = "quest-" + quest.Id });
            }
        }
        WriteJson("story.json", new JsonObject { ["format"] = StoryEditor.Format, ["nodes"] = nodes, ["links"] = links });
    }

    private static string Label(OutlineEntry place) => place.Text("label") is { Length: > 0 } label ? $"{label}: {place.Text("name")}" : place.Text("name");

    private void WriteReport()
    {
        foreach (OutlineEntry note in _outline.OfKind(OutlineKind.Note))
        {
            _report.Add(new ReportLine(note.Id, note.Text("why") is { Length: > 0 } why ? $"{note.Text("text")} ({why})" : note.Text("text")));
        }
        var lines = new JsonArray();
        foreach (ReportLine line in _report)
        {
            lines.Add(new JsonObject { ["entry"] = line.Entry, ["text"] = line.Text });
        }
        Directory.CreateDirectory(_importFolder);
        File.WriteAllText(Path.Combine(_importFolder, ReportFile),
            CreateJson.Write(new JsonObject { ["format"] = "yorehold.import-report", ["version"] = 1, ["lines"] = lines }) + "\n");
    }

    private void CopyPictures()
    {
        foreach ((string picture, (string to, byte[]? cleared)) in _pictures)
        {
            string target = Path.Combine(_package, to.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (cleared != null)
            {
                File.WriteAllBytes(target, cleared);
            }
            else
            {
                File.Copy(Path.Combine(_importFolder, picture.Replace('/', Path.DirectorySeparatorChar)), target, true);
            }
        }
    }

    private List<string> Check(List<string> chapters)
    {
        var problems = new List<string>();
        var play = new ContentFiles();
        foreach (string root in new[] { GameRoot(), _package })
        {
            play.Add(root);
        }
        try
        {
            ContentPackage.Load(new ContentFiles(_package)).Validate(play, new ContentFiles(_package));
            foreach (string chapter in chapters)
            {
                Chapter.Load(play, "chapters/" + chapter);
            }
        }
        catch (ContentException error)
        {
            problems.Add(error.Message);
        }
        return problems;
    }

    private string GameRoot() => _game.FullPath("content.json") is string manifest ? Path.GetDirectoryName(manifest)! : throw new ContentException("content.json", "", "the game's content folder has no manifest");

    // ---------------------------------------------------------------- writing

    private void WriteJson(string path, JsonNode node)
    {
        string file = Path.Combine(_package, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, CreateJson.Write(node) + "\n");
    }

    private static JsonObject Copy(JsonObject data) => (JsonObject)data.DeepClone();

    private static JsonArray Texts(IEnumerable<string> texts) => new(texts.Select(t => (JsonNode?)t).ToArray());

    private static JsonArray Cell(Cell at) => new(at.X, at.Y);

    public static string Slug(string title)
    {
        var slug = new string(title.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray());
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        slug = slug.Trim('-');
        return slug.Length == 0 ? "imported" : slug.Length > 60 ? slug[..60].TrimEnd('-') : slug;
    }

    /// <summary>
    /// One chapter's rooms on the grid. The first place sits at the top left; each link from a
    /// placed room puts the next one beside it (east, south, west, then north) with one wall
    /// between, and the link becomes an opening or a door in that wall. A place no link reaches
    /// is joined to the one before it by an open way, and the report says so.
    /// </summary>
    private sealed class Layout
    {
        private const char Floor = '.', Grass = ',', Wall = '#', Tree = 'T', Empty = ' ';

        private readonly List<OutlineEntry> _places;
        private readonly Dictionary<string, (int X, int Y, int W, int H)> _rooms = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Cell>> _free = new(StringComparer.Ordinal);
        private readonly List<(Cell At, char Tile)> _openings = new();
        private readonly HashSet<Cell> _taken = new();
        private int _width, _height;

        public Layout(List<OutlineEntry> places, List<OutlineEntry> links, List<ReportLine> report)
        {
            _places = places;
            Start = places.FirstOrDefault();
            if (Start == null)
            {
                // a chapter with no places still needs ground to stand on
                var hall = new OutlineEntry { Id = "start", Kind = OutlineKind.Place, Data = new JsonObject { ["name"] = "Start", ["size"] = new JsonArray(8, 6) } };
                _places.Add(hall);
                Start = hall;
            }
            Place(Start, 1, 1);
            var pending = new List<OutlineEntry>(links);
            bool progress = true;
            while (progress)
            {
                progress = false;
                foreach (OutlineEntry link in pending.ToList())
                {
                    string a = link.Text("from"), b = link.Text("to");
                    if (_rooms.ContainsKey(a) == _rooms.ContainsKey(b))
                    {
                        if (_rooms.ContainsKey(a))
                        {
                            Join(link, a, b, report);
                            pending.Remove(link);
                        }
                        continue;
                    }
                    (string from, string to) = _rooms.ContainsKey(a) ? (a, b) : (b, a);
                    PlaceBeside(from, _places.First(p => p.Id == to));
                    Join(link, from, to, report);
                    pending.Remove(link);
                    progress = true;
                }
            }
            foreach (OutlineEntry link in pending)
            {
                report.Add(new ReportLine(link.Id, "joins places in no room of this chapter; left out"));
            }
            OutlineEntry previous = Start;
            foreach (OutlineEntry place in _places.Where(p => !_rooms.ContainsKey(p.Id)).ToList())
            {
                PlaceBeside(previous.Id, place);
                var link = new OutlineEntry { Id = $"{previous.Id}-{place.Id}", Kind = OutlineKind.Link, Data = new JsonObject { ["from"] = previous.Id, ["to"] = place.Id, ["way"] = "open" } };
                Join(link, previous.Id, place.Id, report);
                report.Add(new ReportLine(place.Id, $"no way in is given; joined to {previous.Id} by an open way"));
                previous = place;
            }
            Normalize();
        }

        // rooms put west or north of the first can sit before 1,1: move everything so the map starts at 0,0
        private void Normalize()
        {
            int left = _rooms.Values.Min(r => r.X) - 1, top = _rooms.Values.Min(r => r.Y) - 1;
            foreach ((Cell at, char _) in _openings)
            {
                left = Math.Min(left, at.X - 1);
                top = Math.Min(top, at.Y - 1);
            }
            int dx = -left, dy = -top;
            foreach (string id in _rooms.Keys.ToList())
            {
                (int x, int y, int w, int h) = _rooms[id];
                _rooms[id] = (x + dx, y + dy, w, h);
                _free[id] = _free[id].Select(c => new Cell(c.X + dx, c.Y + dy)).ToList();
            }
            for (int i = 0; i < _openings.Count; i++)
            {
                _openings[i] = (new Cell(_openings[i].At.X + dx, _openings[i].At.Y + dy), _openings[i].Tile);
            }
            foreach (JsonObject o in Objects)
            {
                var at = (JsonArray)o["at"]!;
                o["at"] = new JsonArray(at[0]!.GetValue<int>() + dx, at[1]!.GetValue<int>() + dy);
            }
            _width = Math.Max(_rooms.Values.Max(r => r.X + r.W), _openings.Count > 0 ? _openings.Max(o => o.At.X) + 1 : 0) + 1;
            _height = Math.Max(_rooms.Values.Max(r => r.Y + r.H), _openings.Count > 0 ? _openings.Max(o => o.At.Y) + 1 : 0) + 1;
        }

        public OutlineEntry? Start { get; }
        public List<JsonObject> Objects { get; } = new();
        public List<JsonObject> Areas { get; } = new();

        /// <summary>A place's floor, in the map's squares.</summary>
        public (int X, int Y, int W, int H) Room(string place) => _rooms[place];
        public List<(string Marker, string Place)> Exits { get; } = new();

        private static (int W, int H) SizeOf(OutlineEntry place)
        {
            var size = place.Data["size"] as JsonArray;
            return (size?[0]?.GetValue<int>() ?? 6, size?[1]?.GetValue<int>() ?? 6);
        }

        private static bool Outdoors(OutlineEntry place) => place.Data["outdoors"]?.GetValue<bool>() == true;

        private void Place(OutlineEntry place, int x, int y)
        {
            (int w, int h) = SizeOf(place);
            _rooms[place.Id] = (x, y, w, h);
            var cells = new List<Cell>();
            for (int cy = y; cy < y + h; cy++)
            {
                for (int cx = x; cx < x + w; cx++)
                {
                    cells.Add(new Cell(cx, cy));
                }
            }
            // the middle first: who stands where fills out from the room's centre
            var middle = new Cell(x + w / 2, y + h / 2);
            _free[place.Id] = cells.OrderBy(c => Math.Abs(c.X - middle.X) + Math.Abs(c.Y - middle.Y)).ThenBy(c => c.Y).ThenBy(c => c.X).ToList();
        }

        private bool Fits(int x, int y, int w, int h)
        {
            // rooms keep one wall between them
            return _rooms.Values.All(r => x - 1 >= r.X + r.W || r.X - 1 >= x + w || y - 1 >= r.Y + r.H || r.Y - 1 >= y + h);
        }

        private void PlaceBeside(string fromId, OutlineEntry place)
        {
            (int x, int y, int w, int h) = _rooms[fromId];
            (int pw, int ph) = SizeOf(place);
            var tries = new List<(int X, int Y)>();
            // flush with the room's top or left edge first, then slid along its side
            for (int slide = 0; slide < Math.Max(h, w) + Math.Max(pw, ph); slide++)
            {
                foreach (int s in slide == 0 ? new[] { 0 } : new[] { slide, -slide })
                {
                    if (y + s + ph > y && y + s < y + h)
                    {
                        tries.Add((x + w + 1, y + s));
                    }
                    if (x + s + pw > x && x + s < x + w)
                    {
                        tries.Add((x + s, y + h + 1));
                    }
                    if (y + s + ph > y && y + s < y + h)
                    {
                        tries.Add((x - 1 - pw, y + s));
                    }
                    if (x + s + pw > x && x + s < x + w)
                    {
                        tries.Add((x + s, y - 1 - ph));
                    }
                }
            }
            foreach ((int tx, int ty) in tries)
            {
                if (Fits(tx, ty, pw, ph))
                {
                    Place(place, tx, ty);
                    return;
                }
            }
            // nowhere beside it: below everything, and the way between is cut through as a corridor
            int bottom = _rooms.Values.Max(r => r.Y + r.H) + 2;
            Place(place, x, bottom);
        }

        // the wall cell between two rooms where the link crosses, or a corridor when they don't touch
        private void Join(OutlineEntry link, string a, string b, List<ReportLine> report)
        {
            (int ax, int ay, int aw, int ah) = _rooms[a];
            (int bx, int by, int bw, int bh) = _rooms[b];
            Cell? door = null;
            if (bx == ax + aw + 1 || ax == bx + bw + 1)
            {
                int top = Math.Max(ay, by), end = Math.Min(ay + ah, by + bh);
                if (top < end)
                {
                    door = new Cell(bx == ax + aw + 1 ? ax + aw : bx + bw, (top + end - 1) / 2);
                }
            }
            if (door == null && (by == ay + ah + 1 || ay == by + bh + 1))
            {
                int left = Math.Max(ax, bx), end = Math.Min(ax + aw, bx + bw);
                if (left < end)
                {
                    door = new Cell((left + end - 1) / 2, by == ay + ah + 1 ? ay + ah : by + bh);
                }
            }
            OutlineEntry from = _places.First(p => p.Id == a);
            char ground = Outdoors(from) ? Grass : Floor;
            if (door is not Cell at)
            {
                // a straight run down from a's middle, then across to b's: floor through anything in the way
                var start = new Cell(ax + aw / 2, ay + ah / 2);
                var end = new Cell(bx + bw / 2, by + bh / 2);
                for (int y = Math.Min(start.Y, end.Y); y <= Math.Max(start.Y, end.Y); y++)
                {
                    _openings.Add((new Cell(start.X, y), ground));
                }
                for (int x = Math.Min(start.X, end.X); x <= Math.Max(start.X, end.X); x++)
                {
                    _openings.Add((new Cell(x, end.Y), ground));
                }
                report.Add(new ReportLine(link.Id, "the two rooms couldn't sit side by side; a corridor joins them"));
                return;
            }
            _openings.Add((at, ground));
            string way = link.Text("way", "open");
            switch (way)
            {
                case "door":
                case "secret":
                    Objects.Add(new JsonObject { ["kit"] = "door", ["at"] = new JsonArray(at.X, at.Y) });
                    if (way == "secret")
                    {
                        report.Add(new ReportLine(link.Id, "a secret door is played as a plain door; finding it isn't in the game yet"));
                    }
                    break;
                case "locked":
                    var lockedDoor = new JsonObject { ["kit"] = "locked-door", ["at"] = new JsonArray(at.X, at.Y) };
                    if (link.Text("key") is { Length: > 0 } key)
                    {
                        lockedDoor["tags"] = new JsonArray("key:" + key);
                    }
                    lockedDoor["lock"] = link.Data["check"] is JsonObject check
                        ? new JsonObject { ["dc"] = check["difficulty"]!.DeepClone(), ["skill"] = check["skill"]!.DeepClone() }
                        : new JsonObject { ["dc"] = 0 };
                    Objects.Add(lockedDoor);
                    break;
                case "climb":
                case "jump":
                    report.Add(new ReportLine(link.Id, $"a {way} is played as an open way; the game can't play a {way} yet"));
                    break;
            }
        }

        /// <summary>A free square in the place, from its middle out.</summary>
        public Cell Take(string place)
        {
            List<Cell> free = _free.GetValueOrDefault(place) ?? _free[Start!.Id];
            Cell at = free.First(c => !_taken.Contains(c));
            _taken.Add(at);
            return at;
        }

        /// <summary>A free square in the place, from its far side in: where foes wait.</summary>
        public Cell TakeFar(string place)
        {
            List<Cell> free = _free.GetValueOrDefault(place) ?? _free[Start!.Id];
            Cell at = free.Last(c => !_taken.Contains(c));
            _taken.Add(at);
            return at;
        }

        /// <summary>Places no walk from the start reaches (doors count as passable: they open).</summary>
        public List<string> Unreached()
        {
            char[,] grid = Grid();
            var seen = new HashSet<Cell>();
            var queue = new Queue<Cell>();
            (int sx, int sy, int sw, int sh) = _rooms[Start!.Id];
            queue.Enqueue(new Cell(sx + sw / 2, sy + sh / 2));
            while (queue.Count > 0)
            {
                Cell at = queue.Dequeue();
                if (at.X < 0 || at.Y < 0 || at.X >= _width || at.Y >= _height || grid[at.Y, at.X] is not (Floor or Grass) || !seen.Add(at))
                {
                    continue;
                }
                queue.Enqueue(new Cell(at.X + 1, at.Y));
                queue.Enqueue(new Cell(at.X - 1, at.Y));
                queue.Enqueue(new Cell(at.X, at.Y + 1));
                queue.Enqueue(new Cell(at.X, at.Y - 1));
            }
            return _rooms.Where(r => !seen.Contains(new Cell(r.Value.X, r.Value.Y))).Select(r => r.Key).ToList();
        }

        public JsonObject Map(string name)
        {
            char[,] grid = Grid();
            var rows = new JsonArray();
            for (int y = 0; y < _height; y++)
            {
                var row = new char[_width];
                for (int x = 0; x < _width; x++)
                {
                    row[x] = grid[y, x];
                }
                rows.Add(new string(row));
            }
            return Finish(name, rows);
        }

        private char[,] Grid()
        {
            var grid = new char[_height, _width];
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    grid[y, x] = Empty;
                }
            }
            foreach (OutlineEntry place in _places)
            {
                (int x, int y, int w, int h) = _rooms[place.Id];
                bool outdoors = Outdoors(place);
                for (int cy = y - 1; cy <= y + h; cy++)
                {
                    for (int cx = x - 1; cx <= x + w; cx++)
                    {
                        bool edge = cx == x - 1 || cy == y - 1 || cx == x + w || cy == y + h;
                        char now = grid[cy, cx];
                        // a wall shared with a room drawn before stays as that room drew it
                        grid[cy, cx] = edge ? (now == Empty ? (outdoors ? Tree : Wall) : now) : (outdoors ? Grass : Floor);
                    }
                }
            }
            foreach ((Cell at, char tile) in _openings)
            {
                grid[at.Y, at.X] = tile;
            }
            return grid;
        }

        private JsonObject Finish(string name, JsonArray rows)
        {
            var objects = new JsonArray(Objects.Select(o => (JsonNode?)o.DeepClone()).ToArray());
            var markers = new JsonObject();
            (int sx, int sy, int sw, int sh) = _rooms[Start!.Id];
            markers["start"] = new JsonArray(sx + sw / 2, sy + sh / 2);
            foreach ((string marker, string place) in Exits)
            {
                Cell at = Take(place);
                markers[marker] = new JsonArray(at.X, at.Y);
            }
            return new JsonObject
            {
                ["name"] = name,
                ["tiles"] = new JsonObject
                {
                    ["stone"] = new JsonObject { ["art"] = "stone" },
                    ["grass"] = new JsonObject { ["art"] = "grass" },
                    ["wall"] = new JsonObject { ["art"] = "wall", ["walkable"] = false, ["blocksSight"] = true },
                    ["tree"] = new JsonObject { ["art"] = "tree", ["walkable"] = false, ["blocksSight"] = true },
                },
                ["legend"] = new JsonObject { ["."] = "stone", [","] = "grass", ["#"] = "wall", ["T"] = "tree" },
                ["layers"] = new JsonArray(new JsonObject { ["name"] = "ground", ["rows"] = rows }),
                ["objects"] = objects,
                ["markers"] = markers,
                ["areas"] = new JsonArray(Areas.Select(a => (JsonNode?)a.DeepClone()).ToArray()),
            };
        }
    }
}
