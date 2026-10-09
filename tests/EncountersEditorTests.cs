using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

/// <summary>Encounters mode of Create: its commands, undo, what it writes to chapter.json, and the game playing what it saves.</summary>
public class EncountersEditorTests
{
    // A chapter written by hand, with fields the editor has no tool for.
    private const string Keep = """
        {
          "id": "keep",
          "title": "Keep",
          "map": "map.json",
          "xpPerVictory": 50,
          "party": [{"name": "Ana", "class": "fighter", "at": [1, 1]}],
          "npcs": [{"id": "wren", "name": "Wren", "at": [2, 1], "dialogue": "dialogue/wren.json"}],
          "containers": [{"id": "box", "at": [3, 1]}],
          "encounters": [
            {"id": "hall", "set": ["hall_clear"], "text": "Goblins!", "surrender": "dialogue/yield.json", "creatures": [
              {"creature": "goblin", "name": "Gob", "facing": 180, "at": [5, 1]},
              {"creature": "goblin", "at": [5, 2], "ai": "lookout", "surrender": "dialogue/gob.json"},
              {"creature": "goblin-boss", "name": "Grak", "at": [6, 3], "ai": {"fleeHp": 0.6}}
            ]},
            {"id": "cellar", "creatures": [{"creature": "goblin", "at": [1, 4]}]}
          ],
          "aiChanges": [{"when": ["hall_clear"], "encounter": "cellar", "ai": "brute"}],
          "victoryText": "Won {xp}"
        }
        """;

    private static EncountersEditor.Catalog TestCatalog()
    {
        var catalog = new EncountersEditor.Catalog();
        catalog.Creatures["goblin"] = new("Goblin", 1, new ContentColor(120, 150, 60), 0.36);
        catalog.Creatures["goblin-boss"] = new("Goblin boss", 3, new ContentColor(150, 90, 60), 0.42);
        foreach (string ai in new[] { "brute", "coward", "lookout" })
        {
            catalog.Ai.Add(ai);
        }
        catalog.Items["mace"] = "Mace";
        catalog.Items["shield"] = "Shield";
        return catalog;
    }

    // An 8 x 6 room with a pillar at 4, 4.
    private static bool Room(Cell cell) => cell.X >= 0 && cell.Y >= 0 && cell.X < 8 && cell.Y < 6 && cell != new Cell(4, 4);

    private static EncountersEditor KeepEditor(History history)
    {
        var editor = new EncountersEditor(history);
        Assert.True(editor.Load(Keep, out string error, TestCatalog(), Room), error);
        return editor;
    }

    private static JsonNode Saved(EncountersEditor editor) => JsonNode.Parse(editor.ToJson())!;

    private static JsonNode Encounter(EncountersEditor editor, int index) => Saved(editor)["encounters"]![index]!;

