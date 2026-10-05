namespace Yorehold.Rules.Tests;

/// <summary>Maps, kits, dialogue, quests, cutscenes, chapters, adventures and the manifest.</summary>
public class ChapterLoaderTests
{
    private static readonly IReadOnlyDictionary<string, Kit> NoKits = new Dictionary<string, Kit>();

    private const string SmallMap = """
        {"name": "Yard",
         "tiles": {"grass": {"art": "grass"}, "wall": {"art": "wall", "walkable": false, "blocksSight": true},
                   "plank": {"art": "wood", "indoors": true}},
         "legend": {".": "grass", "#": "wall", "=": "plank"},
         "layers": [{"name": "ground", "rows": ["....", ".==.", ".. ."]},
                    {"name": "walls",  "rows": ["#   ", "    ", "    "]}],
         "lights": [{"at": [1.5, 1.5], "radius": 4}],
         "markers": {"start": [1, 0], "exit": [3, 2]}}
        """;

    private static Dictionary<string, Kit> ShippedKits()
    {
        ContentFiles files = TestContent.Shipped();
        return files.List("kits").ToDictionary(ContentFiles.Stem, path => Kit.Read(ContentNode.Read(files, path)));
    }

    [Fact]
    public void ATextMapReadsItsRows()
    {
        GameMap map = GameMap.Read(TestContent.Json(SmallMap), NoKits);
        Assert.Equal(("Yard", 4, 3, 3, 2), (map.Name, map.Width, map.Height, map.Types.Count, map.Layers.Count));
        Assert.True(map.Walkable(new Cell(1, 0)));
        // A wall painted over ground on a later layer, empty ground, and off the map.
        Assert.False(map.Walkable(new Cell(0, 0)));
        Assert.False(map.Walkable(new Cell(2, 2)));
        Assert.False(map.Walkable(new Cell(4, 0)));
        Assert.False(map.Walkable(new Cell(-1, 1)));
        Assert.True(map.BlocksSight(new Cell(0, 0)));
        Assert.False(map.BlocksSight(new Cell(1, 0)));
        Assert.True(map.Indoors(new Cell(1, 1)));
        Assert.False(map.Indoors(new Cell(0, 1)));
        Assert.Equal(new Cell(3, 2), map.Marker("exit"));
        Assert.Null(map.Marker("nowhere"));
        Assert.Equal("wood", map.TileAt(0, new Cell(2, 1))?.Art);
        Assert.Null(map.TileAt(1, new Cell(2, 1)));

        // Fields left out: mood lighting, a flame of the default colour, traps spotted from two squares.
        Assert.Equal((LightingMode.Mood, LightLevel.Dark, 0.5, 3.5, 8.5, MapTime.Night),
            (map.Lighting.Mode, map.Lighting.Ambient, map.Lighting.BrightFraction, map.Lighting.Carried, map.Lighting.Sight, map.Lighting.Time));
        Assert.Equal(new MapLight(1.5, 1.5, 4, new ContentColor(255, 200, 120), true, ""), map.Lights[0]);
        Assert.Equal(2, map.TrapSpotRange);
        Assert.Empty(map.Objects);
    }

    [Fact]
    public void AMapSavedByCreateReadsTheSame()
    {
        // Tile ids are the place in the array plus 1; each tile is [place in its 32 by 32 chunk, id].
        GameMap map = GameMap.Read(TestContent.Json("""
            {"tiles": [{"name": "grass", "art": "grass"}, {"name": "wall", "art": "wall", "walkable": false, "blocksSight": true}],
             "tileMap": {"width": 40, "height": 2, "tileSize": 64, "layers": [
               {"name": "ground", "default": 1, "chunks": [{"x": 0, "y": 0, "tiles": [[1, 0]]}]},
               {"name": "walls", "chunks": [{"x": 1, "y": 0, "tiles": [[33, 2]]}]},
               {"name": "roof", "floor": 1, "chunks": [{"x": 0, "y": 0, "tiles": [[0, 2]]}]}]}}
            """), NoKits);
        Assert.Equal((40, 2, 3), (map.Width, map.Height, map.Layers.Count));
        Assert.True(map.Walkable(new Cell(0, 0)));
        Assert.False(map.Walkable(new Cell(1, 0)));
        Assert.False(map.Walkable(new Cell(33, 1)));
        Assert.True(map.BlocksSight(new Cell(33, 1)));
        // Only floor 0 counts for walking and sight.
        Assert.False(map.BlocksSight(new Cell(0, 0)));
    }

