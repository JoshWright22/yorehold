using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

/// <summary>
/// Characters as choices: the file, building the sheet from them, level tables, feats, hands, the
/// score methods, the creation draft and the library. The C++ framework's ChoicesTests.
/// </summary>
public class CharacterTests
{
    // Like the framework's "modern" test ruleset: six abilities, a few skills, a proficiency table and no ranks.
    public static Ruleset Modern() => RulesTesting.Rules("""
        {"id":"modern","name":"Modern",
         "abilities":[{"id":"str","name":"Strength"},{"id":"dex","name":"Dexterity"},{"id":"con","name":"Constitution"},
            {"id":"int","name":"Intelligence"},{"id":"wis","name":"Wisdom"},{"id":"cha","name":"Charisma"}],
         "skills":[{"id":"athletics","name":"Athletics","ability":"str"},{"id":"acrobatics","name":"Acrobatics","ability":"dex"},
            {"id":"stealth","name":"Stealth","ability":"dex"},{"id":"arcana","name":"Arcana","ability":"int"},
            {"id":"perception","name":"Perception","ability":"wis"},{"id":"survival","name":"Survival","ability":"wis"}],
         "proficiencyByLevel":[2,2,2,2,3,3,3,3,4,4,4,4,5,5,5,5,6,6,6,6],
         "xpForLevel":[300,900,2700]}
        """);

    // A soldier with a sword that adds to attacks, and a plain scout.
    public static Compendium Classes()
    {
        var compendium = new Compendium();
        compendium.Items["sword"] = new ItemDefinition
        {
            Id = "sword", Name = "Sword", Slot = "mainHand", Damage = "1d8",
            Modifiers = new List<Modifier> { new("attack", ModifierOp.Add, 1) },
        };
        compendium.Classes["soldier"] = new ClassDefinition
        {
            Id = "soldier", Name = "Soldier", HitDie = 10, BonusHp = 2, Speed = 25,
            Proficiencies = new List<string> { "athletics", "weapons" }, Items = new List<string> { "sword" },
            Resources = new Dictionary<string, Resource> { ["second-wind"] = new Resource(1, 1) },
        };
        compendium.Classes["scout"] = new ClassDefinition { Id = "scout", Name = "Scout", HitDie = 8 };
        return compendium;
    }

    public static CharacterChoices Ana(Ruleset rules)
    {
        var c = new CharacterChoices { Name = "Ana" };
        foreach (AbilityDefinition ability in rules.Abilities)
        {
            c.Scores[ability.Id] = 10;
        }
        c.Scores["con"] = 14;
        c.Levels.Add(new LevelChoice("soldier"));
        return c;
    }

    private static string Text(CharacterChoices choices) => choices.ToJson().ToJsonString();

    private static CharacterChoices ReadChoices(string json) => CharacterChoices.Read(TestContent.Json(json, "ana.json"));

    [Fact]
    public void ChoicesFiles()
    {
        Ruleset rules = Modern();
        CharacterChoices choices = Ana(rules);
        choices.Race = "elf";
        choices.Background = "sailor";
        choices.ScoreMethod = "pointBuy";
        choices.Xp = 120;
        choices.Ruleset = "test";
        choices.Notes = "Keeps a ledger.";
        var second = new LevelChoice("scout");
        second.Picks["skills"] = new List<string> { "stealth" };
        second.Picks["feats"] = new List<string> { "quick" };
        choices.Levels.Add(second);
        CharacterChoices back = ReadChoices(Text(choices));
        Assert.Equal(Text(choices), Text(back));
        Assert.Equal(2, back.Level);
        Assert.Equal(new[] { "quick" }, back.Levels[1].Picks["feats"]);

        // every mistake names its field
        string Refused(Action<JsonObject> spoil)
        {
            JsonObject j = choices.ToJson();
            spoil(j);
            return TestContent.Refused(() => ReadChoices(j.ToJsonString())).Message;
        }
        Assert.Contains("scores.str", Refused(j => j["scores"]!["str"] = 31));
        Assert.Contains("scores.str", Refused(j => j["scores"]!["str"] = "ten"));
        Assert.Contains("scores", Refused(j => j.Remove("scores")));
        Assert.Contains("levels", Refused(j => j["levels"] = new JsonArray()));
        Assert.Contains("levels[0].class", Refused(j => j["levels"]![0]!.AsObject().Remove("class")));
        Assert.Contains("levels[1].picks.feats", Refused(j => j["levels"]![1]!["picks"]!["feats"] = new JsonArray(JsonValue.Create(""))));
        Assert.Contains("levels[0].subclass", Refused(j => j["levels"]![0]!["subclass"] = "x"));
        Assert.Contains("scoreMethod", Refused(j => j["scoreMethod"] = "dream"));
        Assert.Contains("xp", Refused(j => j["xp"] = -1));
        Assert.Contains("version", Refused(j => j["version"] = 2));
        Assert.Contains("colour", Refused(j => j["colour"] = "red"));
        Assert.ThrowsAny<Exception>(() => ReadChoices("{"));

        // the ruleset decides which abilities there are
        Assert.Equal("", choices.Check(rules));
        CharacterChoices missing = choices.Copy();
        missing.Scores.Remove("wis");
        Assert.Equal("scores.wis: missing", missing.Check(rules));
        CharacterChoices extra = choices.Copy();
        extra.Scores["luck"] = 12;
        Assert.Equal("scores.luck: not an ability of this ruleset", extra.Check(rules));
    }

