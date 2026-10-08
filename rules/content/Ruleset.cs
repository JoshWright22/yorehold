namespace Yorehold.Rules;

public record AbilityDefinition(string Id, string Name);

public record SkillDefinition(string Id, string Name, string Ability);

public record ProficiencyRank(string Id, string Name, int Bonus, bool AddsLevel);

public enum RecoveryKind
{
    None,
    Full,
    Fraction,
    Flat,
    HitDice,
}

/// <summary>How much HP a rest or a win gives back.</summary>
public record Recovery(RecoveryKind Kind = RecoveryKind.None, double Fraction = 0.5, int Amount = 0, bool ReviveDowned = false);

public class RestDefinition
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public Recovery Recovery { get; init; } = new();
    /// <summary>Uses in one adventure; 0 = unlimited.</summary>
    public int PerAdventure { get; init; }
    public List<string> Restores { get; init; } = new();
    public int SupplyCost { get; init; }
    public bool CampOnly { get; init; }
    public List<string> Resets { get; init; } = new();
}

public class DeathRules
{
    public bool Enabled { get; init; }
    public int SaveDc { get; init; } = 10;
    public int Successes { get; init; } = 3;
    public int Failures { get; init; } = 3;
    public int NaturalOneFailures { get; init; } = 2;
    public int NaturalTwentyHp { get; init; } = 1;
    public int DamageFailures { get; init; } = 1;
    public int CriticalDamageFailures { get; init; } = 2;
    public string DownedCondition { get; init; } = "";
    public string DyingCondition { get; init; } = "";
    public string StableCondition { get; init; } = "";
    public string DeadCondition { get; init; } = "";
}

public class ScoreMethods
{
    public string Roll { get; init; } = "4d6kh3";
    public List<int> StandardArray { get; init; } = new() { 15, 14, 13, 12, 10, 8 };
    public int PointBudget { get; init; } = 27;
    public SortedDictionary<int, int> PointCosts { get; init; } = new()
    {
        [8] = 0, [9] = 1, [10] = 2, [11] = 3, [12] = 4, [13] = 5, [14] = 7, [15] = 9,
    };
}

/// <summary>
/// ruleset.json's "roles": the ids the game's own procedures look for, so a system can name its
/// abilities, skills, conditions, actions and resources as it likes. The defaults are the yorehold
/// set's names; an empty one means the system has no such thing.
/// </summary>
public class RoleNames
{
    /// <summary>Adds to HP per level and to hit dice spent on a rest.</summary>
    public string HpAbility { get; init; } = "con";
    /// <summary>For a weapon that names no ability of its own.</summary>
    public string AttackAbility { get; init; } = "str";
    /// <summary>What carrying capacity is counted from; empty is the system's first ability.</summary>
    public string CarryAbility { get; init; } = "";
    /// <summary>An ability or a skill.</summary>
    public string Initiative { get; init; } = "dex";
    /// <summary>Skills or abilities for noticing, sneaking, and picking locks or disarming traps that name none.</summary>
    public string Perception { get; init; } = "perception";
    public string Stealth { get; init; } = "stealth";
    public string Thievery { get; init; } = "dex";
    public string Hidden { get; init; } = "hidden";
    public string Downed { get; init; } = "downed";
    public string Dead { get; init; } = "dead";
    public string Strike { get; init; } = "strike";
    public string Stride { get; init; } = "stride";
    public string EndTurn { get; init; } = "end-turn";
    public string Interact { get; init; } = "interact";
    /// <summary>Spell slot resources are this plus the slot's level.</summary>
    public string SlotPrefix { get; init; } = "slots-";
    /// <summary>The resource focus spells spend.</summary>
    public string Focus { get; init; } = "focus";
}

public record CompanionRules(int Limit = 0, int PartyLimit = 0, int ApprovalMin = -100, int ApprovalMax = 100);

public enum ModifierTable
{
    D20,
    Classic,
}

