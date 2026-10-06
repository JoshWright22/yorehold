using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

/// <summary>Dialogue mode of Create: its commands, undo, what it writes, the companion actions and the game playing what was written.</summary>
public class DialogueEditorTests
{
    // written by hand, with fields the editor has no tool for
    private const string Tam = """
        {
          "id": "tam",
          "start": "hello",
          "mood": "wary",
          "nodes": [
            {
              "id": "hello",
              "speaker": "Tam",
              "text": "You again.",
              "voice": "tam-hello.ogg",
              "choices": [
                {"id": "join", "text": "Come with us.", "next": "yes", "forbid": ["tam_joined"], "emote": "nod"},
                {"id": "talk", "text": "Tell me about the road.", "check": {"skill": "persuasion", "difficulty": 12, "success": "road", "failure": ""}},
                {"id": "bye", "text": "Later.", "next": ""}
              ]
            },
            {"id": "yes", "speaker": "Tam", "text": "Fine.", "set": ["tam_joined"], "do": ["recruit"]},
            {"id": "road", "speaker": "Tam", "text": "It's long."}
          ]
        }
        """;

    private static DialogueEditor.Catalog TamCatalog() => new()
    {
        Skills = new List<string> { "persuasion", "insight", "cha" },
        Companions = new SortedSet<string>(StringComparer.Ordinal) { "tam", "wren" },
        Companion = true,
    };

    private static DialogueEditor Loaded(History history, DialogueEditor.Catalog? catalog = null)
    {
        var editor = new DialogueEditor(history);
        Assert.True(editor.Load(Tam, out string error, catalog ?? TamCatalog()), error);
        return editor;
    }

    [Fact]
    public void LoadAndSave()
    {
        var history = new History();
        DialogueEditor editor = Loaded(history);
        Assert.True(editor.Nodes.Count == 3 && editor.Start == "hello", "A hand-written conversation opens");
        JsonNode out_ = JsonNode.Parse(editor.ToJson())!;
        Assert.True((string?)out_["mood"] == "wary" && (string?)out_["nodes"]![0]!["voice"] == "tam-hello.ogg" && (string?)out_["nodes"]![0]!["choices"]![0]!["emote"] == "nod",
            "Fields the editor has no tool for are written back");
        Assert.Equal("{}", new DialogueEditor(history).ToJson());
        Dialogue? read = editor.Dialogue();
        Assert.True(read != null && read.Nodes[1].Changes.Actions.SequenceEqual(new[] { "recruit" }) && read.Nodes[0].Choices[1].Check!.Difficulty == 12,
            "What it writes reads back in the game's own format");
        Assert.Empty(editor.Problems());

        var broken = new DialogueEditor(history);
        JsonNode bad = JsonNode.Parse(Tam)!;
        bad["nodes"]![0]!["choices"]![0]!["next"] = "nowhere";
        Assert.True(!broken.Load(bad.ToJsonString(), out string error) && error.Contains("nowhere") && !broken.Loaded, "A file the game refuses doesn't open, and says why: " + error);
        Assert.True(!broken.Load("{ not json", out error) && error.Length > 0, "Neither does one that isn't JSON");

        var fresh = new DialogueEditor(history);
        fresh.Create("stranger");
        Assert.True(fresh.Nodes.Count == 1 && fresh.Start == "start" && fresh.Dialogue() != null && fresh.Problems().Count == 1,
            "A new conversation is one empty node, valid, with a note that it has no line");
    }

