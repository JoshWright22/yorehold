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

        (Ruleset pf2e, Compendium pf2eCompendium) = Load("rulesets/pf2e");
        var hero = new CharacterDraft(pf2e, pf2eCompendium);
        hero.SetName("Ana");
        Assert.Equal("Pick an ancestry.", hero.StepProblem(0));
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
