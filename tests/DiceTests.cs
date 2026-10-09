namespace Yorehold.Rules.Tests;

/// <summary>The seeded generator, dice text and what a roll comes to against a DC.</summary>
public class DiceTests
{
    [Fact]
    public void TheGeneratorIsPcg32()
    {
        // The published PCG32 sample: seed 42, stream 54.
        var reference = new Rng(42, 54);
        uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
        Assert.Equal(expected, expected.Select(_ => reference.Next()).ToArray());

        // What the C++ client's generator gives for the seeds the tests use.
        var seven = new Rng(7);
        Assert.Equal(new uint[] { 3536637593, 1154887489, 2902756104, 1443040102, 3514199328 }, Enumerable.Range(0, 5).Select(_ => seven.Next()).ToArray());
        Assert.Equal((669231770343817572UL, 13005396917011789751UL), (seven.State, seven.Increment));
        var unseeded = new Rng();
        Assert.Equal(new uint[] { 465482994, 3895364073, 1746730475 }, Enumerable.Range(0, 3).Select(_ => unseeded.Next()).ToArray());
    }

    [Fact]
    public void TheSameSeedGivesTheSameRolls()
    {
        var d20 = new Rng(7);
        Assert.Equal(new[] { 14, 10, 5, 3, 9, 16, 2, 13, 18, 7, 13, 12 }, Enumerable.Range(0, 12).Select(_ => d20.Range(1, 20)).ToArray());
        var d6 = new Rng(7);
        Assert.Equal(new[] { 6, 2, 1, 5, 1, 6, 6, 3, 4, 1, 1, 4 }, Enumerable.Range(0, 12).Select(_ => d6.Range(1, 6)).ToArray());
        var signed = new Rng(3);
        Assert.Equal(new[] { -2, 2, 0, 3, 2, 5, 0, 4, 2, 1 }, Enumerable.Range(0, 10).Select(_ => signed.Range(-5, 5)).ToArray());

        Assert.Equal(1389153945, new Rng(7).Range(int.MinValue, int.MaxValue));
        Assert.Equal(4, new Rng(7).Range(4, 4));
        Assert.Throws<ArgumentException>(() => new Rng(7).Range(2, 1));

        DiceExpression expression = DiceExpression.Parse("4d6kh3+2")!;
        var a = new Rng(7);
        var b = new Rng(7);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(Dice.Roll(expression, a).Total, Dice.Roll(expression, b).Total);
        }
        var other = new Rng(8);
        Assert.NotEqual(
            Enumerable.Range(0, 20).Select(_ => a.Range(1, 20)).ToArray(),
            Enumerable.Range(0, 20).Select(_ => other.Range(1, 20)).ToArray());
    }

    [Fact]
    public void ASystemsOwnDieCountsWhatItsFacesSay()
    {
        DiceExpression boost = DiceExpression.Parse("2d{0, 0, 1, 1, 2, -1} + 1")!;
        Assert.Equal("2d{0,0,1,1,2,-1}+1", boost.ToString());
        Assert.Equal((-1, 5), (boost.Minimum(), boost.Maximum()));
        Assert.Equal(2 * 0.5 + 1, boost.Average(), 6);
        var random = new Rng(7);
        for (int i = 0; i < 200; i++)
        {
            RollResult roll = Dice.Roll(boost, random);
            Assert.All(roll.Dice, die => Assert.Contains(die.Value, new[] { 0, 1, 2, -1 }));
            Assert.Equal(roll.Dice.Sum(d => d.Value) + 1, roll.Total);
        }
        // counted exactly: two dice of six faces each, a 2 and a 2 once in 36
        Dictionary<int, double> spread = CheckKind.Spread(DiceExpression.Parse("2d{0,0,1,1,2,-1}")!);
        Assert.Equal(1 / 36.0, spread[4], 9);
        Assert.Equal(1.0, spread.Values.Sum(), 9);
        // successes count faces at or over the number, kept dice the best
        Assert.Equal(2, DiceExpression.Parse("3d{0,1,2}s1")!.Maximum() - 1);
        Assert.Equal(2, Dice.Roll(DiceExpression.Parse("3d{2,2}kh1")!, random).Total);

        foreach (string bad in new[] { "1d{}", "1d{a}", "1d{1,2", "1d{1,2}!", "1d{5000}" })
        {
            Assert.Null(DiceExpression.Parse(bad));
        }
        // in content dice, a list of faces stays a die while a formula in braces is worked out
        string filled = DiceText.Fill("{level}d{0, 1, 2}+{level + 1}", name => name == "level" ? 2 : null);
        Assert.Equal("2d{0,1,2}+3", DiceExpression.Parse(filled)!.ToString());
        Assert.True(DiceText.IsValid("{level}d{0,1,2}"));
        Assert.True(DiceText.IsValid("1d{hands.free >= 1 ? 10 : 8}"));
        Assert.False(DiceText.IsValid("1d{0,1"));
    }

    [Fact]
    public void ASystemsOwnDieIsThrownOnTheSolidItsFacesFit()
    {
        RollResult roll = Dice.Roll(DiceExpression.Parse("1d{0,1,2}")!, new Rng(7));
        DiceFaces.Shown shown = Assert.Single(DiceFaces.Of(roll));
        Assert.Equal("d6", shown.Shape);
        Assert.Equal(new[] { "0", "1", "2", "0", "1", "2" }, shown.Labels);
        Assert.Contains(shown.Face, shown.Labels!);
    }

    [Fact]
    public void ASaveKeepsTheStateAndCarriesOn()
    {
        var first = new Rng(21);
        for (int i = 0; i < 37; i++)
        {
            first.Range(1, 20);
        }
        Rng restored = Rng.Restore(first.State, first.Increment);
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal(first.Range(1, 100), restored.Range(1, 100));
        }
        Assert.Equal((first.State, first.Increment), (restored.State, restored.Increment));
        Assert.Throws<ArgumentException>(() => Rng.Restore(1, 2));
    }

    [Fact]
    public void DiceTextIsParsed()
    {
        DiceExpression expression = DiceExpression.Parse("4d6kh3+2")!;
        Assert.Equal((5, 20), (expression.Minimum(), expression.Maximum()));
        Assert.Equal("4d6kh3+2", expression.ToString());
        Assert.Equal("2d20kl1-1", DiceExpression.Parse(" 2D20 kl1 - 1 ")!.ToString());
        Assert.Equal((-4, 1), (DiceExpression.Parse("1d6-5")!.Minimum(), DiceExpression.Parse("1d6-5")!.Maximum()));
        Assert.Equal((-11, -1), (DiceExpression.Parse("1-2d6")!.Minimum(), DiceExpression.Parse("1-2d6")!.Maximum()));
        Assert.Equal("1d100", DiceExpression.Parse("d%")!.ToString());
        Assert.Equal("-3", DiceExpression.Parse("-3")!.ToString());
        Assert.Equal((7, 7), (DiceExpression.Parse("7")!.Minimum(), DiceExpression.Parse("7")!.Maximum()));

        Assert.Null(DiceExpression.Parse("2d6+"));
        Assert.Null(DiceExpression.Parse("4d6kh5"));
        Assert.Null(DiceExpression.Parse(""));
        Assert.Null(DiceExpression.Parse("lots"));
        Assert.Null(DiceExpression.Parse("2d0"));
        Assert.Null(DiceExpression.Parse("1001d6"));
        Assert.Null(DiceExpression.Parse(string.Join("+", Enumerable.Repeat("1000d100000", 30))));
        Assert.True(DiceText.IsValid("2d6+1"));
        Assert.False(DiceText.IsValid("2d6+"));
    }

    [Fact]
    public void RollsShowEveryDie()
    {
        // The same four rolls the C++ client makes from seed 7, dropped dice and all.
        DiceExpression expression = DiceExpression.Parse("4d6kh3+2")!;
        var random = new Rng(7);
        Assert.Equal("4d6kh3+2: [6, 2, (1), 5] + 2 = 15", Dice.Roll(expression, random).Describe());
        Assert.Equal("4d6kh3+2: [(1), 6, 6, 3] + 2 = 17", Dice.Roll(expression, random).Describe());
        Assert.Equal("4d6kh3+2: [4, 1, (1), 4] + 2 = 11", Dice.Roll(expression, random).Describe());
        Assert.Equal("4d6kh3+2: [(2), 6, 5, 4] + 2 = 17", Dice.Roll(expression, random).Describe());

        RollResult flat = Dice.Roll("3-5", new Rng(1));
        Assert.Equal((-2, -2, 0), (flat.Total, flat.Flat, flat.Dice.Count));
        Assert.Equal("3-5:  - 2 = -2", flat.Describe());

        RollResult bad = Dice.Roll("lots", new Rng(1));
        Assert.Equal(("invalid: lots", 0), (bad.Expression, bad.Total));

        for (ulong seed = 1; seed <= 200; seed++)
        {
            RollResult roll = Dice.Roll("2d6+3", new Rng(seed));
            Assert.InRange(roll.Total, 5, 15);
            Assert.Equal(roll.Total, roll.Dice.Sum(die => die.Value) + 3);
        }
    }

    [Fact]
    public void AD20RollTakesAModifierAndAdvantage()
    {
        RollResult plain = Dice.RollD20(2, Advantage.None, new Rng(7));
        Assert.Equal("1d20+2: [14] + 2 = 16", plain.Describe());
        RollResult worse = Dice.RollD20(-1, Advantage.None, new Rng(7));
        Assert.Equal("1d20-1: [14] - 1 = 13", worse.Describe());
        // Seed 7 rolls 14 then 10.
        Assert.Equal("2d20kh1: [14, (10)] = 14", Dice.RollD20(0, Advantage.Advantage, new Rng(7)).Describe());
        Assert.Equal("2d20kl1: [(14), 10] = 10", Dice.RollD20(0, Advantage.Disadvantage, new Rng(7)).Describe());
    }

    [Fact]
    public void ANaturalOneOrTwentyDecidesTheDegree()
    {
        bool sawOne = false;
        bool sawTwenty = false;
        for (ulong seed = 1; seed <= 400; seed++)
        {
            RollResult roll = Dice.RollD20(5, Advantage.None, new Rng(seed));
            int die = roll.Dice[0].Value;
            Assert.Equal(die == 20, roll.Natural20);
            Assert.Equal(die == 1, roll.Natural1);
            // DC 1 is passed by any total and DC 30 by none, so only the die itself can turn them.
            Degree easy = Checks.DegreeOf(roll, 1);
            Degree hard = Checks.DegreeOf(roll, 30);
            Assert.Equal(die == 1 ? Degree.CriticalFailure : die == 20 ? Degree.CriticalSuccess : Degree.Success, easy);
            Assert.Equal(die == 1 ? Degree.CriticalFailure : die == 20 ? Degree.CriticalSuccess : Degree.Failure, hard);
            Assert.Equal(die != 1, Checks.Passed(easy));
            Assert.Equal(die == 20, Checks.Passed(hard));
            Assert.Equal(die is 1 or 20, Checks.Critical(easy));
            if (die is not (1 or 20))
            {
                Assert.Equal(roll.Total >= 15 ? Degree.Success : Degree.Failure, Checks.DegreeOf(roll, 15));
            }
            sawOne |= die == 1;
            sawTwenty |= die == 20;
        }
        Assert.True(sawOne && sawTwenty);

        // Meeting the DC is enough; a flat number has no natural 1 or 20.
        RollResult fifteen = Dice.Roll("15", new Rng(1));
        Assert.Equal(Degree.Success, Checks.DegreeOf(fifteen, 15));
        Assert.Equal(Degree.Failure, Checks.DegreeOf(fifteen, 16));
        Assert.False(fifteen.Natural1 || fifteen.Natural20);
    }
}
