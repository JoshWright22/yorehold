namespace Yorehold.Rules;

/// <summary>
/// Every definition a chapter can use, by id: the shared files first, then the chapter's own on
/// top, then the ruleset's player options.
/// </summary>
public class Compendium
{
    public SortedDictionary<string, ItemDefinition> Items { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, ClassDefinition> Classes { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, CreatureDefinition> Creatures { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, AiProfile> Ai { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, RaceDefinition> Races { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, BackgroundDefinition> Backgrounds { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, FeatDefinition> Feats { get; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, SpellDefinition> Spells { get; } = new(StringComparer.Ordinal);
    /// <summary>Picks of the system's own option kinds (options/), by id.</summary>
    public SortedDictionary<string, OptionDefinition> Options { get; } = new(StringComparer.Ordinal);

    public Compendium()
    {
        foreach (string name in new[] { "mindless", "animal", "cunning", "tactical" })
        {
            // Preset only returns null for other names.
            Ai[name] = AiProfile.Preset(name)!;
        }
    }

    public ItemDefinition? Item(string id) => Items.GetValueOrDefault(id);
    public ClassDefinition? Class(string id) => Classes.GetValueOrDefault(id);
    public CreatureDefinition? Creature(string id) => Creatures.GetValueOrDefault(id);
    public AiProfile? AiNamed(string name) => Ai.GetValueOrDefault(name);

    /// <summary>The profile a creature file asks for; "cunning" when it asks for none.</summary>
    public AiProfile AiFor(CreatureDefinition creature)
    {
        return creature.Ai is ContentNode node ? AiProfile.Read(node, AiNamed) : AiProfile.Read(ContentNode.Parse("ai", "\"cunning\""), AiNamed);
    }

    /// <summary>The game's own definitions: its system's folder, then anything older at the root.</summary>
    public static Compendium OfGame(ContentFiles files)
    {
        var compendium = new Compendium();
        compendium.Load(files, RulesFolder.Default, "");
        return compendium;
    }

    /// <summary>Where an entry was read from, by kind folder and id ("creatures", "goblin"), for messages.</summary>
    public string PathOf(string kind, string id) => _paths.GetValueOrDefault($"{kind}/{id}", $"{kind}/{id}.json");

    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);

    /// <summary>
    /// Reads items/, classes/, ai/ and creatures/ under each folder in turn ("" for the root): a
    /// chapter's ruleset folder, then the root, then the chapter. Each kind is read from every
    /// folder before the next kind, so a creature anywhere can carry an item or use an AI profile
    /// from any of them. A file with the same id as one already loaded replaces it.
    /// </summary>
    public void Load(ContentFiles files, params string[] folders)
    {
        string[] prefixes = folders.Select(folder => folder.Length == 0 ? "" : folder + "/").Distinct().ToArray();
        foreach (string path in prefixes.SelectMany(prefix => files.List(prefix + "items")))
        {
            ContentNode node = ContentNode.Read(files, path);
            ItemDefinition item = ItemDefinition.Read(node);
            MatchName(node, item.Id, path);
            Items[item.Id] = item;
            _paths["items/" + item.Id] = path;
        }
        foreach (string path in prefixes.SelectMany(prefix => files.List(prefix + "classes")))
        {
            ContentNode node = ContentNode.Read(files, path);
            ClassDefinition definition = ClassDefinition.Read(node);
            MatchName(node, definition.Id, path);
            CheckItems(node, "items", definition.Items);
            Classes[definition.Id] = definition;
            _paths["classes/" + definition.Id] = path;
        }
        foreach (string prefix in prefixes)
        {
            LoadAi(files, prefix + "ai");
        }
        foreach (string path in prefixes.SelectMany(prefix => files.List(prefix + "creatures")))
        {
            ContentNode node = ContentNode.Read(files, path);
            CreatureDefinition creature = CreatureDefinition.Read(node);
            MatchName(node, creature.Id, path);
            CheckItems(node, "items", creature.Items);
            CheckLoot(creature.Loot, node, "loot");
            if (creature.Ai is ContentNode ai)
            {
                AiProfile.Read(ai, AiNamed);
            }
            Creatures[creature.Id] = creature;
            _paths["creatures/" + creature.Id] = path;
        }
    }

    /// <summary>
    /// Reads feats/, races/, backgrounds/ and spells/ of a ruleset folder, then checks what they
    /// name: feats, races, classes, items, and the spells class files list.
    /// </summary>
    // "rulesets/pf2e" and "feats" is "rulesets/pf2e/feats"; the root ("", where a content set keeps them) is "feats"
    private static string Under(string folder, string kind) => folder.Length == 0 ? kind : folder + "/" + kind;

    public void LoadOptions(ContentFiles files, string folder)
    {
        foreach (string path in files.List(Under(folder, "feats")))
        {
            ContentNode node = ContentNode.Read(files, path);
            FeatDefinition feat = FeatDefinition.Read(node);
            MatchName(node, feat.Id, path);
            Feats[feat.Id] = feat;
        }
        foreach (string path in files.List(Under(folder, "races")))
        {
            ContentNode node = ContentNode.Read(files, path);
            RaceDefinition race = RaceDefinition.Read(node);
            MatchName(node, race.Id, path);
            Races[race.Id] = race;
        }
        foreach (string path in files.List(Under(folder, "backgrounds")))
        {
            ContentNode node = ContentNode.Read(files, path);
            BackgroundDefinition background = BackgroundDefinition.Read(node);
            MatchName(node, background.Id, path);
            Backgrounds[background.Id] = background;
        }
        foreach (string path in files.List(Under(folder, "spells")))
        {
            ContentNode node = ContentNode.Read(files, path);
            SpellDefinition spell = SpellDefinition.Read(node);
            MatchName(node, spell.Id, path);
            if (!ContentIds.IsId(spell.Id))
            {
                throw node.Fail("id", "uses a-z, 0-9, - and _");
            }
            Spells[spell.Id] = spell;
        }

        foreach (string path in files.List(Under(folder, "options")))
        {
            ContentNode node = ContentNode.Read(files, path);
            OptionDefinition option = OptionDefinition.Read(node);
            MatchName(node, option.Id, path);
            Options[option.Id] = option;
        }
        foreach (OptionDefinition option in Options.Values)
        {
            string file = $"{folder}/options/{option.Id}.json";
            if (option.Races.FirstOrDefault(race => !Races.ContainsKey(race)) is string race)
            {
                throw new ContentException(file, "races", $"no race \"{race}\"");
            }
            if (option.Classes.FirstOrDefault(c => !Classes.ContainsKey(c)) is string needed)
            {
                throw new ContentException(file, "classes", $"no class \"{needed}\"");
            }
            if (option.Feats.FirstOrDefault(f => !Feats.ContainsKey(f)) is string feat)
            {
                throw new ContentException(file, "feats", $"no feat \"{feat}\"");
            }
        }

        foreach (FeatDefinition feat in Feats.Values)
        {
            string file = $"{folder}/feats/{feat.Id}.json";
            if (feat.Gives.Spells.FirstOrDefault(s => !Spells.ContainsKey(s)) is string unknown)
            {
                throw new ContentException(file, "spells", $"no spell \"{unknown}\"");
            }
            foreach (string race in feat.Needs.Races.Where(race => !Races.ContainsKey(race)))
            {
                throw new ContentException(file, "requires.races", $"no race \"{race}\"");
            }
            foreach (string needed in feat.Needs.Classes.Where(needed => !Classes.ContainsKey(needed)))
            {
                throw new ContentException(file, "requires.classes", $"no class \"{needed}\"");
            }
        }
        foreach (RaceDefinition race in Races.Values)
        {
            foreach (string feat in race.Feats.Where(feat => !Feats.ContainsKey(feat)))
            {
                throw new ContentException($"{folder}/races/{race.Id}.json", "feats", $"no feat \"{feat}\"");
            }
        }
        foreach (BackgroundDefinition background in Backgrounds.Values)
        {
            string file = $"{folder}/backgrounds/{background.Id}.json";
            foreach (string feat in background.Feats.Where(feat => !Feats.ContainsKey(feat)))
            {
                throw new ContentException(file, "feats", $"no feat \"{feat}\"");
            }
            foreach (string item in background.Items.Where(item => !Items.ContainsKey(item)))
            {
                throw new ContentException(file, "items", $"no item \"{item}\"");
            }
        }
        foreach (CreatureDefinition creature in Creatures.Values)
        {
            foreach (string id in creature.Spells.Where(id => !Spells.ContainsKey(id)))
            {
                throw new ContentException(PathOf("creatures", creature.Id), "spells", $"no spell \"{id}\"");
            }
        }
        // A ruleset with no spells at all simply has no casting: shared classes still load under it.
        if (Spells.Count == 0)
        {
            return;
        }
        foreach (ClassDefinition definition in Classes.Values)
        {
            foreach (KeyValuePair<int, List<string>> level in definition.Spells)
            {
                foreach (string id in level.Value)
                {
                    string file = PathOf("classes", definition.Id);
                    if (!Spells.TryGetValue(id, out SpellDefinition? spell))
                    {
                        throw new ContentException(file, $"spells.{level.Key}", $"no spell \"{id}\"");
                    }
                    if (spell.Level != level.Key)
                    {
                        throw new ContentException(file, $"spells.{level.Key}", $"\"{id}\" is a level {spell.Level} spell");
                    }
                }
            }
        }
    }

    public void CheckLoot(LootTable table, ContentNode owner, string field)
    {
        foreach (LootEntry entry in table.Items)
        {
            if (!Items.ContainsKey(entry.Item))
            {
                throw owner.Fail(field, $"unknown item \"{entry.Item}\"");
            }
        }
    }

    // AI profiles can build on each other in any order, so keep resolving until nothing is left,
    // or nothing more can be: a base that doesn't exist, or two that name each other.
    private void LoadAi(ContentFiles files, string folder)
    {
        var waiting = new SortedDictionary<string, ContentNode>(StringComparer.Ordinal);
        foreach (string path in files.List(folder))
        {
            ContentNode node = ContentNode.Read(files, path);
            node.RequireObject("AI profiles are objects with an id");
            MatchName(node, node.At("id").AsId(), path);
            waiting[ContentFiles.Stem(path)] = node;
        }
        var blank = new AiProfile { Base = "custom" };
        while (waiting.Count > 0)
        {
            ContentException? failed = null;
            var resolved = new List<string>();
            foreach (KeyValuePair<string, ContentNode> entry in waiting)
            {
                // A profile may not build on itself, or on one that is still waiting.
                AiProfile? Known(string name) => waiting.ContainsKey(name) && !resolved.Contains(name) ? null : AiNamed(name);
                try
                {
                    Ai[entry.Key] = AiProfile.Read(entry.Value, Known, blank) with { Base = entry.Key };
                    resolved.Add(entry.Key);
                }
                catch (ContentException error)
                {
                    failed = error;
                }
            }
            if (resolved.Count == 0)
            {
                // The loop only runs with something waiting, so one of them failed.
                throw failed!;
            }
            resolved.ForEach(id => waiting.Remove(id));
        }
    }

    private void CheckItems(ContentNode node, string field, List<string> ids)
    {
        foreach (string id in ids)
        {
            if (!Items.ContainsKey(id))
            {
                throw node.Fail(field, $"unknown item \"{id}\"");
            }
        }
    }

    private static void MatchName(ContentNode node, string id, string path)
    {
        if (id != ContentFiles.Stem(path))
        {
            throw node.Fail("id", $"\"{id}\" doesn't match the file name");
        }
    }
}
