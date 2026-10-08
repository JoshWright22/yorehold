using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Yorehold.Rules;

/// <summary>
/// A monster's stat block as a book prints it (the 2014 and 2024 layouts), read into the outline's
/// creature fields: name, size and type, armour class, hit points, speed, darkvision, the six
/// abilities, resistances, immunities and weaknesses by damage type, challenge as a level, and
/// each weapon attack as an item it fights with. It tolerates the usual OCR slips (l or I for 1,
/// O for 0, a long dash for minus). What has no field yet (saves, skills, traits, reactions,
/// legendary actions) stays in the creature's "text", so nothing the book says is lost.
/// </summary>
public static class StatBlockText
{
    public sealed record Attack(string Name, int Bonus, string Dice, string Type, bool Ranged);

    public sealed record Creature(string Name, JsonObject Data, List<Attack> Attacks, List<string> Read);

    private static readonly string[] Sizes = { "Tiny", "Small", "Medium", "Large", "Huge", "Gargantuan" };
    private static readonly string[] DamageTypes =
    {
        "acid", "bludgeoning", "cold", "fire", "force", "lightning", "necrotic", "piercing", "poison", "psychic", "radiant", "slashing", "thunder",
    };
    private static readonly string[] Abilities = { "str", "dex", "con", "int", "wis", "cha" };

