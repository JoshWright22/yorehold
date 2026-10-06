using System.Numerics;
using System.Text.Json.Nodes;
using Kind = Yorehold.Rules.StoryEditor.Kind;

namespace Yorehold.Rules.Tests;

/// <summary>Story mode of Create: the graph's commands and undo, what it writes, its suggestions and warnings, and story.json saved and read back.</summary>
public class StoryEditorTests
{
    // written by hand, with fields the editor has no tool for
    private const string Story = """
        {
          "format": 1,
          "mood": "grim",
          "nodes": [
            {"id": "keep", "kind": "scene", "title": "The keep", "text": "Rain on the walls.", "at": [0, 0], "chapter": "chapters/keep", "colour": "grey"},
            {"id": "gate", "kind": "encounter", "title": "At the gate", "at": [0, 80], "chapter": "chapters/keep", "group": "gate"},
            {"id": "rescue", "kind": "quest", "title": "Find Tobb", "at": [240, 80], "chapter": "chapters/keep", "quest": "rescue"},
            {"id": "end", "kind": "ending", "title": "Home", "at": [0, 160]}
          ],
          "links": [
            {"from": "keep", "to": "gate", "text": "They knock", "note": "loud"},
            {"from": "gate", "to": "end", "when": ["gate-open"]}
          ]
        }
        """;

    private static StoryEditor.Catalog KeepCatalog()
    {
        var keep = new StoryEditor.Catalog.Chapter
        {
            Folder = "chapters/keep",
            Id = "keep",
            Title = "The Keep",
            Groups = new() { new("gate", 50, 2), new("hall", 75, 3) },
            Dialogues = new() { "chapters/keep/dialogue/wren.json" },
            Quests = new() { new("rescue", "Find Tobb") },
            Ending = "chapters/keep/ending.json",
        };
        var warren = new StoryEditor.Catalog.Chapter { Folder = "chapters/warren", Id = "warren" };
        var catalog = new StoryEditor.Catalog { Chapters = new() { keep, warren }, Adventure = true };
        catalog.Travel.Add(("keep", "warren"));
        return catalog;
    }

    private static bool Offers(StoryEditor editor, string key) => editor.Suggestions().Any(s => s.Key == key);

    private static StoryEditor Loaded(History history)
    {
        var editor = new StoryEditor(history);
        Assert.True(editor.Load(Story, out string error), error);
        return editor;
    }

    [Fact]
    public void LoadAndSave()
    {
        var history = new History();
        StoryEditor editor = Loaded(history);
        Assert.True(editor.Nodes.Count == 4 && editor.Links.Count == 2, "A hand-written story opens");
        Assert.True(editor.Nodes[1].Kind == Kind.Encounter && editor.Nodes[1].Ref == "gate" && editor.Nodes[2].Ref == "rescue" && editor.Links[1].When.Count == 1,
            "Its nodes, what they point at and the link's flags are read");
        JsonNode out_ = JsonNode.Parse(editor.ToJson())!;
        Assert.True((string?)out_["mood"] == "grim" && (string?)out_["nodes"]![0]!["colour"] == "grey" && (string?)out_["links"]![0]!["note"] == "loud",
            "Fields the editor has no tool for are written back");
        Assert.True((string?)out_["nodes"]![1]!["group"] == "gate" && (string?)out_["nodes"]![2]!["quest"] == "rescue" && out_["nodes"]![3]!["cutscene"] == null,
            "What a node points at is written under its kind's own name");
        var again = new StoryEditor(history);
        Assert.True(again.Load(editor.ToJson(), out _) && again.ToJson() == editor.ToJson(), "What it writes reads back the same");

        var broken = new StoryEditor(history);
        Assert.True(!broken.Load("""{"nodes": [{"id": "a", "kind": "scene"}, {"id": "a", "kind": "quest"}]}""", out string error) && error.Contains("two nodes"),
            "Two nodes with one id are refused, and it says why");
        Assert.True(!broken.Load("""{"nodes": [{"id": "a", "kind": "boss"}]}""", out error) && error.Length > 0, "So is a kind it doesn't know");
        Assert.True(!broken.Load("""{"nodes": [{"id": "a", "kind": "scene"}], "links": [{"from": "a", "to": "b"}]}""", out error) && error.Length > 0, "And a link to nothing");
        Assert.True(!broken.Load("""{"format": 9}""", out error) && error.Contains("newer"), "A file from a newer game says so");
        Assert.True(!broken.Load("{ not json", out _) && !broken.Loaded, "Something that isn't JSON doesn't open");

        var fresh = new StoryEditor(history);
        fresh.Create();
        Assert.True(fresh.Loaded && fresh.Nodes.Count == 0 && (int)JsonNode.Parse(fresh.ToJson())!["format"]! == 1, "A new story is an empty graph");
    }

