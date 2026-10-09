using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Yorehold.Rules;

/// <summary>
/// Brings content a creator exported from their own Foundry VTT world (an Item or an Actor, or a
/// list of them, as "Export Data" writes them) into this game's files: dnd5e and pf2e weapons,
/// armour, spells, feats and creatures. What has no place here yet is left out and named in the
/// report, so nothing is lost silently. The result is files to write, keyed by their content
/// path ("items/longsword.json"), each in the shape the game's own readers load.
/// </summary>
public sealed class FoundryImport
{
    public Dictionary<string, JsonObject> Files { get; } = new(StringComparer.Ordinal);
    public List<string> Report { get; } = new();

    // every document in the export by its Foundry id, and the ids a class grants as features
    private readonly Dictionary<string, JsonObject> _documents = new(StringComparer.Ordinal);
    private readonly HashSet<string> _granted = new(StringComparer.Ordinal);
    // the files written for the document being read, in order
    private readonly List<string> _written = new();

    /// <summary>Reads exported JSON: one document or a list. Unknown kinds are reported, not refused.</summary>
    public static FoundryImport Read(string json)
    {
        var import = new FoundryImport();
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (System.Text.Json.JsonException error)
        {
            import.Report.Add("not JSON: " + error.Message);
            return import;
        }
        List<JsonObject> documents = (root is JsonArray list ? list : new JsonArray(root?.DeepClone())).OfType<JsonObject>().ToList();
        // a class's features are feats it grants by id; those in the same export become its features, not feats
        foreach (JsonObject document in documents)
        {
            if (Text(document["_id"]) is { Length: > 0 } id)
            {
                import._documents[id] = document;
            }
        }
        foreach (JsonObject document in documents.Where(d => Text(d["type"]) == "class"))
        {
            foreach (JsonObject grant in Advancements(document["system"] as JsonObject).Where(a => Text(a["type"]) == "ItemGrant"))
            {
                foreach (JsonNode? item in grant["configuration"]?["items"] as JsonArray ?? new JsonArray())
                {
                    import._granted.Add(GrantedId(item));
                }
            }
            // pf2e lists its features under items, each with the level it comes at
            foreach (JsonObject item in (document["system"]?["items"] as JsonObject ?? new JsonObject()).Select(p => p.Value).OfType<JsonObject>())
            {
                import._granted.Add(GrantedId(item));
            }
        }
        foreach (JsonObject document in documents)
        {
            try
            {
                import.Add(document);
            }
            catch (InvalidOperationException error)
            {
                // a shape this reader doesn't expect (a number where it looks for an object): that one is left out
                import.Report.Add($"{(document["name"] as JsonValue)?.ToString() ?? "a document"}: couldn't be read ({error.Message}); left out");
            }
        }
        return import;
    }

    private void Add(JsonObject document)
    {
        string name = Text(document["name"]) is { Length: > 0 } n ? n : "Unnamed";
        string type = Text(document["type"]);
        JsonObject system = document["system"] as JsonObject ?? new JsonObject();
        bool pf2e = system["traits"] is JsonObject || system["damageRolls"] != null || system["rules"] is JsonArray;
        _written.Clear();
        AddKind(document, name, type, system, pf2e);
        // its Active Effects as modifiers, on the file written for it (an actor's is written last, after its gear)
        if (document["effects"] is JsonArray { Count: > 0 } effects && _written.LastOrDefault() is string path)
        {
            List<JsonObject> modifiers = EffectModifiers(name, effects);
            if (modifiers.Count > 0 && (path.StartsWith("items/", StringComparison.Ordinal) || path.StartsWith("feats/", StringComparison.Ordinal)))
            {
                var list = Files[path]["modifiers"] as JsonArray ?? new JsonArray();
                foreach (JsonObject modifier in modifiers)
                {
                    list.Add(modifier);
                }
                Files[path]["modifiers"] = list;
            }
            else if (modifiers.Count > 0)
            {
                Report.Add($"{name}: its effects' modifiers have no place on a {path[..path.IndexOf('/')]} file; left out");
            }
        }
    }

    private void AddKind(JsonObject document, string name, string type, JsonObject system, bool pf2e)
    {
        switch (type)
        {
            case "weapon" when !pf2e:
                Write("items", Dnd5eWeapon(name, system));
                break;
            case "melee":
                Write("items", Pf2eStrike(name, system));
                break;
            case "weapon":
                Write("items", Pf2eWeapon(name, system));
                break;
            case "equipment" when !pf2e && (system["armor"]?["value"] != null):
            case "armor":
            case "shield":
                if (Armour(name, system, pf2e) is JsonObject armour)
                {
                    Write("items", armour);
                }
                break;
            case "equipment" when !pf2e:
                Write("items", Dnd5eWorn(name, system));
                break;
            case "spell":
                Write("spells", pf2e ? Pf2eSpell(name, system) : Dnd5eSpell(name, system));
                break;
            case "feat" when _granted.Contains(Text(document["_id"])):
                // written into the class that grants it
                break;
            case "feat":
            case "action":
                Write("feats", Feat(name, system, pf2e));
                break;
            case "class" when pf2e || system["savingThrows"] is JsonObject:
                Write("classes", Pf2eClass(name, system));
                break;
            case "class":
                Write("classes", Dnd5eClass(name, system));
                break;
            case "npc":
                Write("creatures", pf2e || system["abilities"]?["str"]?["mod"] != null ? Pf2eCreature(name, document, system) : Dnd5eCreature(name, document, system));
                break;
            default:
                Report.Add($"{name}: a Foundry \"{type}\" has no place here yet; left out");
                break;
        }
    }

    // ------------------------------------------------------------------ dnd5e

    private static readonly Dictionary<string, string> Dnd5eProperties = new(StringComparer.Ordinal)
    {
        ["fin"] = "finesse", ["ver"] = "versatile", ["two"] = "two-handed", ["hvy"] = "heavy", ["lgt"] = "light",
        ["thr"] = "thrown", ["amm"] = "ammunition", ["rch"] = "reach", ["lod"] = "loading", ["mgc"] = "magic",
    };

