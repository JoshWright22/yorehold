namespace Yorehold.Rules.Tests;

/// <summary>The world between fights: built from a chapter, walking, objects, traps, sight, sneaking and triggers.</summary>
public class WorldTests
{
    // A wall down the middle with a door and a gate in it, a lever for the gate, a chest only a key
    // opens, a trap anyone would see and one nobody will. The C++ client's object yard.
    private static readonly Dictionary<string, string> ObjectYard = new()
    {
        ["chapters/obj-yard/chapter.json"] = """
            {"id": "obj-yard", "title": "Yard", "map": "map.json",
             "party": [{"name": "Ana", "class": "fighter", "at": [3, 2]}, {"name": "Bo", "class": "rogue", "at": [2, 2]}]}
            """,
        ["chapters/obj-yard/items/cell-key.json"] = """{"id": "cell-key", "name": "Cell key", "weight": 0, "value": 0}""",
        ["chapters/obj-yard/map.json"] = """
            {"name": "Yard", "tiles": {"floor": {"art": "stone"}, "wall": {"art": "wall", "walkable": false, "blocksSight": true}},
             "legend": {".": "floor", "#": "wall"},
             "layers": [{"name": "ground", "rows": ["##########", "#....#...#", "#........#", "#....#...#",
                                                    "#....#...#", "#........#", "#....#...#", "##########"]}],
             "objects": [
               {"kit": "door", "at": [5, 2]},
               {"kit": "door", "at": [5, 5], "name": "the gate", "tags": ["link:gate"], "door": {"locked": true}},
               {"kit": "lever", "at": [1, 5], "tags": ["link:gate", "flag:gate-opened"]},
               {"kit": "locked-chest", "at": [1, 1], "tags": ["key:cell-key"], "lock": {"dc": 40}, "contents": {"coins": 50, "healing-potion": 1}},
               {"kit": "dart-trap", "at": [3, 4], "trap": {"detectDc": 1, "disarmDc": 0}},
               {"kit": "dart-trap", "at": [2, 6], "name": "the hidden darts", "trap": {"detectDc": 60}}
             ]}
            """,
    };

    private static readonly string[] Yard =
    {
        "##########",
        "#A.......#",
        "#B.......#",
        "#####....#",
        "#.......g#",
        "##########",
    };

    [Fact]
    public void EveryShippedChapterBuildsAWorld()
    {
        string chapters = Path.Combine(TestContent.AssetsFolder(), "chapters");
        string[] folders = Directory.GetDirectories(chapters).Where(f => File.Exists(Path.Combine(f, "chapter.json"))).ToArray();
        Assert.NotEmpty(folders);
        foreach (string folder in folders)
        {
            string name = "chapters/" + Path.GetFileName(folder);
            using WorldFixture world = WorldFixture.Load(name, 3);
            Chapter chapter = world.World.Chapter;
            int creatures = chapter.Party.Count + chapter.Encounters.Sum(e => e.Creatures.Count) + chapter.Npcs.Count;
            Assert.True(world.World.HeroCount == chapter.Party.Count, name);
            Assert.True(world.World.Creatures.Count == creatures && world.World.Tokens.Tokens.Count == creatures, name);
            Assert.True(world.World.Map.Objects.Count == chapter.Map.Objects.Count, name);
            Assert.True(world.EventsOf(WorldEventKind.Banner).Contains(chapter.Title), name);
            world.Step(1);
            for (int i = 0; i < world.World.HeroCount; i++)
            {
                Assert.True(world.World.Map.Walkable(world.World.CellOf(i)), $"{name}: hero {i} stands somewhere walkable");
            }
            Assert.True(world.World.Fog.State(0, 0, world.World.CellOf(0)) == FogState.Visible, $"{name}: the party sees where it stands");
        }
    }