    [Fact]
    public void Commands()
    {
        var history = new History();
        StoryEditor editor = Loaded(history);
        editor.SetCatalog(KeepCatalog());

        int? talk = editor.AddNode(Kind.Dialogue, new Vector2(300, 0));
        Assert.True(talk != null && editor.Nodes[talk.Value].Id == "dialogue", "A new node gets an id from its kind");
        Assert.True(editor.AddNode(Kind.Dialogue, Vector2.Zero) != null && editor.Nodes[^1].Id == "dialogue-2", "And the next free one after that");
        Assert.True(editor.AddNode(Kind.Scene, Vector2.Zero, "keep") == null && editor.AddNode(Kind.Scene, Vector2.Zero, "Bad Id") == null, "Taken and bad ids are refused");
        int t = talk!.Value;

        Assert.True(editor.AddLink(0, t) != null && editor.AddLink(0, t) == null && editor.AddLink(0, 0) == null, "A link goes once each way and never to itself");
        Assert.True(editor.RenameNode(0, "castle") && editor.Links[0].From == "castle" && editor.FindLink("castle", "dialogue") != null, "Links follow a rename");
        Assert.False(editor.RenameNode(0, "gate"), "A rename can't take another node's id");

        editor.MoveNode(1, new Vector2(10, 90));
        editor.MoveNode(1, new Vector2(20, 100));
        editor.EndTyping();
        Assert.Equal(new Vector2(20, 100), editor.Nodes[1].At);
        history.Undo();
        Assert.Equal(new Vector2(0, 80), editor.Nodes[1].At);

        Assert.True(editor.SetChapter(t, "chapters/keep") && editor.SetRef(t, "chapters/keep/dialogue/wren.json"), "A conversation points at its file");
        Assert.False(editor.SetChapter(t, "chapters/nowhere"), "Only a chapter the package has can be picked");
        Assert.True(editor.SetKind(t, Kind.Quest) && editor.Nodes[t].Ref.Length == 0, "A new kind forgets what it pointed at");
        Assert.True(editor.SetSteps(t, new[] { "Find the cellar", "Open it" }) && !editor.SetSteps(t, new[] { "", "x" }), "A quest has steps, none empty");
        Assert.True(editor.SetXp(1, 40) && !editor.SetXp(1, -5) && !editor.SetXp(0, 10), "Fights and quests take XP; nothing negative, and scenes don't");
        Assert.True(editor.SetMapSize(0, 30, 20) && !editor.SetMapSize(0, 3, 20) && !editor.SetMapSize(1, 30, 20), "Only a scene takes a map size, 8 to 200 each way");
        Assert.True(editor.SetLinkText(0, "They go in") && editor.SetLinkWhen(0, new[] { "gate-open" }) && !editor.SetLinkWhen(0, new[] { "a", "a" }), "A link has words and flags");

        int before = editor.Links.Count;
        Assert.True(editor.RemoveNode(1) && editor.Links.Count == before - 2 && editor.Find("gate") == null, "A removed node takes its links with it");
        history.Undo();
        Assert.True(editor.Find("gate") != null && editor.Links.Count == before, "Undo brings both back");
        Assert.True(editor.RemoveLink(0) && editor.Links.Count == before - 1, "A link can go by itself");
    }

