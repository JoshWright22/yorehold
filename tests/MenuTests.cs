using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

/// <summary>What the menus keep outside an adventure: the settings file, key bindings, the load screen's list and the credits.</summary>
public class MenuTests
{
    private const string Keys = """
        {"actions": [
          {"id": "sheet", "name": "Sheet", "group": "Panels", "keys": ["C"]},
          {"id": "gear", "name": "Gear", "group": "Panels", "keys": ["I"]},
          {"id": "pan_left", "name": "Pan left", "group": "Camera", "keys": ["Left", "A"]}
        ]}
        """;

    [Fact]
    public void SettingsStartAtTheDefaults()
    {
        var settings = new GameSettings();
        Assert.True(settings.ZoomToCursor);
        Assert.True(settings.EdgeScroll);
        Assert.True(settings.CameraFollows);
        Assert.Equal(900, settings.PanSpeed);
        Assert.False(settings.Fullscreen);
        Assert.Equal(0, settings.Lighting);
        Assert.Equal(0, settings.TimeOfDay);
        Assert.True(settings.SharedFog);
        Assert.True(settings.ReactionPrompts);
        Assert.Empty(settings.Keys);
    }

    [Fact]
    public void SettingsComeBackAsTheyWereWritten()
    {
        var settings = new GameSettings
        {
            ZoomToCursor = false, EdgeScroll = false, CameraFollows = false, PanSpeed = 1500, Fullscreen = true,
            Lighting = 3, TimeOfDay = 2, SharedFog = false, ReactionPrompts = false, LastCreatePackage = "create/new-adventure",
        };
        settings.Keys["sheet"] = new List<string> { "P" };
        settings.SetsOff.Add("more-feats");
        GameSettings back = GameSettings.Parse(settings.ToJson());
        Assert.Equal(new[] { "more-feats" }, back.SetsOff);
        Assert.False(back.ZoomToCursor);
        Assert.False(back.EdgeScroll);
        Assert.False(back.CameraFollows);
        Assert.Equal(1500, back.PanSpeed);
        Assert.True(back.Fullscreen);
        Assert.Equal(3, back.Lighting);
        Assert.Equal(2, back.TimeOfDay);
        Assert.False(back.SharedFog);
        Assert.False(back.ReactionPrompts);
        Assert.Equal("create/new-adventure", back.LastCreatePackage);
        Assert.Equal(new[] { "P" }, back.Keys["sheet"]);
        // the names the C++ client writes
        Assert.Contains("\"lighting\": \"rules\"", settings.ToJson());
        Assert.Contains("\"timeOfDay\": \"dusk\"", settings.ToJson());
    }

    [Fact]
    public void BrokenSettingsKeepTheirDefaults()
    {
        Assert.Equal(900, GameSettings.Parse("not json at all").PanSpeed);
        Assert.Equal(900, GameSettings.Parse("[1, 2]").PanSpeed);
        GameSettings odd = GameSettings.Parse("""{"panSpeed": "fast", "edgeScroll": 3, "lighting": "disco", "sharedFog": false, "keys": {"sheet": "C", "gear": [1, "G"]}}""");
        Assert.Equal(900, odd.PanSpeed);
        Assert.True(odd.EdgeScroll);
        Assert.Equal(0, odd.Lighting);
        Assert.False(odd.SharedFog); // the good values in a file with bad ones still count
        Assert.False(odd.Keys.ContainsKey("sheet"));
        Assert.Equal(new[] { "G" }, odd.Keys["gear"]);
        Assert.Equal(GameSettings.PanSpeedMax, GameSettings.Parse("""{"panSpeed": 99999}""").PanSpeed);
        Assert.Equal(GameSettings.PanSpeedMin, GameSettings.Parse("""{"panSpeed": -4}""").PanSpeed);
    }

    [Fact]
    public void SettingsKeepWhatThisPortDoesNotRead()
    {
        // the C++ client's file: co-op name and skin have to survive a save from here
        GameSettings settings = GameSettings.Parse("""{"controls": "foundry", "playerName": "Rook", "skin": "Paper", "panSpeed": 700}""");
        settings.PanSpeed = 800;
        JsonObject written = JsonNode.Parse(settings.ToJson())!.AsObject();
        Assert.Equal("foundry", written["controls"]!.GetValue<string>());
        Assert.Equal("Rook", written["playerName"]!.GetValue<string>());
        Assert.Equal("Paper", written["skin"]!.GetValue<string>());
        Assert.Equal(800, written["panSpeed"]!.GetValue<float>());
    }

