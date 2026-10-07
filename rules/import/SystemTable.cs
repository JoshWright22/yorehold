using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// import/systems/&lt;name&gt;.json: how one source system's numbers become the game's. An outline
/// keeps the book's numbers as written and names its system; the builder runs them through this
/// table: skill names to the game's skills or abilities, check difficulties, hit points and armour
/// class by a scale and an amount added. No source system's rules are added to the game.
/// </summary>
public sealed class SystemTable
{
    public const string Folder = "import/systems";

    public string Name { get; init; } = "";
    public Dictionary<string, string> Skills { get; init; } = new(StringComparer.Ordinal);
    /// <summary>What a skill the table doesn't know becomes.</summary>
    public string Otherwise { get; init; } = "perception";
    public (double Scale, int Add) Difficulty { get; init; } = (1, 0);
    public (double Scale, int Add) HitPoints { get; init; } = (1, 0);
    public (double Scale, int Add) ArmorClass { get; init; } = (1, 0);

    public static string PathOf(string system) => $"{Folder}/{system}.json";

    public static SystemTable Load(ContentFiles files, string system) => Read(ContentNode.Read(files, PathOf(system)));

    public static SystemTable Read(ContentNode node)
    {
        node.RequireObject("a system table is a JSON object");
        node.Only("format", "version", "name", "skills", "otherwise", "difficulty", "hitPoints", "armorClass");
        if (node.At("format").AsText() != "yorehold.system")
        {
            throw node.Fail("format", "is \"yorehold.system\"");
        }
        node.At("version").AsInt(1, 1);
        var skills = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ContentNode> skill in node.Get("skills")?.RequireObject("skills is an object of book skill to game skill").Members() ?? Enumerable.Empty<KeyValuePair<string, ContentNode>>())
        {
            skills[Key(skill.Key)] = skill.Value.AsId();
        }
        (double, int) Line(string key)
        {
            if (node.Get(key) is not ContentNode line)
            {
                return (1, 0);
            }
            line.Only("scale", "add");
            return (line.Number("scale", 1, 0.01, 100), line.Int("add", 0, -1000, 1000));
        }
        return new SystemTable
        {
            Name = node.Text("name", ""),
            Skills = skills,
            Otherwise = node.Get("otherwise")?.AsId() ?? "perception",
            Difficulty = Line("difficulty"),
            HitPoints = Line("hitPoints"),
            ArmorClass = Line("armorClass"),
        };
    }

    /// <summary>"Move Silently", "knowledge (arcana)" and "open_lock" as the table writes them: "move-silently", "knowledge-arcana", "open-lock".</summary>
    public static string Key(string skill)
    {
        var chars = skill.Trim().ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray();
        string key = new(chars);
        while (key.Contains("--", StringComparison.Ordinal))
        {
            key = key.Replace("--", "-", StringComparison.Ordinal);
        }
        return key.Trim('-');
    }

    private static int Scaled(int value, (double Scale, int Add) line, int low) => Math.Max(low, (int)Math.Round(value * line.Scale, MidpointRounding.AwayFromZero) + line.Add);

    /// <summary>
    /// A copy of the outline with the book's numbers made the game's. isGameSkill says which
    /// names the game already has (skills and abilities); those are kept as they are.
    /// </summary>
    public Outline Apply(Outline outline, Func<string, bool> isGameSkill, List<OutlineBuilder.ReportLine> report)
    {
        var converted = new Outline { Title = outline.Title, System = outline.System };
        foreach (OutlineEntry entry in outline.Entries)
        {
            var data = (JsonObject)entry.Data.DeepClone();
            switch (entry.Kind)
            {
                case OutlineKind.Creature:
                    if (data["hp"] is JsonValue hp && hp.TryGetValue(out int points))
                    {
                        data["hp"] = Scaled(points, HitPoints, 1);
                    }
                    if (data["armorClass"] is JsonValue ac && ac.TryGetValue(out int armor))
                    {
                        data["armorClass"] = Scaled(armor, ArmorClass, 0);
                    }
                    break;
                case OutlineKind.Link:
                case OutlineKind.Container:
                    ConvertCheck(entry.Id, data["check"] as JsonObject, isGameSkill, report);
                    break;
                case OutlineKind.Dialogue:
                    foreach (JsonNode? node in data["nodes"] as JsonArray ?? new JsonArray())
                    {
                        foreach (JsonNode? choice in node?["choices"] as JsonArray ?? new JsonArray())
                        {
                            ConvertCheck(entry.Id, choice?["check"] as JsonObject, isGameSkill, report);
                        }
                    }
                    break;
            }
            converted.Entries.Add(new OutlineEntry { Id = entry.Id, Kind = entry.Kind, Data = data, From = entry.From, Picture = entry.Picture, Chapter = entry.Chapter });
        }
        return converted;
    }

    private void ConvertCheck(string entry, JsonObject? check, Func<string, bool> isGameSkill, List<OutlineBuilder.ReportLine> report)
    {
        if (check == null)
        {
            return;
        }
        string skill = Key(check["skill"]?.GetValue<string>() ?? "");
        if (!isGameSkill(skill))
        {
            if (Skills.TryGetValue(skill, out string? game))
            {
                skill = game;
            }
            else
            {
                report.Add(new OutlineBuilder.ReportLine(entry, $"the book's \"{skill}\" check has no match in {Name}; it is {Otherwise}"));
                skill = Otherwise;
            }
        }
        check["skill"] = skill;
        if (check["difficulty"] is JsonValue dc && dc.TryGetValue(out int difficulty))
        {
            check["difficulty"] = Scaled(difficulty, Difficulty, 1);
        }
    }
}