    [Fact]
    public void Suggestions()
    {
        var history = new History();
        var editor = new StoryEditor(history);
        editor.Create();
        editor.SetCatalog(KeepCatalog());

        Assert.True(Offers(editor, "scene|chapters/keep") && Offers(editor, "scene|chapters/warren") && Offers(editor, "encounter|chapters/keep|hall")
            && Offers(editor, "dialogue|chapters/keep|chapters/keep/dialogue/wren.json") && Offers(editor, "quest|chapters/keep|rescue") && Offers(editor, "ending|chapters/keep"),
            "What the package has is offered as nodes");
        Assert.True(editor.Accept("scene|chapters/keep") && editor.Nodes.Count == 1 && editor.Nodes[0].Title == "The Keep" && editor.Nodes[0].Chapter == "chapters/keep",
            "Taking one adds a scene for the chapter");
        Assert.True(editor.Accept("encounter|chapters/keep|gate") && editor.FindLink("keep", "gate") != null && editor.Nodes[1].At.X > editor.Nodes[0].At.X,
            "A fight goes beside its chapter's scene with a link from it");
        Assert.True(Offers(editor, "xp|gate|50") && editor.Accept("xp|gate|50") && editor.Nodes[1].Xp == 50, "Its XP is proposed from creature levels");
        Assert.False(Offers(editor, "xp|gate|50"), "and not again once it matches");
        Assert.True(editor.Accept("scene|chapters/warren") && editor.FindLink("keep", "warren") != null && editor.FindLink("warren", "keep") == null,
            "Travel in adventure.json links the scenes it joins, the way it goes");
        Assert.False(editor.Accept("scene|chapters/warren"), "A suggestion that is taken is gone");

        history.Undo();
        Assert.True(editor.Find("warren") == null && Offers(editor, "scene|chapters/warren"), "Taking one is one undo step");

        Assert.True(editor.Dismiss("ending|chapters/keep") && !Offers(editor, "ending|chapters/keep") && editor.Dismissed.Count == 1, "One can be turned down");
        Assert.Equal("ending|chapters/keep", (string?)JsonNode.Parse(editor.ToJson())!["dismissed"]![0]);
        Assert.True(editor.RestoreDismissed() && Offers(editor, "ending|chapters/keep"), "Turned down ones can come back");

        // quest steps from what comes after it, and a map for a scene with no chapter yet
        int quest = editor.AddNode(Kind.Quest, new Vector2(500, 0), "hunt")!.Value;
        editor.AddLink(quest, editor.Find("gate")!.Value);
        Assert.True(editor.Accept("steps|hunt") && editor.Nodes[quest].Steps.SequenceEqual(new[] { "gate" }), "A quest's steps come from the nodes after it");
        int cave = editor.AddNode(Kind.Scene, new Vector2(700, 0), "cave")!.Value;
        int bats = editor.AddNode(Kind.Encounter, new Vector2(700, 80), "bats")!.Value;
        editor.AddLink(cave, bats);
        Assert.True(Offers(editor, "map|cave") && editor.Accept("map|cave") && editor.Nodes[cave].MapWidth == 32 && editor.Nodes[cave].MapHeight == 20,
            "A scene with no chapter is offered a map with room for its fights");

        int taken = editor.AcceptAll();
        Assert.True(taken >= 4 && editor.Find("warren") != null && editor.Find("wren") != null && editor.Find("rescue") != null && editor.Find("hall") != null,
            "Take all takes every one at once");
        List<StoryEditor.Suggestion> left = editor.Suggestions();
        Assert.True(left.Count == 0, "Nothing is left to suggest: " + string.Join("; ", left.Select(s => s.Key)));
        history.Undo();
        Assert.True(editor.Find("warren") == null && editor.Find("hall") == null, "and undoes as one");
    }

