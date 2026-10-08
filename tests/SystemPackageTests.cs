namespace Yorehold.Rules.Tests;

/// <summary>The rules systems that ship as packages load, build heroes and play a fight.</summary>
public class SystemPackageTests
{
    // A fighter and a wizard against two goblins, under the named system.
    private static WorldFixture Yard(string ruleset, string fighter, string caster, params (string Path, string Json)[] more) =>
        WorldFixture.LoadJson("chapters/sys-yard", new Dictionary<string, string>(more.Select(f => KeyValuePair.Create(f.Path, f.Json)))
    {
        ["chapters/sys-yard/chapter.json"] = $$"""
            {"id":"sys-yard","title":"Yard","map":"map.json","ruleset":"{{ruleset}}",
             "party":[{"name":"Ana","class":"{{fighter}}","at":[2,3]},{"name":"Bo","class":"{{caster}}","at":[2,4]}],
             "encounters":[{"id":"yard","creatures":[{"creature":"goblin","name":"Gik","at":[3,3]},{"creature":"goblin","name":"Rak","at":[5,5]}]}]}
            """,
        ["chapters/sys-yard/map.json"] = """
            {"name":"Yard","tiles":{"floor":{"art":"grass"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
             "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":["########","#......#","#......#","#......#",
             "#......#","#......#","#......#","########"]}]}
            """,
    }, 7);

    [Fact]
    public void Dnd5ePlaysAFight()
    {
        using WorldFixture world = Yard("rulesets/dnd5e", "fighter", "wizard");
        World w = world.World;
        Assert.Equal("dnd5e", w.Rules.Id);
        Assert.Contains(w.ActionsOf(0), a => a.Id == "second-wind");
        Assert.Contains(w.ActionsOf(1), a => a.Id == "fire-bolt");
        Assert.DoesNotContain(w.ActionsOf(0), a => a.Id == "strike");
        PlayOut(world);
        Assert.True(world.Said("Fire Bolt") || world.Said("attacks"), "Heroes attack under the system's own actions");
    }

    [Fact]
    public void SneakAttackIsATrigger()
    {
        using WorldFixture world = Yard("rulesets/dnd5e", "rogue", "wizard");
        World w = world.World;
        CharacterSheet rogue = w.Creatures[0].Sheet;
        rogue.Stats.SetBase("dex", 2000); // acts first, and hits
        world.Fight();
        Assert.True(world.TurnTo(0));
        rogue.AddCondition(w.Rules, "helped");
        Assert.True(world.Use("attack", 2) && world.Said("Ana: Sneak Attack"), "A hit with advantage sets off Sneak Attack");
        int said = world.Log.Count(line => line.Contains("Sneak Attack"));
        rogue.AddCondition(w.Rules, "helped");
        world.Use("attack", 3);
        Assert.Equal(said, world.Log.Count(line => line.Contains("Sneak Attack")));
    }

    [Fact]
    public void CreationStepsAreTheSystems()
    {
        ContentFiles files = TestContent.Shipped();
        (Ruleset Rules, Compendium Compendium) Load(string folder)
        {
            RulesFolder rules = RulesFolder.Load(files, folder);
            var compendium = new Compendium();
            compendium.Load(files, folder, "");
            compendium.LoadOptions(files, folder);
            return (rules.Rules, compendium);
        }
        (Ruleset fate, Compendium fateCompendium) = Load("rulesets/fate-accelerated");
        var draft = new CharacterDraft(fate, fateCompendium);
        Assert.Single(draft.Steps);
        Assert.True(!fate.Creation.AsksClass && pf2eAsks(), "Fate never asks for a class; it builds on its one");
        bool pf2eAsks() => RulesFolder.Load(files, "rulesets/pf2e").Rules.Creation.AsksClass;
        draft.SetName("Zed");
        Assert.True(draft.Finished(), "A Fate character is a name and approaches: " + draft.StepProblem(0) + draft.Problem);

        // Fate's words: high concept, trouble and three aspects, kept on the character and the sheet
        draft.SetField("high-concept", 0, "Wizard detective of the north");
        draft.SetField("aspects", 1, "Owes the queen a favour");
        draft.SetField("aspects", 7, "past the three it has");
        Assert.Equal(new[] { "", "Owes the queen a favour" }, draft.Choices.Fields["aspects"]);
        CharacterSheet zed = CharacterBuild.Build(fate, fateCompendium, draft.Choices)!;
        Assert.Equal((1.0, 1.0, 0.0), (zed.Named(fate, "field.high-concept"), zed.Named(fate, "field.aspects"), zed.Named(fate, "field.trouble")));
        CharacterChoices again = CharacterChoices.Read(TestContent.Json(draft.Choices.ToJson().ToJsonString()));
        Assert.Equal(draft.Choices.Fields["high-concept"], again.Fields["high-concept"]);
        again.Fields["luck"] = new List<string> { "x" };
        Assert.Contains("not a field", again.Check(fate));

        (Ruleset pf2e, Compendium pf2eCompendium) = Load("rulesets/pf2e");
        var hero = new CharacterDraft(pf2e, pf2eCompendium);
        hero.SetName("Ana");
        Assert.Equal("Pick an ancestry.", hero.StepProblem(0));

        // PF2e's attributes start at 10 and four boosts raise four of them by 2
        Assert.Equal("boosts", hero.Choices.ScoreMethod);
        Assert.True(hero.Choices.Scores.Values.All(v => v == 10) && hero.BoostsLeft() == 4);
        foreach (string ability in new[] { "str", "dex", "con", "wis" })
        {
            hero.Raise(ability);
        }
        Assert.True(hero.BoostsLeft() == 0 && !hero.CanRaise("int") && hero.Choices.Scores["str"] == 12 && hero.Choices.Check(pf2e) == "");
        hero.Lower("str");
        Assert.True(hero.BoostsLeft() == 1 && hero.Choices.Scores["str"] == 10);

        // a heritage: the system's own kind of pick, open by ancestry, granting what it says
        hero.SetRace("dwarf");
        Assert.Equal(new[] { "forge-dwarf", "strong-blooded-dwarf" }, hero.OptionIds("heritage"));
        Assert.Equal("Pick a heritage.", hero.StepProblem(0));
        hero.SetOption("heritage", "forge-dwarf");
        CharacterSheet ana = CharacterBuild.Build(pf2e, pf2eCompendium, hero.Choices)!;
        Assert.Equal(2, ana.Stats.Integer("resist.fire"));
        hero.SetRace("elf"); // a forge dwarf heritage doesn't fit an elf
        Assert.False(hero.Choices.Options.ContainsKey("heritage"));
        CharacterChoices wrong = hero.Choices.Copy();
        wrong.Options["heritage"] = "forge-dwarf";
        Assert.Null(CharacterBuild.Build(pf2e, pf2eCompendium, wrong, out string why));
        Assert.Contains("isn't open", why);
    }

