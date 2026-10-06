namespace Yorehold.Rules;

/// <summary>
/// A character being made, or one more level being added, with its sheet rebuilt after every
/// change so the screens can show it live. Everything the player can pick comes from the ruleset
/// and the compendium; anything the rules refuse shows up in Problem instead of a sheet.
/// </summary>
public sealed class CharacterDraft
{
    /// <summary>Making a character: origin (name, race, background), then class and scores, then the first level's picks.</summary>
    public static readonly string[] StepNames = { "Origin", "Class and scores", "Skills and feats" };

    private readonly Ruleset _rules;
    private readonly Compendium _compendium;
    private CharacterChoices _choices = new();
    private readonly List<string> _skillOptions = new();
    private readonly Dictionary<string, List<string>> _featOptions = new();

    public CharacterDraft(Ruleset rules, Compendium compendium)
    {
        _rules = rules;
        _compendium = compendium;
        _choices.Ruleset = rules.Id;
        List<string> classes = ClassIds();
        // the first class a new player sees: the plainest there is, if the ruleset has it
        string first = compendium.Class("fighter") != null ? "fighter" : classes.FirstOrDefault() ?? "";
        _choices.Levels.Add(new LevelChoice(first));
        SetMethod("array", new Rng(1));
    }

    /// <summary>One more level for a character, going into its latest class to start with.</summary>
    public static CharacterDraft LevelUp(Ruleset rules, Compendium compendium, CharacterChoices choices)
    {
        var draft = new CharacterDraft(rules, compendium) { LevellingUp = true, Step = StepNames.Length - 1 };
        draft._choices = choices.Copy();
        draft._choices.Levels.Add(new LevelChoice(draft._choices.Levels[^1].ClassId));
        draft.Rebuild();
        return draft;
    }

    public CharacterChoices Choices => _choices;
    public CharacterSheet? Sheet { get; private set; }
    public string Problem { get; private set; } = "";
    public bool LevellingUp { get; private init; }
    public int Step { get; set; }
    public IReadOnlyList<string> SkillOptions => _skillOptions;

    private LevelChoice Current => _choices.Levels[^1];

    public bool StepDone(int which) => StepProblem(which).Length == 0;

    /// <summary>What a step still needs, or empty.</summary>
    public string StepProblem(int which)
    {
        if (which == 0 && !LevellingUp)
        {
            if (_choices.Name.Trim().Length == 0)
            {
                return "Give the character a name.";
            }
            if (_compendium.Races.Count > 0 && !_compendium.Races.ContainsKey(_choices.Race))
            {
                return "Pick a race.";
            }
            if (_compendium.Backgrounds.Count > 0 && !_compendium.Backgrounds.ContainsKey(_choices.Background))
            {
                return "Pick a background.";
            }
        }
        if (which == 1 && !LevellingUp)
        {
            if (_compendium.Class(Current.ClassId) == null)
            {
                return "Pick a class.";
            }
            string error = _choices.Check(_rules);
            if (error.Length > 0)
            {
                return error;
            }
        }
        if (which == 2)
        {
            if (_compendium.Class(Current.ClassId) == null)
            {
                return "Pick a class.";
            }
            int skills = Math.Min(Math.Max(0, SkillPicks()), _skillOptions.Count);
            int have = Picked("skills").Count;
            if (have < skills)
            {
                int more = skills - have;
                return $"Pick {more} more skill{(more == 1 ? "." : "s.")}";
            }
            foreach (string kind in FeatKinds())
            {
                bool chosen = Picked("feats").Any(id => _compendium.Feats.TryGetValue(id, out FeatDefinition? f) && f.Kind == kind);
                if (!chosen && FeatOptions(kind).Count > 0)
                {
                    return $"Pick a {kind} feat.";
                }
            }
            if (Sheet == null)
            {
                return Problem;
            }
        }
        return "";
    }

    public bool Finished()
    {
        for (int i = 0; i < StepNames.Length; i++)
        {
            if (!StepDone(i))
            {
                return false;
            }
        }
        return Sheet != null;
    }

    public void SetName(string name)
    {
        _choices.Name = name;
        Rebuild();
    }

    public void SetRace(string id)
    {
        _choices.Race = id;
        Rebuild();
    }

    public void SetBackground(string id)
    {
        _choices.Background = id;
        Rebuild();
    }

    /// <summary>The class of the level being chosen. Its picks start over.</summary>
    public void SetClass(string id)
    {
        _choices.Levels[^1] = new LevelChoice(id);
        Rebuild();
    }

