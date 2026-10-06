using System.Numerics;

namespace Yorehold.Rules.Tests;

/// <summary>Areas on the map, slots and hands, and concentration. The C++ framework's SpellTests and its area template checks.</summary>
public class SpellTests
{
    private static SpellDefinition Spell(string json) => SpellDefinition.Read(TestContent.Json(json));

    [Fact]
    public void AreasStartAtTheDoerOrSitOnTheAim()
    {
        ActionDefinition cone = ActionDefinition.Read(TestContent.Json("""
            {"id":"fan","target":{"kind":"point","side":"any"},"area":{"shape":"cone","size":3},"effects":[{"do":"damage","dice":1}]}
            """));
        ActionDefinition burst = ActionDefinition.Read(TestContent.Json("""{"id":"ring","area":{"shape":"burst","size":1},"target":{"kind":"self","side":"enemy"}}"""));
        ActionDefinition line = ActionDefinition.Read(TestContent.Json("""{"id":"bolt","target":{"kind":"creature","range":6},"area":{"shape":"line","size":6,"width":1}}"""));
        var grid = new Grid(GridType.Square, 10);
        Vector2 from = grid.Center(new Cell(5, 5));
        List<Cell> fan = AreaTemplate.Place(cone.Area!, grid, from, grid.Center(new Cell(8, 5))).Cells(grid);
        Assert.True(fan.Contains(new Cell(6, 5)) && fan.Contains(new Cell(7, 5)) && fan.Contains(new Cell(8, 5))
            && !fan.Contains(new Cell(9, 5)) && !fan.Contains(new Cell(4, 5)) && !fan.Contains(new Cell(6, 7)) && !fan.Contains(new Cell(8, 7)));
        List<Cell> ring = AreaTemplate.Place(burst.Area!, grid, from, grid.Center(new Cell(8, 5))).Cells(grid);
        Assert.True(ring.Count == 5 && ring.Contains(new Cell(8, 5)) && ring.Contains(new Cell(8, 4)) && ring.Contains(new Cell(7, 5))
            && !ring.Contains(new Cell(7, 4)) && !ring.Contains(new Cell(5, 5)));
        List<Cell> bolt = AreaTemplate.Place(line.Area!, grid, from, grid.Center(new Cell(5, 2))).Cells(grid);
        Assert.True(bolt.Contains(new Cell(5, 4)) && bolt.Contains(new Cell(5, 0)) && !bolt.Contains(new Cell(5, -2))
            && !bolt.Contains(new Cell(6, 3)) && !bolt.Contains(new Cell(5, 6)));
        Assert.Equal(9, AreaTemplate.Place(new ActionArea(AreaShape.Square, 3), grid, from, grid.Center(new Cell(2, 2))).Cells(grid).Count);
        // aimed at the square it stands on, a cone still points somewhere
        Assert.NotEmpty(AreaTemplate.Place(cone.Area!, grid, from, from).Cells(grid));

        // a cell is in when its centre is: a two-cell burst on a corner covers twelve
        var corner = new AreaTemplate { Shape = AreaShape.Burst, Origin = new Vector2(50, 50), Size = 20 };
        Assert.Equal(12, corner.Cells(grid).Count);
        Assert.Equal(4, new AreaTemplate { Shape = AreaShape.Square, Origin = new Vector2(50, 50), Size = 20 }.Cells(grid).Count);
        var straight = new AreaTemplate { Shape = AreaShape.Line, Origin = new Vector2(5, 5), Direction = new Vector2(195, 0), Size = 50 };
        Assert.True(straight.Cells(grid).Count == 6 && straight.Cells(grid).All(c => c.Y == 0));
        var wedge = new AreaTemplate { Shape = AreaShape.Cone, Origin = new Vector2(0, 50), Direction = new Vector2(100, 0), Size = 30 };
        Assert.True(wedge.Cells(grid).All(c => grid.Center(c).X >= 0 && grid.Center(c).X <= 30));
        Assert.True(wedge.Contains(new Vector2(25, 50), 10) && !wedge.Contains(new Vector2(-5, 50), 10) && !wedge.Contains(new Vector2(10, 70), 10));
    }