    [Theory]
    [InlineData("\"rows\": [\"....\", \".==.\", \".. .\"]", "\"rows\": [\"....\", \".==.\"]", "layers[1].rows")]
    [InlineData("\".==.\"", "\".==\"", "layers[0].rows[1]")]
    [InlineData("\".==.\"", "\".=?.\"", "layers[0].rows[1]")]
    [InlineData("\"#\": \"wall\"", "\"#\": \"stone\"", "legend.#")]
    [InlineData("\"#\": \"wall\"", "\"##\": \"wall\"", "legend.##")]
    [InlineData("[1.5, 1.5]", "[9, 1]", "lights[0].at")]
    [InlineData("\"radius\": 4", "\"radius\": 0", "lights[0]")]
    [InlineData("[3, 2]", "[3, 3]", "markers.exit")]
    [InlineData("\"name\": \"Yard\"", "\"lighting\": {\"mode\": \"spooky\"}", "lighting.mode")]
    [InlineData("\"name\": \"Yard\"", "\"lighting\": {\"sight\": 0}", "lighting")]
    [InlineData("\"name\": \"Yard\"", "\"trapSpotRange\": 500", "trapSpotRange")]
    [InlineData("\"name\": \"Yard\"", "\"objects\": [{\"kit\": \"portcullis\", \"at\": [1, 0]}]", "objects[0].kit")]
    [InlineData("\"name\": \"Yard\"", "\"objects\": [{\"name\": \"a crate\", \"at\": [7, 0]}]", "objects[0]")]
    [InlineData("\"legend\"", "\"key\"", "")]
    public void ABrokenMapNamesTheField(string find, string replace, string field)
    {
        Assert.Contains(find, SmallMap);
        ContentException error = TestContent.Refused(() => GameMap.Read(TestContent.Json(SmallMap.Replace(find, replace), "chapters/yard/map.json"), NoKits));
        Assert.Equal(("chapters/yard/map.json", field), (error.File, error.Field));
    }

    [Fact]
    public void KitsArePlacedAndChangedPerCopy()
    {
        Dictionary<string, Kit> kits = ShippedKits();
        Assert.Equal(new[] { "chest", "dart-trap", "door", "lever", "locked-chest", "locked-door" }, kits.Keys.Order());
        Assert.NotNull(kits["dart-trap"].Prototype.Trap?.Effect);
        Assert.Equal("dex", kits["dart-trap"].Prototype.Trap?.Effect?.Save.Ability);

        string withObjects = SmallMap.Replace("\"name\": \"Yard\"", """
            "objects": [
              {"kit": "door", "at": [1, 0], "name": "the east door", "tags": ["link:east"], "door": {"locked": true}},
              {"kit": "chest", "at": [1, 2], "contents": {"coins": 12, "healing-potion": 1}},
              {"name": "a crate", "at": [3, 0], "tags": ["blocksMovement"]}]
            """);
        GameMap map = GameMap.Read(TestContent.Json(withObjects), kits);
        Assert.Equal(3, map.Objects.Count);

        MapObject door = map.Objects[0];
        Assert.Equal(("the east door", 64.0, 0.0), (door.Name, door.X, door.Y));
        // The copy's tags add to the kit's, and its door fields merge into the kit's door.
        Assert.Contains("link:east", door.Tags);
        Assert.Contains("blocksMovement", door.Tags);
        Assert.True(door.Door is { Locked: true, Open: false });
        Assert.False(map.Walkable(new Cell(1, 0)));
        // The kit itself is untouched.
        Assert.False(kits["door"].Prototype.Door?.Locked);
        Assert.DoesNotContain("link:east", kits["door"].Prototype.Tags);

        Assert.Equal((12, 1), (map.Objects[1].Contents["coins"], map.Objects[1].Contents["healing-potion"]));
        // A whole object with no kit is one cell big.
        Assert.Equal((192.0, 0.0, 64.0, 64.0), (map.Objects[2].X, map.Objects[2].Y, map.Objects[2].Width, map.Objects[2].Height));
        Assert.False(map.Walkable(new Cell(3, 0)));
        Assert.True(map.Walkable(new Cell(2, 0)));
    }

    [Fact]
    public void ABrokenKitOrTrapIsNamed()
    {
        Assert.Equal("object", TestContent.Refused(() => Kit.Read(TestContent.Json("{\"name\": \"Door\"}", "kits/door.json"))).Field);
        Assert.Equal("object.area", TestContent.Refused(() => Kit.Read(TestContent.Json("{\"name\": \"Door\", \"object\": {\"area\": [0, 0, 64]}}"))).Field);
        Assert.Equal("object.trap.effect.effects[0].dice", TestContent.Refused(() => Kit.Read(TestContent.Json(
            "{\"name\": \"Trap\", \"object\": {\"trap\": {\"effect\": {\"effects\": [{\"do\": \"damage\"}]}}}}"))).Field);
        Assert.Equal("object.durability.health", TestContent.Refused(() => Kit.Read(TestContent.Json(
            "{\"name\": \"Crate\", \"object\": {\"durability\": {\"health\": 9, \"maximum\": 5}}}"))).Field);
    }

