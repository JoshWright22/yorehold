using System.Numerics;
using System.Text.Json.Nodes;
using Kind = Yorehold.Rules.CutsceneEditor.Kind;

namespace Yorehold.Rules.Tests;

/// <summary>Cutscene mode of Create: its commands, undo, timing and the preview's frames, what it writes, the chapter's triggers and endings, and the game loading what was written.</summary>
public class CutsceneEditorTests
{
    // written by hand, with fields the editor has no tool for
    private const string Ending = """
        {
          "music": "quiet.ogg",
          "steps": [
            {"bars": true},
            {"pause": 0.8},
            {"camera": [2816, 928], "zoom": 1.3, "seconds": 3.5, "wait": false},
            {"caption": "The brazier gutters out.", "seconds": 3.5, "voice": "grak.ogg"},
            {"fade": [0, 0, 0, 255], "seconds": 1.5},
            {"title": "The End"},
            {"event": "finished"}
          ]
        }
        """;

    private static bool Near(double a, double b) => Math.Abs(a - b) < 1e-4;

    private static CutsceneEditor Loaded(History history, string text = Ending)
    {
        var editor = new CutsceneEditor(history);
        Assert.True(editor.Load(text, out string error), error);
        return editor;
    }

    [Fact]
    public void LoadAndSave()
    {
        var history = new History();
        CutsceneEditor editor = Loaded(history);
        Assert.Equal(7, editor.Steps.Count);
        Assert.True(editor.Steps[2].Kind == Kind.Camera && editor.Steps[2].X == 2816 && !editor.Steps[2].Wait && editor.Steps[5].Kind == Kind.Title && editor.Steps[5].Seconds == 3,
            "Its steps read as the game reads them, a title's time too");
        JsonNode out_ = JsonNode.Parse(editor.ToJson())!;
        Assert.True((string?)out_["music"] == "quiet.ogg" && (string?)out_["steps"]![3]!["voice"] == "grak.ogg", "Fields the editor has no tool for are written back");
        Assert.True(out_["steps"]![2]!["zoom"]!.ToJsonString() == "1.3" && out_["steps"]![2]!["camera"]!.ToJsonString() == "[2816,928]", "Numbers are written as they were typed");
        Assert.Equal(3, (double)out_["steps"]![5]!["seconds"]!);
        Cutscene? read = editor.Cutscene(out _);
        Assert.True(read != null && read.Steps.Count == 7 && read.Steps[3].Text == "The brazier gutters out." && read.Steps[1].Seconds == 0.8,
            "What it writes reads back in the game's own format");
        Assert.Empty(editor.Problems());
        Assert.Equal("{}", new CutsceneEditor(history).ToJson());

        var broken = new CutsceneEditor(history);
        Assert.True(!broken.Load("""{"steps": [{"event": ""}]}""", out string error) && error.Length > 0 && !broken.Loaded, "A file the game refuses doesn't open, and says why");
        Assert.True(!broken.Load("{ not json", out error) && error.Length > 0, "Neither does one that isn't JSON");

        var fresh = new CutsceneEditor(history);
        fresh.Create();
        Assert.True(fresh.Steps.Count == 3 && fresh.Steps[0].Kind == Kind.Bars && fresh.Steps[0].On && !fresh.Steps[2].On && fresh.Cutscene(out _) != null,
            "A new cutscene is bars in, a caption and bars out, and the game reads it");
        Assert.True(fresh.Problems().Count == 1 && !fresh.Problems()[0].Error, "Its empty caption is only a warning");
    }

