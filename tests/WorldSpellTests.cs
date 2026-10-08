namespace Yorehold.Rules.Tests;

/// <summary>Spells in a World: casting in and out of fights, slots, aiming areas, concentration, preparing, focus and the starter lists. The C++ client's WorldSpellTests.</summary>
public class WorldSpellTests
{
    // A wizard, a cleric and a fighter in a long hall with three goblins: two in a row to the
    // east, one off to the south.
    public static Dictionary<string, string> HallFiles()
    {
        return new Dictionary<string, string>
        {
            ["chapters/spell-hall/chapter.json"] = """
                {"id":"spell-hall","title":"Hall","map":"map.json",
                 "party":[{"name":"Wiz","class":"wizard","at":[3,3]},{"name":"Bo","class":"cleric","at":[3,4]},{"name":"Ana","class":"fighter","at":[2,3]}],
                 "encounters":[{"id":"hall","creatures":[{"creature":"goblin","name":"Gik","at":[5,3]},
                 {"creature":"goblin","name":"Nok","at":[6,3]},{"creature":"goblin","name":"Zed","at":[3,6]}]}]}
                """,
            ["chapters/spell-hall/map.json"] = """
                {"name":"Hall","tiles":{"floor":{"art":"grass"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
                 "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":["##############","#............#","#............#",
                 "#............#","#............#","#............#","#............#","##############"]}]}
                """,
        };
    }

    public static WorldFixture Hall(ulong seed, Action<Dictionary<string, string>>? change = null)
    {
        Dictionary<string, string> files = HallFiles();
        change?.Invoke(files);
        return WorldFixture.LoadJson("chapters/spell-hall", files, seed);
    }

    private static void EmptyHands(CharacterSheet sheet)
    {
        for (int i = 0; i < sheet.Inventory.Count; i++)
        {
            if (sheet.Inventory[i].Equipped && sheet.Inventory[i].Held)
            {
                sheet.Unequip(i);
            }
        }
    }

    // Tough goblins that always fail the wizard's saves, with the wizard first in the order.
    private static void Prepare(World w)
    {
        CharacterSheet wizard = w.Creatures[0].Sheet;
        wizard.Stats.SetBase("dex", 1000);
        wizard.Stats.SetBase("int", 1000);
        wizard.Stats.SetBase("maxHp", 500);
        wizard.Hp = 500;
        for (int goblin = 3; goblin < 6; goblin++)
        {
            w.Creatures[goblin].Sheet.Stats.SetBase("maxHp", 100);
            w.Creatures[goblin].Sheet.Hp = 100;
        }
    }

    private static bool StartFight(WorldFixture world)
    {
        world.Fight();
        return world.World.CurrentCreature == 0;
    }

    private static CharacterSheet Sheet(World w, int who) => w.Creatures[who].Sheet;

    private static int Slots(World w, int who, int level) => Sheet(w, who).Resources[$"slots-{level}"].Current;