    [Fact]
    public void DialogueReadsNodesRepliesAndChecks()
    {
        Dialogue wren = Dialogue.Read(TestContent.Json("""
            {"id": "wren", "start": "hail", "nodes": [
              {"id": "hail", "speaker": "Wren", "text": "Well met.", "set": ["met_wren"], "choices": [
                {"id": "pay", "text": "Take this.", "require": ["has_coin"], "forbid": ["wren_paid"], "set": ["wren_paid"], "do": ["approve 5"],
                 "check": {"skill": "persuasion", "difficulty": 12, "success": "paid", "failure": "hail"}},
                {"id": "bye", "text": "Farewell.", "next": ""}]},
              {"id": "paid", "text": "Much obliged."}]}
            """));
        Assert.Equal(("wren", "hail", 2), (wren.Id, wren.Start, wren.Nodes.Count));
        DialogueChoice pay = wren.Node("hail")!.Choices[0];
        Assert.Equal(new DialogueCheck("persuasion", 12, "paid", "hail"), pay.Check);
        Assert.Equal(new[] { "has_coin" }, pay.Require);
        Assert.Equal(new[] { "wren_paid" }, pay.Forbid);
        Assert.Equal(new[] { "approve 5" }, pay.Changes.Actions);
        Assert.Equal(new[] { "met_wren" }, wren.Node("hail")!.Changes.Set);
        // A node with no replies is the last line.
        Assert.Empty(wren.Node("paid")!.Choices);
        Assert.Equal("", wren.Node("paid")!.Speaker);
    }

    [Theory]
    [InlineData("{\"id\": \"x\", \"start\": \"a\", \"nodes\": []}", "nodes")]
    [InlineData("{\"id\": \"x\", \"start\": \"b\", \"nodes\": [{\"id\": \"a\"}]}", "start")]
    [InlineData("{\"id\": \"x\", \"start\": \"a\", \"nodes\": [{\"id\": \"a\"}, {\"id\": \"a\"}]}", "nodes[1].id")]
    [InlineData("{\"id\": \"x\", \"start\": \"a\", \"nodes\": [{\"id\": \"a\", \"choices\": [{\"id\": \"go\", \"text\": \"Go\", \"next\": \"b\"}]}]}", "nodes[0].choices[0].next")]
    [InlineData("{\"id\": \"x\", \"start\": \"a\", \"nodes\": [{\"id\": \"a\", \"choices\": [{\"id\": \"go\", \"next\": \"a\"}]}]}", "nodes[0].choices[0].text")]
    [InlineData("{\"id\": \"x\", \"start\": \"a\", \"nodes\": [{\"id\": \"a\", \"set\": [\"f\"], \"clear\": [\"f\"]}]}", "nodes[0].clear")]
    [InlineData("{\"id\": \"x\", \"start\": \"a\", \"nodes\": [{\"id\": \"a\", \"choices\": [{\"id\": \"go\", \"text\": \"Go\", \"check\": {\"skill\": \"lore\", \"difficulty\": -1, \"success\": \"a\", \"failure\": \"a\"}}]}]}", "nodes[0].choices[0].check.difficulty")]
    [InlineData("{\"id\": \"x\", \"start\": \"a\", \"nodes\": [{\"id\": \"a\", \"choices\": [{\"id\": \"go\", \"text\": \"Go\", \"check\": {\"skill\": \"lore\", \"difficulty\": 5, \"success\": \"a\", \"failure\": \"z\"}}]}]}", "nodes[0].choices[0].check")]
    public void ABrokenDialogueNamesTheField(string text, string field)
    {
        ContentException error = TestContent.Refused(() => Dialogue.Read(TestContent.Json(text, "dialogue/x.json")));
        Assert.Equal(("dialogue/x.json", field), (error.File, error.Field));
    }