    /// <summary>Switching method starts the scores over: rolled afresh, the array in ability order, or the cheapest the point buy allows.</summary>
    public void SetMethod(string method, Rng random)
    {
        _choices.ScoreMethod = method;
        _choices.Scores.Clear();
        if (method == "roll")
        {
            Reroll(random);
            return;
        }
        List<int> values = ArrayValues();
        SortedDictionary<int, int> costs = _rules.ScoreMethods.PointCosts;
        int cheapest = costs.Count == 0 ? _rules.ScoreMin : costs.Keys.First();
        for (int i = 0; i < _rules.Abilities.Count; i++)
        {
            _choices.Scores[_rules.Abilities[i].Id] = method == "pointBuy" ? cheapest : values[i];
        }
        Rebuild();
    }

    public void Reroll(Rng random)
    {
        _choices.ScoreMethod = "roll";
        CharacterChoices rolled = CharacterChoices.Roll(_rules, _choices.Name, Current.ClassId, random);
        _choices.Scores.Clear();
        foreach (KeyValuePair<string, int> score in rolled.Scores)
        {
            _choices.Scores[score.Key] = score.Value;
        }
        Rebuild();
    }

    /// <summary>Point buy: the next score the cost table lists. Array: swap with whoever holds the next value up. Not for rolled scores.</summary>
    public bool CanRaise(string ability)
    {
        if (!_choices.Scores.TryGetValue(ability, out int score))
        {
            return false;
        }
        if (_choices.ScoreMethod == "pointBuy")
        {
            SortedDictionary<int, int> costs = _rules.ScoreMethods.PointCosts;
            int? next = costs.Keys.Where(k => k > score).Cast<int?>().FirstOrDefault();
            return costs.ContainsKey(score) && next != null && costs[next.Value] - costs[score] <= PointsLeft();
        }
        return _choices.ScoreMethod == "array" && _choices.Scores.Values.Any(v => v > score);
    }

    public bool CanLower(string ability)
    {
        if (!_choices.Scores.TryGetValue(ability, out int score))
        {
            return false;
        }
        if (_choices.ScoreMethod == "pointBuy")
        {
            SortedDictionary<int, int> costs = _rules.ScoreMethods.PointCosts;
            return costs.ContainsKey(score) && costs.Keys.First() < score;
        }
        return _choices.ScoreMethod == "array" && _choices.Scores.Values.Any(v => v < score);
    }

    public void Raise(string ability)
    {
        if (!CanRaise(ability))
        {
            return;
        }
        int score = _choices.Scores[ability];
        if (_choices.ScoreMethod == "pointBuy")
        {
            _choices.Scores[ability] = _rules.ScoreMethods.PointCosts.Keys.First(k => k > score);
        }
        else
        {
            string other = _choices.Scores.Where(s => s.Value > score).OrderBy(s => s.Value).First().Key;
            (_choices.Scores[ability], _choices.Scores[other]) = (_choices.Scores[other], score);
        }
        Rebuild();
    }

    public void Lower(string ability)
    {
        if (!CanLower(ability))
        {
            return;
        }
        int score = _choices.Scores[ability];
        if (_choices.ScoreMethod == "pointBuy")
        {
            _choices.Scores[ability] = _rules.ScoreMethods.PointCosts.Keys.Last(k => k < score);
        }
        else
        {
            string other = _choices.Scores.Where(s => s.Value < score).OrderByDescending(s => s.Value).First().Key;
            (_choices.Scores[ability], _choices.Scores[other]) = (_choices.Scores[other], score);
        }
        Rebuild();
    }

    public int PointsLeft()
    {
        int cost = CharacterChoices.PointBuyCost(_rules, _choices.Scores);
        return cost < 0 ? 0 : _rules.ScoreMethods.PointBudget - cost;
    }

    /// <summary>How many skills the level being chosen trains.</summary>
    public int SkillPicks() => Row()?.Skills ?? 0;

    /// <summary>The kinds of feat it offers, one each.</summary>
    public List<string> FeatKinds() => Row()?.Feats ?? new List<string>();

    /// <summary>Feats of that kind the character could take now: requirements met, not taken before.</summary>
    public List<string> FeatOptions(string kind) => _featOptions.TryGetValue(kind, out List<string>? ids) ? ids : new List<string>();

    public List<string> Picked(string kind) => Current.Picked(kind);

    /// <summary>On or off, up to SkillPicks.</summary>
    public void ToggleSkill(string id)
    {
        List<string> skills = Current.Picks.TryGetValue("skills", out List<string>? list) ? list : Current.Picks["skills"] = new List<string>();
        if (!skills.Remove(id) && skills.Count < SkillPicks())
        {
            skills.Add(id);
        }
        if (skills.Count == 0)
        {
            Current.Picks.Remove("skills");
        }
        Rebuild();
    }

