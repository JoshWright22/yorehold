namespace Yorehold.Rules.Tests;

/// <summary>The AI scorer on an open field: who it hits, where it stands, when it breaks. The C++ framework's tactics checks.</summary>
public class TacticsTests
{
    private static readonly Grid Field = new(GridType.Square, 64);

    private static TacticalUnit Unit(int team, int x, int y, int hp, int maxHp, int ac, int attack, float damage, int speed = 6)
    {
        return new TacticalUnit
        {
            Team = team, At = new Cell(x, y), Hp = hp, MaxHp = maxHp, ArmorClass = ac, AttackBonus = attack, AverageDamage = damage, Speed = speed,
        };
    }

    // self can walk speed squares (twice that with a dash) and everyone else stands still.
    private static TacticalView OpenField(List<TacticalUnit> units, int self, int speed)
    {
        var view = new TacticalView { Units = units, Self = self };
        TacticalUnit me = units[self];
        for (int y = -5; y < 30; y++)
        {
            for (int x = -5; x < 30; x++)
            {
                var c = new Cell(x, y);
                float nearest = 1e9f;
                bool taken = false;
                for (int i = 0; i < units.Count; i++)
                {
                    taken |= i != self && units[i].At == c;
                    if (units[i].Team != me.Team)
                    {
                        nearest = Math.Min(nearest, Field.Distance(c, units[i].At));
                    }
                }
                view.FoeDistance[c] = nearest;
                float cost = Field.Distance(me.At, c);
                if (taken)
                {
                    continue;
                }
                if (cost <= speed)
                {
                    view.Reach[c] = cost;
                }
                if (cost <= speed * 2)
                {
                    view.DashReach[c] = cost;
                }
            }
        }
        view.SideAtStart = units.Count(u => u.Team == me.Team);
        return view;
    }

    private static TacticalView Copy(TacticalView view)
    {
        return new TacticalView
        {
            Units = new List<TacticalUnit>(view.Units), Self = view.Self, Actions = view.Actions, StrikeCost = view.StrikeCost,
            Reach = new Dictionary<Cell, float>(view.Reach), DashReach = new Dictionary<Cell, float>(view.DashReach),
            FoeDistance = new Dictionary<Cell, float>(view.FoeDistance), SideAtStart = view.SideAtStart, HadLeader = view.HadLeader,
            Fleeing = view.Fleeing, BreakAs = view.BreakAs, AllyDistance = new Dictionary<Cell, float>(view.AllyDistance),
        };
    }

    private static AiProfile Read(string json) => AiProfile.Read(TestContent.Json(json));

    private readonly Rng _random = new(1);
    private readonly AiProfile _mindless = AiProfile.Preset("mindless")!;
    private readonly AiProfile _cunning = AiProfile.Preset("cunning")!;
    private readonly AiProfile _tactical = AiProfile.Preset("tactical")! with { Random = 0 };
    private readonly AiProfile _sure = AiProfile.Preset("mindless")! with { Random = 0 };
    private readonly TacticalUnit _me = Unit(1, 10, 10, 10, 10, 13, 4, 5);
    private readonly TacticalUnit _healthy = Unit(0, 12, 10, 20, 20, 14, 5, 6);

    [Fact]
    public void WhoToHit()
    {
        // A healthy hero two squares away and a nearly dead one five away: the mindless creature
        // takes the near one, the tactical one walks further to finish the wounded.
        TacticalUnit wounded = Unit(0, 10, 15, 2, 20, 14, 5, 6);
        TacticalView twoTargets = OpenField(new List<TacticalUnit> { _me, _healthy, wounded }, 0, 6);
        TacticalChoice dumb = Tactics.Decide(_sure, twoTargets, Field, _random);
        Assert.True(dumb.Kind == ChoiceKind.Attack && dumb.Target == 1 && Field.Distance(dumb.Cell, _healthy.At) <= 1.01f && !dumb.Dash);
        var considered = new List<TacticalChoice>();
        TacticalChoice smart = Tactics.Decide(_tactical, twoTargets, Field, _random, considered);
        Assert.True(smart.Kind == ChoiceKind.Attack && smart.Target == 2 && Field.Distance(smart.Cell, wounded.At) <= 1.01f);
        Assert.True(considered.Count > 2 && considered[0].Score == smart.Score);
        for (int i = 1; i < considered.Count; i++)
        {
            Assert.True(considered[i - 1].Score >= considered[i].Score);
        }

        // The same seed makes the same noisy choice.
        TacticalChoice first = Tactics.Decide(_cunning, twoTargets, Field, new Rng(5));
        TacticalChoice second = Tactics.Decide(_cunning, twoTargets, Field, new Rng(5));
        Assert.Equal(first, second);
    }