    [Fact]
    public void QuestsAndCutscenesRead()
    {
        QuestJournal journal = QuestJournal.Read(TestContent.Json("""
            {"quests": [{"id": "keep", "title": "The Keep", "require": ["keep_quest"], "fail": ["tobb_dead"],
              "objectives": [{"id": "entry", "text": "Clear the entry hall", "require": ["entry_hall_clear"]}]}]}
            """));
        Assert.Equal(("keep", "The Keep", "", "entry"), (journal.Quests[0].Id, journal.Quests[0].Title, journal.Quests[0].Description, journal.Quests[0].Objectives[0].Id));
        Assert.Equal("quests[0].objectives", TestContent.Refused(() => QuestJournal.Read(TestContent.Json(
            "{\"quests\": [{\"id\": \"q\", \"title\": \"Q\", \"objectives\": []}]}"))).Field);
        Assert.Equal("quests[0].objectives[0].require", TestContent.Refused(() => QuestJournal.Read(TestContent.Json(
            "{\"quests\": [{\"id\": \"q\", \"title\": \"Q\", \"objectives\": [{\"id\": \"o\", \"text\": \"Do it\", \"require\": []}]}]}"))).Field);
        Assert.Equal("quests[0].title", TestContent.Refused(() => QuestJournal.Read(TestContent.Json("{\"quests\": [{\"id\": \"q\"}]}"))).Field);

        Cutscene ending = Cutscene.Read(TestContent.Json("""
            {"steps": [{"bars": true, "wait": false}, {"camera": [640, 320], "zoom": 1.5, "seconds": 2, "ease": "outCubic"},
                       {"caption": "The keep is quiet."}, {"fade": [0, 0, 0, 255], "seconds": 1}, {"pause": 0.5}, {"event": "finished"}]}
            """));
        Assert.Equal(new[] { CutsceneStepKind.Bars, CutsceneStepKind.Camera, CutsceneStepKind.Caption, CutsceneStepKind.Fade, CutsceneStepKind.Wait, CutsceneStepKind.Event },
            ending.Steps.Select(s => s.Kind));
        Assert.Equal((true, false), (ending.Steps[0].On, ending.Steps[0].Wait));
        Assert.Equal((640.0, 320.0, 1.5, 2.0, "outCubic"), (ending.Steps[1].X, ending.Steps[1].Y, ending.Steps[1].Zoom, ending.Steps[1].Seconds, ending.Steps[1].Ease));
        // A caption with no time stays up three seconds.
        Assert.Equal(3, ending.Steps[2].Seconds);
        Assert.Equal(0.5, ending.Steps[4].Seconds);
        Assert.Equal("steps[0]", TestContent.Refused(() => Cutscene.Read(TestContent.Json("{\"steps\": [{\"dance\": true}]}"))).Field);
        Assert.Equal("steps[0].seconds", TestContent.Refused(() => Cutscene.Read(TestContent.Json("{\"steps\": [{\"pause\": 1, \"seconds\": 9000}]}"))).Field);
        Assert.Equal("steps", TestContent.Refused(() => Cutscene.Read(TestContent.Json("{}"))).Field);
    }

    // A one-room chapter of the test's own, on top of the shipped rules and definitions.
    private static Scratch Pit(string chapter)
    {
        return new Scratch()
            .Write("chapters/pit/map.json", SmallMap)
            .Write("chapters/pit/talk.json", "{\"id\": \"talk\", \"start\": \"a\", \"nodes\": [{\"id\": \"a\", \"text\": \"Hm.\"}]}")
            .Write("chapters/pit/chapter.json", chapter);
    }

    private const string PitChapter = """
        {"id": "pit",
         "party": [{"name": "Ada", "class": "fighter", "at": [1, 0]}],
         "encounters": [{"id": "rats", "creatures": [{"creature": "goblin", "at": [3, 0]}]}],
         "npcs": [{"id": "old-tam", "name": "Old Tam", "at": [0, 1], "dialogue": "talk.json"}]}
        """;

    [Fact]
    public void AChapterWithOnlyTheNeededFieldsGetsTheDefaults()
    {
        using Scratch scratch = Pit(PitChapter);
        Chapter chapter = Chapter.Load(TestContent.ShippedWith(scratch), "chapters/pit");
        Assert.Equal(("pit", "pit", 1, 0, "Victory!", "The party has fallen.", "Chapter complete", "", true),
            (chapter.Id, chapter.Title, chapter.Level, chapter.XpPerVictory, chapter.VictoryText, chapter.DefeatText, chapter.ClearedText, chapter.ClearedCutscene, chapter.CampAllowed));
        Assert.Equal("yorehold", chapter.Rules.Rules.Id);
        Assert.Equal(new ContentColor(200, 200, 210), chapter.Party[0].Color);
        Assert.Equal(("", (int?)null, (double?)null), (chapter.Encounters[0].Text, chapter.Encounters[0].Xp, chapter.Encounters[0].Creatures[0].Facing));
        Assert.Null(chapter.Encounters[0].Creatures[0].Ai);
        ChapterNpc tam = chapter.Npcs[0];
        // The dialogue is found in the chapter's own folder, and an NPC is a commoner unless it says.
        Assert.Equal(("chapters/pit/talk.json", "commoner"), (tam.Dialogue, tam.Creature));
        Assert.Null(tam.Merchant);
        Assert.Null(tam.Companion);
        Assert.True(chapter.Dialogues.ContainsKey("chapters/pit/talk.json"));
        // The shared surrender conversation is used when the chapter names none.
        Assert.Equal("dialogue/surrender.json", chapter.Surrender);
        Assert.Empty(chapter.Triggers);
        Assert.Null(chapter.WinCondition);
        Assert.Empty(chapter.Quests.Quests);
    }