    [Fact]
    public void Steps()
    {
        var history = new History();
        CutsceneEditor editor = Loaded(history);
        editor.SetBounds((0, 0, 3200, 1600));

        int? camera = editor.AddStep(Kind.Camera, 3);
        Assert.True(camera == 4 && editor.Steps[4].Kind == Kind.Camera && editor.Steps[4].X == 2816, "A new camera step starts where the last one looked");
        int? bars = editor.AddStep(Kind.Bars);
        Assert.True(bars == 8 && !editor.Steps[8].On, "New bars go the other way from how they stand");
        Assert.True(editor.AddStep(Kind.Event) != null && editor.Steps[^1].Text == "finished", "A new event is one the game knows");
        history.Undo();
        history.Undo();
        Assert.Equal(8, editor.Steps.Count);

        CutsceneEditor.Step moved = editor.Steps[4] with { X = 100, Y = 200, Zoom = 2 };
        Assert.True(editor.SetStep(4, moved, "camera") && editor.Steps[4].Y == 200 && editor.Steps[4].Zoom == 2, "A camera is aimed and zoomed");
        Assert.False(editor.SetStep(4, moved with { Zoom = 80 }), "A zoom the game refuses is refused");
        Assert.False(editor.SetStep(4, editor.Steps[4] with { Seconds = 700 }), "So are seconds past 600");
        Assert.False(editor.SetStep(4, editor.Steps[4] with { Seconds = 2, Ease = "wobbly" }), "And an ease the game doesn't know");
        Assert.True(editor.SetStep(4, editor.Steps[4] with { Seconds = 2, Ease = "outBack" }) && (string?)JsonNode.Parse(editor.ToJson())!["steps"]![4]!["ease"] == "outBack",
            "A known ease is written");
        Assert.False(editor.SetStep(4, editor.Steps[4]), "Setting what is there already is no change");

        editor.SetStep(3, editor.Steps[3] with { Text = "The brazier" }, "line");
        editor.SetStep(3, editor.Steps[3] with { Text = "The brazier dies." }, "line");
        editor.EndTyping();
        history.Undo();
        Assert.Equal("The brazier gutters out.", editor.Steps[3].Text);
        Assert.False(editor.SetStep(7, editor.Steps[7] with { Text = "" }), "An event needs a name");

        Assert.True(editor.MoveStep(0, 1) && editor.Steps[1].Kind == Kind.Bars && !editor.MoveStep(0, -1), "Steps move up and down, not off the end");
        history.Undo();
        int? copy = editor.CopyStep(3);
        Assert.True(copy == 4 && editor.Steps[4].Text == editor.Steps[3].Text && editor.Steps.Count == 9, "A copy goes right after the step");
        Assert.True(editor.RemoveStep(4) && editor.Steps.Count == 8 && !editor.RemoveStep(99), "Remove takes it away");
    }

    [Fact]
    public void Timing()
    {
        CutsceneEditor editor = Loaded(new History(), """
            {"steps": [
              {"bars": true},
              {"camera": [100, 0], "zoom": 2, "seconds": 2, "ease": "linear", "wait": false},
              {"caption": "Hello", "seconds": 3},
              {"fade": [0, 0, 0, 255], "seconds": 1},
              {"event": "finished"}
            ]}
            """);
        List<CutsceneEditor.Span> spans = editor.Spans();
        Assert.True(spans[1].Start == 0 && spans[2].Start == 0, "A step that doesn't wait starts with the next one");
        Assert.True(spans[3].Start == 3 && spans[4].Start == 4 && Near(editor.Length(), 4), "The next waits for everything running to finish");

        CutsceneEditor.Frame half = editor.FrameAt(1, Vector2.Zero, 1);
        Assert.True(Near(half.Camera.X, 50) && Near(half.Zoom, 1.5), "Halfway through a camera move the view is halfway there");
        Assert.True(half.Lines.Count == 1 && half.Lines[0].Text == "Hello" && half.Lines[0].Alpha == 1, "The caption shows at full strength in its middle");
        Assert.True(Near(editor.FrameAt(0.3, Vector2.Zero, 1).Lines[0].Alpha, 0.5), "And fades in over its first 0.6 s");
        Assert.True(half.Bars == 1 && Near(editor.FrameAt(0.2, Vector2.Zero, 1).Bars, 0.5), "The bars slide in over 0.4 s");
        CutsceneEditor.Frame fading = editor.FrameAt(3.5, Vector2.Zero, 1);
        Assert.True(fading.Lines.Count == 0 && fading.Fade.A == 128 && Near(fading.Camera.X, 100), "Then the caption is gone, the fade halfway and the camera arrived");
        Assert.Equal(255, editor.FrameAt(10, Vector2.Zero, 1).Fade.A);
    }

