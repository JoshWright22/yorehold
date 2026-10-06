namespace Yorehold.Rules.Tests;

/// <summary>Chapters of an adventure and camp. The C++ client's WorldTravelTests and WorldCampTests.</summary>
public class WorldTravelTests
{
    private const string Room = """
        "tiles":{"floor":{"art":"stone"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
        "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":["########","#......#","#......#","#......#","#......#","########"]}]
        """;

    private const string WideRoom = """
        "tiles":{"floor":{"art":"stone"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
        "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":["##########","#........#","#........#","#........#","#........#","##########"]}]
        """;

    // Two small rooms. East of the first leads into the second and back the same way; a secret way
    // back opens once its flag is set.
    public static Dictionary<string, string> AdventureFiles()
    {
        return new Dictionary<string, string>
        {
            ["adventure.json"] = """
                {"id":"two-rooms","title":"Two Rooms","minLevel":1,"maxLevel":3,"recommendedPartySize":2,
                 "chapters":["chapters/adv-a","chapters/adv-b"],
                 "transitions":[{"from":"adv-a","exitMarker":"east","to":"adv-b","entryMarker":"west"},
                    {"from":"adv-b","exitMarker":"west","to":"adv-a","entryMarker":"east"},
                    {"from":"adv-b","exitMarker":"secret","to":"adv-a","entryMarker":"hidden","when":["found-secret"]}],
                 "flags":["met-guide","found-secret"]}
                """,
            ["chapters/adv-a/chapter.json"] = """
                {"id":"adv-a","title":"The First Room","localFlags":["a-local"],
                 "party":[{"name":"Ana","class":"fighter","at":[2,2]},{"name":"Bo","class":"cleric","at":[2,3]}]}
                """,
            ["chapters/adv-a/map.json"] = "{" + Room + ""","markers":{"east":[6,2],"hidden":[1,4]}}""",
            ["chapters/adv-b/chapter.json"] = """
                {"id":"adv-b","title":"The Second Room","level":2,
                 "party":[{"name":"Cy","class":"fighter","at":[3,2]},{"name":"Di","class":"cleric","at":[3,3]}]}
                """,
            ["chapters/adv-b/map.json"] = "{" + Room + ""","markers":{"west":[1,2],"secret":[6,4]}}""",
        };
    }

    // A room with a goblin and a chest, and a camp of its own (over the shared one) with four seats.
    public static Dictionary<string, string> CampFiles(string roomExtra = "")
    {
        return new Dictionary<string, string>
        {
            ["chapters/wild/chapter.json"] = """{"id":"wild","title":"The Wild",""" + roomExtra + """
                "party":[{"name":"Ana","class":"fighter","at":[2,2]},{"name":"Bo","class":"cleric","at":[2,3]}],
                "encounters":[{"id":"g","creatures":[{"creature":"goblin","at":[7,4]}]}],
                "containers":[{"id":"box","name":"Box","at":[3,4],"coins":50}]}
                """,
            ["chapters/wild/map.json"] = "{" + WideRoom + "}",
            ["chapters/camp/chapter.json"] = """
                {"id":"camp","title":"The Camp",
                 "party":[{"name":"A","class":"fighter","at":[2,2]},{"name":"B","class":"fighter","at":[3,2]},
                    {"name":"C","class":"fighter","at":[4,2]},{"name":"D","class":"fighter","at":[5,2]}]}
                """,
            ["chapters/camp/map.json"] = "{" + WideRoom + ""","markers":{"entry":[4,3]}}""",
        };
    }

    private static void Put(WorldFixture world, int hero, Cell cell) => world.Put(hero, cell);

    private static Item Food(World w, int units) => new(w.Chapter.Compendium.Item("supplies")!, units);