    [Fact]
    public void AChapterReadsItsOptionalParts()
    {
        string full = PitChapter.Replace("\"id\": \"pit\",", """
            "id": "pit", "title": "The Pit", "level": 3, "xpPerVictory": 50, "camp": false, "localFlags": ["pit_seen"],
            "containers": [{"id": "crate", "at": [3, 2], "items": ["mace"], "coins": 30, "loot": {"items": [{"item": "shield", "chance": 0.5}]}}],
            "triggers": [{"id": "hello", "dialogue": "talk.json"}, {"id": "later", "when": ["pit_seen"], "dialogue": "talk.json"}],
            "winCondition": {"when": ["pit_seen"], "dialogue": "talk.json"},
            "onWipe": {"destination": [[2, 0]]},
            "aiChanges": [{"when": ["pit_seen"], "encounter": "rats", "ai": {"fleeHp": 0.9}}],
            """).Replace("\"dialogue\": \"talk.json\"}]}", """
            "dialogue": "talk.json", "killed": ["tam_dead"],
            "merchant": {"coins": 500, "stock": [{"item": "mace", "quantity": 2, "value": 500}, {"item": "shield"}]},
            "companion": {"joinAt": 5, "leaveAt": -10, "flags": {"pit_seen": 3}}}]}
            """).Replace("{\"creature\": \"goblin\", \"at\": [3, 0]}]", "{\"creature\": \"goblin\", \"name\": \"Nib\", \"at\": [3, 0], \"facing\": 180, \"ai\": \"lookout\"}], \"xp\": 75, \"set\": [\"rats_dead\"]");
        using Scratch scratch = Pit(full);
        Chapter chapter = Chapter.Load(TestContent.ShippedWith(scratch), "chapters/pit");

        Assert.Equal(("The Pit", 3, 50, false), (chapter.Title, chapter.Level, chapter.XpPerVictory, chapter.CampAllowed));
        EncounterGroup rats = chapter.Encounters[0];
        Assert.Equal((75, "Nib", 180.0, "lookout"), (rats.Xp, rats.Creatures[0].Name, rats.Creatures[0].Facing, rats.Creatures[0].Ai?.AsText()));
        Assert.Equal(new[] { "rats_dead" }, rats.Set);
        ChapterContainer crate = chapter.Containers[0];
        Assert.Equal(("Chest", 30, new LootEntry("shield", 0.5, 1)), (crate.Name, crate.Coins, crate.Loot.Items[0]));
        Assert.Equal(new[] { "hello", "later" }, chapter.Triggers.Select(t => t.Id));
        Assert.Equal(("chapters/pit/talk.json", ""), (chapter.Triggers[0].Dialogue, chapter.Triggers[0].Cutscene));
        Assert.Empty(chapter.Triggers[0].When);
        Assert.Equal(new[] { "pit_seen" }, chapter.Triggers[1].When);
        Assert.Equal("chapters/pit/talk.json", chapter.WinCondition?.Dialogue);
        Assert.Equal(new[] { new Cell(2, 0) }, chapter.WipeDestination);
        Assert.Equal("rats", chapter.AiChanges[0].Encounter);

        ChapterNpc tam = chapter.Npcs[0];
        Assert.Equal(new[] { "tam_dead" }, tam.Killed);
        Assert.Equal((500, 1.0, 0.5), (tam.Merchant!.Coins, tam.Merchant.BuyMultiplier, tam.Merchant.SellMultiplier));
        // Stock takes the item file's quantity and price unless it gives its own.
        ItemDefinition shield = chapter.Compendium.Item("shield")!;
        Assert.Equal(new[] { new MerchantStock("mace", 2, 500), new MerchantStock("shield", shield.Quantity, shield.Value) }, tam.Merchant.Stock);
        Assert.Equal(("old-tam", 0, 5, -10), (tam.Companion!.Id, tam.Companion.Approval, tam.Companion.JoinAt, tam.Companion.LeaveAt));
        Assert.Equal(new KeyValuePair<string, int>("pit_seen", 3), tam.Companion.Flags[0]);
    }

