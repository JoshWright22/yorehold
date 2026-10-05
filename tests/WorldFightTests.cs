namespace Yorehold.Rules.Tests;

/// <summary>
/// Fights played through the World: the shipped actions, reactions, shared turns, flanking and
/// cover, dying, and whole fights to their end. The C++ client's WorldActionTests, WorldTurnTests,
/// WorldPositioningTests, WorldDeathTests and the fights in WorldTests.
/// </summary>
public class WorldFightTests
{
    private const string OpenRows = """["########","#......#","#......#","#......#","#......#","#......#","#......#","########"]""";

    private static string Map(string rows = OpenRows)
    {
        return """{"name":"Yard","tiles":{"floor":{"art":"grass"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},"""
            + """ "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":""" + rows + "}]}";
    }

    // A walled yard with Ana the fighter, Bo the cleric and the goblins given, everyone with 1000 HP.
    private static WorldFixture Yard(string party, string goblins, Dictionary<string, string>? extra = null, string rows = OpenRows,
        string chapter = "")
    {
        var files = new Dictionary<string, string>
        {
            ["chapters/yard/chapter.json"] = """{"id":"yard","title":"Yard","map":"map.json","party":""" + party
                + ""","encounters":[{"id":"yard","creatures":""" + goblins + "}]" + (chapter.Length > 0 ? "," + chapter : "") + "}",
            ["chapters/yard/map.json"] = Map(rows),
        };
        foreach (KeyValuePair<string, string> file in extra ?? new Dictionary<string, string>())
        {
            files[file.Key] = file.Value;
        }
        WorldFixture world = WorldFixture.LoadJson("chapters/yard", files, 5);
        foreach (WorldCreature c in world.World.Creatures)
        {
            c.Sheet.Stats.SetBase("maxHp", 1000);
            c.Sheet.Hp = 1000;
        }
        return world;
    }

    // Ana next to Gik with Gok further off, Ana strong, quick and sharp-eyed so her rolls pass and she goes first.
    private static WorldFixture ActionYard(Dictionary<string, string>? extra = null)
    {
        WorldFixture world = Yard(
            """[{"name":"Ana","class":"fighter","at":[3,3]},{"name":"Bo","class":"cleric","at":[3,4]}]""",
            """[{"creature":"goblin","name":"Gik","at":[4,3]},{"creature":"goblin","name":"Gok","at":[6,3]}]""", extra);
        CharacterSheet ana = world.World.Creatures[0].Sheet;
        ana.Stats.SetBase("dex", 2000);
        ana.Stats.SetBase("str", 2000);
        ana.Stats.SetBase("wis", 2000);
        world.Fight();
        Assert.True(world.World.Fighting && world.World.CurrentCreature == 0, "The yard starts with Ana's turn");
        return world;
    }

    // Round the table once, back to Ana.
    private static bool NextAna(WorldFixture world)
    {
        for (int i = 0; i < 8; i++)
        {
            if (!world.Use(World.EndTurnAction))
            {
                return false;
            }
            if (world.World.CurrentCreature == 0)
            {
                return true;
            }
        }
        return false;
    }

    [Fact]
    public void DefendingAndHelping()
    {
        using WorldFixture world = ActionYard();
        World w = world.World;
        Ruleset rules = w.Rules;
        CharacterSheet ana = w.Creatures[0].Sheet;
        CharacterSheet bo = w.Creatures[1].Sheet;
        Assert.Equal(11, w.ActionsOf(0).Count);
        Assert.Equal(World.StrikeAction, w.ActionsOf(0)[0].Id);
        int ac = ana.ArmorClass(rules);
        Assert.True(world.Use("defend") && ana.ArmorClass(rules) == ac + 2 && w.ActionsLeft == 1, "Defend costs one action and adds two AC");
        Assert.True(world.Use("defend") && ana.ArmorClass(rules) == ac + 2, "Repeated Defend refreshes rather than stacking");
        Assert.False(world.Use("defend"), "No actions left");
        Assert.Contains("not enough actions", w.Refusal);
        Assert.True(NextAna(world) && ana.ArmorClass(rules) == ac, "Defend ends when the defender's next turn starts");
        bo.Hp = 50;
        Assert.False(world.Use("help", 2), "Help refuses an enemy");
        Assert.True(world.Use("help", 1) && bo.Hp == 50 && bo.AttackAdvantage(rules) == Advantage.Advantage, "Help aids a standing ally without healing it");
        bo.ConditionEvent(rules, "attack");
        Assert.Equal(Advantage.None, bo.AttackAdvantage(rules));
        bo.TakeDamage(50, rules);
        Assert.True(bo.Down);
        Assert.True(world.Use("help", 1) && bo.Hp == 1 && !bo.HasCondition("downed") && w.Tokens.Tokens[1].Floor == 0,
            "Help gets a downed ally up with one HP in the same fight");
        Assert.True(NextAna(world), "The party can keep taking turns after Help");
        bo.AddCondition(rules, "dead");
        Assert.False(world.Use("help", 1), "Help cannot revive the dead");
    }

