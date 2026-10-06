using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// Encounters mode of Create: a chapter's groups and the commands that change them. It draws
/// nothing, so tests and any layout use it as it is. It edits the "encounters" and "xpPerVictory"
/// of a chapter.json and writes the rest of the file back as it was, in its order. Every command
/// goes on the history it was given, which the whole open package shares.
/// </summary>
public sealed class EncountersEditor
{
    public const int MaxXp = 1000000;

    /// <summary>What a chapter can name: the creatures, AI profiles and items its package can see.</summary>
    public sealed class Catalog
    {
        public sealed record Creature(string Name, int Level, ContentColor Color, double Size);

        public SortedDictionary<string, Creature> Creatures { get; } = new(StringComparer.Ordinal);
        public SortedSet<string> Ai { get; } = new(StringComparer.Ordinal);
        /// <summary>Id to name.</summary>
        public SortedDictionary<string, string> Items { get; } = new(StringComparer.Ordinal);
        /// <summary>What the editor proposes for a fight: this much for each level of each creature in it.</summary>
        public int XpPerLevel { get; set; } = 25;

        public static Catalog From(Compendium compendium)
        {
            var catalog = new Catalog();
            foreach ((string id, CreatureDefinition creature) in compendium.Creatures)
            {
                catalog.Creatures[id] = new Creature(creature.Name.Length == 0 ? id : creature.Name, creature.Level, creature.Token.Color, creature.Token.Size);
            }
            foreach (string id in compendium.Ai.Keys)
            {
                catalog.Ai.Add(id);
            }
            foreach ((string id, ItemDefinition item) in compendium.Items)
            {
                catalog.Items[id] = item.Name.Length == 0 ? id : item.Name;
            }
            return catalog;
        }
    }

    public sealed record Placement
    {
        public string Creature { get; init; } = "";
        /// <summary>Empty = the creature's own.</summary>
        public string Name { get; init; } = "";
        public Cell At { get; init; }
        /// <summary>Degrees, 0 = east, 90 = south; null = toward the party's start.</summary>
        public double? Facing { get; init; }
        /// <summary>JSON: a profile's name or an object of changes; empty = none.</summary>
        public string Ai { get; init; } = "";
        /// <summary>Fields the editor has no tool for, as a JSON object; empty = none.</summary>
        public string Extra { get; init; } = "";
    }

    /// <summary>Creatures that wake up and fight together.</summary>
    public sealed record Group
    {
        public string Id { get; init; } = "";
        /// <summary>The line shown when the fight starts.</summary>
        public string Text { get; init; } = "";
        public List<Placement> Creatures { get; init; } = new();
        /// <summary>Story flags set when the party wins.</summary>
        public List<string> Set { get; init; } = new();
        /// <summary>JSON, for everyone in it; empty = none.</summary>
        public string Ai { get; init; } = "";
        /// <summary>Null = the chapter's xpPerVictory.</summary>
        public int? Xp { get; init; }
        /// <summary>Left with the last of them to fall.</summary>
        public LootTable Loot { get; init; } = new();
        public string Extra { get; init; } = "";

        public Group Copy() => this with { Creatures = new List<Placement>(Creatures), Set = new List<string>(Set) };
    }

    public enum FixedKind
    {
        Hero,
        Npc,
        Chest,
    }

    /// <summary>Who else stands on the map: heroes, NPCs and chests. Shown and kept clear of, not edited here.</summary>
    public sealed record Fixed(FixedKind Kind, string Name, Cell At, ContentColor Color);

    /// <summary>Error false: worth a look, but the chapter still saves and plays.</summary>
    public sealed record Problem(string Text, bool Error = true);

    // What an undo step puts back.
    private sealed class State
    {
        public List<Group> Groups = new();
        public int ChapterXp;
        /// <summary>The chapter's aiChanges as JSON; empty = none.</summary>
        public string AiChanges = "";

        public State Copy() => new() { Groups = Groups.Select(g => g.Copy()).ToList(), ChapterXp = ChapterXp, AiChanges = AiChanges };
    }

    private static readonly string[] GroupFields = { "id", "text", "creatures", "set", "ai", "xp", "loot" };
    private static readonly string[] PlacementFields = { "creature", "name", "at", "facing", "ai" };