    [Fact]
    public void LoadsAndSaves()
    {
        var history = new History();
        var editor = new EncountersEditor(history);
        Assert.True(!editor.Load("{not json", out string error) && !editor.Loaded && error.Length > 0, "A broken chapter is refused with a reason");
        Assert.True(editor.Load(Keep, out error, TestCatalog(), Room) && editor.Loaded, error);
        Assert.True(editor.Groups.Count == 2 && editor.Groups[0].Id == "hall" && editor.Groups[0].Creatures.Count == 3
            && editor.Groups[0].Text == "Goblins!" && editor.Groups[0].Set.SequenceEqual(new[] { "hall_clear" }), "It has the groups of the file");
        EncountersEditor.Placement gob = editor.Groups[0].Creatures[0];
        Assert.True(gob.Creature == "goblin" && gob.Name == "Gob" && gob.At == new Cell(5, 1) && gob.Facing == 180, "Creatures are read with their names, cells and facing");
        Assert.True(EncountersEditor.ProfileOf(editor.Groups[0].Creatures[1].Ai) == "lookout" && EncountersEditor.ProfileOf(editor.Groups[0].Creatures[2].Ai) == "custom"
            && EncountersEditor.ProfileOf(gob.Ai) == "", "An AI entry is a profile's name, changes of its own, or nothing");
        Assert.True(editor.FixedOnes.Count == 3 && editor.FixedOnes[0].Name == "Ana" && editor.FixedOnes[2].Kind == EncountersEditor.FixedKind.Chest,
            "Heroes, NPCs and chests are known, to keep clear of");
        Assert.True(editor.ChapterXp == 50 && editor.XpOf(0) == 50, "A group without XP of its own gives the chapter's");
        Assert.True(history.Count == 0 && !history.Dirty && editor.Problems().Count == 0, "Loading is not an edit, and the chapter has nothing wrong");

        JsonObject saved = Saved(editor).AsObject();
        Assert.True(saved.First().Key == "id" && (string?)saved["title"] == "Keep" && saved["party"]!.AsArray().Count == 1 && saved["npcs"]!.AsArray().Count == 1
            && (string?)saved["victoryText"] == "Won {xp}", "The rest of the file is written back as it was, in its order");
        JsonNode hall = saved["encounters"]![0]!;
        Assert.True((string?)hall["surrender"] == "dialogue/yield.json" && (string?)hall["creatures"]![1]!["surrender"] == "dialogue/gob.json"
            && (double)hall["creatures"]![2]!["ai"]!["fleeHp"]! == 0.6, "Fields the editor has no tool for are kept");
        Assert.True((int)hall["creatures"]![0]!["at"]![0]! == 5 && (double)hall["creatures"]![0]!["facing"]! == 180 && hall["creatures"]![1]!["facing"] == null
            && hall["creatures"]![1]!["name"] == null && (string?)hall["set"]![0] == "hall_clear", "Creatures are written as the game reads them");
        Assert.Equal("cellar", (string?)saved["aiChanges"]![0]!["encounter"]);

        var twice = new EncountersEditor(history);
        Assert.True(!twice.Load("""{"encounters":[{"id":"a","creatures":[]},{"id":"a","creatures":[]}]}""", out error) && error.Length > 0, "Two groups with one id are refused");
        var bare = new EncountersEditor(history);
        Assert.True(bare.Load("""{"id":"empty","party":[]}""", out _) && bare.Groups.Count == 0 && bare.ChapterXp == 0, "A chapter with no encounters loads");
        Assert.True(Saved(bare)["xpPerVictory"] == null && Saved(bare)["encounters"]!.AsArray().Count == 0, "... and saves with none");
    }

    [Fact]
    public void SeveralCreaturesArePlacedAsOneChange()
    {
        var history = new History();
        EncountersEditor editor = KeepEditor(history);
        Assert.False(editor.AddCreatures(1, "goblin", new[] { new Cell(2, 4), new Cell(4, 4) }), "One square nobody can stand on refuses them all");
        Assert.False(editor.AddCreatures(1, "goblin", new[] { new Cell(2, 4), new Cell(2, 4) }));
        Assert.Single(editor.Groups[1].Creatures);
        Assert.True(editor.AddCreatures(1, "goblin", new[] { new Cell(2, 4), new Cell(3, 4) }));
        Assert.Equal(3, editor.Groups[1].Creatures.Count);
        Assert.True(history.Count == 1 && history.Undo() && editor.Groups[1].Creatures.Count == 1, "Undo takes them all away");
    }

