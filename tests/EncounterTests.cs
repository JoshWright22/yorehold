namespace Yorehold.Rules.Tests;

/// <summary>The fight on its own: initiative, shared turns, budgets, attacks and death saves. The C++ framework's CombatTests and DeathTests.</summary>
public class EncounterTests
{
    private const string Abilities = """
        "abilities":[{"id":"str","name":"Str"},{"id":"dex","name":"Dex"},{"id":"con","name":"Con"},{"id":"wis","name":"Wis"}]
        """;

    private static Ruleset TurnRules(bool shared)
    {
        return RulesTesting.Rules($$"""
            {"id":"t","name":"Test",{{Abilities}},"actionsPerTurn":2,"bonusActions":false,"sharedTurns":{{(shared ? "true" : "false")}},
             "conditions":[{"id":"start","name":"Start","ends":["turnStart"]},{"id":"end","name":"End","ends":["turnEnd"]},
                           {"id":"stunned","name":"Stunned","flags":["cantAct"]}]}
            """);
    }

    private static Ruleset DeathRules(bool shared = false, int saveDc = 10, int naturalTwentyHp = 1)
    {
        return RulesTesting.Rules($$"""
            {"id":"t","name":"Test",{{Abilities}},"sharedTurns":{{(shared ? "true" : "false")}},
             "death":{"enabled":true,"saveDc":{{saveDc}},"naturalTwentyHp":{{naturalTwentyHp}},
                      "downedCondition":"fallen","dyingCondition":"dying","stableCondition":"stable","deadCondition":"dead"},
             "conditions":[{"id":"fallen","name":"fallen","flags":["cantAct","cantMove"]},{"id":"dying","name":"dying","flags":["cantAct","cantMove"]},
                           {"id":"stable","name":"stable","flags":["cantAct","cantMove"]},{"id":"dead","name":"dead","flags":["cantAct","cantMove"]}]}
            """);
    }

    private static CharacterSheet[] People(int count, int hp = 200)
    {
        var people = new CharacterSheet[count];
        for (int i = 0; i < count; i++)
        {
            people[i] = RulesTesting.Plain(i.ToString());
            people[i].Stats.SetBase("dex", 2000.0f - i * 200.0f);
            people[i].Stats.SetBase("maxHp", hp);
            people[i].Hp = hp;
        }
        return people;
    }

    private static CharacterSheet Ana()
    {
        CharacterSheet c = RulesTesting.Plain("Ana");
        c.Hp = 10;
        c.Stats.SetBase("maxHp", 10);
        return c;
    }

    // A seed whose first plain d20 shows face.
    private static ulong SeedFor(int face)
    {
        for (ulong seed = 0; seed < 10000; seed++)
        {
            if (Dice.RollD20(0, Advantage.None, new Rng(seed)).Total == face)
            {
                return seed;
            }
        }
        throw new InvalidOperationException("no death-save seed");
    }