    [Fact]
    public void BetweenFights()
    {
        using WorldFixture world = Hall(5);
        World w = world.World;
        Assert.Equal(new[] { "spark", "chill-bite", "arcane-dart", "flame-fan", "mire" }, Sheet(w, 0).Spells);
        Assert.Equal(new[] { "rebuke", "kind-word", "shield-of-faith", "mend", "brand-of-light" }, Sheet(w, 1).Spells);
        Assert.True(Sheet(w, 2).Spells.Count == 0 && Sheet(w, 0).Resources["slots-1"].Max == 2, "Casters start with their class's spells and slots");
        List<ActionDefinition> has = w.ActionsOf(0);
        Assert.True(ReferenceEquals(w.FindAction("mire"), w.FindSpell("mire")!.Action) && has.Contains(w.FindAction("spark")!)
            && has[^1].Id == "end-turn" && w.ActionsOf(2).Count + 5 == has.Count, "A caster's spells are listed with its actions");

        Sheet(w, 2).Hp = 1;
        Assert.True(!w.Cast(1, "mend", 2) && w.Refusal.Contains("free hand"), "A mace and a shield leave no hand to cast with");
        EmptyHands(Sheet(w, 1));
        Assert.True(!w.Cast(0, "spark", 2) && !w.Cast(0, "spark", 3) && !w.Cast(0, "flame-fan", 0) && !w.Cast(0, "mend", 2),
            "Harmful spells wait for a fight and nobody casts a spell they don't know");
        w.Place(2, new Cell(9, 5));
        Assert.True(!w.Cast(1, "mend", 2) && Slots(w, 1, 1) == 2, "A touch spell out of reach spends no slot");
        w.Place(2, new Cell(2, 3));
        Assert.True(w.Cast(1, "mend", 2));
        Assert.True(Sheet(w, 2).Hp >= 4 && Sheet(w, 2).Hp <= 11 && Slots(w, 1, 1) == 1, "Mend heals and spends one slot");

        // With no slot of its own level left, the next one up is spent and the spell grows with it.
        Sheet(w, 1).Resources["slots-1"] = new Resource(0, 2);
        Sheet(w, 1).Resources["slots-2"] = new Resource(1, 1);
        Sheet(w, 2).Hp = 1;
        Assert.True(w.Cast(1, "mend", 2) && Slots(w, 1, 2) == 0 && Sheet(w, 2).Hp >= 5 && world.Said("from a level 2 slot"), "A spell can be cast from a higher slot");
        Assert.True(!w.Cast(1, "mend", 2) && w.Refusal.Contains("no spell slot left"), "Without slots a levelled spell is refused");
    }

    [Fact]
    public void AimingSpellsInAFight()
    {
        using WorldFixture world = Hall(9);
        World w = world.World;
        Prepare(w);
        Sheet(w, 4).Stats.SetBase("dex", 3000); // Nok always makes the save
        Sheet(w, 0).Stats.SetBase("dex", 9000); // and the wizard still goes first
        Assert.True(StartFight(world), "The wizard acts first");

        // A cantrip: one hand, one action, no slot.
        Assert.True(world.Use("spark", 3) && Sheet(w, 3).Hp <= 99 && Sheet(w, 3).Hp >= 94 && w.ActionsLeft == 1 && Slots(w, 0, 1) == 2,
            "Spark costs an action and no slot, and a failed save takes the damage");
        int nok = Sheet(w, 4).Hp;
        Assert.True(world.Use("spark", 4) && Sheet(w, 4).Hp == nok, "A made save stops Spark");
        Assert.False(world.Use("spark", 5), "No actions left, no more spells");

        // Two hands: the staff has to be put away, and the cost is two actions.
        using WorldFixture second = Hall(9);
        World s = second.World;
        Prepare(s);
        Sheet(s, 4).Stats.SetBase("dex", 3000);
        Sheet(s, 0).Stats.SetBase("dex", 9000);
        Assert.True(StartFight(second), "The wizard acts first again");
        Assert.True(!s.Use("flame-fan", at: new Cell(5, 3)) && s.Refusal.Contains("2 free hands") && s.ActionsLeft == 2,
            "A two-handed spell is refused while a hand holds the staff");
        EmptyHands(Sheet(s, 0));
        Assert.True(!s.Use("flame-fan", at: new Cell(3, 3)) && !s.Use("flame-fan", at: new Cell(40, 3)) && !s.Use("flame-fan") && s.ActionsLeft == 2,
            "A cone must be aimed away from the caster, on the map");
        Assert.Equal(new[] { 3, 4 }, s.CreaturesIn(0, s.FindAction("flame-fan")!, new Cell(5, 3)));
        Assert.True(s.Use("flame-fan", at: new Cell(5, 3)) && s.ActionsLeft == 0 && Slots(s, 0, 1) == 1, "Flame fan costs two actions and a first-level slot");
        int burned = 100 - Sheet(s, 3).Hp;
        Assert.True(burned >= 2 && burned <= 12 && 100 - Sheet(s, 4).Hp == burned / 2 && Sheet(s, 5).Hp == 100 && Sheet(s, 2).Hp == Sheet(s, 2).MaxHp,
            "One roll for the area: full on a failed save, half on a made one");

        // Walls stop an area, and so does range.
        ActionDefinition mire = s.FindAction("mire")!;
        Assert.True(s.ValidAim(0, mire, new Cell(8, 3), out _) && !s.ValidAim(0, mire, new Cell(11, 3), out string why) && why.Contains("out of range")
            && !s.ValidAim(0, mire, new Cell(3, -1), out _), "A burst is aimed at a square in range");
        Assert.Equal(new[] { 3, 4 }, s.CreaturesIn(0, mire, new Cell(5, 3)));
        Assert.Equal(new[] { 5 }, s.CreaturesIn(0, mire, new Cell(3, 5)));
        Assert.Empty(s.CreaturesIn(0, mire, new Cell(3, 3)));
    }