    [Fact]
    public void Creatures()
    {
        var history = new History();
        EncountersEditor editor = KeepEditor(history);

        int? rat = editor.AddCreature(1, "goblin", new Cell(2, 4));
        Assert.True(rat == 1 && editor.Groups[1].Creatures.Count == 2 && editor.CreatureAt(new Cell(2, 4)) == (1, 1), "A creature is placed in a group");
        Assert.True(editor.AddCreature(1, "dragon", new Cell(3, 4)) == null && editor.AddCreature(5, "goblin", new Cell(3, 4)) == null, "Unknown creatures and groups are refused");
        Assert.True(editor.AddCreature(1, "goblin", new Cell(1, 1)) == null && editor.AddCreature(1, "goblin", new Cell(2, 1)) == null
            && editor.AddCreature(1, "goblin", new Cell(3, 1)) == null && editor.AddCreature(1, "goblin", new Cell(5, 1)) == null, "Not on a hero, an NPC, a chest or another creature");
        Assert.True(editor.AddCreature(1, "goblin", new Cell(4, 4)) == null && editor.AddCreature(1, "goblin", new Cell(8, 0)) == null
            && !editor.Free(new Cell(4, 4)) && editor.Free(new Cell(3, 4)), "Not where nobody can stand");
        Assert.True(history.Count == 1 && history.Undo() && editor.Groups[1].Creatures.Count == 1 && editor.CreatureAt(new Cell(2, 4)) == null, "Undo takes the creature away");
        Assert.True(history.Redo() && editor.Groups[1].Creatures[1].At == new Cell(2, 4), "Redo puts it back");

        Assert.True(editor.MoveCreature(1, 1, new Cell(3, 4)) && editor.Groups[1].Creatures[1].At == new Cell(3, 4), "A creature can be moved");
        Assert.True(!editor.MoveCreature(1, 1, new Cell(3, 4)) && !editor.MoveCreature(1, 1, new Cell(1, 4)) && !editor.MoveCreature(1, 1, new Cell(4, 4))
            && !editor.MoveCreature(1, 9, new Cell(6, 5)), "... but not onto its own cell, someone else or a pillar");
        Assert.True(history.Undo() && editor.Groups[1].Creatures[1].At == new Cell(2, 4), "Undo moves it back");

        foreach (string typed in new[] { "R", "Ra", "Rat" })
        {
            editor.SetCreatureName(1, 1, typed);
        }
        editor.EndTyping();
        Assert.True(editor.Groups[1].Creatures[1].Name == "Rat" && editor.SetCreatureName(1, 1, "Rats"), "A creature can be named");
        editor.EndTyping();
        Assert.True(history.Undo() && editor.Groups[1].Creatures[1].Name == "Rat", "Typing again later is an undo step of its own");
        Assert.True(history.Undo() && editor.Groups[1].Creatures[1].Name == "", "Typing a name is one undo step");

        Assert.True(editor.SetFacing(0, 0, 90) && editor.FacingOf(editor.Groups[0].Creatures[0]) == 90, "A creature can be turned");
        Assert.True(!editor.SetFacing(0, 0, 90) && !editor.SetFacing(0, 0, 400), "The same way again or more than a full turn is refused");
        Assert.True(editor.SetFacing(0, 0, null) && editor.Groups[0].Creatures[0].Facing == null && editor.FacingOf(editor.Groups[0].Creatures[0]) == 180,
            "With no facing it looks toward where the party starts");
        Assert.Null(Encounter(editor, 0)["creatures"]![0]!["facing"]);
        Assert.True(history.Undo() && history.Undo() && editor.Groups[0].Creatures[0].Facing == 180, "Undo turns it back");

        Assert.True(editor.SetCreatureAi(0, 0, "coward") && EncountersEditor.ProfileOf(editor.Groups[0].Creatures[0].Ai) == "coward", "A creature can be given an AI profile");
        Assert.Equal("coward", (string?)Encounter(editor, 0)["creatures"]![0]!["ai"]);
        Assert.True(!editor.SetCreatureAi(0, 0, "coward") && !editor.SetCreatureAi(0, 0, "genius"), "The same profile again or one the package lacks is refused");
        Assert.True(editor.SetCreatureAi(0, 2, "") && editor.Groups[0].Creatures[2].Ai == "", "Picking none clears changes written by hand");
        Assert.True(history.Undo() && EncountersEditor.ProfileOf(editor.Groups[0].Creatures[2].Ai) == "custom", "Undo brings them back");

        int? moved = editor.MoveToGroup(0, 2, 1);
        Assert.True(moved != null && editor.Groups[0].Creatures.Count == 2 && editor.Groups[1].Creatures[moved.Value].Name == "Grak", "A creature can change group and keeps its place on the map");
        Assert.True(editor.MoveToGroup(1, 0, 1) == null && editor.MoveToGroup(1, 0, 7) == null, "Its own group or one that isn't there is refused");
        Assert.True(history.Undo() && editor.Groups[0].Creatures.Count == 3 && editor.Groups[0].Creatures[2].Name == "Grak", "Undo puts it back where it was in the list");

        Assert.True(editor.RemoveCreature(0, 1) && editor.Groups[0].Creatures.Count == 2 && editor.Groups[0].Creatures[1].Name == "Grak" && !editor.RemoveCreature(0, 5),
            "A creature can be removed");
        Assert.True(history.Undo() && editor.Groups[0].Creatures.Count == 3 && EncountersEditor.ProfileOf(editor.Groups[0].Creatures[1].Ai) == "lookout",
            "Undo brings it back as it was");
    }

