namespace Yorehold.Rules.Tests;

/// <summary>Running an effect's steps against a table of four plain creatures.</summary>
public class EffectRunTests
{
    private const string Authored = """
        {"id":"t","name":"Test","abilities":[{"id":"str","name":"Str"},{"id":"dex","name":"Dex"},{"id":"con","name":"Con"},{"id":"wis","name":"Wis"}],
        "skills":[{"id":"athletics","name":"Athletics","ability":"str"}],
        "conditions":[
            {"id":"burning","name":"Burning","duration":3},
            {"id":"shaken","name":"Shaken","stacking":"value","maxValue":5},
            {"id":"asleep","name":"Asleep","ends":["damage"]},
            {"id":"downed","name":"Downed","ends":["healed"]},
            {"id":"unseen","name":"Unseen","ends":["attack"]},
            {"id":"wounded","name":"Wounded","flags":["wounded"]}
        ]}
        """;

    private const string Strike = """
        [{"do":"roll","kind":"attack","steps":[
            {"do":"damage","dice":"weapon","when":"hit","minimum":1},
            {"do":"condition","id":"shaken","when":"crit"},
            {"do":"condition","id":"burning","when":"miss","target":"self"}]}]
        """;

    private static readonly Ruleset Rules = RulesTesting.Rules(Authored);

    private static EffectContext Context(ulong seed, params int[] targets)
    {
        return new EffectContext(Rules, new Rng(seed)) { Self = 0, Targets = targets.ToList(), Source = "test" };
    }

    private static EffectResult Run(string json, EffectHost host, EffectContext context)
    {
        return RulesTesting.Effect(json).Run(host, context);
    }

    private static int Count(EffectResult result, EffectEventKind kind) => result.Events.Count(e => e.Kind == kind);

    private static EffectEvent? First(EffectResult result, EffectEventKind kind) => result.Events.Find(e => e.Kind == kind);

    [Fact]
    public void DamageHealingAndTemporaryHp()
    {
        EffectContext context = Context(7, 2);

        // Damage: off the target, in the type named, ending what being hit ends.
        var host = new TableHost();
        host.Sheets[2].AddCondition(Rules, "asleep");
        EffectResult result = Run("""[{"do":"damage","dice":10,"type":"fire"}]""", host, context);
        Assert.Equal((90, 100, "fire"), (host.Sheets[2].Hp, host.Sheets[0].Hp, host.DamageType));
        Assert.False(host.Sheets[2].HasCondition("asleep"));
        Assert.Equal(2, result.Events.Count);
        EffectEvent hit = result.Events[0];
        Assert.Equal((EffectEventKind.Damage, 2, 0, 10, "fire", false), (hit.Kind, hit.Who, hit.By, hit.Amount, hit.Id, hit.Dropped));
        Assert.Equal((EffectEventKind.ConditionEnded, "asleep"), (result.Events[1].Kind, result.Events[1].Id));
        host.Sheets[2].TempHp = 4;
        host.Sheets[2].Hp = 5;
        result = Run("""[{"do":"damage","dice":"9"}]""", host, context);
        Assert.Equal((0, 0), (host.Sheets[2].Hp, host.Sheets[2].TempHp));
        Assert.True(result.Events[0].Dropped);
        Assert.Equal("untyped", result.Events[0].Id);
        host = new TableHost();
        Run("""[{"do":"damage","dice":"1-5","minimum":1}]""", host, context);
        Assert.Equal(99, host.Sheets[2].Hp); // a blow that lands always does something where the step says so
        Run("""[{"do":"damage","dice":"1-5"}]""", host, context);
        Assert.Equal(99, host.Sheets[2].Hp);

        // Heal: up to the maximum, and getting someone up ends what being healed ends.
        host.Sheets[2].Hp = 0;
        host.Sheets[2].AddCondition(Rules, "downed");
        result = Run("""[{"do":"heal","dice":30}]""", host, context);
        Assert.Equal(30, host.Sheets[2].Hp);
        Assert.False(host.Sheets[2].HasCondition("downed"));
        Assert.Equal(new[] { EffectEventKind.Heal, EffectEventKind.ConditionEnded }, result.Events.Select(e => e.Kind).ToArray());
        Assert.Equal(30, result.Events[0].Amount);
        host.Sheets[2].Hp = 95;
        result = Run("""[{"do":"heal","dice":30}]""", host, context);
        Assert.Equal((100, 5), (host.Sheets[2].Hp, result.Events[0].Amount));

        // Temporary HP: the larger stays, they never add up.
        Run("""[{"do":"tempHp","dice":8,"target":"self"}]""", host, context);
        Run("""[{"do":"tempHp","dice":5,"target":"self"}]""", host, context);
        Assert.Equal((8, 0), (host.Sheets[0].TempHp, host.Sheets[2].TempHp));
    }