    [Fact]
    public void TheKeepHasWallsRoofsAndHiddenGoblins()
    {
        using WorldFixture world = WorldFixture.Load("chapters/goblin-keep", 2);
        World w = world.World;
        Assert.True(world.Said("Goblins have taken it."));
        Assert.Equal("yorehold", w.Rules.Id);
        Assert.NotEmpty(w.Map.Walls);
        Assert.All(Enumerable.Range(w.HeroCount, w.NpcStart - w.HeroCount), i => Assert.Equal(1, w.Creatures[i].Team));
        Assert.All(Enumerable.Range(w.NpcStart, w.Creatures.Count - w.NpcStart), i => Assert.Equal(2, w.Creatures[i].Team));
        // An enemy out of sight stays hidden on the map.
        world.Step(0.05);
        Assert.Contains(Enumerable.Range(w.HeroCount, w.Creatures.Count - w.HeroCount), i => w.Tokens.Tokens[i].Floor == World.HiddenFloor);
    }

    [Fact]
    public void WalkingAcrossTheYardWakesTheGoblin()
    {
        using WorldFixture world = WorldFixture.Small(Yard);
        World w = world.World;
        Assert.Equal((2, 3), (w.HeroCount, w.Creatures.Count));
        Assert.False(world.Go(0, new Cell(0, 0)), "Walking into a wall is refused");
        Assert.False(world.Go(0, new Cell(9, 9)), "Off the map too");
        Assert.True(world.Go(0, new Cell(7, 4)));
        Assert.True(world.StepUntil(() => w.Fighting, 30), "Seeing the goblin starts its fight");
        Assert.True(world.Said("Goblins!"));
        Assert.Equal(0, w.FightGroup);
        Assert.True(w.Creatures[2].Awake);
        Assert.Contains(world.Events, e => e.Kind == WorldEventKind.Fight && e.Group == 0);
        Assert.False(world.Walking, "Everyone stops when the fight starts");
        Assert.False(world.Go(0, new Cell(1, 1)), "No walking about once it has");
    }

    [Fact]
    public void TheOthersFollowTheLeader()
    {
        using WorldFixture world = WorldFixture.Small(new[]
        {
            "############",
            "#AB........#",
            "#C.........#",
            "############",
        });
        World w = world.World;
        Assert.Equal((0, 1), (w.Tokens.Follows(1), w.Tokens.Follows(2)));
        Assert.True(world.Go(0, new Cell(10, 1)));
        Assert.True(world.StepUntil(() => !world.Walking, 20));
        Assert.Equal(new Cell(10, 1), w.CellOf(0));
        for (int i = 1; i < 3; i++)
        {
            Assert.True(w.Grid.Distance(w.CellOf(i), w.CellOf(i - 1)) <= 2, $"hero {i} kept up");
        }
        Assert.Equal(3, Enumerable.Range(0, 3).Select(w.CellOf).Distinct().Count());
    }

