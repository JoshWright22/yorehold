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

    [Theory]
    [InlineData("d4", 4, 3)]
    [InlineData("d6", 6, 4)]
    [InlineData("d8", 8, 3)]
    [InlineData("d10", 10, 4)]
    [InlineData("d12", 12, 5)]
    [InlineData("d20", 20, 3)]
    [InlineData("dF", 6, 4)]
    public void EachSolidHasAFacePerLabel(string shape, int faces, int corners)
    {
        DiceSolids.Solid solid = DiceSolids.Of(shape);
        Assert.Equal(faces, solid.Faces.Count);
        Assert.Equal(DiceFaces.Labels(shape).Count, solid.Faces.Count);
        Assert.All(solid.Faces, face => Assert.Equal(corners, face.Length));
        // flat faces, normals outward and all different, so each face can be turned up alone
        for (int f = 0; f < solid.Faces.Count; f++)
        {
            System.Numerics.Vector3 n = solid.Normals[f];
            float[] along = solid.Faces[f].Select(i => System.Numerics.Vector3.Dot(solid.Vertices[i], n)).ToArray();
            Assert.True(along.Max() - along.Min() < 1e-3f, $"{shape} face {f} is flat");
            Assert.True(along[0] > 0, $"{shape} face {f} faces outward");
        }
        Assert.Equal(solid.Normals.Count, solid.Normals.Select(n => (MathF.Round(n.X, 3), MathF.Round(n.Y, 3), MathF.Round(n.Z, 3))).Distinct().Count());
    }

    [Theory]
    [InlineData("d4")]
    [InlineData("d6")]
    [InlineData("d8")]
    [InlineData("d10")]
    [InlineData("d12")]
    [InlineData("d20")]
    public void ADieThrownAcrossTheTableComesToRestFlat(string shape)
    {
        var table = new DiceTumble.Table(4, 1.2f);
        DiceSolids.Solid solid = DiceSolids.Of(shape);
        for (int seed = 1; seed <= 8; seed++)
        {
            DiceTumble.Path path = DiceTumble.Throw(shape, 0.3f, table, 0, seed);
            Assert.Equal(path.Face, DiceTumble.Throw(shape, 0.3f, table, 0, seed).Face);
            Assert.Equal(path.Frames.Select(f => f.Position), DiceTumble.Throw(shape, 0.3f, table, 0, seed).Frames.Select(f => f.Position));
            DiceTumble.Frame first = path.Frames[0], last = path.Frames[^1];
            Assert.True(last.Position.X > first.Position.X + 2, $"{shape} {seed}: it travels across the table");
            Assert.True(Math.Abs(last.Position.X) <= table.HalfWidth && Math.Abs(last.Position.Z) <= table.HalfDepth, $"{shape} {seed}: it stays on the table");
            // flat: the face it is read by points straight up (a d4's straight down)
            float y = System.Numerics.Vector3.Transform(solid.Normals[path.Face], last.Rotation).Y;
            Assert.True(shape == "d4" ? y < -0.99f : y > 0.99f, $"{shape} {seed}: rests flat ({y})");
            Assert.True(path.Frames.Count < 60 * 5, $"{shape} {seed}: settles in time");
        }
    }

    [Fact]
    public void EachActionPlaysTheAnimationThatFitsIt()
    {
        ContentFiles files = TestContent.Shipped();
        RulesFolder rules = RulesFolder.Load(files, "rulesets/dnd5e");
        var compendium = new Compendium();
        compendium.Load(files, "rulesets/dnd5e", "");
        compendium.LoadOptions(files, "rulesets/dnd5e");
        string Weapon(string action, string item) => UiAnimation.For(rules.Action(action)!, false, compendium.Items[item]);
        string Spell(string id) => UiAnimation.For(compendium.Spells[id].Action, true, null);
        Assert.Equal(("slash", "thrust", "bash", "arrow", "claw"),
            (Weapon("attack", "longsword"), Weapon("attack", "rapier"), Weapon("attack", "mace"), Weapon("attack", "longbow"), Weapon("attack", "bite")));
        Assert.Equal(("burst", "cone", "bolt", "heal", "debuff", "buff"),
            (Spell("fireball"), Spell("burning-hands"), Spell("fire-bolt"), Spell("cure-wounds"), Spell("hold-person"), Spell("bless")));
        // every set it can pick ships, and each loads
        foreach (string id in new[] { "slash", "thrust", "bash", "claw", "arrow", "thrown", "bolt", "burst", "cone", "touch", "heal", "buff", "debuff" })
        {
            UiAnimation set = UiAnimation.Read(ContentNode.Read(files, $"{UiAnimation.Folder}/{id}.json"));
            Assert.True(set.Impact <= set.Seconds && set.Hit.Count > 0, id);
        }
    }

    [Fact]
    public void DiceThrownTogetherDontEndInsideEachOther()
    {
        var table = new DiceTumble.Table(4, 1.6f);
        string[] shapes = { "d20", "d6", "d8", "d20", "d12", "d4" };
        float[] lanes = { -1.2f, -0.7f, -0.2f, 0.3f, 0.8f, 1.3f };
        for (int seed = 1; seed <= 5; seed++)
        {
            List<DiceTumble.Path> paths = DiceTumble.ThrowAll(shapes, 0.42f, table, lanes, seed);
            for (int a = 0; a < paths.Count; a++)
            {
                for (int b = a + 1; b < paths.Count; b++)
                {
                    float apart = System.Numerics.Vector3.Distance(paths[a].Frames[^1].Position, paths[b].Frames[^1].Position);
                    Assert.True(apart > 0.42f * 1.5f, $"seed {seed}: dice {a} and {b} end {apart} apart");
                }
            }
        }
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

    [Fact]
    public void ASecretActionsRollIsNotThrown()
    {
        using WorldFixture world = WorldFixture.LoadJson("chapters/secret", new Dictionary<string, string>
        {
            ["chapters/secret/chapter.json"] = """
                {"id":"secret","title":"Secret","map":"map.json","party":[{"name":"Ana","class":"fighter","at":[0,0]}],"encounters":[{"id":"e","creatures":[{"creature":"goblin","name":"Gik","at":[3,3]}]}]}
                """,
            ["chapters/secret/map.json"] = """
                {"name":"Room","tiles":{"floor":{"art":"grass"}},"legend":{".":"floor"},"layers":[{"name":"ground","rows":["....","....","....","...."]}]}
                """,
            ["rulesets/yorehold/actions/sense.json"] = """
                {"id": "sense", "name": "Sense", "secret": true, "target": {"kind": "self"},
                 "effects": [{"do": "roll", "kind": "check", "ability": "wis", "dc": 10, "steps": [{"do": "heal", "dice": "1", "when": "success"}]}]}
                """,
        }, 3);
        world.Fight();
        Assert.True(world.TurnTo(0) && world.Use("sense", 0), world.World.Refusal);
        Assert.DoesNotContain(world.Events, e => e.Kind == WorldEventKind.Dice);
    }

    [Fact]
    public void ADieRolledInTheOpenIsThrownAndSaid()
    {
        using WorldFixture world = WorldFixture.Load("chapters/goblin-keep", 3);
        RollResult roll = world.World.RollInOpen(0, 20);
        Assert.InRange(roll.Total, 1, 20);
        Assert.True(world.Said($"rolls d20: {roll.Total}"));
        WorldEvent thrown = Assert.Single(world.Events, e => e.Kind == WorldEventKind.Dice && e.Text == "open");
        Assert.Single(DiceFaces.Of(thrown.Roll!));
        // no one acted on anyone, so no blow plays after it
        Assert.Equal(-1, thrown.By);
    }

    [Fact]
    public void EachAttackOfAMultiattackIsSaidBeforeTheNextOnesDice()
    {
        using WorldFixture world = WorldFixture.Load("chapters/dnd5e-test", 1);
        Assert.True(world.StepUntil(() => world.Log.Count(l => l.StartsWith("Vex attacks", StringComparison.Ordinal)) >= 2, 30), "The captain attacks twice");
        var attacks = world.Events.Select((e, i) => (e, i)).Where(p => p.e.Kind == WorldEventKind.Dice && p.e.Text == "attack" && p.e.Action.Length > 0).Select(p => p.i).ToList();
        int firstLine = world.Events.FindIndex(e => e.Kind == WorldEventKind.Log && e.Text.StartsWith("Vex attacks", StringComparison.Ordinal));
        Assert.True(attacks.Count >= 2 && attacks[0] < firstLine && firstLine < attacks[1],
            "The first attack's line comes after its dice and before the second attack's, so the screen can show one beat per attack");
    }

    [Fact]
    public void ACheckRolledFromTheSheetUsesTheHerosModifier()
    {
        using WorldFixture world = WorldFixture.Load("chapters/dnd5e-test", 2);
        World w = world.World;
        CharacterSheet ana = w.Creatures[0].Sheet;
        RollResult roll = w.RollCheckInOpen(0, "athletics")!;
        Assert.Equal(roll.Dice.Sum(d => d.Kept ? d.Value : 0) + ana.CheckModifier(w.Rules, "athletics"), roll.Total);
        Assert.True(world.Said($"Ana rolls Athletics: "), "The roll is said in the log by its skill's name");
        RollResult save = w.RollCheckInOpen(0, "save:str")!;
        Assert.Equal(save.Dice.Sum(d => d.Kept ? d.Value : 0) + ana.SaveModifier(w.Rules, "str"), save.Total);
        Assert.Null(w.RollCheckInOpen(0, "juggling"));
        Assert.True(world.Said("Ana rolls Strength save: "));
        Assert.Equal(2, world.Events.Count(e => e.Kind == WorldEventKind.Dice && e.Text == "open"));
    }

    [Fact]
    public void TheAimsOddsAreTheActionsOwnAttack()
    {
        using WorldFixture world = WorldFixture.Load("chapters/dnd5e-test", 2);
        World w = world.World;
        Assert.False(w.Fighting);
        CharacterSheet cy = w.Creatures[2].Sheet;
        int vex = Enumerable.Range(0, w.Creatures.Count).First(i => w.Creatures[i].Sheet.Name == "Vex");
        CheckKind attack = w.Rules.Checks.Kind(CheckRules.Attack);
        int ac = w.Creatures[vex].Sheet.AttackDefence(w.Rules);
        Assert.Equal((float)attack.ChanceToPass(cy.SpellAttackModifier(w.Rules), ac, Advantage.None), w.HitChance(2, vex, "fire-bolt"), 4);
        Assert.Equal((float)attack.ChanceToPass(cy.AttackModifier(w.Rules), ac, Advantage.None), w.HitChance(2, vex), 4);
    }
}