    [Theory]
    [InlineData("\"class\": \"fighter\"", "\"class\": \"jester\"", "chapters/pit/chapter.json", "party[0].class", "unknown class \"jester\"")]
    [InlineData("\"at\": [1, 0]", "\"at\": [0, 0]", "chapters/pit/chapter.json", "party[0].at", "can't stand on")]
    [InlineData("\"at\": [3, 0]", "\"at\": [1, 0]", "chapters/pit/chapter.json", "encounters[0].creatures[0].at", "occupied")]
    [InlineData("\"creature\": \"goblin\"", "\"creature\": \"dragon\"", "chapters/pit/chapter.json", "encounters[0].creatures[0].creature", "unknown creature \"dragon\"")]
    [InlineData("\"creature\": \"goblin\"", "\"creature\": \"goblin\", \"ai\": \"genius\"", "chapters/pit/chapter.json", "encounters[0].creatures[0].ai", "unknown AI \"genius\"")]
    [InlineData("\"creature\": \"goblin\"", "\"creature\": \"goblin\", \"facing\": 400", "chapters/pit/chapter.json", "encounters[0].creatures[0].facing", "-360 to 360")]
    [InlineData("\"id\": \"rats\"", "\"id\": \"rats\", \"xp\": -5", "chapters/pit/chapter.json", "encounters[0].xp", "whole number")]
    [InlineData("\"id\": \"rats\"", "\"id\": \"rats\", \"loot\": {\"items\": [\"gem\"]}", "chapters/pit/chapter.json", "encounters[0].loot", "unknown item \"gem\"")]
    [InlineData("\"dialogue\": \"talk.json\"", "\"dialogue\": \"gone.json\"", "chapters/pit/chapter.json", "npcs[0].dialogue", "missing file gone.json")]
    [InlineData("\"dialogue\": \"talk.json\"", "\"dialogue\": \"../talk.json\"", "chapters/pit/chapter.json", "npcs[0].dialogue", "relative content path")]
    [InlineData("\"dialogue\": \"talk.json\"", "\"dialogue\": \"talk.json\", \"merchant\": {\"stock\": [{\"item\": \"gem\"}]}", "chapters/pit/chapter.json", "npcs[0].merchant.stock[0].item", "unknown item \"gem\"")]
    [InlineData("\"dialogue\": \"talk.json\"", "\"dialogue\": \"talk.json\", \"merchant\": {\"sellMultiplier\": 2}", "chapters/pit/chapter.json", "npcs[0].merchant", "sellMultiplier")]
    [InlineData("\"id\": \"pit\",", "\"id\": \"pit\", \"endings\": {\"cleared\": \"missing.json\"},", "chapters/pit/chapter.json", "endings.cleared", "missing file missing.json")]
    [InlineData("\"id\": \"pit\",", "\"id\": \"pit\", \"level\": 21,", "chapters/pit/chapter.json", "level", "1 to 20")]
    [InlineData("\"id\": \"pit\",", "\"id\": \"pit\", \"triggers\": [{\"id\": \"empty\"}],", "chapters/pit/chapter.json", "triggers[0]", "needs dialogue or cutscene")]
    [InlineData("\"id\": \"pit\",", "\"id\": \"pit\", \"winCondition\": {\"when\": []},", "chapters/pit/chapter.json", "winCondition.when", "at least one flag")]
    [InlineData("\"id\": \"pit\",", "\"id\": \"pit\", \"onWipe\": {\"destination\": [[0, 0]]},", "chapters/pit/chapter.json", "onWipe.destination[0]", "walkable")]
    [InlineData("\"id\": \"pit\",", "\"id\": \"pit\", \"aiChanges\": [{\"encounter\": \"bats\", \"ai\": \"brute\"}],", "chapters/pit/chapter.json", "aiChanges[0].encounter", "unknown encounter \"bats\"")]
    [InlineData("\"id\": \"pit\",", "\"id\": \"pit\", \"ruleset\": \"rulesets/homebrew\",", "rulesets/homebrew", "", "missing file")]
    [InlineData("\"id\": \"pit\",", "\"id\": \"pit\", \"ruleset\": \"modern\",", "modern", "", "name a ruleset folder or file")]
    [InlineData("\"id\": \"pit\",", "\"id\": \"The Pit\",", "chapters/pit/chapter.json", "id", "a-z, 0-9")]
    [InlineData("\"party\": [{\"name\": \"Ada\", \"class\": \"fighter\", \"at\": [1, 0]}]", "\"party\": []", "chapters/pit/chapter.json", "party", "one to four")]
    public void ABrokenChapterNamesTheFileAndField(string find, string replace, string file, string field, string says)
    {
        Assert.Contains(find, PitChapter);
        using Scratch scratch = Pit(PitChapter.Replace(find, replace));
        ContentFiles files = TestContent.ShippedWith(scratch);
        ContentException error = TestContent.Refused(() => Chapter.Load(files, "chapters/pit"));
        Assert.Equal((file, field), (error.File, error.Field));
        Assert.Contains(says, error.Message);
    }