    [Fact]
    public void HidingAndSeeking()
    {
        using WorldFixture world = ActionYard();
        World w = world.World;
        Ruleset rules = w.Rules;
        CharacterSheet ana = w.Creatures[0].Sheet;
        Assert.True(world.Use("hide") && ana.HasCondition("hidden") && ana.AttackAdvantage(rules) == Advantage.Advantage,
            "Passing every enemy's perception hides the creature");
        Assert.True(world.MoveTo(new Cell(2, 3)) && !ana.HasCondition("hidden"), "Moving in combat ends Hide");
        world.Put(0, new Cell(2, 3));
        w.Creatures[2].Sheet.AddCondition(rules, "hidden");
        Assert.True(world.Use("seek", 2) && !w.Creatures[2].Sheet.HasCondition("hidden"), "Seek reveals an enemy when Perception beats passive Stealth");
        Assert.True(NextAna(world));
        w.Creatures[3].Sheet.Stats.SetBase("wis", 4000);
        Assert.True(world.Use("hide") && !ana.HasCondition("hidden"), "One watcher spotting the creature defeats Hide");
        w.Creatures[2].Sheet.Stats.SetBase("dex", 4000);
        w.Creatures[2].Sheet.AddCondition(rules, "hidden");
        Assert.True(world.Use("seek", 2) && w.Creatures[2].Sheet.HasCondition("hidden"), "A failed Seek leaves the enemy hidden");
    }

    [Fact]
    public void PositioningAndReadying()
    {
        using WorldFixture world = ActionYard();
        World w = world.World;
        Ruleset rules = w.Rules;
        CharacterSheet ana = w.Creatures[0].Sheet;
        Assert.True(world.Use("shove", 2) && w.CellOf(2) == new Cell(5, 3), "Shove pushes an adjacent enemy one square on success");
        Assert.False(world.Use("grapple", 2), "Grapple refuses a target out of reach");
        Assert.Contains("can't reach Gik", w.Refusal);
        world.Put(2, new Cell(4, 3));
        Assert.True(world.Use("grapple", 2) && w.Creatures[2].Sheet.HasFlag(rules, "cantMove"), "Grapple holds an adjacent enemy in place");
        Assert.True(NextAna(world));
        world.Put(3, new Cell(5, 3));
        Assert.True(world.Use("shove", 2) && w.CellOf(2) == new Cell(4, 3), "Shove stops before an occupied square");
        ana.AddCondition(rules, "prone");
        Assert.True(world.Use("interact") && !ana.HasCondition("prone"), "Interact stands up from prone");
        Assert.True(NextAna(world));
        Assert.False(world.Use("interact"), "Stand up is unavailable when already standing");
        Assert.DoesNotContain(w.UsableActions(), a => a.Id == "interact");
        ana.AddCondition(rules, "grabbed");
        Assert.False(world.MoveTo(new Cell(2, 3)), "Being grabbed prevents movement at once, free movement included");
        ana.RemoveCondition("grabbed");
        Assert.True(world.Use("ready") && w.CurrentCreature != 0 && w.Creatures[0].ReadiedAction == "strike",
            "Ready records a Strike, spends both actions and ends the turn");
        Assert.True(world.TurnTo(0) && w.Creatures[0].ReadiedAction.Length == 0, "An unused Ready expires at the creature's next turn");
    }

    [Fact]
    public void MovementReactions()
    {
        using WorldFixture world = ActionYard();
        World w = world.World;
        Assert.Equal(2, w.Chapter.Rules.Reactions.Count);
        Assert.True(world.MoveTo(new Cell(2, 3)) && !world.HasReaction(2) && world.HasReaction(3) && w.ActionsLeft == 2,
            "Leaving reach with free movement spends the foe's reaction, not a turn action");
        world.FinishWalk(0);
        Assert.True(world.MoveTo(new Cell(3, 3)), "Returning toward the foe is allowed");
        world.FinishWalk(0);
        Assert.True(world.MoveTo(new Cell(2, 3)), "The hero can leave again after the foe spent its reaction");
        Assert.Equal(1, world.Log.Count(line => line == "Gik takes Opportunity Strike."));
        world.FinishWalk(0);
        Assert.True(world.TurnTo(2) && world.HasReaction(2), "The reaction comes back at the start of the creature's turn");

        using WorldFixture forced = ActionYard();
        Assert.True(forced.Use("shove", 2) && forced.HasReaction(0) && forced.HasReaction(2), "Being shoved sets off no reactions");

        using WorldFixture ready = ActionYard();
        ready.World.Creatures[0].Sheet.Stats.SetBase("str", 20);
        ready.Put(2, new Cell(5, 3));
        Assert.True(ready.Use("ready") && ready.TurnTo(2), "Ready waits through the other turns");
        Assert.True(ready.MoveTo(new Cell(4, 3)) && !ready.HasReaction(0) && ready.World.Creatures[0].ReadiedAction.Length == 0
            && ready.Said("Ana takes Readied Strike."), "An enemy entering reach sets off the readied action once");

        using WorldFixture lethal = ActionYard(new Dictionary<string, string>
        {
            ["rulesets/yorehold/actions/strike.json"] =
                """{"id":"strike","order":10,"target":{"kind":"creature","side":"enemy","range":1},"effects":[{"do":"damage","dice":2000}]}""",
        });
        World l = lethal.World;
        Assert.True(lethal.MoveTo(new Cell(2, 3)) && l.Creatures[0].Sheet.Down && l.Tokens.Tokens[0].Path.Count == 0
            && l.CellOf(0) == new Cell(3, 3) && l.CurrentCreature != 0, "A lethal opportunity stops the move before it leaves and the turn passes");
    }