    [Fact]
    public void TriggersAnswerBeingHitAKillAndATurnStarting()
    {
        using WorldFixture world = Yard("rulesets/pf2e", "fighter", "wizard",
            ("rulesets/pf2e/triggers/thorns.json", """{"id": "thorns", "name": "Thorns", "on": "hitBy", "effects": [{"do": "damage", "dice": "2"}]}"""),
            ("rulesets/pf2e/triggers/bloodlust.json", """{"id": "bloodlust", "name": "Bloodlust", "on": "kill", "effects": [{"do": "heal", "dice": "1"}]}"""),
            ("rulesets/pf2e/triggers/regrow.json", """{"id": "regrow", "name": "Regrow", "on": "turnStart", "if": "level >= 0", "effects": [{"do": "heal", "dice": "1"}]}"""));
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        CharacterSheet gik = w.Creatures[2].Sheet;
        ana.Stats.SetBase("perception", 2000); // acts first
        gik.Stats.SetBase("ac", -1000); // always hit
        ana.Granted.Add("bloodlust");
        gik.Granted.Add("thorns");
        w.Creatures[3].Sheet.Granted.Add("regrow");
        world.Fight();
        Assert.True(world.TurnTo(0));
        int hp = ana.Hp;
        gik.Hp = 1000;
        Assert.True(world.Use("strike", 2) && world.Said("Gik: Thorns") && ana.Hp < hp, "Being hit burns the attacker");
        Assert.False(world.Said("Ana: Bloodlust"));
        gik.Hp = 1;
        Assert.True(world.Use("strike", 2) && gik.Down && world.Said("Ana: Bloodlust"), "Dropping Gik sets off Ana's kill trigger");
        Assert.Single(world.Log, line => line.Contains("Gik: Thorns")); // down, it burns no one
        Assert.True(world.TurnTo(3) && world.Said("Rak: Regrow"), "Rak's turn starting sets off its own");
    }

    [Fact]
    public void FeatKindsAreTheSystems()
    {
        using var scratch = new Scratch();
        scratch.Write("rulesets/pf2e/feats/toughness.json", """{"id": "toughness", "name": "Toughness", "kind": "general"}""");
        scratch.Write("rulesets/pf2e/feats/natural-ambition.json", """{"id": "natural-ambition", "name": "Natural Ambition", "kind": "ancestry"}""");
        ContentFiles files = TestContent.ShippedWith(scratch);
        void Check()
        {
            RulesFolder rules = RulesFolder.Load(files, "rulesets/pf2e");
            var compendium = new Compendium();
            compendium.Load(files, "rulesets/pf2e", "");
            compendium.LoadOptions(files, "rulesets/pf2e");
            rules.Check(compendium, "", files);
        }
        Check();
        Assert.Equal("Ancestry feat", RulesFolder.Load(files, "rulesets/pf2e").Rules.FeatKindName("ancestry"));

        // the game's own "race" kind is not one of Pathfinder's
        scratch.Write("rulesets/pf2e/feats/old.json", """{"id": "old", "name": "Old", "kind": "race"}""");
        ContentException error = TestContent.Refused(Check);
        Assert.Contains("unknown feat kind \"race\"; the ruleset's are ancestry, class, skill, general", error.Message);
    }

    [Fact]
    public void ANightsRestHealsWounds()
    {
        using WorldFixture world = Yard("rulesets/pf2e", "fighter", "wizard");
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        // down, the card reads the system's dying track: the value it dies at comes from its formula
        ana.Death.Wounded = 1;
        ana.TakeDamage(ana.Hp + 1, w.Rules);
        Assert.Equal("dying 2/4", ana.DownedText(w.Rules).Short);
        Assert.Contains("wounded 1", ana.DownedText(w.Rules).Line);
        ana.Heal(ana.MaxHp);
        Assert.Equal("", ana.DownedText(w.Rules).Short);
        ana.Death.Wounded = 2;
        Assert.True(w.Rest("refocus") && ana.Death.Wounded == 2, "A short rest leaves the wounds: " + w.Refusal);
        // the night's rest is taken at camp, which plays the adventure's system
        Assert.True(w.MakeCamp() && w.Rules.Id == "pf2e", w.Refusal);
        ana = w.Creatures[0].Sheet;
        ana.Inventory.Add(new Item(w.Chapter.Compendium.Item("supplies")!, 4));
        Assert.True(w.Rest("rest") && ana.Death.Wounded == 0 && world.Said("Ana is no longer wounded."), w.Refusal);
        Assert.False(ana.HealWounds(w.Rules, "rest"));

        ContentException error = TestContent.Refused(() => RulesTesting.Rules(
            """{"id": "x", "name": "X", "abilities": [{"id": "str", "name": "S"}], "death": {"enabled": true, "track": {"woundedClearedBy": ["nap"]}}}"""));
        Assert.Contains("unknown rest \"nap\"", error.Message);
    }

    [Fact]
    public void AReactionCanTurnAHitIntoAMiss()
    {
        // a Shield: as a hit lands, the defence goes up and the same roll is read again
        using WorldFixture world = Yard("rulesets/pf2e", "fighter", "wizard",
            ("rulesets/pf2e/actions/ward.json", """
                {"id": "ward", "name": "Ward", "cost": 1, "general": false, "target": {"kind": "self"},
                 "effects": [{"do": "modifier", "stat": "ac", "value": 2000, "duration": 1}]}
                """),
            ("rulesets/pf2e/reactions/ward.json", """{"id": "ward", "name": "Ward", "trigger": "beforeHit", "action": "ward", "general": false}"""));
        World w = world.World;
        w.Creatures[0].Sheet.Stats.SetBase("perception", 2000); // acts first
        CharacterSheet gik = w.Creatures[2].Sheet;
        gik.Stats.SetBase("ac", -1000); // every roll would hit
        gik.Granted.Add("ward");
        world.Fight();
        int hp = gik.Hp;
        Assert.True(world.TurnTo(0) && world.Use("strike", 2), "Ana strikes");
        Assert.True(world.Said("Gik takes Ward") && gik.Hp == hp, "Gik's ward turns the hit aside:\n" + string.Join("\n", world.Log.TakeLast(6)));
        // the reaction is spent: the next strike lands against the warded AC, so it still misses, and no second ward
        world.Use("strike", 2);
        Assert.Single(world.Log, line => line.Contains("Gik takes Ward"));
    }

