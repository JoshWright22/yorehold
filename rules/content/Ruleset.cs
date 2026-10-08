namespace Yorehold.Rules;

public record AbilityDefinition(string Id, string Name);

public record SkillDefinition(string Id, string Name, string Ability);

/// <summary>A save of its own (Fortitude, Reflex, Will), rolled with an ability.</summary>
public record SaveDefinition(string Id, string Name, string Ability);

public record ProficiencyRank(string Id, string Name, int Bonus, bool AddsLevel);

/// <summary>A kind of feat a level can offer ("class", PF2e's "ancestry"), and what it is called.</summary>
public record FeatKind(string Id, string Name);

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
    /// <summary>
    /// Null: death saves (successes and failures). Else a dying value: it starts at Start when the
    /// creature drops, damage while down adds Damage, each turn a roll of RollKind against Dc moves
    /// it by the outcome's Change, it dies when Dead holds, and at 0 it is stable and Wounded goes up.
    /// </summary>
    public DyingTrack? Track { get; init; }
}

public sealed class DyingTrack
{
    public Formula Start { get; init; } = Formula.Parse("1", out _)!;
    public Formula Damage { get; init; } = Formula.Parse("1", out _)!;
    public Formula Dc { get; init; } = Formula.Parse("10 + dying", out _)!;
    public Formula Dead { get; init; } = Formula.Parse("dying >= 4", out _)!;
    public string RollKind { get; init; } = CheckRules.Check;
    public Dictionary<string, int> Change { get; init; } = new(StringComparer.Ordinal);
    /// <summary>Added to wounded each time the creature stops dying.</summary>
    public int WoundedStep { get; init; } = 1;
    /// <summary>The rests that take wounded back to 0 (PF2e: a night's rest).</summary>
    public List<string> WoundedClearedBy { get; init; } = new();
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
    /// <summary>Saves of their own; empty = every ability is a save.</summary>
    public List<SaveDefinition> Saves { get; init; } = new();
    /// <summary>The kinds of feat its feats and class levels name.</summary>
    public List<FeatKind> FeatKinds { get; init; } = DefaultFeatKinds();
    public string FeatKindName(string id) => FeatKinds.Find(k => k.Id == id)?.Name ?? id;

    /// <summary>The game's own kinds, when a system names none: class, skill, general and race feats.</summary>
    public static List<FeatKind> DefaultFeatKinds() =>
        FeatDefinition.Kinds.Select(k => new FeatKind(k, char.ToUpperInvariant(k[0]) + k[1..] + " feat")).ToList();

    /// <summary>Filled from the file's own list and then from the folder's conditions/ files.</summary>
    public List<ConditionDefinition> Conditions { get; } = new();
    public List<SurfaceDefinition> Surfaces { get; } = new();
    /// <summary>Effects that happen by themselves for whoever is granted them (triggers/).</summary>
    public List<TriggerDefinition> Triggers { get; } = new();
    public TriggerDefinition? Trigger(string id) => Triggers.Find(t => t.Id == id);
    public ModifierTable ModifierTable { get; init; }
    public int ScoreMin { get; init; } = 3;
    public int ScoreMax { get; init; } = 20;
    public int BaseArmorClass { get; init; } = 10;
    public string ArmorClassAbility { get; init; } = "dex";
    public RoleNames Roles { get; init; } = new();
    /// <summary>How attacks, checks and saves are rolled and read.</summary>
    public CheckRules Checks { get; init; } = new();
    /// <summary>How a creature's own numbers are counted, where the system says.</summary>
    public SheetFormulas Formulas { get; init; } = new();
    /// <summary>The steps of making a character.</summary>
    public CreationRules Creation { get; init; } = new();
    /// <summary>What the system calls an action, a bonus action and a reaction.</summary>
    public TurnWords Words { get; init; } = new();
    /// <summary>The parts of a character sheet the system shows, and their names.</summary>
    public SheetLayout Sheet { get; init; } = new();
    /// <summary>What harm uses up, in the order it does; empty = plain HP.</summary>
    public List<TrackDefinition> Tracks { get; init; } = new();
    /// <summary>Who acts when: one initiative order, or side by side.</summary>
    public TurnOrder TurnOrder { get; init; } = new();
    /// <summary>Defences of its own beside armour class.</summary>
    public List<DefenceDefinition> Defences { get; init; } = new();
    public DefenceDefinition? Defence(string id) => Defences.Find(d => d.Id == id);
    /// <summary>A defence attacks may name: "ac" or one of the system's.</summary>
    public bool IsDefence(string id) => id == DefenceDefinition.ArmorClass || Defence(id) != null;
    /// <summary>What the system calls a defence by id, for the sheet and the aim ("AC", "Defend").</summary>
    public string DefenceName(string id) => Defence(id)?.Name ?? (id == DefenceDefinition.ArmorClass ? Sheet.NameOf("ac") : id);
    public List<int> ProficiencyByLevel { get; init; } = new();
    public List<ProficiencyRank> ProficiencyRanks { get; init; } = new();
    public string ProficientRank { get; init; } = "";
    public string UntrainedRank { get; init; } = "";
    public int BaseDc { get; init; } = 10;
    public DeathRules Death { get; init; } = new();
    public List<int> XpForLevel { get; init; } = new();
    public int ActionsPerTurn { get; init; } = 1;
    public bool BonusActions { get; init; } = true;
    /// <summary>A turn starts with its speed to move for free; false: moving takes an action (Stride).</summary>
    public bool FreeMove { get; init; } = true;
    /// <summary>Added to each attack after the first in a turn, from "attacks" (made so far this turn). Null = none.</summary>
    public Formula? AttackPenalty { get; init; }
    /// <summary>A failed disarm springs the trap when this holds, from "margin" (total less DC) and "outcome" (its place, 0 = worst).</summary>
    public Formula TrapFumble { get; init; } = Formula.Parse("margin <= -5", out _)!;
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
    public SaveDefinition? SaveOf(string id) => Saves.Find(s => s.Id == id);
    /// <summary>What a save may name: one of the system's saves, or an ability.</summary>
    public bool IsSave(string id) => SaveOf(id) != null || Ability(id) != null;
    public ConditionDefinition? Condition(string id) => Conditions.Find(c => c.Id == id);
    public SurfaceDefinition? Surface(string id) => Surfaces.Find(s => s.Id == id);
    public RestDefinition? Rest(string id) => Rests.Find(r => r.Id == id);
    public ProficiencyRank? Rank(string id) => ProficiencyRanks.Find(r => r.Id == id);

