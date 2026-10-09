namespace Yorehold.Rules.Tests;

public class FightForecastTests
{
    [Fact]
    public void AnEncounterPlayedManyTimesGivesTheSameForecast()
    {
        ContentFiles files = TestContent.Shipped();
        World Load(ulong seed) => World.Load(files, "chapters/goblin-keep", seed);
        FightForecast forecast = FightSimulation.Forecast(Load, 0, 6);
        Assert.Equal(6, forecast.Fights);
        Assert.Equal(0, forecast.Unfinished);
        Assert.True(forecast.AverageRounds >= 1, forecast.Summary());
        Assert.InRange(forecast.WinChance, 0, 1);
        // every roll comes from the seed, so the same seeds forecast the same
        Assert.Equal(forecast, FightSimulation.Forecast(Load, 0, 6));

        Assert.Equal("this fight: won 9 in 10, 1 in 5 lose a hero, 3 rounds", new FightForecast(10, 9, 2, 0, 3, 0).Summary());
        Assert.Equal("this fight: won always, no hero lost, 2.5 rounds", new FightForecast(4, 4, 0, 0, 2.5, 0).Summary());

        // what it says about the fight for the party
        Assert.StartsWith("too hard", new FightForecast(10, 5, 6, 1, 4, 0).Verdict());
        Assert.StartsWith("too easy", new FightForecast(10, 10, 0, 0, 1.5, 0).Verdict());
        Assert.Equal("", new FightForecast(10, 9, 2, 0, 3, 0).Verdict());
    }

    [Fact]
    public void AFightTooHardForThePartyIsFittedByTakingFoesOut()
    {
        // one hero against three goblin bosses: the fit takes some out, never all
        using var scratch = new Scratch();
        scratch.Write("chapters/crowd/chapter.json", """
            {"id":"crowd","title":"Crowd","map":"map.json","party":[{"name":"Ana","class":"fighter","at":[1,1]}],
             "encounters":[{"id":"crowd","creatures":[{"creature":"goblin-boss","at":[4,4]},{"creature":"goblin-boss","at":[5,4]},{"creature":"goblin-boss","at":[4,5]}]}]}
            """);
        scratch.Write("chapters/crowd/map.json", """
            {"name":"Room","tiles":{"floor":{"art":"grass"}},"legend":{".":"floor"},"layers":[{"name":"ground","rows":["........","........","........","........","........","........","........","........"]}]}
            """);
        ContentFiles files = TestContent.ShippedWith(scratch);
        World Load(ulong seed) => World.Load(files, "chapters/crowd", seed);
        (int leaveOut, FightForecast fitted) = FightSimulation.Fit(Load, 0, 2);
        Assert.InRange(leaveOut, 1, 2);
        Assert.True(leaveOut == 2 || !fitted.Verdict().StartsWith("too hard"), fitted.Summary());
    }

    [Fact]
    public void AFightTooEasyIsGrownWithMoreOfItsLastFoe()
    {
        // three heroes against one goblin: more goblins beside it, in a copy of the chapter
        using var scratch = new Scratch();
        const string chapter = """
            {"id":"lone","title":"Lone","map":"map.json","party":[{"name":"Ana","class":"fighter","at":[1,1]},{"name":"Bo","class":"fighter","at":[1,2]},{"name":"Cy","class":"fighter","at":[1,3]}],
             "encounters":[{"id":"lone","creatures":[{"creature":"goblin","name":"Snik","at":[5,5]}]}]}
            """;
        scratch.Write("chapters/lone/chapter.json", chapter);
        scratch.Write("chapters/lone/map.json", """
            {"name":"Room","tiles":{"floor":{"art":"grass"}},"legend":{".":"floor"},"layers":[{"name":"ground","rows":["........","........","........","........","........","........","........","........"]}]}
            """);
        using var bench = new Scratch();
        (int more, FightForecast grown, List<Cell> at) = FightSimulation.Grow(() => TestContent.ShippedWith(scratch), "chapters/lone", 0, 2, bench.Folder);
        Assert.InRange(more, 1, 4);
        Assert.Equal(more, at.Count);
        Assert.Equal(more, at.Distinct().Count());
        Assert.DoesNotContain(new Cell(5, 5), at);
        Assert.Equal(2, grown.Fights);
        // the chapter itself is as it was
        Assert.Equal(chapter, File.ReadAllText(Path.Combine(scratch.Folder, "chapters", "lone", "chapter.json")));

        // the same party at another level, from a copy of the chapter
        using var level = new Scratch();
        ContentFiles atFive = FightSimulation.AtLevel(() => TestContent.ShippedWith(scratch), "chapters/lone", 5, level.Folder)!;
        World five = World.Load(atFive, "chapters/lone", 1);
        Assert.Equal(5, five.Creatures[0].Sheet.Level);
        Assert.Equal(1, World.Load(TestContent.ShippedWith(scratch), "chapters/lone", 1).Creatures[0].Sheet.Level);
        Assert.Equal(chapter, File.ReadAllText(Path.Combine(scratch.Folder, "chapters", "lone", "chapter.json")));
    }
}