    [Fact]
    public void SharedTurnsRunByBlocks()
    {
        Ruleset rules = TurnRules(true);
        CharacterSheet[] people = People(5);
        var fight = new Encounter(rules, 7);
        for (int i = 0; i < people.Length; i++)
        {
            fight.Add(people[i], i == 2 || i == 4 ? 1 : 0);
        }
        fight.Start();
        Assert.True(fight.Round == 1 && fight.CurrentIndex == 0 && fight.BlockFirst == 0 && fight.BlockEnd == 2);
        Assert.True(fight.CanSelectTurn(1) && !fight.CanSelectTurn(2) && !fight.CanSelectTurn(3) && !fight.SelectTurn(999));
        ulong serial = fight.BlockSerial;
        people[0].AddCondition(rules, "start");
        people[0].AddCondition(rules, "end");
        Assert.True(fight.SpendActions(1) && fight.SpendMovement(1) && fight.UseReaction(0));
        TurnBudget left = fight.Current.Budget.Copy();
        Assert.True(fight.SelectTurn(1) && fight.Current.Budget.Actions == 2 && fight.BlockSerial == serial);
        Assert.True(fight.SelectTurn(0) && fight.Current.Budget.Actions == left.Actions
            && fight.Current.Budget.MovementLeft == left.MovementLeft && !fight.Current.Budget.Reaction);
        Assert.True(people[0].HasCondition("start") && people[0].HasCondition("end"));
        fight.NextTurn();
        Assert.True(fight.CurrentIndex == 1 && !fight.CanSelectTurn(0) && !people[0].HasCondition("end"));
        fight.NextTurn();
        Assert.True(fight.CurrentIndex == 2 && fight.Round == 1 && !fight.CanSelectTurn(3));
        fight.NextTurn();
        Assert.True(fight.CurrentIndex == 3 && fight.CanSelectTurn(3) && !fight.CanSelectTurn(0));
        fight.NextTurn();
        fight.NextTurn();
        Assert.True(fight.Round == 2 && fight.CurrentIndex == 0 && !people[0].HasCondition("start")
            && fight.Current.Budget.Reaction && fight.Current.Budget.Actions == 2);

        // A surprised side loses its block; whoever withdraws is skipped.
        var together = new Encounter(rules, 7);
        together.Add(people[0], 0);
        together.Add(people[1], 0);
        together.Add(people[2], 1);
        together.Add(people[3], 1);
        together.Surprise(0);
        together.Start();
        Assert.True(together.Round == 1 && together.CurrentIndex == 2 && together.BlockEnd == 4 && together.CanSelectTurn(3));
        Assert.True(together.SelectTurn(3));
        together.NextTurn();
        Assert.True(together.CurrentIndex == 2 && !together.CanSelectTurn(3));
        together.NextTurn();
        Assert.True(together.Round == 2 && together.CurrentIndex == 0 && together.CanSelectTurn(1));
        together.Withdraw(1);
        together.NextTurn();
        Assert.True(together.CurrentIndex == 2 && !together.CanSelectTurn(1));

        // Joining mid-round neither splits the block nor gets a turn before the next round.
        CharacterSheet reinforcement = RulesTesting.Plain("4b");
        reinforcement.Stats.SetBase("dex", 1900);
        reinforcement.Hp = 200;
        var joining = new Encounter(rules, 7);
        joining.Add(people[0], 0);
        joining.Add(people[1], 0);
        joining.Add(people[2], 1);
        joining.Start();
        joining.Join(reinforcement, 1);
        Assert.True(ReferenceEquals(joining.Current.Sheet, people[0]) && joining.BlockEnd == 3 && !joining.CanSelectTurn(1) && joining.CanSelectTurn(2));
        joining.NextTurn();
        Assert.Same(people[1], joining.Current.Sheet);
        joining.NextTurn();
        Assert.Same(people[2], joining.Current.Sheet);
        joining.NextTurn();
        joining.NextTurn();
        Assert.True(joining.Round == 2 && ReferenceEquals(joining.Current.Sheet, reinforcement));

        // Someone down when the block began stays out of it even if they get up.
        people[1].Hp = 0;
        var down = new Encounter(rules, 7);
        down.Add(people[0], 0);
        down.Add(people[1], 0);
        down.Add(people[2], 1);
        down.Start();
        Assert.False(down.CanSelectTurn(1));
        people[1].Hp = 200;
        Assert.False(down.CanSelectTurn(1));
        down.NextTurn();
        Assert.Same(people[2], down.Current.Sheet);

        var separate = new Encounter(TurnRules(false), 7);
        separate.Add(people[0], 0);
        separate.Add(people[1], 0);
        separate.Add(people[2], 1);
        separate.Start();
        Assert.True(!separate.SelectTurn(1) && separate.BlockEnd == separate.CurrentIndex + 1);
    }

    [Fact]
    public void ReactionsNeedSomeoneWhoCanAct()
    {
        Ruleset rules = TurnRules(false);
        CharacterSheet one = RulesTesting.Plain("One");
        CharacterSheet two = RulesTesting.Plain("Two");
        var fight = new Encounter(rules, 5);
        fight.Add(one, 0);
        fight.Add(two, 1);
        fight.Start();
        fight.Current.Sheet.AddCondition(rules, "stunned");
        Assert.False(fight.UseReaction(fight.CurrentIndex));
    }

