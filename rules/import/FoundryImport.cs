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
        IEnumerable<JsonNode?> documents = root is JsonArray list ? list : new[] { root };
        foreach (JsonObject document in documents.OfType<JsonObject>())
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
            case "spell":
                Write("spells", pf2e ? Pf2eSpell(name, system) : Dnd5eSpell(name, system));
                break;
            case "feat":
            case "action":
                Write("feats", Feat(name, system, pf2e));
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