    [Fact]
    public void SettingsFileIsWrittenAndRead()
    {
        using var scratch = new Scratch();
        string path = Path.Combine(scratch.Folder, "deep", "settings.json");
        Assert.Equal(900, GameSettings.Load(path).PanSpeed); // no file yet
        var settings = new GameSettings { PanSpeed = 1200 };
        settings.Save(path);
        Assert.Equal(1200, GameSettings.Load(path).PanSpeed);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void PanSpeedStepsInsideItsRange()
    {
        var settings = new GameSettings { PanSpeed = 930 };
        settings.StepPanSpeed(1);
        Assert.Equal(1000, settings.PanSpeed);
        settings.StepPanSpeed(-100);
        Assert.Equal(GameSettings.PanSpeedMin, settings.PanSpeed);
        settings.StepPanSpeed(1000);
        Assert.Equal(GameSettings.PanSpeedMax, settings.PanSpeed);
    }

    [Fact]
    public void KeysStartAsShipped()
    {
        KeyBindings keys = KeyBindings.Read(TestContent.Json(Keys));
        Assert.Equal(3, keys.Actions.Count);
        Assert.Equal(new[] { "C" }, keys.Keys("sheet"));
        Assert.Equal("Left, A", keys.KeysText("pan_left"));
        Assert.Equal("sheet", keys.ActionFor("c"));
        Assert.Null(keys.ActionFor("Q"));
        Assert.False(keys.Changed("sheet"));
        Assert.Empty(keys.Overrides());
        Assert.Empty(keys.Keys("nothing"));
    }

    [Fact]
    public void BindingAKeyTakesItFromWhoeverHadIt()
    {
        KeyBindings keys = KeyBindings.Read(TestContent.Json(Keys));
        Assert.True(keys.Bind("sheet", "P", out string? taken));
        Assert.Null(taken);
        Assert.Equal(new[] { "P" }, keys.Keys("sheet"));
        Assert.True(keys.Changed("sheet"));

        Assert.True(keys.Bind("sheet", "I", out taken));
        Assert.Equal("gear", taken);
        Assert.Empty(keys.Keys("gear"));
        Assert.Equal("none", keys.KeysText("gear"));
        Assert.Equal("sheet", keys.ActionFor("I"));

        // an action with two keys only loses the one that was taken
        Assert.True(keys.Bind("gear", "A", out taken));
        Assert.Equal("pan_left", taken);
        Assert.Equal(new[] { "Left" }, keys.Keys("pan_left"));

        Assert.False(keys.Bind("nothing", "Z", out _));
        Assert.False(keys.Bind("sheet", "", out _));
    }

    [Fact]
    public void ChangedKeysGoThroughTheSettingsFile()
    {
        KeyBindings keys = KeyBindings.Read(TestContent.Json(Keys));
        keys.Bind("sheet", "I", out _);
        var settings = new GameSettings();
        foreach (KeyValuePair<string, List<string>> changed in keys.Overrides())
        {
            settings.Keys[changed.Key] = changed.Value;
        }
        Assert.Equal(new[] { "gear", "sheet" }, settings.Keys.Keys.Order());

        KeyBindings again = KeyBindings.Read(TestContent.Json(Keys));
        again.Apply(GameSettings.Parse(settings.ToJson()).Keys);
        Assert.Equal(new[] { "I" }, again.Keys("sheet"));
        Assert.Empty(again.Keys("gear"));
        Assert.Equal(new[] { "Left", "A" }, again.Keys("pan_left"));
    }

    [Fact]
    public void AppliedKeysNeverLandOnTwoActions()
    {
        KeyBindings keys = KeyBindings.Read(TestContent.Json(Keys));
        // a file edited by hand: both want X, and sheet takes gear's shipped key without gear being named
        keys.Apply(new Dictionary<string, List<string>>
        {
            ["sheet"] = new() { "X", "A" },
            ["pan_left"] = new() { "X", "Left" },
            ["gone"] = new() { "Q" },
        });
        Assert.Equal(new[] { "X", "A" }, keys.Keys("sheet"));
        Assert.Equal(new[] { "Left" }, keys.Keys("pan_left"));
        Assert.Null(keys.ActionFor("Q"));

        keys.Apply(new Dictionary<string, List<string>> { ["sheet"] = new() { "I" } });
        Assert.Equal("sheet", keys.ActionFor("I"));
        Assert.Empty(keys.Keys("gear"));
    }

    [Fact]
    public void ResetGivesBackTheShippedKeysThatAreFree()
    {
        KeyBindings keys = KeyBindings.Read(TestContent.Json(Keys));
        keys.Bind("sheet", "I", out _);
        keys.Reset("gear");
        Assert.Empty(keys.Keys("gear")); // sheet still has I
        keys.Reset("sheet");
        Assert.Equal(new[] { "C" }, keys.Keys("sheet"));
        keys.Reset("gear");
        Assert.Equal(new[] { "I" }, keys.Keys("gear"));

        keys.Bind("pan_left", "C", out _);
        keys.ResetAll();
        Assert.Empty(keys.Overrides());
        Assert.Equal(new[] { "C" }, keys.Keys("sheet"));
    }

    [Fact]
    public void KeyFilesThatClashAreRefused()
    {
        ContentException twice = TestContent.Refused(() => KeyBindings.Read(TestContent.Json(
            """{"actions": [{"id": "a", "keys": ["C"]}, {"id": "b", "keys": ["c"]}]}""", "ui/keys.json")));
        Assert.Contains("ui/keys.json", twice.Message);
        Assert.Contains("already the key", twice.Message);
        Assert.Contains("listed twice", TestContent.Refused(() => KeyBindings.Read(TestContent.Json(
            """{"actions": [{"id": "a", "keys": []}, {"id": "a", "keys": []}]}"""))).Message);
    }

    [Fact]
    public void ShippedKeysCoverThePlayScreen()
    {
        KeyBindings keys = KeyBindings.Read(ContentNode.Read(TestContent.Shipped(), "ui/keys.json"));
        foreach (string id in new[] { "pan_left", "pan_right", "pan_up", "pan_down", "zoom_in", "zoom_out", "recenter", "sheet", "gear", "spells", "journal", "camp", "end_turn", "save", "load", "help", "chat" })
        {
            Assert.True(keys.Keys(id).Count > 0, $"{id} has a key");
            Assert.True(keys.Action(id)!.Description.Length > 0, $"{id} says what it does");
        }
        // Escape, Enter and the number keys are fixed and may not be given away
        Assert.Null(keys.ActionFor("Escape"));
        Assert.Null(keys.ActionFor("1"));
    }

    [Fact]
    public void CreditsListTheGameAndItsPalette()
    {
        List<CreditEntry> credits = Credits.Read(ContentNode.Read(TestContent.Shipped(), "ui/credits.json"));
        Assert.Equal("Yorehold", credits[0].Name);
        Assert.Contains(credits, c => c.Name.StartsWith("Apollo") && c.By.Length > 0);
        Assert.Contains("listed twice", TestContent.Refused(() => Credits.Read(TestContent.Json(
            """{"entries": [{"name": "A"}, {"name": "A"}]}"""))).Message);
    }

    [Fact]
    public void ASaveIsSummedUpWithoutLoadingIt()
    {
        using var scratch = new Scratch();
        using WorldFixture fixture = WorldFixture.Load("chapters/chapter-one");
        string path = Path.Combine(scratch.Folder, "adventure.json");
        World.SaveFile.WriteFile(path, fixture.World.StateJson());

        SaveSummary summary = SaveSummary.Read(path, TestContent.Shipped());
        Assert.Equal("", summary.Problem);
        Assert.False(summary.Backup);
        Assert.Equal("chapters/chapter-one", summary.ChapterFolder);
        Assert.Equal(fixture.World.Chapter.Title, summary.ChapterTitle);
        Assert.Equal(fixture.World.HeroCount, summary.Heroes.Count);
        for (int i = 0; i < summary.Heroes.Count; i++)
        {
            CharacterSheet sheet = fixture.World.Creatures[i].Sheet;
            Assert.Equal(new SaveHero(sheet.Name, sheet.ClassName, sheet.Level, sheet.Hp, sheet.Xp, false), summary.Heroes[i]);
        }
        // without the content the id stands in for the title
        Assert.Equal(summary.ChapterId, SaveSummary.Read(path).ChapterTitle);
    }

    [Fact]
    public void TheLoadListHasBackupsAndBrokenFiles()
    {
        using var scratch = new Scratch();
        Assert.Empty(SaveSummary.List(Path.Combine(scratch.Folder, "missing")));

        using WorldFixture fixture = WorldFixture.Load("chapters/chapter-one");
        string path = Path.Combine(scratch.Folder, "adventure.json");
        World.SaveFile.WriteFile(path, fixture.World.StateJson());
        World.SaveFile.WriteFile(path, fixture.World.StateJson()); // the second write leaves a .bak
        scratch.Write("broken.json", "{ not a save");
        scratch.Write("notes.txt", "not listed");
        File.SetLastWriteTime(Path.Combine(scratch.Folder, "broken.json"), new DateTime(2020, 1, 1));

        List<SaveSummary> saves = SaveSummary.List(scratch.Folder);
        Assert.Equal(3, saves.Count);
        Assert.Equal("broken.json", saves[^1].FileName); // the oldest is last
        Assert.True(saves[^1].Problem.Length > 0);
        Assert.Empty(saves[^1].Heroes);
        SaveSummary backup = saves.Single(s => s.Backup);
        Assert.Equal("adventure.json.bak", backup.FileName);
        Assert.Equal("", backup.Problem);

        // a backup loads on its own
        Assert.True(fixture.World.Load(backup.Path), fixture.World.Refusal);
    }

    [Fact]
    public void ASaveFromAnotherGameSaysSo()
    {
        using var scratch = new Scratch();
        scratch.Write("other.json", """{"format": "something.else", "version": 1, "data": {}}""");
        scratch.Write("old.json", """{"format": "yorehold.adventure", "version": 2, "data": {}}""");
        Assert.Contains("not a yorehold.adventure file", SaveSummary.Read(Path.Combine(scratch.Folder, "other.json")).Problem);
        Assert.Contains("older version", SaveSummary.Read(Path.Combine(scratch.Folder, "old.json")).Problem);
    }
}