    [Fact]
    public void WhenToDashAndWhereToStand()
    {
        // Nine squares off: one action only closes in; two dash and strike; a two-handed swing
        // needs both, so it closes in again.
        TacticalUnit farAway = Unit(0, 19, 10, 20, 20, 14, 5, 6);
        TacticalView distantHero = OpenField(new List<TacticalUnit> { _me, farAway }, 0, 6);
        Assert.Equal(ChoiceKind.Advance, Tactics.Decide(_sure, distantHero, Field, _random).Kind);
        distantHero.Actions = 2;
        TacticalChoice charge = Tactics.Decide(_sure, distantHero, Field, _random);
        Assert.True(charge.Kind == ChoiceKind.Attack && charge.Dash && Field.Distance(charge.Cell, farAway.At) <= 1.01f);
        distantHero.StrikeCost = 2;
        Assert.Equal(ChoiceKind.Advance, Tactics.Decide(_sure, distantHero, Field, _random).Kind);

        // The careful one attacks from a square the second hero isn't next to.
        TacticalUnit left = Unit(0, 12, 10, 20, 20, 14, 5, 6);
        TacticalUnit right = Unit(0, 14, 10, 20, 20, 14, 5, 6);
        TacticalChoice careful = Tactics.Decide(_tactical, OpenField(new List<TacticalUnit> { _me, left, right }, 0, 6), Field, _random);
        Assert.True(careful.Kind == ChoiceKind.Attack && Field.Distance(careful.Cell, careful.Target == 1 ? right.At : left.At) > 1.01f);

        // Nobody in reach: it closes in, dashing to get nearer. With no action left it just walks.
        TacticalUnit far = Unit(0, 25, 10, 20, 20, 14, 5, 6);
        TacticalView distant = OpenField(new List<TacticalUnit> { _me, far }, 0, 6);
        TacticalChoice rush = Tactics.Decide(_sure, distant, Field, _random);
        Assert.True(rush.Kind == ChoiceKind.Advance && rush.Dash && Field.Distance(rush.Cell, far.At) <= 3.01f);
        distant.Actions = 0;
        TacticalChoice walk = Tactics.Decide(_sure, distant, Field, _random);
        Assert.True(walk.Kind == ChoiceKind.Advance && !walk.Dash && Field.Distance(walk.Cell, _me.At) <= 6.01f && Field.Distance(walk.Cell, far.At) <= 9.01f);
    }