    [Fact]
    public void TheSheetShowsWhatTheSystemLists()
    {
        using WorldFixture pf2e = Yard("rulesets/pf2e", "fighter", "wizard");
        World w = pf2e.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        Assert.Equal(new[] { "Fortitude", "Reflex", "Will" }, SheetLayout.Saves(w.Rules, ana).Select(s => s.Name));
        ana.Stats.SetBase("resist.fire", 5);
        ana.Stats.SetBase("immune.poison", 1);
        Assert.Equal(new[] { "immune to poison", "resists fire 5" }, SheetLayout.Defences(ana));

        using WorldFixture fate = Yard("rulesets/fate-accelerated", "character", "character");
        SheetLayout layout = fate.World.Rules.Sheet;
        Assert.True(!layout.Shows("level") && layout.Shows("tracks") && layout.NameOf("hp") == "Shifts left" && layout.NameOf("xp") == "XP");
        ContentException error = TestContent.Refused(() => SheetLayout.Read(TestContent.Json("""{"sections": ["vitals", "luck"]}""")));
        Assert.Contains("unknown section \"luck\"", error.Message);
    }

    [Fact]
    public void FateHarmIsStressThenConsequences()
    {
        using WorldFixture world = Yard("rulesets/fate-accelerated", "character", "character");
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        string Tracks() => string.Join(" ", ana.Tracks.Select(t => t.Value));
        // 3 stress, then mild (2), moderate (4) and severe (6) consequences: 15 shifts in all
        Assert.Equal(("3 1 1 1", 15, 15), (Tracks(), ana.Hp, ana.MaxHp));
        ana.TakeDamage(2);
        Assert.Equal(("1 1 1 1", 13), (Tracks(), ana.Hp));
        ana.TakeDamage(3); // the last stress box and the mild consequence
        Assert.Equal(("0 0 1 1", 10), (Tracks(), ana.Hp));
        ana.Heal(5); // healing clears stress, never a consequence
        Assert.Equal("3 0 1 1", Tracks());
        ana.TakeDamage(13); // stress, moderate, severe: all taken, still standing
        Assert.True(Tracks() == "0 0 0 0" && !ana.Down, Tracks());
        Assert.True(ana.TakeDamage(1) && ana.Down, "A shift no track takes takes Ana out");

        // stress clears once the conflict ends, consequences between scenes; down stays down
        CharacterSheet bo = w.Creatures[1].Sheet;
        bo.TakeDamage(5);
        bo.ClearTracks(TrackDefinition.FightEnd);
        Assert.Equal(("3 0 1 1", 13), (string.Join(" ", bo.Tracks.Select(t => t.Value)), bo.Hp));
        bo.ClearTracks("scene");
        Assert.Equal(15, bo.Hp);
        // a step's "resource" names a track too: spending the severe slot, then restoring it
        Assert.True(bo.AdjustTrack("severe", -1) && bo.Hp == 9 && bo.AdjustTrack("severe", 1) && bo.Hp == 15);
        Assert.False(bo.AdjustTrack("luck", 1));

        // a mook has its own stress and no consequences; the sheet saves its tracks
        CharacterSheet gik = w.Creatures[2].Sheet;
        Assert.Equal(("2 0 0 0", 2), (string.Join(" ", gik.Tracks.Select(t => t.Value)), gik.MaxHp));
        bo.TakeDamage(4);
        CharacterSheet loaded = CharacterSheet.Read(TestContent.Json(bo.ToJson().ToJsonString()));
        Assert.Equal(string.Join(" ", bo.Tracks.Select(t => t.Value)), string.Join(" ", loaded.Tracks.Select(t => t.Value)));
    }

    [Fact]
    public void AttacksAreRolledAgainstTheSystemsDefence()
    {
        // Fate: no armour class; an attack meets Defend, the better of Quick and Careful
        using WorldFixture fate = Yard("rulesets/fate-accelerated", "character", "character");
        World w = fate.World;
        CharacterSheet gik = w.Creatures[2].Sheet;
        int quick = gik.AbilityModifier(w.Rules, "quick");
        int careful = gik.AbilityModifier(w.Rules, "careful");
        Assert.Equal(Math.Max(quick, careful), gik.AttackDefence(w.Rules));
        gik.Stats.SetBase("careful", 40);
        Assert.Equal(gik.AbilityModifier(w.Rules, "careful"), gik.AttackDefence(w.Rules));
        Assert.Equal("Defend", w.Rules.DefenceName("defend"));

        // an attack step may name another defence; one the system lacks is refused
        Ruleset rules = RulesTesting.Rules("""
            {"id": "x", "name": "X", "abilities": [{"id": "dex", "name": "Dex"}],
             "defences": [{"id": "reflex", "name": "Reflex", "value": "10 + mod.dex"}]}
            """);
        RulesTesting.Effect("""[{"do": "roll", "kind": "attack", "against": "reflex", "steps": [{"do": "damage", "dice": "1d6"}]}]""").Check(rules, "x.json");
        ContentException error = TestContent.Refused(() =>
            RulesTesting.Effect("""[{"do": "roll", "kind": "attack", "against": "will", "steps": [{"do": "damage", "dice": "1d6"}]}]""").Check(rules, "x.json"));
        Assert.Contains("unknown defence \"will\"", error.Message);
        var nimble = new CharacterSheet();
        nimble.Stats.SetBase("dex", 14);
        Assert.Equal(10 + nimble.AbilityModifier(rules, "dex"), nimble.Defence(rules, "reflex"));
    }

    [Fact]
    public void AMilestoneRaisesEveryHeroALevel()
    {
        using WorldFixture world = Yard("rulesets/pf2e", "fighter", "wizard");
        World w = world.World;
        int hp = w.Creatures[0].Sheet.MaxHp;
        w.Milestone();
        Assert.True(w.Creatures[0].Sheet.Level == 2 && w.Creatures[1].Sheet.Level == 2 && w.Creatures[0].Sheet.MaxHp > hp, "Both heroes reach level 2");
        Assert.True(world.Said("Ana reaches level 2"));

        Assert.Equal("xp", w.Rules.Advancement);
        using WorldFixture fate = Yard("rulesets/fate-accelerated", "character", "character");
        Assert.Equal("milestone", fate.World.Rules.Advancement);
        // a Fate milestone raises one approach by 1, picked like any level's boosts
        CharacterSheet ana = fate.World.Creatures[0].Sheet;
        fate.World.Milestone();
        Assert.Equal(2, ana.Level);
        ContentException error = TestContent.Refused(() => RulesTesting.Rules("""{"id": "x", "name": "X", "abilities": [{"id": "str", "name": "S"}], "advancement": "luck"}"""));
        Assert.Equal("advancement", error.Field);
    }

