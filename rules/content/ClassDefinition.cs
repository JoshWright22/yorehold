namespace Yorehold.Rules;

/// <summary>A resource on a sheet: potions 1 of 1.</summary>
public record Resource(int Current, int Max);

/// <summary>What a feat or a class feature gives.</summary>
public class Grants
{
    public List<Modifier> Modifiers { get; init; } = new();
    public List<string> Proficiencies { get; init; } = new();
    public Dictionary<string, string> Ranks { get; init; } = new();
    /// <summary>Added to these maximums.</summary>
    public Dictionary<string, int> Resources { get; init; } = new();

    public static Grants Read(ContentNode node)
    {
        return new Grants
        {
            Modifiers = ContentParts.ModifiersFrom(node, strict: true),
            Proficiencies = node.Ids("proficiencies").Distinct().ToList(),
            Ranks = ContentParts.NamesFrom(node, "ranks", ids: true),
            Resources = ContentParts.NumbersFrom(node, "resources", 1, 1000, idKeys: true),
        };
    }
}

public class ClassFeature
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public Grants Gives { get; init; } = new();
}

/// <summary>One row of a class level table: what reaching that level in the class brings.</summary>
public class ClassLevel
{
    public List<ClassFeature> Features { get; init; } = new();
    public Dictionary<string, string> Ranks { get; init; } = new();
    /// <summary>The kinds of feat the player may pick at this level, one pick each.</summary>
    public List<string> Feats { get; init; } = new();
    public int Skills { get; init; }
    /// <summary>Spell slots by slot level, as totals. A row without them keeps the previous row's.</summary>
    public SortedDictionary<int, int> Slots { get; init; } = new();
    public int Spells { get; init; }

    public static List<ClassLevel> ReadRows(ContentNode rows)
    {
        if (!rows.IsArray || rows.Count > 1000)
        {
            throw rows.Fail("is a list with one entry per class level");
        }
        var levels = new List<ClassLevel>();
        foreach (ContentNode row in rows.Items())
        {
            row.RequireObject("is an object");
            row.Only("features", "ranks", "feats", "skills", "slots", "spells");
            var features = new List<ClassFeature>();
            if (row.Get("features") is ContentNode list)
            {
                if (!list.IsArray || list.Count > 100)
                {
                    throw list.Fail("is a list");
                }
                foreach (ContentNode f in list.Items())
                {
                    f.RequireObject("is an object with an id");
                    f.Only("id", "name", "description", "modifiers", "proficiencies", "ranks", "resources");
                    string id = f.At("id").AsId();
                    features.Add(new ClassFeature
                    {
                        Id = id,
                        Name = f.Text("name", id, 64),
                        Description = f.Text("description", "", 4000),
                        Gives = Grants.Read(f),
                    });
                }
            }
            List<string> feats = row.Ids("feats");
            if (feats.Any(kind => !FeatDefinition.Kinds.Contains(kind)))
            {
                throw row.Fail("feats", "lists feat kinds: \"class\", \"skill\", \"general\" or \"race\"");
            }
            var slots = new SortedDictionary<int, int>();
            if (row.Get("slots") is ContentNode slotList)
            {
                if (!slotList.IsObject)
                {
                    throw slotList.Fail("maps a slot level to a number of slots");
                }
                foreach (KeyValuePair<string, ContentNode> member in slotList.Members())
                {
                    bool digits = member.Key.Length is 1 or 2 && member.Key.All(char.IsAsciiDigit);
                    if (!digits || int.Parse(member.Key) < 1 || !member.Value.IsWhole || member.Value.AsInt() < 0 || member.Value.AsInt() > 100)
                    {
                        throw member.Value.Fail("is a slot level from 1 with 0 to 100 slots");
                    }
                    slots[int.Parse(member.Key)] = member.Value.AsInt();
                }
            }
            levels.Add(new ClassLevel
            {
                Features = features,
                Ranks = ContentParts.NamesFrom(row, "ranks", ids: true),
                Feats = feats,
                Skills = row.Int("skills", 0, 0, 100),
                Slots = slots,
                Spells = row.Int("spells", 0, 0, 1000),
            });
        }
        return levels;
    }
}