    [Fact]
    public void Nodes()
    {
        var history = new History();
        DialogueEditor editor = Loaded(history);

        int? added = editor.AddNode();
        Assert.True(added == 3 && editor.Nodes[3].Id == "node-4" && editor.Nodes[3].Speaker == "Tam", "A new node gets a free id and the last speaker");
        Assert.True(editor.AddNode("yes") == null && editor.AddNode("two words") == null, "Node ids are unique and have no spaces");

        Assert.True(editor.RenameNode(1, "agreed") && editor.Nodes[0].Choices[0].Next == "agreed", "Renaming a node takes the replies that led to it along");
        Assert.True(editor.RenameNode(2, "the-road") && editor.Nodes[0].Choices[1].Check!.Success == "the-road", "... and checks");
        Assert.True(editor.RenameNode(0, "hi") && editor.Start == "hi", "... and the start");
        Assert.True(!editor.RenameNode(0, "agreed") && !editor.RenameNode(0, ""), "Not onto another node's id or nothing");

        Assert.True(editor.SetStart(1) && editor.Start == "agreed" && !editor.SetStart(1), "The start can move");
        Assert.True(editor.RemoveNode(1) && editor.Start == "hi" && editor.Nodes[0].Choices[0].Next.Length == 0 && editor.Dialogue() != null,
            "Removing a node ends the replies that led there, and the start goes to the first node");
        Assert.True(editor.LinksTo("the-road") == 1 && editor.LinksTo("node-4") == 0, "Links to a node are counted");

        // typing a line is one undo step until the box is left
        Assert.True(editor.SetText(0, "You") && editor.SetText(0, "You there") && !editor.SetText(0, "You there"), "A line is typed");
        editor.EndTyping();
        Assert.True(editor.SetSpeaker(0, "Tamsin"), "And a speaker");
        history.Undo();
        history.Undo();
        Assert.True(editor.Nodes[0].Text == "You again." && editor.Nodes[0].Speaker == "Tam", "Undo takes back the speaker, then the whole line");
        history.Undo();
        Assert.True(editor.Nodes.Count == 4 && editor.Nodes[1].Id == "agreed" && editor.Start == "agreed", "Undo brings a removed node back, and the start with it");
        while (history.Undo())
        {
        }
        Assert.Equal(Loaded(new History()).ToJson(), editor.ToJson());
        while (history.Redo())
        {
        }
        Assert.True(editor.Nodes.Count == 3 && editor.Nodes[0].Id == "hi" && editor.Nodes[0].Speaker == "Tamsin", "Redo comes all the way back");

        var flags = new DialogueEditor.Flags { Set = new() { "met_tam" }, Actions = new() { "approve 2" } };
        Assert.True(editor.SetNodeFlags(0, flags) && editor.Nodes[0].Flags.Set.SequenceEqual(flags.Set), "A node can set flags and do things on arrival");
        Assert.False(editor.SetNodeFlags(0, flags with { Clear = new() { "met_tam" } }), "But not set and clear the same flag");
        var single = new DialogueEditor(history);
        single.Create("one");
        Assert.False(single.RemoveNode(0), "The last node can't go");
    }

    [Fact]
    public void Choices()
    {
        var history = new History();
        DialogueEditor editor = Loaded(history);

        int? added = editor.AddChoice(2);
        Assert.True(added == 0 && editor.Nodes[2].Choices[0].Text == "..." && editor.Nodes[2].Choices[0].Id == "reply-1" && editor.Nodes[2].Choices[0].Next.Length == 0,
            "A new reply has words, a free id and ends the conversation");
        Assert.True(editor.SetNext(2, 0, "hello") && !editor.SetNext(2, 0, "nowhere") && editor.Nodes[2].Choices[0].Next == "hello", "It can lead to any node there is");
        Assert.True(!editor.SetChoiceText(2, 0, "") && editor.SetChoiceText(2, 0, "Back to it."), "A reply needs words");
        Assert.True(!editor.SetChoiceId(0, 2, "join") && editor.SetChoiceId(0, 2, "leave"), "Reply ids are unique in their node");

        Assert.True(editor.MoveChoice(0, 2, -1) && editor.Nodes[0].Choices[1].Id == "leave" && !editor.MoveChoice(0, 0, -1) && !editor.MoveChoice(0, 2, 1),
            "Replies move up and down, not past the ends");

        Assert.True(!editor.SetConditions(0, 0, new[] { "a" }, new[] { "a" }) && editor.SetConditions(0, 0, new[] { "met_tam" }, new[] { "tam_joined" })
            && editor.Nodes[0].Choices[0].Require.SequenceEqual(new[] { "met_tam" }), "A reply can need flags and be hidden by others, not the same one");

        // a check takes over where the reply went, and gives it back
        Assert.True(editor.SetCheck(0, 0, new DialogueCheck("insight", 10, "", "road")) && editor.Nodes[0].Choices[0].Check!.Success == "yes"
            && editor.Nodes[0].Choices[0].Next.Length == 0 && editor.Dialogue() != null, "Adding a check moves the reply's next to its success");
        Assert.False(editor.SetNext(0, 0, "road"), "A checked reply has no next of its own");
        Assert.True(!editor.SetCheck(0, 0, new DialogueCheck("insight", -1, "yes", "")) && !editor.SetCheck(0, 0, new DialogueCheck("", 10, "yes", ""))
            && !editor.SetCheck(0, 0, new DialogueCheck("insight", 10, "nowhere", "")), "A check needs a skill, a fair difficulty and real nodes");
        Assert.True(editor.SetCheck(0, 0, null) && editor.Nodes[0].Choices[0].Next == "yes" && editor.Nodes[0].Choices[0].Check == null, "Taking it off gives the success back");

        // a new node straight from a reply, as one step
        int before = history.Count;
        int? branch = editor.Branch(2, 0);
        Assert.True(branch == 3 && editor.Nodes[2].Choices[0].Next == editor.Nodes[3].Id && history.Count == before + 1, "A reply can make the node it leads to");
        Assert.True(editor.Branch(0, 2) == 4 && editor.Nodes[0].Choices[2].Check!.Failure == editor.Nodes[4].Id, "A check's empty way gets it");
        history.Undo();
        Assert.True(editor.Nodes.Count == 4 && editor.Nodes[0].Choices[2].Check!.Failure.Length == 0, "Undo takes the node and the link back together");

        Assert.True(editor.RemoveChoice(2, 0) && editor.Nodes[2].Choices.Count == 0 && !editor.RemoveChoice(2, 0), "A reply comes off");
        Assert.NotNull(editor.Dialogue());
    }