    private JsonObject Dnd5eWeapon(string name, JsonObject system)
    {
        var item = Base(name, system);
        item["slot"] = "mainHand";
        JsonObject? damage = system["damage"]?["base"] as JsonObject;
        string dice = damage != null && Int(damage["number"]) > 0 ? $"{Int(damage["number"])}d{Int(damage["denomination"])}" : "1";
        if (damage?["bonus"] is JsonValue bonus && Text(bonus) is { Length: > 0 } extra)
        {
            dice += (extra.StartsWith('-') ? "" : "+") + extra.Replace(" ", "");
        }
        item["damage"] = dice;
        if (damage?["types"] is JsonArray types && types.Count > 0)
        {
            item["damageType"] = Text(types[0]);
        }
        var traits = new JsonArray();
        foreach (string property in (system["properties"] as JsonArray ?? new JsonArray()).Select(Text))
        {
            if (Dnd5eProperties.TryGetValue(property, out string? trait))
            {
                traits.Add(trait);
            }
        }
        if (traits.Count > 0)
        {
            item["traits"] = traits;
        }
        if (traits.Any(t => Text(t) == "two-handed"))
        {
            item["hands"] = 2;
        }
        if (traits.Any(t => Text(t) == "finesse"))
        {
            item["attackAbility"] = "dex";
        }
        // a ranged weapon's normal range, in squares of 5 feet
        if (Int(system["range"]?["value"]) is int feet and > 5)
        {
            item["range"] = feet / 5;
            item["attackAbility"] = "dex";
        }
        if (traits.Any(t => Text(t) == "versatile"))
        {
            Report.Add($"{name}: its two-handed damage is the system's versatile rule here, not the file's die");
        }
        return item;
    }

    // where a worn thing goes, by the word for it in its name
    private static readonly (string Slot, string[] Words)[] WornWords =
    {
        ("ring", new[] { "ring" }),
        ("neck", new[] { "amulet", "necklace", "periapt", "medallion", "pendant", "brooch" }),
        ("cloak", new[] { "cloak", "cape", "mantle", "robe" }),
        ("head", new[] { "helm", "hat", "circlet", "headband", "crown", "cap", "goggles", "eyes" }),
        ("hands", new[] { "gloves", "gauntlets", "bracers" }),
        ("feet", new[] { "boots", "slippers", "shoes" }),
    };

    /// <summary>A dnd5e ring, cloak or other worn thing (equipment that isn't armour): in the slot its name suggests, its effects added by the caller.</summary>
    private JsonObject Dnd5eWorn(string name, JsonObject system)
    {
        var item = Base(name, system);
        string kind = Text(system["type"]?["value"]);
        string[] words = Regex.Split(name.ToLowerInvariant(), "[^a-z]+");
        string slot = kind == "ring" ? "ring" : WornWords.FirstOrDefault(w => w.Words.Any(words.Contains)).Slot ?? "";
        if (slot.Length > 0)
        {
            item["slot"] = slot;
        }
        else
        {
            Report.Add($"{name}: no slot could be told from its name; it is carried, and its effects count once a slot is set");
        }
        return item;
    }

    private JsonObject Dnd5eSpell(string name, JsonObject system)
    {
        var spell = new JsonObject { ["id"] = Slug(name), ["name"] = name, ["description"] = Plain(system["description"]?["value"]) };
        spell["level"] = Int(system["level"]) ?? 0;
        spell["hands"] = 1;
        bool concentration = (system["properties"] as JsonArray)?.Any(p => Text(p) == "concentration") == true;
        if (concentration)
        {
            spell["concentration"] = true;
        }
        JsonObject? activity = (system["activities"] as JsonObject)?.Select(a => a.Value).OfType<JsonObject>().FirstOrDefault();
        string kind = Text(activity?["type"]);
        string cost = Text(activity?["activation"]?["type"]) is "bonus" ? "bonus" : "";
        if (cost.Length > 0)
        {
            spell["cost"] = cost;
        }
        int range = Int(system["range"]?["value"]) is int feet ? Math.Max(1, feet / 5) : 1;
        JsonObject? template = activity?["target"]?["template"] as JsonObject;
        string shape = Text(template?["type"]);
        if (shape.Length > 0 && Int(template?["size"]) is int size)
        {
            spell["target"] = new JsonObject { ["kind"] = "point", ["side"] = "any", ["range"] = Math.Max(1, range) };
            spell["area"] = new JsonObject
            {
                ["shape"] = shape switch { "cone" => "cone", "line" => "line", _ => "burst" },
                ["size"] = Math.Max(1, shape is "sphere" or "radius" or "cylinder" ? size / 5 : size / 5),
            };
        }
        else if (Text(system["range"]?["units"]) == "self")
        {
            spell["target"] = new JsonObject { ["kind"] = "self" };
        }
        else
        {
            spell["target"] = new JsonObject { ["kind"] = "creature", ["side"] = kind == "heal" ? "ally" : "enemy", ["range"] = range };
        }
        var effects = new JsonArray();
        string parts = DamageParts(activity?["damage"]?["parts"] as JsonArray ?? activity?["healing"] as JsonNode, out string type);
        switch (kind)
        {
            case "attack":
                effects.Add(new JsonObject
                {
                    ["do"] = "roll", ["kind"] = "attack", ["ability"] = "caster",
                    ["steps"] = new JsonArray(new JsonObject { ["do"] = "damage", ["dice"] = parts, ["type"] = type, ["when"] = "hit" }),
                });
                break;
            case "save":
                spell["save"] = new JsonObject { ["ability"] = Text((activity?["save"]?["ability"] as JsonArray)?.FirstOrDefault()) is { Length: > 0 } a ? a : Text(activity?["save"]?["ability"]), ["dc"] = "caster" };
                effects.Add(new JsonObject { ["do"] = "damage", ["dice"] = parts, ["type"] = type, ["onSave"] = Text(activity?["damage"]?["onSave"]) is "none" ? "none" : "half" });
                break;
            case "heal":
                effects.Add(new JsonObject { ["do"] = "heal", ["dice"] = parts });
                break;
            case "damage":
                effects.Add(new JsonObject { ["do"] = "damage", ["dice"] = parts, ["type"] = type });
                break;
            default:
                Report.Add($"{name}: a \"{(kind.Length > 0 ? kind : "no")}\" activity; the spell keeps its words and no effect, to finish by hand");
                effects.Add(new JsonObject { ["do"] = "flag", ["id"] = Slug(name) + "-cast" });
                break;
        }
        spell["effects"] = effects;
        return spell;
    }