    [Fact]
    public void DoorsAndLocks()
    {
        using WorldFixture world = WorldFixture.LoadJson("chapters/obj-yard", ObjectYard, 3);
        World w = world.World;
        Assert.Equal(6, w.Map.Objects.Count);
        int door = world.ObjectOn(new Cell(5, 2)), gate = world.ObjectOn(new Cell(5, 5));
        int lever = world.ObjectOn(new Cell(1, 5)), chest = world.ObjectOn(new Cell(1, 1));
        Assert.False(w.Walkable(new Cell(5, 2)) || w.Walkable(new Cell(5, 5)), "Shut doors block the way");

        world.Put(0, new Cell(3, 2));
        Assert.False(world.Interact(0, door));
        Assert.Contains("too far", w.Refusal);
        world.Put(0, new Cell(4, 2));
        int wallsShut = w.Map.Walls.Count;
        Assert.Equal(door, w.ObjectNear(0));
        Assert.True(world.Interact(0, door));
        Assert.True(w.Walkable(new Cell(5, 2)) && w.Map.Walls.Count < wallsShut && world.Said("opens the door"));
        world.Put(1, new Cell(5, 2));
        Assert.False(world.Interact(0, door));
        Assert.Equal("Something is in the way.", w.Refusal);
        world.Put(1, new Cell(2, 2));
        Assert.True(world.Interact(0, door));
        Assert.True(!w.Walkable(new Cell(5, 2)) && w.Map.Walls.Count == wallsShut, "Closing it blocks the way again");

        // The chest: locked past any roll, so only the key opens it.
        world.Put(0, new Cell(2, 1));
        WorldObject box = w.Map.Get(chest)!;
        Assert.Equal((50, 1), (box.Contents["coins"], box.Contents["healing-potion"]));
        Assert.True(world.Interact(0, chest));
        Assert.True(world.Said("stays locked") && box.IsLocked);
        Assert.Equal(0, w.Map.Take(chest, "coins", 50));
        w.Creatures[0].Items.Add("cell-key");
        Assert.True(world.Interact(0, chest));
        Assert.True(!box.IsLocked && world.Said("unlocks"), "The key the lock names opens it");
        Assert.Equal(50, w.Map.Take(chest, "coins", 50));
        Assert.NotEqual(chest, w.ObjectNear(0));

        // The lever swings the gate, locked or not, and sets the story flag on it.
        world.Put(0, new Cell(1, 4));
        Assert.True(world.Interact(0, lever));
        Assert.True(w.Map.Get(gate)!.Open && w.Walkable(new Cell(5, 5)), "The lever opens the gate it is linked to");
        Assert.Contains("gate-opened", w.Flags);

        w.NewAdventure(3);
        Assert.False(w.Map.Get(door)!.Open);
        Assert.True(w.Map.Get(chest)!.IsLocked);
        Assert.Empty(w.Flags);
    }

    [Fact]
    public void GoNearWalksUpToAnObject()
    {
        using WorldFixture world = WorldFixture.LoadJson("chapters/obj-yard", ObjectYard, 3);
        World w = world.World;
        int door = world.ObjectOn(new Cell(5, 2)), chest = world.ObjectOn(new Cell(1, 1));
        Assert.True(w.Usable(w.Map.Get(door)!) && w.Usable(w.Map.Get(chest)!));

        world.Put(0, new Cell(1, 2));
        Assert.True(w.GoNear(0, door));
        Assert.NotEmpty(w.Tokens.Tokens[0].Path);
        Assert.True(world.StepUntil(() => w.Tokens.Tokens[0].Path.Count == 0, 10));
        Cell at = w.CellOf(0);
        Assert.True(at.X == 4 && Math.Abs(at.Y - 2) <= 1, $"stops beside the door, not at {at}");
        Assert.True(world.Interact(0, door));

        // Already beside it: nothing to walk.
        Assert.True(w.GoNear(0, door));
        Assert.Empty(w.Tokens.Tokens[0].Path);
        Assert.False(w.GoNear(0, 99));
    }

    [Fact]
    public void TrapsAreFoundDisarmedAndSprung()
    {
        using WorldFixture world = WorldFixture.LoadJson("chapters/obj-yard", ObjectYard, 9);
        World w = world.World;
        int seen = world.ObjectOn(new Cell(3, 4)), hidden = world.ObjectOn(new Cell(2, 6));
        world.Put(0, new Cell(3, 2));
        world.Put(1, new Cell(1, 2));
        world.Step(0.1);
        Assert.True(w.Map.Get(seen)!.TrapFound && world.Said("spots"), "A hero whose passive score beats a nearby trap finds it");
        Assert.False(w.Map.Get(hidden)!.TrapFound, "A trap too well hidden stays unseen");
        Assert.False(w.Walkable(new Cell(3, 4)), "Paths go around a trap the party knows about");
        world.Put(0, new Cell(3, 3));
        Assert.True(world.Interact(0, seen));
        Assert.True(!w.Map.Get(seen)!.ArmedTrap && world.Said("disarms"));
        Assert.True(w.Walkable(new Cell(3, 4)), "A disarmed trap is safe to walk over");

        int hp = w.Creatures[0].Sheet.Hp;
        world.Put(0, new Cell(2, 6));
        world.Step(0.1);
        Assert.True(world.Said("sets off the hidden darts"));
        Assert.True(!w.Map.Get(hidden)!.ArmedTrap && w.Map.Get(hidden)!.TrapFound, "Stepping on a hidden trap sets it off once");
        Assert.True(world.Said("dex save"), "Its effect is run on the hero");
        Assert.True(w.Creatures[0].Sheet.Hp <= hp);
        int after = w.Creatures[0].Sheet.Hp;
        world.Step(0.5);
        Assert.Equal(after, w.Creatures[0].Sheet.Hp);
    }