    [Fact]
    public void TheDarkGivesConditionsForTheRoll()
    {
        using WorldFixture world = Yard("rulesets/dnd5e", "fighter", "wizard");
        World w = world.World;
        w.Options.Lighting = 1 + (int)LightingMode.Rules;
        w.Options.TimeOfDay = 1 + (int)MapTime.Night;
        w.Creatures[2].Sheet.Stats.SetBase("darkvision", 60);
        w.Creatures[0].Sheet.Stats.SetBase("darkvision", 0);
        // far apart: the light the party carries doesn't reach Gik
        world.Put(0, new Cell(1, 1));
        world.Put(1, new Cell(1, 2));
        world.Put(2, new Cell(6, 6));
        world.Put(3, new Cell(6, 1));
        world.Fight();
        Assert.Equal(LightLevel.Dark, w.LightAt(w.Tokens.Tokens[2].Position));
        // Ana can't see Gik; Gik sees Ana by darkvision, and she can't see him
        Assert.Equal(new[] { "unseen-target" }, w.PlaceConditions(0, 2));
        Assert.Equal(new[] { "unseen-attacker" }, w.PlaceConditions(2, 0));
        Assert.Equal(Advantage.Advantage, w.Creatures[2].Sheet.AttackAdvantage(w.Rules, w.Creatures[0].Sheet, w.PlaceConditions(2, 0)));
        // in daylight, nothing
        w.Options.TimeOfDay = 1 + (int)MapTime.Day;
        Assert.Empty(w.PlaceConditions(0, 2));
    }

    [Fact]
    public void AReactionCanCounterASpell()
    {
        // Gik answers a spell being cast: a hex that loses the caster the spell, slot and all
        using WorldFixture world = Yard("rulesets/pf2e", "fighter", "wizard",
            ("rulesets/pf2e/conditions/hexed.json", """{"id": "hexed", "name": "Hexed", "flags": ["spellLost"], "ends": ["turnEnd"]}"""),
            ("rulesets/pf2e/actions/hex.json", """
                {"id": "hex", "name": "Hex", "cost": 1, "general": false, "target": {"kind": "creature", "side": "enemy", "range": 12},
                 "effects": [{"do": "condition", "id": "hexed"}]}
                """),
            ("rulesets/pf2e/reactions/hex.json", """{"id": "hex", "name": "Hex", "trigger": "spellCast", "action": "hex", "general": false}"""));
        World w = world.World;
        w.Creatures[1].Sheet.Stats.SetBase("perception", 2000); // Bo acts first
        CharacterSheet gik = w.Creatures[2].Sheet;
        gik.Granted.Add("hex");
        world.Fight();
        int hp = gik.Hp;
        Assert.True(world.TurnTo(1) && world.Use("electric-arc", 2), "Bo casts: " + w.Refusal);
        Assert.True(world.Said("Gik takes Hex") && world.Said("Electric Arc is lost") && gik.Hp == hp,
            "The hex comes first and the spell is lost:\n" + string.Join("\n", world.Log.TakeLast(6)));
    }

    [Fact]
    public void FireballAndCounterspellIn5e()
    {
        using WorldFixture world = Yard("rulesets/dnd5e", "fighter", "wizard");
        World w = world.World;
        CharacterSheet bo = w.Creatures[1].Sheet, gik = w.Creatures[2].Sheet, rak = w.Creatures[3].Sheet;
        bo.Stats.SetBase("dex", 2000); // Bo acts first
        bo.Spells.Add("fireball");
        bo.Resources["slots-3"] = new Resource(2, 2);
        world.Put(2, new Cell(5, 4));
        world.Fight();
        Assert.True(world.TurnTo(1));
        (int gikHp, int rakHp) = (gik.Hp, rak.Hp);
        // the two goblins stand together, away from the party: one burst reaches both
        Assert.True(world.Use("fireball", null, new Cell(5, 5)), w.Refusal);
        Assert.True(gik.Hp < gikHp && rak.Hp < rakHp, "8d6, half on a save, on both");
        Assert.Equal(1, bo.Resources["slots-3"].Current);
    }

    [Fact]
    public void ThePf2eAiStridesMoreThanOnce()
    {
        // Rak moves one square a Stride and starts far off: with three actions it strides on
        // (Demoralize only from beside them here, so it can't spend its actions on that instead)
        using WorldFixture world = Yard("rulesets/pf2e", "fighter", "wizard",
            ("rulesets/pf2e/actions/demoralize.json", """
                {"id": "demoralize", "name": "Demoralize", "cost": 1, "target": {"kind": "creature", "side": "enemy", "range": 1},
                 "effects": [{"do": "condition", "id": "frightened", "value": 1}]}
                """));
        World w = world.World;
        CharacterSheet rak = w.Creatures[3].Sheet;
        rak.Stats.SetBase("speed", 5);
        rak.Stats.SetBase("perception", 2000); // acts first
        world.Put(2, new Cell(6, 1));
        world.Put(3, new Cell(6, 6));
        world.Fight();
        Assert.True(world.TurnTo(3), w.Refusal);
        Cell start = w.CellOf(3);
        Assert.True(world.StepUntil(() => w.CurrentCreature != 3, 20));
        Assert.True(Math.Abs(start.X - w.CellOf(3).X) + Math.Abs(start.Y - w.CellOf(3).Y) >= 2,
            "It strode more than once:\n" + string.Join("\n", world.Log.TakeLast(6)));
    }

    [Fact]
    public void AWolfsBiteKnocksDown()
    {
        // the bestiary's on-hit riders: a failed Strength save leaves the bitten prone
        using WorldFixture world = Yard("rulesets/dnd5e", "fighter", "wizard");
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet, gik = w.Creatures[2].Sheet;
        gik.Granted.Add("knock-down");
        gik.Stats.SetBase("dex", 2000); // acts first, and hits
        ana.Stats.SetBase("str", -2000); // the save fails
        world.Fight();
        Assert.True(world.TurnTo(2) && world.Use("attack", 0), w.Refusal);
        Assert.True(ana.HasCondition("prone"), string.Join("\n", world.Log.TakeLast(6)));
    }

    [Fact]
    public void ACounterspellLosesTheSpell()
    {
        using WorldFixture world = Yard("rulesets/dnd5e", "fighter", "wizard");
        World w = world.World;
        CharacterSheet bo = w.Creatures[1].Sheet, gik = w.Creatures[2].Sheet;
        bo.Stats.SetBase("dex", 2000);
        bo.Stats.SetBase("con", -2000); // the save fails
        gik.Spells.Add("counterspell");
        gik.Resources["slots-3"] = new Resource(1, 1);
        world.Fight();
        int hp = gik.Hp;
        Assert.True(world.TurnTo(1) && world.Use("fire-bolt", 2), "Bo casts: " + w.Refusal);
        Assert.True(world.Said("Gik takes Counterspell") && world.Said("Fire Bolt is lost") && gik.Hp == hp && gik.Resources["slots-3"].Current == 0,
            string.Join("\n", world.Log.TakeLast(6)));
    }