    [Fact]
    public void Concentration()
    {
        using WorldFixture world = Hall(11);
        World w = world.World;
        Prepare(w);
        Assert.True(StartFight(world), "The wizard acts first to concentrate");
        bool Slowed(int who) => Sheet(w, who).HasCondition("slowed");

        Assert.True(world.Use("mire", at: new Cell(5, 3)) && Slowed(3) && Slowed(4) && !Slowed(5) && !Slowed(1)
            && w.Creatures[0].Concentration.Spell == "mire" && w.Creatures[0].Concentration.Holds.Count == 2 && world.Said("concentrates on Mire"),
            "Mire slows the goblins in it and the wizard concentrates");
        // One at a time: casting it again lets the first go.
        Assert.True(world.Use("mire", at: new Cell(3, 6)) && !Slowed(3) && !Slowed(4) && Slowed(5) && w.Creatures[0].Concentration.Holds.Count == 1
            && world.Said("stops concentrating on Mire") && Slots(w, 0, 1) == 0, "A second concentration spell ends the first");

        // Damage: a Constitution save against 10 or half the damage.
        ActionDefinition jab = ActionDefinition.Read(TestContent.Json("""
            {"id":"jab","target":{"kind":"creature","side":"any","range":20},"effects":[{"do":"damage","dice":40}]}
            """));
        Sheet(w, 0).Stats.SetBase("con", 1000);
        w.RunActionEffect(3, jab, 0);
        Assert.True(w.Creatures[0].Concentration.Active && Slowed(5) && world.Said("DC 20") && world.Said("- held"), "A made save keeps the spell through damage");
        Sheet(w, 0).Stats.SetBase("con", 1);
        w.RunActionEffect(3, jab, 0);
        Assert.True(!w.Creatures[0].Concentration.Active && !Slowed(5) && world.Said("- lost") && world.Said("(hurt)"),
            "A failed save on damage breaks concentration and what it held");
    }

    [Fact]
    public void ConcentrationEndsWhenNothingIsLeftOrTheCasterDrops()
    {
        using WorldFixture world = Hall(11);
        World w = world.World;
        Prepare(w);
        Assert.True(StartFight(world));
        ActionDefinition jab = ActionDefinition.Read(TestContent.Json("""
            {"id":"jab","target":{"kind":"creature","side":"any","range":20},"effects":[{"do":"damage","dice":40}]}
            """));
        Assert.True(world.Use("mire", at: new Cell(3, 6)) && w.Creatures[0].Concentration.Active);
        Sheet(w, 5).RemoveCondition("slowed");
        w.RunActionEffect(3, jab, 5);
        Assert.True(!w.Creatures[0].Concentration.Active && world.Said("run its course"), "Concentration with nothing left to hold is over");

        Assert.True(world.Use("mire", at: new Cell(5, 3)) && w.Creatures[0].Concentration.Active);
        Sheet(w, 0).Stats.SetBase("con", 1000);
        Sheet(w, 0).Hp = 5;
        w.RunActionEffect(3, jab, 0);
        Assert.True(Sheet(w, 0).Down && !w.Creatures[0].Concentration.Active && !Sheet(w, 3).HasCondition("slowed") && world.Said("(down)"),
            "A caster who drops lets the spell go");
    }