    [Fact]
    public void Travelling()
    {
        using WorldFixture world = WorldFixture.LoadJson("chapters/adv-a", AdventureFiles(), 4);
        World w = world.World;
        Assert.True(w.Adventure?.Id == "two-rooms", "A chapter listed in adventure.json plays as part of that adventure");
        Assert.True(w.ExitMarkerAt(new Cell(6, 2)) == "east" && w.ExitMarkerAt(new Cell(1, 4)) == "", "Only markers a transition leaves from are exits");

        world.SetFlags("met-guide", "a-local");
        w.Creatures[0].Sheet.Hp -= 3;
        w.Creatures[0].Sheet.Coins = 77;
        int hp = w.Creatures[0].Sheet.Hp;
        string name = w.Creatures[0].Sheet.Name;
        Put(world, 0, new Cell(6, 2));
        world.Step(0.1);
        Assert.True(w.Chapter.Id == "adv-b" && world.Said("goes on to The Second Room"), "Stepping onto an exit takes the party to the next chapter");
        Assert.True(w.CellOf(0) == new Cell(1, 2) && w.CellOf(1) != w.CellOf(0) && w.Walkable(w.CellOf(1)), "The party arrives at the entry marker");
        Assert.True(w.Creatures[0].Sheet.Name == name && w.Creatures[0].Sheet.Hp == hp && w.Creatures[0].Sheet.Coins == 77, "Heroes come along as they were, wounds and coins too");
        Assert.True(w.Flags.Contains("met-guide") && !w.Flags.Contains("a-local"), "Adventure flags travel; the old chapter's local flags stay behind");
        Assert.Contains(world.Events, e => e.Kind == WorldEventKind.ChapterChanged && e.Text == "chapters/adv-b");
        world.Step(0.5);
        Assert.True(w.Chapter.Id == "adv-b", "Arriving on a marker that leads back doesn't send the party straight back");

        Put(world, 0, new Cell(6, 4));
        world.Step(0.1);
        Assert.True(w.Chapter.Id == "adv-b", "A way whose flags aren't set stays shut");
        string saved = w.StateJson().ToJsonString();
        world.SetFlags("found-secret");
        world.Step(0.1);
        Assert.True(w.Chapter.Id == "adv-b", "Setting the flag while standing there doesn't move anyone");
        Put(world, 0, new Cell(5, 4));
        world.Step(0.1);
        Put(world, 0, new Cell(6, 4));
        world.Step(0.1);
        Assert.True(w.Chapter.Id == "adv-a" && w.CellOf(0) == new Cell(1, 4), "Once open, the secret way leads back to its own entry");

        using WorldFixture again = WorldFixture.LoadJson("chapters/adv-a", AdventureFiles(), 4);
        Assert.True(again.World.Restore(saved) && again.World.Chapter.Id == "adv-b" && again.World.Creatures[0].Sheet.Hp == hp
            && again.World.Flags.Contains("met-guide"), "A save made in the second chapter loads into it");
        Assert.Equal(saved, again.World.StateJson().ToJsonString());
    }

    [Fact]
    public void AdventuresAreChecked()
    {
        Dictionary<string, string> Broken(string path, string text)
        {
            Dictionary<string, string> files = AdventureFiles();
            files[path] = text;
            return files;
        }
        ContentException Refused(Dictionary<string, string> files) => TestContent.Refused(() => WorldFixture.LoadJson("chapters/adv-a", files, 1).Dispose());

        Assert.Contains("no marker", Refused(Broken("adventure.json", """
            {"id":"x","chapters":["chapters/adv-a","chapters/adv-b"],"transitions":[{"from":"adv-a","exitMarker":"nowhere","to":"adv-b","entryMarker":"west"}]}
            """)).Message);
        Assert.Contains("isn't in the list", Refused(Broken("adventure.json", """
            {"id":"x","chapters":["chapters/adv-a","chapters/adv-b"],"transitions":[{"from":"adv-a","exitMarker":"east","to":"adv-c","entryMarker":"west"}]}
            """)).Message);
        Assert.Contains("outside", Refused(Broken("adventure.json", """{"id":"x","maxLevel":1,"chapters":["chapters/adv-a","chapters/adv-b"]}""")).Message);
        Assert.Contains("seats", Refused(Broken("chapters/adv-b/chapter.json", """{"id":"adv-b","party":[{"name":"Cy","class":"fighter","at":[3,2]}]}""")).Message);
        Refused(Broken("adventure.json", """{"id":"x","minLevel":4,"maxLevel":2,"chapters":["chapters/adv-a"]}"""));
        using WorldFixture alone = WorldFixture.LoadJson("chapters/adv-a", Broken("adventure.json", """{"id":"x","chapters":["chapters/adv-b"]}"""), 1);
        Assert.True(alone.World.Adventure == null, "A chapter the adventure doesn't list plays on its own");
    }

