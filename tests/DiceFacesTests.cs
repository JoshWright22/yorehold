namespace Yorehold.Rules.Tests;

public class DiceFacesTests
{
    [Fact]
    public void EachDieShowsTheFaceItRolled()
    {
        var roll = new RollResult
        {
            Dice =
            {
                new DieRoll(20, 17), new DieRoll(20, 4, false), new DieRoll(10, 10), new DieRoll(100, 37),
                new DieRoll(3, -1, true, true), new DieRoll(6, 9), new DieRoll(7, 5),
            },
        };
        List<DiceFaces.Shown> shown = DiceFaces.Of(roll);
        Assert.Equal(new[] { ("d20", "17", true), ("d20", "4", false), ("d10", "0", true), ("d10t", "30", true), ("d10", "7", true),
            ("dF", "-", true), ("d6", "6", true), ("d20", "5", true) }, shown.Select(s => (s.Shape, s.Face, s.Kept)));
        // every face shown is one the shape has
        Assert.All(shown, s => Assert.Contains(s.Face, DiceFaces.Labels(s.Shape)));
        Assert.Equal(20, DiceFaces.Labels("d20").Count);
    }

    [Fact]
    public void AnAttackThrowsItsDiceToTheScreen()
    {
        using WorldFixture world = WorldFixture.Load("chapters/goblin-keep", 3);
        World w = world.World;
        world.Fight();
        // play until someone rolls
        world.World.Options.AutoPlay = true;
        Assert.True(world.StepUntil(() => world.Events.Any(e => e.Kind == WorldEventKind.Dice), 60));
        WorldEvent dice = world.Events.First(e => e.Kind == WorldEventKind.Dice);
        Assert.NotNull(dice.Roll);
        Assert.NotEmpty(dice.Roll!.Dice);
    }
}