    [Fact]
    public void Building()
    {
        Ruleset rules = Modern();
        Compendium compendium = Classes();
        CharacterChoices choices = Ana(rules);
        CharacterSheet? first = CharacterBuild.Build(rules, compendium, choices, out string error);
        Assert.Equal("", error);
        Assert.NotNull(first);
        Assert.True(first!.Name == "Ana" && first.Level == 1 && first.ClassName == "Soldier" && first.HitDie == "1d10");
        Assert.True(first.MaxHp == 10 + 2 + 2 && first.Hp == first.MaxHp && first.SpeedFeet == 25);
        Assert.True(first.AbilityScore("con") == 14 && first.Proficiencies.Contains("athletics"));
        Assert.True(first.WeaponItem?.Id == "sword" && first.Weapon?.Damage == "1d8" && first.Resources["second-wind"].Max == 1);

        // later levels add their class's average hit die (rounded up) plus CON; skill picks train
        var scout = new LevelChoice("scout");
        scout.Picks["skills"] = new List<string> { "stealth" };
        choices.Levels.Add(scout);
        choices.Levels.Add(new LevelChoice("soldier"));
        CharacterSheet third = CharacterBuild.Build(rules, compendium, choices)!;
        Assert.True(third.Level == 3 && third.MaxHp == 14 + (5 + 2) + (6 + 2));
        Assert.True(third.ClassName == "Soldier / Scout" && third.Proficiencies.Contains("stealth"));

        // a weak constitution still gains at least 1 HP a level
        CharacterChoices frail = choices.Copy();
        frail.Scores["con"] = 1;
        Assert.Equal(Math.Max(1, 12 - 5) + 1 + Math.Max(1, 6 - 5), CharacterBuild.Build(rules, compendium, frail)!.MaxHp);

        CharacterChoices unknown = choices.Copy();
        unknown.Levels[1].ClassId = "bard";
        Assert.Null(CharacterBuild.Build(rules, compendium, unknown, out error));
        Assert.Equal("levels[1].class: no class \"bard\"", error);
        CharacterChoices noScores = choices.Copy();
        noScores.Scores.Clear();
        Assert.Null(CharacterBuild.Build(rules, compendium, noScores, out error));
        Assert.StartsWith("scores.", error);

        // rolled scores are the ruleset's dice, the same for the same seed
        CharacterChoices rolled = CharacterChoices.Roll(rules, "Bo", "soldier", new Rng(42));
        Assert.True(rolled.ScoreMethod == "roll" && rolled.Level == 1 && rolled.Ruleset == "modern");
        Assert.Equal(Text(rolled), Text(CharacterChoices.Roll(rules, "Bo", "soldier", new Rng(42))));
        Assert.All(rolled.Scores.Values, score => Assert.InRange(score, 3, 18));
    }