    [Fact]
    public void SneakingIsTheHiddenCondition()
    {
        using WorldFixture world = WorldFixture.Small(Yard);
        World w = world.World;
        Assert.True(world.Sneak(true));
        Assert.True(w.Creatures[0].Sneaking && w.Creatures[1].Sheet.HasCondition("hidden") && w.Creatures[1].Sheet.HasFlag(w.Rules, "hidden") && w.AnySneaking);
        world.Step(0.1);
        Assert.Equal(0.5f, w.Tokens.Tokens[0].Pace);
        Assert.True(world.Sneak(false));
        Assert.False(w.Creatures[0].Sheet.HasCondition("hidden") || w.AnySneaking);
        world.Step(0.1);
        Assert.Equal(1f, w.Tokens.Tokens[0].Pace);

        // A fight starting gives sneakers away.
        world.Sneak(true);
        w.Notice(0);
        Assert.True(w.Fighting && !w.AnySneaking);
        Assert.False(world.Sneak(true));
    }

    [Fact]
    public void SneakingBehindAWatcherGoesUnnoticed()
    {
        string[] hall =
        {
            "############",
            "#A.......g.#",
            "#B.........#",
            "############",
        };
        using WorldFixture world = WorldFixture.Small(hall);
        World w = world.World;
        int goblin = w.HeroCount;
        // Left alone it would watch the way the party comes from; this one looks east, away from it.
        Assert.Equal(MathF.PI, w.Creatures[goblin].Facing, 3);
        w.Creatures[goblin].Facing = 0;
        Assert.True(world.Sneak(true));
        Assert.True(world.Go(0, new Cell(7, 1)));
        Assert.True(world.StepUntil(() => !world.Walking, 20));
        Assert.False(w.Fighting, "Behind its back the goblin sees nothing");
        Assert.Equal(FogState.Visible, w.Fog.State(0, 0, w.CellOf(goblin)));

        // Out in the open, being seen is enough.
        Assert.True(world.Sneak(false));
        world.Step(0.1);
        Assert.True(w.Fighting);
    }

    [Fact]
    public void SneakingIntoAConeMeansStealthChecks()
    {
        string[] hall =
        {
            "############",
            "#A.........#",
            "#B.......g.#",
            "############",
        };
        using WorldFixture world = WorldFixture.Small(hall, seed: 4);
        World w = world.World;
        int goblin = w.HeroCount;
        // Watching the party's corner, and far too sharp to slip past but for a natural 20.
        w.Creatures[goblin].Sheet.Stats.SetBase("wis", 50);
        List<Watcher> watchers = w.Watchers();
        Assert.Single(watchers);
        Assert.True(watchers[0].Range > 0 && watchers[0].PassivePerception >= 30);
        Assert.True(world.Sneak(true));
        Assert.True(world.Go(0, new Cell(6, 1)));
        Assert.True(world.StepUntil(() => w.Fighting, 20));
        Assert.True(world.Said("spots Ana! (Stealth"));
        Assert.Equal(-1f, w.Watchers()[0].Range);
    }

