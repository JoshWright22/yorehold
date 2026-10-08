namespace Yorehold.Rules;

/// <summary>A seat in the party with its ready-made hero.</summary>
public record PartyMember(string Name, string ClassId, ContentColor Color, Cell At)
{
    /// <summary>The seat's own picture, a content path; empty = the class's portrait.</summary>
    public string Image { get; init; } = "";
}

public class Placement
{
    public string CreatureId { get; init; } = "";
    /// <summary>Empty = the creature's own name.</summary>
    public string Name { get; init; } = "";
    public Cell At { get; init; }
    public ContentNode? Ai { get; init; }
    /// <summary>Dialogue when it gives up; empty = the encounter's.</summary>
    public string Surrender { get; init; } = "";
    /// <summary>Where it looks until it notices the party, in degrees (0 = east, 90 = south). Null = toward the party's start.</summary>
    public double? Facing { get; init; }
}

public class EncounterGroup
{
    public string Id { get; init; } = "";
    /// <summary>Shown when the fight starts.</summary>
    public string Text { get; init; } = "";
    public List<Placement> Creatures { get; init; } = new();
    /// <summary>Story flags set when the party wins this fight.</summary>
    public List<string> Set { get; init; } = new();
    public ContentNode? Ai { get; init; }
    public string Surrender { get; init; } = "";
    /// <summary>What winning gives each hero. Null = the chapter's XpPerVictory.</summary>
    public int? Xp { get; init; }
    public LootTable Loot { get; init; } = new();
}

public class ChapterContainer
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "Chest";
    public Cell At { get; init; }
    public List<string> Items { get; init; } = new();
    public int Coins { get; init; }
    public LootTable Loot { get; init; } = new();
}

/// <summary>An AI change the story makes once its flags are set.</summary>
public record AiChange(List<string> When, string Creature, string Encounter, string Name, ContentNode Ai);

/// <summary>Dialogue or a cutscene fired by the chapter: at the start when When is empty, else once all its flags are set.</summary>
public record ChapterTrigger(string Id, List<string> When, string Dialogue, string Cutscene);

public record WinCondition(List<string> When, string Dialogue, string Cutscene);

/// <summary>
/// A chapter folder, loaded and checked: chapter.json, its map, the rules it plays by and every
/// definition, dialogue and cutscene it names.
/// </summary>
public class Chapter
{
    public string Folder { get; init; } = "";
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public List<string> Intro { get; init; } = new();
    public int XpPerVictory { get; init; }
    /// <summary>"{xp}" becomes the XP given.</summary>
    public string VictoryText { get; init; } = "Victory!";
    public string DefeatText { get; init; } = "The party has fallen.";
    public string ResumeText { get; init; } = "Adventure resumed.";
    public string ClearedText { get; init; } = "Chapter complete";
    /// <summary>Content path of the cutscene played when the chapter is cleared; empty = the banner only.</summary>
    public string ClearedCutscene { get; private set; } = "";
    public List<PartyMember> Party { get; init; } = new();
    /// <summary>The level the chapter is written for.</summary>
    public int Level { get; init; } = 1;
    public List<EncounterGroup> Encounters { get; init; } = new();
    public List<ChapterNpc> Npcs { get; init; } = new();
    public List<ChapterContainer> Containers { get; init; } = new();
    public List<AiChange> AiChanges { get; init; } = new();
    /// <summary>Dialogue for anyone who gives up and has none of their own; empty = none.</summary>
    public string Surrender { get; private set; } = "";
    public string QuestsFile { get; private set; } = "";
    public QuestJournal Quests { get; private set; } = new();
    public List<string> CompleteWhen { get; init; } = new();
    /// <summary>Flags that stay behind when the party travels on.</summary>
    public List<string> LocalFlags { get; init; } = new();
    public bool CampAllowed { get; init; } = true;
    public string WipeCutscene { get; private set; } = "";
    public List<Cell> WipeDestination { get; init; } = new();
    public List<ChapterTrigger> Triggers { get; init; } = new();
    public WinCondition? WinCondition { get; private set; }