    [Fact]
    public void RebuildingKeepsWhatWasLivedThrough()
    {
        Ruleset rules = Modern();
        Compendium compendium = Classes();
        CharacterChoices choices = Ana(rules);
        CharacterSheet live = CharacterBuild.Build(rules, compendium, choices)!;
        live.Hp = 5;
        live.TempHp = 3;
        live.Xp = 200;
        live.Resources["second-wind"] = new Resource(0, 1);
        live.Resources["torch"] = new Resource(2, 3);
        live.AddModifier("blessed", new Modifier("attack", ModifierOp.Add, 1), 3);
        live.Inventory.Add(new Item(new ItemDefinition { Id = "rope", Name = "Rope" }));
        int attack = live.AttackModifier(rules);

        // a new level raises the maximum but not the HP lost; extra modifiers and gear stay
        choices.Levels.Add(new LevelChoice("soldier"));
        live.AdoptBuild(CharacterBuild.Build(rules, compendium, choices)!);
        Assert.True(live.Level == 2 && live.MaxHp == 14 + 8 && live.Hp == 5 && live.TempHp == 3 && live.Xp == 200);
        Assert.True(live.AttackModifier(rules) == attack && live.Inventory.Count == 2 && live.HasCondition("blessed"));
        Assert.True(live.Resources["second-wind"].Current == 0 && live.Resources["torch"].Current == 2);

        // lower scores bring HP down with the maximum
        live.Hp = live.MaxHp;
        choices.Scores["con"] = 10;
        live.AdoptBuild(CharacterBuild.Build(rules, compendium, choices)!);
        Assert.True(live.MaxHp == 12 + 6 && live.Hp == 18);

        // XP raises the level, never lowers it
        live.AddXp(rules, 800);
        Assert.Equal((1000, 3), (live.Xp, live.Level));
    }

    // The options folder of the C++ test: an elf with a keen eye, a sailor, and three feats.
    private static Compendium WithOptions(Scratch scratch)
    {
        scratch.Write("good/feats/keen.json", """{"id":"keen","kind":"race","ranks":{"perception":"expert"}}""")
            .Write("good/feats/tough.json", """{"id":"tough","modifiers":[{"stat":"maxHp","value":3}],"resources":{"grit":1},"requires":{"level":2,"abilities":{"con":12}}}""")
            .Write("good/feats/sure-foot.json", """{"id":"sure-foot","kind":"class","proficiencies":["acrobatics"],"requires":{"classes":["scout"],"races":["elf"],"proficiencies":["stealth"]}}""")
            .Write("good/races/elf.json", """{"id":"elf","name":"Elf","speed":35,"darkvision":60,"abilities":{"dex":2,"con":-2},"feats":["keen"]}""")
            .Write("good/backgrounds/sailor.json", """{"id":"sailor","proficiencies":["athletics","survival"],"items":["sword"],"abilities":{"str":1}}""");
        Compendium compendium = Classes();
        compendium.LoadOptions(new ContentFiles(scratch.Folder), "good");
        return compendium;
    }