    [Fact]
    public void Problems()
    {
        CutsceneEditor editor = Loaded(new History(), """
            {"steps": [
              {"camera": [9000, 50], "ease": "wobbly"},
              {"caption": "", "seconds": 0},
              {"event": "dance"}
            ]}
            """);
        editor.SetBounds((0, 0, 1000, 1000));
        List<CutsceneEditor.Problem> found = editor.Problems();
        bool Has(string text) => found.Any(p => p.Text.Contains(text, StringComparison.Ordinal));
        Assert.True(Has("outside the map") && Has("wobbly") && Has("no line") && Has("no time") && Has("event dance"),
            "Off-map cameras, unknown eases and events, and empty or instant captions are listed: " + string.Join("; ", found.Select(p => p.Text)));
        Assert.DoesNotContain(found, p => p.Error);
    }

    [Fact]
    public void Hooks()
    {
        var history = new History();
        var hooks = new CutsceneHooks(history);
        const string chapter = """
            {
              "id": "keep",
              "endings": {"cleared": "ending.json"},
              "triggers": [{"id": "talk", "dialogue": "dialogue/a.json", "cutscene": "cutscenes/intro.json", "note": "x"}]
            }
            """;
        var files = new HashSet<string> { "chapters/keep/ending.json", "chapters/keep/cutscenes/intro.json", "cutscenes/shared.json" };
        Assert.True(hooks.Load(chapter, "chapters/keep", files.Contains, out string error), error);
        Assert.True(hooks.Plays(hooks.Cleared, "chapters/keep/ending.json") && hooks.Resolve("cutscenes/shared.json") == "cutscenes/shared.json",
            "Names are found in the chapter folder first, then from the root, as the game does");
        Assert.True(hooks.Named().Count == 2 && !hooks.Changed, "It lists the cutscenes it plays");

        int? added = hooks.AddTrigger("chapters/keep/cutscenes/intro.json", new[] { "gate-open" });
        Assert.True(added == 1 && hooks.Triggers[1].Id == "cutscene-1" && hooks.Triggers[1].Cutscene == "cutscenes/intro.json" && hooks.Changed,
            "A new trigger names the file from the chapter folder");
        Assert.True(!hooks.SetTriggerId(1, "talk") && !hooks.SetTriggerId(1, "Bad Id") && hooks.SetTriggerId(1, "gate"), "Trigger ids are unique and plain");
        Assert.True(hooks.SetTriggerWhen(1, new[] { "gate-open", "night" }) && !hooks.SetTriggerWhen(1, new[] { "a", "a" }), "Its flags change, each once");
        Assert.True(hooks.SetWipe("cutscenes/shared.json") && hooks.Wipe == "cutscenes/shared.json" && !hooks.SetWin("cutscenes/shared.json"),
            "A wipe can play one; a chapter with no winCondition can't");
        Assert.True(hooks.RemoveTrigger(0) && hooks.Triggers.Count == 2 && hooks.Triggers[0].Cutscene.Length == 0 && hooks.Triggers[0].Dialogue == "dialogue/a.json",
            "A trigger that also opens a conversation keeps it");
        Assert.True(hooks.SetCleared("") && hooks.Cleared.Length == 0, "The cleared ending can go");

        string applied = hooks.ApplyTo(chapter);
        JsonNode written = JsonNode.Parse(applied)!;
        Assert.True((string?)written["id"] == "keep" && written["endings"] == null && (string?)written["onWipe"]!["cutscene"] == "cutscenes/shared.json",
            "Applied to a chapter, only these fields change");
        Assert.True((string?)written["triggers"]![0]!["note"] == "x" && written["triggers"]![1]!["when"]!.AsArray().Count == 2, "Trigger fields it has no tool for stay");
        Assert.Equal(applied, hooks.ApplyTo(applied));
        Assert.Empty(hooks.Problems());
        Assert.True(hooks.SetCleared("chapters/keep/gone.json") && hooks.Problems().Count > 0 && hooks.Problems()[0].Error, "A file that isn't there stops a save");

        for (int i = 0; i < 7; i++)
        {
            history.Undo();
        }
        Assert.True(hooks.Triggers.Count == 1 && hooks.Cleared == "ending.json" && hooks.Wipe.Length == 0, "Undo puts it all back");
    }