    [Fact]
    public void ReactionPrompts()
    {
        using WorldFixture world = ActionYard();
        World w = world.World;
        w.Options.ReactionPrompts = true;
        w.Creatures[0].Sheet.Stats.SetBase("str", 20);
        world.Put(1, new Cell(1, 6));
        Assert.True(world.TurnTo(2) && world.MoveTo(new Cell(5, 3)) && w.ReactionPrompt != null && w.ReactionPrompt.Creature == 0,
            "An enemy leaving the hero's reach asks before the reaction is taken");
        ulong offer = w.ReactionPrompt!.Id;
        Assert.False(world.Use(World.EndTurnAction), "Turns and other actions wait for the answer");
        Assert.True(world.React(false) && w.ReactionPrompt == null && world.HasReaction(0) && w.CellOf(2) == new Cell(5, 3),
            "Passing lets the move go on and keeps the reaction");
        world.FinishWalk(2);
        world.Put(2, new Cell(4, 3));
        Assert.True(world.MoveTo(new Cell(5, 3)) && w.ReactionPrompt != null && w.ReactionPrompt.Id != offer, "A new trigger offers the kept reaction again");
        world.Step(2.1);
        Assert.True(w.ReactionPrompt == null && !world.HasReaction(0) && world.Said("Ana takes Opportunity Strike."),
            "With no answer the short prompt takes the reaction");
        Assert.False(world.React(true), "Nothing left to answer");

        using WorldFixture both = ActionYard();
        World b = both.World;
        b.Options.ReactionPrompts = true;
        Assert.True(both.TurnTo(2) && both.MoveTo(new Cell(5, 3)) && b.ReactionPrompt != null, "A move crossing two allies' reach offers the first reaction");
        ulong first = b.ReactionPrompt!.Id;
        both.React(false);
        Assert.True(b.ReactionPrompt != null && b.ReactionPrompt.Creature == 1 && b.ReactionPrompt.Id != first,
            "Passing on one ally's reaction offers the next ally's on the same step");
        both.React(false);
        Assert.True(b.ReactionPrompt == null && both.HasReaction(0) && both.HasReaction(1) && b.CellOf(2) == new Cell(5, 3),
            "Passing on both lets the move finish with both reactions kept");
    }

    [Fact]
    public void SharedTurns()
    {
        using WorldFixture world = Yard(
            """[{"name":"Ana","class":"fighter","at":[2,2]},{"name":"Bo","class":"cleric","at":[3,2]}]""",
            """[{"creature":"goblin","name":"Gik","at":[5,5]},{"creature":"goblin","name":"Gok","at":[6,5]}]""");
        World w = world.World;
        for (int i = 0; i < w.Creatures.Count; i++)
        {
            w.Creatures[i].Sheet.Stats.SetBase("dex", 2000.0f - i * 200.0f);
        }
        world.Fight();
        Assert.True(w.Rules.SharedTurns && w.CurrentCreature == 0 && w.CanChooseTurn(1) && !w.CanChooseTurn(2) && !w.CanChooseTurn(999),
            "Only the allies in the block whose turn it is can be picked");
        Assert.True(world.Use("defend") && world.MoveTo(new Cell(2, 3)));
        Assert.True(!w.CanChooseTurn(1) && !world.ChooseTurn(1), "A walk finishes before the turn goes to someone else");
        world.Put(0, new Cell(2, 3));
        TurnBudget ana = w.BudgetOf(0)!.Copy();
        Assert.True(world.ChooseTurn(1) && world.Use("defend") && world.ChooseTurn(0), "Allies can take their actions in any order");
        Assert.True(w.BudgetOf(0)!.Actions == ana.Actions && w.BudgetOf(0)!.MovementLeft == ana.MovementLeft
            && w.Creatures[0].Sheet.HasCondition("shielded") && w.BudgetOf(1)!.Actions == 1, "Switching keeps both budgets and what began with the turn");
        Assert.True(world.Use(World.EndTurnAction) && w.CurrentCreature == 1 && !w.CanChooseTurn(0),
            "Ending one member's turn leaves the other, and can't be undone by switching");
        Assert.True(world.Use(World.EndTurnAction) && w.CurrentCreature == 2 && !w.CanChooseTurn(1), "When every ally is done the next side's block starts");
        Assert.True(world.ChooseTurn(3) && world.Use(World.EndTurnAction) && w.CurrentCreature == 2, "Enemies share their block the same way");
        Assert.True(world.Use(World.EndTurnAction) && w.Encounter!.Round == 2 && w.CanChooseTurn(1) && w.BudgetOf(0)!.Actions == 2
            && !w.Creatures[0].Sheet.HasCondition("shielded"), "The next round refreshes the whole block once");
        Assert.True(world.Use("ready") && w.Creatures[0].ReadiedAction == "strike" && w.CurrentCreature == 1 && world.ChooseTurn(1)
            && w.Creatures[0].ReadiedAction == "strike", "Ready outlasts another ally's turn in the same block");
        Assert.Equal(new[] { "Ana", "Bo", "Gik", "Gok" }, w.Encounter!.Order.Select(c => c.Sheet.Name));
        Assert.True(world.EventsOf(WorldEventKind.Turn).Count >= 6 && world.EventsOf(WorldEventKind.Fight).Count == 1);
    }