    [Fact]
    public void RacesBackgroundsAndFeats()
    {
        using var scratch = new Scratch();
        Compendium compendium = WithOptions(scratch);
        Ruleset rules = Modern();
        CharacterChoices choices = Ana(rules);
        choices.Race = "elf";
        choices.Background = "sailor";
        CharacterSheet elf = CharacterBuild.Build(rules, compendium, choices, out string error)!;
        Assert.Equal("", error);
        Assert.True(elf.Ancestry == "Elf" && elf.AbilityScore("dex") == 12 && elf.AbilityScore("con") == 12 && elf.AbilityScore("str") == 11);
        Assert.True(elf.SpeedFeet == 35 && elf.Stats.Integer("darkvision") == 60 && elf.MaxHp == 10 + 2 + 1);
        Assert.True(elf.Proficiencies.Contains("survival") && elf.ProficiencyRank(rules, "perception") == "expert");
        // the second sword stays in the pack: the first one has the hand
        Assert.True(elf.Inventory.Count == 2 && elf.Inventory.Count(i => i.Equipped) == 1);

        // feat picks: requirements judged at the level they were taken
        string Refusal(CharacterChoices c)
        {
            Assert.Null(CharacterBuild.Build(rules, compendium, c, out string why));
            return why;
        }
        CharacterChoices picked = choices.Copy();
        picked.Levels[0].Picks["feats"] = new List<string> { "tough" };
        Assert.Equal("levels[0].picks.feats: \"tough\" needs level 2", Refusal(picked));
        picked.Levels[0].Picks.Clear();
        var toughLevel = new LevelChoice("soldier");
        toughLevel.Picks["feats"] = new List<string> { "tough" };
        picked.Levels.Add(toughLevel);
        CharacterSheet second = CharacterBuild.Build(rules, compendium, picked)!;
        Assert.True(second.MaxHp == 13 + 7 + 3 && second.Resources["grit"].Max == 1);
        CharacterChoices weak = picked.Copy();
        weak.Scores["con"] = 12; // 10 after the elf's -2
        Assert.Equal("levels[1].picks.feats: \"tough\" needs con 12", Refusal(weak));
        CharacterChoices twice = picked.Copy();
        twice.Levels.Add(toughLevel.Copy());
        Assert.Equal("levels[2].picks.feats: \"tough\" is already taken", Refusal(twice));
        CharacterChoices foot = choices.Copy();
        var footLevel = new LevelChoice("scout");
        footLevel.Picks["feats"] = new List<string> { "sure-foot" };
        foot.Levels.Add(footLevel);
        Assert.Equal("levels[1].picks.feats: \"sure-foot\" needs training in stealth", Refusal(foot));
        foot.Levels[1].Picks["skills"] = new List<string> { "stealth" };
        Assert.NotNull(CharacterBuild.Build(rules, compendium, foot));
        CharacterChoices human = foot.Copy();
        human.Race = "";
        Assert.Equal("levels[1].picks.feats: \"sure-foot\" is for another race", Refusal(human));
        CharacterChoices soldierOnly = foot.Copy();
        soldierOnly.Levels[1].ClassId = "soldier";
        Assert.Equal("levels[1].picks.feats: \"sure-foot\" is for another class", Refusal(soldierOnly));
        CharacterChoices missing = choices.Copy();
        missing.Levels[0].Picks["feats"] = new List<string> { "flight" };
        Assert.Equal("levels[0].picks.feats: no feat \"flight\"", Refusal(missing));
        CharacterChoices noRace = choices.Copy();
        noRace.Race = "giant";
        Assert.Equal("race: no race \"giant\"", Refusal(noRace));
        CharacterChoices noBackground = choices.Copy();
        noBackground.Background = "pirate";
        Assert.Equal("background: no background \"pirate\"", Refusal(noBackground));

        // a rebuild swaps the feats' modifiers for the new set and leaves others alone
        CharacterSheet live = second;
        live.AddModifier("blessed", new Modifier("maxHp", ModifierOp.Add, 1), 3);
        live.AdoptBuild(CharacterBuild.Build(rules, compendium, picked)!);
        Assert.Equal(23 + 1, live.MaxHp);
        live.AdoptBuild(CharacterBuild.Build(rules, compendium, choices)!);
        Assert.True(live.MaxHp == 13 + 1 && live.HasCondition("blessed"));
    }