    [Fact]
    public void AFightWithOneSideIsOverBeforeItStarts()
    {
        Ruleset rules = TurnRules(false);
        var fight = new Encounter(rules, 1);
        fight.Add(RulesTesting.Plain("Alone"), 0);
        fight.Start();
        Assert.True(fight.Started && fight.Finished && fight.Round == 1 && fight.WinningTeam == 0);
    }

    [Fact]
    public void DeathSavesAndDying()
    {
        Ruleset rules = DeathRules();
        CharacterSheet c = Ana();
        Assert.True(c.TakeDamage(10, rules) && c.Hp == 0 && c.HasCondition("fallen") && c.HasCondition("dying"));
        ulong success = SeedFor(11);
        ulong failure = SeedFor(5);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(c.RollDeathSave(rules, new Rng(success))!.Total == 11 && c.Death.Successes == i + 1);
        }
        var random = new Rng(failure);
        Assert.True(c.Death.Stable && c.HasCondition("stable") && !c.HasCondition("dying") && c.RollDeathSave(rules, random) == null);
        c.TakeDamage(1, rules);
        Assert.True(!c.Death.Stable && c.Death.Successes == 0 && c.Death.Failures == 1 && c.HasCondition("dying"));
        c.TempHp = 5;
        c.TakeDamage(5, rules);
        Assert.True(c.Death.Failures == 1 && c.TempHp == 0);
        c.TakeDamage(1, rules, true);
        Assert.True(c.Death.Dead && c.HasCondition("dead") && !c.HasCondition("fallen") && !c.HasCondition("dying"));
        c.Heal(10);
        Assert.True(c.Hp == 0 && c.Recover(rules, new Recovery(RecoveryKind.Full), random) == 0 && c.RollDeathSave(rules, random) == null);

        c = Ana();
        c.TakeDamage(10, rules);
        for (int i = 0; i < 3; i++)
        {
            c.RollDeathSave(rules, new Rng(failure));
        }
        Assert.True(c.Death.Dead && c.Death.Failures == 3);
        c = Ana();
        c.TakeDamage(10, rules);
        c.RollDeathSave(rules, new Rng(SeedFor(1)));
        Assert.True(c.Death.Failures == 2 && !c.Death.Dead);
        c.RollDeathSave(rules, new Rng(SeedFor(20)));
        Assert.True(c.Hp == 1 && c.Death.Failures == 0 && c.Death.Successes == 0 && c.Conditions.Count == 0);
        c.TakeDamage(1, rules);
        c.Heal(2);
        c.SyncDeath(rules);
        Assert.True(c.Hp == 2 && c.Conditions.Count == 0);
        c = Ana();
        c.Death.Saves = false;
        c.TakeDamage(10, rules);
        Assert.True(c.Death.Dead && c.RollDeathSave(rules, random) == null);

        // Without death rules 0 HP is only down, and healing gets it up.
        Ruleset plain = TurnRules(false);
        CharacterSheet legacy = Ana();
        legacy.TakeDamage(10, plain);
        Assert.True(!legacy.Death.Dead && legacy.RollDeathSave(plain, random) == null);
        legacy.Heal(1);
        Assert.Equal(1, legacy.Hp);

