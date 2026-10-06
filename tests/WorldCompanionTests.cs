namespace Yorehold.Rules.Tests;

/// <summary>NPCs who join the party, approval and taking them along. The C++ client's WorldCompanionTests.</summary>
public class WorldCompanionTests
{
    // A wall down the middle keeps the goblin out of sight until a test starts the fight.
    private const string Room = """
        "tiles":{"floor":{"art":"stone"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
        "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":["############","#......#...#","#......#...#","#......#...#","#......#...#","############"]}]
        """;

    // Replies, in order: be kind, ask them along, send them away, nudge tam, goodbye.
    private const int Kind = 0, Join = 1, Part = 2, Nudge = 3;

    private const string Talk = """
        {"id":"talk","start":"hello","nodes":[{"id":"hello","speaker":"Them","text":"Hello.","choices":[
            {"id":"kind","text":"Kind words.","do":["approve +10"],"next":"hello"},
            {"id":"join","text":"Come along.","do":["recruit"],"next":""},
            {"id":"part","text":"Wait here.","do":["dismiss"],"next":""},
            {"id":"nudge","text":"Tam would like this.","do":["approve tam 3"],"next":"hello"},
            {"id":"bye","text":"Bye.","next":""}]}]}
        """;

    // Four heroes and people who could join, in an adventure of two rooms. The yorehold ruleset
    // allows two companions and a party of six.
    private static Dictionary<string, string> Files()
    {
        const string party = """
            "party":[{"name":"Ana","class":"fighter","at":[1,1]},{"name":"Bo","class":"cleric","at":[1,2]},
                {"name":"Cy","class":"rogue","at":[1,3]},{"name":"Di","class":"wizard","at":[1,4]}]
            """;
        return new Dictionary<string, string>
        {
            ["adventure.json"] = """
                {"id":"pair","title":"Pair","chapters":["chapters/yard","chapters/far"],
                 "transitions":[{"from":"yard","exitMarker":"east","to":"far","entryMarker":"west"},
                    {"from":"far","exitMarker":"west","to":"yard","entryMarker":"back"}]}
                """,
            ["chapters/yard/chapter.json"] = """{"id":"yard","title":"The Yard",""" + party + """
                ,"encounters":[{"id":"g","creatures":[{"creature":"goblin","at":[10,2]}]}],
                "npcs":[{"id":"tam","name":"Tam","at":[4,1],"dialogue":"talk.json","companion":{"joinAt":10,"leaveAt":-20,"flags":{"burned":-40}}},
                    {"id":"bel","name":"Bel","at":[4,2],"dialogue":"talk.json","companion":{}},
                    {"id":"cob","name":"Cob","at":[4,3],"dialogue":"talk.json","companion":{"approval":5}},
                    {"id":"dee","name":"Dee","at":[4,4],"dialogue":"talk.json"}]}
                """,
            ["chapters/yard/talk.json"] = Talk,
            ["chapters/yard/map.json"] = "{" + Room + ""","markers":{"east":[6,4],"back":[5,1]}}""",
            ["chapters/far/chapter.json"] = """{"id":"far","title":"Far Away",""" + party + "}",
            ["chapters/far/map.json"] = "{" + Room + ""","markers":{"west":[2,2]}}""",
        };
    }

    private static bool InFight(World w, int creature)
    {
        return w.Encounter!.Order.Any(c => ReferenceEquals(c.Sheet, w.Creatures[creature].Sheet) && c.Team == 0);
    }