    [Fact]
    public void DisengagingKeepsOpportunityAttacksOff()
    {
        bool Provokes(bool disengage)
        {
            using WorldFixture world = Yard("rulesets/dnd5e", "fighter", "wizard");
            World w = world.World;
            w.Creatures[0].Sheet.Stats.SetBase("dex", 2000); // Ana acts first, beside Gik
            world.Fight();
            Assert.True(world.TurnTo(0) && w.Adjacent(0, 2));
            if (disengage)
            {
                Assert.True(world.Use("disengage"), w.Refusal);
            }
            Assert.True(world.MoveTo(new Cell(1, 6)), w.Refusal);
            world.StepUntil(() => !world.Walking, 10);
            return world.Said("Gik takes Opportunity Attack");
        }
        Assert.True(Provokes(false), "Walking out of reach provokes");
        Assert.False(Provokes(true), "Disengaged, it doesn't");
    }

    [Fact]
    public void TheAiWeighsAGuardByTheSystemsOdds()
    {
        using WorldFixture world = Yard("rulesets/pf2e", "fighter", "wizard");
        World w = world.World;
        w.Creatures[0].Sheet.Stats.SetBase("perception", 2000); // Ana acts first, Gik beside her
        world.Fight();
        Assert.True(world.TurnTo(0));
        ActionDefinition shield = w.FindAction("raise-a-shield")!;
        float worth = w.GuardWorth(0, shield);
        Assert.True(worth > 0, "A shield's +2 AC saves some of what Gik would deal");
        Assert.Equal(0, w.GuardWorth(0, w.FindAction("stride")!));
        // a third Strike at -10 is worth less than the first; the guard is worth the same
        w.Creatures[2].Sheet.Hp = 1000; // Gik outlasts two Strikes
        float first = w.StrikeWorth(0);
        Assert.True(world.Use("strike", 2) && world.Use("strike", 2));
        Assert.True(w.StrikeWorth(0) < first, "The attack penalty lowers a third Strike's worth");
        Assert.True(world.Use("raise-a-shield"), w.Refusal);
        Assert.Equal(0, w.GuardWorth(0, shield)); // raised already
    }

    [Fact]
    public void ConditionsCanHinderChecks()
    {
        using WorldFixture world = Yard("rulesets/dnd5e", "fighter", "wizard");
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        Assert.Equal(Advantage.None, ana.CheckAdvantage(w.Rules));
        ana.AddCondition(w.Rules, "poisoned");
        Assert.Equal(Advantage.Disadvantage, ana.CheckAdvantage(w.Rules));
        // a check asked for plainly is rolled with the condition's disadvantage: two d20s, the lower kept
        RollResult roll = ana.RollCheck(w.Rules, "athletics", Advantage.None, new Rng(5));
        Assert.Equal(2, roll.Dice.Count);
    }

    [Fact]
    public void TheAiTakesItsBestAttack()
    {
        // the captain's Multiattack is two swings for one action: worth about twice the Attack
        using WorldFixture world = WorldFixture.LoadJson("chapters/cap-yard", new Dictionary<string, string>
        {
            ["chapters/cap-yard/chapter.json"] = """
                {"id":"cap-yard","title":"Yard","map":"map.json","ruleset":"rulesets/dnd5e",
                 "party":[{"name":"Ana","class":"fighter","at":[2,3]}],
                 "encounters":[{"id":"yard","creatures":[{"creature":"bandit-captain","name":"Vex","at":[3,3]}]}]}
                """,
            ["chapters/cap-yard/map.json"] = """
                {"name":"Yard","tiles":{"floor":{"art":"grass"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
                 "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":["######","#....#","#....#","#....#","#....#","######"]}]}
                """,
        }, 7);
        World w = world.World;
        world.Fight();
        ActionDefinition multi = w.FindAction("multiattack")!;
        ActionDefinition attack = w.FindAction(w.StrikeAction)!;
        float twice = w.AttackWorth(1, multi, 0);
        float once = w.AttackWorth(1, attack, 0);
        Assert.True(once > 0 && Math.Abs(twice - 2 * once) < 0.01f, $"{twice} vs {once}");
        Assert.True(world.TurnTo(1));
        Assert.Equal("multiattack", w.BestAttack(1, 0)?.Id);
        w.Options.AutoPlay = true;
        Assert.True(world.StepUntil(() => world.Said("Vex attacks Ana") || !w.Fighting, 120));
    }

    [Fact]
    public void ProneAndParalysedCareHowFarTheAttackerIs()
    {
        using WorldFixture world = Yard("rulesets/dnd5e", "fighter", "wizard");
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        CharacterSheet gik = w.Creatures[2].Sheet;
        gik.AddCondition(w.Rules, "prone");
        Assert.Equal(Advantage.Advantage, ana.AttackAdvantage(w.Rules, gik, null, 1));
        Assert.Equal(Advantage.Disadvantage, ana.AttackAdvantage(w.Rules, gik, null, 4));

        // a hit from beside a paralysed creature is a critical hit
        gik.RemoveCondition("prone");
        gik.AddCondition(w.Rules, "paralyzed");
        gik.Stats.SetBase("ac", -1000);
        ana.Stats.SetBase("dex", 2000);
        world.Fight();
        Assert.True(world.TurnTo(0) && w.Adjacent(0, 2));
        Assert.True(world.Use("attack", 2) && world.Said("CRITICAL"), string.Join("\n", world.Log.TakeLast(4)));
    }

    [Fact]
    public void AWizardCastsShieldAsAReaction()
    {
        // a Shield strong enough that any hit it is cast against turns into a miss
        using WorldFixture world = Yard("rulesets/dnd5e", "fighter", "wizard",
            ("rulesets/dnd5e/conditions/shielded.json", """{"id": "shielded", "name": "Shielded", "modifiers": [{"stat": "ac", "op": "add", "value": 5000}], "ends": ["turnStart"]}"""));
        World w = world.World;
        CharacterSheet bo = w.Creatures[1].Sheet;
        if (!bo.Spells.Contains("shield"))
        {
            bo.Spells.Add("shield");
        }
        bo.Stats.SetBase("ac", -1000); // every attack would hit
        w.Creatures[2].Sheet.Stats.SetBase("dex", 2000); // Gik acts first
        world.Put(2, new Cell(3, 4));
        world.Fight();
        int slots = bo.Resources.Where(r => r.Key.StartsWith("slots-", StringComparison.Ordinal)).Sum(r => r.Value.Current);
        Assert.True(world.TurnTo(2) && world.Use("attack", 1), w.Refusal);
        Assert.True(world.Said("Bo takes Shield") && bo.HasCondition("shielded"), string.Join("\n", world.Log.TakeLast(6)));
        Assert.Equal(slots - 1, bo.Resources.Where(r => r.Key.StartsWith("slots-", StringComparison.Ordinal)).Sum(r => r.Value.Current));
    }