    [Fact]
    public void LevelTables()
    {
        Compendium compendium = Classes();
        ClassDefinition mage = ClassDefinition.Read(TestContent.Json("""
            {"id":"mage","name":"Mage","hitDie":6,"proficiencyRanks":{"dc":"trained"},"levels":[
                {"features":[{"id":"spellbook","name":"Spellbook","resources":{"recovery":1}}], "slots":{"1":2}, "skills":1},
                {"feats":["class"], "slots":{"1":3}},
                {"ranks":{"dc":"expert"}, "slots":{"1":4,"2":2}, "feats":["general","skill"],
                 "features":[{"id":"focus","modifiers":[{"stat":"dc","value":1}]}]}]}
            """, "classes/mage.json"));
        compendium.Classes["mage"] = mage;
        compendium.Feats["arcane-eye"] = FeatDefinition.Read(TestContent.Json("""{"id":"arcane-eye","kind":"class","requires":{"classes":["mage"]}}"""));
        compendium.Feats["sturdy"] = FeatDefinition.Read(TestContent.Json("""{"id":"sturdy","kind":"general","modifiers":[{"stat":"maxHp","value":2}]}"""));
        compendium.Feats["lore"] = FeatDefinition.Read(TestContent.Json("""{"id":"lore","kind":"skill"}"""));

        Ruleset rules = Modern();
        CharacterChoices choices = Ana(rules);
        choices.Levels.Clear();
        LevelChoice Level(string classId, string kind = "", params string[] ids)
        {
            var level = new LevelChoice(classId);
            if (kind.Length > 0)
            {
                level.Picks[kind] = ids.ToList();
            }
            return level;
        }
        choices.Levels.Add(Level("mage", "skills", "arcana"));
        CharacterSheet first = CharacterBuild.Build(rules, compendium, choices, out string error)!;
        Assert.Equal("", error);
        Assert.True(first.Resources["slots-1"].Max == 2 && first.Resources["recovery"].Max == 1 && first.Proficiencies.Contains("arcana"));

        // each row in turn: slots replace the last row's, ranks rise, features stay
        choices.Levels.Add(Level("mage", "feats", "arcane-eye"));
        choices.Levels.Add(Level("mage", "feats", "sturdy", "lore"));
        CharacterSheet third = CharacterBuild.Build(rules, compendium, choices, out error)!;
        Assert.Equal("", error);
        Assert.True(third.Resources["slots-1"].Max == 4 && third.Resources["slots-2"].Max == 2);
        Assert.True(third.ProficiencyRank(rules, "dc") == "expert" && third.Stats.Integer("dc") == 1 && third.Resources["recovery"].Max == 1);
        Assert.Equal((6 + 2) + 2 * (4 + 2) + 2, third.MaxHp);

        // picks must fit what the row offers
        string Refusal(CharacterChoices c)
        {
            Assert.Null(CharacterBuild.Build(rules, compendium, c, out string why));
            return why;
        }
        CharacterChoices extraSkill = choices.Copy();
        extraSkill.Levels[0].Picks["skills"] = new List<string> { "arcana", "stealth" };
        Assert.Equal("levels[0].picks.skills: 2 picked, this level offers 1", Refusal(extraSkill));
        CharacterChoices wrongKind = choices.Copy();
        wrongKind.Levels[1].Picks["feats"] = new List<string> { "sturdy" };
        Assert.Equal("levels[1].picks.feats: \"sturdy\" is a general feat, and this level has no general feat to choose", Refusal(wrongKind));
        CharacterChoices tooMany = choices.Copy();
        tooMany.Levels[2].Picks["feats"] = new List<string> { "sturdy", "lore", "arcane-eye" };
        Assert.StartsWith("levels[2].picks.feats: \"arcane-eye\" is a class feat", Refusal(tooMany));
        CharacterChoices open = choices.Copy();
        open.Levels[2].Picks.Clear();
        Assert.NotNull(CharacterBuild.Build(rules, compendium, open)); // a choice left open builds; it's asked for later

        // any level into any class: the class's own level picks its row, and slots don't stack
        CharacterChoices multi = choices.Copy();
        multi.Levels.Insert(1, new LevelChoice("soldier"));
        CharacterSheet mixed = CharacterBuild.Build(rules, compendium, multi, out error)!;
        Assert.Equal("", error);
        Assert.True(mixed.Level == 4 && mixed.ClassName == "Mage / Soldier");
        Assert.True(mixed.Resources["slots-1"].Max == 4 && mixed.MaxHp == 22 + 8);
        CharacterChoices twoCasters = choices.Copy();
        compendium.Classes["mage2"] = new ClassDefinition { Id = "mage2", Name = "Mage", HitDie = 6, Levels = mage.Levels };
        twoCasters.Levels.Add(new LevelChoice("mage2"));
        CharacterSheet both = CharacterBuild.Build(rules, compendium, twoCasters)!;
        Assert.True(both.Resources["slots-1"].Max == 4 && both.Resources["recovery"].Max == 2);
    }

    [Fact]
    public void HandsAreShared()
    {
        var c = new CharacterSheet();
        Item Thing(string id, string slot, int hands, params Modifier[] modifiers) =>
            new(new ItemDefinition { Id = id, Name = id, Slot = slot, Hands = hands, Modifiers = modifiers.ToList() });
        c.Inventory.AddRange(new[]
        {
            Thing("sword", "mainHand", 1), Thing("shield", "offHand", 1, new Modifier("ac", ModifierOp.Add, 2)),
            Thing("greatsword", "mainHand", 2), Thing("mail", "armor", 1),
        });
        c.Stats.SetBase("ac", 10);
        Assert.True(c.Equip(0) && c.Equip(1) && c.Equip(3) && c.HandsInUse() == 2 && c.Stats.Integer("ac") == 12);
        // both hands on the greatsword: the sword leaves its slot and the shield its hand; armour stays
        Assert.True(c.Equip(2) && c.HandsInUse() == 2 && !c.Inventory[0].Equipped && !c.Inventory[1].Equipped && c.Inventory[3].Equipped
            && c.Stats.Integer("ac") == 10);
        // taking the shield back up puts the greatsword away
        Assert.True(c.Equip(1) && !c.Inventory[2].Equipped && c.HandsInUse() == 1 && c.WeaponItem == null);
        Assert.True(c.Equip(0) && c.HandsInUse() == 2 && c.Inventory[1].Equipped);

        // taking one out keeps the others' modifiers on, though their places moved
        c.TakeOut(0);
        Assert.True(c.Inventory[0].Id == "shield" && c.Inventory[0].Equipped && c.Stats.Integer("ac") == 12);
        c.Unequip(0);
        Assert.Equal(10, c.Stats.Integer("ac"));
    }

