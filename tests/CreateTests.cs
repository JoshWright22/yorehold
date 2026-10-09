using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

/// <summary>Create's undo history and its Map mode: the commands, undo, and what it writes to map.json.</summary>
public class CreateTests
{
    // A walled room in a field, written by hand the way chapters are.
    private const string Room = """
        {
          "name": "Test Map",
          "tiles": {
            "grass": {"art": "grass"},
            "wall": {"art": "wall", "walkable": false, "blocksSight": true},
            "stone": {"art": "stone", "indoors": true}
          },
          "legend": {".": "grass", "#": "wall", "_": "stone"},
          "layers": [
            {"name": "ground", "rows": ["........", ".######.", ".#____#.", ".#____#.", ".######.", "........"]}
          ],
          "lighting": {"mode": "rules", "time": "dusk"},
          "lights": [{"at": [3.5, 2.5], "color": [255, 200, 120], "radius": 5, "flame": true}],
          "markers": {"partyStart": [0, 0], "exit": [7, 5]}
        }
        """;

    private static Dictionary<string, Kit> Kits() => new()
    {
        ["door"] = Kit.Read(TestContent.Json("""{"name":"Door","object":{"name":"the door","area":[0,0,64,64],"tags":["blocksMovement","blocksSight"],"door":{}}}""")),
    };

    // The id of a tile type by name, as the editor's palette numbers them.
    private static int TileNamed(MapEditor editor, string name) => editor.Types.ToList().FindIndex(t => t.Name == name) + 1;

    private static MapEditor RoomEditor(History history, bool kits = false)
    {
        var editor = new MapEditor(history);
        Assert.True(editor.Load(Room, out string error, kits ? Kits() : null), error);
        return editor;
    }

    private static JsonNode Parsed(string json) => JsonNode.Parse(json)!;