    [Fact]
    public void AHeldReactionIsNotTaken()
    {
        using WorldFixture world = Yard("rulesets/dnd5e", "fighter", "wizard",
            ("rulesets/dnd5e/conditions/shielded.json", """{"id": "shielded", "name": "Shielded", "modifiers": [{"stat": "ac", "op": "add", "value": 5000}], "ends": ["turnStart"]}"""));
        World w = world.World;
        CharacterSheet bo = w.Creatures[1].Sheet;
        if (!bo.Spells.Contains("shield"))
        {
            bo.Spells.Add("shield");
        }
        bo.Stats.SetBase("ac", -1000);
        w.Creatures[2].Sheet.Stats.SetBase("dex", 2000);
        world.Put(2, new Cell(3, 4));
        world.Fight();
        Assert.Contains(w.ReactionsOf(1), r => r.Id == "shield");
        Assert.DoesNotContain(w.ReactionsOf(0), r => r.Id == "shield");
        Assert.False(w.HoldReaction(0, "shield", true));
        Assert.True(w.HoldReaction(1, "shield", true), w.Refusal);
        Assert.True(world.TurnTo(2) && world.Use("attack", 1), w.Refusal);
        Assert.False(world.Said("Bo takes Shield"));
        Assert.True(w.HoldReaction(1, "shield", false));
        Assert.Empty(w.Creatures[1].HeldReactions);
    }

    [Fact]
    public void AShieldThatCantTurnTheHitIsKept()
    {
        // the real Shield (+5) against a hit that beats AC by a thousand: not worth the slot
        using WorldFixture world = Yard("rulesets/dnd5e", "fighter", "wizard");
        World w = world.World;
        CharacterSheet bo = w.Creatures[1].Sheet;
        if (!bo.Spells.Contains("shield"))
        {
            bo.Spells.Add("shield");
        }
        bo.Stats.SetBase("ac", -1000);
        w.Creatures[2].Sheet.Stats.SetBase("dex", 2000);
        world.Put(2, new Cell(3, 4));
        world.Fight();
        Assert.True(world.TurnTo(2) && world.Use("attack", 1), w.Refusal);
        Assert.False(world.Said("Bo takes Shield"));
    }

    [Fact]
    public void AVersatileWeaponHitsHarderInBothHands()
    {
        using WorldFixture world = Yard("rulesets/dnd5e", "fighter", "wizard");
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        ana.Inventory.Clear();
        ana.Inventory.Add(new Item(w.Chapter.Compendium.Item("longsword")!, 1));
        ana.Equip(0);
        Assert.StartsWith("1d10", ana.DamageDice(w.Rules));
        ana.Inventory.Add(new Item(w.Chapter.Compendium.Item("shield")!, 1));
        ana.Equip(1);
        Assert.StartsWith("1d8", ana.DamageDice(w.Rules));
    }

    [Fact]
    public void WeaponTraitsAndTheHiddenFlatCheck()
    {
        using WorldFixture world = Yard("rulesets/pf2e", "fighter", "wizard");
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        CharacterSheet gik = w.Creatures[2].Sheet;
        ana.Inventory.Clear();
        ana.Inventory.Add(new Item(w.Chapter.Compendium.Item("rapier")!, 1));
        ana.Equip(0);
        ana.Stats.SetBase("perception", 2000);
        gik.Stats.SetBase("ac", -1000); // every Strike is a critical hit
        gik.Hp = 1000;
        world.Fight();
        Assert.True(world.TurnTo(0) && world.Use("strike", 2) && world.Said("Ana: Deadly"), "A deadly weapon's critical adds its die, granted to nobody");

        // the hidden: an attack has to pass a DC 11 flat check before it can land
        gik.AddCondition(w.Rules, "hidden");
        Assert.True(world.Use("strike", 2) && world.Said("flat check, DC 11"), string.Join("\n", world.Log.TakeLast(4)));
    }

    [Fact]
    public void AContentSetOnlyAddsItsTypeForItsSystem()
    {
        using var scratch = new Scratch();
        scratch.Write("content.json", """{"format": "yorehold.content", "version": 1, "name": "More foes", "kind": "creatures", "ruleset": "pf2e@1.0"}""");
        scratch.Write("creatures/cave-rat.json", """
            {"id": "cave-rat", "name": "Cave rat", "hp": 6, "armorClass": 14, "speed": 25, "level": -1,
             "abilities": {"str": 8, "dex": 14, "con": 12, "int": 2, "wis": 12, "cha": 4}, "items": [],
             "token": {"color": [120, 110, 100], "size": 0.3}}
            """);
        ContentFiles Files()
        {
            ContentFiles files = TestContent.Shipped();
            files.Add(scratch.Folder);
            return files;
        }
        ContentPackage set = ContentPackage.Load(new ContentFiles(scratch.Folder));
        Assert.True(set.IsSet);
        Assert.Empty(set.CheckSet(scratch.Folder, Files()));

        // nothing but its own type: a ruleset file in it is refused
        scratch.Write("ruleset.json", "{}");
        Assert.Contains(set.CheckSet(scratch.Folder, Files()), p => p.StartsWith("ruleset.json: a set of creatures holds only"));
        File.Delete(Path.Combine(scratch.Folder, "ruleset.json"));

        // it names its system, and its entries load under it
        scratch.Write("content.json", """{"format": "yorehold.content", "version": 1, "kind": "creatures"}""");
        Assert.Contains(ContentPackage.Load(new ContentFiles(scratch.Folder)).CheckSet(scratch.Folder, Files()), p => p.Contains("names the system"));
        scratch.Write("content.json", """{"format": "yorehold.content", "version": 1, "kind": "creatures", "ruleset": "pf2e"}""");
        scratch.Write("creatures/cave-rat.json", """{"id": "cave-rat", "name": "Cave rat", "speed": "fast"}""");
        Assert.NotEmpty(ContentPackage.Load(new ContentFiles(scratch.Folder)).CheckSet(scratch.Folder, Files()));
    }