    [Fact]
    public void Problems()
    {
        StoryEditor editor = Loaded(new History());
        editor.SetCatalog(KeepCatalog());
        bool Says(string part) => editor.Problems().Any(p => p.Text.Contains(part, StringComparison.Ordinal) && !p.Error);
        Assert.True(Says("rescue isn't linked"), "A node on its own is a warning");
        editor.SetRef(1, "cellar");
        Assert.True(Says("group cellar isn't in chapters/keep"), "So is pointing at a group the chapter doesn't have");
        int after = editor.AddNode(Kind.Scene, new Vector2(0, 240), "after")!.Value;
        editor.AddLink(3, after);
        Assert.True(Says("end is an ending but the story goes on"), "And a story going on after an ending");
        int warren = editor.AddNode(Kind.Scene, new Vector2(200, 0), "warren")!.Value;
        editor.SetChapter(warren, "chapters/warren");
        editor.AddLink(warren, 0);
        Assert.True(Says("adventure.json has no way from warren to keep"), "A link between scenes that adventure.json can't travel");
        editor.AddLink(0, warren);
        Assert.False(Says("no way from keep to warren"), "but not the way it can");
        Assert.DoesNotContain(editor.Problems(), p => p.Error);
    }

    [Fact]
    public void InCreate()
    {
        using var scratch = new Scratch();
        var package = new CreatePackage(TestContent.AssetsFolder());
        Assert.True(package.New(Path.Combine(scratch.Folder, "create")), package.Status);
        StoryEditor? story = package.StoryEditor();
        Assert.True(story != null && story.Nodes.Count == 0 && Offers(story, "scene|" + package.Chapter), "A new package has an empty story that offers its chapter");
        string file = Path.Combine(package.PackagePath, "story.json");
        Assert.True(package.Save() && package.Status == "Nothing to save" && !File.Exists(file), "An untouched story isn't written");

        // a fight made in Encounters mode shows up here before it is saved
        EncountersEditor encounters = package.EncountersEditor()!;
        int group = encounters.AddGroup("ambush")!.Value;
        Assert.NotNull(encounters.AddCreature(group, "goblin", new Cell(6, 6)));
        story!.SetCatalog(package.StoryCatalog());
        Assert.True(Offers(story, $"encounter|{package.Chapter}|ambush"), "Story mode offers it");
        story.AcceptAll();
        int? ambush = story.Find("ambush");
        Assert.True(ambush != null && story.Nodes[ambush.Value].Xp == encounters.ProposedXp(group), "with the XP Encounters mode proposes");

        Assert.True(package.Save() && File.Exists(file), package.Status);
        var reread = new StoryEditor(new History());
        string scene = CreatePackage.Leaf(package.Chapter);
        Assert.True(reread.Load(File.ReadAllText(file), out _) && reread.Find(scene) != null && reread.Find("ambush") != null && reread.FindLink(scene, "ambush") != null,
            "and it reads back");

        // the same history as the other modes
        story.SetTitle(ambush!.Value, "Ambush on the road");
        package.Undo();
        Assert.Equal("ambush", story.Nodes[ambush.Value].Title);

        // the game's own content: every suggestion taken leaves nothing that stops a save
        package.Open(TestContent.AssetsFolder());
        StoryEditor? keep = package.StoryEditor();
        Assert.True(keep != null && Offers(keep, "scene|chapters/goblin-keep") && Offers(keep, "ending|chapters/goblin-keep"), "The keep's chapter and ending are offered");
        Assert.True(keep!.AcceptAll() > 3 && keep.Find("goblin-keep") != null && keep.Find("entry-hall") != null, "Taking them all draws the keep");
        Assert.DoesNotContain(keep.Problems(), p => p.Error);
    }
}