    private static readonly Regex SizeLine = new(@"^\s*(Tiny|Small|Medium|Large|Huge|Gargantuan)\b[^\n]*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

    /// <summary>
    /// Every stat block in a page's text: each runs from the name line above a size-and-type line
    /// ("Medium Humanoid, Neutral Evil") to the next block's name.
    /// </summary>
    public static List<string> Find(string text)
    {
        string[] lines = text.Replace("\r", "").Split('\n');
        var starts = new List<int>();
        for (int i = 1; i < lines.Length; i++)
        {
            if (SizeLine.IsMatch(lines[i]) && lines[i - 1].Trim().Length is > 1 and < 60 && IsBlockAhead(lines, i))
            {
                starts.Add(i - 1);
            }
        }
        var blocks = new List<string>();
        for (int s = 0; s < starts.Count; s++)
        {
            int end = s + 1 < starts.Count ? starts[s + 1] : lines.Length;
            blocks.Add(string.Join("\n", lines[starts[s]..end]).Trim());
        }
        return blocks;
    }

    // a size line starts a stat block only when armour class and hit points follow soon after
    private static bool IsBlockAhead(string[] lines, int at)
    {
        string ahead = string.Join("\n", lines.Skip(at).Take(8));
        return Regex.IsMatch(ahead, @"\b(Armor Class|AC)\s+[\dlIO]", RegexOptions.IgnoreCase)
            && Regex.IsMatch(ahead, @"\b(Hit Points|HP)\s+[\dlIO]", RegexOptions.IgnoreCase);
    }

    /// <summary>One stat block's text (name first) read; null when it has no armour class or hit points.</summary>
    public static Creature? Read(string block)
    {
        string text = block.Replace("\r", "").Replace('−', '-').Replace('–', '-').Replace('—', '-');
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 3)
        {
            return null;
        }
        var read = new List<string>();
        string name = lines[0];
        string flat = Regex.Replace(text, @"\s+", " ");

        int? ac = Number(flat, @"\b(?:Armor Class|AC)\s+([\dlIO]+)");
        int? hp = Number(flat, @"\b(?:Hit Points|HP)\s+([\dlIO]+)");
        if (ac == null || hp == null)
        {
            return null;
        }
        var data = new JsonObject { ["name"] = name, ["armorClass"] = Math.Clamp(ac.Value, 0, 100), ["hp"] = Math.Max(1, hp.Value) };
        read.Add("armour class");
        read.Add("hit points");

        if (lines.Skip(1).FirstOrDefault(l => SizeLine.IsMatch(l)) is string kind)
        {
            data["description"] = kind;
            string size = Sizes.First(s => kind.StartsWith(s, StringComparison.OrdinalIgnoreCase));
            data["token"] = new JsonObject
            {
                ["color"] = new JsonArray(130, 110, 90),
                ["size"] = size switch { "Tiny" => 0.25, "Small" => 0.36, "Medium" => 0.4, "Large" => 0.8, "Huge" => 1.3, _ => 1.8 },
            };
            read.Add("size and type");
        }
        if (Number(flat, @"\bSpeed\s+([\dlIO]+)\s*ft") is int speed)
        {
            data["speed"] = speed;
            read.Add("speed");
        }
        if (Number(flat, @"\bdarkvision\s+([\dlIO]+)\s*ft") is int dark)
        {
            data["darkvision"] = dark;
            read.Add("darkvision");
        }
        if (ReadAbilities(flat) is JsonObject abilities)
        {
            data["abilities"] = abilities;
            read.Add("abilities");
        }
        if (Regex.Match(flat, @"\b(?:Challenge|CR)\s+([\dlIO]+(?:/[\dlIO]+)?)", RegexOptions.IgnoreCase) is { Success: true } cr)
        {
            string value = Digits(cr.Groups[1].Value);
            int level = value.Contains('/') ? 1 : int.TryParse(value, out int whole) ? Math.Max(1, whole) : 1;
            data["level"] = level;
            read.Add("challenge");
        }

        // resistances, immunities and weaknesses by damage type, as the system's stats
        var stats = new JsonObject();
        foreach ((string label, string stat) in new[]
        {
            ("Damage Resistances", "resist"), ("Damage Immunities", "immune"), ("Damage Vulnerabilities", "weak"),
            ("Resistances", "resist"), ("Immunities", "immune"), ("Vulnerabilities", "weak"),
        })
        {
            foreach (Match line in Regex.Matches(text, $@"^{label}\s+([^\n]+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                // "Poison; Poisoned": damage types before a semicolon, conditions after
                string damage = line.Groups[1].Value.Split(';')[0];
                foreach (string type in DamageTypes.Where(t => Regex.IsMatch(damage, $@"\b{t}\b", RegexOptions.IgnoreCase)))
                {
                    stats[$"{stat}.{type}"] = 1;
                }
            }
        }
        if (stats.Count > 0)
        {
            data["stats"] = stats;
            read.Add("resistances and immunities");
        }

        List<Attack> attacks = ReadAttacks(flat);
        if (attacks.Count > 0)
        {
            read.Add("attacks");
        }
        if (Regex.IsMatch(flat, @"\bMultiattack\.\s+[^.]*\b(two|2)\b", RegexOptions.IgnoreCase))
        {
            data["multiattack"] = 2;
            read.Add("multiattack");
        }
        data["proficiencies"] = new JsonArray("weapons");
        data["ai"] = "cunning";
        data["text"] = text.Trim();
        return new Creature(name, data, attacks, read);
    }

    // "STR DEX CON INT WIS CHA / 11 (+0) 12 (+1) ..." (2014), or "Str 11 +0 +0 Dex 12 +1 +1 ..." (2024)
    private static JsonObject? ReadAbilities(string flat)
    {
        Match old = Regex.Match(flat, @"STR\s+DEX\s+CON\s+INT\s+WIS\s+CHA\s+" + string.Concat(Enumerable.Repeat(@"([\dlIO]+)\s*\(\s*[+-]?[\dlIO]+\s*\)\s*", 6)));
        if (old.Success)
        {
            var scores = new JsonObject();
            for (int i = 0; i < 6; i++)
            {
                scores[Abilities[i]] = int.Parse(Digits(old.Groups[i + 1].Value), CultureInfo.InvariantCulture);
            }
            return scores;
        }
        var found = new JsonObject();
        foreach (string ability in Abilities)
        {
            if (Regex.Match(flat, $@"\b{ability}\s+([\dlIO]+)\s+[+-]", RegexOptions.IgnoreCase) is { Success: true } m)
            {
                found[ability] = int.Parse(Digits(m.Groups[1].Value), CultureInfo.InvariantCulture);
            }
        }
        return found.Count == 6 ? found : null;
    }

    // "Scimitar. Melee Weapon Attack: +4 to hit, reach 5 ft., one target. Hit: 5 (1d6 + 2) slashing damage."
    // "Scimitar. Melee Attack Roll: +4, reach 5 ft. Hit: 5 (1d6 + 2) Slashing damage."
    private static List<Attack> ReadAttacks(string flat)
    {
        var attacks = new List<Attack>();
        var pattern = new Regex(
            @"([A-Z][A-Za-z' ]{1,40}?)\.\s+(Melee|Ranged)(?:\s+or\s+Ranged)?\s+(?:Weapon\s+|Spell\s+)?Attack(?:\s+Roll)?:\s*\+?([\dlIO]+).{0,160}?Hit:\s*[\dlIO]+\s*\(([^)]+)\)\s+([A-Za-z]+)\s+damage",
            RegexOptions.IgnoreCase);
        foreach (Match m in pattern.Matches(flat))
        {
            // the dice alone: the creature's own ability adds the rest, as it does for anyone
            Match dice = Regex.Match(Digits(m.Groups[4].Value), @"(\d+)\s*d\s*(\d+)");
            string type = m.Groups[5].Value.ToLowerInvariant();
            if (!dice.Success)
            {
                continue;
            }
            // the section heading the lines were joined after is not part of the name
            string called = Regex.Replace(m.Groups[1].Value.Trim(), @"^(?:(?:Bonus|Legendary)\s+)?(?:Actions|Reactions)\s+", "", RegexOptions.IgnoreCase);
            attacks.Add(new Attack(called, int.Parse(Digits(m.Groups[3].Value), CultureInfo.InvariantCulture),
                $"{dice.Groups[1].Value}d{dice.Groups[2].Value}", DamageTypes.Contains(type) ? type : "",
                m.Groups[2].Value.Equals("Ranged", StringComparison.OrdinalIgnoreCase)));
        }
        return attacks;
    }

    private static int? Number(string text, string pattern)
    {
        Match m = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(Digits(m.Groups[1].Value), NumberStyles.None, CultureInfo.InvariantCulture, out int value) ? value : null;
    }

    // what OCR makes of digits: l and I for 1, O for 0
    private static string Digits(string text) => text.Replace('l', '1').Replace('I', '1').Replace('O', '0').Replace('o', '0');
}
