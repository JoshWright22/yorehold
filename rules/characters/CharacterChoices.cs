using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// What a player picked when a character gained one level: the class the level went into and the
/// options taken with it, by kind ("feats", "spells", "skills").
/// </summary>
public sealed class LevelChoice
{
    public LevelChoice(string classId)
    {
        ClassId = classId;
    }

    public string ClassId { get; set; }
    public SortedDictionary<string, List<string>> Picks { get; } = new(StringComparer.Ordinal);

    public List<string> Picked(string kind) => Picks.TryGetValue(kind, out List<string>? ids) ? ids : new List<string>();

    public LevelChoice Copy()
    {
        var copy = new LevelChoice(ClassId);
        foreach (KeyValuePair<string, List<string>> pick in Picks)
        {
            copy.Picks[pick.Key] = pick.Value.ToList();
        }
        return copy;
    }
}

/// <summary>
/// A character as the player made it: choices, not results. The sheet is rebuilt from these and
/// the ruleset every time (CharacterBuild), so a rules change shows up at once. Live state (HP
/// lost, conditions, what is carried) belongs to the sheet, not here.
/// </summary>
public sealed class CharacterChoices
{
    public const int Version = 1;
    // 0 is a real rating in some systems (Fate's Mediocre +0)
    public const int LowestScore = 0;
    public const int HighestScore = 30;
    public const int MostLevels = 1000;
    /// <summary>How the scores were reached, so a creation screen can reopen the same method.</summary>
    public static readonly string[] Methods = { "roll", "pointBuy", "array", "fixed" };

    public string Name { get; set; } = "";
    /// <summary>A race id; empty where the ruleset has none.</summary>
    public string Race { get; set; } = "";
    public string Background { get; set; } = "";
    public string ScoreMethod { get; set; } = "fixed";
    /// <summary>Every ruleset ability, before race or background changes.</summary>
    public SortedDictionary<string, int> Scores { get; } = new(StringComparer.Ordinal);
    /// <summary>One per character level; the first is the starting class.</summary>
    public List<LevelChoice> Levels { get; } = new();
    public int Xp { get; set; }
    /// <summary>The ruleset id it was made under.</summary>
    public string Ruleset { get; set; } = "";
    public string Notes { get; set; } = "";
    /// <summary>The ruleset's fields as written for this character, by field id.</summary>
    public SortedDictionary<string, List<string>> Fields { get; } = new(StringComparer.Ordinal);

    public int Level => Levels.Count;

    public CharacterChoices Copy()
    {
        var copy = new CharacterChoices
        {
            Name = Name, Race = Race, Background = Background, ScoreMethod = ScoreMethod, Xp = Xp, Ruleset = Ruleset, Notes = Notes,
        };
        foreach (KeyValuePair<string, int> score in Scores)
        {
            copy.Scores[score.Key] = score.Value;
        }
        copy.Levels.AddRange(Levels.Select(l => l.Copy()));
        foreach (KeyValuePair<string, List<string>> field in Fields)
        {
            copy.Fields[field.Key] = field.Value.ToList();
        }
        return copy;
    }

    /// <summary>
    /// What can be checked without the compendium: a score for each of the ruleset's abilities and
    /// nothing else, in range, and a level. Point-buy scores are bought within the budget and an
    /// array's values each used once. Empty when all is well.
    /// </summary>
    public string Check(Ruleset rules)
    {
        foreach (KeyValuePair<string, List<string>> field in Fields)
        {
            FieldDefinition? definition = rules.Fields.Find(f => f.Id == field.Key);
            if (definition == null)
            {
                return $"fields.{field.Key}: not a field of this ruleset";
            }
            if (field.Value.Count > definition.Count)
            {
                return $"fields.{field.Key}: at most {definition.Count}";
            }
        }
        if (Levels.Count == 0)
        {
            return "levels: at least one is needed";
        }
        foreach (AbilityDefinition ability in rules.Abilities)
        {
            if (!Scores.TryGetValue(ability.Id, out int score))
            {
                return $"scores.{ability.Id}: missing";
            }
            if (score < LowestScore || score > HighestScore)
            {
                return $"scores.{ability.Id}: out of range";
            }
        }
        foreach (string ability in Scores.Keys)
        {
            if (rules.Ability(ability) == null)
            {
                return $"scores.{ability}: not an ability of this ruleset";
            }
        }
        if (ScoreMethod == "pointBuy")
        {
            SortedDictionary<int, int> costs = rules.ScoreMethods.PointCosts;
            int cost = PointBuyCost(rules, Scores);
            if (costs.Count == 0)
            {
                return "scoreMethod: this ruleset has no point buy";
            }
            if (cost < 0)
            {
                return $"scores: point buy only buys scores {costs.Keys.First()} to {costs.Keys.Last()}";
            }
            if (cost > rules.ScoreMethods.PointBudget)
            {
                return $"scores: cost {cost} points, the budget is {rules.ScoreMethods.PointBudget}";
            }
        }
        if (ScoreMethod == "array")
        {
            List<int> given = Scores.Values.OrderBy(v => v).ToList();
            List<int> wanted = rules.ScoreMethods.StandardArray.OrderBy(v => v).ToList();
            if (!given.SequenceEqual(wanted))
            {
                return "scores: the standard array uses each of its values once";
            }
        }
        return "";
    }

    /// <summary>What scores cost under the ruleset's point buy; -1 if one can't be bought.</summary>
    public static int PointBuyCost(Ruleset rules, IReadOnlyDictionary<string, int> scores)
    {
        int total = 0;
        foreach (int score in scores.Values)
        {
            if (!rules.ScoreMethods.PointCosts.TryGetValue(score, out int cost))
            {
                return -1;
            }
            total += cost;
        }
        return total;
    }