    /// <summary>What an ability score adds to a roll.</summary>
    public int AbilityModifier(int score)
    {
        if (Formulas.Of("abilityModifier") is Formula own)
        {
            return own.Whole(name => name == "score" ? score : null);
        }
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

        var saves = new List<SaveDefinition>();
        foreach (ContentNode entry in node.Get("saves")?.Items() ?? Array.Empty<ContentNode>())
        {
            var save = new SaveDefinition(entry.At("id").AsName(), entry.Text("name", "", 64), entry.At("ability").AsText());
            if (saves.Any(s => s.Id == save.Id) || abilities.Any(a => a.Id == save.Id))
            {
                throw entry.Fail("id", "save ids are different from each other and from the abilities");
            }
            if (abilities.All(a => a.Id != save.Ability))
            {
                throw entry.Fail("ability", $"unknown ability \"{save.Ability}\"");
            }
            saves.Add(save);
        }

        List<FeatKind> featKinds = DefaultFeatKinds();
        if (node.Get("featKinds") is ContentNode kindList)
        {
            if (!kindList.IsArray || kindList.Count < 1 || kindList.Count > 16)
            {
                throw kindList.Fail("is a list of 1 to 16 feat kinds, each an id and a name");
            }
            featKinds = new List<FeatKind>();
            foreach (ContentNode entry in kindList.Items())
            {
                entry.RequireObject("is a feat kind with an id and a name");
                entry.Only("id", "name");
                var kind = new FeatKind(entry.At("id").AsId(), entry.At("name").AsText(64));
                if (featKinds.Any(k => k.Id == kind.Id))
                {
                    throw entry.Fail("id", "feat kinds have different ids");
                }
                featKinds.Add(kind);
            }
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
            Saves = saves,
            FeatKinds = featKinds,
            ModifierTable = table,
            ScoreMin = scoreMin,
            ScoreMax = scoreMax,
            BaseArmorClass = node.Int("baseArmorClass", 10),
            ArmorClassAbility = AbilityOrNone("armorClassAbility", "dex"),
            Roles = RolesFrom(node, abilities, skills, AbilityOrNone("initiativeAbility", "dex"), AbilityOrNone("hitDieAbility", "con")),
            Checks = CheckRules.Read(node.Get("checks")),
            Formulas = SheetFormulas.Read(node.Get("formulas")),
            Creation = CreationRules.Read(node.Get("creation")),
            Words = TurnWords.Read(node.Get("turnWords")),
            Sheet = SheetLayout.Read(node.Get("sheet")),
            Tracks = (node.Get("tracks")?.Items() ?? Array.Empty<ContentNode>()).Select(TrackDefinition.Read).ToList(),
            TurnOrder = TurnOrder.Read(node.Get("turnOrder")),
            Defences = (node.Get("defences")?.Items() ?? Array.Empty<ContentNode>()).Select(DefenceDefinition.Read).ToList(),
            ProficiencyByLevel = WholeList(node, "proficiencyByLevel", int.MinValue, int.MaxValue),
            ProficiencyRanks = ranks,
            ProficientRank = proficientRank,
            UntrainedRank = untrainedRank,
            BaseDc = node.Int("baseDc", 10, 0, 1000),
            Death = DeathFrom(node.Get("death")),
            XpForLevel = xp,
            ActionsPerTurn = node.Int("actionsPerTurn", 1, 1, 10),
            BonusActions = node.Bool("bonusActions", true),
            FreeMove = node.Bool("freeMove", true),
            AttackPenalty = node.Get("attackPenalty") is ContentNode penalty ? FormulaOf(penalty, "attacks", "trait.*") : null,
            TrapFumble = node.Get("trapFumble") is ContentNode fumble ? FormulaOf(fumble, "margin", "outcome") : Formula.Parse("margin <= -5", out _)!,
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
        foreach (string rest in rules.Death.Track?.WoundedClearedBy.Where(id => rules.Rest(id) == null) ?? Enumerable.Empty<string>())
        {
            throw new ContentException(node.File, "death.track.woundedClearedBy", $"unknown rest \"{rest}\"");
        }
        if (rules.Defences.Select(d => d.Id).Distinct().Count() != rules.Defences.Count)
        {
            throw new ContentException(node.File, "defences", "defences have different ids");
        }
        foreach (CheckKind kind in rules.Checks.Kinds.Values.Where(k => !rules.IsDefence(k.DefenceId)))
        {
            throw new ContentException(node.File, $"checks.{kind.Id}.defence", $"unknown defence \"{kind.DefenceId}\"; it is ac or one of defences");
        }
        if (rules.Tracks.Select(t => t.Id).Distinct().Count() != rules.Tracks.Count)
        {
            throw new ContentException(node.File, "tracks", "tracks have different ids");
        }
        foreach (TrackDefinition track in rules.Tracks)
        {
            if (track.Clears.FirstOrDefault(e => e != TrackDefinition.FightEnd && rules.Rest(e) == null) is string unknown)
            {
                throw new ContentException(node.File, $"tracks.{track.Id}.clears", $"unknown rest \"{unknown}\"; it is fightEnd or a rest's id");
            }
        }
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

    public void LoadTriggers(ContentFiles files, string folder)
    {
        foreach (string path in files.List(folder))
        {
            ContentNode node = ContentNode.Read(files, path);
            TriggerDefinition trigger = TriggerDefinition.Read(node);
            if (trigger.Id != ContentFiles.Stem(path))
            {
                throw node.Fail("id", $"\"{trigger.Id}\" doesn't match the file name");
            }
            trigger.Effect.Check(this, path);
            Triggers.RemoveAll(t => t.Id == trigger.Id);
            Triggers.Add(trigger);
        }
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
            if (surface.SaveAbility.Length > 0 && !IsSave(surface.SaveAbility))
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
            if (condition.SaveAbility.Length > 0 && !IsSave(condition.SaveAbility))
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
            Track = node.Get("track") is ContentNode track ? TrackFrom(track) : null,
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

    private static DyingTrack TrackFrom(ContentNode node)
    {
        node.RequireObject("is an object: start, damage, roll, dc, change, dead");
        node.Only("start", "damage", "roll", "dc", "change", "dead", "woundedStep", "woundedClearedBy");
        var track = new DyingTrack();
        var change = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ContentNode> member in node.Get("change")?.Members() ?? Enumerable.Empty<KeyValuePair<string, ContentNode>>())
        {
            change[member.Key] = member.Value.AsInt(-100, 100);
        }
        return new DyingTrack
        {
            Start = node.Get("start") is ContentNode start ? FormulaOf(start, "wounded", "critical") : track.Start,
            Damage = node.Get("damage") is ContentNode damage ? FormulaOf(damage, "dying", "wounded", "critical") : track.Damage,
            Dc = node.Get("dc") is ContentNode dc ? FormulaOf(dc, "dying", "wounded") : track.Dc,
            Dead = node.Get("dead") is ContentNode dead ? FormulaOf(dead, "dying", "wounded") : track.Dead,
            RollKind = node.Text("roll", CheckRules.Check, 64),
            Change = change,
            WoundedStep = node.Int("woundedStep", 1, 0, 100),
            WoundedClearedBy = node.Ids("woundedClearedBy"),
        };
    }

    private static Formula FormulaOf(ContentNode node, params string[] names)
    {
        Formula formula = Formula.Parse(node.AsText(2000), out string error) ?? throw node.Fail(error);
        // "trait.*" lets the formula read any trait of the weapon in hand
        bool Allowed(string name) => names.Contains(name)
            || (names.Contains("trait.*") && name.StartsWith("trait.", StringComparison.Ordinal) && name.Length > 6);
        foreach (string name in formula.Names.Where(name => !Allowed(name)))
        {
            throw node.Fail($"unknown name \"{name}\"; it can use {string.Join(", ", names)}");
        }
        return formula;
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
        List<int> array = node.Has("standardArray") ? WholeList(node, "standardArray", 0, 30) : defaults.StandardArray;
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