    private static WorldFixture PositionYard(bool wall = false, string positioning = "")
    {
        var files = new Dictionary<string, string>
        {
            ["rulesets/yorehold/actions/shoot.json"] =
                """{"id":"shoot","cost":0,"target":{"kind":"creature","side":"enemy","range":6},"effects":[{"do":"roll","kind":"attack","steps":[]}]}""",
            ["rulesets/yorehold/actions/position-check.json"] =
                """{"id":"position-check","cost":0,"target":{"kind":"creature","side":"enemy","range":6},"effects":[{"do":"damage","dice":1,"ifFlag":"offGuard"}]}""",
        };
        if (positioning.Length > 0)
        {
            files["rulesets/yorehold/positioning.json"] = positioning;
        }
        string rows = wall ? OpenRows.Replace("\"#......#\",\"#......#\",\"#......#\",\"#......#\",", "\"#......#\",\"#..#...#\",\"#......#\",\"#......#\",") : OpenRows;
        WorldFixture world = Yard(
            """[{"name":"Ana","class":"fighter","at":[2,3]},{"name":"Bo","class":"cleric","at":[4,4]}]""",
            """[{"creature":"goblin","name":"Gik","at":[3,3]},{"creature":"goblin","name":"Gok","at":[6,5]}]""", files, rows);
        world.World.Creatures[0].Sheet.Stats.SetBase("dex", 2000);
        world.Put(1, new Cell(4, 3));
        world.Fight();
        return world;
    }

    [Fact]
    public void FlankingAndCover()
    {
        using WorldFixture world = PositionYard();
        World w = world.World;
        Ruleset rules = w.Rules;
        CharacterSheet gik = w.Creatures[2].Sheet;
        int ac = gik.ArmorClass(rules);
        Assert.True(w.IsFlanked(2) && w.PositionalArmorClass(2) == ac - 2 && w.AttackArmorClass(0, 2, false) == ac - 2,
            "Foes on opposite sides apply the Off-guard file's AC");
        float flanked = w.HitChance(0, 2);
        Assert.True(world.Use("strike", 2) && world.Said($"(AC {ac - 2})"), "A Strike rolls against the positional AC");
        int hp = gik.Hp;
        Assert.True(world.Use("position-check", 2) && gik.Hp == hp - 1, "Flanking also gives Off-guard's flag to effects that ask for it");
        gik.AddCondition(rules, "off-guard");
        Assert.Equal(ac - 2, w.PositionalArmorClass(2));
        gik.RemoveCondition("off-guard");
        world.Put(1, new Cell(4, 4));
        int before = gik.Hp;
        Assert.True(!w.IsFlanked(2) && w.PositionalArmorClass(2) == ac && !gik.HasCondition("off-guard")
            && world.Use("position-check", 2) && gik.Hp == before, "Stepping out of the opposite square ends it with no condition left behind");
        Assert.True(w.HitChance(0, 2) < flanked, "The hit chance follows the armour class");
        world.Put(1, new Cell(4, 3));
        w.Creatures[1].Sheet.Hp = 0;
        Assert.False(w.IsFlanked(2), "A downed ally cannot flank");
        w.Creatures[1].Sheet.Hp = 1000;
        world.Put(2, new Cell(5, 3));
        Assert.True(w.CoverFrom(0, 2) == Cover.Half && w.AttackArmorClass(0, 2, true) == ac + 2 && w.AttackArmorClass(0, 2, false) == ac,
            "Someone standing in the way is half cover against ranged attacks, not melee");
        Assert.True(w.HitChance(0, 2, "shoot") < w.HitChance(0, 2));
        Assert.True(world.Use("shoot", 2) && world.Said($"(AC {ac + 2})"), "An attack effect includes the half-cover bonus");
        w.Creatures[1].Sheet.Hp = 0;
        Assert.Equal(Cover.None, w.CoverFrom(0, 2));

        using WorldFixture terrain = PositionYard(wall: true);
        World t = terrain.World;
        terrain.Put(2, new Cell(5, 2));
        Assert.Equal(Cover.Half, t.CoverFrom(0, 2));
        terrain.Put(2, new Cell(4, 2));
        int targetAc = t.Creatures[2].Sheet.ArmorClass(t.Rules);
        Assert.True(t.CoverFrom(0, 2) == Cover.ThreeQuarters && t.AttackArmorClass(0, 2, true) == targetAc + 4
            && terrain.Use("shoot", 2) && terrain.Said($"(AC {targetAc + 4})"), "Three hidden corners add four AC while one clear line still allows the shot");
        Assert.Contains(2, t.ValidTargets("shoot"));
        terrain.Put(0, new Cell(2, 2));
        Assert.True(t.CoverFrom(0, 2) == Cover.Full && !terrain.Use("shoot", 2), "Full cover refuses the ranged action");
        Assert.DoesNotContain(2, t.ValidTargets("shoot"));

        using WorldFixture disabled = PositionYard(positioning: """{"enabled":false}""");
        Assert.True(!disabled.World.IsFlanked(2)
            && disabled.World.PositionalArmorClass(2) == disabled.World.Creatures[2].Sheet.ArmorClass(disabled.World.Rules), "A ruleset can turn positioning off");
        ContentException refused = TestContent.Refused(() => PositionYard(positioning: """{"flankingCondition":"missing"}""").Dispose());
        Assert.Contains("positioning.json", refused.Message);
        Assert.Contains("missing", refused.Message);
    }