    [Fact]
    public void ScoreMethods()
    {
        Ruleset rules = Modern();
        CharacterChoices bought = Ana(rules); // five 10s and a 14: 5 * 2 + 7 points
        bought.ScoreMethod = "pointBuy";
        Assert.True(CharacterChoices.PointBuyCost(rules, bought.Scores) == 17 && bought.Check(rules) == "");
        bought.Scores["str"] = 15;
        bought.Scores["dex"] = 15; // 17 - 4 + 18
        Assert.Equal(31, CharacterChoices.PointBuyCost(rules, bought.Scores));
        Assert.Equal("scores: cost 31 points, the budget is 27", bought.Check(rules));
        bought.Scores["dex"] = 16;
        Assert.Equal(-1, CharacterChoices.PointBuyCost(rules, bought.Scores));
        Assert.Equal("scores: point buy only buys scores 8 to 15", bought.Check(rules));

        CharacterChoices arrayed = Ana(rules);
        arrayed.ScoreMethod = "array";
        Assert.Equal("scores: the standard array uses each of its values once", arrayed.Check(rules));
        int[] values = { 8, 15, 13, 14, 10, 12 };
        for (int i = 0; i < rules.Abilities.Count; i++)
        {
            arrayed.Scores[rules.Abilities[i].Id] = values[i];
        }
        Assert.Equal("", arrayed.Check(rules));
    }

    [Fact]
    public void DraftMakesACharacterStepByStep()
    {
        using var scratch = new Scratch();
        Compendium compendium = WithOptions(scratch);
        Ruleset rules = Modern();
        var draft = new CharacterDraft(rules, compendium);
        // a new character starts as the first class by name on the standard array, with a sheet already
        Assert.True(draft.Choices.Levels[0].ClassId == "scout" && draft.Choices.ScoreMethod == "array" && draft.Sheet != null);
        Assert.Equal("Give the character a name.", draft.StepProblem(0));
        draft.SetName("Ada");
        Assert.Equal("Pick a race.", draft.StepProblem(0));
        draft.SetRace("elf");
        Assert.Equal("Pick a background.", draft.StepProblem(0));
        draft.SetBackground("sailor");
        Assert.True(draft.StepDone(0) && draft.StepDone(1));

        // the array swaps two scores; point buy spends from the budget
        string top = draft.Choices.Scores.First(s => s.Value == 15).Key;
        Assert.True(draft.CanLower(top) && !draft.CanRaise(top));
        draft.Lower(top);
        Assert.Equal(14, draft.Choices.Scores[top]);
        draft.SetMethod("pointBuy", new Rng(3));
        Assert.True(draft.Choices.Scores.Values.All(v => v == 8) && draft.PointsLeft() == 27 && !draft.CanLower("str"));
        for (int i = 0; i < 7; i++)
        {
            draft.Raise("str");
        }
        Assert.True(draft.Choices.Scores["str"] == 15 && draft.PointsLeft() == 18 && !draft.CanRaise("str"));
        draft.SetMethod("roll", new Rng(3));
        Assert.True(draft.Choices.ScoreMethod == "roll" && !draft.CanRaise("str") && draft.StepDone(1));
        draft.SetClass("soldier");
        Assert.True(draft.Finished() && draft.Sheet!.ClassName == "Soldier" && draft.Sheet.Ancestry == "Elf");

        // levelling up offers the feats a trial build accepts, and only one per kind
        CharacterChoices made = draft.Choices.Copy();
        made.ScoreMethod = "fixed";
        made.Scores["con"] = 16; // 14 after the elf's -2, enough for tough
        made.Xp = 300;
        compendium.Classes["soldier"] = new ClassDefinition
        {
            Id = "soldier", Name = "Soldier", HitDie = 10, Items = new List<string> { "sword" },
            Levels = ClassLevel.ReadRows(TestContent.Json("""[{}, {"feats":["general","class"],"skills":1}]""")),
        };
        CharacterDraft up = CharacterDraft.LevelUp(rules, compendium, made);
        Assert.True(up.LevellingUp && up.Choices.Level == 2 && up.Choices.Levels[1].ClassId == "soldier");
        Assert.Equal(new[] { "tough" }, up.FeatOptions("general"));
        Assert.Empty(up.FeatOptions("class")); // sure-foot needs the scout and stealth
        Assert.DoesNotContain("athletics", up.SkillOptions); // the sailor already has it
        Assert.Equal("Pick 1 more skill.", up.StepProblem(2));
        up.ToggleSkill("stealth");
        up.ToggleSkill("arcana"); // no room for a second
        Assert.Equal(new[] { "stealth" }, up.Picked("skills"));
        Assert.Equal("Pick a general feat.", up.StepProblem(2));
        up.PickFeat("tough");
        Assert.True(up.Finished() && up.Sheet!.Level == 2 && up.Sheet.Resources["grit"].Max == 1);
        up.SetClass("scout");
        Assert.True(up.Picked("feats").Count == 0 && up.Sheet!.ClassName == "Soldier / Scout");
    }

