using System.Numerics;

namespace Yorehold.Rules.Tests;

/// <summary>Paths, fog, light, objects, tokens and stealth: the C++ framework's unit checks for them.</summary>
public class MapRulesTests
{
    private static readonly Wall[] MiddleWall = { new(new Vector2(5, -10), new Vector2(5, 10)) };

    [Fact]
    public void FogShowsWhatIsInSightAndRemembersIt()
    {
        var fog = new FogOfWar(16, 16, 1);
        fog.Update(0, 0, new[] { new Vision(new Vector2(2.5f, 2.5f), 10) }, MiddleWall);
        Assert.Equal(FogState.Visible, fog.State(0, 0, new Cell(2, 2)));
        Assert.Equal(FogState.Unexplored, fog.State(0, 0, new Cell(7, 2)));
        Assert.Equal(FogState.Unexplored, fog.State(1, 0, new Cell(2, 2)));
        Assert.Equal(FogState.Unexplored, fog.State(0, 1, new Cell(2, 2)));
        fog.Update(0, 0, Array.Empty<Vision>(), MiddleWall);
        Assert.Equal(FogState.Explored, fog.State(0, 0, new Cell(2, 2)));
        fog.Reset(0);
        Assert.Equal(FogState.Unexplored, fog.State(0, 0, new Cell(2, 2)));
        Assert.Throws<ArgumentException>(() => new FogOfWar(0, 1, 1));
    }

    [Fact]
    public void LightIsBrightNearALampDimToItsEdgeAndStoppedByWalls()
    {
        var levels = new LightLevels(16, 16, 1);
        levels.SetFixed(new[] { new WorldLight(new Vector2(2.5f, 8.5f), 4) }, MiddleWall);
        Assert.Equal(LightLevel.Bright, levels.Level(new Cell(2, 8)));
        Assert.Equal(LightLevel.Bright, levels.Level(new Cell(4, 8)));
        Assert.Equal(LightLevel.Dim, levels.Level(new Cell(2, 11)));
        Assert.Equal(LightLevel.Dark, levels.Level(new Cell(2, 13)));
        Assert.Equal(LightLevel.Dark, levels.Level(new Cell(5, 8))); // the wall at x = 5 blocks it
        Assert.Equal(LightLevel.Dark, levels.Level(new Cell(-1, 0)));
        var carried = new[] { new WorldLight(new Vector2(2.5f, 13.5f), 2) };
        Assert.True(levels.Lit(new Cell(2, 13), carried, MiddleWall));
        Assert.False(levels.Lit(new Cell(2, 13)));
        levels.Ambient = LightLevel.Dim;
        Assert.Equal((LightLevel.Dim, LightLevel.Bright), (levels.Level(new Cell(2, 13)), levels.Level(new Cell(2, 8))));
        levels.Ambient = LightLevel.Dark;

        // Fog that follows the light: lit cells are seen from afar, dark ones only within darkvision.
        var dark = new FogOfWar(16, 16, 1);
        Func<Cell, bool> isLit = c => levels.Lit(c);
        dark.Update(0, 0, new[] { new Vision(new Vector2(2.5f, 15.5f), 12, 0) }, MiddleWall, isLit);
        Assert.Equal(FogState.Visible, dark.State(0, 0, new Cell(2, 8)));
        Assert.Equal(FogState.Unexplored, dark.State(0, 0, new Cell(2, 13)));
        var seeing = new[] { new Vision(new Vector2(2.5f, 15.5f), 12, 3) };
        dark.Update(0, 0, seeing, MiddleWall, isLit);
        Assert.Equal(FogState.Visible, dark.State(0, 0, new Cell(2, 13)));
        Assert.Equal(FogState.Unexplored, dark.State(0, 0, new Cell(2, 3)));
        dark.Update(0, 0, seeing, MiddleWall);
        Assert.Equal(FogState.Visible, dark.State(0, 0, new Cell(2, 5)));
    }

    [Fact]
    public void PathsGoAroundWallsWithoutCuttingCorners()
    {
        var square = new Grid(GridType.Square, 10);
        var blocked = new HashSet<Cell> { new(1, 0), new(0, 1) };
        bool Passable(Cell c) => c.X >= 0 && c.Y >= 0 && c.X < 100 && c.Y < 100 && !blocked.Contains(c);
        Assert.Empty(Paths.Find(square, new Cell(0, 0), new Cell(2, 2), Passable));

        blocked.Clear();
        for (int y = 0; y < 100; y++)
        {
            if (y != 50)
            {
                blocked.Add(new Cell(40, y));
            }
        }
        List<Cell> path = Paths.Find(square, new Cell(2, 2), new Cell(98, 98), Passable);
        Assert.Equal((new Cell(2, 2), new Cell(98, 98)), (path[0], path[^1]));
        Assert.Contains(new Cell(40, 50), path);
        Assert.All(path, c => Assert.True(Passable(c)));
        for (int i = 1; i < path.Count; i++)
        {
            Assert.Equal(1f, square.Distance(path[i - 1], path[i]));
        }
        blocked.Add(new Cell(40, 50));
        Assert.Empty(Paths.Find(square, new Cell(2, 2), new Cell(98, 98), Passable));
        Assert.Single(Paths.Find(square, new Cell(3, 3), new Cell(3, 3), Passable));
        Assert.Empty(Paths.Find(square, new Cell(2, 2), new Cell(40, 3), Passable));
    }

