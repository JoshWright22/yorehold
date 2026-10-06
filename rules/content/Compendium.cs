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

    /// <summary>
    /// Reads items/, classes/, ai/ and creatures/ under a folder ("" for the root). A file with the
    /// same id as one already loaded replaces it.
    /// </summary>
    public void Load(ContentFiles files, string folder)
    {
        string prefix = folder.Length == 0 ? "" : folder + "/";
        foreach (string path in files.List(prefix + "items"))
        {
            ContentNode node = ContentNode.Read(files, path);
            ItemDefinition item = ItemDefinition.Read(node);
            MatchName(node, item.Id, path);
            Items[item.Id] = item;
        }
        foreach (string path in files.List(prefix + "classes"))
        {
            ContentNode node = ContentNode.Read(files, path);
            ClassDefinition definition = ClassDefinition.Read(node);
            MatchName(node, definition.Id, path);
            CheckItems(node, "items", definition.Items);
            Classes[definition.Id] = definition;
        }
        LoadAi(files, prefix + "ai");
        foreach (string path in files.List(prefix + "creatures"))
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
        }
    }

    /// <summary>
    /// Reads feats/, races/, backgrounds/ and spells/ of a ruleset folder, then checks what they
    /// name: feats, races, classes, items, and the spells class files list.
    /// </summary>
    public void LoadOptions(ContentFiles files, string folder)
    {
        foreach (string path in files.List(folder + "/feats"))
        {
            ContentNode node = ContentNode.Read(files, path);
            FeatDefinition feat = FeatDefinition.Read(node);
            MatchName(node, feat.Id, path);
            Feats[feat.Id] = feat;
        }
        foreach (string path in files.List(folder + "/races"))
        {
            ContentNode node = ContentNode.Read(files, path);
            RaceDefinition race = RaceDefinition.Read(node);
            MatchName(node, race.Id, path);
            Races[race.Id] = race;
        }
        foreach (string path in files.List(folder + "/backgrounds"))
        {
            ContentNode node = ContentNode.Read(files, path);
            BackgroundDefinition background = BackgroundDefinition.Read(node);
            MatchName(node, background.Id, path);
            Backgrounds[background.Id] = background;
        }
        foreach (string path in files.List(folder + "/spells"))
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

        foreach (FeatDefinition feat in Feats.Values)
        {
            string file = $"{folder}/feats/{feat.Id}.json";
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
                    // Class files sit with the shared content, so their path is not under this folder.
                    string file = $"classes/{definition.Id}.json";
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