    [Fact]
    public void ChapterFoldersStayInsideTheContent()
    {
        Assert.Contains("relative chapter folder", TestContent.Refused(() => Chapter.Load(TestContent.Shipped(), "../outside")).Message);
        Assert.Equal("chapters/nowhere/chapter.json: missing file", TestContent.Refused(() => Chapter.Load(TestContent.Shipped(), "chapters/nowhere")).Message);
    }

    [Fact]
    public void AChapterCanBringItsOwnDefinitionsAndBrokenOnesAreNamed()
    {
        using Scratch scratch = Pit(PitChapter);
        scratch.Write("chapters/pit/creatures/goblin.json", "{\"id\": \"goblin\", \"hp\": 33}");
        Assert.Equal(33, Chapter.Load(TestContent.ShippedWith(scratch), "chapters/pit").Compendium.Creature("goblin")?.Hp);

        scratch.Write("chapters/pit/creatures/goblin.json", "{\"id\": \"goblin\", \"hp\": \"lots\"}");
        ContentException error = TestContent.Refused(() => Chapter.Load(TestContent.ShippedWith(scratch), "chapters/pit"));
        Assert.Equal("chapters/pit/creatures/goblin.json: hp: is a whole number from 1 to 100000", error.Message);

        // A trap whose effect names something the ruleset lacks is caught when the map is placed.
        scratch.Write("chapters/pit/creatures/goblin.json", "{\"id\": \"goblin\"}");
        scratch.Write("chapters/pit/kits/snare.json",
            "{\"name\": \"Snare\", \"object\": {\"area\": [0, 0, 64, 64], \"trap\": {\"effect\": [{\"do\": \"condition\", \"id\": \"snared\"}]}}}");
        scratch.Write("chapters/pit/map.json", SmallMap.Replace("\"name\": \"Yard\"", "\"objects\": [{\"kit\": \"snare\", \"at\": [2, 0]}]"));
        error = TestContent.Refused(() => Chapter.Load(TestContent.ShippedWith(scratch), "chapters/pit"));
        Assert.Equal("chapters/pit/map.json", error.File);
        Assert.Contains("unknown condition \"snared\"", error.Message);
    }

    [Fact]
    public void AnAdventureIsCheckedAgainstItsChapters()
    {
        const string good = """
            {"id": "trip", "title": "A Trip", "chapters": ["chapters/chapter-one", {"folder": "chapters/chapter-two"}],
             "transitions": [{"from": {"chapter": "chapter-one", "marker": "exit"}, "to": {"chapter": "chapter-two", "marker": "entry"}, "when": ["ready"]}]}
            """;
        using var scratch = new Scratch();
        scratch.Write("adventure.json", good);
        Adventure trip = Adventure.Load(TestContent.ShippedWith(scratch));
        // Left out: levels 1 to 20, a party of four, no flags, the shared camp.
        Assert.Equal((1, 20, 4, ""), (trip.MinLevel, trip.MaxLevel, trip.RecommendedPartySize, trip.Camp));
        Transition way = trip.Transitions[0];
        Assert.Equal(("chapter-one", "exit", "chapter-two", "entry"), (way.FromChapter, way.ExitMarker, way.ToChapter, way.EntryMarker));
        Assert.Equal(new[] { "ready" }, way.When);
        Assert.Equal(new[] { "chapters/chapter-one", "chapters/chapter-two" }, trip.ChapterFolders);

        void Broken(string find, string replace, string field, string says)
        {
            Assert.Contains(find, good);
            scratch.Write("adventure.json", good.Replace(find, replace));
            ContentException error = TestContent.Refused(() => Adventure.Load(TestContent.ShippedWith(scratch)));
            Assert.Equal(("adventure.json", field), (error.File, error.Field));
            Assert.Contains(says, error.Message);
        }
        Broken("\"marker\": \"exit\"", "\"marker\": \"trapdoor\"", "transitions[0]", "chapter-one has no marker \"trapdoor\"");
        Broken("\"chapter\": \"chapter-two\"", "\"chapter\": \"chapter-nine\"", "transitions[0]", "isn't in the list: chapter-nine");
        Broken("\"title\": \"A Trip\"", "\"minLevel\": 5, \"maxLevel\": 2", "minLevel", "lowest first");
        Broken("\"title\": \"A Trip\"", "\"minLevel\": 3", "chapters", "outside the adventure's range");
        Broken("\"title\": \"A Trip\"", "\"recommendedPartySize\": 6", "recommendedPartySize", "1 to 4");
        Broken("\"title\": \"A Trip\"", "\"camp\": \"chapters/chapter-one\"", "camp", "its own chapter");
        Broken("{\"folder\": \"chapters/chapter-two\"}", "\"chapters/chapter-one\"", "chapters", "two chapters have the id chapter-one");
        Broken("{\"folder\": \"chapters/chapter-two\"}", "\"chapters/goblin-keep\"", "chapters", "seats 4 heroes");
        Broken("[\"chapters/chapter-one\", {\"folder\": \"chapters/chapter-two\"}]", "[]", "chapters", "at least one chapter");
    }