    [Fact]
    public void ConditionsModifiersAndResources()
    {
        EffectContext context = Context(7, 2);
        var host = new TableHost();

        // Conditions go on with the step's duration and value, or the definition's, and come off again.
        EffectResult result = Run("""[{"do":"condition","id":"burning"},{"do":"condition","id":"shaken","value":2,"duration":4},{"do":"condition","id":"shaken"}]""", host, context);
        Assert.True(host.Sheets[2].HasCondition("burning"));
        Assert.Equal((3, 3), (host.Sheets[2].Conditions[0].RoundsLeft, host.Sheets[2].ConditionValue("shaken")));
        Assert.Equal((3, 2, 3), (Count(result, EffectEventKind.ConditionAdded), result.Events[1].Amount, result.Events[2].Amount));
        result = Run("""[{"do":"condition","id":"burning","remove":true},{"do":"condition","id":"asleep","remove":true}]""", host, context);
        Assert.False(host.Sheets[2].HasCondition("burning"));
        Assert.Equal((1, "burning"), (Count(result, EffectEventKind.ConditionRemoved), result.Events[0].Id));

        // A modifier lasts its rounds, and doing the same thing again does not double it.
        Effect warded = RulesTesting.Effect("""[{"do":"modifier","stat":"ac","value":2,"duration":2,"target":"self"},{"do":"modifier","stat":"speed","op":"multiply","value":0.5,"duration":2,"target":"self"}]""");
        int ac = host.Sheets[0].ArmorClass(Rules);
        result = warded.Run(host, context);
        Assert.Equal((ac + 2, 15, 2), (host.Sheets[0].ArmorClass(Rules), host.Sheets[0].SpeedFeet, Count(result, EffectEventKind.Modifier)));
        Assert.Equal(("ac", 2, "effect:test"), (result.Events[0].Id, result.Events[0].Amount, result.Events[0].Tracked));
        warded.Run(host, context);
        Assert.Equal((ac + 2, 15), (host.Sheets[0].ArmorClass(Rules), host.Sheets[0].SpeedFeet));
        Assert.True(host.Sheets[0].HasCondition("effect:test"));
        host.Sheets[0].EndRound(Rules);
        Assert.Equal(ac + 2, host.Sheets[0].ArmorClass(Rules));
        host.Sheets[0].EndRound(Rules);
        Assert.Equal((ac, 30), (host.Sheets[0].ArmorClass(Rules), host.Sheets[0].SpeedFeet));
        Assert.Empty(host.Sheets[0].Conditions);
        // A step with an id of its own is tracked under that instead of the effect's name.
        result = Run("""[{"do":"modifier","id":"ward","stat":"ac","value":1,"target":"self"}]""", host, context);
        Assert.Equal("effect:ward", result.Events[0].Tracked);
        Assert.Equal(-1, host.Sheets[0].Conditions[0].RoundsLeft);
        host.Sheets[0].RemoveCondition("effect:ward");
        Assert.Equal(ac, host.Sheets[0].ArmorClass(Rules));

        // Resources are spent and restored within what the sheet has.
        host.Sheets[0].Resources["rage"] = new Resource(2, 3);
        result = Run("""[{"do":"resource","id":"rage","amount":1,"target":"self"}]""", host, context);
        Assert.Equal(1, host.Sheets[0].Resources["rage"].Current);
        Assert.Single(result.Events);
        Assert.Equal((EffectEventKind.Resource, -1), (result.Events[0].Kind, result.Events[0].Amount));
        Run("""[{"do":"resource","id":"rage","op":"restore","amount":9,"target":"self"}]""", host, context);
        Assert.Equal(3, host.Sheets[0].Resources["rage"].Current);
        Run("""[{"do":"resource","id":"rage","op":"spend","amount":9,"target":"self"}]""", host, context);
        Assert.Equal(0, host.Sheets[0].Resources["rage"].Current);
        Assert.Empty(Run("""[{"do":"resource","id":"ki","target":"self"}]""", host, context).Events); // it has none
    }