    /// <summary>A first-level character of a class with each score rolled, in the ruleset's ability order.</summary>
    public static CharacterChoices Roll(Ruleset rules, string name, string classId, Rng random)
    {
        var choices = new CharacterChoices { Name = name, ScoreMethod = "roll", Ruleset = rules.Id };
        foreach (AbilityDefinition ability in rules.Abilities)
        {
            choices.Scores[ability.Id] = Dice.Roll(rules.ScoreMethods.Roll, random).Total;
        }
        choices.Levels.Add(new LevelChoice(classId));
        return choices;
    }

    public JsonObject ToJson()
    {
        var scores = new JsonObject();
        foreach (KeyValuePair<string, int> score in Scores)
        {
            scores[score.Key] = score.Value;
        }
        var levels = new JsonArray();
        foreach (LevelChoice level in Levels)
        {
            var entry = new JsonObject { ["class"] = level.ClassId };
            if (level.Picks.Count > 0)
            {
                var picks = new JsonObject();
                foreach (KeyValuePair<string, List<string>> pick in level.Picks)
                {
                    picks[pick.Key] = new JsonArray(pick.Value.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray());
                }
                entry["picks"] = picks;
            }
            levels.Add(entry);
        }
        var j = new JsonObject { ["version"] = Version, ["name"] = Name };
        if (Race.Length > 0)
        {
            j["race"] = Race;
        }
        if (Background.Length > 0)
        {
            j["background"] = Background;
        }
        j["scoreMethod"] = ScoreMethod;
        j["scores"] = scores;
        j["levels"] = levels;
        j["xp"] = Xp;
        if (Ruleset.Length > 0)
        {
            j["ruleset"] = Ruleset;
        }
        if (Notes.Length > 0)
        {
            j["notes"] = Notes;
        }
        if (Fields.Count > 0)
        {
            var fields = new JsonObject();
            foreach (KeyValuePair<string, List<string>> field in Fields)
            {
                fields[field.Key] = new JsonArray(field.Value.Select(line => (JsonNode)JsonValue.Create(line)!).ToArray());
            }
            j["fields"] = fields;
        }
        return j;
    }

    public static CharacterChoices Read(ContentNode node)
    {
        node.RequireObject("a character is a JSON object");
        node.Only("version", "name", "race", "background", "scoreMethod", "scores", "levels", "xp", "ruleset", "notes", "fields");
        int version = node.Int("version", Version, 1);
        if (version > Version)
        {
            throw node.Fail("version", "was saved by a newer version of the game");
        }
        var choices = new CharacterChoices
        {
            Name = node.Text("name", "", 64),
            Race = node.Text("race", "", 64),
            Background = node.Text("background", "", 64),
            Ruleset = node.Text("ruleset", "", 64),
            Notes = node.Text("notes", "", 20000),
            Xp = node.Int("xp", 0, 0, 1000000000),
            ScoreMethod = node.Text("scoreMethod", "fixed", 64),
        };
        foreach (KeyValuePair<string, ContentNode> field in node.Get("fields")?.Members() ?? Enumerable.Empty<KeyValuePair<string, ContentNode>>())
        {
            if (!ContentIds.IsId(field.Key) || !field.Value.IsArray || field.Value.Count > 20)
            {
                throw field.Value.Fail("is a list of up to 20 lines");
            }
            choices.Fields[field.Key] = field.Value.Items().Select(line => line.AsText(FieldDefinition.MostLetters)).ToList();
        }
        if (!Methods.Contains(choices.ScoreMethod))
        {
            throw node.Fail("scoreMethod", "is \"roll\", \"pointBuy\", \"array\" or \"fixed\"");
        }
        if (node.Get("scores") is not ContentNode scores || !scores.IsObject)
        {
            throw node.Fail("scores", "maps each ability to a score");
        }
        foreach (KeyValuePair<string, ContentNode> score in scores.Members())
        {
            if (score.Key.Length == 0 || score.Key.Length > 64 || !score.Value.IsWhole)
            {
                throw score.Value.Fail($"is a score from {LowestScore} to {HighestScore}");
            }
            choices.Scores[score.Key] = score.Value.AsInt(LowestScore, HighestScore);
        }
        if (node.Get("levels") is not ContentNode levels || !levels.IsArray || levels.Count == 0 || levels.Count > MostLevels)
        {
            throw node.Fail("levels", "lists each level the character has, at least one");
        }
        foreach (ContentNode entry in levels.Items())
        {
            entry.RequireObject("is an object with a class");
            entry.Only("class", "picks");
            var level = new LevelChoice(entry.At("class").AsName());
            if (entry.Get("picks") is ContentNode picks)
            {
                if (!picks.IsObject)
                {
                    throw picks.Fail("maps a kind to a list of ids");
                }
                foreach (KeyValuePair<string, ContentNode> pick in picks.Members())
                {
                    if (pick.Key.Length == 0 || pick.Key.Length > 64 || !pick.Value.IsArray
                        || pick.Value.Items().Any(id => !id.IsString || id.AsText().Length == 0 || id.AsText().Length > 64))
                    {
                        throw pick.Value.Fail("is a list of ids, 1 to 64 characters each");
                    }
                    level.Picks[pick.Key] = pick.Value.Items().Select(id => id.AsText()).ToList();
                }
            }
            choices.Levels.Add(level);
        }
        return choices;
    }
}