    [Fact]
    public void ASetJoinsOnlyItsSystemsGames()
    {
        using var feats = new Scratch();
        feats.Write("content.json", """{"format": "yorehold.content", "version": 1, "id": "more-feats", "kind": "feats", "ruleset": "pf2e"}""");
        feats.Write("feats/toughness.json", """{"id": "toughness", "name": "Toughness", "kind": "general", "modifiers": [{"stat": "maxHp", "op": "add", "value": 3}]}""");
        using var other = new Scratch();
        other.Write("content.json", """{"format": "yorehold.content", "version": 1, "kind": "feats", "ruleset": "dnd5e"}""");
        var left = new List<string>();
        Assert.Equal(new[] { feats.Folder }, ContentSets.For(new[] { feats.Folder, other.Folder }, "pf2e", null, left));
        Assert.Contains(left, why => why.Contains("for dnd5e, not pf2e"));
        Assert.Empty(ContentSets.For(new[] { feats.Folder }, "pf2e", new HashSet<string> { "more-feats" }, left));

        // laid over the game's content, its feat joins the system's options
        ContentFiles files = TestContent.Shipped();
        files.Add(feats.Folder);
        var compendium = new Compendium();
        compendium.LoadOptions(files, "rulesets/pf2e");
        compendium.LoadOptions(files, "");
        Assert.True(compendium.Feats.ContainsKey("toughness"));
    }

    [Fact]
    public void Level4RaisesTwoAbilitiesIn5e()
    {
        ContentFiles files = TestContent.Shipped();
        Ruleset rules = RulesFolder.Load(files, "rulesets/dnd5e").Rules;
        var compendium = new Compendium();
        compendium.Load(files, "rulesets/dnd5e", "");
        compendium.LoadOptions(files, "rulesets/dnd5e");
        var draft = new CharacterDraft(rules, compendium);
        draft.SetName("Ana");
        CharacterChoices choices = draft.Choices;
        for (int level = 2; level <= 4; level++)
        {
            draft = CharacterDraft.LevelUp(rules, compendium, choices);
            if (level < 4)
            {
                Assert.Equal(0, draft.LevelBoosts().Count);
                choices = draft.Choices;
            }
        }
        Assert.Equal((2, 1), draft.LevelBoosts());
        // gained in play with no picks, the level still owes them, and the same screen fills them in
        Assert.True(CharacterDraft.OwesPicks(rules, compendium, draft.Choices));
        Assert.Equal(4, CharacterDraft.FillLevel(rules, compendium, draft.Choices).Choices.Level);
        Assert.StartsWith("Raise 2 more", draft.StepProblem(draft.Step));
        int str = CharacterBuild.Build(rules, compendium, draft.Choices)!.AbilityScore("str");
        draft.ToggleBoost("str");
        draft.ToggleBoost("con");
        draft.ToggleBoost("dex"); // a third is one too many: it takes back any on dex, none here
        Assert.Equal(new[] { "str", "con" }, draft.Choices.Levels[^1].Picked("boosts"));
        Assert.Equal(str + 1, CharacterBuild.Build(rules, compendium, draft.Choices)!.AbilityScore("str"));
        // or +2 to one: the same ability twice
        draft.ToggleBoost("con");
        draft.ToggleBoost("str");
        Assert.Equal(new[] { "str", "str" }, draft.Choices.Levels[^1].Picked("boosts"));
        Assert.Equal(str + 2, CharacterBuild.Build(rules, compendium, draft.Choices)!.AbilityScore("str"));
    }

    [Fact]
    public void Level5BoostsFourAttributesInPf2e()
    {
        ContentFiles files = TestContent.Shipped();
        Ruleset rules = RulesFolder.Load(files, "rulesets/pf2e").Rules;
        var compendium = new Compendium();
        compendium.Load(files, "rulesets/pf2e", "");
        compendium.LoadOptions(files, "rulesets/pf2e");
        var draft = new CharacterDraft(rules, compendium);
        draft.SetName("Ana");
        CharacterChoices choices = draft.Choices;
        for (int level = 2; level <= 5; level++)
        {
            draft = CharacterDraft.LevelUp(rules, compendium, choices);
            choices = draft.Choices;
        }
        Assert.Equal((4, 2), draft.LevelBoosts());
        string[] four = rules.Abilities.Take(4).Select(a => a.Id).ToArray();
        int before = CharacterBuild.Build(rules, compendium, draft.Choices, out string problem)?.AbilityScore(four[0]) ?? throw new Xunit.Sdk.XunitException(problem);
        foreach (string ability in four)
        {
            draft.ToggleBoost(ability);
        }
        // each a different one: pressing the first again takes it back
        Assert.Equal(four, draft.Choices.Levels[^1].Picked("boosts"));
        Assert.Equal(before + 2, CharacterBuild.Build(rules, compendium, draft.Choices)!.AbilityScore(four[0]));
        draft.ToggleBoost(four[0]);
        Assert.Equal(3, draft.Choices.Levels[^1].Picked("boosts").Count);
    }

    [Fact]
    public void AHerosAttackReactionIsAskedFor()
    {
        // with prompts on, Ana's riposte to a miss waits for her player's answer
        using WorldFixture world = Yard("rulesets/pf2e", "fighter", "wizard",
            ("rulesets/pf2e/reactions/riposte.json", """{"id": "riposte", "name": "Riposte", "trigger": "missed", "action": "strike", "general": false}"""));
        World w = world.World;
        w.Options.ReactionPrompts = true;
        CharacterSheet ana = w.Creatures[0].Sheet;
        ana.Granted.Add("riposte");
        ana.Stats.SetBase("ac", 1000); // Gik can't hit her
        w.Creatures[2].Sheet.Stats.SetBase("perception", 2000); // Gik acts first
        world.Fight();
        Assert.True(world.TurnTo(2) && world.Use("strike", 0));
        Assert.True(w.ReactionPrompt is { Name: "Riposte" } && !world.Said("Ana takes Riposte"), "Asked, not taken");
        Assert.True(world.React(true) && world.Said("Ana takes Riposte") && w.ReactionPrompt == null);
    }

    [Fact]
    public void TheAiWeighsAConditionByTheOdds()
    {
        using WorldFixture world = Yard("rulesets/pf2e", "fighter", "wizard");
        World w = world.World;
        world.Fight();
        // frightened on Gik: worse at hitting Ana, easier for her to hit
        float scared = w.ConditionWorth(0, 2, "frightened");
        Assert.True(scared > 0, $"{scared}");
        // the same on Ana herself is bad for her side
        Assert.True(w.ConditionWorth(0, 0, "frightened") < 0);
        // stunned stops Gik's turn: worth at least what he would deal her
        Assert.True(w.ConditionWorth(0, 2, "stunned") >= scared);
    }