    [Fact]
    public void TheManifestReadsWithAndWithoutItsNewerFields()
    {
        using var scratch = new Scratch();
        var files = new ContentFiles(scratch.Folder);
        Assert.Equal("content.json: missing manifest", TestContent.Refused(() => ContentPackage.Load(files)).Message);

        // Old packages without the newer fields still load, with empty defaults.
        scratch.Write("content.json", "{\"format\": \"yorehold.content\", \"version\": 1, \"name\": \"Old Package\", \"chapters\": []}");
        ContentPackage old = ContentPackage.Load(files);
        Assert.Equal(("Old Package", "", "", 0, "", ""), (old.Name, old.Kind, old.Id, old.Revision, old.Ruleset, old.DefaultChapter));
        Assert.Empty(old.Requires);
        // Nothing to play, but its definitions still have to load.
        old.Validate(files);
        scratch.Write("items/axe.json", "{\"id\": \"hatchet\"}");
        Assert.Equal("items/axe.json", TestContent.Refused(() => old.Validate(files)).File);

        scratch.Write("content.json", """
            {"format": "yorehold.content", "version": 1, "name": "Adventure Pack", "kind": "adventure", "id": "dragon-lair", "revision": 2,
             "ruleset": "yorehold@1.0", "requires": ["asset-pack-1"], "defaultChapter": "chapters/lair", "chapters": ["chapters/lair"]}
            """);
        ContentPackage pack = ContentPackage.Load(files);
        Assert.Equal(("adventure", "dragon-lair", 2, "yorehold@1.0", "chapters/lair"), (pack.Kind, pack.Id, pack.Revision, pack.Ruleset, pack.DefaultChapter));
        Assert.Equal(new[] { "asset-pack-1" }, pack.Requires);

        void Broken(string text, string field)
        {
            scratch.Write("content.json", text);
            ContentException error = TestContent.Refused(() => ContentPackage.Load(files));
            Assert.Equal(("content.json", field), (error.File, error.Field));
        }
        Broken("{\"format\": \"yorehold.content\", \"version\": 2}", "version");
        Broken("{\"format\": \"other.content\", \"version\": 1}", "format");
        Broken("{\"version\": 1}", "format");
        Broken("{\"format\": \"yorehold.content\", \"version\": 1, \"kind\": \"mod\"}", "kind");
        Broken("{\"format\": \"yorehold.content\", \"version\": 1, \"id\": \"My Pack\"}", "id");
        Broken("{\"format\": \"yorehold.content\", \"version\": 1, \"revision\": -1}", "revision");
        Broken("{\"format\": \"yorehold.content\", \"version\": 1, \"chapters\": [\"chapters/a\"], \"defaultChapter\": \"chapters/b\"}", "defaultChapter");
        Broken("{\"format\": \"yorehold.content\", \"version\": 1, \"chapters\": [\"chapters/a\"]}", "defaultChapter");
        Broken("{\"format\": \"yorehold.content\", \"version\": 1, \"chapters\": [\"../a\"], \"defaultChapter\": \"../a\"}", "chapters");
        Broken("{\"format\": \"yorehold.content\", \"version\": 1, \"chapters\": [\"chapters/a\", \"chapters/a\"], \"defaultChapter\": \"chapters/a\"}", "chapters");
        Broken("{\"format\": \"yorehold.content\", \"version\": 1, \"name\": \"" + new string('n', 81) + "\"}", "name");
    }

    [Fact]
    public void TheShippedManifestValidates()
    {
        ContentFiles files = TestContent.Shipped();
        ContentPackage package = ContentPackage.Load(files);
        Assert.Equal(("Yorehold", "adventure", "yorehold-builtin", "ui/theme.json"), (package.Name, package.Kind, package.Id, package.Theme));
        package.Validate(files);
    }
}