    [Fact]
    public void PreparingSpells()
    {
        using WorldFixture world = Hall(13);
        World w = world.World;
        CharacterSheet wizard = Sheet(w, 0);
        Assert.Equal(new[] { "flame-fan", "mire", "glass-skin" }, wizard.Preparable);
        Assert.True(wizard.PrepareLimit == 2 && wizard.Prepared.SequenceEqual(new[] { "flame-fan", "mire" }) && w.Creatures[0].MayPrepare,
            "A new wizard has its first spells prepared and may choose again before the first fight");
        Assert.True(!w.Prepare(0, new[] { "mire", "mend" }) && !w.Prepare(0, Array.Empty<string>()) && !w.Prepare(2, new[] { "mire" })
            && wizard.Prepared.Count == 2, "Only spells from its list, and at least one");
        Assert.True(w.Prepare(0, new[] { "mire" }) && wizard.Prepared.SequenceEqual(new[] { "mire" }) && !wizard.Spells.Contains("flame-fan")
            && world.Said("prepares Mire"), "Preparing swaps what the wizard can cast");
        w.Creatures[0].MayPrepare = false;
        Assert.True(!w.Prepare(0, new[] { "flame-fan", "mire" }) && w.Refusal.Contains("long rest"), "The choice waits for a long rest");

        using WorldFixture fight = Hall(9);
        Prepare(fight.World);
        Assert.True(StartFight(fight) && !fight.World.Creatures[0].MayPrepare && !fight.World.Prepare(0, new[] { "mire" }), "A fight closes it");
    }

    [Fact]
    public void FocusSpells()
    {
        using WorldFixture world = Hall(15);
        World w = world.World;
        EmptyHands(Sheet(w, 1));
        Assert.True(Sheet(w, 1).Resources["focus"].Max == 1 && w.Cast(1, "shield-of-faith", 2) && Sheet(w, 2).TempHp >= 3
            && Sheet(w, 1).Resources["focus"].Current == 0 && Slots(w, 1, 1) == 2, "A focus spell spends a focus point and no slot");
        Assert.True(!w.Cast(1, "shield-of-faith", 2) && w.Refusal.Contains("needs 1 focus"), "An empty pool refuses it");
    }