    [Fact]
    public void MapStepsGoToTheHost()
    {
        EffectContext context = Context(7, 2);
        var host = new TableHost();

        // Everything on the map goes to the host, with the numbers from the file.
        EffectResult result = Run("""
            [{"do":"move","how":"push","distance":2},{"do":"move","how":"teleport","distance":"speed","target":"self"},
            {"do":"summon","id":"wolf","count":2,"duration":10},{"do":"light","radius":4,"duration":5,"target":"self"},
            {"do":"surface","id":"grease","size":2,"duration":3},{"do":"flag","id":"bell_rung"},{"do":"flag","id":"door_shut","remove":true}]
            """, host, context);
        Assert.Equal(new[] { "move 2 push 2", "move 0 teleport 6", "summon wolf 2 10", "light 0 4 5", "surface grease 2 3", "set bell_rung", "clear door_shut" }, host.Calls.ToArray());
        Assert.Equal(
            new[]
            {
                EffectEventKind.Move, EffectEventKind.Move, EffectEventKind.Summon, EffectEventKind.Light,
                EffectEventKind.Surface, EffectEventKind.Flag, EffectEventKind.Flag,
            },
            result.Events.Select(e => e.Kind).ToArray());
        Assert.Equal((2, 1, 0), (result.Events[2].Amount, result.Events[5].Amount, result.Events[6].Amount));

        // A host that supports none of that: the steps do nothing and report nothing.
        var bare = new BareHost();
        result = Run("""
            [{"do":"move","how":"pull","target":"self"},{"do":"summon","id":"wolf"},{"do":"light","radius":4,"target":"self"},
            {"do":"surface","id":"ice"},{"do":"flag","id":"x"},{"do":"damage","dice":3,"target":"self"},{"do":"damage","dice":3,"target":"enemies"}]
            """, bare, context);
        Assert.Single(result.Events);
        Assert.Equal(97, bare.One.Hp);
    }

    [Fact]
    public void StepsLandOnWhoTheyAreAimedAt()
    {
        EffectContext context = Context(7, 2);
        var host = new TableHost { Area = new List<int> { 1, 2, 3 } };
        Run("""
            [{"do":"damage","dice":1,"target":"self"},{"do":"damage","dice":2,"target":"target"},{"do":"damage","dice":4,"target":"area"},
            {"do":"damage","dice":8,"target":"allies"},{"do":"damage","dice":16,"target":"enemies"}]
            """, host, context);
        Assert.Equal(new[] { 100 - 1 - 8, 100 - 4 - 8, 100 - 2 - 4 - 16, 100 - 4 - 16 }, host.Sheets.Select(s => s.Hp).ToArray());

        context.Targets = new List<int> { 2, 3, 99 }; // several targets, and one that is not there
        host = new TableHost();
        Run("""[{"do":"damage","dice":3}]""", host, context);
        Assert.Equal((97, 97), (host.Sheets[2].Hp, host.Sheets[3].Hp));
        context.Targets = new List<int> { 2 };

        // Repeat, with a fixed count; choose, through the host.
        host = new TableHost();
        EffectResult result = Run("""[{"do":"repeat","times":3,"steps":[{"do":"damage","dice":2}]}]""", host, context);
        Assert.Equal((94, 3), (host.Sheets[2].Hp, Count(result, EffectEventKind.Damage)));
        Effect either = RulesTesting.Effect("""
            [{"do":"choose","options":[{"name":"Burn","steps":[{"do":"damage","dice":5,"type":"fire"}]},
            {"name":"Mend","steps":[{"do":"heal","dice":3}]}]}]
            """);
        result = either.Run(host, context);
        Assert.Equal((89, EffectEventKind.Choice, "Burn"), (host.Sheets[2].Hp, result.Events[0].Kind, result.Events[0].Id));
        host.Pick = 1;
        result = either.Run(host, context);
        Assert.Equal((92, "Mend", 1), (host.Sheets[2].Hp, result.Events[0].Id, result.Events[0].Amount));
        host.Pick = 7; // a host answering out of range gets the last option
        either.Run(host, context);
        Assert.Equal(95, host.Sheets[2].Hp);

        // A step can ask for a flag on whoever it lands on.
        host = new TableHost();
        host.Sheets[2].Hp = 50;
        context.Targets = new List<int> { 2, 99 };
        Effect help = RulesTesting.Effect("""[{"do":"heal","dice":1,"ifFlag":"wounded"}]""");
        Assert.Empty(help.Run(host, context).Events);
        Assert.Equal(50, host.Sheets[2].Hp);
        host.Sheets[2].AddCondition(Rules, "wounded");
        Assert.Single(help.Run(host, context).Events);
        Assert.Equal(51, host.Sheets[2].Hp);
    }