    [Fact]
    public void GoingToCamp()
    {
        using WorldFixture world = WorldFixture.LoadJson("chapters/wild", CampFiles(), 7);
        World w = world.World;
        Assert.False(w.AtCamp);
        Put(world, 0, new Cell(3, 3));
        Assert.True(w.Take(0, 0, all: true));
        Cell where = w.CellOf(0);
        w.Tokens.Tokens[1].Color = new ContentColor(1, 2, 3);
        w.Creatures[1].Sheet.Hp = 1;
        Assert.True(w.MakeCamp() && w.AtCamp && w.Chapter.Id == "camp" && world.Said("makes camp"), "The party makes camp between fights");
        Assert.True(w.HeroCount == 2 && w.Creatures[0].Sheet.Name == "Ana" && w.Creatures[1].Sheet.Hp == 1 && w.Creatures[0].Sheet.Coins == 50
            && w.Tokens.Tokens[1].Color.R == 1 && w.Tokens.Tokens[1].Color.B == 3, "Camp seats as many heroes as came, as they were");
        Assert.True(w.CellOf(0) == new Cell(4, 3) && w.CellOf(1) != w.CellOf(0), "They arrive at the camp's entry marker");
        Assert.False(w.MakeCamp(), "Camp can't be made at camp");

        // A save at camp comes back at camp, still knowing the way back.
        using WorldFixture other = WorldFixture.LoadJson("chapters/wild", CampFiles(), 7);
        Assert.True(other.World.Restore(w.StateJson().ToJsonString()) && other.World.AtCamp && other.World.CanLeaveCamp && other.World.HeroCount == 2,
            "A save made at camp loads at camp");

        Assert.True(w.LeaveCamp() && !w.AtCamp && w.Chapter.Id == "wild" && w.CellOf(0) == where, "Leaving camp goes back to the same place");
        Assert.True(w.Piles[0].Coins == 0 && w.Creatures.Count == 3 && !w.Creatures[2].Sheet.Down && w.Creatures[1].Sheet.Hp == 1,
            "The chapter is as the party left it, and the heroes as they left camp");
        Assert.False(w.LeaveCamp(), "Only a party at camp can leave it");
        Assert.True(other.World.LeaveCamp() && other.World.Chapter.Id == "wild" && other.World.Piles[0].Coins == 0, "The loaded save leaves camp the same way");

        using WorldFixture shut = WorldFixture.LoadJson("chapters/wild", CampFiles("\"camp\":false,"), 7);
        Assert.True(!shut.World.MakeCamp() && !shut.World.AtCamp, "A chapter can forbid making camp");
    }