    // Each spell cast once through the real casting path, from the slot of its own level, against
    // goblins that always fail the save.
    [Fact]
    public void StarterLists()
    {
        using WorldFixture world = Hall(17);
        World w = world.World;
        Prepare(w);
        Sheet(w, 1).Stats.SetBase("wis", 1000);
        foreach (int caster in new[] { 0, 1 })
        {
            for (int level = 1; level <= 3; level++)
            {
                Sheet(w, caster).Resources[$"slots-{level}"] = new Resource(4, 4);
            }
        }
        int Lost(int who) => Sheet(w, who).MaxHp - Sheet(w, who).Hp;
        void Heal()
        {
            for (int i = 0; i < w.Creatures.Count; i++)
            {
                Sheet(w, i).Hp = Sheet(w, i).MaxHp;
                Sheet(w, i).TempHp = 0;
                foreach (string id in new[] { "slowed", "frightened", "prone", "off-guard", "aided" })
                {
                    Sheet(w, i).RemoveCondition(id);
                }
            }
        }
        bool Cast(int caster, string id, int? target, Cell? at = null)
        {
            SpellDefinition? spell = w.FindSpell(id);
            if (spell == null)
            {
                return false;
            }
            w.CastSpell(caster, spell, target, at, spell.Level);
            return true;
        }

        // Between fights: helping spells go through Cast like any other.
        Sheet(w, 2).Hp = 0;
        Sheet(w, 0).Hp = 1;
        Assert.True(!w.Cast(1, "kind-word", 2) && Sheet(w, 2).Hp == 0 && !Sheet(w, 2).HasCondition("aided"), "Kind word can't help someone downed");
        Assert.True(w.Cast(1, "kind-word", 0) && Sheet(w, 0).HasCondition("aided") && Slots(w, 1, 1) == 4, "Kind word aids an ally, even with a mace and shield in hand");
        Sheet(w, 1).Spells.Add("gathered-mending");
        EmptyHands(Sheet(w, 1));
        Assert.True(w.Cast(1, "gathered-mending", 1) && Sheet(w, 2).Hp >= 4 && Sheet(w, 0).Hp >= 5 && Slots(w, 1, 2) == 3,
            "Gathered mending heals everyone near the cleric and gets the downed up");
        Heal();
        Assert.True(StartFight(world), "The fight for the starter lists starts");

        // Wizard.
        Assert.True(w.ActionCost(1, w.FindAction("kind-word")!) == 1 && w.ActionCost(1, w.FindAction("steadfast-chorus")!) == 2,
            "A spell cast with no hands still costs its actions");
        Assert.True(Cast(0, "chill-bite", 3) && Lost(3) >= 1 && Lost(3) <= 4 && Sheet(w, 3).HasCondition("slowed"), "Chill bite does 1d4 cold and slows");
        Heal();
        int ac = Sheet(w, 0).ArmorClass(w.Rules);
        Assert.True(Cast(0, "glass-skin", null) && Sheet(w, 0).ArmorClass(w.Rules) == ac + 2 && Slots(w, 0, 1) == 3, "Glass skin gives +2 AC for a first-level slot");
        Assert.True(Cast(0, "arcane-dart", 3) && Lost(3) >= 3 && Lost(3) <= 9 && Sheet(w, 0).Resources["focus"].Current == 0, "Arcane dart does 2d4+1 force for a focus point");
        Heal();
        Cell nok = w.CellOf(4);
        Assert.True(Cast(0, "rams-breath", null, new Cell(5, 3)) && Lost(3) >= 3 && Lost(3) <= 18 && Lost(4) == Lost(3)
            && w.CellOf(4).X == nok.X + 2 && Slots(w, 0, 2) == 3, "Ram's breath hits the cone and shoves the goblins back");
        w.Place(4, nok);
        Heal();
        int attack = Sheet(w, 5).AttackModifier(w.Rules);
        Assert.True(Cast(0, "lead-limbs", 5) && Sheet(w, 5).HasCondition("slowed") && Sheet(w, 5).AttackModifier(w.Rules) == attack - 2
            && w.Creatures[0].Concentration.Spell == "lead-limbs" && w.Creatures[0].Concentration.Holds.Count == 2,
            "Lead limbs slows and weakens one goblin while the wizard concentrates");
        w.EndConcentration(0, "");
        Assert.True(!Sheet(w, 5).HasCondition("slowed") && Sheet(w, 5).AttackModifier(w.Rules) == attack, "Letting go of Lead limbs undoes both");
        Heal();
        Assert.True(Cast(0, "cinder-burst", null, w.CellOf(4)) && Lost(3) >= 5 && Lost(3) <= 30 && Lost(4) == Lost(3) && Lost(5) == 0
            && Lost(0) == 0 && Slots(w, 0, 3) == 3, "Cinder burst burns everyone two squares around the spot");
        Heal();
        Assert.True(Cast(0, "earth-heave", null, w.CellOf(5)) && Lost(5) >= 3 && Lost(5) <= 18 && Sheet(w, 5).HasCondition("prone")
            && Lost(1) == 0 && !Sheet(w, 1).HasCondition("prone"), "Earth heave hurts and floors enemies only");
        Heal();

        // Cleric.
        Assert.True(Cast(1, "rebuke", 3) && Lost(3) >= 1 && Lost(3) <= 6 && Sheet(w, 3).HasCondition("frightened"), "Rebuke does 1d6 radiant and frightens");
        Heal();
        Assert.True(Cast(1, "brand-of-light", 4) && Lost(4) >= 3 && Lost(4) <= 18 && Sheet(w, 4).HasCondition("off-guard"),
            "Brand of light does 3d6 radiant and leaves the goblin off-guard");
        Heal();
        int fighterAttack = Sheet(w, 2).AttackModifier(w.Rules);
        int wizardAttack = Sheet(w, 0).AttackModifier(w.Rules);
        int goblinAttack = Sheet(w, 3).AttackModifier(w.Rules);
        Assert.True(Cast(1, "rallying-hymn", null) && Sheet(w, 2).AttackModifier(w.Rules) == fighterAttack + 1
            && Sheet(w, 0).AttackModifier(w.Rules) == wizardAttack + 1 && Sheet(w, 3).AttackModifier(w.Rules) == goblinAttack
            && w.Creatures[1].Concentration.Spell == "rallying-hymn", "Rallying hymn lifts the allies near the cleric and nobody else");
        int fighterAc = Sheet(w, 2).ArmorClass(w.Rules);
        Assert.True(Cast(1, "iron-vow", 2) && Sheet(w, 2).ArmorClass(w.Rules) == fighterAc + 2 && Sheet(w, 2).AttackModifier(w.Rules) == fighterAttack
            && w.Creatures[1].Concentration.Spell == "iron-vow", "Iron vow gives +2 AC and ends the hymn");
        w.EndConcentration(1, "");
        Assert.Equal(fighterAc, Sheet(w, 2).ArmorClass(w.Rules));
        Assert.True(Cast(1, "dawnburst", null, w.CellOf(4)) && Lost(3) >= 4 && Lost(3) <= 32 && Lost(4) == Lost(3) && Lost(0) == 0,
            "Dawnburst burns the goblins and spares the party");
        Heal();
        Sheet(w, 2).AddCondition(w.Rules, "frightened", 3, 2);
        Assert.True(Cast(1, "steadfast-chorus", null) && !Sheet(w, 2).HasCondition("frightened") && Sheet(w, 2).TempHp >= 5
            && Sheet(w, 0).TempHp >= 5 && Sheet(w, 3).TempHp == 0 && Slots(w, 1, 3) == 2, "Steadfast chorus ends fear and gives the party temporary HP");
    }