    private static WorldFixture DeathYard(string chapter = "")
    {
        WorldFixture world = Yard(
            """[{"name":"Ana","class":"fighter","at":[3,3]},{"name":"Bo","class":"cleric","at":[3,4]}]""",
            """[{"creature":"goblin","name":"Gik","at":[4,3]}]""",
            new Dictionary<string, string>
            {
                ["chapters/yard/wipe.json"] = """{"steps":[{"title":"Return","seconds":1}]}""",
                ["rulesets/yorehold/actions/finish-test.json"] = """{"id":"finish-test","cost":0,"effects":[{"do":"damage","dice":10000,"target":"enemies"}]}""",
                ["rulesets/yorehold/actions/wipe-test.json"] = """{"id":"wipe-test","cost":0,"effects":[{"do":"damage","dice":10000,"target":"allies"}]}""",
            }, chapter: chapter);
        World w = world.World;
        for (int i = 0; i < w.Creatures.Count; i++)
        {
            w.Creatures[i].Sheet.Stats.SetBase("dex", i == 0 ? 2000.0f : i == 1 ? 1800.0f : 1000.0f);
        }
        world.Fight();
        Assert.Equal(0, w.CurrentCreature);
        return world;
    }

    [Fact]
    public void DyingAndRecovering()
    {
        using WorldFixture world = DeathYard();
        World w = world.World;
        Ruleset rules = w.Rules;
        CharacterSheet ana = w.Creatures[0].Sheet;
        CharacterSheet bo = w.Creatures[1].Sheet;
        bo.TakeDamage(1000, rules);
        Assert.True(bo.HasCondition("dying") && world.Use("help", 1) && bo.Hp == 1 && !bo.HasCondition("dying") && !bo.HasCondition("downed"),
            "Help gets a dying ally up and takes the fallen conditions off");
        bo.TakeDamage(1, rules);
        bo.Death.Successes = 3;
        bo.Death.Stable = true;
        bo.SyncDeath(rules);
        ana.Resources["potions"] = new Resource(1, 1);
        Assert.Contains(w.ActionsOf(0), a => a.Id == "potion");
        Assert.True(world.Use("potion", 1) && bo.Hp >= 4 && bo.Hp <= 10 && !bo.Death.Stable && bo.Death.Successes == 0
            && ana.Resources["potions"].Current == 0, "A potion heals a stable ally and spends one carried use");
        Assert.True(!w.FindAction("potion")!.Meets(ana, rules, out string reason) && reason.Contains("potions"), "No potions left stops another use");
        bo.TakeDamage(1000, rules);
        int logAt = world.Log.Count;
        for (int i = 0; i < 4 && world.Log.Count < logAt + 4; i++)
        {
            world.Use(World.EndTurnAction);
        }
        Assert.True(world.Said("Bo death save") && (bo.Hp > 0 || bo.Death.Successes + bo.Death.Failures > 0),
            "A downed hero still rolls a death save when its block comes round");
        bo.Hp = 0;
        bo.Death.Clear();
        bo.TakeDamage(1, rules, true);
        bo.TakeDamage(1, rules);
        Assert.True(bo.Death.Dead && !w.ValidTarget(0, w.FindAction("help")!, 1) && !w.ValidTarget(0, w.FindAction("potion")!, 1),
            "Help and potions refuse a dead ally");
        bo.Heal(1000);
        Assert.Equal(0, bo.Hp);
        Assert.True(world.TurnTo(0) && world.Use("finish-test") && world.Said("Victory") && bo.Death.Dead && bo.Hp == 0,
            "The healing after a win leaves a dead ally dead");
        Assert.True(!w.Fighting && w.FightGroup == null && !w.PartyWiped && w.CurrentCreature == null);
        Assert.Contains("victory", world.EventsOf(WorldEventKind.FightOver));
        Assert.True(w.Creatures[2].Sheet.HasCondition("dead") && w.Tokens.Tokens[2].Floor == World.DeadFloor, "The goblin lies dead where it fell");
        Assert.True(w.Flags.Count == 0 || w.ChapterCleared());
        Assert.False(world.Use(World.EndTurnAction), "Back to exploring: no turns to end");
        Assert.True(world.Go(0, new Cell(1, 1)), "And the party walks freely again");
    }