    [Fact]
    public void Joining()
    {
        using WorldFixture world = WorldFixture.LoadJson("chapters/yard", Files(), 3);
        World w = world.World;
        int tam = w.CompanionToken("tam")!.Value, bel = w.CompanionToken("bel")!.Value, cob = w.CompanionToken("cob")!.Value;
        Assert.True(tam >= w.HeroCount && !w.Companion(tam) && w.Companions.Approval("tam") == 0 && w.Companions.Approval("cob") == 5
            && w.CompanionToken("dee") == null, "NPCs with a companion entry are met with their starting approval; others aren't companions");
        Assert.True(w.Rules.Companions.Limit == 2 && w.Rules.Companions.PartyLimit == 6, "The party cap is ruleset data");

        world.Say(tam, Join);
        Assert.True(!w.Companion(tam) && world.Said("isn't ready"), "Below their joinAt they won't come");
        world.Say(tam, Kind);
        Assert.True(w.Companions.Approval("tam") == 10 && world.Said("Tam approves (+10)"), "Approval moves with dialogue choices");
        world.Say(tam, Join);
        Assert.True(w.Companion(tam) && w.Creatures[tam].Team == 0 && w.Tokens.Follows(tam) != null && world.Said("Tam joins the party"),
            "Asked again they join, on the party's side and walking with it");
        world.Say(bel, Join);
        world.Say(cob, Join);
        Assert.True(w.Companion(bel) && !w.Companion(cob) && world.Said("The party is full") && w.HeroCount + w.Companions.Members.Count == 6,
            "Four heroes and two companions fill the party");
        world.Say(w.NpcStart + 3, Join);
        Assert.True(world.Said("Dee won't join"), "Someone without a companion entry never joins");

        world.Say(bel, Part);
        Assert.True(!w.Companion(bel) && w.Creatures[bel].Team == 2 && w.Tokens.Follows(bel) == null && world.Said("Bel leaves the party"),
            "A companion can be sent away and stays as an NPC");
        world.Say(cob, Join);
        Assert.True(w.Companion(cob), "That makes room for another");
        world.Say(cob, Nudge);
        Assert.True(w.Companions.Approval("tam") == 13, "One conversation can change what another companion thinks");

        world.Fight();
        Assert.True(InFight(w, tam) && InFight(w, cob) && !InFight(w, bel), "Companions fight on the party's side");
    }

    [Fact]
    public void ApprovalFromFlags()
    {
        using WorldFixture world = WorldFixture.LoadJson("chapters/yard", Files(), 3);
        World w = world.World;
        int tam = w.CompanionToken("tam")!.Value;
        world.Say(tam, Kind);
        world.Say(tam, Join);
        world.SetFlags("burned");
        Assert.True(!w.Companion(tam) && w.Companions.Approval("tam") == -30 && world.Said("Tam disapproves (-40)") && world.Said("Tam leaves the party"),
            "A story flag changes approval, and falling to leaveAt makes them leave");
        world.SetFlags("burned", "other");
        Assert.True(w.Companions.Approval("tam") == -30, "A flag counts once");
    }

    [Fact]
    public void SavingAndTravelling()
    {
        using WorldFixture world = WorldFixture.LoadJson("chapters/yard", Files(), 5);
        World w = world.World;
        int tam = w.CompanionToken("tam")!.Value;
        world.Say(tam, Kind);
        world.Say(tam, Join);
        w.Creatures[tam].Sheet.Hp -= 1;
        int hp = w.Creatures[tam].Sheet.Hp;

        using WorldFixture loaded = WorldFixture.LoadJson("chapters/yard", Files(), 5);
        Assert.True(loaded.World.Restore(w.StateJson().ToJsonString()) && loaded.World.Companion(tam) && loaded.World.Tokens.Follows(tam) != null
            && loaded.World.Companions.Approval("tam") == 10, "A save keeps who joined and what they think");

        world.Put(0, new Cell(6, 4));
        world.Step(0.1);
        Assert.True(w.Chapter.Id == "far", "The party travels on");
        int? along = w.CompanionToken("tam");
        Assert.True(along is int a && w.Companion(a) && w.Creatures[a].Sheet.Hp == hp && w.Creatures.Count == w.HeroCount + 1
            && w.Walkable(w.CellOf(a)), "Companions come along to the next chapter as they were");
        Assert.True(w.Talkable(along!.Value), "and can still be talked to there");

        string far = w.StateJson().ToJsonString();
        using WorldFixture again = WorldFixture.LoadJson("chapters/yard", Files(), 5);
        Assert.True(again.World.Restore(far) && again.World.Chapter.Id == "far" && again.World.CompanionToken("tam") is int back
            && again.World.Companion(back) && again.World.Creatures[back].Sheet.Hp == hp, "A save in another chapter brings the companion back");

        Assert.True(w.MakeCamp() && w.AtCamp, "Make camp with a companion");
        Assert.True(w.CompanionToken("tam") is int atCamp && w.Companion(atCamp), "Companions come to camp");
        w.Creatures[w.CompanionToken("tam")!.Value].Sheet.Hp = hp - 1;
        Assert.True(w.LeaveCamp() && w.Chapter.Id == "far" && w.CompanionToken("tam") is int home
            && w.Creatures[home].Sheet.Hp == hp - 1 && w.Creatures.Count == w.HeroCount + 1, "and back again, without a second copy");

        world.Put(0, new Cell(3, 3));
        world.Step(0.1);
        world.Put(0, new Cell(2, 2));
        world.Step(0.1);
        Assert.True(w.Chapter.Id == "yard" && w.CompanionToken("tam") == tam && w.Companion(tam) && w.Creatures.Count == w.HeroCount + 1 + 4,
            "Back in their own chapter they are the NPC there, not a copy");
    }
}