    [Fact]
    public void Actions()
    {
        DialogueEditor.Catalog catalog = TamCatalog();
        bool Fine(string action) => DialogueEditor.ActionProblem(action, catalog, out bool error).Length == 0 && !error;
        bool Warning(string action) => DialogueEditor.ActionProblem(action, catalog, out bool error).Length > 0 && !error;
        bool Wrong(string action) => DialogueEditor.ActionProblem(action, catalog, out bool error).Length > 0 && error;

        Assert.True(Fine("recruit") && Fine("dismiss") && Fine("approve 5") && Fine("approve -3") && Fine("approve +2") && Fine("approve wren -3"),
            "The companion actions as the game reads them");
        Assert.True(Fine("release") && Fine("kill") && Fine("fight"), "And the others");
        Assert.True(Wrong("approve") && Wrong("approve lots") && Wrong("approve wren") && Wrong("recruit tam") && Wrong("fight now"),
            "Ones the game would do nothing with are errors");
        Assert.True(Warning("approve stranger 2") && Warning("dance"), "A companion from elsewhere and an unknown action are only warnings");
        catalog.Companion = false;
        Assert.True(Warning("recruit") && Warning("approve 2") && Fine("approve tam 2"), "Recruit and a bare approve only work for someone who can join");
        Assert.True(DialogueEditor.Actions.Contains("recruit") && DialogueEditor.Actions.Contains("approve"), "The list offers both");

        var history = new History();
        DialogueEditor editor = Loaded(history, catalog);
        editor.SetChoiceFlags(0, 2, new DialogueEditor.Flags { Actions = new() { "approve" } });
        List<DialogueEditor.Problem> problems = editor.Problems();
        Assert.True(problems.Any(p => p.Error && p.Text.StartsWith("hello/bye", StringComparison.Ordinal)), "A broken action shows as an error, with where it is");
        Assert.True(problems.Any(p => !p.Error && p.Text.StartsWith("yes: recruit", StringComparison.Ordinal)), "Recruit in a file nobody who can join uses is a warning");

        // the game plays what was written
        editor.Load(Tam, out _, TamCatalog());
        editor.SetChoiceFlags(0, 0, new DialogueEditor.Flags { Set = new() { "asked" }, Actions = new() { "approve 3" } });
        var session = new DialogueSession(editor.Dialogue()!, Array.Empty<string>());
        session.TakeActions();
        session.Choose("join");
        Assert.True(session.TakeActions().SequenceEqual(new[] { "approve 3", "recruit" }) && session.Flags.Contains("asked") && session.Flags.Contains("tam_joined"),
            "A session over the edited file does the reply's actions, then the node's");
    }

    [Fact]
    public void Problems()
    {
        var history = new History();
        DialogueEditor editor = Loaded(history);
        editor.AddNode("lost");
        editor.SetCheck(0, 1, new DialogueCheck("juggling", 10, "road", ""));
        List<DialogueEditor.Problem> found = editor.Problems();
        bool Has(string text) => found.Any(p => !p.Error && p.Text.Contains(text, StringComparison.Ordinal));
        Assert.True(Has("lost can't be reached") && Has("lost has no line") && Has("checks juggling"), "Unreachable nodes, empty lines and unknown skills are warnings");
        Assert.DoesNotContain(found, p => p.Error);
    }