    [Fact]
    public void LibraryKeepsCharactersAndTheGraveyard()
    {
        using var scratch = new Scratch();
        string folder = Path.Combine(scratch.Folder, "characters");
        Ruleset rules = Modern();
        var entry = new LibraryEntry { Choices = Ana(rules), Coins = 125 };
        entry.Choices.Name = "Ser Ada";
        entry.Inventory.Add(new Item(Classes().Items["sword"]) { Equipped = true });
        CharacterLibrary.Write(folder, entry);
        Assert.Equal("ser-ada.json", entry.FileName);
        var other = new LibraryEntry { Choices = Ana(rules) };
        other.Choices.Name = "Ser Ada";
        CharacterLibrary.Write(folder, other);
        Assert.Equal("ser-ada-2.json", other.FileName);
        Assert.Equal("ana", CharacterLibrary.PlainName("Ana!!"));
        Assert.Equal("ser-ada-2", CharacterLibrary.PlainName("Ser Ada (2)"));

        LibraryEntry back = CharacterLibrary.Read(entry.Path);
        Assert.True(back.Choices.Name == "Ser Ada" && back.Coins == 125 && back.Inventory.Count == 1 && back.Inventory[0].Equipped
            && back.Inventory[0].Definition.Modifiers.Count == 1 && !back.Retired);
        Assert.Equal(Text(entry.Choices), Text(back.Choices));

        // the file is the C++ client's envelope
        JsonNode file = JsonNode.Parse(File.ReadAllText(entry.Path))!;
        Assert.True(file["format"]!.GetValue<string>() == "yorehold.character" && file["version"]!.GetValue<int>() == 1
            && file["data"]!["choices"]!["name"]!.GetValue<string>() == "Ser Ada");

        // a broken file is skipped and named; a missing folder is empty
        File.WriteAllText(Path.Combine(folder, "broken.json"), "{");
        var problems = new List<string>();
        List<LibraryEntry> listed = CharacterLibrary.List(folder, problems);
        Assert.Equal(2, listed.Count);
        Assert.Contains(problems, p => p.Contains("broken.json"));
        Assert.Empty(CharacterLibrary.List(Path.Combine(scratch.Folder, "nobody")));

        // retiring moves the file, which can't be written again; the graveyard lists last
        CharacterLibrary.Retire(folder, back);
        Assert.True(back.Retired && !File.Exists(entry.Path) && File.Exists(back.Path));
        Assert.Throws<InvalidOperationException>(() => CharacterLibrary.Write(folder, back));
        listed = CharacterLibrary.List(folder);
        Assert.True(listed.Count == 2 && !listed[0].Retired && listed[1].Retired);
        Assert.Null(CharacterLibrary.Find(folder, "../ser-ada.json"));
        Assert.Equal("Ser Ada", CharacterLibrary.Find(folder, "ser-ada-2.json")!.Choices.Name);
    }
}