    // Foundry's short names for the dnd5e skills, as the game's skill ids
    private static readonly Dictionary<string, string> Dnd5eSkills = new(StringComparer.Ordinal)
    {
        ["acr"] = "acrobatics", ["ani"] = "animal-handling", ["arc"] = "arcana", ["ath"] = "athletics", ["dec"] = "deception",
        ["his"] = "history", ["ins"] = "insight", ["itm"] = "intimidation", ["inv"] = "investigation", ["med"] = "medicine",
        ["nat"] = "nature", ["prc"] = "perception", ["prf"] = "performance", ["per"] = "persuasion", ["rel"] = "religion",
        ["slt"] = "sleight-of-hand", ["ste"] = "stealth", ["sur"] = "survival",
    };

    /// <summary>
    /// A dnd5e class item: its hit die, casting ability, the saves, armour, weapons and skills its
    /// Trait advancements give at first level, and a row per level with the features its
    /// ItemGrants give (named from the feats in the same export), its scale values and its ability
    /// score improvements. Spell slots and subclasses are named in the report.
    /// </summary>
    private JsonObject Dnd5eClass(string name, JsonObject system)
    {
        string id = Text(system["identifier"]) is { Length: > 0 } identifier ? Slug(identifier) : Slug(name);
        var entry = new JsonObject { ["id"] = id, ["name"] = name };
        if (Plain(system["description"]?["value"]) is { Length: > 0 } words)
        {
            entry["description"] = words;
        }
        string die = Text(system["hd"]?["denomination"]) is { Length: > 0 } d ? d : Text(system["hitDice"]);
        entry["hitDie"] = int.TryParse(die.TrimStart('d'), out int sides) && sides > 0 ? sides : 8;
        string casting = Text(system["spellcasting"]?["ability"]);
        if (casting.Length > 0)
        {
            entry["dcAbility"] = casting;
        }
        if (Text(system["spellcasting"]?["progression"]) is { Length: > 0 } progression && progression != "none")
        {
            Report.Add($"{name}: casts as a \"{progression}\" caster; its spell slots are added on its level rows by hand");
        }

        List<JsonObject> advancements = Advancements(system);
        var proficiencies = new JsonArray();
        var rows = new SortedDictionary<int, JsonObject>();
        JsonObject Row(int level)
        {
            if (!rows.TryGetValue(level, out JsonObject? row))
            {
                rows[level] = row = new JsonObject();
            }
            return row;
        }
        foreach (JsonObject advancement in advancements)
        {
            int level = Math.Clamp(Int(advancement["level"]) ?? 1, 1, 20);
            JsonObject configuration = advancement["configuration"] as JsonObject ?? new JsonObject();
            string title = Text(advancement["title"]);
            switch (Text(advancement["type"]))
            {
                case "HitPoints":
                    break;
                case "Trait":
                    // the multiclass copy of a trait ("secondary") is not the class's own
                    if (Text(advancement["classRestriction"]) == "secondary")
                    {
                        break;
                    }
                    foreach (string grant in (configuration["grants"] as JsonArray ?? new JsonArray()).Select(Text))
                    {
                        string? proficiency = Proficiency(grant);
                        if (proficiency != null && !proficiencies.Any(p => Text(p) == proficiency))
                        {
                            proficiencies.Add(proficiency);
                        }
                    }
                    foreach (JsonObject choice in (configuration["choices"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
                    {
                        var pool = (choice["pool"] as JsonArray ?? new JsonArray()).Select(Text).ToList();
                        if (pool.Count > 0 && pool.All(p => p.StartsWith("skills:", StringComparison.Ordinal)))
                        {
                            Row(level)["skills"] = (Int(Row(level)["skills"]) ?? 0) + (Int(choice["count"]) ?? 1);
                            Report.Add($"{name}: picks {Int(choice["count"]) ?? 1} skills at level {level} from any; the file's list ({string.Join(", ", pool.Select(Proficiency))}) has no place here yet");
                        }
                        else if (pool.Count > 0)
                        {
                            Report.Add($"{name}: a choice of {Int(choice["count"]) ?? 1} from {string.Join(", ", pool)} at level {level}; left out");
                        }
                    }
                    break;
                case "ItemGrant":
                    var features = Row(level)["features"] as JsonArray ?? new JsonArray();
                    Row(level)["features"] = features;
                    foreach (JsonNode? item in configuration["items"] as JsonArray ?? new JsonArray())
                    {
                        if (Feature(name, GrantedId(item), "", level) is JsonObject feature)
                        {
                            features.Add(feature);
                        }
                    }
                    if (features.Count == 0)
                    {
                        Row(level).Remove("features");
                    }
                    break;
                case "ScaleValue":
                    string scaleId = Text(configuration["identifier"]) is { Length: > 0 } sid ? Slug(sid) : Slug(title);
                    string kind = Text(configuration["type"]);
                    foreach ((string at, JsonNode? value) in configuration["scale"] as JsonObject ?? new JsonObject())
                    {
                        // dice count their dice (scale.sneak-attack is how many); a number or a distance is itself
                        int? number = kind == "dice" ? Int(value?["number"]) ?? 1 : Int(value?["value"]);
                        if (int.TryParse(at, out int scaleLevel) && scaleLevel is >= 1 and <= 20 && number is int n)
                        {
                            var scale = Row(scaleLevel)["scale"] as JsonObject ?? new JsonObject();
                            scale[scaleId] = n;
                            Row(scaleLevel)["scale"] = scale;
                        }
                    }
                    if (kind == "dice")
                    {
                        Report.Add($"{name}: scale.{scaleId} counts the dice of {title}; their size is in the effect that rolls them");
                    }
                    break;
                case "AbilityScoreImprovement":
                    Row(level)["boosts"] = Int(configuration["points"]) ?? 2;
                    Row(level)["boostStep"] = 1;
                    Row(level)["boostsRepeat"] = true;
                    break;
                default:
                    Report.Add($"{name}: a \"{Text(advancement["type"])}\" advancement at level {level}{(title.Length > 0 ? $" ({title})" : "")}; left out");
                    break;
            }
        }
        entry["proficiencies"] = proficiencies;
        if (rows.Count > 0)
        {
            var levels = new JsonArray();
            for (int level = 1; level <= rows.Keys.Max(); level++)
            {
                levels.Add(rows.TryGetValue(level, out JsonObject? row) ? row : new JsonObject());
            }
            entry["levels"] = levels;
        }
        return entry;
    }

    // A class feature from the feat of that id in the same export; with only a name (pf2e lists
    // one), that name and no words; with neither, a line in the report and nothing.
    private JsonObject? Feature(string owner, string key, string named, int level)
    {
        if (_documents.TryGetValue(key, out JsonObject? feat))
        {
            var feature = new JsonObject { ["id"] = Slug(Text(feat["name"])), ["name"] = Text(feat["name"]) };
            if (Plain(feat["system"]?["description"]?["value"]) is { Length: > 0 } about)
            {
                feature["description"] = about;
            }
            return feature;
        }
        if (named.Length > 0)
        {
            Report.Add($"{owner}: {named} (level {level}) isn't in this export; it comes with its name only");
            return new JsonObject { ["id"] = Slug(named), ["name"] = named };
        }
        Report.Add($"{owner}: a level {level} feature ({key}) isn't in this export; export it with the class to bring its name and words");
        return null;
    }

    // "saves:dex" is "dex", "armor:lgt" is "armor", "weapon:sim" is "weapons", "skills:ste" is "stealth"
    private static string? Proficiency(string grant)
    {
        string[] parts = grant.Split(':');
        return parts[0] switch
        {
            "saves" when parts.Length > 1 => parts[1],
            "armor" => "armor",
            "weapon" => "weapons",
            "skills" when parts.Length > 1 => Dnd5eSkills.GetValueOrDefault(parts[1], parts[1]),
            _ => null,
        };
    }

    // advancement is a list in current exports and an object keyed by id in some older ones
    private static List<JsonObject> Advancements(JsonObject? system) => system?["advancement"] switch
    {
        JsonArray list => list.OfType<JsonObject>().ToList(),
        JsonObject keyed => keyed.Select(p => p.Value).OfType<JsonObject>().ToList(),
        _ => new List<JsonObject>(),
    };

    // an ItemGrant's item, {"uuid": "Compendium.dnd5e.classfeatures.Item.abc123"} or the uuid alone, is "abc123"
    private static string GrantedId(JsonNode? item)
    {
        string uuid = item is JsonObject o ? Text(o["uuid"]) : Text(item);
        return uuid.Contains('.') ? uuid[(uuid.LastIndexOf('.') + 1)..] : uuid;
    }

    // "8d6" from a damage part's number and die (or its custom formula), with its first type.
    private static string DamageParts(JsonNode? parts, out string type)
    {
        type = "";
        JsonObject? part = parts is JsonArray list ? list.OfType<JsonObject>().FirstOrDefault() : parts as JsonObject;
        if (part == null)
        {
            return "1";
        }
        type = Text((part["types"] as JsonArray)?.FirstOrDefault());
        if (part["custom"]?["enabled"]?.GetValue<bool>() == true && Text(part["custom"]?["formula"]) is { Length: > 0 } formula)
        {
            return formula.Replace(" ", "");
        }
        string dice = Int(part["number"]) is int count and > 0 ? $"{count}d{Int(part["denomination"]) ?? 6}" : "1";
        string bonus = Text(part["bonus"]).Replace(" ", "");
        return bonus.Length == 0 ? dice : dice + (bonus.StartsWith('-') ? "" : "+") + bonus;
    }

    private JsonObject Dnd5eCreature(string name, JsonObject document, JsonObject system)
    {
        var creature = new JsonObject { ["id"] = Slug(name), ["name"] = name, ["description"] = Plain(system["details"]?["biography"]?["value"]) };
        JsonObject attributes = system["attributes"] as JsonObject ?? new JsonObject();
        creature["hp"] = Int(attributes["hp"]?["max"]) ?? Int(attributes["hp"]?["value"]) ?? 1;
        creature["armorClass"] = Int(attributes["ac"]?["flat"]) ?? Int(attributes["ac"]?["value"]) ?? 10;
        creature["speed"] = Int(attributes["movement"]?["walk"]) ?? 30;
        creature["level"] = Math.Max(1, (int)Math.Ceiling(Number(system["details"]?["cr"]) ?? 1));
        var abilities = new JsonObject();
        foreach ((string id, JsonNode? ability) in system["abilities"] as JsonObject ?? new JsonObject())
        {
            abilities[id] = Int(ability?["value"]) ?? 10;
        }
        creature["abilities"] = abilities;
        creature["proficiencies"] = new JsonArray("weapons");
        if (Int(attributes["senses"]?["ranges"]?["darkvision"] ?? attributes["senses"]?["darkvision"]) is int darkvision and > 0)
        {
            creature["darkvision"] = darkvision;
        }
        creature["items"] = Embedded(name, document, pf2e: false);
        creature["ai"] = "cunning";
        return creature;
    }

    // ------------------------------------------------------------------ pf2e

    private JsonObject Pf2eStrike(string name, JsonObject system)
    {
        var item = new JsonObject { ["id"] = Slug(name), ["name"] = name, ["value"] = 0, ["slot"] = "mainHand", ["weight"] = 0 };
        JsonObject? roll = (system["damageRolls"] as JsonObject)?.Select(r => r.Value).OfType<JsonObject>().FirstOrDefault();
        item["damage"] = Text(roll?["damage"]) is { Length: > 0 } dice ? dice.Replace(" ", "") : "1";
        if (Text(roll?["damageType"]) is { Length: > 0 } type)
        {
            item["damageType"] = type;
        }
        Traits(item, system);
        if (Int(system["range"]?["increment"]) is int feet and > 5)
        {
            item["range"] = feet / 5;
        }
        return item;
    }

    private JsonObject Pf2eWeapon(string name, JsonObject system)
    {
        var item = Base(name, system);
        item["slot"] = "mainHand";
        JsonObject? damage = system["damage"] as JsonObject;
        item["damage"] = Int(damage?["dice"]) is int count and > 0 ? $"{count}{Text(damage?["die"])}" : "1d4";
        if (Text(damage?["damageType"]) is { Length: > 0 } type)
        {
            item["damageType"] = type;
        }
        Traits(item, system);
        if (Int(system["range"]) is int feet and > 5)
        {
            item["range"] = feet / 5;
        }
        return item;
    }

    // Foundry's pf2e proficiency numbers
    private static readonly string[] Pf2eRanks = { "untrained", "trained", "expert", "master", "legendary" };

    /// <summary>
    /// A pf2e class item: HP per level, key ability, the first-level ranks it lists (perception,
    /// saves, the best of its weapon and armour ranks, its trained skills), a row per level with
    /// the feats it picks there and its skill increases, and its features by level (named from
    /// the feats in the same export, else by the name it lists). Rank rises that features give
    /// later are rule elements, named in the report.
    /// </summary>
    private JsonObject Pf2eClass(string name, JsonObject system)
    {
        string id = Text(system["slug"]) is { Length: > 0 } slug ? Slug(slug) : Slug(name);
        var entry = new JsonObject { ["id"] = id, ["name"] = name };
        if (Plain(system["description"]?["value"]) is { Length: > 0 } words)
        {
            entry["description"] = words;
        }
        entry["hitDie"] = Int(system["hp"]) ?? 8;
        var keys = (system["keyAbility"]?["value"] as JsonArray ?? new JsonArray()).Select(Text).Where(k => k.Length > 0).ToList();
        if (keys.Count > 0)
        {
            entry["dcAbility"] = keys[0];
            if (keys.Count > 1)
            {
                Report.Add($"{name}: its key ability is one of {string.Join(", ", keys)}; {keys[0]} is taken");
            }
        }
        string Rank(JsonNode? node) => Pf2eRanks[Math.Clamp(Int(node) ?? 0, 0, Pf2eRanks.Length - 1)];
        int Best(JsonObject? group, params string[] names) => names.Max(n => Int(group?[n]) ?? 0);
        var ranks = new JsonObject();
        var proficiencies = new JsonArray();
        void Set(string target, int number)
        {
            if (number > 0)
            {
                ranks[target] = Pf2eRanks[Math.Clamp(number, 0, Pf2eRanks.Length - 1)];
            }
        }
        Set("perception", Int(system["perception"]) ?? 0);
        foreach ((string save, JsonNode? rank) in system["savingThrows"] as JsonObject ?? new JsonObject())
        {
            Set(save, Int(rank) ?? 0);
        }
        int weapons = Best(system["attacks"] as JsonObject, "simple", "martial", "advanced");
        Set("weapons", weapons);
        int armour = Best(system["defenses"] as JsonObject, "light", "medium", "heavy");
        Set("armor", armour);
        if (Int(system["attacks"]?["simple"]) != Int(system["attacks"]?["martial"]))
        {
            Report.Add($"{name}: simple and martial weapons have different ranks; both are {Rank(weapons)} here");
        }
        ranks["dc"] = Pf2eRanks[Math.Clamp(Int(system["classDC"]) ?? 1, 1, Pf2eRanks.Length - 1)];
        if (weapons > 0)
        {
            proficiencies.Add("weapons");
        }
        if (armour > 0)
        {
            proficiencies.Add("armor");
        }
        foreach (string skill in (system["trainedSkills"]?["value"] as JsonArray ?? new JsonArray()).Select(Text).Where(s => s.Length > 0))
        {
            ranks[Slug(skill)] = "trained";
            proficiencies.Add(Slug(skill));
        }
        entry["proficiencyRanks"] = ranks;
        entry["proficiencies"] = proficiencies;

        var rows = new SortedDictionary<int, JsonObject>();
        JsonObject Row(int level)
        {
            if (!rows.TryGetValue(level, out JsonObject? row))
            {
                rows[level] = row = new JsonObject();
            }
            return row;
        }
        if (Int(system["trainedSkills"]?["additional"]) is int additional and > 0)
        {
            Row(1)["skills"] = additional;
        }
        foreach ((string key, string kind) in new[] { ("ancestryFeatLevels", "ancestry"), ("classFeatLevels", "class"), ("skillFeatLevels", "skill"), ("generalFeatLevels", "general") })
        {
            foreach (int level in (system[key]?["value"] as JsonArray ?? new JsonArray()).Select(Int).OfType<int>().Where(l => l is >= 1 and <= 20))
            {
                var feats = Row(level)["feats"] as JsonArray ?? new JsonArray();
                feats.Add(kind);
                Row(level)["feats"] = feats;
            }
        }
        foreach (int level in (system["skillIncreaseLevels"]?["value"] as JsonArray ?? new JsonArray()).Select(Int).OfType<int>().Where(l => l is >= 1 and <= 20))
        {
            Row(level)["skills"] = (Int(Row(level)["skills"]) ?? 0) + 1;
        }
        foreach (JsonObject item in (system["items"] as JsonObject ?? new JsonObject()).Select(p => p.Value).OfType<JsonObject>().OrderBy(i => Int(i["level"]) ?? 1))
        {
            int level = Math.Clamp(Int(item["level"]) ?? 1, 1, 20);
            if (Feature(name, GrantedId(item), Text(item["name"]), level) is JsonObject feature)
            {
                var features = Row(level)["features"] as JsonArray ?? new JsonArray();
                features.Add(feature);
                Row(level)["features"] = features;
            }
        }
        Report.Add($"{name}: ability boosts at 5, 10, 15 and 20 and rank rises from features are the system's or the features' own; add them to the level rows if this system's class files carry them");
        if (rows.Count > 0)
        {
            var levels = new JsonArray();
            for (int level = 1; level <= rows.Keys.Max(); level++)
            {
                levels.Add(rows.TryGetValue(level, out JsonObject? row) ? row : new JsonObject());
            }
            entry["levels"] = levels;
        }
        return entry;
    }

    private static void Traits(JsonObject item, JsonObject system)
    {
        var traits = new JsonArray();
        foreach (string trait in (system["traits"]?["value"] as JsonArray ?? new JsonArray()).Select(Text))
        {
            // "deadly-d10" is the deadly trait; its die is the system's own rider here
            string plain = Regex.Replace(trait, @"-(d\d+|\d+)$", "");
            if (plain.Length > 0 && !traits.Any(t => Text(t) == plain))
            {
                traits.Add(plain);
            }
        }
        if (traits.Count > 0)
        {
            item["traits"] = traits;
        }
    }

    private JsonObject Pf2eSpell(string name, JsonObject system)
    {
        var spell = new JsonObject { ["id"] = Slug(name), ["name"] = name, ["description"] = Plain(system["description"]?["value"]) };
        spell["level"] = (system["traits"]?["value"] as JsonArray)?.Any(t => Text(t) == "cantrip") == true ? 0 : Int(system["level"]?["value"]) ?? 1;
        spell["hands"] = 0;
        spell["cost"] = Int(system["time"]?["value"]) ?? 2;
        int range = Regex.Match(Text(system["range"]?["value"]), @"\d+") is { Success: true } feet ? Math.Max(1, int.Parse(feet.Value, CultureInfo.InvariantCulture) / 5) : 1;
        if (system["area"] is JsonObject area && Int(area["value"]) is int size)
        {
            spell["target"] = new JsonObject { ["kind"] = "point", ["side"] = "any", ["range"] = range };
            spell["area"] = new JsonObject { ["shape"] = Text(area["type"]) switch { "cone" => "cone", "line" => "line", _ => "burst" }, ["size"] = Math.Max(1, size / 5) };
        }
        else
        {
            spell["target"] = new JsonObject { ["kind"] = "creature", ["side"] = "enemy", ["range"] = range };
        }
        JsonObject? damage = (system["damage"] as JsonObject)?.Select(d => d.Value).OfType<JsonObject>().FirstOrDefault();
        string dice = Text(damage?["formula"]) is { Length: > 0 } formula ? formula.Replace(" ", "") : "1";
        var effects = new JsonArray();
        string save = Text(system["defense"]?["save"]?["statistic"]);
        if (save.Length > 0)
        {
            spell["save"] = new JsonObject { ["ability"] = save, ["dc"] = "caster" };
            effects.Add(new JsonObject { ["do"] = "damage", ["dice"] = dice, ["type"] = Text(damage?["type"]), ["onSave"] = "half" });
        }
        else if (Text(system["defense"]?["passive"]?["statistic"]) == "ac")
        {
            effects.Add(new JsonObject
            {
                ["do"] = "roll", ["kind"] = "attack", ["ability"] = "caster",
                ["steps"] = new JsonArray(new JsonObject { ["do"] = "damage", ["dice"] = dice, ["type"] = Text(damage?["type"]), ["when"] = "hit" }),
            });
        }
        else if (damage != null)
        {
            effects.Add(new JsonObject { ["do"] = "damage", ["dice"] = dice, ["type"] = Text(damage["type"]) });
        }
        else
        {
            Report.Add($"{name}: no damage, save or attack the importer reads; the spell keeps its words and no effect, to finish by hand");
            effects.Add(new JsonObject { ["do"] = "flag", ["id"] = Slug(name) + "-cast" });
        }
        spell["effects"] = effects;
        return spell;
    }

    private JsonObject Pf2eCreature(string name, JsonObject document, JsonObject system)
    {
        var creature = new JsonObject { ["id"] = Slug(name), ["name"] = name, ["description"] = Plain(system["details"]?["publicNotes"]) };
        JsonObject attributes = system["attributes"] as JsonObject ?? new JsonObject();
        int level = Int(system["details"]?["level"]?["value"]) ?? 0;
        creature["level"] = level;
        creature["hp"] = Int(attributes["hp"]?["max"]) ?? 1;
        creature["armorClass"] = Int(attributes["ac"]?["value"]) ?? 10;
        creature["speed"] = Int(attributes["speed"]?["value"]) ?? 25;
        // a pf2e creature has modifiers, not scores: the score that gives each
        var abilities = new JsonObject();
        foreach ((string id, JsonNode? ability) in system["abilities"] as JsonObject ?? new JsonObject())
        {
            abilities[id] = 10 + 2 * (Int(ability?["mod"]) ?? 0);
        }
        creature["abilities"] = abilities;
        creature["proficiencyRanks"] = new JsonObject
        {
            ["weapons"] = "trained", ["dc"] = "trained", ["fortitude"] = "trained", ["reflex"] = "trained", ["will"] = "trained", ["perception"] = "trained",
        };
        if ((system["perception"]?["senses"] as JsonArray)?.Any(s => Text(s?["type"]) == "darkvision") == true)
        {
            creature["darkvision"] = 60;
        }
        // the creature's own attack bonus, less what a trained creature of its level and Strength or
        // Dexterity already has, rides on as a flat "attack" stat so the strike rolls as the book prints
        JsonObject? strike = (document["items"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(i => Text(i["type"]) == "melee");
        if (Int(strike?["system"]?["bonus"]?["value"]) is int printed)
        {
            bool finesse = (strike!["system"]?["traits"]?["value"] as JsonArray)?.Any(t => Text(t) is "finesse" or "agile") == true
                || strike["system"]?["range"] is JsonObject;
            int mod = (Int(system["abilities"]?[finesse ? "dex" : "str"]?["mod"]) ?? 0);
            creature["stats"] = new JsonObject { ["attack"] = printed - (mod + 2 + level) };
        }
        creature["items"] = Embedded(name, document, pf2e: true);
        creature["ai"] = "cunning";
        Report.Add($"{name}: saves, perception and skills follow trained ranks here, not the printed numbers");
        return creature;
    }

    // ------------------------------------------------------------------ shared

    private JsonObject? Armour(string name, JsonObject system, bool pf2e)
    {
        var item = Base(name, system);
        string kind = Text(system["type"]?["value"]);
        bool shield = kind == "shield" || Text(system["category"]) == "shield";
        int value = pf2e ? Int(system["acBonus"]) ?? 0 : Int(system["armor"]?["value"]) ?? 0;
        item["slot"] = shield ? "offHand" : "armor";
        var modifiers = new JsonArray();
        if (shield || pf2e)
        {
            modifiers.Add(new JsonObject { ["stat"] = "ac", ["op"] = "add", ["value"] = value });
        }
        else
        {
            modifiers.Add(new JsonObject { ["stat"] = "ac", ["op"] = "override", ["value"] = value });
            // armorDexCap is the cap plus one, as the 5e rules read it (0 = no cap)
            int? cap = kind switch { "medium" => 2, "heavy" => 0, _ => null };
            if (Int(system["armor"]?["dex"]) is int written)
            {
                cap = written;
            }
            if (cap is int dex)
            {
                modifiers.Add(new JsonObject { ["stat"] = "armorDexCap", ["op"] = "override", ["value"] = dex + 1 });
            }
        }
        item["modifiers"] = modifiers;
        return item;
    }

    private JsonObject Feat(string name, JsonObject system, bool pf2e)
    {
        var feat = new JsonObject { ["id"] = Slug(name), ["name"] = name, ["description"] = Plain(system["description"]?["value"]), ["kind"] = "general" };
        if (pf2e && Text(system["category"]) is { Length: > 0 } category && category is "ancestry" or "class" or "skill" or "general")
        {
            feat["kind"] = category;
        }
        var modifiers = new JsonArray();
        foreach (JsonObject rule in (system["rules"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            string key = Text(rule["key"]);
            if (key == "FlatModifier" && Int(rule["value"]) is int value && Selector(Text(rule["selector"]) is { Length: > 0 } s ? s : Text((rule["selector"] as JsonArray)?.FirstOrDefault())) is var (stat, when))
            {
                var modifier = new JsonObject { ["stat"] = stat, ["value"] = value };
                if (Text(rule["type"]) is { Length: > 0 } type && type != "untyped")
                {
                    modifier["type"] = type;
                }
                if (when != null)
                {
                    modifier["if"] = when;
                }
                if (rule["predicate"] != null)
                {
                    Report.Add($"{name}: a FlatModifier's predicate isn't read; its bonus always applies here");
                }
                modifiers.Add(modifier);
            }
            else
            {
                Report.Add($"{name}: rule element {key} has no place here yet; left out");
            }
        }
        if (modifiers.Count > 0)
        {
            feat["modifiers"] = modifiers;
        }
        if (!pf2e && (system["activities"] as JsonObject)?.Count > 0)
        {
            Report.Add($"{name}: its activities aren't brought over; give it an action by hand");
        }
        return feat;
    }

    // A pf2e selector as the stat it changes here, with the "if" that narrows it.
    /// <summary>
    /// Active Effects that change a number by a plain amount (a ring's +1 to AC and saves, boots'
    /// speed) as modifiers. Foundry's modes: 1 multiply, 2 add, 3 at most, 4 at least, 5 override;
    /// custom changes, formulas and paths with no stat here are named in the report.
    /// </summary>
    private List<JsonObject> EffectModifiers(string owner, JsonArray effects)
    {
        var modifiers = new List<JsonObject>();
        foreach (JsonObject effect in effects.OfType<JsonObject>())
        {
            if (effect["disabled"]?.GetValueKind() == System.Text.Json.JsonValueKind.True)
            {
                continue;
            }
            // an effect not passed to the wearer (Foundry's transfer false) works only when used
            if (effect["transfer"]?.GetValueKind() == System.Text.Json.JsonValueKind.False)
            {
                Report.Add($"{owner}: effect {Text(effect["name"] ?? effect["label"])} applies when used, not while carried; left out");
                continue;
            }
            foreach (JsonObject change in (effect["changes"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
            {
                string key = Text(change["key"]);
                string op = (Int(change["mode"]) ?? 2) switch { 1 => "multiply", 2 => "add", 3 => "min", 4 => "max", 5 => "override", _ => "" };
                (string Stat, string? If)? target = EffectStat(key);
                if (op.Length == 0 || target == null || Number(change["value"]) is not double value)
                {
                    Report.Add($"{owner}: effect change {key} = {Text(change["value"])} has no place here yet; left out");
                    continue;
                }
                var modifier = new JsonObject { ["stat"] = target.Value.Stat, ["value"] = value % 1 == 0 ? (int)value : value };
                if (op != "add")
                {
                    modifier["op"] = op;
                }
                if (target.Value.If != null)
                {
                    modifier["if"] = target.Value.If;
                }
                if (key.Split('.') is [_, "bonuses", "mwak" or "rwak" or "msak" or "rsak", _])
                {
                    Report.Add($"{owner}: {key} counts on every attack here, not only that kind");
                }
                modifiers.Add(modifier);
            }
        }
        return modifiers;
    }

    // dnd5e data paths to the stats and roll filters modifiers use here
    private static (string Stat, string? If)? EffectStat(string key)
    {
        string[] part = key.Split('.');
        return key switch
        {
            "system.attributes.ac.bonus" => ("ac", null),
            "system.attributes.init.bonus" => ("initiative", null),
            "system.attributes.movement.walk" => ("speed", null),
            "system.attributes.hp.bonuses.overall" => ("maxHp", null),
            "system.attributes.senses.darkvision" or "system.attributes.senses.ranges.darkvision" => ("darkvision", null),
            "system.bonuses.abilities.save" => ("saves", null),
            "system.bonuses.abilities.check" => ("checks", null),
            "system.bonuses.spell.dc" => ("dc", null),
            _ when part.Length == 4 && part[1] == "bonuses" && part[2] is "mwak" or "rwak" or "msak" or "rsak" && part[3] is "attack" or "damage" => (part[3], null),
            _ when part.Length == 5 && part[1] == "abilities" && part[3] == "bonuses" && part[4] == "save" => ("saves", "save." + part[2]),
            _ when part.Length == 5 && part[1] == "skills" && part[3] == "bonuses" && part[4] == "check" => ("checks", "check." + Dnd5eSkills.GetValueOrDefault(part[2], part[2])),
            _ => null,
        };
    }

    private static (string Stat, string? If)? Selector(string selector) => selector switch
    {
        "attack" or "attack-roll" or "strike-attack-roll" => ("attack", null),
        "damage" or "strike-damage" => ("damage", null),
        "ac" => ("ac", null),
        "saving-throw" => ("saves", null),
        "fortitude" or "reflex" or "will" => ("saves", "save." + selector),
        "perception" => ("checks", "check.perception"),
        "initiative" => ("initiative", null),
        "speed" or "land-speed" => ("speed", null),
        "hp" => ("maxHp", null),
        _ when selector.EndsWith("-check", StringComparison.Ordinal) => ("checks", "check." + selector[..^6]),
        _ => null,
    };

    // The weapons and armour an actor carries, each written as an item and listed by id.
    private JsonArray Embedded(string owner, JsonObject document, bool pf2e)
    {
        var ids = new JsonArray();
        foreach (JsonObject child in (document["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>())
        {
            string name = Text(child["name"]);
            string type = Text(child["type"]);
            JsonObject system = child["system"] as JsonObject ?? new JsonObject();
            JsonObject? item = (pf2e, type) switch
            {
                (true, "melee") => Pf2eStrike(name, system),
                (false, "weapon") => Dnd5eWeapon(name, system),
                (false, "equipment") when system["armor"]?["value"] != null => Armour(name, system, false),
                (true, "armor") or (true, "shield") => Armour(name, system, true),
                _ => null,
            };
            if (item == null)
            {
                if (type is not "weapon" and not "ammo" and not "lore" and not "spellcastingEntry")
                {
                    Report.Add($"{owner}: {name} ({type}) isn't brought over");
                }
                continue;
            }
            // the creature's own strike under its own id, so two creatures' bites stay apart
            string id = pf2e ? Slug(owner + " " + name) : Slug(name);
            item["id"] = id;
            Write("items", item);
            ids.Add(id);
        }
        return ids;
    }

    private static JsonObject Base(string name, JsonObject system)
    {
        var item = new JsonObject { ["id"] = Slug(name), ["name"] = name };
        if (Plain(system["description"]?["value"]) is { Length: > 0 } words)
        {
            item["description"] = words;
        }
        // prices in copper; dnd5e writes a value and a coin, pf2e a gp/sp/cp object
        JsonNode? price = (system["price"] as JsonObject)?["value"];
        double gold = price is JsonObject coins ? (Number(coins["gp"]) ?? 0) + (Number(coins["sp"]) ?? 0) / 10 + (Number(coins["cp"]) ?? 0) / 100
            : Text(system["price"]?["denomination"]) is "gp" or "" ? Number(price) ?? 0 : 0;
        item["value"] = (int)Math.Round(gold * 100);
        item["weight"] = Number((system["weight"] as JsonObject)?["value"]) ?? Number((system["bulk"] as JsonObject)?["value"]) ?? 0;
        return item;
    }

    private void Write(string folder, JsonObject entry)
    {
        string path = $"{folder}/{Text(entry["id"])}.json";
        if (!Files.ContainsKey(path))
        {
            Files[path] = entry;
            _written.Add(path);
        }
    }

    /// <summary>A name as an id: "Goblin Warrior" is "goblin-warrior".</summary>
    public static string Slug(string name)
    {
        string slug = Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "imported" : slug.Length > 64 ? slug[..64].TrimEnd('-') : slug;
    }

    // HTML descriptions as plain words; Foundry's @UUID[...]{Label} links keep their label.
    private static string Plain(JsonNode? html)
    {
        string text = Text(html);
        text = Regex.Replace(text, @"@\w+\[[^\]]*\]\{([^}]*)\}", "$1");
        text = Regex.Replace(text, @"@\w+\[[^\]]*\]", "");
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > 4000 ? text[..4000] : text;
    }

    private static string Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : "";

    private static int? Int(JsonNode? node) => Number(node) is double d ? (int)Math.Round(d) : null;

    private static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }
        if (value.TryGetValue(out double number))
        {
            return number;
        }
        // Foundry keeps some numbers as strings ("30", "1/4")
        if (value.TryGetValue(out string? text))
        {
            if (text.Contains('/') && text.Split('/') is [string top, string bottom]
                && double.TryParse(top, NumberStyles.Float, CultureInfo.InvariantCulture, out double a) && double.TryParse(bottom, NumberStyles.Float, CultureInfo.InvariantCulture, out double b) && b != 0)
            {
                return a / b;
            }
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
            {
                return parsed;
            }
        }
        return null;
    }
}