    [Fact]
    public void ALevelCanOfferAnArchetype()
    {
        Ruleset rules = RulesTesting.Rules("""
            {"id": "x", "name": "X", "abilities": [{"id": "str", "name": "Str"}, {"id": "con", "name": "Con"}],
             "optionKinds": [{"id": "path", "name": "Path"}], "scoreMethods": {"standardArray": [15, 14]}}
            """);
        var compendium = new Compendium();
        compendium.Classes["warrior"] = ClassDefinition.Read(TestContent.Json("""
            {"id": "warrior", "name": "Warrior", "hitDie": 10, "levels": [{}, {"options": ["path"]}]}
            """));
        compendium.Options["brute"] = OptionDefinition.Read(TestContent.Json("""
            {"id": "brute", "name": "Brute", "kind": "path", "classes": ["warrior"], "modifiers": [{"stat": "damage", "op": "add", "value": 2}]}
            """));
        compendium.Options["duelist"] = OptionDefinition.Read(TestContent.Json("""{"id": "duelist", "name": "Duelist", "kind": "path"}"""));
        var draft = new CharacterDraft(rules, compendium);
        draft.SetName("Ana");
        draft = CharacterDraft.LevelUp(rules, compendium, draft.Choices);
        Assert.Equal(new[] { "path" }, draft.LevelOptionKinds());
        Assert.Equal(new[] { "brute", "duelist" }, draft.LevelOptionIds("path"));
        Assert.Equal("Pick a path.", draft.StepProblem(draft.Step));
        draft.PickLevelOption("brute");
        draft.PickLevelOption("duelist"); // one per kind: the new one replaces it
        draft.PickLevelOption("brute");
        Assert.Equal(new[] { "brute" }, draft.Choices.Levels[^1].Picked("options"));
        CharacterSheet? built = CharacterBuild.Build(rules, compendium, draft.Choices, out string why);
        Assert.True(built != null, why + " / " + draft.Problem);
        CharacterSheet ana = built!;
        Assert.Equal(2, ana.Stats.Integer("damage"));
    }

    [Fact]
    public void DamageCanAimAtOneTrack()
    {
        // physical and mental stress apart, consequences shared, as Fate Core keeps them
        Ruleset rules = RulesTesting.Rules("""
            {"id": "x", "name": "X", "abilities": [{"id": "will", "name": "Will"}],
             "tracks": [{"id": "physical", "name": "Physical stress", "max": "2"}, {"id": "mental", "name": "Mental stress", "max": "2"},
                        {"id": "mild", "name": "Mild consequence", "max": "1", "absorbs": 2, "heals": false, "shared": true}]}
            """);
        var sheet = new CharacterSheet { Name = "Ana" };
        sheet.UseTracks(rules);
        sheet.TakeDamage(2, rules, false, "mental");
        Assert.Equal("2 0 1", string.Join(" ", sheet.Tracks.Select(t => t.Value)));
        sheet.TakeDamage(2, rules, false, "mental"); // no mental stress left: the shared consequence takes it
        Assert.Equal("2 0 0", string.Join(" ", sheet.Tracks.Select(t => t.Value)));
        Assert.False(sheet.Down);
        Assert.True(sheet.TakeDamage(1, rules, false, "mental") && sheet.Down, "Physical stress doesn't take mental harm");

        RulesTesting.Effect("""[{"do": "damage", "dice": "2", "track": "mental"}]""").Check(rules, "x.json");
        ContentException error = TestContent.Refused(() => RulesTesting.Effect("""[{"do": "damage", "dice": "2", "track": "spirit"}]""").Check(rules, "x.json"));
        Assert.Contains("unknown track \"spirit\"", error.Message);
    }

    [Fact]
    public void AMissCanSetOffAReaction()
    {
        // a riposte: when an attack on it misses, strike back
        using WorldFixture world = Yard("rulesets/pf2e", "fighter", "wizard",
            ("rulesets/pf2e/reactions/riposte.json", """{"id": "riposte", "name": "Riposte", "trigger": "missed", "action": "strike"}"""));
        World w = world.World;
        w.Creatures[0].Sheet.Stats.SetBase("perception", 2000);
        w.Creatures[2].Sheet.Stats.SetBase("ac", 1000); // Ana can't hit Gik
        world.Fight();
        Assert.True(world.TurnTo(0) && world.Use("strike", 2));
        Assert.True(world.Said("Gik takes Riposte") && world.Said("Gik attacks Ana"), "The miss sets off Gik's riposte");
    }

    [Fact]
    public void SuddenChargeMovesThenStrikes()
    {
        using WorldFixture world = Yard("rulesets/pf2e", "fighter", "wizard");
        World w = world.World;
        w.Creatures[0].Sheet.Stats.SetBase("perception", 2000); // acts first
        world.Fight();
        Assert.True(world.TurnTo(0));
        Assert.False(w.Adjacent(0, 3));
        Assert.True(world.Use("sudden-charge", 3) && w.Adjacent(0, 3) && world.Said("Ana attacks Rak"), "It closes in, then Strikes");
    }

    [Fact]
    public void Pathfinder2ePlaysAFight()
    {
        using WorldFixture world = Yard("rulesets/pf2e", "fighter", "wizard");
        World w = world.World;
        Assert.True(w.Rules.Id == "pf2e" && w.Rules.ActionsPerTurn == 3 && !w.Rules.FreeMove);
        Assert.Contains(w.ActionsOf(1), a => a.Id == "electric-arc");
        Assert.Equal(4, w.Rules.Checks.Kind(CheckRules.Attack).Outcomes.Count);
        PlayOut(world);
    }

    [Fact]
    public void FateAcceleratedPlaysAFight()
    {
        // no classes, no d20: approaches, 4dF against the ladder and shifts as stress
        using WorldFixture world = Yard("rulesets/fate-accelerated", "character", "character");
        World w = world.World;
        Assert.True(w.Rules.Id == "fate-accelerated" && w.Creatures[0].Sheet.MaxHp == 15);
        Assert.Contains(w.ActionsOf(0), a => a.Id == "create-advantage");
        PlayOut(world);
        Assert.True(world.Said("4dF") && world.Said("defends"), "Rolls are Fate dice, and defenders roll too");
    }

    // Heroes attack the nearest goblin in reach or fire at any, else end the turn; enemies play themselves.
    private static void PlayOut(WorldFixture world)
    {
        World w = world.World;
        world.Fight();
        for (int turn = 0; turn < 200 && w.Fighting; turn++)
        {
            if (w.CurrentCreature is int me && me < w.HeroCount)
            {
                List<int> foes = Enumerable.Range(w.HeroCount, w.Creatures.Count - w.HeroCount).Where(f => !w.Creatures[f].Sheet.Down).ToList();
                string? used = w.ActionsOf(me).Select(a => a.Id).FirstOrDefault(id => w.CanUse(me, id) && foes.Any(f => w.ValidTargets(id).Contains(f)));
                if (used == null || !world.Use(used, foes.First(f => w.ValidTargets(used).Contains(f))))
                {
                    world.Use(w.EndTurnAction);
                }
                continue;
            }
            world.StepUntil(() => !w.Fighting || w.CurrentCreature is int c && c < w.HeroCount, 30);
        }
        Assert.False(w.Fighting, "The fight ends: " + string.Join(" | ", world.Log.TakeLast(8)));
    }
}
