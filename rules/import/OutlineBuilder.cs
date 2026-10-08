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
    // where each place's floor was put, for the report: the import's score holds it against the book's map
    private readonly SortedDictionary<string, (string Chapter, (int X, int Y, int W, int H) At)> _rooms = new(StringComparer.Ordinal);
    // the square each place is centred on, and whether its walls are the book's map's
    private readonly Dictionary<string, (Cell Spot, bool Drawn)> _spots = new(StringComparer.Ordinal);
    private string _package = "";
    // the rules system the adventure is built for: its ruleset folder and the id it gives itself
    private readonly string _system;
    private string _systemId = "yorehold";

    // the plainest class the system has, for a seat the book names no class for
    private string DefaultClass() =>
        _gameCompendium.Class("fighter") != null ? "fighter" : _gameCompendium.Classes.Keys.FirstOrDefault() ?? "fighter";

    public OutlineBuilder(Outline outline, string importFolder, ContentFiles game, string system = RulesFolder.Default)
    {
        _source = outline;
        _outline = outline;
        _importFolder = importFolder;
        _game = game;
        _system = system;
        _gameCompendium.Load(game, system, "");
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
        _rooms.Clear();
        _outline = _source;
        Ruleset rules;
        try
        {
            rules = RulesFolder.Load(_game, _system).Rules;
        }
        catch (ContentException error)
        {
            return new List<string> { $"the rules system \"{_system}\": {error.Message}" };
        }
        _systemId = rules.Id;
        bool known(string s) => rules.Skill(s) != null || rules.Ability(s) != null;
        // a check the system has no skill for is rolled with what it notices things with
        string otherwise = new[] { rules.Roles.Perception, rules.Skills.FirstOrDefault()?.Id ?? "", rules.Abilities.FirstOrDefault()?.Id ?? "" }
            .FirstOrDefault(id => id.Length > 0) ?? "";
        if (_outline.System.Length > 0)
        {
            // the book's numbers become the game's before anything is written
            try
            {
                SystemTable table = SystemTable.Load(_game, _outline.System);
                _outline = table.Apply(_outline, known, _report, otherwise);
            }
            catch (ContentException error)
            {
                return new List<string> { $"the book's system \"{_outline.System}\": {error.Message}" };
            }
        }
        else if (_system != RulesFolder.Default)
        {
            // written in the game's own words: what the chosen system lacks takes its nearest
            _outline = new SystemTable { Name = rules.Name }.Apply(_outline, known, _report, otherwise);
        }
        if (_system != RulesFolder.Default)
        {
            FitHeroes();
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
            OutlineEntry? entry = chapters.FirstOrDefault(c => c.Id == chapter);
            layouts[chapter] = ReadWalls && DrawnMap(chapter, entry) is BookMap drawn
                ? new Layout(PlacesOf(chapter), LinksWithin(chapter), _report, drawn)
                : new Layout(PlacesOf(chapter), LinksWithin(chapter), _report, BookMapSquares(entry));
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

    // What the outline names from the game's own set that the chosen system doesn't have: heroes
    // take its plainest class; items and creatures take the system's one of the same name, or
    // are left out. The report says each.
    private void FitHeroes()
    {
        var own = new Compendium();
        own.Load(_game, RulesFolder.Default, "");
        bool HasItem(string id) => _outline.Find(id)?.Kind == OutlineKind.Item || _gameCompendium.Item(id) != null;
        bool HasCreature(string id) => _outline.Find(id)?.Kind == OutlineKind.Creature || _gameCompendium.Creature(id) != null;
        string? ItemLike(string id) => own.Item(id) is ItemDefinition item
            ? _gameCompendium.Items.Values.FirstOrDefault(i => string.Equals(i.Name, item.Name, StringComparison.OrdinalIgnoreCase))?.Id : null;
        string? CreatureLike(string id) => own.Creature(id) is CreatureDefinition creature
            ? _gameCompendium.Creatures.Values.FirstOrDefault(c => string.Equals(c.Name, creature.Name, StringComparison.OrdinalIgnoreCase))?.Id : null;

        var fitted = new Outline { Title = _outline.Title, System = _outline.System };
        foreach (OutlineEntry entry in _outline.Entries)
        {
            var data = (JsonObject)entry.Data.DeepClone();
            switch (entry.Kind)
            {
                case OutlineKind.Hero when _gameCompendium.Class(entry.Text("class")) == null:
                    _report.Add(new ReportLine(entry.Id, $"this system has no class \"{entry.Text("class")}\"; {entry.Text("name", entry.Id)} is a {DefaultClass()}"));
                    data["class"] = DefaultClass();
                    break;
                case OutlineKind.Container when data["items"] is JsonArray items:
                    var kept = new JsonArray();
                    foreach (string id in entry.Texts("items"))
                    {
                        if (HasItem(id) || ItemLike(id) is not null)
                        {
                            kept.Add(HasItem(id) ? id : ItemLike(id));
                        }
                        else
                        {
                            _report.Add(new ReportLine(entry.Id, $"this system has no item \"{id}\"; left out"));
                        }
                    }
                    data["items"] = kept;
                    break;
                case OutlineKind.Encounter when data["creatures"] is JsonArray creatures:
                    for (int i = creatures.Count - 1; i >= 0; i--)
                    {
                        string id = creatures[i]?["creature"]?.GetValue<string>() ?? "";
                        if (HasCreature(id))
                        {
                            continue;
                        }
                        if (CreatureLike(id) is string like && creatures[i] is JsonObject placed)
                        {
                            placed["creature"] = like;
                        }
                        else
                        {
                            _report.Add(new ReportLine(entry.Id, $"this system has no creature \"{id}\"; left out"));
                            creatures.RemoveAt(i);
                        }
                    }
                    break;
            }
            fitted.Entries.Add(new OutlineEntry { Id = entry.Id, Kind = entry.Kind, Data = data, From = entry.From, Picture = entry.Picture, Chapter = entry.Chapter });
        }
        _outline = fitted;
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

    // the chosen system has an action by that id
    private bool FindSystemAction(string id) => _game.Exists($"{_system}/actions/{id}.json");

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
            // the items it fights with, as the package names them (the game's own where it has one)
            if (data["items"] is JsonArray carried)
            {
                data["items"] = new JsonArray(carried.Select(i => (JsonNode?)IdOf(i?.ToString() ?? "")).ToArray());
            }
            // a stat block's Multiattack is the system's own, where it has one
            if (data["multiattack"] is JsonNode && FindSystemAction("multiattack"))
            {
                var actions = data["actions"] as JsonArray ?? new JsonArray();
                if (!actions.Any(a => a?.ToString() == "multiattack"))
                {
                    actions.Add("multiattack");
                }
                data["actions"] = actions;
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
        if (_system != RulesFolder.Default)
        {
            // the system the writer chose; left out, a chapter plays the game's own
            j["ruleset"] = _system;
        }
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
            party.Add(new JsonObject { ["name"] = "Hero", ["class"] = DefaultClass(), ["at"] = Cell(layout.Take(layout.Start?.Id ?? "")) });
            _report.Add(new ReportLine("", $"the book names no heroes; one {DefaultClass()} seat stands in"));
        }
        j["party"] = party;

        var npcs = new JsonArray();
        var dialogues = new SortedSet<string>(StringComparer.Ordinal);
        foreach (OutlineEntry npc in _outline.OfKind(OutlineKind.Npc).Where(n => EntryChapter(n) == id))
        {
            var n = new JsonObject { ["id"] = npc.Id, ["name"] = npc.Text("name") };
            string creature = npc.Text("creature") is { Length: > 0 } c ? IdOf(c) : Bystander();
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
            // a room drawn off the book's map is not a box: as many boxes as cover its floor, each setting the same flag
            int part = 0;
            foreach ((int x, int y, int w, int h) in layout.RoomAreas(place.Id))
            {
                layout.Areas.Add(new JsonObject
                {
                    ["id"] = ++part == 1 ? "room-" + place.Id : $"room-{place.Id}-{part}",
                    ["area"] = new JsonArray(x, y, w, h),
                    ["set"] = new JsonArray(flag),
                });
            }
            string talk = "read-" + place.Id;
            WriteJson($"{folder}/dialogue/{talk}.json", new JsonObject
            {
                ["id"] = talk,
                ["start"] = "read",
                // narration: nobody speaks it, so it shows with no name plate and no faces
                ["nodes"] = new JsonArray(new JsonObject { ["id"] = "read", ["text"] = string.Join("\n\n", place.Texts("readAloud")) }),
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
        JsonObject map = layout.Map(j["title"]!.GetValue<string>());
        foreach (OutlineEntry place in PlacesOf(id))
        {
            _rooms[place.Id] = (id, layout.Room(place.Id));
            _spots[place.Id] = (layout.Spot(place.Id), layout.Drawn);
        }
        if (chapter?.Text("mapPicture") is { Length: > 0 } bookMap)
        {
            if (File.Exists(Path.Combine(_importFolder, bookMap.Replace('/', Path.DirectorySeparatorChar))))
            {
                // the book's map under Map mode, over the whole map to start with, for the writer to line up and trace
                _pictures.TryAdd(bookMap, (bookMap, null));
                var rows = (JsonArray)map["layers"]![0]!["rows"]!;
                // lined up with the places put where it draws them; else over the whole map, for the writer to line up
                map["trace"] = new JsonObject
                {
                    ["path"] = bookMap,
                    ["area"] = layout.TraceArea is (double x, double y, double w, double h)
                        ? new JsonArray(x, y, w, h)
                        : new JsonArray(0, 0, rows[0]!.GetValue<string>().Length, rows.Count),
                };
            }
            else
            {
                _report.Add(new ReportLine(id, $"its map picture {bookMap} is not in the import folder"));
            }
        }
        WriteJson($"{folder}/map.json", map);
        foreach (string lost in layout.Unreached())
        {
            _report.Add(new ReportLine(lost, "no walk from the start reaches it"));
        }
    }

    /// <summary>How many squares across a chapter's map picture is taken to be when places are put where it draws them.</summary>
    public const double BookMapWidth = 60;

    // the chapter's map picture in squares, BookMapWidth across and as tall as its shape says; null without one
    private (double Width, double Height)? BookMapSquares(OutlineEntry? chapter)
    {
        string file = chapter?.Text("mapPicture") is { Length: > 0 } picture ? Path.Combine(_importFolder, picture.Replace('/', Path.DirectorySeparatorChar)) : "";
        if (file.Length == 0 || !File.Exists(file))
        {
            return null;
        }
        try
        {
            using FileStream stream = File.OpenRead(file);
            StbImageSharp.ImageInfo? info = StbImageSharp.ImageInfo.FromStream(stream);
            return info is { Width: > 0, Height: > 0 } size ? (BookMapWidth, Math.Round(BookMapWidth * size.Height / size.Width, 2)) : null;
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Take each room's shape, walls and doors from the book's map when it can be read. On by default; off, rooms are boxes put where the map has them.</summary>
    public bool ReadWalls { get; init; } = true;

    // the chapter's map picture read into squares, when every place of the chapter has its number on it
    private BookMap? DrawnMap(string id, OutlineEntry? chapter)
    {
        List<OutlineEntry> places = PlacesOf(id);
        string file = chapter?.Text("mapPicture") is { Length: > 0 } picture ? Path.Combine(_importFolder, picture.Replace('/', Path.DirectorySeparatorChar)) : "";
        if (file.Length == 0 || !File.Exists(file) || places.Count == 0)
        {
            return null;
        }
        var labels = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        foreach (OutlineEntry place in places)
        {
            if (place.Data["mapAt"] is not JsonArray { Count: 2 } at)
            {
                _report.Add(new ReportLine(place.Id, "its number isn't on the book's map, so the map's walls aren't used; rooms are boxes"));
                return null;
            }
            labels[place.Id] = (at[0]!.GetValue<double>(), at[1]!.GetValue<double>());
        }
        BookMap? map;
        try
        {
            map = BookMap.Read(File.ReadAllBytes(file), labels);
        }
        catch (IOException)
        {
            map = null;
        }
        if (map == null)
        {
            _report.Add(new ReportLine(id, "the floor on the book's map couldn't be told from the rest, so its walls aren't used; rooms are boxes"));
        }
        else if (!map.GridFound)
        {
            _report.Add(new ReportLine(id, $"the book's map draws no grid; it is taken to be {BookMap.SquaresWithoutGrid} squares across"));
        }
        return map;
    }

    private bool IsStarted(string dialogue) =>
        _outline.Entries.Any(e => e.Kind is OutlineKind.Npc or OutlineKind.Trigger && e.Text("dialogue") == dialogue);

    private const string BystanderId = "bystander";

    // An ordinary person nobody wrote numbers for: only what every system's creature file has.
    private static JsonObject BystanderData() => new()
    {
        ["id"] = BystanderId, ["name"] = "Bystander", ["description"] = "Ordinary folk who'd rather not fight.",
        ["hp"] = 4, ["armorClass"] = 10, ["speed"] = 30, ["items"] = new JsonArray(),
        ["token"] = new JsonObject { ["color"] = new JsonArray(200, 180, 140), ["size"] = 0.4 },
    };

    // The creature an NPC with none named stands as: the system's commoner, or a plain bystander
    // written into the package when it has none.
    private string Bystander()
    {
        if (_gameCompendium.Creature("commoner") != null)
        {
            return "commoner";
        }
        if (!File.Exists(Path.Combine(_package, "creatures", BystanderId + ".json")))
        {
            WriteJson($"creatures/{BystanderId}.json", BystanderData());
        }
        return BystanderId;
    }

    private string NpcCreature(OutlineEntry npc, string creature, string face)
    {
        JsonObject data;
        if (_outline.Find(creature) is { Kind: OutlineKind.Creature } own)
        {
            data = Copy(own.Data);
        }
        else if (creature == BystanderId && _gameCompendium.Creature(BystanderId) == null)
        {
            data = BystanderData();
        }
        else
        {
            // The game's creatures belong to its system now; older content kept them at the root.
            string path = $"{_system}/creatures/{creature}.json";
            data = (JsonObject)JsonNode.Parse(_game.ReadText(_game.Exists(path) ? path : $"creatures/{creature}.json"))!;
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
            ["ruleset"] = _systemId + "@1.0",
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
        // its cover in the library: the first of the book's pictures it uses, by page
        string cover = _pictures.Values.Select(p => p.Target)
            .OrderBy(p => int.TryParse(System.Text.RegularExpressions.Regex.Match(p, @"/p(\d+)-").Groups[1].Value, out int page) ? page : int.MaxValue)
            .ThenBy(p => p, StringComparer.Ordinal).FirstOrDefault() ?? "";
        if (cover.Length > 0)
        {
            manifest["cover"] = cover;
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
        var rooms = new JsonObject();
        foreach ((string place, (string chapter, (int X, int Y, int W, int H) at)) in _rooms)
        {
            (Cell spot, bool drawn) = _spots[place];
            rooms[place] = new JsonObject { ["chapter"] = chapter, ["at"] = new JsonArray(at.X, at.Y, at.W, at.H), ["spot"] = new JsonArray(spot.X, spot.Y), ["drawn"] = drawn };
        }
        Directory.CreateDirectory(_importFolder);
        File.WriteAllText(Path.Combine(_importFolder, ReportFile),
            CreateJson.Write(new JsonObject { ["format"] = "yorehold.import-report", ["version"] = 1, ["lines"] = lines, ["rooms"] = rooms }) + "\n");
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

        public Layout(List<OutlineEntry> places, List<OutlineEntry> links, List<ReportLine> report, (double Width, double Height)? mapSquares = null)
        {
            _places = places;
            _mapSquares = mapSquares;
            Start = places.FirstOrDefault();
            if (Start == null)
            {
                // a chapter with no places still needs ground to stand on
                var hall = new OutlineEntry { Id = "start", Kind = OutlineKind.Place, Data = new JsonObject { ["name"] = "Start", ["size"] = new JsonArray(8, 6) } };
                _places.Add(hall);
                Start = hall;
            }
            if (OnBookMap(Start) is (int, int) first)
            {
                Place(Start, first.X, first.Y);
            }
            else
            {
                Place(Start, 1, 1);
            }
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
                    OutlineEntry next = _places.First(p => p.Id == to);
                    // where the book's map draws it wins over beside the room it links from; the way is cut through if they don't touch
                    if (OnBookMap(next) is (int mx, int my))
                    {
                        PlaceNear(next, mx, my);
                    }
                    else
                    {
                        PlaceBeside(from, next);
                    }
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
                string to = previous.Id;
                if (OnBookMap(place) is (int, int) spot)
                {
                    // where the book's map draws it, joined to the nearest room already down
                    PlaceNear(place, spot.X, spot.Y);
                    (int x, int y, int w, int h) = _rooms[place.Id];
                    to = _rooms.Where(r => r.Key != place.Id)
                        .OrderBy(r => Math.Pow(r.Value.X + r.Value.W / 2.0 - x - w / 2.0, 2) + Math.Pow(r.Value.Y + r.Value.H / 2.0 - y - h / 2.0, 2))
                        .ThenBy(r => r.Key, StringComparer.Ordinal).First().Key;
                }
                else
                {
                    PlaceBeside(previous.Id, place);
                }
                var link = new OutlineEntry { Id = $"{to}-{place.Id}", Kind = OutlineKind.Link, Data = new JsonObject { ["from"] = to, ["to"] = place.Id, ["way"] = "open" } };
                Join(link, to, place.Id, report);
                report.Add(new ReportLine(place.Id, $"no way in is given; joined to {to} by an open way"));
                previous = place;
            }
            Normalize();
        }

        // the book's map read into tiles, when the rooms are drawn off it instead of put down as boxes
        private readonly char[,]? _drawn;

        /// <summary>Are the walls the book's map's own.</summary>
        public bool Drawn => _drawn != null;

        /// <summary>
        /// The chapter as the book's map draws it: floor where the map has floor, rock walls round
        /// it, each place the floor nearest its number, and a door where the map draws one. A link
        /// decides what kind of door it is; places the map doesn't join are joined by a corridor.
        /// </summary>
        public Layout(List<OutlineEntry> places, List<OutlineEntry> links, List<ReportLine> report, BookMap map)
        {
            _places = places;
            Start = places[0];
            // one square of rock all round, so the outermost floor has a wall
            _width = map.Width + 2;
            _height = map.Height + 2;
            _drawn = new char[_height, _width];
            var outdoors = places.Where(Outdoors).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            var owner = new Dictionary<Cell, string>();
            var doors = new List<Cell>();
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    _drawn[y, x] = Empty;
                    if (x == 0 || y == 0 || x > map.Width || y > map.Height || !map.Floor[y - 1, x - 1])
                    {
                        continue;
                    }
                    string place = map.Place[y - 1, x - 1]!;
                    _drawn[y, x] = outdoors.Contains(place) ? Grass : Floor;
                    owner[new Cell(x, y)] = place;
                    if (map.Door[y - 1, x - 1])
                    {
                        doors.Add(new Cell(x, y));
                    }
                }
            }
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    if (_drawn[y, x] != Empty)
                    {
                        continue;
                    }
                    var round = new List<char>();
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (x + dx >= 0 && y + dy >= 0 && x + dx < _width && y + dy < _height && _drawn[y + dy, x + dx] is Floor or Grass)
                            {
                                round.Add(_drawn[y + dy, x + dx]);
                            }
                        }
                    }
                    if (round.Count > 0)
                    {
                        _drawn[y, x] = round.All(c => c == Grass) ? Tree : Wall;
                    }
                }
            }
            foreach (OutlineEntry place in places)
            {
                Cell label = map.Labels[place.Id];
                var spot = new Cell(label.X + 1, label.Y + 1);
                List<Cell> cells = owner.Where(o => o.Value == place.Id).Select(o => o.Key).ToList();
                _rooms[place.Id] = (cells.Min(c => c.X), cells.Min(c => c.Y), cells.Max(c => c.X) - cells.Min(c => c.X) + 1, cells.Max(c => c.Y) - cells.Min(c => c.Y) + 1);
                _cells[place.Id] = cells;
                // from the place's number out; nobody stands in a doorway
                _free[place.Id] = cells.Where(c => !doors.Contains(c))
                    .OrderBy(c => Math.Abs(c.X - spot.X) + Math.Abs(c.Y - spot.Y)).ThenBy(c => c.Y).ThenBy(c => c.X).ToList();
            }
            TraceArea = (map.Picture.X + 1, map.Picture.Y + 1, map.Picture.Width, map.Picture.Height);

            IEnumerable<Cell> Beside(Cell c) => new[] { new Cell(c.X + 1, c.Y), new Cell(c.X - 1, c.Y), new Cell(c.X, c.Y + 1), new Cell(c.X, c.Y - 1) };
            var used = new HashSet<Cell>();
            foreach (OutlineEntry link in links)
            {
                string a = link.Text("from"), b = link.Text("to");
                if (!_rooms.ContainsKey(a) || !_rooms.ContainsKey(b))
                {
                    report.Add(new ReportLine(link.Id, "joins places in no room of this chapter; left out"));
                    continue;
                }
                string way = link.Text("way", "open");
                // a door the map draws between the two: in one's floor and touching the other's
                List<Cell> between = doors.Where(d => !used.Contains(d) && (owner[d] == a || owner[d] == b)
                    && Beside(d).Any(n => owner.TryGetValue(n, out string? other) && other == (owner[d] == a ? b : a))).ToList();
                if (between.Count > 0)
                {
                    foreach (Cell door in between)
                    {
                        used.Add(door);
                        // the map draws a door even where the words only say the way is there
                        WayObject(link, way == "open" ? "door" : way, door, report);
                    }
                    continue;
                }
                List<Cell> meet = _cells[a].Where(c => Beside(c).Any(n => owner.TryGetValue(n, out string? other) && other == b)).ToList();
                if (meet.Count == 0 && !Walk(a, b))
                {
                    Cell start = _free[a][0], end = _free[b][0];
                    char ground = outdoors.Contains(a) ? Grass : Floor;
                    for (int y = Math.Min(start.Y, end.Y); y <= Math.Max(start.Y, end.Y); y++)
                    {
                        _openings.Add((new Cell(start.X, y), ground));
                    }
                    for (int x = Math.Min(start.X, end.X); x <= Math.Max(start.X, end.X); x++)
                    {
                        _openings.Add((new Cell(x, end.Y), ground));
                    }
                    report.Add(new ReportLine(link.Id, "the book's map draws no way between them; a corridor joins them"));
                    continue;
                }
                if (way is "door" or "secret" or "locked" && (meet.Count == 0 || meet.Count > 3))
                {
                    report.Add(new ReportLine(link.Id, $"the book's map draws no door here and the two don't meet at a doorway; the way is left open"));
                    continue;
                }
                if (way == "open")
                {
                    continue;
                }
                foreach (Cell at in way is "climb" or "jump" ? meet.Take(1) : meet)
                {
                    WayObject(link, way, at, report);
                }
            }
            foreach (Cell door in doors.Where(d => !used.Contains(d)))
            {
                Objects.Add(new JsonObject { ["kit"] = "door", ["at"] = new JsonArray(door.X, door.Y) });
            }
        }

        private readonly Dictionary<string, List<Cell>> _cells = new(StringComparer.Ordinal);

        // can b's number be walked to from a's, over the floor as it stands (doors open)
        private bool Walk(string a, string b)
        {
            char[,] grid = Grid();
            var seen = new HashSet<Cell>();
            var queue = new Queue<Cell>();
            queue.Enqueue(_free[a][0]);
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
            return seen.Contains(_free[b][0]);
        }

        /// <summary>The square a place is centred on: a box's middle, or where its number is on the book's map.</summary>
        public Cell Spot(string place) => _free[place][0];

        /// <summary>A place's floor as boxes: the one it is, or as many as cover a floor drawn off the book's map.</summary>
        public List<(int X, int Y, int W, int H)> RoomAreas(string place)
        {
            if (!_cells.TryGetValue(place, out List<Cell>? cells))
            {
                return new() { _rooms[place] };
            }
            // each row's runs of floor, a run growing down while the row under it has the same one
            var boxes = new List<(int X, int Y, int W, int H)>();
            foreach (IGrouping<int, Cell> row in cells.GroupBy(c => c.Y).OrderBy(g => g.Key))
            {
                List<int> xs = row.Select(c => c.X).OrderBy(x => x).ToList();
                for (int i = 0; i < xs.Count;)
                {
                    int end = i;
                    while (end + 1 < xs.Count && xs[end + 1] == xs[end] + 1)
                    {
                        end++;
                    }
                    int x = xs[i], w = xs[end] - xs[i] + 1;
                    int above = boxes.FindIndex(b => b.X == x && b.W == w && b.Y + b.H == row.Key);
                    if (above >= 0)
                    {
                        boxes[above] = (x, boxes[above].Y, w, boxes[above].H + 1);
                    }
                    else
                    {
                        boxes.Add((x, row.Key, w, 1));
                    }
                    i = end + 1;
                }
            }
            return boxes;
        }

        private readonly (double Width, double Height)? _mapSquares;

        /// <summary>Where the book's map picture lies, in the map's squares, once laid out; null without one.</summary>
        public (double X, double Y, double Width, double Height)? TraceArea { get; private set; }

        // the top-left square that puts a place's middle where its number is on the book's map
        private (int X, int Y)? OnBookMap(OutlineEntry place)
        {
            if (_mapSquares is not (double width, double height) || place.Data["mapAt"] is not JsonArray at)
            {
                return null;
            }
            (int w, int h) = SizeOf(place);
            return ((int)Math.Round(at[0]!.GetValue<double>() * width - w / 2.0), (int)Math.Round(at[1]!.GetValue<double>() * height - h / 2.0));
        }

        // the free spot nearest to x, y, rings out from it
        private void PlaceNear(OutlineEntry place, int x, int y)
        {
            (int w, int h) = SizeOf(place);
            for (int ring = 0; ring < 200; ring++)
            {
                for (int dy = -ring; dy <= ring; dy++)
                {
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) == ring && Fits(x + dx, y + dy, w, h))
                        {
                            Place(place, x + dx, y + dy);
                            return;
                        }
                    }
                }
            }
            Place(place, x, _rooms.Values.Max(r => r.Y + r.H) + 2);
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
            if (_mapSquares is (double width, double height))
            {
                TraceArea = (dx, dy, width, height);
            }
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
            WayObject(link, link.Text("way", "open"), at, report);
        }

        // what stands in a way that isn't open: a door, a locked one, or a line in the report for what the game can't play
        private void WayObject(OutlineEntry link, string way, Cell at, List<ReportLine> report)
        {
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
        public Cell Take(string place) => Take(place, far: false);

        /// <summary>A free square in the place, from its far side in: where foes wait.</summary>
        public Cell TakeFar(string place) => Take(place, far: true);

        private Cell Take(string place, bool far)
        {
            List<Cell> free = _free.GetValueOrDefault(place) ?? _free[Start!.Id];
            List<Cell> left = free.Where(c => !_taken.Contains(c)).ToList();
            if (left.Count == 0)
            {
                // a place the book's map draws smaller than what the book puts in it: the nearest free floor of any place
                Cell middle = free[0];
                left = _free.Values.SelectMany(cells => cells).Where(c => !_taken.Contains(c))
                    .OrderBy(c => Math.Abs(c.X - middle.X) + Math.Abs(c.Y - middle.Y)).ThenBy(c => c.Y).ThenBy(c => c.X).Take(1).ToList();
                if (left.Count == 0)
                {
                    return middle;
                }
            }
            Cell at = far ? left[^1] : left[0];
            _taken.Add(at);
            return at;
        }

        /// <summary>Places no walk from the start reaches (doors count as passable: they open).</summary>
        public List<string> Unreached()
        {
            char[,] grid = Grid();
            var seen = new HashSet<Cell>();
            var queue = new Queue<Cell>();
            queue.Enqueue(_free[Start!.Id][0]);
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
            return _rooms.Where(r => !seen.Contains(_free[r.Key][0])).Select(r => r.Key).ToList();
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
            if (_drawn != null)
            {
                var drawn = (char[,])_drawn.Clone();
                foreach ((Cell at, char tile) in _openings)
                {
                    drawn[at.Y, at.X] = tile;
                }
                return drawn;
            }
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
            Cell start = _free[Start!.Id][0];
            markers["start"] = new JsonArray(start.X, start.Y);
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