/// <summary>One class file: classes/fighter.json. The top is the first-level character; levels is its table.</summary>
public class ClassDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public int HitDie { get; init; } = 8;
    /// <summary>Feet.</summary>
    public int Speed { get; init; } = 30;
    public int Darkvision { get; init; }
    public int BonusHp { get; init; }
    public List<string> Proficiencies { get; init; } = new();
    public Dictionary<string, string> ProficiencyRanks { get; init; } = new();
    public string DcAbility { get; init; } = "";
    public Dictionary<string, Resource> Resources { get; init; } = new();
    public List<string> Items { get; init; } = new();
    /// <summary>"known", "prepared" or "spontaneous".</summary>
    public string Casting { get; init; } = "known";
    /// <summary>Spell ids by spell level.</summary>
    public SortedDictionary<int, List<string>> Spells { get; init; } = new();
    public List<ClassLevel> Levels { get; init; } = new();

    public static ClassDefinition Read(ContentNode node)
    {
        node.RequireObject("a class is a JSON object");
        string id = node.At("id").AsId();
        string casting = node.Text("casting", "known");
        if (casting != "known" && casting != "prepared" && casting != "spontaneous")
        {
            throw node.Fail("casting", "is \"known\", \"prepared\" or \"spontaneous\"");
        }
        var spells = new SortedDictionary<int, List<string>>();
        if (node.Get("spells") is ContentNode spellList)
        {
            if (!spellList.IsObject)
            {
                throw spellList.Fail("maps a spell level to a list of spell ids");
            }
            foreach (KeyValuePair<string, ContentNode> member in spellList.Members())
            {
                bool digits = member.Key.Length is 1 or 2 && member.Key.All(char.IsAsciiDigit);
                if (!digits || !member.Value.IsArray)
                {
                    throw member.Value.Fail("is a spell level with a list of spell ids");
                }
                spells[int.Parse(member.Key)] = member.Value.Items().Select(spell => spell.AsId()).ToList();
            }
        }
        return new ClassDefinition
        {
            Id = id,
            Name = node.Text("name", id),
            Description = node.Text("description", ""),
            HitDie = node.Int("hitDie", 8, 1, 100),
            Speed = node.Int("speed", 30, 0, 1000),
            Darkvision = node.Int("darkvision", 0, 0, 10000),
            BonusHp = node.Int("bonusHp", 0, 0, 1000),
            Proficiencies = node.Texts("proficiencies").Distinct().ToList(),
            ProficiencyRanks = ContentParts.NamesFrom(node, "proficiencyRanks", ids: false),
            DcAbility = node.Text("dcAbility", "", 64),
            Resources = ResourcesFrom(node),
            Items = node.Texts("items"),
            Casting = casting,
            Spells = spells,
            Levels = node.Get("levels") is ContentNode levels ? ClassLevel.ReadRows(levels) : new List<ClassLevel>(),
        };
    }

    /// <summary>An optional "resources" object on a class or creature: {"potions": {"max": 1, "current": 1}}.</summary>
    public static Dictionary<string, Resource> ResourcesFrom(ContentNode node)
    {
        var resources = new Dictionary<string, Resource>();
        if (node.Get("resources") is not ContentNode list)
        {
            return resources;
        }
        if (!list.IsObject || list.Members().Count() > 1000)
        {
            throw list.Fail("is an object of resources");
        }
        foreach (KeyValuePair<string, ContentNode> member in list.Members())
        {
            if (member.Key.Length == 0 || member.Key.Length > 64)
            {
                throw member.Value.Fail("resource names are 1 to 64 characters");
            }
            member.Value.RequireObject("is an object with a max");
            int max = member.Value.At("max").AsInt(0, 100000);
            // Current defaults to full.
            resources[member.Key] = new Resource(member.Value.Int("current", max, 0, max), max);
        }
        return resources;
    }

    /// <summary>Rank choices name the ruleset's ranks and targets. Table-based rulesets keep them unused.</summary>
    public static void CheckRanks(Ruleset rules, Dictionary<string, string> ranks, string dcAbility, string file)
    {
        if (rules.ProficiencyRanks.Count == 0)
        {
            return;
        }
        if (dcAbility.Length > 0 && rules.Ability(dcAbility) == null)
        {
            throw new ContentException(file, "dcAbility", $"unknown ability \"{dcAbility}\"");
        }
        foreach (KeyValuePair<string, string> choice in ranks)
        {
            bool known = choice.Key is "weapons" or "armor" or "dc" || rules.Skill(choice.Key) != null || rules.Ability(choice.Key) != null;
            if (!known)
            {
                throw new ContentException(file, "proficiencyRanks." + choice.Key, "unknown proficiency target");
            }
            if (rules.Rank(choice.Value) == null)
            {
                throw new ContentException(file, "proficiencyRanks." + choice.Key, $"unknown rank \"{choice.Value}\"");
            }
        }
    }
}