    [Fact]
    public void HistoryUndoesRedoesMergesAndGroups()
    {
        var history = new History(3);
        int value = 0;
        void Set(int to)
        {
            int from = value;
            history.Perform("set", () => value = to, () => value = from);
        }
        Set(1);
        Set(2);
        Assert.True(value == 2 && history.Dirty && history.Undo() && value == 1 && history.RedoLabel == "set");
        history.MarkSaved();
        Set(5); // drops the redo branch
        Assert.True(!history.CanRedo && history.Dirty && history.Undo() && !history.Dirty && history.Undo() && value == 0);
        Assert.True(!history.Undo() && history.Redo() && history.Redo() && value == 5);
        for (int stroke = 6; stroke < 9; ++stroke)
        {
            int from = value, to = stroke;
            history.Perform("paint", () => value = to, () => value = from, "brush");
        }
        history.BreakMerge();
        Assert.True(value == 8 && history.UndoLabel == "paint" && history.Undo() && value == 5, "One stroke undoes as one step");
        history.BeginGroup("paste");
        Set(20);
        Set(30);
        Assert.False(history.CanUndo, "Nothing undoes in the middle of a group");
        history.EndGroup();
        Assert.True(history.Undo() && value == 5 && history.Redo() && value == 30, "A group undoes as one step");
        for (int i = 0; i < 5; ++i)
        {
            Set(i);
        }
        Assert.True(history.Count == 3 && history.Undo() && history.Undo() && history.Undo() && !history.Undo() && value == 1, "The oldest steps go past the limit");
        bool threw = false;
        history.Record("nested", () =>
        {
            try
            {
                Set(99);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
        }, () => { });
        history.Undo();
        history.Redo();
        Assert.True(threw, "An edit can't be recorded while one is undone or redone");
    }

    [Fact]
    public void MapLoadsAndSaves()
    {
        var history = new History();
        var editor = new MapEditor(history);
        Assert.True(!editor.Load("{not json", out string error) && !editor.HasMap && error.Length > 0, "A broken map is refused with a reason");
        Assert.True(editor.Load(Room, out error, Kits()) && editor.HasMap, "A hand-written map loads into the editor");
        Assert.True(editor.Width == 8 && editor.Height == 6 && editor.LayerCount == 1, "It has the size and layers of the file");
        Assert.True(editor.Lights.Count == 1 && editor.Lights[0].X == 3.5 && editor.Lights[0].Radius == 5, "Lights are read in cells");
        Assert.True(editor.Markers.Count == 2 && editor.Markers["exit"] == new Cell(7, 5), "Markers are read");
        Assert.True(history.Count == 0 && !history.Dirty, "Loading is not an edit");

        JsonNode saved = Parsed(editor.ToJson());
        Assert.True((string?)saved["name"] == "Test Map" && saved["tileMap"] != null && saved["tiles"] is JsonArray, "It saves in the editor's form");
        JsonNode light = saved["lights"]![0]!;
        Assert.True((double)light["at"]![0]! == 3.5 && (double)light["at"]![1]! == 2.5 && light["color"]!.AsArray().Count == 3
            && (double)light["radius"]! == 5 && (bool)light["flame"]!, "Lights are written as the game reads them");
        Assert.True((int)saved["markers"]!["partyStart"]![0]! == 0, "Markers are written");
        Assert.Equal("dusk", (string?)saved["lighting"]!["time"]);

        GameMap again = GameMap.Read(TestContent.Json(editor.ToJson()), new Dictionary<string, Kit>());
        Assert.True(again.Width == 8 && again.Lights.Count == 1 && again.Marker("exit") != null && !again.Walkable(new Cell(1, 1)), "The game loads what the editor saves");

        var old = new MapEditor(history);
        Assert.True(old.Load("""{"name":"Old","tiles":{"grass":{"art":"grass"}},"legend":{".":"grass"},"layers":[{"name":"ground","rows":["..."]}]}""", out _)
            && old.Lights.Count == 0 && old.Markers.Count == 0, "A map with no lights or markers loads");
        Assert.Empty(Parsed(old.ToJson())["lights"]!.AsArray());

        var blank = new MapEditor(history);
        Assert.True(blank.Load(MapEditor.BlankMap("New", 24, 16), out error) && blank.Width == 24 && blank.Height == 16, "A blank map loads");
        Assert.True(blank.WallTile() != 0 && blank.Map().Walkable(new Cell(5, 5)), "It has ground to walk on and a tile for walls");
    }

    [Fact]
    public void MapTiles()
    {
        var history = new History();
        MapEditor editor = RoomEditor(history);
        int wall = TileNamed(editor, "wall"), grass = TileNamed(editor, "grass");

        Assert.True(editor.Paint(0, new Cell(0, 0), wall) && editor.Paint(0, new Cell(1, 0), wall) && editor.Paint(0, new Cell(2, 0), wall), "Painting changes cells");
        editor.EndStroke();
        Assert.False(editor.Paint(0, new Cell(0, 0), wall), "Painting the same tile again is not an edit");
        Assert.True(!editor.Paint(0, new Cell(99, 0), wall) && !editor.Paint(3, new Cell(0, 0), wall) && !editor.Paint(0, new Cell(0, 0), 99),
            "Cells, layers and tiles that aren't there are refused");
        Assert.Equal(1, history.Count);
        Assert.True(!editor.Map().Walkable(new Cell(1, 0)) && editor.Map().BlocksSight(new Cell(1, 0)), "Painted walls block walking and sight straight away");
        Assert.True(history.Undo() && editor.Tile(0, 2, 0) == grass && editor.Map().Walkable(new Cell(1, 0)), "Undo takes the whole stroke back");
        Assert.True(history.Redo() && editor.Tile(0, 0, 0) == wall, "Redo paints it again");

        Assert.True(editor.Paint(0, new Cell(5, 0), 0) && !editor.Map().Walkable(new Cell(5, 0)), "Erasing the ground leaves a hole");
        editor.EndStroke();
        Assert.True(editor.Paint(0, new Cell(6, 0), wall), "A new stroke starts after the last one ended");
        editor.EndStroke();
        Assert.Equal(3, history.Count);

        Assert.True(editor.Fill(0, new Cell(6, 5), new Cell(3, 3), grass), "A box can be filled from any corner");
        Assert.True(editor.Tile(0, 3, 3) == grass && editor.Tile(0, 6, 5) == grass && editor.Map().Walkable(new Cell(6, 4)), "Every cell in it changes");
        Assert.False(editor.Fill(0, new Cell(3, 3), new Cell(6, 5), grass), "Filling with what is there is not an edit");
        Assert.True(history.Undo() && editor.Tile(0, 6, 4) == wall && editor.Tile(0, 3, 3) != grass, "Undo puts back each cell's own tile");
        Assert.True(editor.Fill(0, new Cell(-5, -5), new Cell(0, 0), grass) && editor.Tile(0, 0, 0) == grass, "A box partly off the map fills the part on it");
    }

    [Fact]
    public void MapLayersAndSize()
    {
        var history = new History();
        MapEditor editor = RoomEditor(history, true);
        int wall = TileNamed(editor, "wall"), stone = TileNamed(editor, "stone");

        int upstairs = editor.AddLayer("upstairs", 1);
        Assert.True(upstairs == 1 && editor.LayerCount == 2 && editor.LayersOn(1).SequenceEqual(new[] { 1 }) && editor.Floors() == (0, 1), "A layer can be added on another floor");
        editor.Paint(upstairs, new Cell(2, 2), stone);
        editor.EndStroke();
        GameMap saved = GameMap.Read(TestContent.Json(editor.ToJson()), new Dictionary<string, Kit>());
        Assert.True(saved.Layers.Count == 2 && saved.Layers[1].Floor == 1 && saved.TileAt(1, new Cell(2, 2))?.Name == "stone", "Floors and their tiles are saved");
        Assert.True(saved.Walkable(new Cell(2, 2)), "Only floor 0 decides where the party walks");

        int walls = editor.WallLayer(0);
        Assert.True(walls == 2 && editor.LayerName(walls) == "walls" && editor.WallLayer(0) == walls, "A floor gets one walls layer");
        Assert.Equal(3, editor.WallLayer(1));
        editor.Paint(walls, new Cell(0, 3), wall);
        editor.EndStroke();
        Assert.False(editor.Map().Walkable(new Cell(0, 3)), "A wall over the ground stops walking");

        Assert.True(editor.RemoveLayer(upstairs) && editor.LayerCount == 3 && editor.LayerName(1) == "walls", "A layer can be removed");
        Assert.Equal(wall, editor.Tile(1, 0, 3));
        Assert.True(history.Undo() && editor.LayerCount == 4 && editor.Tile(upstairs, 2, 2) == stone && editor.LayerFloor(upstairs) == 1,
            "Undo brings the layer back with what was painted on it");
        history.Redo();

        MapEditor single = RoomEditor(history);
        Assert.False(single.RemoveLayer(0), "The last layer stays");

        // shrinking drops what is left outside; undo brings all of it back
        editor.PlaceKit("door", new Cell(7, 0), 0);
        int steps = history.Count;
        Assert.True(editor.Resize(6, 4) && editor.Width == 6 && editor.Height == 4 && editor.Map().Width == 6, "The map can be made smaller");
        Assert.True(!editor.Markers.ContainsKey("exit") && editor.Markers.ContainsKey("partyStart") && editor.Map().Objects.Count == 0, "Markers and objects outside the new size go");
        Assert.True(editor.Tile(0, 1, 1) == wall && history.Count == steps + 1, "Cells are kept from the top-left");
        GameMap.Read(TestContent.Json(editor.ToJson()), new Dictionary<string, Kit>());
        Assert.True(history.Undo() && editor.Width == 8 && editor.Markers.ContainsKey("exit") && editor.Map().Objects.Count == 1 && editor.Tile(0, 6, 4) == wall,
            "Undo restores the size and everything dropped");
        Assert.True(editor.Resize(10, 8) && editor.Tile(0, 9, 7) == 0 && !editor.Map().Walkable(new Cell(9, 7)), "A bigger map starts empty in the new part");
        Assert.True(!editor.Resize(0, 5) && !editor.Resize(10, 8), "Sizes that are wrong or unchanged are refused");
    }

    [Fact]
    public void MapLightsMarkersAndKits()
    {
        var history = new History();
        MapEditor editor = RoomEditor(history, true);

        var torch = new EditorLight(4.5, 3.5, 3, new ContentColor(120, 200, 255), false);
        Assert.True(editor.AddLight(torch) == 1 && editor.Lights.Count == 2, "A light is placed");
        Assert.Null(editor.AddLight(torch with { X = 20 }));
        for (int radius = 4; radius <= 8; radius++)
        {
            editor.SetLight(1, editor.Lights[1] with { Radius = radius }, "light-1");
        }
        Assert.True(editor.Lights[1].Radius == 8 && history.Count == 2, "Dragging a light's radius is one undo step");
        JsonNode light = Parsed(editor.ToJson())["lights"]![1]!;
        Assert.True((double)light["at"]![0]! == 4.5 && (double)light["radius"]! == 8 && (int)light["color"]![2]! == 255 && light["color"]!.AsArray().Count == 3
            && !(bool)light["flame"]!, "The light is saved with its colour, radius and flame");
        Assert.True(history.Undo() && editor.Lights[1].Radius == 3, "Undo puts the radius back");
        Assert.True(editor.RemoveLight(0) && editor.Lights.Count == 1 && editor.Lights[0].Radius == 3, "A light can be removed");
        Assert.True(history.Undo() && editor.Lights.Count == 2 && editor.Lights[0].X == 3.5, "Undo puts it back in its place");

        Assert.True(editor.SetMarker("ambush", new Cell(3, 2)) && editor.Markers["ambush"] == new Cell(3, 2), "A marker is placed");
        Assert.True(editor.SetMarker("ambush", new Cell(4, 3)) && editor.Markers.Count == 3, "Placing a name again moves it");
        Assert.True(!editor.SetMarker("ambush", new Cell(4, 3)) && !editor.SetMarker("", new Cell(1, 1)) && !editor.SetMarker("far", new Cell(40, 1)),
            "Unnamed or off-map markers are refused");
        Assert.True(history.Undo() && editor.Markers["ambush"] == new Cell(3, 2), "Undo moves it back");
        Assert.True(editor.RemoveMarker("ambush") && !editor.RemoveMarker("ambush"), "A marker can be removed once");
        Assert.True(history.Undo() && editor.Markers.ContainsKey("ambush"), "Undo brings it back");
        Assert.True(editor.SetMarker("stuck", new Cell(1, 1)) && editor.Problems().Count == 1, "A marker inside a wall is pointed out");
        history.Undo();
        Assert.Empty(editor.Problems());

        Assert.True(editor.Kits.Count == 1 && editor.PlaceKit("portcullis", new Cell(0, 0), 0) == null, "Only kits the package has can be placed");
        Assert.True(editor.Map().Walkable(new Cell(0, 2)), "The cell is open before the door");
        int? door = editor.PlaceKit("door", new Cell(0, 2), 0);
        Assert.True(door != null && editor.ObjectAt(new Cell(0, 2), 0) == door && !editor.Map().Walkable(new Cell(0, 2)), "A placed door is on its cell and shut");
        Assert.True(editor.PlaceKit("door", new Cell(0, 2), 0) == null && editor.PlaceKit("door", new Cell(-1, 0), 0) == null, "Not on top of another object or off the map");
        Assert.NotNull(editor.PlaceKit("door", new Cell(0, 2), 1));
        GameMap saved = GameMap.Read(TestContent.Json(editor.ToJson()), new Dictionary<string, Kit>());
        Assert.True(saved.Objects.Count == 2 && !saved.Walkable(new Cell(0, 2)), "Placed kits are saved as whole objects, no kit file needed");
        Assert.True(editor.RemoveObject(door!.Value) && editor.ObjectAt(new Cell(0, 2), 0) == null && editor.Map().Walkable(new Cell(0, 2)), "An object can be removed");
        Assert.True(history.Undo() && editor.ObjectAt(new Cell(0, 2), 0) == door, "Undo brings it back as the same object");
    }

    [Fact]
    public void TwoMapsShareOneHistory()
    {
        var history = new History();
        MapEditor first = RoomEditor(history), second = RoomEditor(history);
        first.SetMarker("a", new Cell(0, 1));
        second.SetMarker("b", new Cell(0, 2));
        Assert.True(history.Undo() && !second.Markers.ContainsKey("b") && first.Markers.ContainsKey("a"), "Undo takes back the latest edit, whichever map it was on");
        Assert.True(history.Undo() && !first.Markers.ContainsKey("a") && !history.CanUndo, "... then the one before, on the other map");
    }

    [Fact]
    public void ArtPacksSitBetweenTheGameAndThePackage()
    {
        using var scratch = new Scratch();
        string pack = Path.Combine(scratch.Folder, "art", "pack");
        Directory.CreateDirectory(Path.Combine(pack, "tiles"));
        File.WriteAllText(Path.Combine(pack, "tiles", "grass.png"), "pack");
        var package = new CreatePackage(TestContent.AssetsFolder());
        package.ArtFolders.Add(pack);
        Assert.True(package.New(Path.Combine(scratch.Folder, "create")), package.Status);
        Assert.Equal("pack", File.ReadAllText(package.PlayFiles().FullPath("tiles/grass.png")!));
        Directory.CreateDirectory(Path.Combine(package.PackagePath, "tiles"));
        File.WriteAllText(Path.Combine(package.PackagePath, "tiles", "grass.png"), "own");
        Assert.True(File.ReadAllText(package.PlayFiles().FullPath("tiles/grass.png")!) == "own", "The package's own picture wins over a pack's");
        Assert.True(package.PlayFiles().Exists("content.json"), "The game's content is still under both");
    }

    [Theory]
    [InlineData("rulesets/yorehold")]
    [InlineData("rulesets/dnd5e")]
    [InlineData("rulesets/pf2e")]
    [InlineData("rulesets/fate-accelerated")]
    public void ANewAdventureComesWithExamplesToCopy(string system)
    {
        using var scratch = new Scratch();
        var package = new CreatePackage(TestContent.AssetsFolder());
        Assert.True(package.New(Path.Combine(scratch.Folder, "create"), system), package.Status);
        string[] examples = Directory.GetFiles(package.PackagePath, "example-*.json", SearchOption.AllDirectories);
        Assert.True(examples.Length >= 1, "Something of the system's own to copy: an item, a spell, a creature as it has them");
        package.CheckFiles();
        Assert.True(package.Problems().All(p => !p.Error), string.Join("; ", package.Problems().Select(p => p.Message)));
    }

    [Fact]
    public void PackagesAreMadeOpenedAndSaved()
    {
        using var scratch = new Scratch();
        var package = new CreatePackage(TestContent.AssetsFolder());
        Assert.True(!package.IsOpen && !package.History.CanUndo && !package.History.CanRedo, "Nothing open has nothing to undo");
        Assert.True(package.New(Path.Combine(scratch.Folder, "create")) && package.IsOpen, package.Status);
        string folder = package.PackagePath;
        Assert.Equal("chapters/" + Path.GetFileName(folder), package.Chapter);
        string file = Path.Combine(folder, package.Chapter, "map.json");
        Assert.True(File.Exists(Path.Combine(folder, "content.json")) && File.Exists(file), "It is a folder with a manifest, a chapter and a map");
        Assert.True(package.Problems().All(p => !p.Error), string.Join("; ", package.Problems().Select(p => p.Message)));
        Assert.Contains((RulesFolder.Default, "Yorehold"), package.Systems());
        Assert.True(ContentPackage.Load(new ContentFiles(folder)).Ruleset == "yorehold", "A new adventure says which rules system it plays");
        Assert.False(new CreatePackage(TestContent.AssetsFolder()).New(Path.Combine(scratch.Folder, "other"), "rulesets/none"), "There is no new adventure for rules the game doesn't have");
        MapEditor? editor = package.MapEditor();
        Assert.True(editor != null && editor.Width == 24 && editor.Markers.ContainsKey("partyStart"), "Map mode opens the chapter's map");
        Assert.NotEmpty(editor!.Kits);

        string untouched = File.ReadAllText(file);
        Assert.True(package.Save() && File.ReadAllText(file) == untouched, "Saving with nothing changed leaves the file alone");
        editor.Paint(editor.WallLayer(0), new Cell(5, 5), editor.WallTile());
        editor.EndStroke();
        editor.SetMarker("exit", new Cell(20, 10));
        Assert.True(package.History.Dirty, "Edits make the package unsaved");
        Assert.False(package.StatusCurrent, "... and the status no longer says what was done last");
        Assert.True(package.Save() && !package.History.Dirty, "Save writes it");
        GameMap written = GameMap.Read(ContentNode.Parse("map.json", File.ReadAllText(file)), new Dictionary<string, Kit>());
        Assert.True(!written.Walkable(new Cell(5, 5)) && written.Marker("exit") == new Cell(20, 10) && written.Marker("partyStart") != null, "map.json on disk has the edits");
        Chapter played = Chapter.Load(package.PlayFiles(), package.Chapter);
        Assert.False(played.Map.Walkable(new Cell(5, 5)), "... and plays in the game");
        package.Undo();
        Assert.True(!editor.Markers.ContainsKey("exit") && package.History.Dirty && package.Status.StartsWith("Undid", StringComparison.Ordinal), "Undo after a save makes it unsaved again");
        package.Redo();
        Assert.True(editor.Markers.ContainsKey("exit") && !package.History.Dirty, "Redo comes back to the saved state");

        package.Open(folder);
        Assert.True(package.IsOpen && !package.History.CanUndo && package.MapEditor()?.Markers.ContainsKey("exit") == true, "The saved package opens again");
        package.New(Path.Combine(scratch.Folder, "create"));
        Assert.True(package.IsOpen && package.PackagePath != folder, "A second new package gets its own folder");

        package.Open(TestContent.AssetsFolder());
        Assert.True(package.IsOpen && package.IsGameContent && package.MapEditor()?.Width > 0, "The game's own adventure opens in the editor");
        package.Open(Path.Combine(scratch.Folder, "nowhere"));
        Assert.True(!package.IsOpen && package.Status.Length > 0, "A path with no package says so");
    }
}