    [Fact]
    public void RestingAtCamp()
    {
        using WorldFixture world = WorldFixture.LoadJson("chapters/wild", CampFiles(), 7);
        World w = world.World;
        RestDefinition longRest = w.Rules.Rest("long")!;
        Assert.True(longRest.CampOnly && longRest.SupplyCost == 40, "The game's long rest is taken at camp and costs 40 supplies");
        w.Creatures[0].Sheet.Hp = 3;
        w.Creatures[0].Sheet.Inventory.Add(Food(w, 3));
        Assert.True(!w.Rest("long") && w.Refusal.Contains("Make camp"), "The long rest waits for camp");
        w.MakeCamp();
        Assert.True(w.SuppliesHeld() == 30 && !w.Rest("long") && w.Refusal.Contains("30 of 40"), "Without enough supplies the long rest is refused");
        Assert.True(w.ToStash(0, w.Creatures[0].Sheet.Inventory.Count - 1));
        Assert.True(w.Stash.Items.Count == 1 && w.Stash.Supplies == 30 && w.SuppliesHeld() == 30, "Supplies go in the stash");
        w.Creatures[1].Sheet.Inventory.Add(Food(w, 2));
        w.Rest("short");
        w.Rest("short");
        Assert.True(w.RestsLeft(w.Rules.Rest("short")!) == 0, "Two short rests are used up");
        Assert.True(w.Rest("long") && w.Creatures[0].Sheet.Hp == w.Creatures[0].Sheet.MaxHp, "A long rest at camp heals fully");
        Assert.True(w.SuppliesHeld() == 10 && w.Stash.Empty && w.Creatures[1].Sheet.Inventory[^1].Quantity == 1,
            "It uses the stash's supplies first, then what the heroes carry");
        Assert.True(w.RestsLeft(w.Rules.Rest("short")!) == 2, "A long rest gives the short rests back");
    }

    [Fact]
    public void StashAndRevival()
    {
        using WorldFixture world = WorldFixture.LoadJson("chapters/wild", CampFiles(), 7);
        World w = world.World;
        w.Creatures[0].Sheet.Inventory.Add(Food(w, 1));
        int carried = w.Creatures[0].Sheet.Inventory.Count;
        Assert.False(w.ToStash(0, carried - 1), "The stash is only at camp");
        w.Creatures[1].Sheet.Hp = 0;
        w.Creatures[1].Sheet.Death.Dead = true;
        w.Creatures[0].Sheet.Coins = 0;
        w.MakeCamp();
        Assert.True(w.ToStash(0, carried - 1) && w.Creatures[0].Sheet.Inventory.Count == carried - 1, "A hero puts an item in the stash");
        Assert.False(w.FromStash(1, 0), "The dead take nothing from the stash");
        Assert.True(w.FromStash(0, 0) && w.Stash.Empty && w.Creatures[0].Sheet.Inventory.Count == carried, "A hero takes it back out");

        int price = w.Rules.RevivePrice;
        Assert.True(price > 0, "The game sells revival at camp");
        Assert.True(!w.Revive(0, 1) && w.Refusal.Contains("needs"), "Revival needs the price");
        w.Creatures[0].Sheet.Coins = price + 5;
        Assert.False(w.Revive(0, 0), "Only the dead are revived");
        Assert.True(w.Revive(0, 1) && !w.Creatures[1].Sheet.Death.Dead && w.Creatures[1].Sheet.Hp > 0 && w.Creatures[0].Sheet.Coins == 5,
            "Paying the price brings a dead hero back");

        using WorldFixture away = WorldFixture.LoadJson("chapters/wild", CampFiles(), 7);
        World a = away.World;
        a.Creatures[1].Sheet.Hp = 0;
        a.Creatures[1].Sheet.Death.Dead = true;
        a.Creatures[0].Sheet.Coins = price;
        Assert.False(a.Revive(0, 1), "Revival is only at camp");
        a.MakeCamp();
        a.Creatures[0].Sheet.Inventory.Add(Food(a, 4));
        a.Rest("long");
        Assert.True(a.Creatures[1].Sheet.Death.Dead, "A long rest doesn't bring back the dead");

        a.ToStash(0, 0);
        string saved = a.StateJson().ToJsonString();
        using WorldFixture later = WorldFixture.LoadJson("chapters/wild", CampFiles(), 7);
        Assert.True(later.World.Restore(saved) && later.World.Stash.Items.Count == 1, "The stash is saved");
    }
}