    [Fact]
    public void Groups()
    {
        var history = new History();
        EncountersEditor editor = KeepEditor(history);

        int? added = editor.AddGroup();
        Assert.True(added == 2 && editor.Groups[2].Id == "encounter-3" && editor.Groups[2].Creatures.Count == 0, "A new group gets an id of its own");
        Assert.True(editor.AddGroup("hall") == null && editor.AddGroup("yard") == 3, "A group can be named, but not like another");
        List<EncountersEditor.Problem> empty = editor.Problems();
        Assert.True(empty.Count == 2 && !empty[0].Error, "A group with nobody in it is pointed out, as a warning");
        Assert.Equal(2, Saved(editor)["encounters"]!.AsArray().Count);
        history.Undo();
        Assert.True(editor.AddCreature(2, "goblin", new Cell(6, 5)) != null && Saved(editor)["encounters"]!.AsArray().Count == 3 && editor.Problems().Count == 0,
            "With a creature in it the group is saved");

        Assert.True(editor.SetGroupId(1, "vault") && editor.Groups[1].Id == "vault", "A group can be renamed");
        Assert.Equal("vault", (string?)Saved(editor)["aiChanges"]![0]!["encounter"]);
        Assert.True(!editor.SetGroupId(1, "hall") && !editor.SetGroupId(1, "") && !editor.SetGroupId(1, new string('a', 65)), "Taken, empty and overlong ids are refused");
        editor.EndTyping();
        Assert.True(history.Undo() && editor.Groups[1].Id == "cellar" && (string?)Saved(editor)["aiChanges"]![0]!["encounter"] == "cellar",
            "Undo gives it and the story changes the old id back");

        Assert.True(editor.SetGroupText(0, "Goblins ahead!") && editor.SetGroupFlags(0, new[] { "hall_clear", "alarm" }) && editor.SetGroupAi(0, "brute"),
            "A group's line, flags and AI can be changed");
        JsonNode hall = Encounter(editor, 0);
        Assert.True((string?)hall["text"] == "Goblins ahead!" && (string?)hall["set"]![1] == "alarm" && (string?)hall["ai"] == "brute", "They are written to the file");
        Assert.True(!editor.SetGroupFlags(0, new[] { "ok", "" }) && !editor.SetGroupAi(0, "genius") && !editor.SetGroupText(0, "Goblins ahead!"),
            "Blank flags, unknown AI and no change are refused");
        Assert.True(editor.SetGroupText(0, "") && Encounter(editor, 0)["text"] == null, "An empty line is not written");

        int steps = history.Count;
        Assert.True(editor.RemoveGroup(1) && editor.Groups.Count == 2 && editor.Groups[1].Id == "encounter-3" && !editor.RemoveGroup(9), "A group can be removed");
        Assert.Null(Saved(editor)["aiChanges"]);
        Assert.True(history.Count == steps + 1 && history.Undo() && editor.Groups.Count == 3 && editor.Groups[1].Id == "cellar"
            && Saved(editor)["aiChanges"]!.AsArray().Count == 1, "Undo brings back the group, its creatures and the story changes");
    }