    [Fact]
    public void AnAttackFollowsTheDice()
    {
        // The weapon's bonus against armour class, a natural 1 always missing and a natural 20
        // always a critical hit that rolls the damage dice twice. Whatever the dice do, the steps
        // under it follow the result.
        Effect strike = RulesTesting.Effect(Strike);
        int hits = 0;
        int misses = 0;
        int crits = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            TableHost host = Armed();
            host.Sheets[0].AddCondition(Rules, "unseen");
            EffectResult result = strike.Run(host, Context(seed, 2));
            EffectEvent attack = result.Events[0];
            EffectEvent? damage = First(result, EffectEventKind.Damage);
            Assert.Equal(EffectEventKind.Attack, attack.Kind);
            bool critical = attack.Roll.Natural20;
            bool hit = !attack.Roll.Natural1 && (critical || attack.Roll.Total >= 13);
            Assert.Equal((hit, critical, 13, 2), (attack.Success, attack.Critical, attack.Dc, attack.Roll.Flat));
            Assert.Equal(hit, damage != null);
            Assert.False(host.Sheets[0].HasCondition("unseen"));
            Assert.Equal((EffectEventKind.ConditionEnded, 0), (result.Events[1].Kind, result.Events[1].Who));
            Assert.Equal(critical, host.Sheets[2].HasCondition("shaken"));
            Assert.Equal(!hit, host.Sheets[0].HasCondition("burning"));
            if (damage != null)
            {
                Assert.Equal(critical ? 2 : 1, damage.Roll.Dice.Count(die => die.Sides == 8));
                Assert.Equal((2, critical, damage.Roll.Total), (damage.Roll.Flat, damage.Critical, damage.Amount));
                Assert.Equal(100 - damage.Amount, host.Sheets[2].Hp);
                Assert.Equal(critical, host.CriticalDamage);
            }
            hits += hit && !critical ? 1 : 0;
            misses += hit ? 0 : 1;
            crits += critical ? 1 : 0;
        }
        Assert.True(hits > 0 && misses > 0 && crits > 0);
    }

    [Fact]
    public void AnAttackRollsTheSameDiceAsTheOldClient()
    {
        // Seed 7 gives 14 on the d20 and then 2 on the d8, in that order: the attack, then the damage.
        TableHost host = Armed();
        EffectResult result = RulesTesting.Effect(Strike).Run(host, Context(7, 2));
        Assert.Equal(new[] { EffectEventKind.Attack, EffectEventKind.Damage }, result.Events.Select(e => e.Kind).ToArray());
        Assert.Equal("1d20+2: [14] + 2 = 16", result.Events[0].Roll.Describe());
        Assert.Equal("1d8+2: [2] + 2 = 4", result.Events[1].Roll.Describe());
        Assert.Equal(96, host.Sheets[2].Hp);

        // Disadvantage takes the lower of 14 and 10, which misses AC 13; the damage die is never rolled.
        host = Armed();
        Ruleset rules = RulesTesting.Rules(Authored.Replace("\"name\":\"Unseen\"", "\"name\":\"Unseen\",\"disadvantageOnAttacks\":true"));
        host.Sheets[0].AddCondition(rules, "unseen");
        var random = new Rng(7);
        result = RulesTesting.Effect(Strike).Run(host, new EffectContext(rules, random) { Self = 0, Targets = new List<int> { 2 } });
        Assert.Equal("2d20kl1+2: [(14), 10] + 2 = 12", result.Events[0].Roll.Describe());
        Assert.False(result.Events[0].Success);
        Assert.Equal(100, host.Sheets[2].Hp);
        Assert.True(host.Sheets[0].HasCondition("burning"));
        Assert.Equal(5, random.Range(1, 20)); // the third number from seed 7 is still to come
    }

    [Fact]
    public void ChecksAndSavesGateTheStepsUnderThem()
    {
        EffectContext context = Context(3, 2);
        context.Dc = 1000;

        // A check by the doer against a DC, the caster's DC or the target's passive score.
        const string check = """
            [{"do":"roll","kind":"check","ability":"athletics","dc":DC,"target":"target","steps":[
            {"do":"condition","id":"shaken","when":"success"},{"do":"condition","id":"burning","when":"failure","target":"self"}]}]
            """;
        var host = new TableHost();
        EffectResult result = Run(check.Replace("DC", "-100"), host, context);
        Assert.True(host.Sheets[2].HasCondition("shaken") && !host.Sheets[0].HasCondition("burning"));
        Assert.Equal((EffectEventKind.Check, true, -100, "athletics"), (result.Events[0].Kind, result.Events[0].Success, result.Events[0].Dc, result.Events[0].Id));
        host = new TableHost();
        result = Run(check.Replace("DC", "\"caster\""), host, context);
        Assert.True(!host.Sheets[2].HasCondition("shaken") && host.Sheets[0].HasCondition("burning"));
        Assert.Equal((false, 1000), (result.Events[0].Success, result.Events[0].Dc));
        host = new TableHost();
        host.Sheets[2].Stats.SetBase("str", 18);
        result = Run("""[{"do":"roll","kind":"check","ability":"athletics","against":"athletics","steps":[]}]""", host, context);
        Assert.Single(result.Events);
        Assert.Equal((2, 14), (result.Events[0].Who, result.Events[0].Dc)); // 10 + 4
        result = Run("""[{"do":"roll","kind":"check","ability":"str","dc":5,"steps":[{"do":"heal","dice":1}]}]""", host, context);
        Assert.Equal(0, result.Events[0].Who); // with nobody to beat, the check is about the doer

        // A save made by each target, gating the steps under it.
        host = new TableHost();
        context.Targets = new List<int> { 2, 3 };
        result = Run("""
            [{"do":"roll","kind":"save","ability":"dex","dc":1000,"steps":[
            {"do":"damage","dice":10,"onSave":"half"},{"do":"condition","id":"burning","when":"saveFailed"},{"do":"condition","id":"shaken","when":"saveSucceeded"}]}]
            """, host, context);
        Assert.Equal((90, 90), (host.Sheets[2].Hp, host.Sheets[3].Hp));
        Assert.True(host.Sheets[2].HasCondition("burning") && !host.Sheets[3].HasCondition("shaken"));
        Assert.Equal((2, false, 2), (Count(result, EffectEventKind.Save), result.Events[0].Success, result.Events[0].Who));
        host = new TableHost();
        Run("""
            [{"do":"roll","kind":"save","ability":"dex","dc":"caster","steps":[{"do":"damage","dice":10,"onSave":"none"}]},
            {"do":"roll","kind":"save","ability":"dex","dc":-100,"steps":[
            {"do":"damage","dice":9,"onSave":"half"},{"do":"damage","dice":50,"onSave":"none"},{"do":"condition","id":"burning","when":"saveFailed"},
            {"do":"condition","id":"shaken","when":"saveSucceeded"}]}]
            """, host, context);
        Assert.Equal((100 - 10 - 4, 100 - 10 - 4), (host.Sheets[2].Hp, host.Sheets[3].Hp));
        Assert.True(!host.Sheets[2].HasCondition("burning") && host.Sheets[3].HasCondition("shaken"));
    }

    [Fact]
    public void TheEffectsOwnSaveIsMadeOnceEach()
    {
        EffectContext context = Context(5, 2);

        // Each creature makes it once, however many steps ask; a success halves or stops a step as
        // the step says, and steps that do not mention it ignore it.
        const string blast = """
            {"save":{"ability":"dex","dc":DC},"effects":[
            {"do":"damage","dice":11,"type":"fire","onSave":"half","target":"area"},
            {"do":"condition","id":"burning","onSave":"none","target":"area"},
            {"do":"condition","id":"shaken","when":"saveSucceeded","target":"area"},
            {"do":"damage","dice":1,"target":"area"}]}
            """;
        var host = new TableHost { Area = new List<int> { 2, 3 } };
        EffectResult result = Run(blast.Replace("DC", "-100"), host, context);
        Assert.Equal((100 - 5 - 1, 100 - 5 - 1), (host.Sheets[2].Hp, host.Sheets[3].Hp));
        Assert.True(!host.Sheets[2].HasCondition("burning") && host.Sheets[3].HasCondition("shaken"));
        Assert.Equal(2, Count(result, EffectEventKind.Save));
        EffectEvent save = result.Events[0];
        Assert.Equal((EffectEventKind.Save, true, 2, "dex"), (save.Kind, save.Success, save.Who, save.Id));
        Assert.Equal(5, First(result, EffectEventKind.Damage)?.Amount);
        host = new TableHost { Area = new List<int> { 2, 3 } };
        result = Run(blast.Replace("DC", "1000"), host, context);
        Assert.Equal(100 - 11 - 1, host.Sheets[2].Hp);
        Assert.True(host.Sheets[2].HasCondition("burning") && !host.Sheets[2].HasCondition("shaken"));
        Assert.Equal(2, Count(result, EffectEventKind.Save));
        host = new TableHost { Area = new List<int> { 2 } };
        context.Dc = -100;
        Run(blast.Replace("DC", "\"caster\""), host, context);
        Assert.Equal((94, 100), (host.Sheets[2].Hp, host.Sheets[1].Hp));

        // One roll of the damage for everyone in the area.
        host = new TableHost { Area = new List<int> { 1, 2, 3 } };
        result = Run("""[{"do":"damage","dice":"4d6","target":"area"}]""", host, context);
        Assert.Equal(3, Count(result, EffectEventKind.Damage));
        Assert.True(host.Sheets[1].Hp == host.Sheets[2].Hp && host.Sheets[2].Hp == host.Sheets[3].Hp && host.Sheets[1].Hp < 100);
        // Nobody in a save-for-nothing effect rolls if no step asks.
        result = Run("""{"save":{"ability":"dex","dc":10},"effects":[{"do":"damage","dice":1}]}""", host, context);
        Assert.Equal(0, Count(result, EffectEventKind.Save));
    }

    [Fact]
    public void StepsScaleByLevelOrSlot()
    {
        EffectContext context = Context(5, 2);

        // Once for each `every` levels above `from`, by the doer's level or the slot used.
        Effect byLevel = RulesTesting.Effect("""[{"do":"damage","dice":10,"scale":{"by":"level","from":1,"every":2,"dice":"3","value":1}}]""");
        var host = new TableHost();
        byLevel.Run(host, context);
        Assert.Equal(90, host.Sheets[2].Hp); // level 1: nothing added
        host.Sheets[2].Hp = 100;
        host.Sheets[0].Level = 5;
        byLevel.Run(host, context);
        Assert.Equal(100 - 10 - 2 * 4, host.Sheets[2].Hp);
        host.Sheets[2].Hp = 100;
        host.Sheets[0].Level = 6; // not yet a third step
        byLevel.Run(host, context);
        Assert.Equal(100 - 18, host.Sheets[2].Hp);
        host.Sheets[2].Hp = 100;
        context.Level = 9; // the caller can say what level it is done at
        byLevel.Run(host, context);
        Assert.Equal(100 - 10 - 4 * 4, host.Sheets[2].Hp);
        context.Level = 0;

        Effect bySlot = RulesTesting.Effect("""
            [{"do":"heal","dice":4,"scale":{"by":"slot","from":1,"dice":"2"}},
            {"do":"condition","id":"shaken","scale":{"by":"slot","from":1,"value":1}},
            {"do":"repeat","times":1,"scale":{"by":"slot","from":2,"value":1},"steps":[{"do":"tempHp","dice":1,"target":"self"},{"do":"damage","dice":1,"target":"self"}]}]
            """);
        host = new TableHost();
        host.Sheets[2].Hp = 50;
        context.Slot = 1;
        bySlot.Run(host, context);
        Assert.Equal((54, 1, 100, 0), (host.Sheets[2].Hp, host.Sheets[2].ConditionValue("shaken"), host.Sheets[0].Hp, host.Sheets[0].TempHp));
        host = new TableHost();
        host.Sheets[2].Hp = 50;
        context.Slot = 3;
        EffectResult result = bySlot.Run(host, context);
        Assert.Equal((58, 3, 2), (host.Sheets[2].Hp, host.Sheets[2].ConditionValue("shaken"), Count(result, EffectEventKind.TempHp)));
        Assert.Equal(8, First(result, EffectEventKind.Heal)?.Roll.Total);
    }

    [Fact]
    public void StepsThatWaitForAnEventRunOnlyForIt()
    {
        EffectContext context = Context(5, 2);
        Effect lingering = RulesTesting.Effect("""
            [{"do":"damage","dice":6},{"do":"damage","dice":2,"when":"turnStart"},
            {"do":"repeat","times":1,"when":"turnEnd","steps":[{"do":"heal","dice":1}]}]
            """);
        var host = new TableHost();
        lingering.Run(host, context);
        Assert.Equal(94, host.Sheets[2].Hp);
        context.Event = "turnStart";
        lingering.Run(host, context);
        Assert.Equal(92, host.Sheets[2].Hp);
        context.Event = "turnEnd";
        lingering.Run(host, context);
        Assert.Equal(93, host.Sheets[2].Hp);
    }

    [Fact]
    public void TheHostDecidesArmourClassAndFlags()
    {
        var host = new PositionedHost();
        EffectContext context = new(Rules, new Rng(7)) { Self = 0, Targets = new List<int> { 0 } };
        Effect effect = RulesTesting.Effect("""[{"do":"roll","kind":"attack","steps":[]},{"do":"damage","dice":3,"ifFlag":"positioned"}]""");
        Assert.Equal(123, effect.Run(host, context).Events[0].Dc);
        Assert.Equal(97, host.Creature.Hp);
        host.Positional = false;
        effect.Run(host, context);
        Assert.Equal(97, host.Creature.Hp);

        // Left alone, a host answers from the sheet.
        var bare = new BareHost();
        Assert.Equal(bare.One.ArmorClass(Rules), bare.ArmorClass(0, context));
        Assert.False(bare.HasFlag(0, "positioned", context));
        Assert.Equal(0, bare.ArmorClass(5, context));
    }

    private static TableHost Armed()
    {
        var host = new TableHost();
        host.Sheets[0].Weapon = new Weapon("1d8");
        host.Sheets[0].Stats.SetBase("str", 14);
        host.Sheets[2].Stats.SetBase("ac", 13);
        return host;
    }

    private sealed class PositionedHost : EffectHost
    {
        public CharacterSheet Creature { get; } = RulesTesting.Plain("Ana");
        public bool Positional { get; set; } = true;

        public override CharacterSheet? Sheet(int who) => Creature;
        public override List<int> Group(string which, EffectContext context) => new();
        public override int ArmorClass(int who, EffectContext context, string defence = "") => 123;
        public override bool HasFlag(int who, string flag, EffectContext context) => Positional && flag == "positioned";
    }
}