    [Fact]
    public void PathsKeepToTheStraightLine()
    {
        // Every route of four diagonals and two straights costs the same; the one a person
        // would draw stays within a cell of the line from start to goal.
        var square = new Grid(GridType.Square, 64);
        List<Cell> path = Paths.Find(square, new Cell(0, 0), new Cell(6, 4), c => c.X >= 0 && c.Y >= 0 && c.X < 10 && c.Y < 10);
        Assert.Equal(7, path.Count);
        Assert.All(path, c => Assert.True(MathF.Abs(c.X * 4 - c.Y * 6) / MathF.Sqrt(52) < 1));
    }

    [Fact]
    public void HeapQueuePopsInPriorityOrder()
    {
        var heap = new HeapQueue<(float Priority, int Order)>((a, b) => a.Priority > b.Priority);
        float[] priorities = { 5, 1, 4, 1, 3, 9, 2, 6, 5, 3, 5 };
        for (int i = 0; i < priorities.Length; i++)
        {
            heap.Push((priorities[i], i));
        }
        var popped = new List<float>();
        while (heap.Count > 0)
        {
            popped.Add(heap.Pop().Priority);
        }
        Assert.Equal(priorities.OrderBy(p => p), popped);
    }

    private static MapState ObjectMap()
    {
        const string json = """
            {"tiles": {"floor": {}}, "legend": {".": "floor"},
             "layers": [{"rows": ["........", "........", "........", "........", "........", "........", "........", "........"]}],
             "objects": [
               {"name": "Door", "area": [64, 64, 64, 64], "tags": ["blocksMovement", "blocksSight", "key:red", "link:gate"], "door": {"locked": true}},
               {"area": [0, 0, 64, 64], "tags": ["lever", "link:gate"]},
               {"area": [128, 0, 64, 64], "tags": ["container"], "contents": {"coin": 5}},
               {"area": [192, 0, 64, 64], "tags": ["container"], "door": {"locked": true}, "lock": {"dc": 15, "skill": "dex"}, "contents": {"gem": 1}},
               {"area": [300, 300, 32, 32], "trap": {"detectDc": 14, "disarmDc": 12, "detectSkill": "perception",
                 "effect": [{"do": "damage", "dice": "1d4", "type": "piercing"}]}},
               {"area": [400, 400, 32, 32], "trap": {"effect": [{"do": "damage", "dice": "1d4", "type": "piercing"}]}}
             ]}
            """;
        return new MapState(GameMap.Read(TestContent.Json(json, "map.json"), new Dictionary<string, Kit>()));
    }

    [Fact]
    public void DoorsLeversAndKeys()
    {
        MapState map = ObjectMap();
        const int door = 1, lever = 2;
        Assert.False(map.Passable(new Rect(70, 70, 1, 1), 0));
        Assert.Equal(4, map.Walls.Count);
        Assert.False(map.Walkable(new Cell(1, 1)));
        Assert.Equal(Interaction.Locked, map.Interact(door, Array.Empty<string>()));
        Assert.Equal(Interaction.Opened, map.Interact(door, new[] { "key:red" }));
        map.RefreshWalls();
        Assert.True(map.Passable(new Rect(70, 70, 1, 1), 0));
        Assert.Empty(map.Walls);
        Assert.True(map.Walkable(new Cell(1, 1)));
        Assert.Equal(Interaction.Activated, map.Interact(lever, Array.Empty<string>()));
        Assert.False(map.Get(door)!.Open);
        Assert.Equal(Interaction.Missing, map.Interact(99, Array.Empty<string>()));
        Assert.Equal(door, map.ObjectAt(new Cell(1, 1))?.Id);
        Assert.Equal(new List<Cell> { new(1, 1) }, MapState.CellsOf(map.Get(door)!));

        map.ResetObjects();
        Assert.True(map.Get(door)!.Locked);
        Assert.Equal(4, map.Walls.Count);
    }