    private readonly History _history;
    private bool _loaded;
    private string _document = "{}";
    private State _state = new();
    private Catalog _catalog = new();
    private Func<Cell, bool>? _walkable;
    private List<Fixed> _fixed = new();

    public EncountersEditor(History history)
    {
        _history = history;
    }

    public bool Loaded => _loaded;
    public IReadOnlyList<Group> Groups => _state.Groups;
    public IReadOnlyList<Fixed> FixedOnes => _fixed;
    public Catalog Names => _catalog;
    public int ChapterXp => _state.ChapterXp;

    /// <summary>
    /// Reads a chapter.json. walkable says where someone can stand on the chapter's map as it is
    /// now; without it any cell from 0, 0 will do. False with the reason for a file the editor can't take.
    /// </summary>
    public bool Load(string text, out string error, Catalog? catalog = null, Func<Cell, bool>? walkable = null)
    {
        error = "";
        try
        {
            ContentNode j = ContentNode.Parse("chapter.json", text);
            j.RequireObject("a chapter is an object");
            var state = new State { ChapterXp = j.Int("xpPerVictory", 0) };
            if (j.Get("aiChanges") is ContentNode changes)
            {
                if (!changes.IsArray)
                {
                    throw changes.Fail("must be an array");
                }
                state.AiChanges = changes.Count == 0 ? "" : CreateJson.Compact(JsonNode.Parse(changes.Raw()));
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (j.Get("encounters") is ContentNode encounters && !encounters.IsArray)
            {
                throw encounters.Fail("must be an array");
            }
            foreach (ContentNode e in j.Get("encounters")?.Items() ?? Array.Empty<ContentNode>())
            {
                e.RequireObject("each encounter is an object");
                string id = e.Text("id", $"encounter {state.Groups.Count + 1}");
                if (id.Length == 0 || !ids.Add(id))
                {
                    throw e.Fail("id", $"duplicate encounter {id}");
                }
                if (e.Get("creatures") is ContentNode list && !list.IsArray)
                {
                    throw list.Fail("must be an array");
                }
                var creatures = new List<Placement>();
                foreach (ContentNode p in e.Get("creatures")?.Items() ?? Array.Empty<ContentNode>())
                {
                    p.RequireObject("each creature is an object");
                    creatures.Add(new Placement
                    {
                        Creature = p.At("creature").AsText(),
                        Name = p.Text("name", ""),
                        At = ContentParts.CellFrom(p.At("at")),
                        Facing = p.Get("facing")?.AsNumber(),
                        Ai = p.Get("ai") is ContentNode ai ? CreateJson.Compact(JsonNode.Parse(ai.Raw())) : "",
                        Extra = ExtraOf(p, PlacementFields),
                    });
                }
                state.Groups.Add(new Group
                {
                    Id = id,
                    Text = e.Text("text", ""),
                    Creatures = creatures,
                    Set = e.Texts("set"),
                    Ai = e.Get("ai") is ContentNode groupAi ? CreateJson.Compact(JsonNode.Parse(groupAi.Raw())) : "",
                    Xp = e.Get("xp")?.AsInt(0, MaxXp),
                    Loot = e.Get("loot") is ContentNode loot ? LootTable.Read(loot) : new LootTable(),
                    Extra = ExtraOf(e, GroupFields),
                });
            }

            // everyone else with a cell of their own
            var others = new List<Fixed>();
            void Others(string key, FixedKind kind, ContentColor color, string unnamed)
            {
                if (j.Get(key) is not ContentNode list || !list.IsArray)
                {
                    return;
                }
                foreach (ContentNode entry in list.Items())
                {
                    if (entry.IsObject && entry.Has("at"))
                    {
                        others.Add(new Fixed(kind, entry.Text("name", unnamed), ContentParts.CellFrom(entry.At("at")),
                            entry.Get("color") is ContentNode c ? ContentParts.ColorFrom(c) : color));
                    }
                }
            }
            Others("party", FixedKind.Hero, new ContentColor(200, 200, 210), "Hero");
            Others("npcs", FixedKind.Npc, new ContentColor(200, 180, 140), "NPC");
            Others("containers", FixedKind.Chest, new ContentColor(200, 160, 70), "Chest");

            _document = text;
            _state = state;
            _catalog = catalog ?? new Catalog();
            _walkable = walkable;
            _fixed = others;
            _loaded = true;
            return true;
        }
        catch (ContentException problem)
        {
            error = problem.Message;
            return false;
        }
    }

    /// <summary>The chapter.json to save. A group with nobody in it is left out: the game refuses one.</summary>
    public string ToJson()
    {
        if (!_loaded)
        {
            return "{}";
        }
        // Load parsed it already, so it is an object
        JsonObject j = JsonNode.Parse(_document)!.AsObject();
        var encounters = new JsonArray();
        string changes = _state.AiChanges;
        foreach (Group group in _state.Groups)
        {
            if (group.Creatures.Count == 0)
            {
                changes = Retarget(changes, group.Id, "");
                continue;
            }
            var e = new JsonObject { ["id"] = group.Id };
            if (group.Set.Count > 0)
            {
                e["set"] = new JsonArray(group.Set.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray());
            }
            if (group.Text.Length > 0)
            {
                e["text"] = group.Text;
            }
            if (group.Xp is int xp)
            {
                e["xp"] = xp;
            }
            if (group.Ai.Length > 0)
            {
                e["ai"] = JsonNode.Parse(group.Ai);
            }
            if (!group.Loot.IsEmpty)
            {
                e["loot"] = LootJson(group.Loot);
            }
            AddExtra(e, group.Extra);
            var creatures = new JsonArray();
            foreach (Placement p in group.Creatures)
            {
                var c = new JsonObject { ["creature"] = p.Creature };
                if (p.Name.Length > 0)
                {
                    c["name"] = p.Name;
                }
                if (p.Facing is double facing)
                {
                    c["facing"] = facing;
                }
                c["at"] = new JsonArray(p.At.X, p.At.Y);
                if (p.Ai.Length > 0)
                {
                    c["ai"] = JsonNode.Parse(p.Ai);
                }
                AddExtra(c, p.Extra);
                creatures.Add(c);
            }
            e["creatures"] = creatures;
            encounters.Add(e);
        }
        j["encounters"] = encounters;
        if (_state.ChapterXp != 0 || j.ContainsKey("xpPerVictory"))
        {
            j["xpPerVictory"] = _state.ChapterXp;
        }
        if (changes.Length > 0)
        {
            j["aiChanges"] = JsonNode.Parse(changes);
        }
        else
        {
            j.Remove("aiChanges");
        }
        return CreateJson.Write(j);
    }

    /// <summary>A loot table as the game reads it.</summary>
    public static JsonObject LootJson(LootTable loot)
    {
        var j = new JsonObject();
        if (loot.Coins.Length > 0)
        {
            j["coins"] = loot.Coins;
        }
        if (loot.Items.Count > 0)
        {
            j["items"] = new JsonArray(loot.Items.Select(i => (JsonNode?)new JsonObject { ["item"] = i.Item, ["chance"] = i.Chance, ["quantity"] = i.Quantity }).ToArray());
        }
        return j;
    }

    /// <summary>What winning the fight a group starts gives each hero.</summary>
    public int XpOf(int group)
    {
        return group >= 0 && group < _state.Groups.Count && _state.Groups[group].Xp is int xp ? xp : _state.ChapterXp;
    }

    /// <summary>From the levels of the creatures in it.</summary>
    public int ProposedXp(int group)
    {
        if (group < 0 || group >= _state.Groups.Count)
        {
            return 0;
        }
        int levels = _state.Groups[group].Creatures.Sum(p => _catalog.Creatures.TryGetValue(p.Creature, out Catalog.Creature? c) ? Math.Max(1, c.Level) : 1);
        return Math.Min(MaxXp, levels * _catalog.XpPerLevel);
    }

    /// <summary>Where a placed creature looks until it notices the party, in degrees.</summary>
    public double FacingOf(Placement p)
    {
        if (p.Facing is double facing)
        {
            return facing;
        }
        // nobody told it where to look: it watches the way the party comes from, as in the game
        foreach (Fixed other in _fixed.Where(f => f.Kind == FixedKind.Hero))
        {
            if (other.At == p.At)
            {
                return 0;
            }
            double degrees = Math.Atan2(other.At.Y - p.At.Y, other.At.X - p.At.X) * 180 / Math.PI;
            return degrees < 0 ? degrees + 360 : degrees;
        }
        return 0;
    }

    /// <summary>The name of the profile an "ai" entry gives, "" for none and "custom" for an object of changes.</summary>
    public static string ProfileOf(string ai)
    {
        if (ai.Length == 0)
        {
            return "";
        }
        try
        {
            return JsonNode.Parse(ai) is JsonValue value && value.TryGetValue(out string? name) ? name : "custom";
        }
        catch (JsonException)
        {
            return "custom";
        }
    }

    /// <summary>The group and place in it of the creature on a cell.</summary>
    public (int Group, int Index)? CreatureAt(Cell cell)
    {
        for (int g = 0; g < _state.Groups.Count; g++)
        {
            for (int c = 0; c < _state.Groups[g].Creatures.Count; c++)
            {
                if (_state.Groups[g].Creatures[c].At == cell)
                {
                    return (g, c);
                }
            }
        }
        return null;
    }

    /// <summary>Someone could be put there: it can be stood on and nobody is on it.</summary>
    public bool Free(Cell cell)
    {
        if (!_loaded || !Standable(cell) || CreatureAt(cell) != null)
        {
            return false;
        }
        return _fixed.All(f => f.At != cell);
    }

    // ---------------------------------------------------------------- groups

    /// <summary>An empty id gets the next free "encounter-N".</summary>
    public int? AddGroup(string id = "")
    {
        bool Taken(string name) => _state.Groups.Any(g => g.Id == name);
        if (!_loaded || id.Length > 64 || (id.Length > 0 && Taken(id)))
        {
            return null;
        }
        for (int n = _state.Groups.Count + 1; id.Length == 0; n++)
        {
            if (!Taken($"encounter-{n}"))
            {
                id = $"encounter-{n}";
            }
        }
        Edit("Add group", () => _state.Groups.Add(new Group { Id = id }));
        return _state.Groups.Count - 1;
    }

    /// <summary>Story changes to its creatures' AI (aiChanges naming it) go with it.</summary>
    public bool RemoveGroup(int group)
    {
        if (!HasGroup(group))
        {
            return false;
        }
        Edit("Remove group", () =>
        {
            _state.AiChanges = Retarget(_state.AiChanges, _state.Groups[group].Id, "");
            _state.Groups.RemoveAt(group);
        });
        return true;
    }

    /// <summary>Ids are unique, 1 to 64 characters. aiChanges that name the group follow the new id.</summary>
    public bool SetGroupId(int group, string id)
    {
        if (!HasGroup(group) || id.Length == 0 || id.Length > 64 || _state.Groups.Any(g => g.Id == id))
        {
            return false;
        }
        Edit("Rename group", () =>
        {
            _state.AiChanges = Retarget(_state.AiChanges, _state.Groups[group].Id, id);
            _state.Groups[group] = _state.Groups[group] with { Id = id };
        }, $"group-id-{group}");
        return true;
    }

    public bool SetGroupText(int group, string text)
    {
        if (!HasGroup(group) || text.Length > 400 || _state.Groups[group].Text == text)
        {
            return false;
        }
        Edit("Change group line", () => _state.Groups[group] = _state.Groups[group] with { Text = text }, $"group-text-{group}");
        return true;
    }

    public bool SetGroupFlags(int group, IReadOnlyList<string> flags)
    {
        if (!HasGroup(group) || _state.Groups[group].Set.SequenceEqual(flags) || flags.Any(f => f.Length == 0 || f.Length > 64))
        {
            return false;
        }
        var copy = flags.ToList();
        Edit("Change group flags", () => _state.Groups[group] = _state.Groups[group] with { Set = copy }, $"group-flags-{group}");
        return true;
    }

    /// <summary>A profile from the catalog, or "" for none.</summary>
    public bool SetGroupAi(int group, string profile)
    {
        if (!HasGroup(group) || (profile.Length > 0 && !_catalog.Ai.Contains(profile)))
        {
            return false;
        }
        string ai = profile.Length == 0 ? "" : CreateJson.Compact(JsonValue.Create(profile));
        if (_state.Groups[group].Ai == ai)
        {
            return false;
        }
        Edit("Change group AI", () => _state.Groups[group] = _state.Groups[group] with { Ai = ai });
        return true;
    }

    public bool SetGroupXp(int group, int? xp)
    {
        if (!HasGroup(group) || _state.Groups[group].Xp == xp || xp is < 0 or > MaxXp)
        {
            return false;
        }
        Edit("Change group XP", () => _state.Groups[group] = _state.Groups[group] with { Xp = xp }, $"group-xp-{group}");
        return true;
    }

    public bool SetGroupLoot(int group, LootTable loot)
    {
        if (!HasGroup(group))
        {
            return false;
        }
        // through the file's own reader, so dice, chances and quantities are checked the way the game will
        string text = CreateJson.Compact(LootJson(loot));
        if (text == CreateJson.Compact(LootJson(_state.Groups[group].Loot)) || loot.Items.Any(i => !_catalog.Items.ContainsKey(i.Item)))
        {
            return false;
        }
        LootTable read;
        try
        {
            read = LootTable.Read(ContentNode.Parse("loot", text));
        }
        catch (ContentException)
        {
            return false;
        }
        Edit("Change group loot", () => _state.Groups[group] = _state.Groups[group] with { Loot = read }, $"group-loot-{group}");
        return true;
    }

    public bool SetChapterXp(int xp)
    {
        if (!_loaded || xp < 0 || xp > MaxXp || _state.ChapterXp == xp)
        {
            return false;
        }
        Edit("Change chapter XP", () => _state.ChapterXp = xp, "chapter-xp");
        return true;
    }

    // ---------------------------------------------------------------- creatures

    public int? AddCreature(int group, string creature, Cell at)
    {
        if (!HasGroup(group) || !_catalog.Creatures.TryGetValue(creature, out Catalog.Creature? found) || !Free(at))
        {
            return null;
        }
        Edit("Place " + found.Name, () => _state.Groups[group].Creatures.Add(new Placement { Creature = creature, At = at }));
        return _state.Groups[group].Creatures.Count - 1;
    }

    public bool MoveCreature(int group, int index, Cell at)
    {
        if (PlacementOf(group, index) is not Placement p || p.At == at || !Free(at))
        {
            return false;
        }
        Edit("Move creature", () => Change(group, index, p with { At = at }));
        return true;
    }

    public bool RemoveCreature(int group, int index)
    {
        if (PlacementOf(group, index) == null)
        {
            return false;
        }
        Edit("Remove creature", () => _state.Groups[group].Creatures.RemoveAt(index));
        return true;
    }

    public bool SetCreatureName(int group, int index, string name)
    {
        if (PlacementOf(group, index) is not Placement p || name.Length > 64 || p.Name == name)
        {
            return false;
        }
        Edit("Rename creature", () => Change(group, index, p with { Name = name }), $"creature-name-{group}-{index}");
        return true;
    }

    /// <summary>Degrees from -360 to 360, or null to look toward the party's start.</summary>
    public bool SetFacing(int group, int index, double? degrees)
    {
        if (PlacementOf(group, index) is not Placement p || p.Facing == degrees || (degrees is double d && (!double.IsFinite(d) || Math.Abs(d) > 360)))
        {
            return false;
        }
        Edit("Turn creature", () => Change(group, index, p with { Facing = degrees }));
        return true;
    }

    public bool SetCreatureAi(int group, int index, string profile)
    {
        if (PlacementOf(group, index) is not Placement p || (profile.Length > 0 && !_catalog.Ai.Contains(profile)))
        {
            return false;
        }
        string ai = profile.Length == 0 ? "" : CreateJson.Compact(JsonValue.Create(profile));
        if (p.Ai == ai)
        {
            return false;
        }
        Edit("Change creature AI", () => Change(group, index, p with { Ai = ai }));
        return true;
    }

    /// <summary>Its place at the end of the other group.</summary>
    public int? MoveToGroup(int group, int index, int toGroup)
    {
        if (PlacementOf(group, index) is not Placement p || !HasGroup(toGroup) || toGroup == group)
        {
            return null;
        }
        Edit("Move to " + _state.Groups[toGroup].Id, () =>
        {
            _state.Groups[toGroup].Creatures.Add(p);
            _state.Groups[group].Creatures.RemoveAt(index);
        });
        return _state.Groups[toGroup].Creatures.Count - 1;
    }

    /// <summary>Typing in a box is one undo step until this is called.</summary>
    public void EndTyping() => _history.BreakMerge();

    public List<Problem> Problems()
    {
        var found = new List<Problem>();
        if (!_loaded)
        {
            return found;
        }
        bool KnownAi(string ai)
        {
            string profile = ProfileOf(ai);
            return profile.Length == 0 || profile == "custom" || _catalog.Ai.Contains(profile);
        }
        var taken = _fixed.Select(f => f.At).ToList();
        foreach (Group group in _state.Groups)
        {
            if (group.Creatures.Count == 0)
            {
                found.Add(new Problem($"{group.Id} has no creatures and won't be saved", false));
            }
            if (!KnownAi(group.Ai))
            {
                found.Add(new Problem($"{group.Id} names the AI profile {ProfileOf(group.Ai)}, which isn't in this package"));
            }
            foreach (LootEntry entry in group.Loot.Items.Where(i => !_catalog.Items.ContainsKey(i.Item)))
            {
                found.Add(new Problem($"{group.Id} loot has the unknown item {entry.Item}"));
            }
            foreach (Placement p in group.Creatures)
            {
                string who = $"{(p.Name.Length == 0 ? p.Creature : p.Name)} ({group.Id})";
                if (!_catalog.Creatures.ContainsKey(p.Creature))
                {
                    found.Add(new Problem($"{who} is the unknown creature {p.Creature}"));
                }
                if (!KnownAi(p.Ai))
                {
                    found.Add(new Problem($"{who} names the AI profile {ProfileOf(p.Ai)}, which isn't in this package"));
                }
                if (!Standable(p.At))
                {
                    found.Add(new Problem($"{who} is on a cell nobody can stand on"));
                }
                if (taken.Contains(p.At))
                {
                    found.Add(new Problem($"{who} shares a cell with someone"));
                }
                taken.Add(p.At);
            }
        }
        return found;
    }

    private bool Standable(Cell cell) => _walkable != null ? _walkable(cell) : cell.X >= 0 && cell.Y >= 0;

    private bool HasGroup(int group) => _loaded && group >= 0 && group < _state.Groups.Count;

    private Placement? PlacementOf(int group, int index)
    {
        return HasGroup(group) && index >= 0 && index < _state.Groups[group].Creatures.Count ? _state.Groups[group].Creatures[index] : null;
    }

    private void Change(int group, int index, Placement p) => _state.Groups[group].Creatures[index] = p;

    // Runs change and records it with what was there before and after.
    private void Edit(string label, Action change, string mergeKey = "")
    {
        State before = _state.Copy();
        change();
        State after = _state.Copy();
        _history.Record(label, () => _state = after.Copy(), () => _state = before.Copy(), mergeKey);
    }

    // The fields of an entry the editor has no tool for, as a JSON object; empty if there are none.
    private static string ExtraOf(ContentNode entry, string[] known)
    {
        var rest = new JsonObject();
        foreach (KeyValuePair<string, ContentNode> member in entry.Members())
        {
            if (Array.IndexOf(known, member.Key) < 0)
            {
                rest[member.Key] = JsonNode.Parse(member.Value.Raw());
            }
        }
        return rest.Count == 0 ? "" : CreateJson.Compact(rest);
    }

    private static void AddExtra(JsonObject entry, string extra)
    {
        if (extra.Length == 0)
        {
            return;
        }
        // only ExtraOf writes these, so it is an object
        foreach (KeyValuePair<string, JsonNode?> field in JsonNode.Parse(extra)!.AsObject())
        {
            entry[field.Key] = field.Value?.DeepClone();
        }
    }

    // The chapter's aiChanges with the encounter from called to, or, with no to, without the entries that name it.
    private static string Retarget(string changes, string from, string to)
    {
        if (changes.Length == 0)
        {
            return changes;
        }
        var after = new JsonArray();
        foreach (JsonNode? change in JsonNode.Parse(changes)!.AsArray())
        {
            bool names = change is JsonObject o && o["encounter"] is JsonValue v && v.TryGetValue(out string? id) && id == from;
            if (names && to.Length == 0)
            {
                continue;
            }
            JsonNode? copy = change?.DeepClone();
            if (names)
            {
                copy!["encounter"] = to;
            }
            after.Add(copy);
        }
        return after.Count == 0 ? "" : CreateJson.Compact(after);
    }
}