    [Fact]
    public void LightAndTimeOfDayDecideWhatIsSeen()
    {
        string[] rows =
        {
            "##########",
            "#A.......#",
            "#,,,,,,,,#",
            "##########",
        };
        const string darkRules = "\"lighting\": {\"mode\": \"rules\", \"carried\": 0, \"time\": \"night\"}";
        using WorldFixture night = WorldFixture.Small(rows, map: darkRules);
        night.Step(0.05);
        Assert.Equal(FogState.Unexplored, night.World.Fog.State(0, 0, new Cell(5, 1)));
        Assert.Equal(LightLevel.Dark, night.World.LightAt(night.World.Grid.Center(new Cell(5, 1))));
        Assert.Single(night.World.Map.IndoorAreas);
        Assert.Equal(new Rect(64, 128, 8 * 64, 64), night.World.Map.IndoorAreas[0]);

        night.World.Options.TimeOfDay = 1; // day
        night.Step(0.05);
        Assert.Equal(FogState.Visible, night.World.Fog.State(0, 0, new Cell(5, 1)));
        Assert.Equal(LightLevel.Bright, night.World.LightAt(night.World.Grid.Center(new Cell(5, 1))));
        // Under the roof it stays as dark as the map's own lights leave it.
        Assert.Equal(LightLevel.Dark, night.World.LightAt(night.World.Grid.Center(new Cell(5, 2))));

        const string lamp = "\"lighting\": {\"mode\": \"rules\", \"carried\": 0}, \"lights\": [{\"at\": [6.5, 2.5], \"radius\": 2}]";
        using WorldFixture lit = WorldFixture.Small(rows, map: lamp);
        lit.Step(0.05);
        Assert.Equal(FogState.Visible, lit.World.Fog.State(0, 0, new Cell(6, 2)));
        Assert.Equal(LightLevel.Bright, lit.World.LightAt(lit.World.Grid.Center(new Cell(6, 2))));
        Assert.Equal(FogState.Unexplored, lit.World.Fog.State(0, 0, new Cell(2, 2)));
    }

    [Fact]
    public void TriggersFireOnEnterAndOnFlags()
    {
        using WorldFixture world = WorldFixture.Load("chapters/trigger-test");
        World w = world.World;
        Assert.Equal(new[] { "chapters/trigger-test/dialogue/on-enter.json" }, world.EventsOf(WorldEventKind.Talk));
        Assert.Equal(new[] { "on-start" }, w.FiredTriggers);
        Assert.False(w.ChapterCleared());

        world.SetFlags("something-else");
        Assert.Single(world.EventsOf(WorldEventKind.Talk));
        world.SetFlags("enemy-defeated");
        Assert.Equal(new[]
        {
            "chapters/trigger-test/dialogue/on-enter.json",
            "chapters/trigger-test/dialogue/on-victory.json",
            "chapters/trigger-test/dialogue/chapter-complete.json",
        }, world.EventsOf(WorldEventKind.Talk));
        Assert.True(w.ChapterCleared());
        Assert.True(world.Said(w.Chapter.ClearedText));
        Assert.Contains(w.Chapter.ClearedText, world.EventsOf(WorldEventKind.Banner));

        world.SetFlags("enemy-defeated", "more");
        Assert.Equal(3, world.EventsOf(WorldEventKind.Talk).Count);

        w.NewAdventure(1);
        world.Step(0.02);
        Assert.Equal(4, world.EventsOf(WorldEventKind.Talk).Count);
        Assert.Equal(new[] { "on-start" }, w.FiredTriggers);
    }

    [Fact]
    public void ACutsceneTriggerHoldsTheWorld()
    {
        var files = new Dictionary<string, string>
        {
            ["scene.json"] = """{"steps": [{"caption": "Dawn.", "seconds": 1}]}""",
        };
        const string trigger = "\"triggers\": [{\"id\": \"dawn\", \"when\": [\"rested\"], \"cutscene\": \"scene.json\"}]";
        using WorldFixture world = WorldFixture.Small(Yard, trigger, files: files);
        World w = world.World;
        Assert.Empty(world.EventsOf(WorldEventKind.Cutscene));
        world.SetFlags("rested");
        Assert.Equal(new[] { "chapters/small/scene.json" }, world.EventsOf(WorldEventKind.Cutscene));
        Assert.True(w.InCutscene);
        Assert.False(world.Go(0, new Cell(3, 1)));
        w.EndCutscene();
        Assert.True(world.Go(0, new Cell(3, 1)));
    }
}