    [Fact]
    public void ADownedHeroGetsUpAfterAWin()
    {
        using WorldFixture world = DeathYard();
        World w = world.World;
        CharacterSheet bo = w.Creatures[1].Sheet;
        bo.TakeDamage(1000, w.Rules);
        int xp = bo.Xp;
        Assert.True(world.Use("finish-test") && !bo.Down && bo.Hp == w.Rules.ReviveAfterVictory && !bo.HasCondition("downed") && !bo.HasCondition("dying")
            && w.Tokens.Tokens[1].Floor == 0, "The ruleset gets the downed up after a win");
        Assert.True(bo.Xp > xp || w.Chapter.XpPerVictory == 0);
    }

    [Fact]
    public void AWipedPartyLoses()
    {
        using WorldFixture world = DeathYard();
        World w = world.World;
        Assert.True(world.Use("wipe-test") && w.PartyWiped && w.PartyDown && !w.Fighting && !w.InCutscene);
        Assert.Contains("defeat", world.EventsOf(WorldEventKind.FightOver));
        Assert.True(world.Said(w.Chapter.DefeatText));
        Assert.False(world.Use(World.EndTurnAction));
        Assert.False(world.Go(0, new Cell(1, 1)), "Nobody is left to walk");

        using WorldFixture scripted = DeathYard("""
            "onWipe":{"cutscene":"wipe.json","destination":[[1,1],[1,2]]}
            """);
        Assert.True(scripted.Use("wipe-test") && scripted.World.PartyWiped && scripted.World.InCutscene, "An onWipe cutscene starts");
        Assert.Contains(scripted.EventsOf(WorldEventKind.Cutscene), path => path.EndsWith("wipe.json", StringComparison.Ordinal));
    }

    [Fact]
    public void OutsideAFightActionsAreRefused()
    {
        using WorldFixture world = WorldFixture.Load("chapters/goblin-keep", 2);
        World w = world.World;
        Assert.False(world.Use(World.StrikeAction, w.HeroCount), "An attack while exploring is refused");
        Assert.Equal("Not in a fight.", w.Refusal);
        Assert.False(world.Use(World.EndTurnAction));
        Assert.False(world.MoveTo(new Cell(1, 1)));
        Assert.True(w.CurrentCreature == null && w.ActionsLeft == 0 && w.MovementLeft == 0 && w.UsableActions().Count == 0
            && w.ReachableCells().Count == 0 && w.ValidTargets(World.StrikeAction).Count == 0);
        Assert.True(w.FindAction(World.StrikeAction) != null && w.FindAction(World.StrideAction) != null && w.FindAction(World.EndTurnAction) != null
            && w.ActionsOf(0).Count >= 3 && w.ActionsOf(0)[0].Id == World.StrikeAction, "The ruleset has Strike, Dash and End turn, Strike first on the bar");
    }

    [Fact]
    public void WhatAScreenAsksBeforeActing()
    {
        using WorldFixture world = ActionYard();
        World w = world.World;
        Assert.True(w.ActionsLeft == 2 && w.MovementLeft == w.Creatures[0].Sheet.SpeedSquares(w.Rules));
        Assert.Contains(w.UsableActions(), a => a.Id == "strike");
        Assert.Equal(new[] { 2 }, w.ValidTargets("strike"));
        Assert.Equal(new[] { 2, 3 }, w.ValidTargets("seek"));
        Assert.Equal(new[] { 0, 1 }, w.ValidTargets("help"));
        IReadOnlyDictionary<Cell, float> reach = w.ReachableCells();
        Assert.True(reach.ContainsKey(new Cell(2, 3)) && !reach.ContainsKey(new Cell(4, 3)) && !reach.ContainsKey(new Cell(0, 0)),
            "Reach stops at walls and at squares someone stands on");
        Assert.InRange(w.HitChance(0, 2), 0.05f, 1.0f);
        int left = w.MovementLeft;
        Assert.True(world.MoveTo(new Cell(1, 3)) && w.MovementLeft == left - 2);
        world.FinishWalk(0);
        Assert.True(world.Use("stride") && w.MovementLeft == left - 2 + w.Creatures[0].Sheet.SpeedSquares(w.Rules) && w.ActionsLeft == 1, "Dash buys the speed again");

        // Attack walks up first and swings on arrival.
        world.Put(3, new Cell(6, 6));
        Assert.True(w.Attack(3) && w.ActionsLeft == 1);
        Assert.True(world.StepUntil(() => world.Said("Ana attacks Gok"), 10), "The swing lands once the walk has");
        Assert.True(w.Adjacent(0, 3) && w.ActionsLeft == 0);
        Assert.False(w.Attack(3));
        Assert.Contains("can't attack", w.Refusal);
    }