/// <summary>
/// ruleset.json: every number the rules run on. The defaults are what a field left out of the
/// file means, the same as the C++ framework's.
/// </summary>
public class Ruleset
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public List<AbilityDefinition> Abilities { get; init; } = new();
    public List<SkillDefinition> Skills { get; init; } = new();
    /// <summary>Filled from the file's own list and then from the folder's conditions/ files.</summary>
    public List<ConditionDefinition> Conditions { get; } = new();
    public List<SurfaceDefinition> Surfaces { get; } = new();
    public ModifierTable ModifierTable { get; init; }
    public int ScoreMin { get; init; } = 3;
    public int ScoreMax { get; init; } = 20;
    public int BaseArmorClass { get; init; } = 10;
    public string ArmorClassAbility { get; init; } = "dex";
    public RoleNames Roles { get; init; } = new();
    /// <summary>How attacks, checks and saves are rolled and read.</summary>
    public CheckRules Checks { get; init; } = new();
    public List<int> ProficiencyByLevel { get; init; } = new();
    public List<ProficiencyRank> ProficiencyRanks { get; init; } = new();
    public string ProficientRank { get; init; } = "";
    public string UntrainedRank { get; init; } = "";
    public int BaseDc { get; init; } = 10;
    public DeathRules Death { get; init; } = new();
    public List<int> XpForLevel { get; init; } = new();
    public int ActionsPerTurn { get; init; } = 1;
    public bool BonusActions { get; init; } = true;
    public bool StrikeCostsHands { get; init; }
    public bool SharedTurns { get; init; }
    public int FeetPerSquare { get; init; } = 5;
    public int CarryPerStrength { get; init; } = 15;
    public double EncumberedAt { get; init; } = 1;
    public double ImmobileAt { get; init; } = 2;
    public double EncumberedSpeed { get; init; } = 0.5;
    public int MagicItemLimit { get; init; }
    public int PassiveBase { get; init; } = 10;
    public List<RestDefinition> Rests { get; init; } = new();
    public Recovery AfterVictory { get; init; } = new();
    public int ReviveAfterVictory { get; init; }
    public int RevivePrice { get; init; }
    public int ReviveHp { get; init; }
    public CompanionRules Companions { get; init; } = new();
    public int DefaultHitDie { get; init; } = 8;
    public Dictionary<string, int> HitDieByClass { get; init; } = new();
    public ScoreMethods ScoreMethods { get; init; } = new();

    public AbilityDefinition? Ability(string id) => Abilities.Find(a => a.Id == id);
    public SkillDefinition? Skill(string id) => Skills.Find(s => s.Id == id);
    public ConditionDefinition? Condition(string id) => Conditions.Find(c => c.Id == id);
    public SurfaceDefinition? Surface(string id) => Surfaces.Find(s => s.Id == id);
    public RestDefinition? Rest(string id) => Rests.Find(r => r.Id == id);
    public ProficiencyRank? Rank(string id) => ProficiencyRanks.Find(r => r.Id == id);

    /// <summary>What an ability score adds to a roll.</summary>
    public int AbilityModifier(int score)
    {
        if (ModifierTable == ModifierTable.Classic)
        {
            return score switch
            {
                <= 3 => -3,
                <= 5 => -2,
                <= 8 => -1,
                <= 12 => 0,
                <= 15 => 1,
                <= 17 => 2,
                _ => 3,
            };
        }
        return (int)Math.Floor((score - 10) / 2.0);
    }

    /// <summary>The per-level table, for rulesets without ranks. 0 when there is no table.</summary>
    public int ProficiencyBonus(int level)
    {
        return ProficiencyByLevel.Count == 0 ? 0 : ProficiencyByLevel[Math.Clamp(level, 1, ProficiencyByLevel.Count) - 1];
    }

    /// <summary>A rank's bonus, plus the level where the rank adds it. 0 for a rank that isn't there.</summary>
    public int ProficiencyBonus(int level, string rank)
    {
        ProficiencyRank? found = Rank(rank);
        return found == null ? 0 : found.Bonus + (found.AddsLevel ? Math.Max(0, level) : 0);
    }

    public int LevelForXp(int xp)
    {
        int level = 1;
        foreach (int needed in XpForLevel)
        {
            if (xp < needed)
            {
                break;
            }
            level++;
        }
        return level;
    }

    public int HitDie(string characterClass)
    {
        return HitDieByClass.TryGetValue(characterClass, out int sides) ? sides : DefaultHitDie;
    }

    public static Ruleset Read(ContentNode node)
    {
        node.RequireObject("a ruleset is a JSON object");
        if (node.Int("version", 1) != 1)
        {
            throw node.Fail("version", "unsupported ruleset version");
        }
        string id = node.At("id").AsText();
        if (id.Length == 0)
        {
            throw node.Fail("id", "is empty");
        }
        ModifierTable table = node.Text("modifierTable", "d20") switch
        {
            "d20" => ModifierTable.D20,
            "classic" => ModifierTable.Classic,
            _ => throw node.Fail("modifierTable", "is \"d20\" or \"classic\""),
        };

        var abilities = new List<AbilityDefinition>();
        foreach (ContentNode entry in node.At("abilities").Items())
        {
            var ability = new AbilityDefinition(entry.At("id").AsText(), entry.At("name").AsText());
            if (ability.Id.Length == 0 || abilities.Any(a => a.Id == ability.Id))
            {
                throw entry.Fail("id", "ability ids are different and not empty");
            }
            abilities.Add(ability);
        }
        if (abilities.Count == 0)
        {
            throw node.Fail("abilities", "a ruleset needs at least one ability");
        }
        string AbilityOrNone(string key, string fallback)
        {
            // A system without the default ability ("dex", "con") goes without, rather than failing.
            if (!node.Has(key) && abilities.All(a => a.Id != fallback))
            {
                return "";
            }
            string value = node.Text(key, fallback);
            if (value.Length > 0 && abilities.All(a => a.Id != value))
            {
                throw node.Fail(key, $"unknown ability \"{value}\"");
            }
            return value;
        }

        var skills = new List<SkillDefinition>();
        foreach (ContentNode entry in node.Get("skills")?.Items() ?? Array.Empty<ContentNode>())
        {
            var skill = new SkillDefinition(entry.At("id").AsText(), entry.At("name").AsText(), entry.At("ability").AsText());
            if (skill.Id.Length == 0 || skills.Any(s => s.Id == skill.Id))
            {
                throw entry.Fail("id", "skill ids are different and not empty");
            }
            if (abilities.All(a => a.Id != skill.Ability))
            {
                throw entry.Fail("ability", $"unknown ability \"{skill.Ability}\"");
            }
            skills.Add(skill);
        }

        var ranks = new List<ProficiencyRank>();
        if (node.Get("proficiencyRanks") is ContentNode rankList)
        {
            if (!rankList.IsArray || rankList.Count > 100)
            {
                throw rankList.Fail("is a list of at most 100 ranks");
            }
            foreach (ContentNode entry in rankList.Items())
            {
                string rankId = entry.At("id").AsName();
                if (ranks.Any(r => r.Id == rankId))
                {
                    throw entry.Fail("id", $"two ranks have the id \"{rankId}\"");
                }
                ranks.Add(new ProficiencyRank(rankId, entry.Text("name", rankId, 64), entry.Int("bonus", 0, 0, 100), entry.Bool("addsLevel", false)));
            }
        }
        string proficientRank = node.Text("proficientRank", "");
        string untrainedRank = node.Text("untrainedRank", "");
        if (ranks.Count > 0)
        {
            if (ranks.All(r => r.Id != proficientRank))
            {
                throw node.Fail("proficientRank", $"unknown rank \"{proficientRank}\"");
            }
            if (ranks.All(r => r.Id != untrainedRank))
            {
                throw node.Fail("untrainedRank", $"unknown rank \"{untrainedRank}\"");
            }
        }

        int scoreMin = node.Int("scoreMin", 3, -100000, 100000);
        int scoreMax = node.Int("scoreMax", 20, -100000, 100000);
        if (scoreMin > scoreMax)
        {
            throw node.Fail("scoreMin", "is at most scoreMax");
        }
        List<int> xp = WholeList(node, "xpForLevel", 0, int.MaxValue);
        for (int i = 1; i < xp.Count; i++)
        {
            if (xp[i] < xp[i - 1])
            {
                throw node.Fail($"xpForLevel[{i}]", "each level needs at least as much XP as the one before");
            }
        }

        var rests = new List<RestDefinition>();
        foreach (ContentNode entry in node.Get("rests")?.Items() ?? Array.Empty<ContentNode>())
        {
            string restId = entry.At("id").AsText();
            if (restId.Length == 0 || rests.Any(r => r.Id == restId))
            {
                throw entry.Fail("id", "rest ids are different and not empty");
            }
            List<string> restores = entry.Names("restores");
            if (restores.Count > 100)
            {
                throw entry.Fail("restores", "lists at most 100 resources");
            }
            rests.Add(new RestDefinition
            {
                Id = restId,
                Name = entry.Text("name", ""),
                Recovery = RecoveryFrom(entry.Get("recovery")),
                PerAdventure = entry.Int("perAdventure", 0, 0),
                Restores = restores,
                SupplyCost = entry.Int("supplyCost", 0, 0, 100000),
                CampOnly = entry.Bool("campOnly", false),
                Resets = entry.Texts("resets"),
            });
        }
        foreach (ContentNode entry in node.Get("rests")?.Items() ?? Array.Empty<ContentNode>())
        {
            foreach (string other in entry.Texts("resets"))
            {
                if (rests.All(r => r.Id != other))
                {
                    throw entry.Fail("resets", $"unknown rest \"{other}\"");
                }
            }
        }

        var rules = new Ruleset
        {
            Id = id,
            Name = node.At("name").AsText(),
            Abilities = abilities,
            Skills = skills,
            ModifierTable = table,
            ScoreMin = scoreMin,
            ScoreMax = scoreMax,
            BaseArmorClass = node.Int("baseArmorClass", 10),
            ArmorClassAbility = AbilityOrNone("armorClassAbility", "dex"),
            Roles = RolesFrom(node, abilities, skills, AbilityOrNone("initiativeAbility", "dex"), AbilityOrNone("hitDieAbility", "con")),
            Checks = CheckRules.Read(node.Get("checks")),
            ProficiencyByLevel = WholeList(node, "proficiencyByLevel", int.MinValue, int.MaxValue),
            ProficiencyRanks = ranks,
            ProficientRank = proficientRank,
            UntrainedRank = untrainedRank,
            BaseDc = node.Int("baseDc", 10, 0, 1000),
            Death = DeathFrom(node.Get("death")),
            XpForLevel = xp,
            ActionsPerTurn = node.Int("actionsPerTurn", 1, 1, 10),
            BonusActions = node.Bool("bonusActions", true),
            StrikeCostsHands = node.Bool("strikeCostsHands", false),
            SharedTurns = node.Bool("sharedTurns", false),
            FeetPerSquare = node.Int("feetPerSquare", 5, 1),
            CarryPerStrength = node.Int("carryPerStrength", 15, 0),
            EncumberedAt = node.Number("encumberedAt", 1, 0, 1000),
            ImmobileAt = node.Number("immobileAt", 2, 0, 1000),
            EncumberedSpeed = node.Number("encumberedSpeed", 0.5, 0, 1),
            MagicItemLimit = node.Int("magicItemLimit", 0, 0, 1000),
            PassiveBase = node.Int("passiveBase", 10, -1000, 1000),
            Rests = rests,
            AfterVictory = RecoveryFrom(node.Get("afterVictory")),
            ReviveAfterVictory = node.Int("reviveAfterVictory", 0, 0),
            RevivePrice = node.Int("revivePrice", 0, 0, 100000000),
            ReviveHp = node.Int("reviveHp", 0, 0, 100000),
            Companions = CompanionsFrom(node.Get("companions")),
            DefaultHitDie = node.Int("defaultHitDie", 8, 1, 1000),
            HitDieByClass = ContentParts.NumbersFrom(node, "hitDieByClass", 1, 1000, idKeys: false),
            ScoreMethods = ScoreMethodsFrom(node.Get("scoreMethods")),
        };

        // Conditions written inline. A ruleset folder keeps them one to a file instead.
        foreach (ContentNode entry in node.Get("conditions")?.Items() ?? Array.Empty<ContentNode>())
        {
            ConditionDefinition condition = ConditionDefinition.Read(entry);
            if (rules.Condition(condition.Id) != null)
            {
                throw entry.Fail("id", $"two conditions have the id \"{condition.Id}\"");
            }
            rules.Conditions.Add(condition);
        }
        rules.CheckConditions(node.File);
        return rules;
    }

    /// <summary>Reads every condition file in a folder; one with the same id replaces the old one.</summary>
    public void LoadConditions(ContentFiles files, string folder)
    {
        foreach (string path in files.List(folder))
        {
            ContentNode node = ContentNode.Read(files, path);
            ConditionDefinition condition = ConditionDefinition.Read(node);
            if (condition.Id != ContentFiles.Stem(path))
            {
                throw node.Fail("id", $"\"{condition.Id}\" doesn't match the file name");
            }
            Conditions.RemoveAll(c => c.Id == condition.Id);
            Conditions.Add(condition);
        }
        CheckConditions(folder);
    }

    public void LoadSurfaces(ContentFiles files, string folder)
    {
        foreach (string path in files.List(folder))
        {
            ContentNode node = ContentNode.Read(files, path);
            SurfaceDefinition surface = SurfaceDefinition.Read(node);
            if (surface.Id != ContentFiles.Stem(path))
            {
                throw node.Fail("id", $"\"{surface.Id}\" doesn't match the file name");
            }
            if (surface.SaveAbility.Length > 0 && Ability(surface.SaveAbility) == null)
            {
                throw node.Fail("save.ability", $"unknown ability \"{surface.SaveAbility}\"");
            }
            if (surface.SlipAbility.Length > 0 && Ability(surface.SlipAbility) == null && Skill(surface.SlipAbility) == null)
            {
                throw node.Fail("slipAbility", $"unknown ability or skill \"{surface.SlipAbility}\"");
            }
            Surfaces.RemoveAll(s => s.Id == surface.Id);
            Surfaces.Add(surface);
        }
    }

    /// <summary>The death rules name conditions; this is checked once the condition files are in.</summary>
    public void CheckDeathRules(string file)
    {
        if (!Death.Enabled)
        {
            return;
        }
        foreach ((string field, string id) in new[]
        {
            ("death.downedCondition", Death.DownedCondition), ("death.dyingCondition", Death.DyingCondition),
            ("death.stableCondition", Death.StableCondition), ("death.deadCondition", Death.DeadCondition),
        })
        {
            if (id.Length > 0 && Condition(id) == null)
            {
                throw new ContentException(file, field, $"unknown condition \"{id}\"");
            }
        }
    }

    // What only the whole set can tell: saves use the ruleset's abilities, removes names conditions that exist.
    private void CheckConditions(string where)
    {
        foreach (ConditionDefinition condition in Conditions)
        {
            string file = where.EndsWith(".json", StringComparison.Ordinal) ? where : $"{where}/{condition.Id}.json";
            if (condition.SaveAbility.Length > 0 && Ability(condition.SaveAbility) == null)
            {
                throw new ContentException(file, "save.ability", $"unknown ability \"{condition.SaveAbility}\"");
            }
            foreach (string other in condition.Removes)
            {
                if (Condition(other) == null)
                {
                    throw new ContentException(file, "removes", $"unknown condition \"{other}\"");
                }
            }
        }
    }

    private static List<int> WholeList(ContentNode node, string key, int low, int high)
    {
        var list = new List<int>();
        foreach (ContentNode entry in node.Get(key)?.Items() ?? Array.Empty<ContentNode>())
        {
            list.Add(entry.AsInt(low, high));
        }
        return list;
    }

    private static Recovery RecoveryFrom(ContentNode? found)
    {
        if (found is not ContentNode node)
        {
            return new Recovery();
        }
        node.RequireObject("is an object with a kind");
        RecoveryKind kind = node.Text("kind", "none") switch
        {
            "none" => RecoveryKind.None,
            "full" => RecoveryKind.Full,
            "fraction" => RecoveryKind.Fraction,
            "flat" => RecoveryKind.Flat,
            "hitDice" => RecoveryKind.HitDice,
            _ => throw node.Fail("kind", "is \"none\", \"full\", \"fraction\", \"flat\" or \"hitDice\""),
        };
        return new Recovery(kind, node.Number("fraction", 0.5, 0, 1), node.Int("amount", 0, 0, 100000), node.Bool("reviveDowned", false));
    }

    private static DeathRules DeathFrom(ContentNode? found)
    {
        if (found is not ContentNode node)
        {
            return new DeathRules();
        }
        node.RequireObject("is an object");
        var death = new DeathRules
        {
            Enabled = node.Bool("enabled", false),
            SaveDc = node.Int("saveDc", 10, -1000, 1000),
            Successes = node.Int("successes", 3, 1, 100),
            Failures = node.Int("failures", 3, 1, 100),
            NaturalOneFailures = node.Int("naturalOneFailures", 2, 1, 100),
            NaturalTwentyHp = node.Int("naturalTwentyHp", 1, 0, 100000),
            DamageFailures = node.Int("damageFailures", 1, 1, 100),
            CriticalDamageFailures = node.Int("criticalDamageFailures", 2, 1, 100),
            DownedCondition = node.Text("downedCondition", "", 64),
            DyingCondition = node.Text("dyingCondition", "", 64),
            StableCondition = node.Text("stableCondition", "", 64),
            DeadCondition = node.Text("deadCondition", "", 64),
        };
        string[] named = new[] { death.DownedCondition, death.DyingCondition, death.StableCondition, death.DeadCondition }
            .Where(name => name.Length > 0).ToArray();
        if (named.Distinct().Count() != named.Length)
        {
            throw node.Fail("the four death conditions are different ones");
        }
        return death;
    }

    // The older top-level initiativeAbility and hitDieAbility stay as the defaults, so files
    // written before "roles" keep their meaning.
    private static RoleNames RolesFrom(ContentNode file, List<AbilityDefinition> abilities, List<SkillDefinition> skills,
        string initiative, string hpAbility)
    {
        // A default the system doesn't have falls back as the code did before roles (perception to
        // wis, stealth to dex), else to none, so a system with other names still loads.
        string Known(params string[] ids) =>
            ids.FirstOrDefault(id => abilities.Any(a => a.Id == id) || skills.Any(s => s.Id == id)) ?? "";
        var defaults = new RoleNames
        {
            HpAbility = hpAbility,
            AttackAbility = Known("str"),
            Initiative = initiative,
            Perception = Known("perception", "wis"),
            Stealth = Known("stealth", "dex"),
            Thievery = Known("dex"),
        };
        if (file.Get("roles") is not ContentNode node)
        {
            return defaults;
        }
        node.RequireObject("is an object of role names");
        node.Only("hpAbility", "attackAbility", "carryAbility", "initiative", "perception", "stealth", "thievery",
            "hidden", "downed", "dead", "strike", "stride", "endTurn", "interact", "slotPrefix", "focus");
        string Ability(string key, string fallback)
        {
            string value = node.Text(key, fallback, 64);
            if (value.Length > 0 && abilities.All(a => a.Id != value))
            {
                throw node.Fail(key, $"unknown ability \"{value}\"");
            }
            return value;
        }
        string Check(string key, string fallback)
        {
            string value = node.Text(key, fallback, 64);
            if (value.Length > 0 && abilities.All(a => a.Id != value) && skills.All(s => s.Id != value))
            {
                throw node.Fail(key, $"unknown ability or skill \"{value}\"");
            }
            return value;
        }
        // Conditions and actions may be missing from a system: the game then goes without them.
        string Name(string key, string fallback) => node.Text(key, fallback, 64);
        string prefix = Name("slotPrefix", defaults.SlotPrefix);
        if (prefix.Length == 0)
        {
            throw node.Fail("slotPrefix", "is a name of 1 to 60 characters");
        }
        return new RoleNames
        {
            HpAbility = Ability("hpAbility", defaults.HpAbility),
            AttackAbility = Ability("attackAbility", defaults.AttackAbility),
            CarryAbility = Ability("carryAbility", defaults.CarryAbility),
            Initiative = Check("initiative", defaults.Initiative),
            Perception = Check("perception", defaults.Perception),
            Stealth = Check("stealth", defaults.Stealth),
            Thievery = Check("thievery", defaults.Thievery),
            Hidden = Name("hidden", defaults.Hidden),
            Downed = Name("downed", defaults.Downed),
            Dead = Name("dead", defaults.Dead),
            Strike = Name("strike", defaults.Strike),
            Stride = Name("stride", defaults.Stride),
            EndTurn = Name("endTurn", defaults.EndTurn),
            Interact = Name("interact", defaults.Interact),
            SlotPrefix = prefix,
            Focus = Name("focus", defaults.Focus),
        };
    }

    private static CompanionRules CompanionsFrom(ContentNode? found)
    {
        if (found is not ContentNode node)
        {
            return new CompanionRules();
        }
        node.RequireObject("is an object");
        var rules = new CompanionRules(node.Int("limit", 0, 0, 100), node.Int("partyLimit", 0, 0, 100),
            node.Int("approvalMin", -100), node.Int("approvalMax", 100));
        if (rules.ApprovalMin > rules.ApprovalMax)
        {
            throw node.Fail("approvalMin", "is at most approvalMax");
        }
        return rules;
    }

    private static ScoreMethods ScoreMethodsFrom(ContentNode? found)
    {
        var defaults = new ScoreMethods();
        if (found is not ContentNode node)
        {
            return defaults;
        }
        node.RequireObject("is an object");
        string roll = node.Text("roll", defaults.Roll);
        if (!DiceText.IsValid(roll))
        {
            throw node.Fail("roll", "is dice like \"4d6kh3\"");
        }
        List<int> array = node.Has("standardArray") ? WholeList(node, "standardArray", 1, 30) : defaults.StandardArray;
        if (array.Count > 100)
        {
            throw node.Fail("standardArray", "has at most 100 values");
        }
        SortedDictionary<int, int> costs = defaults.PointCosts;
        if (node.Get("pointCosts") is ContentNode costList)
        {
            costs = new SortedDictionary<int, int>();
            if (!costList.IsObject)
            {
                throw costList.Fail("maps a score to its cost");
            }
            foreach (KeyValuePair<string, ContentNode> member in costList.Members())
            {
                if (!int.TryParse(member.Key, out int score) || score < 1 || score > 30)
                {
                    throw member.Value.Fail("bought scores are 1 to 30");
                }
                costs[score] = member.Value.AsInt(0, 1000);
            }
        }
        return new ScoreMethods
        {
            Roll = roll,
            StandardArray = array,
            PointBudget = node.Int("pointBudget", defaults.PointBudget, 0, 1000),
            PointCosts = costs,
        };
    }
}