    [Fact]
    public void XpAndLoot()
    {
        var history = new History();
        EncountersEditor editor = KeepEditor(history);

        Assert.True(editor.ProposedXp(0) == 125 && editor.ProposedXp(1) == 25, "XP is proposed from the levels of the creatures in a group");
        Assert.True(editor.SetGroupXp(0, editor.ProposedXp(0)) && editor.XpOf(0) == 125 && editor.XpOf(1) == 50, "A group can give XP of its own");
        Assert.True(editor.SetChapterXp(80) && editor.XpOf(1) == 80 && editor.XpOf(0) == 125, "The chapter's XP is for the groups without");
        Assert.True(!editor.SetGroupXp(0, -1) && !editor.SetGroupXp(0, 125) && !editor.SetChapterXp(-5) && !editor.SetChapterXp(80), "Negative or unchanged XP is refused");
        JsonNode saved = Saved(editor);
        Assert.True((int)saved["encounters"]![0]!["xp"]! == 125 && saved["encounters"]![1]!["xp"] == null && (int)saved["xpPerVictory"]! == 80, "Both are written to the file");
        Assert.True(editor.SetGroupXp(0, null) && Encounter(editor, 0)["xp"] == null && editor.XpOf(0) == 80, "Clearing a group's XP goes back to the chapter's");
        editor.EndTyping();
        Assert.True(history.Undo() && editor.XpOf(0) == 125, "Undo brings its own XP back");

        var loot = new LootTable { Coins = "2d6", Items = new List<LootEntry> { new("mace", 0.5, 2) } };
        Assert.True(editor.SetGroupLoot(1, loot) && editor.Groups[1].Loot.Items.Count == 1, "A group can leave loot");
        JsonNode written = Encounter(editor, 1)["loot"]!;
        Assert.True((string?)written["coins"] == "2d6" && (string?)written["items"]![0]!["item"] == "mace" && (double)written["items"]![0]!["chance"]! == 0.5
            && (int)written["items"]![0]!["quantity"]! == 2, "It is written as a loot table");
        Assert.False(editor.SetGroupLoot(1, loot), "The same loot again is not an edit");
        Assert.False(editor.SetGroupLoot(1, new LootTable { Coins = "lots", Items = loot.Items }), "Coins that aren't dice are refused");
        Assert.False(editor.SetGroupLoot(1, new LootTable { Coins = "2d6", Items = new List<LootEntry> { new("mace", 0.5, 2), new("crown") } }), "... and so is an item the package lacks");
        Assert.True(!editor.SetGroupLoot(1, new LootTable { Coins = "2d6", Items = new List<LootEntry> { new("mace", 2, 2) } }) && editor.Groups[1].Loot.Items[0].Chance == 0.5,
            "... and a chance over 1");
        editor.EndTyping();
        Assert.True(history.Undo() && editor.Groups[1].Loot.IsEmpty && Encounter(editor, 1)["loot"] == null, "Undo takes the loot away");
    }

    [Fact]
    public void Problems()
    {
        var history = new History();
        var editor = new EncountersEditor(history);
        // the map changes under the creatures when a wall is painted in map mode
        bool walled = false;
        Assert.True(editor.Load(Keep, out _, TestCatalog(), cell => Room(cell) && !(walled && cell == new Cell(5, 1))));
        Assert.Empty(editor.Problems());
        walled = true;
        List<EncountersEditor.Problem> stuck = editor.Problems();
        Assert.True(stuck.Count == 1 && stuck[0].Error && stuck[0].Text.Contains("Gob (hall)"), "A creature walled in is pointed out by name and group");

        var strange = new EncountersEditor(history);
        Assert.True(strange.Load("""
            {"id":"odd","party":[{"name":"Ana","class":"fighter","at":[1,1]}],"encounters":[{"id":"den","ai":"genius","loot":{"items":["crown"]},"creatures":[
              {"creature":"dragon","at":[1,1]},{"creature":"goblin","at":[2,2]},{"creature":"goblin","at":[2,2],"ai":"sly"}]}]}
            """, out _, TestCatalog(), Room), "A chapter with things wrong still opens");
        List<EncountersEditor.Problem> found = strange.Problems();
        bool Says(string words) => found.Any(p => p.Error && p.Text.Contains(words));
        Assert.True(found.Count == 6 && Says("unknown creature dragon") && Says("profile genius") && Says("profile sly") && Says("unknown item crown"),
            "Unknown creatures, AI profiles and items are listed");
        Assert.True(Says("dragon (den) shares a cell") && Says("goblin (den) shares a cell"), "... and so is everyone on a taken cell");
    }