    [Fact]
    public void SlotsAndHands()
    {
        SpellDefinition cantrip = Spell("""{"id":"spark","effects":[{"do":"damage","dice":1}]}""");
        SpellDefinition first = Spell("""{"id":"fan","level":1,"hands":2,"effects":[{"do":"damage","dice":1}]}""");
        var rules = new SpellRules();
        var caster = new CharacterSheet();
        caster.Resources["slots-1"] = new Resource(1, 2);
        caster.Resources["slots-2"] = new Resource(1, 1);

        // cantrips spend nothing; a spell takes the lowest slot that is left
        Assert.True(Spellcasting.SlotFor(caster, cantrip, rules) == 0 && Spellcasting.SpendSlot(caster, rules, 0) && caster.Resources["slots-1"].Current == 1);
        Assert.True(Spellcasting.SlotFor(caster, first, rules) == 1 && Spellcasting.CanCast(caster, first, rules, out _));
        Assert.True(Spellcasting.SpendSlot(caster, rules, 1) && caster.Resources["slots-1"].Current == 0 && !Spellcasting.SpendSlot(caster, rules, 1));
        Assert.True(Spellcasting.SlotFor(caster, first, rules) == 2 && Spellcasting.SlotFor(caster, first, rules, 2) == 2
            && Spellcasting.SlotFor(caster, first, rules, 1) == null && Spellcasting.SlotFor(caster, first, rules, 3) == null);
        var exact = new SpellRules { Upcast = false };
        Assert.True(Spellcasting.SlotFor(caster, first, exact) == null && !Spellcasting.CanCast(caster, first, exact, out string why)
            && why == "no spell slot left" && Spellcasting.CanCast(caster, cantrip, exact, out _));
        Assert.True(Spellcasting.SpendSlot(caster, rules, 2) && Spellcasting.SlotFor(caster, first, rules) == null);
        var mana = new SpellRules { SlotPrefix = "mana-" };
        caster.Resources["mana-1"] = new Resource(1, 1);
        Assert.Equal(1, Spellcasting.SlotFor(caster, first, mana));

        // hands: what is held is in the way, where the rules say so
        caster.Inventory.Add(new Item(new ItemDefinition { Id = "staff", Slot = "mainHand" }));
        Assert.True(caster.FreeHands == 2 && caster.Equip(0) && caster.FreeHands == 1);
        Assert.True(Spellcasting.CanCast(caster, cantrip, mana, out _) && !Spellcasting.CanCast(caster, first, mana, out why) && why == "needs 2 free hands");
        Assert.True(Spellcasting.CanCast(caster, first, new SpellRules { Hands = SpellHands.Ignored, SlotPrefix = "mana-" }, out _));

        // a focus spell needs no slot and is refused with an empty pool
        SpellDefinition dart = Spell("""{"id":"dart","level":1,"spends":{"focus":1},"effects":[{"do":"damage","dice":"1d4"}]}""");
        caster.Resources["focus"] = new Resource(1, 1);
        caster.Unequip(0);
        Assert.True(Spellcasting.SlotFor(caster, dart, rules) == 0 && Spellcasting.CanCast(caster, dart, rules, out _));
        Assert.True(Spellcasting.SpendCasting(caster, dart, rules, 0) && caster.Resources["focus"].Current == 0);
        Assert.True(!Spellcasting.CanCast(caster, dart, rules, out why) && why == "needs 1 focus" && !Spellcasting.SpendCasting(caster, dart, rules, 0));

        Assert.True(Spellcasting.ConcentrationDc(rules, 4) == 10 && Spellcasting.ConcentrationDc(rules, 31) == 15);
    }

    [Fact]
    public void ConcentrationHoldsWhatOneCastingLeft()
    {
        Ruleset rules = CharacterTests.Modern();
        rules.Conditions.Add(ConditionDefinition.Read(TestContent.Json("""{"id":"mired"}""")));
        var host = new TableHost { Area = new List<int> { 1, 2 } };
        var random = new Rng(11);
        var context = new EffectContext(rules, random) { Self = 0, Targets = host.Area, Source = "mire" };
        Effect effect = RulesTesting.Effect("""
            [{"do":"condition","id":"mired","duration":3,"target":"area"},{"do":"modifier","stat":"ac","value":-1,"duration":3},{"do":"damage","dice":2}]
            """);
        EffectResult result = effect.Run(host, context);
        Concentration held = Concentration.Begin("mire", result);
        Assert.True(held.Active && held.Spell == "mire" && held.Holds.Count == 4 && host.Sheets[1].HasCondition("mired")
            && host.Sheets[2].HasCondition("effect:mire") && host.Sheets[1].Hp == 98);

        // what has run out or been taken off is forgotten; the rest comes off when it ends
        Func<int, CharacterSheet?> sheets = host.Sheet;
        host.Sheets[1].RemoveCondition("mired");
        Assert.True(held.Tidy(sheets) && held.Holds.Count == 3);
        List<Concentration.Hold> removed = held.End(sheets);
        Assert.True(removed.Count == 3 && !held.Active && held.Holds.Count == 0 && !host.Sheets[2].HasCondition("mired")
            && !host.Sheets[1].HasCondition("effect:mire") && !host.Sheets[2].HasCondition("effect:mire"));
        Assert.Equal(host.Sheets[0].ArmorClass(rules), host.Sheets[1].ArmorClass(rules));
        // a spell that leaves nothing behind has nothing to concentrate on
        Concentration nothing = Concentration.Begin("zap", new EffectResult());
        Assert.True(nothing.Active && !nothing.Tidy(sheets));

        // damage: a save against the larger of the floor and a share of the damage
        var spells = new SpellRules();
        CharacterSheet caster = host.Sheets[0];
        Assert.True(!Spellcasting.CheckConcentration(caster, rules, spells, 0, random).Rolled && Spellcasting.CheckConcentration(caster, rules, spells, 0, random).Kept);
        caster.Stats.SetBase("con", 1000);
        ConcentrationCheck sure = Spellcasting.CheckConcentration(caster, rules, spells, 60, random);
        Assert.True(sure.Rolled && sure.Kept && sure.Dc == 30 && sure.Roll.Total >= 30);
        ConcentrationCheck lost = Spellcasting.CheckConcentration(caster, rules, new SpellRules { MinimumDc = 600 }, 1, random);
        Assert.True(lost.Rolled && !lost.Kept && lost.Dc == 600);
        var breaks = new SpellRules { OnDamage = ConcentrationDamage.Breaks };
        Assert.True(!Spellcasting.CheckConcentration(caster, rules, breaks, 1, random).Rolled && !Spellcasting.CheckConcentration(caster, rules, breaks, 1, random).Kept);
        Assert.True(Spellcasting.CheckConcentration(caster, rules, new SpellRules { OnDamage = ConcentrationDamage.Ignored }, 100, random).Kept);
    }
}