    [Fact]
    public void InCreate()
    {
        using var scratch = new Scratch();
        var package = new CreatePackage(TestContent.AssetsFolder());
        Assert.True(package.New(Path.Combine(scratch.Folder, "create")), package.Status);
        Assert.True(package.DialogueFiles().Count == 0 && package.DialogueEditor() == null, "A new package's chapter has no conversations");

        // a companion who talks through a file in the chapter's dialogue folder
        string folder = Path.Combine(package.PackagePath, package.Chapter);
        JsonNode chapter = JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "chapter.json")))!;
        chapter["npcs"] = JsonNode.Parse("""[{"id": "tam", "name": "Tam", "at": [4, 4], "dialogue": "dialogue/tam.json", "companion": {}}]""");
        Directory.CreateDirectory(Path.Combine(folder, "dialogue"));
        File.WriteAllText(Path.Combine(folder, "chapter.json"), chapter.ToJsonString());
        string tamFile = Path.Combine(folder, "dialogue", "tam.json");
        File.WriteAllText(tamFile, Tam);
        package.Open(package.PackagePath);
        List<string> files = package.DialogueFiles();
        Assert.Equal(new[] { package.Chapter + "/dialogue/tam.json" }, files);
        Assert.True(package.OpenDialogue(files[0]) && package.DialogueEditor()!.Names.Companion && package.DialogueEditor()!.Names.Companions.Contains("tam")
            && package.DialogueEditor()!.Names.Skills.Count > 0, "Its file knows it belongs to someone who can join, and the ruleset's skills");

        string untouched = File.ReadAllText(tamFile);
        Assert.True(package.Save() && File.ReadAllText(tamFile) == untouched, "Saving with nothing changed leaves the file alone");

        DialogueEditor tam = package.DialogueEditor()!;
        tam.SetText(1, "Fine, I'll come.");
        Assert.True(package.History.Dirty && package.Save() && package.Status == "Saved 1 file", package.Status);
        string written = File.ReadAllText(tamFile);
        Dialogue read = Dialogue.Read(ContentNode.Parse("tam.json", written));
        Assert.True(read.Nodes[1].Text == "Fine, I'll come." && (string?)JsonNode.Parse(written)!["mood"] == "wary", "The file on disk has it, and keeps the rest");

        // a broken action keeps the file from being written
        tam.SetNodeFlags(2, new DialogueEditor.Flags { Actions = new() { "approve" } });
        Assert.True(!package.Save() && package.Status.Contains("not saved") && File.ReadAllText(tamFile) == written,
            "A file with an action the game can't do isn't saved, and the status says why");
        package.Undo();

        // a new one goes in the chapter's dialogue folder at the next save
        string made = package.NewDialogue();
        Assert.True(made == package.Chapter + "/dialogue/conversation.json" && package.DialoguePath == made && package.DialogueFiles().Count == 2,
            "New starts another conversation and opens it");
        DialogueEditor fresh = package.DialogueEditor()!;
        fresh.SetText(0, "Hello.");
        int? reply = fresh.AddChoice(0, "Bye.");
        string madeFile = Path.Combine(package.PackagePath, made);
        Assert.True(reply != null && package.Save() && File.Exists(madeFile), package.Status);
        Dialogue.Read(ContentNode.Parse(made, File.ReadAllText(madeFile)));

        // the same history as the other modes
        package.Undo();
        Assert.Empty(fresh.Nodes[0].Choices);
        package.Redo();

        // the game's own content opens too
        package.Open(TestContent.AssetsFolder());
        package.SelectChapter("chapters/goblin-keep");
        List<string> keep = package.DialogueFiles();
        Assert.Contains("chapters/goblin-keep/dialogue/wren.json", keep);
        Assert.Contains("chapters/goblin-keep/dialogue/tobb.json", keep);
        foreach (string path in keep)
        {
            package.OpenDialogue(path);
            DialogueEditor? editor = package.DialogueEditor();
            Assert.True(editor != null && !editor.Problems().Any(p => p.Error), path + ": " + package.DialogueError + string.Join("; ", editor?.Problems().Select(p => p.Text) ?? Array.Empty<string>()));
        }
    }
}
