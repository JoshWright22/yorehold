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
        Assert.Equal("none", fate.World.Rules.Advancement);
        ContentException error = TestContent.Refused(() => RulesTesting.Rules("""{"id": "x", "name": "X", "abilities": [{"id": "str", "name": "S"}], "advancement": "luck"}"""));
        Assert.Equal("advancement", error.Field);
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