    /// <summary>One feat per kind the level offers: a new one replaces any of its kind; the same one again takes it back.</summary>
    public void PickFeat(string id)
    {
        if (!_compendium.Feats.TryGetValue(id, out FeatDefinition? feat))
        {
            return;
        }
        List<string> feats = Current.Picks.TryGetValue("feats", out List<string>? list) ? list : Current.Picks["feats"] = new List<string>();
        bool wasPicked = feats.Contains(id);
        feats.RemoveAll(other => !_compendium.Feats.TryGetValue(other, out FeatDefinition? o) || o.Kind == feat.Kind);
        if (!wasPicked)
        {
            feats.Add(id);
        }
        if (feats.Count == 0)
        {
            Current.Picks.Remove("feats");
        }
        Rebuild();
    }

    // sorted by name, for the screens
    public List<string> RaceIds() => ByName(_compendium.Races, r => r.Name);
    public List<string> BackgroundIds() => ByName(_compendium.Backgrounds, b => b.Name);
    public List<string> ClassIds() => ByName(_compendium.Classes, k => k.Name);
    public List<string> FeatIds() => ByName(_compendium.Feats, f => f.Name);

    private static List<string> ByName<T>(IDictionary<string, T> definitions, Func<T, string> name)
    {
        return definitions.OrderBy(d => name(d.Value), StringComparer.Ordinal).ThenBy(d => d.Key, StringComparer.Ordinal).Select(d => d.Key).ToList();
    }

    private ClassLevel? Row()
    {
        ClassDefinition? definition = _compendium.Class(Current.ClassId);
        if (definition == null)
        {
            return null;
        }
        int classLevel = _choices.Levels.Count(l => l.ClassId == definition.Id);
        return classLevel >= 1 && classLevel <= definition.Levels.Count ? definition.Levels[classLevel - 1] : null;
    }

    // a ruleset whose array doesn't fit still gets scores
    private List<int> ArrayValues()
    {
        List<int> values = _rules.ScoreMethods.StandardArray.ToList();
        while (values.Count < _rules.Abilities.Count)
        {
            values.Add(_rules.ScoreMin);
        }
        return values;
    }

    private void Rebuild()
    {
        Problem = "";
        Sheet = null;
        _skillOptions.Clear();
        _featOptions.Clear();
        CharacterChoices built = _choices.Copy();
        if (built.Name.Trim().Length == 0)
        {
            built.Name = "New character"; // the sheet shows before the name is typed
        }
        string problem = built.Check(_rules);
        if (problem.Length > 0)
        {
            Problem = problem;
            return;
        }
        Sheet = CharacterBuild.Build(_rules, _compendium, built, out problem);
        Problem = problem;

        // Each feat this level could take, tried in place of the one of its kind: the rules decide.
        // Feats taken at earlier levels aren't offered again unless they can be.
        var taken = new HashSet<string>(_choices.Levels.Take(_choices.Levels.Count - 1).SelectMany(l => l.Picked("feats")));
        foreach (string kind in FeatKinds())
        {
            foreach (string id in FeatIds())
            {
                FeatDefinition feat = _compendium.Feats[id];
                if (feat.Kind != kind || (!feat.Repeatable && taken.Contains(id)))
                {
                    continue;
                }
                CharacterChoices trial = built.Copy();
                LevelChoice last = trial.Levels[^1];
                List<string> feats = last.Picked("feats").Where(other => _compendium.Feats.TryGetValue(other, out FeatDefinition? o) && o.Kind != kind).ToList();
                feats.Add(id);
                last.Picks["feats"] = feats;
                if (CharacterBuild.Build(_rules, _compendium, trial) != null)
                {
                    if (!_featOptions.ContainsKey(kind))
                    {
                        _featOptions[kind] = new List<string>();
                    }
                    _featOptions[kind].Add(id);
                }
            }
        }
        // what this level could train: every skill the character doesn't have without its picks
        built.Levels[^1].Picks.Remove("skills");
        if (CharacterBuild.Build(_rules, _compendium, built) is CharacterSheet without)
        {
            foreach (SkillDefinition skill in _rules.Skills)
            {
                bool ranked = without.ProficiencyRanks.TryGetValue(skill.Id, out string? rank) && rank != _rules.UntrainedRank;
                if (!without.Proficiencies.Contains(skill.Id) && !ranked)
                {
                    _skillOptions.Add(skill.Id);
                }
            }
        }
    }
}