    // The C++ client's SurfacesTest, in this port's terms: a size is a radius in squares and a
    // round between fights is six seconds of world time.
    [Fact]
    public void Surfaces()
    {
        // no goblins here, so nothing starts a fight while time passes
        using WorldFixture world = WorldFixture.Small(new[]
        {
            "##########", "#A.......#", "#........#", "#........#", "#........#", "#........#", "#........#", "##########",
        });
        World w = world.World;
        Assert.Empty(w.Surfaces);
        Assert.True(w.AddSurface("fire", new Cell(5, 5), 1, 2) && !w.AddSurface("lava", new Cell(5, 5), 1, 2) && !w.AddSurface("fire", new Cell(-1, 5), 1, 2));
        Assert.True(w.Surfaces.Count == 1 && w.Surfaces[0].Id == "fire" && w.Surfaces[0].At == new Cell(5, 5) && w.Surfaces[0].RoundsLeft == 2);
        Assert.True(w.SurfacesAt(new Cell(5, 5)).Count == 1 && w.SurfacesAt(new Cell(6, 5)).Count == 1 && w.SurfacesAt(new Cell(7, 5)).Count == 0,
            "A surface of size 1 covers its square and the ones beside it");
        Assert.True(w.AddSurface("fire", new Cell(5, 5), 2, 1) && w.Surfaces.Count == 1 && w.Surfaces[0].Size == 2 && w.Surfaces[0].RoundsLeft == 2,
            "The same surface on the same square grows instead of stacking");
        Assert.True(w.AddSurface("water", new Cell(6, 5), 1, 1) && w.Surfaces.Count == 1 && w.Surfaces[0].Id == "water" && world.Said("puts out the fire"),
            "Water puts out fire where they meet");
        world.Step(World.SecondsPerRound + 0.1);
        Assert.Empty(w.Surfaces);

        using WorldFixture fight = Hall(9);
        World f = fight.World;
        Prepare(f);
        Sheet(f, 3).Stats.SetBase("dex", 900); // Gik goes right after the wizard
        Assert.True(StartFight(fight));
        Assert.True(f.AddSurface("fire", f.CellOf(3), 0.5f, 3) && f.SurfacesAt(f.CellOf(4)).Count == 0);
        Assert.True(fight.Use("end-turn") && fight.StepUntil(() => fight.Said("Gik is in the fire"), 10),
            "A creature starting its turn in fire burns");
    }