    [Fact]
    public void MoraleBreaks()
    {
        // Badly hurt: the cunning one runs (the mindless one never does); once running it keeps
        // running; with nowhere to go it turns and fights.
        TacticalUnit wounded = Unit(0, 10, 15, 2, 20, 14, 5, 6);
        TacticalView twoTargets = OpenField(new List<TacticalUnit> { _me, _healthy, wounded }, 0, 6);
        TacticalUnit hurt = _me with { Hp = 2 };
        TacticalView losing = OpenField(new List<TacticalUnit> { hurt, _healthy }, 0, 6);
        Assert.True(Tactics.WantsToFlee(_cunning, losing) && !Tactics.WantsToFlee(_mindless, losing) && !Tactics.WantsToFlee(_cunning, twoTargets));
        TacticalChoice run = Tactics.Decide(_cunning, losing, Field, _random);
        Assert.True(run.Kind == ChoiceKind.Flee && run.Dash && Field.Distance(run.Cell, _healthy.At) >= 13.9f);
        Assert.Equal(ChoiceKind.Attack, Tactics.Decide(_mindless, losing, Field, _random).Kind);
        TacticalView rallied = OpenField(new List<TacticalUnit> { _me, _healthy }, 0, 6);
        rallied.Fleeing = true;
        Assert.Equal(ChoiceKind.Flee, Tactics.Decide(_cunning, rallied, Field, _random).Kind);
        TacticalView cornered = Copy(losing);
        cornered.Reach = new Dictionary<Cell, float> { [hurt.At] = 0, [new Cell(11, 10)] = 1 };
        cornered.DashReach = new Dictionary<Cell, float>(cornered.Reach);
        Assert.Equal(ChoiceKind.Attack, Tactics.Decide(_cunning, cornered, Field, _random).Kind);

        // Losses and leaders: three of four down breaks the cunning; so does losing the chief.
        TacticalView lastOne = OpenField(new List<TacticalUnit> { _me, _healthy }, 0, 6);
        lastOne.SideAtStart = 4;
        Assert.True(Tactics.WantsToFlee(_cunning, lastOne) && !Tactics.WantsToFlee(_tactical, lastOne));
        TacticalUnit chief = _me with { At = new Cell(9, 10), Leader = true };
        TacticalView led = OpenField(new List<TacticalUnit> { _me, _healthy, chief }, 0, 6);
        led.HadLeader = true;
        Assert.False(Tactics.WantsToFlee(_cunning, led));
        led.Units.RemoveAt(2);
        led.SideAtStart = 2;
        Assert.True(Tactics.WantsToFlee(_cunning, led));
        led.HadLeader = false;
        Assert.False(Tactics.WantsToFlee(_cunning, led));

        // What breaking means, by weight.
        AiProfile yielder = Read("""{"base":"cunning","onBreak":"surrender"}""");
        Assert.True(yielder.OnBreak.Count == 1 && yielder.OnBreak[0].Key == "surrender");
        AiProfile mixed = Read("""{"onBreak":{"flee":3,"surrender":1},"surrenderCornered":true}""");
        Assert.True(mixed.OnBreak.Count == 2 && mixed.SurrenderCornered);
        int fled = 0;
        var picks = new Rng(3);
        for (int i = 0; i < 400; i++)
        {
            fled += Tactics.PickBreak(mixed, picks) == "flee" ? 1 : 0;
        }
        Assert.InRange(fled, 251, 349);

        // Surrender gives up on the spot; cornered with surrenderCornered it gives up rather than
        // fight; "fight" ignores morale altogether.
        Assert.Equal(ChoiceKind.Surrender, Tactics.Decide(yielder, losing, Field, _random).Kind);
        AiProfile backedIn = _cunning with { SurrenderCornered = true };
        Assert.Equal(ChoiceKind.Surrender, Tactics.Decide(backedIn, cornered, Field, _random).Kind);
        TacticalView stubborn = Copy(losing);
        stubborn.BreakAs = "fight";
        Assert.Equal(ChoiceKind.Attack, Tactics.Decide(_cunning, stubborn, Field, _random).Kind);

        // Alarm: it runs toward the allies who aren't fighting yet, not just away. With none left it flees.
        TacticalView alarm = Copy(losing);
        alarm.BreakAs = "alarm";
        var help = new Cell(10, 4);
        foreach (Cell cell in alarm.DashReach.Keys)
        {
            alarm.AllyDistance[cell] = Field.Distance(cell, help);
        }
        alarm.AllyDistance[hurt.At] = Field.Distance(hurt.At, help);
        TacticalChoice warn = Tactics.Decide(_cunning, alarm, Field, _random);
        Assert.True(warn.Kind == ChoiceKind.Alarm && Field.Distance(warn.Cell, help) < Field.Distance(hurt.At, help));
        alarm.AllyDistance.Clear();
        Assert.Equal(ChoiceKind.Flee, Tactics.Decide(_cunning, alarm, Field, _random).Kind);
    }
}