    [Fact]
    public void ChestsLocksAndTraps()
    {
        MapState map = ObjectMap();
        const int chest = 3, box = 4, darts = 5, darts2 = 6;
        Assert.Equal((3, 2), (map.Take(chest, "coin", 3), map.Take(chest, "coin", 10)));
        Assert.Equal(0, map.Take(chest, "coin", 1));

        Assert.True(map.Get(box)!.IsLocked);
        Assert.Equal((15, "dex"), (map.Get(box)!.Lock!.Dc, map.Get(box)!.Lock!.Skill));
        Assert.Equal(0, map.Take(box, "gem", 1));
        Assert.Equal(Interaction.Locked, map.Interact(box, Array.Empty<string>()));
        Assert.True(map.Unlock(box));
        Assert.False(map.Unlock(box));
        Assert.False(map.Get(box)!.Open);
        Assert.Equal(Interaction.Opened, map.Interact(box, Array.Empty<string>()));
        Assert.Equal(1, map.Take(box, "gem", 1));

        WorldObject trap = map.Get(darts)!;
        Assert.True(trap.ArmedTrap && !trap.BlocksMovement);
        Assert.Equal("piercing", Assert.Single(trap.Trap!.Effect!.Steps).Type);
        Assert.Equal(new[] { darts }, map.TrapsIn(new Rect(310, 310, 4, 4), 0).Select(o => o.Id));
        Assert.Empty(map.TrapsIn(new Rect(310, 310, 4, 4), 1));
        Assert.True(map.Spring(darts));
        Assert.True(trap.TrapFound);
        Assert.False(map.Spring(darts));
        Assert.Empty(map.TrapsIn(new Rect(310, 310, 4, 4), 0));
        Assert.True(map.Disarm(darts2));
        Assert.False(map.Disarm(darts2));
        Assert.False(map.Spring(darts2));
        Assert.True(map.Get(darts2)!.TrapFound);
    }

    [Fact]
    public void FollowersKeepUpAndCombatFreezesThem()
    {
        var mover = new TokenMover();
        for (int i = 0; i < 4; i++)
        {
            mover.Tokens.Add(new Token());
        }
        mover.Tokens[0].Position = new Vector2(95, 5);
        mover.Tokens[1].Position = new Vector2(5, 5);
        mover.Tokens[2].Position = new Vector2(5, 25);
        mover.Tokens[3].Owner = 1;
        Assert.True(mover.Link(1, 0) && mover.Link(2, 1));
        Assert.False(mover.Link(0, 2) || mover.Link(3, 0) || mover.Link(1, 1));
        var grid = new Grid(GridType.Square, 10);
        bool Passable(Cell c) => c.X >= 0 && c.X < 20 && c.Y >= 0 && c.Y < 10;
        for (int i = 0; i < 60; i++)
        {
            mover.Advance(grid, Passable, 0.1);
        }
        Assert.True(mover.Tokens[1].Position.X > 60);
        Assert.True(grid.Distance(grid.CellAt(mover.Tokens[0].Position), grid.CellAt(mover.Tokens[1].Position)) >= 1);

        mover.Settings.InCombat = true;
        mover.Settings.ActiveTurn = 0;
        Vector2 before = mover.Tokens[1].Position;
        mover.Tokens[1].Path.Add(new Vector2(195, 5));
        mover.Advance(grid, Passable, 1);
        Assert.Equal(before, mover.Tokens[1].Position);
        mover.SetFloor(0, 2);
        Assert.True(mover.Tokens.Take(3).All(t => t.Floor == 2));
        Assert.Empty(mover.Tokens[1].Path);
        mover.Unlink(1);
        Assert.Null(mover.Follows(1));
        mover.ClearLinks();
        Assert.Null(mover.Follows(2));
    }