    // One decision for the party, the way a plain player would make it: walk up to the nearest
    // enemy and hit it until it drops.
    private static void PlayParty(WorldFixture world)
    {
        World w = world.World;
        if (w.CurrentCreature is not int me || w.Creatures[me].Team != 0 || world.Walking)
        {
            return;
        }
        int? enemy = null;
        float nearest = 0;
        for (int i = w.HeroCount; i < w.Creatures.Count; i++)
        {
            if (w.Creatures[i].Team != 1 || w.Creatures[i].Sheet.Down || w.OrderIndex(i) == null)
            {
                continue;
            }
            float d = w.Grid.Distance(w.CellOf(me), w.CellOf(i));
            if (enemy == null || d < nearest)
            {
                enemy = i;
                nearest = d;
            }
        }
        if (enemy is not int target)
        {
            return;
        }
        if (w.CanUse(me, World.StrikeAction) && w.Adjacent(me, target) && world.Use(World.StrikeAction, target))
        {
            return;
        }
        Cell goal = w.CellOf(target);
        Cell? best = null;
        float bestDistance = w.Grid.Distance(w.CellOf(me), goal);
        float bestCost = float.MaxValue;
        foreach ((Cell cell, float cost) in w.ReachableCells().OrderBy(p => p.Key.Y).ThenBy(p => p.Key.X).Select(p => (p.Key, p.Value)))
        {
            float d = w.Grid.Distance(cell, goal);
            if (d < bestDistance - 0.01f || (best != null && d < bestDistance + 0.01f && cost < bestCost))
            {
                best = cell;
                bestDistance = d;
                bestCost = cost;
            }
        }
        if (best is Cell to && to != w.CellOf(me) && world.MoveTo(to))
        {
            return;
        }
        world.Use(World.EndTurnAction);
    }

    private static readonly Dictionary<string, string> WalledYard = new()
    {
        ["chapters/fixture-yard/chapter.json"] = """
            {"id":"fixture-yard","title":"The Yard","map":"map.json","intro":["A goblin waits in the yard."],"xpPerVictory":10,
             "party":[{"name":"Ana","class":"fighter","at":[1,1]},{"name":"Bo","class":"cleric","at":[1,2]}],
             "encounters":[{"id":"yard","set":["yard_clear"],"text":"A goblin!","creatures":[{"creature":"goblin","name":"Gik","at":[8,4]}]}],
             "victoryText":"Won ({xp} XP).","defeatText":"Lost.","clearedText":"The yard is clear."}
            """,
        ["chapters/fixture-yard/map.json"] = Map("""["##########","#........#","#........#","#####....#","#........#","##########"]"""),
    };

    [Fact]
    public void AFightFromSightToVictory()
    {
        using WorldFixture world = WorldFixture.LoadJson("chapters/fixture-yard", WalledYard, 5);
        World w = world.World;
        Assert.True(world.Go(0, new Cell(7, 4)));
        Assert.True(world.StepUntil(() => w.Fighting, 30) && world.Said("A goblin!"), "Seeing the goblin starts its fight");
        Assert.True(world.Said("initiative") && world.Said("Round 1") && w.Encounter!.Order.Count == 3);
        bool done = world.StepUntil(() =>
        {
            PlayParty(world);
            return !w.Fighting;
        }, 300);
        Assert.True(done && !w.PartyDown && w.ChapterCleared() && w.Flags.Contains("yard_clear"), "The fight is won and sets its flag");
        Assert.True(world.Said("Won (10 XP).") && world.Said("The yard is clear."), "Clearing the chapter says its text");
        Assert.True(w.Creatures[0].Sheet.Xp == 10 && w.FightGroup == null);
    }