    // What the editor saves plays: the game loads it, and a group's XP and loot are what a win gives.
    [Fact]
    public void TheGamePlaysWhatItSaves()
    {
        var files = new Dictionary<string, string>
        {
            ["chapters/yard/chapter.json"] = """
                {"id":"yard","title":"Yard","map":"map.json","xpPerVictory":10,"victoryText":"Won {xp}",
                 "party":[{"name":"Ana","class":"fighter","at":[3,3]},{"name":"Bo","class":"cleric","at":[3,4]}],
                 "encounters":[{"id":"gate","creatures":[{"creature":"goblin","name":"Gik","at":[5,3]}]}]}
                """,
            ["chapters/yard/map.json"] = """
                {"name":"Yard","tiles":{"floor":{"art":"grass"},"wall":{"art":"wall","walkable":false,"blocksSight":true}},
                 "legend":{".":"floor","#":"wall"},"layers":[{"name":"ground","rows":["########","#......#","#......#","#......#","#......#","#......#","#......#","########"]}]}
                """,
            ["rulesets/yorehold/actions/finish-test.json"] = """{"id":"finish-test","cost":0,"effects":[{"do":"damage","dice":10000,"target":"enemies"}]}""",
        };
        using WorldFixture before = WorldFixture.LoadJson("chapters/yard", files, 5);
        Chapter chapter = before.World.Chapter;

        var history = new History();
        var editor = new EncountersEditor(history);
        Assert.True(editor.Load(files["chapters/yard/chapter.json"], out string error, EncountersEditor.Catalog.From(chapter.Compendium), chapter.Map.Walkable), error);
        Assert.True(editor.Names.Creatures.ContainsKey("goblin") && editor.Names.Ai.Contains("lookout") && editor.Names.Items.ContainsKey("mace"), "The catalog is the chapter's compendium");
        Assert.True(editor.AddCreature(0, "goblin", new Cell(0, 0)) == null && editor.AddCreature(0, "goblin", new Cell(5, 4)) != null, "Walls are the map's own");
        editor.SetCreatureName(0, 1, "Nok");
        editor.SetFacing(0, 1, 270);
        editor.SetCreatureAi(0, 1, "lookout");
        editor.SetGroupXp(0, 120);
        Assert.True(editor.SetGroupLoot(0, new LootTable { Coins = "40", Items = new List<LootEntry> { new("mace") } }) && editor.Problems().Count == 0,
            "A second goblin, XP and loot are added");

        files["chapters/yard/chapter.json"] = editor.ToJson();
        using WorldFixture world = WorldFixture.LoadJson("chapters/yard", files, 5);
        EncounterGroup gate = world.World.Chapter.Encounters[0];
        Assert.True(gate.Creatures.Count == 2 && gate.Creatures[1].Name == "Nok" && gate.Creatures[1].Facing == 270 && gate.Creatures[1].Ai?.AsText() == "lookout"
            && gate.Xp == 120 && gate.Loot.Coins == "40" && gate.Loot.Items.Count == 1, "The chapter has the placement, its facing and AI, the XP and the loot");

        // Bo acts first and drops every enemy at once
        static bool Win(WorldFixture fight)
        {
            World w = fight.World;
            for (int i = 0; i < w.Creatures.Count; i++)
            {
                w.Creatures[i].Sheet.Stats.SetBase("dex", i == 1 ? 2000.0f : 10.0f);
            }
            fight.Fight();
            return w.CurrentCreature == 1 && fight.Use("finish-test");
        }
        int xpBefore = world.World.Creatures[0].Sheet.Xp;
        Assert.True(Win(world), "Both goblins are beaten");
        Assert.True(world.Said("Won 120") && world.World.Creatures[0].Sheet.Xp == xpBefore + 120 && world.World.Creatures[1].Sheet.Xp == xpBefore + 120,
            "The win gives the group's XP, not the chapter's");
        Assert.Single(world.World.Piles, pile => pile.Items.Any(i => i.Id == "mace") && pile.Coins >= 40);

        // a chapter that says nothing about XP or loot plays as it always did
        Assert.True(Win(before) && before.Said("Won 10") && before.World.Creatures[0].Sheet.Xp == xpBefore + 10, "Without them a win gives the chapter's XP");
    }