    [Fact]
    public void FollowersStepAsideThenFallIn()
    {
        var grid = new Grid(GridType.Square, 10);
        var line = new TokenMover();
        for (int i = 0; i < 3; i++)
        {
            line.Tokens.Add(new Token { Position = grid.Center(new Cell(5 - i, 1)) });
        }
        line.Link(1, 0);
        line.Link(2, 1);
        bool Hall(Cell c) => c.X >= 0 && c.X < 20 && c.Y >= 0 && c.Y < 3;
        for (int x = 4; x >= 0; x--)
        {
            line.Tokens[0].Path.Add(grid.Center(new Cell(x, 1)));
        }
        line.Advance(grid, Hall, 0.01);
        Assert.NotEmpty(line.Tokens[1].Path);
        Assert.NotEqual(1, grid.CellAt(line.Tokens[1].Path[^1]).Y);
        bool blocked = false;
        for (int i = 0; i < 40; i++)
        {
            line.Advance(grid, Hall, 0.05);
            for (int f = 1; f < 3; f++)
            {
                Vector2 gap = line.Tokens[f].Position - line.Tokens[0].Position;
                blocked |= gap.X * gap.X + gap.Y * gap.Y < 5 * 5; // closer than half a cell
            }
        }
        Assert.False(blocked);
        Assert.Equal(new Cell(0, 1), grid.CellAt(line.Tokens[0].Position));
        Assert.NotEqual(grid.CellAt(line.Tokens[1].Position), grid.CellAt(line.Tokens[2].Position));
        Assert.True(grid.Distance(grid.CellAt(line.Tokens[0].Position), grid.CellAt(line.Tokens[1].Position)) <= 2);

        // A token's own pace scales the shared walking speed.
        var race = new TokenMover();
        race.Tokens.Add(new Token { Position = new Vector2(5, 5) });
        race.Tokens.Add(new Token { Position = new Vector2(5, 25), Owner = 1, Pace = 0.5f }); // strangers, so neither makes way
        race.Tokens[0].Path.Add(new Vector2(195, 5));
        race.Tokens[1].Path.Add(new Vector2(195, 25));
        race.Advance(grid, c => c.X >= 0 && c.X < 20 && c.Y >= 0 && c.Y < 10, 1);
        Assert.True(MathF.Abs(race.Tokens[0].Position.X - 55) < 0.01f && MathF.Abs(race.Tokens[1].Position.X - 30) < 0.01f);
    }

    [Fact]
    public void WatchersSeeInAConeAndNotThroughWallsOrDarkness()
    {
        var guard = new Watcher { Range = 100, PassivePerception = 12 };
        Wall[] walls = { new(new Vector2(50, -100), new Vector2(50, -10)) };
        Assert.True(Stealth.Sees(guard, new Vector2(40, 0), walls));
        Assert.False(Stealth.Sees(guard, new Vector2(-40, 0), walls));
        Assert.False(Stealth.Sees(guard, new Vector2(150, 0), walls));
        Assert.False(Stealth.Sees(guard, new Vector2(80, -40), walls)); // behind the wall
        Assert.True(Stealth.Sees(guard with { Alert = true }, new Vector2(-40, 0), walls));

        Func<Vector2, LightLevel> dark = _ => LightLevel.Dark;
        Assert.False(Stealth.Sees(guard, new Vector2(40, 0), walls, dark));
        Assert.True(Stealth.Sees(guard with { DarkRange = 60 }, new Vector2(40, 0), walls, dark));

        List<Vector2> cone = Stealth.VisionCone(guard, walls, 8);
        Assert.Equal(10, cone.Count);
        Assert.Equal(guard.Position, cone[0]);
        Assert.Contains(cone, p => p.X <= 50.01f && p.Y < -10 && Stealth.Length(p) < 99);
    }

    [Fact]
    public void SneakingCrossesAConeWithChecksAlongTheWay()
    {
        // Walking across the cone: one check on the way in, then one every 5 units, none outside it.
        var guard = new Watcher { Range = 100, PassivePerception = 12 };
        var random = new Rng(7);
        var rules = new StealthRules();
        var tracker = new StealthTracker(rules);
        Watcher[] watchers = { guard };
        Wall[] none = Array.Empty<Wall>();
        List<StealthCheck> checks = tracker.Move(new Vector2(30, 60), new Vector2(30, -60), true, 30, watchers, none, random);
        Assert.InRange(checks.Count, 20, 22); // in view from y = 52 to -52
        Assert.All(checks, c => Assert.True(!c.Spotted && c.Dc == 12 && c.Total == c.Roll + 30 + rules.BrightBonus));
        Assert.Empty(tracker.Move(new Vector2(200, 0), new Vector2(200, 10), true, 30, watchers, none, random));

        // Walking in the open gets you seen at once; a hopeless sneaker is spotted and stops there.
        tracker.Reset();
        StealthCheck seen = Assert.Single(tracker.Move(new Vector2(30, 60), new Vector2(30, -60), false, 0, watchers, none, random));
        Assert.True(seen.Spotted && seen.At.Y < 60);
        tracker.Reset();
        Assert.True(Assert.Single(tracker.Move(new Vector2(30, 60), new Vector2(30, -60), true, -40, watchers, none, random)).Spotted);

        // Light changes the check.
        tracker.Reset();
        Watcher[] seeing = { guard with { DarkRange = 200 } };
        StealthCheck inDark = Assert.Single(tracker.Move(new Vector2(30, 0), new Vector2(30, 0), true, 0, seeing, none, random, _ => LightLevel.Dark));
        Assert.Equal(inDark.Roll + rules.DarkBonus, inDark.Total);

        // The ruleset writes metres; on the map that is world units at 5 feet a square.
        Assert.Equal(5 / (5 * 0.3048f) * 64, Stealth.CheckEveryOnMap(rules, 5), 3);
    }
}