    [Fact]
    public void AnAmbushSurprisesAndTheFallenAreMarked()
    {
        using WorldFixture world = WorldFixture.LoadJson("chapters/fixture-yard", WalledYard, 5);
        World w = world.World;
        Ruleset rules = w.Rules;
        int goblin = w.HeroCount;
        Assert.False(w.Ambush(goblin), "Not while in plain view of nobody: the party isn't hidden");
        Assert.True(world.Sneak(true) && world.Go(0, new Cell(7, 4)));
        world.StepUntil(() => w.Fighting || w.Fog.State(0, 0, w.CellOf(goblin)) == FogState.Visible, 60);
        if (!w.Fighting)
        {
            Assert.True(w.Ambush(goblin), w.Refusal);
            world.Step(1.0 / 60);
            Assert.True(world.Said("strikes from hiding") && world.Said("surprise"));
        }
        Assert.True(w.Fighting && !w.Creatures[0].Sneaking && !w.Creatures[1].Sheet.HasCondition("hidden"), "The fight ends Hidden for everyone");
        bool done = world.StepUntil(() =>
        {
            PlayParty(world);
            return !w.Fighting;
        }, 300);
        Assert.True(done);
        CharacterSheet fallen = w.Creatures[goblin].Sheet;
        // One that ran off is out of the adventure instead, with nothing left on its sheet.
        Assert.True(fallen.Down && fallen.HasCondition("dead") != w.Creatures[goblin].Fled && fallen.HasFlag(rules, "dead") != w.Creatures[goblin].Fled,
            "A goblin that falls in the fight is Dead");
        for (int i = 0; i < w.HeroCount; i++)
        {
            Assert.False(w.Creatures[i].Sheet.Down && !w.PartyWiped && !w.Creatures[i].Sheet.Death.Dead, "A win gets the downed back up");
        }
    }

    // The party stood a couple of steps from an encounter's first creature, wherever the map has room.
    private static void GatherPartyAt(World w, int group)
    {
        int first = w.Creatures.FindIndex(c => c.Group == group && c.Team == 1);
        var seen = new HashSet<Cell> { w.CellOf(first) };
        var queue = new Queue<(Cell Cell, int Steps)>();
        queue.Enqueue((w.CellOf(first), 0));
        int hero = 0;
        while (queue.Count > 0 && hero < w.HeroCount)
        {
            (Cell at, int steps) = queue.Dequeue();
            if (steps >= 2 && !w.Occupied(at, -1))
            {
                w.Place(hero++, at);
            }
            foreach (Cell next in w.Grid.Neighbours(at))
            {
                // Straight steps only, so the party isn't put behind a wall's corner.
                if ((next.X == at.X || next.Y == at.Y) && w.Walkable(next) && seen.Add(next))
                {
                    queue.Enqueue((next, steps + 1));
                }
            }
        }
        Assert.True(hero == w.HeroCount, "room for the party");
    }

    private static List<string> PlayOut(string folder, int group, ulong seed)
    {
        using WorldFixture world = WorldFixture.Load(folder, seed);
        World w = world.World;
        w.Options.AutoPlay = true;
        GatherPartyAt(w, group);
        world.Fight(group);
        string name = $"{folder} encounter {w.Chapter.Encounters[group].Id}";
        Assert.True(w.Encounter != null && w.Encounter.Order.Count > w.HeroCount, name + " has its creatures in the fight");
        bool over = world.StepUntil(() => !w.Fighting, 3600);
        Assert.True(over, $"{name} never ended (round {w.Encounter.Round}):\n{string.Join("\n", world.Log.TakeLast(25))}");
        Assert.True(w.Encounter.Finished && w.CurrentCreature == null, name);
        List<string> ends = world.EventsOf(WorldEventKind.FightOver);
        Assert.Single(ends);
        if (ends[0] == "victory")
        {
            Assert.True(!w.PartyWiped && !w.PartyDown && w.FightGroup == null, name);
            Assert.DoesNotContain(w.Creatures, c => c.Team == 1 && c.Awake && w.OrderIndex(w.Creatures.IndexOf(c)) != null && !c.Sheet.Down);
        }
        else
        {
            Assert.True(ends[0] == "defeat" && w.PartyWiped, name);
        }
        return world.Log;
    }

    [Fact]
    public void EveryShippedEncounterPlaysToItsEnd()
    {
        int played = 0;
        string chapters = Path.Combine(TestContent.AssetsFolder(), "chapters");
        foreach (string path in Directory.GetDirectories(chapters).OrderBy(p => p, StringComparer.Ordinal))
        {
            string folder = "chapters/" + Path.GetFileName(path);
            int groups;
            using (WorldFixture probe = WorldFixture.Load(folder, 1))
            {
                groups = probe.World.HeroCount == 0 ? 0 : probe.World.Chapter.Encounters.Count;
            }
            for (int group = 0; group < groups; group++)
            {
                List<string> log = PlayOut(folder, group, 11);
                // The same seed plays the same fight, line for line. Once a chapter keeps the run short.
                if (group == 0)
                {
                    Assert.Equal(log, PlayOut(folder, group, 11));
                }
                played++;
            }
        }
        Assert.True(played >= 3, "the shipped chapters have encounters");
    }
}