        // Reviving is its own step.
        c.Stats.SetBase("maxHp", 10);
        Assert.True(c.Revive(rules, 3) && c.Hp == 3 && !c.Death.Dead && !c.HasCondition("dead") && !c.Revive(rules, 3));
    }

    [Fact]
    public void TheDyingRollOnTheirTurn()
    {
        foreach (bool shared in new[] { false, true })
        {
            Ruleset rules = DeathRules(shared, saveDc: -1000, naturalTwentyHp: 0);
            CharacterSheet[] people = { Ana(), Ana(), Ana() };
            for (int i = 0; i < people.Length; i++)
            {
                people[i].Stats.SetBase("dex", 2000.0f - i * 200);
            }
            people[0].TakeDamage(10, rules);
            var fight = new Encounter(rules, 7);
            fight.Add(people[0], 0);
            fight.Add(people[1], 0);
            fight.Add(people[2], 1);
            fight.Start();
            int Rolled() => people[0].Death.Successes + people[0].Death.Failures;
            Assert.True(ReferenceEquals(fight.Current.Sheet, people[1]) && Rolled() > 0, $"shared {shared}");
            int first = Rolled();
            fight.NextTurn();
            Assert.True(ReferenceEquals(fight.Current.Sheet, people[2]) && Rolled() == first, $"shared {shared}");
            fight.NextTurn();
            Assert.True(fight.Round == 2 && Rolled() > first, $"shared {shared}");
        }
    }

    [Fact]
    public void ACriticalHitOnTheDyingCostsTwoFailures()
    {
        Ruleset rules = DeathRules();
        var host = new TableHost();
        host.Sheets[0] = Ana();
        host.Sheets[1] = Ana();
        host.Sheets[0].TakeDamage(10, rules);
        host.Sheets[0].Death.Failures = 1;
        var context = new EffectContext(rules, new Rng(SeedFor(20))) { Self = 1, Targets = new List<int> { 0 } };
        EffectResult result = RulesTesting.Effect("""[{"do":"roll","kind":"attack","steps":[{"do":"damage","dice":1}]}]""").Run(host, context);
        Assert.True(result.Events.Count > 0 && host.CriticalDamage && host.Sheets[0].Death.Dead);
    }

    [Fact]
    public void AnEffectsAttackRollsTheEncountersDice()
    {
        // The same dice as the encounter's own attack, in the same order, so a fight can move onto
        // effects unchanged.
        Ruleset rules = TurnRules(false);
        Effect strike = RulesTesting.Effect("""
            [{"do":"roll","kind":"attack","steps":[{"do":"damage","dice":"weapon","when":"hit","minimum":1}]}]
            """);
        for (ulong seed = 1; seed <= 40; seed++)
        {
            CharacterSheet MakeAttacker()
            {
                CharacterSheet sheet = RulesTesting.Plain("Ana");
                sheet.Stats.SetBase("str", 16);
                sheet.Weapon = new Weapon("1d8");
                return sheet;
            }
            CharacterSheet attacker = MakeAttacker();
            CharacterSheet defender = RulesTesting.Plain("Gik");
            var fight = new Encounter(rules, seed);
            fight.Add(attacker, 0);
            fight.Add(defender, 1);
            fight.Start();
            int me = fight.CurrentIndex;
            AttackResult expected = fight.Attack(1 - me);

            // The effect, from a fresh copy of the encounter's dice as they stood before that attack.
            var host = new TableHost();
            host.Sheets[0] = MakeAttacker();
            host.Sheets[2] = RulesTesting.Plain("Gik");
            var again = new Encounter(rules, seed);
            again.Add(host.Sheets[0], 0);
            again.Add(host.Sheets[2], 1);
            again.Start();
            int self = ReferenceEquals(again.Order[me].Sheet, host.Sheets[0]) ? 0 : 2;
            var context = new EffectContext(rules, again.Random) { Self = self, Targets = new List<int> { self == 0 ? 2 : 0 } };
            EffectResult result = strike.Run(host, context);
            EffectEvent? attack = result.Events.Find(e => e.Kind == EffectEventKind.Attack);
            EffectEvent? damage = result.Events.Find(e => e.Kind == EffectEventKind.Damage);
            Assert.True(attack != null && attack.Roll.Describe() == expected.AttackRoll.Describe() && attack.Success == expected.Hit
                && attack.Critical == expected.Critical, $"seed {seed}");
            Assert.True((damage != null) == expected.Hit && (damage == null || damage.Roll.Describe() == expected.DamageRoll.Describe()), $"seed {seed}");
        }
    }

    [Fact]
    public void AStrikeCostsAHandEach()
    {
        Ruleset rules = RulesTesting.Rules($$"""{"id":"t","name":"Test",{{Abilities}},"actionsPerTurn":2,"strikeCostsHands":true}""");
        CharacterSheet sheet = RulesTesting.Plain("Ana");
        Assert.Equal(1, sheet.StrikeCost(rules));
        sheet.Weapon = new Weapon("1d12", "str", 2);
        Assert.Equal(2, sheet.StrikeCost(rules));
        sheet.Weapon = new Weapon("1d12", "str", 5);
        Assert.Equal(2, sheet.StrikeCost(rules));
        Ruleset flat = TurnRules(false);
        Assert.Equal(1, sheet.StrikeCost(flat));
    }
}