    [Fact]
    public void InCreate()
    {
        using var scratch = new Scratch();
        var package = new CreatePackage(TestContent.AssetsFolder());
        Assert.True(package.New(Path.Combine(scratch.Folder, "create")), package.Status);
        Assert.True(package.CutsceneFiles().Count == 0 && package.CutsceneEditor() == null, "A new package's chapter has no cutscenes");

        string made = package.NewCutscene();
        Assert.True(made == package.Chapter + "/cutscenes/cutscene.json" && package.CutscenePath == made && package.CutsceneFiles().Count == 1,
            "New starts a cutscene in the chapter's cutscenes folder and opens it");
        CutsceneEditor cut = package.CutsceneEditor()!;
        cut.SetStep(1, cut.Steps[1] with { Text = "The road begins." });
        CutsceneHooks? hooks = package.CutsceneHooks();
        Assert.True(hooks != null && hooks.AddTrigger(made) != null && hooks.SetCleared(made), "It is set to play at the start and when the chapter is cleared");

        string folder = Path.Combine(package.PackagePath, package.Chapter);
        Assert.True(package.Save() && package.Status == "Saved 2 files" && File.Exists(Path.Combine(folder, "cutscenes", "cutscene.json")), package.Status);
        Chapter loaded = Chapter.Load(package.PlayFiles(), package.Chapter);
        Assert.True(loaded.Triggers.Count == 1 && loaded.Triggers[0].Cutscene == made && loaded.ClearedCutscene == made, "The game loads the chapter and plays it at both");

        // groups from Encounters mode and triggers from here end up in the same file
        EncountersEditor? encounters = package.EncountersEditor();
        int? group = encounters?.AddGroup("ambush");
        bool placed = group != null && encounters!.AddCreature(group.Value, "goblin", new Cell(6, 6)) != null;
        hooks.SetTriggerWhen(0, new[] { "ambushed" });
        Assert.True(placed && package.Save(), package.Status);
        JsonNode chapter = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "chapter.json")))!;
        Assert.True(chapter["encounters"]!.AsArray().Count == 1 && (string?)chapter["triggers"]![0]!["when"]![0] == "ambushed", "The chapter has both");

        Assert.False(cut.SetStep(0, cut.Steps[0] with { Kind = Kind.Event, Text = "" }), "The editor refuses a step the game would");

        // the same history as the other modes
        package.Undo();
        Assert.Empty(package.CutsceneHooks()!.Triggers[0].When);

        // the game's own content opens too
        package.Open(TestContent.AssetsFolder());
        List<string> keep = package.CutsceneFiles();
        Assert.Contains("chapters/goblin-keep/ending.json", keep);
        foreach (string path in keep)
        {
            package.OpenCutscene(path);
            CutsceneEditor? editor = package.CutsceneEditor();
            Assert.True(editor != null && editor.Problems().Count == 0, path + ": " + package.CutsceneError + string.Join("; ", editor?.Problems().Select(p => p.Text) ?? Array.Empty<string>()));
            Assert.True(package.CutsceneHooks()!.Plays(package.CutsceneHooks()!.Cleared, path), "It plays when the keep is cleared");
        }
    }
}