    // Not in the C++ client: creatures the game plays cast their spells when a spell beats a swing.
    [Fact]
    public void TheAiCastsSpells()
    {
        using WorldFixture world = Hall(21, f =>
        {
            f["creatures/goblin-hexer.json"] = """{"id":"goblin-hexer","name":"Goblin hexer","hp":7,"armorClass":12,"speed":30,"spells":["spark"]}""";
            f["chapters/spell-hall/chapter.json"] = f["chapters/spell-hall/chapter.json"].Replace("\"creature\":\"goblin\",\"name\":\"Zed\"", "\"creature\":\"goblin-hexer\",\"name\":\"Zed\"");
        });
        World w = world.World;
        Assert.Equal(new[] { "spark" }, Sheet(w, 5).Spells);
        Sheet(w, 5).Stats.SetBase("dex", 9000);
        world.Fight();
        Assert.True(w.CurrentCreature == 5 && w.PickAbility(5) is AbilityChoice pick && pick.Action.Id == "spark" && pick.Value > w.StrikeWorth(5),
            "Empty handed, Spark is worth more than a punch");
        Assert.True(world.StepUntil(() => world.Said("Zed casts Spark"), 10), "The hexer casts it on its turn");

        // From across the hall it walks only into Spark's range, and casts instead of dashing in to punch.
        using WorldFixture far = Hall(21, f =>
        {
            f["creatures/goblin-hexer.json"] = """{"id":"goblin-hexer","name":"Goblin hexer","hp":7,"armorClass":12,"speed":30,"spells":["spark"]}""";
            f["chapters/spell-hall/chapter.json"] = f["chapters/spell-hall/chapter.json"].Replace("\"creature\":\"goblin\",\"name\":\"Zed\",\"at\":[3,6]", "\"creature\":\"goblin-hexer\",\"name\":\"Zed\",\"at\":[12,6]");
        });
        World h = far.World;
        Sheet(h, 5).Stats.SetBase("dex", 9000);
        far.Fight();
        Assert.True(h.CurrentCreature == 5 && h.PickAbility(5) == null && h.ApproachToCast(5) is Cell spot && h.Grid.Distance(spot, h.CellOf(0)) <= 6.01f);
        Assert.True(far.StepUntil(() => far.Said("Zed casts Spark"), 10) && !far.Said("Zed dashes") && !far.Said("Zed attacks"),
            "The hexer steps into range and casts");
    }

    [Fact]
    public void BadSpellFiles()
    {
        TestContent.Refused(() => Hall(1, f => f["rulesets/yorehold/spells/murk.json"] =
            """{"id":"murk","level":1,"effects":[{"do":"condition","id":"missing-condition"}]}"""));
        TestContent.Refused(() => Hall(1, f => f["rulesets/yorehold/spells/strike.json"] = """{"id":"strike","effects":[{"do":"damage","dice":1}]}"""));
        TestContent.Refused(() => Hall(1, f => f["rulesets/yorehold/spellcasting.json"] = """{"concentration":{"ability":"luck"}}"""));
        TestContent.Refused(() => Hall(1, f => f["creatures/hexer.json"] = """{"id":"hexer","spells":["no-such-spell"]}"""));
        // Hands that only set the cost: the cleric casts with a mace and a shield.
        using WorldFixture easy = Hall(1, f => f["rulesets/yorehold/spellcasting.json"] = """{"hands":"ignored"}""");
        Sheet(easy.World, 2).Hp = 1;
        Assert.True(easy.World.Cast(1, "mend", 2) && Sheet(easy.World, 2).Hp > 1, "With hands ignored, a full-handed cleric casts");
    }
}