    public RulesFolder Rules { get; init; } = new();
    public Compendium Compendium { get; init; } = new();
    public SortedDictionary<string, Kit> Kits { get; init; } = new(StringComparer.Ordinal);
    public GameMap Map { get; init; } = new();
    /// <summary>Every conversation and cutscene the chapter names, by content path.</summary>
    public SortedDictionary<string, Dialogue> Dialogues { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, Cutscene> Cutscenes { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Reads a chapter folder. system is the ruleset for a chapter that names none, like camp,
    /// which plays whatever system the adventure does; null = the game's own.
    /// </summary>
    public static Chapter Load(ContentFiles files, string folder, string? system = null)
    {
        if (folder.Length > 0 && !ContentFiles.IsContentPath(folder))
        {
            throw new ContentException(folder, "", "expected a relative chapter folder");
        }
        ContentNode j = ContentNode.Read(files, folder.Length == 0 ? "chapter.json" : folder + "/chapter.json");
        j.RequireObject("a chapter is a JSON object");
        string id = j.At("id").AsId();

        bool borrowed = system != null && !j.Has("ruleset");
        RulesFolder rules = RulesFolder.Load(files, borrowed ? system! : j.Text("ruleset", RulesFolder.Default), folder);

        // The system's own definitions first, then shared content, then the chapter's own; each can
        // replace entries of the ones before.
        var compendium = new Compendium();
        compendium.Load(files, rules.Folder, "", folder);
        // Races, backgrounds, feats and spells only come from the ruleset, never from a chapter.
        if (rules.Folder.Length > 0)
        {
            compendium.LoadOptions(files, rules.Folder);
            // then the content sets for this system the game was given (their feats/, spells/...)
            compendium.LoadOptions(files, "");
        }
        rules.Check(compendium, folder, files);

        // Kits: the chapter's own replace shared ones.
        var kits = new SortedDictionary<string, Kit>(StringComparer.Ordinal);
        foreach (string kitFolder in folder.Length == 0 ? new[] { "kits" } : new[] { "kits", folder + "/kits" })
        {
            foreach (string path in files.List(kitFolder))
            {
                ContentNode kit = ContentNode.Read(files, path);
                if (!ContentIds.IsId(ContentFiles.Stem(path)))
                {
                    throw kit.Fail("kit file names use a-z, 0-9, - and _");
                }
                kits[ContentFiles.Stem(path)] = Kit.Read(kit);
            }
        }

        string mapFile = Resolve(files, folder, j.Get("map") ?? j, j.Text("map", "map.json"));
        ContentNode mapNode = ContentNode.Read(files, mapFile);
        GameMap map = GameMap.Read(mapNode, kits);
        foreach (MapObject o in map.Objects)
        {
            string what = o.Name.Length == 0 ? "a map object" : o.Name;
            foreach (string item in o.Contents.Keys)
            {
                if (item != "coins" && compendium.Item(item) == null)
                {
                    throw mapNode.Fail("objects", $"unknown item \"{item}\" in {what}");
                }
            }
            o.Trap?.Effect?.Check(rules.Rules, mapFile, $"objects ({what}) trap.effect");
        }

        var chapter = new Chapter
        {
            Folder = folder,
            Id = id,
            Title = j.Text("title", id),
            Intro = j.Texts("intro"),
            XpPerVictory = j.Int("xpPerVictory", 0, 0),
            VictoryText = j.Text("victoryText", "Victory!"),
            DefeatText = j.Text("defeatText", "The party has fallen."),
            ResumeText = j.Text("resumeText", "Adventure resumed."),
            ClearedText = j.Text("clearedText", "Chapter complete"),
            Level = j.Int("level", 1, 1, 20),
            CompleteWhen = j.Flags("completeWhen"),
            LocalFlags = j.Flags("localFlags"),
            CampAllowed = j.Bool("camp", true),
            Rules = rules,
            Compendium = compendium,
            Kits = kits,
            Map = map,
        };
        var reader = new Reader(files, folder, chapter);

        string clearedCutscene = "";
        if (j.Get("endings")?.Get("cleared") is ContentNode cleared && cleared.AsText().Length > 0)
        {
            clearedCutscene = reader.Cutscene(cleared);
        }
        string surrender = j.Get("surrender") is ContentNode own ? reader.Dialogue(own)
            : files.Exists("dialogue/surrender.json") ? reader.Dialogue(j, "dialogue/surrender.json") : "";

        ContentNode partyNode = j.At("party");
        foreach (ContentNode p in partyNode.Items())
        {
            var member = new PartyMember(p.At("name").AsText(), p.At("class").AsText(),
                p.Get("color") is ContentNode color ? ContentParts.ColorFrom(color) : new ContentColor(200, 200, 210), ContentParts.CellFrom(p.At("at")))
            {
                Image = p.Text("image", ""),
            };
            if (compendium.Class(member.ClassId) == null && borrowed && compendium.Classes.Count > 0)
            {
                // a chapter playing another system's rules stands in the real party; its own are placeholders
                member = member with { ClassId = compendium.Classes.Keys.First() };
            }
            if (compendium.Class(member.ClassId) == null)
            {
                throw p.Fail("class", $"unknown class \"{member.ClassId}\" for {member.Name}");
            }
            if (member.Image.Length > 0 && (!ContentFiles.IsContentPath(member.Image) || !files.Exists(member.Image)))
            {
                throw p.Fail("image", $"\"{member.Image}\" is not a picture in the content, like portraits/marn.png");
            }
            reader.Place(p.At("at"), member.At, member.Name);
            chapter.Party.Add(member);
        }
        if (chapter.Party.Count == 0 || chapter.Party.Count > 4)
        {
            throw partyNode.Fail("the party has one to four members");
        }

        foreach (ContentNode e in j.Get("encounters")?.Items() ?? Array.Empty<ContentNode>())
        {
            string encounterId = e.Text("id", $"encounter {chapter.Encounters.Count + 1}");
            if (chapter.Encounters.Any(other => other.Id == encounterId))
            {
                throw e.Fail("id", $"duplicate encounter {encounterId}");
            }
            var creatures = new List<Placement>();
            foreach (ContentNode p in e.At("creatures").Items())
            {
                string creatureId = p.At("creature").AsText();
                if (compendium.Creature(creatureId) == null)
                {
                    throw p.Fail("creature", $"unknown creature \"{creatureId}\" in {encounterId}");
                }
                string name = p.Text("name", "");
                Cell at = ContentParts.CellFrom(p.At("at"));
                reader.Place(p.At("at"), at, name.Length == 0 ? creatureId : name);
                creatures.Add(new Placement
                {
                    CreatureId = creatureId,
                    Name = name,
                    At = at,
                    Ai = reader.Ai(p),
                    Surrender = p.Get("surrender") is ContentNode gives ? reader.Dialogue(gives) : "",
                    Facing = p.Get("facing")?.AsNumber(-360, 360),
                });
            }
            if (creatures.Count == 0)
            {
                throw e.Fail("creatures", $"{encounterId} has no creatures");
            }
            LootTable loot = new();
            if (e.Get("loot") is ContentNode lootNode)
            {
                loot = LootTable.Read(lootNode);
                compendium.CheckLoot(loot, e, "loot");
            }
            chapter.Encounters.Add(new EncounterGroup
            {
                Id = encounterId,
                Text = e.Text("text", ""),
                Creatures = creatures,
                Set = e.Flags("set"),
                Ai = reader.Ai(e),
                Surrender = e.Get("surrender") is ContentNode groupGives ? reader.Dialogue(groupGives) : "",
                Xp = e.Get("xp")?.AsInt(0, 1000000),
                Loot = loot,
            });
        }

        foreach (ContentNode n in j.Get("npcs")?.Items() ?? Array.Empty<ContentNode>())
        {
            string npcId = n.At("id").AsId();
            if (chapter.Npcs.Any(other => other.Id == npcId))
            {
                throw n.Fail("id", $"two npcs have the id \"{npcId}\"");
            }
            string creature = n.Text("creature", "commoner");
            if (compendium.Creature(creature) == null)
            {
                throw n.Fail("creature", $"unknown creature \"{creature}\"");
            }
            CompanionDefinition? companion = null;
            if (n.Get("companion") is ContentNode companionNode)
            {
                companion = CompanionDefinition.Read(companionNode, npcId);
            }
            else if (n.Has("approvalStart") || n.Has("approvalJoinThreshold"))
            {
                // The first companion fields, from before the companion object.
                companion = new CompanionDefinition { Id = npcId, Approval = n.Int("approvalStart", 0), JoinAt = n.Int("approvalJoinThreshold", 0) };
            }
            Cell at = ContentParts.CellFrom(n.At("at"));
            string name = n.At("name").AsText();
            reader.Place(n.At("at"), at, name);
            chapter.Npcs.Add(new ChapterNpc
            {
                Id = npcId,
                Name = name,
                Color = n.Get("color") is ContentNode color ? ContentParts.ColorFrom(color) : new ContentColor(200, 180, 140),
                At = at,
                Dialogue = reader.Dialogue(n.At("dialogue")),
                Creature = creature,
                Attacked = n.Flags("attacked"),
                Killed = n.Flags("killed"),
                Ai = reader.Ai(n),
                Merchant = n.Get("merchant") is ContentNode merchant ? MerchantDefinition.Read(merchant, compendium) : null,
                Companion = companion,
            });
        }

        foreach (ContentNode entry in j.Get("containers")?.Items() ?? Array.Empty<ContentNode>())
        {
            string containerId = entry.At("id").AsId();
            if (chapter.Containers.Any(other => other.Id == containerId))
            {
                throw entry.Fail("id", $"two containers have the id \"{containerId}\"");
            }
            List<string> items = entry.Texts("items");
            foreach (string item in items.Where(item => compendium.Item(item) == null))
            {
                throw entry.Fail("items", $"unknown item \"{item}\"");
            }
            LootTable loot = new();
            if (entry.Get("loot") is ContentNode lootNode)
            {
                loot = LootTable.Read(lootNode);
                compendium.CheckLoot(loot, entry, "loot");
            }
            var container = new ChapterContainer
            {
                Id = containerId,
                Name = entry.Name("name", "Chest"),
                At = ContentParts.CellFrom(entry.At("at")),
                Items = items,
                Coins = entry.Int("coins", 0, 0, 100000000),
                Loot = loot,
            };
            reader.Place(entry.At("at"), container.At, container.Name);
            chapter.Containers.Add(container);
        }

        string questsFile = "";
        QuestJournal quests = new();
        if (j.Get("quests") is ContentNode questsNode && questsNode.AsText().Length > 0)
        {
            questsFile = Resolve(files, folder, questsNode, questsNode.AsText());
            quests = QuestJournal.Read(ContentNode.Read(files, questsFile));
        }

        foreach (ContentNode change in j.Get("aiChanges")?.Items() ?? Array.Empty<ContentNode>())
        {
            ContentNode ai = reader.Ai(change) ?? throw change.Fail("ai", "each aiChanges entry needs an ai");
            var entry = new AiChange(change.Flags("when"), change.Text("creature", ""), change.Text("encounter", ""), change.Text("name", ""), ai);
            if (entry.Creature.Length > 0 && compendium.Creature(entry.Creature) == null)
            {
                throw change.Fail("creature", $"unknown creature \"{entry.Creature}\"");
            }
            if (entry.Encounter.Length > 0 && chapter.Encounters.All(e => e.Id != entry.Encounter))
            {
                throw change.Fail("encounter", $"unknown encounter \"{entry.Encounter}\"");
            }
            chapter.AiChanges.Add(entry);
        }

        string wipeCutscene = "";
        if (j.Get("onWipe") is ContentNode wipe)
        {
            wipe.RequireObject("is an object");
            wipe.Only("cutscene", "destination");
            if (wipe.Get("destination") is ContentNode destinations)
            {
                if (!destinations.IsArray || destinations.Count != chapter.Party.Count)
                {
                    throw destinations.Fail("needs one cell per party member");
                }
                foreach (ContentNode destination in destinations.Items())
                {
                    Cell cell = ContentParts.CellFrom(destination);
                    if (!map.Walkable(cell) || chapter.WipeDestination.Contains(cell))
                    {
                        throw destination.Fail("cells must be walkable and distinct");
                    }
                    if (chapter.Encounters.Any(e => e.Creatures.Any(c => c.At == cell)) || chapter.Npcs.Any(n => n.At == cell))
                    {
                        throw destination.Fail("overlaps a creature or NPC placement");
                    }
                    chapter.WipeDestination.Add(cell);
                }
            }
            if (wipe.Get("cutscene") is ContentNode scene && scene.AsText().Length > 0)
            {
                wipeCutscene = reader.Cutscene(scene);
            }
        }

        foreach (ContentNode t in j.Get("triggers")?.Items() ?? Array.Empty<ContentNode>())
        {
            var trigger = new ChapterTrigger(t.At("id").AsId(), t.Flags("when"),
                t.Get("dialogue") is ContentNode d ? reader.Dialogue(d) : "", t.Get("cutscene") is ContentNode c ? reader.Cutscene(c) : "");
            if (trigger.Dialogue.Length == 0 && trigger.Cutscene.Length == 0)
            {
                throw t.Fail($"trigger {trigger.Id} needs dialogue or cutscene");
            }
            chapter.Triggers.Add(trigger);
        }

        WinCondition? win = null;
        if (j.Get("winCondition") is ContentNode w)
        {
            w.RequireObject("is an object");
            win = new WinCondition(w.Flags("when"), w.Get("dialogue") is ContentNode d ? reader.Dialogue(d) : "",
                w.Get("cutscene") is ContentNode c ? reader.Cutscene(c) : "");
            if (win.When.Count == 0)
            {
                throw w.Fail("when", "needs at least one flag");
            }
        }

        chapter.ClearedCutscene = clearedCutscene;
        chapter.Surrender = surrender;
        chapter.QuestsFile = questsFile;
        chapter.Quests = quests;
        chapter.WipeCutscene = wipeCutscene;
        chapter.WinCondition = win;
        return chapter;
    }

    // A path written at `at`, looked up in the chapter folder first and then from the root.
    private static string Resolve(ContentFiles files, string folder, ContentNode at, string path)
    {
        if (!ContentFiles.IsContentPath(path))
        {
            throw at.Fail($"expected a relative content path: {path}");
        }
        string local = folder.Length == 0 ? path : folder + "/" + path;
        return files.Exists(local) ? local : path;
    }

    // What reading a chapter keeps track of: who stands where, and the files already checked.
    private sealed class Reader
    {
        private readonly ContentFiles _files;
        private readonly string _folder;
        private readonly Chapter _chapter;
        private readonly HashSet<Cell> _taken = new();

        public Reader(ContentFiles files, string folder, Chapter chapter)
        {
            _files = files;
            _folder = folder;
            _chapter = chapter;
        }

        public void Place(ContentNode at, Cell cell, string who)
        {
            if (!_chapter.Map.Walkable(cell))
            {
                throw at.Fail($"{who} starts on a cell you can't stand on");
            }
            if (!_taken.Add(cell))
            {
                throw at.Fail($"{who} starts on an occupied cell");
            }
        }

        // An optional "ai" entry: a profile name or an object of changes, checked and kept as written.
        public ContentNode? Ai(ContentNode owner)
        {
            if (owner.Get("ai") is not ContentNode ai)
            {
                return null;
            }
            AiProfile.Read(ai, _chapter.Compendium.AiNamed, _chapter.Compendium.AiNamed("cunning"));
            return ai;
        }

        public string Dialogue(ContentNode at)
        {
            return Dialogue(at, at.AsText());
        }

        public string Dialogue(ContentNode at, string written)
        {
            string path = Resolve(_files, _folder, at, written);
            if (!_chapter.Dialogues.ContainsKey(path))
            {
                Missing(at, path);
                _chapter.Dialogues[path] = global::Yorehold.Rules.Dialogue.Read(ContentNode.Read(_files, path));
            }
            return path;
        }

        public string Cutscene(ContentNode at)
        {
            string path = Resolve(_files, _folder, at, at.AsText());
            if (!_chapter.Cutscenes.ContainsKey(path))
            {
                Missing(at, path);
                _chapter.Cutscenes[path] = global::Yorehold.Rules.Cutscene.Read(ContentNode.Read(_files, path));
            }
            return path;
        }

        // Says which field named the file, not only that a file is missing.
        private void Missing(ContentNode at, string path)
        {
            if (!_files.Exists(path))
            {
                throw at.Fail($"missing file {path}");
            }
        }
    }
}