    [Fact]
    public void EncountersInAPackage()
    {
        using var scratch = new Scratch();
        var package = new CreatePackage(TestContent.AssetsFolder());
        Assert.True(package.New(scratch.Folder), package.Status);
        EncountersEditor? editor = package.EncountersEditor();
        MapEditor? map = package.MapEditor();
        Assert.True(editor != null && map != null && editor.Groups.Count == 0 && editor.FixedOnes.Count == 1, "Encounters mode opens a new package's chapter");
        Assert.True(editor!.Names.Creatures.ContainsKey("goblin") && editor.Names.Ai.Contains("lookout"), "The game's own creatures and AI profiles are there to place");

        string file = Path.Combine(package.PackagePath, package.Chapter, "chapter.json");
        string untouched = File.ReadAllText(file);
        Assert.True(package.Save() && File.ReadAllText(file) == untouched, "Saving with nothing changed leaves chapter.json alone");

        // one history for both modes, and the map as it is drawn right now
        map!.Paint(map.WallLayer(0), new Cell(8, 8), map.WallTile());
        map.EndStroke();
        int? group = editor.AddGroup("gate");
        Assert.True(group != null && editor.AddCreature(group.Value, "goblin", new Cell(8, 8)) == null && editor.AddCreature(group.Value, "goblin", new Cell(2, 2)) == null,
            "Nobody is placed on a wall just painted, or on the hero");
        Assert.True(editor.AddCreature(group!.Value, "goblin", new Cell(10, 8)) != null && editor.SetFacing(group.Value, 0, 180) && editor.SetGroupXp(group.Value, 75), "A goblin is placed and turned");
        Assert.True(package.History.Dirty && package.Save() && !package.History.Dirty && package.Status == "Saved 2 files", package.Status);
        JsonNode written = JsonNode.Parse(File.ReadAllText(file))!;
        Assert.True(written["encounters"]!.AsArray().Count == 1 && (string?)written["encounters"]![0]!["id"] == "gate" && (int)written["encounters"]![0]!["xp"]! == 75
            && (int)written["encounters"]![0]!["creatures"]![0]!["at"]![0]! == 10 && (string?)written["title"] == "Chapter one" && written["party"]!.AsArray().Count == 1,
            "chapter.json on disk has the group and the rest of the chapter");
        Assert.Single(Chapter.Load(package.PlayFiles(), package.Chapter).Encounters);

        package.Undo();
        package.Undo();
        package.Undo();
        Assert.True(editor.Groups[0].Creatures.Count == 0 && !map.Map().Walkable(new Cell(8, 8)), "Undo steps back through the encounter edits first");
        package.Undo();
        package.Undo();
        Assert.True(editor.Groups.Count == 0 && map.Map().Walkable(new Cell(8, 8)), "... then through the group and the wall, on the same history");
        for (int i = 0; i < 5; i++)
        {
            package.Redo();
        }
        Assert.True(editor.Groups.Count == 1 && editor.XpOf(0) == 75 && !package.History.Dirty, "Redo comes back to the saved state");

        // a wall painted over a creature would leave a chapter the game refuses: neither file is written
        string mapFile = Path.Combine(package.PackagePath, package.Chapter, "map.json");
        string mapWritten = File.ReadAllText(mapFile);
        string chapterWritten = File.ReadAllText(file);
        map.Paint(map.WallLayer(0), new Cell(10, 8), map.WallTile());
        map.EndStroke();
        Assert.True(!package.Save() && package.Status.Contains("not saved") && File.ReadAllText(mapFile) == mapWritten && File.ReadAllText(file) == chapterWritten,
            "A wall over a creature is not saved, and the status says why");
        Assert.True(editor.MoveCreature(0, 0, new Cell(11, 8)) && package.Save() && File.ReadAllText(mapFile) != mapWritten
            && (int)JsonNode.Parse(File.ReadAllText(file))!["encounters"]![0]!["creatures"]![0]!["at"]![0]! == 11, "Once the creature has moved off it both are saved");

        package.Open(package.PackagePath);
        Assert.True(package.EncountersEditor()?.Groups.Count == 1 && package.EncountersEditor()!.Groups[0].Creatures[0].Facing == 180 && !package.History.CanUndo,
            "The saved package opens again with its group");

        // the game's own content opens too, and has nothing wrong
        package.Open(TestContent.AssetsFolder());
        editor = package.EncountersEditor();
        Chapter keep = Chapter.Load(TestContent.Shipped(), package.Chapter);
        Assert.True(editor != null && editor.Groups.Count == keep.Encounters.Count && editor.Groups.Sum(g => g.Creatures.Count) == keep.Encounters.Sum(e => e.Creatures.Count)
            && editor.FixedOnes.Count == keep.Party.Count + keep.Npcs.Count + keep.Containers.Count, "The keep's encounters open in the editor");
        Assert.Empty(editor!.Problems());
        Assert.DoesNotContain(package.Problems(), p => p.Error);
    }
}
