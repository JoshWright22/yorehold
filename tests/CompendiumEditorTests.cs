using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

/// <summary>Compendium mode of Create: the forms and how they read typed text, the editor's commands and undo, what it writes, and the game's own definitions opening in it untouched.</summary>
public class CompendiumEditorTests
{
    // written by hand, with a field no form lists
    private const string Rope = """
        {
          "id": "rope",
          "name": "Rope",
          "notes": "fifty feet",
          "weight": 10,
          "value": 100
        }
        """;

    private const string Gear = """
        {
          "id": "gear",
          "label": "Gear",
          "folder": "gear",
          "editorOnly": "kept for the caller",
          "fields": [
            {"key": "name", "required": true, "default": "New gear"},
            {"key": "weight", "type": "number", "min": 0},
            {"key": "value", "type": "integer", "min": 0, "max": 1000, "label": "Value (cp)"},
            {"key": "magic", "type": "flag"},
            {"key": "slot", "type": "choice", "options": ["", "hand", "body"]},
            {"key": "ability", "type": "choice", "optionsFrom": "abilities"},
            {"key": "tags", "type": "list", "optionsFrom": "tags"},
            {"key": "use", "type": "json"}
          ]
        }
        """;

    private static string Forms() => File.ReadAllText(Path.Combine(TestContent.AssetsFolder(), "create", "compendium.json"));

    private static FormSchema GearForm()
    {
        FormSchema? read = FormSchema.Read(Gear, out string error);
        Assert.True(read != null, error);
        return read!;
    }

    // the parsed value, or a string no field makes, so a failed parse never matches
    private static string Parsed(FormField field, string text) => field.Parse(text, out JsonNode? value, out _) ? CreateJson.Compact(value) : "<refused>";

    private static bool HasError(List<CompendiumEditor.Problem> problems) => problems.Any(p => p.Error);

    [Fact]
    public void FormsAreRead()
    {
        FormSchema form = GearForm();
        Assert.True(form.Id == "gear" && form.Folder == "gear" && form.IdKey == "id" && form.Fields.Count == 8);
        Assert.True(form.Field("value")!.Title == "Value (cp)" && form.Field("weight")!.Title == "weight" && form.Field("missing") == null);
        Assert.True(form.Field("value")!.Min == 0 && form.Field("value")!.Max == 1000);

        Assert.True(FormSchema.Read("""{"id": "x", "fields": [{"key": "a", "type": "colour"}]}""", out string error) == null && error.Contains("colour"));
        Assert.True(FormSchema.Read("""{"id": "x", "fields": [{"key": "a", "colour": 1}]}""", out error) == null && error.Contains("colour"));
        Assert.True(FormSchema.Read("""{"id": "x", "fields": [{"key": "a"}, {"key": "a"}]}""", out error) == null && error.Contains("twice"));
        Assert.Null(FormSchema.Read("""{"id": "x", "fields": [{"key": "a", "type": "integer", "default": "ten"}]}""", out _));
        Assert.Null(FormSchema.Read("""{"id": "x", "fields": [{"key": "a", "type": "integer", "min": 5, "max": 1}]}""", out _));
        Assert.True(FormSchema.Read("""{"fields": []}""", out _) == null && FormSchema.Read("[", out _) == null);
        // no id key: the file name is the id
        FormSchema? kit = FormSchema.Read("""{"id": "kit", "idKey": "", "fields": [{"key": "name"}]}""", out _);
        Assert.True(kit != null && kit.Blank("door").Count == 0);
    }

    [Fact]
    public void FormsReadTypedText()
    {
        FormSchema form = GearForm();
        FormField weight = form.Field("weight")!, value = form.Field("value")!;
        Assert.True(Parsed(weight, "2.5") == "2.5" && Parsed(weight, " 3 ") == "3");
        Assert.True(!weight.Parse("-1", out _, out string error) && error == "weight is at least 0");
        Assert.Equal("null", Parsed(weight, "")); // blank leaves it out
        Assert.True(Parsed(value, "40") == "40" && !value.Parse("4.5", out _, out error) && error == "Value (cp) is a whole number" && Parsed(value, "2000") == "<refused>");
        Assert.True(Parsed(form.Field("magic")!, "true") == "true" && Parsed(form.Field("magic")!, "no") == "false" && Parsed(form.Field("magic")!, "maybe") == "<refused>");
        Assert.True(Parsed(form.Field("tags")!, "a, b,, c ") == """["a","b","c"]""" && Parsed(form.Field("tags")!, " , ") == "null");
        Assert.True(Parsed(form.Field("use")!, """{"cost": 1}""") == """{"cost":1}""" && !form.Field("use")!.Parse("{cost", out _, out error) && error.Contains("JSON"));
        Assert.Equal("\"\"", Parsed(form.Field("name")!, "")); // required: kept, and reported as empty
        Assert.Equal("\"hand\"", Parsed(form.Field("slot")!, "hand"));

        JsonObject item = JsonNode.Parse("""{"id": "rope", "name": "Rope", "weight": 10, "tags": ["tool", "camp"], "use": {"cost": 1}}""")!.AsObject();
        Assert.True(form.Field("name")!.Text(item) == "Rope" && weight.Text(item) == "10" && form.Field("tags")!.Text(item) == "tool, camp");
        Assert.True(form.Field("use")!.Text(item) == """{"cost":1}""" && value.Text(item).Length == 0);
    }

    [Fact]
    public void FormsCheckValues()
    {
        FormSchema form = GearForm();
        var lists = new Dictionary<string, List<string>> { ["abilities"] = new() { "str", "dex" }, ["tags"] = new() { "tool" } };
        Assert.Equal(new[] { "str", "dex" }, form.Field("ability")!.Choices(lists));
        Assert.Equal(3, form.Field("slot")!.Choices(lists).Count);

        JsonObject made = form.Blank("rope");
        Assert.True(CreateJson.Compact(made) == """{"id":"rope","name":"New gear"}""" && form.Problems(made, lists).Count == 0);

        JsonObject wrong = JsonNode.Parse("""{"id": "x", "name": "", "weight": "heavy", "value": 2.5, "magic": 1, "slot": 3, "tags": [1], "extra": true}""")!.AsObject();
        var other = new List<string>();
        List<string> found = form.Problems(wrong, lists, other);
        Assert.True(found.Count == 5 && found[0] == "name: is empty" && found[1] == "weight: is a number", string.Join("; ", found));
        // a choice written another way (an object, a number) is left to the file's reader
        Assert.Equal(new[] { "slot: names something that isn't offered here" }, other);
        Assert.Equal(new[] { "extra" }, form.Unlisted(wrong));

        var warnings = new List<string>();
        JsonObject odd = JsonNode.Parse("""{"name": "Odd", "ability": "luck", "tags": ["tool", "junk"], "slot": "hand"}""")!.AsObject();
        Assert.True(form.Problems(odd, lists, warnings).Count == 0 && warnings.Count == 2);
        // without a list to check against, anything goes
        warnings.Clear();
        Assert.True(form.Problems(odd, new Dictionary<string, List<string>>(), warnings).Count == 0 && warnings.Count == 0);
    }

    [Fact]
    public void Loading()
    {
        var history = new History();
        var editor = new CompendiumEditor(history);
        Assert.True(editor.SetKinds(Forms(), out string error) && editor.Kinds.Count == 10 && editor.KindOf("item") != null && editor.KindOf("spell")!.Ruleset,
            "The game's forms file lists every kind: " + error);
        Assert.True(!editor.SetKinds("""{"kinds": [{"id": "item", "folder": "items", "fields": [{"key": "name", "type": "colour"}]}]}""", out error) && error.Contains("colour"),
            "A forms file with an unknown field type is refused, saying which");
        editor.SetKinds(Forms(), out _);

        Assert.True(editor.AddFile("item", "items/rope.json", Rope, out error) && editor.Of("item").Count == 1, "A hand-written item opens");
        Assert.True(editor.Changed().Count == 0 && editor.Problems(0).Count == 0, "It isn't rewritten until something changes, and nothing is wrong with it: "
            + string.Join("; ", editor.Problems(0).Select(p => p.Text)));
        Assert.True(!editor.AddFile("item", "items/broken.json", "[1, 2]", out error) && error.Contains("broken"), "A file that isn't an object is left out");
        Assert.False(editor.AddFile("dragon", "dragons/red.json", "{}", out _), "A kind with no form is left out");
    }

    [Fact]
    public void Editing()
    {
        var history = new History();
        var editor = new CompendiumEditor(history);
        editor.SetKinds(Forms(), out _);
        editor.SetOptions(new Dictionary<string, List<string>> { ["abilities"] = new() { "str", "dex" }, ["items"] = new() { "longsword" } });
        editor.AddFile("item", "items/rope.json", Rope, out _);
        double Value(string key) => FormJson.Number(editor.Entries[0].Value[key]);

        Assert.True(editor.SetField(0, "value", "250") && Value("value") == 250, "A number typed in a box is set");
        Assert.True(!editor.SetField(0, "weight", "-1", out string error) && error.Contains("at least 0") && Value("weight") == 10, "A number out of range is refused, saying why");
        Assert.True(!editor.SetField(0, "value", "lots") && Value("value") == 250, "Text in a number box is refused");
        Assert.True(editor.SetField(0, "slot", "offHand") && editor.SetField(0, "magic", "true") && editor.SetField(0, "modifiers", """[{"stat": "armorClass", "value": 1}]"""),
            "Choices, flags and nested JSON are set");
        Assert.True(editor.SetField(0, "weight", "") && !editor.Entries[0].Value.ContainsKey("weight"), "A blank box leaves the field out");
        Assert.True(!editor.SetField(0, "id", "cord") && !editor.SetField(0, "colour", "red"), "The id follows the file name, and a field no form lists can't be set");

        JsonNode written = JsonNode.Parse(editor.ToJson(0))!;
        Assert.True((string?)written["notes"] == "fifty feet" && (string?)written["slot"] == "offHand", "Fields the form doesn't list are written back");
        Assert.True(editor.Changed().SequenceEqual(new[] { 0 }) && !HasError(editor.Problems(0)), string.Join("; ", editor.Problems(0).Select(p => p.Text)));
        string text = editor.ToJson(0);
        Assert.True(text.IndexOf("\"id\"", StringComparison.Ordinal) < text.IndexOf("\"notes\"", StringComparison.Ordinal)
            && text.IndexOf("\"notes\"", StringComparison.Ordinal) < text.IndexOf("\"value\"", StringComparison.Ordinal), "Fields stay in the order the file had them");

        // one box typed into is one undo step
        editor.EndTyping();
        editor.SetField(0, "name", "Ro");
        editor.SetField(0, "name", "Rop");
        editor.SetField(0, "name", "Rope ladder");
        editor.EndTyping();
        history.Undo();
        Assert.Equal("Rope", (string?)editor.Entries[0].Value["name"]);
        history.Redo();
        Assert.Equal("Rope ladder", (string?)editor.Entries[0].Value["name"]);

        // the game's reader has the last word
        editor.SetField(0, "damage", "zz");
        Assert.True(HasError(editor.Problems(0)), "Dice the game can't read are an error");
        editor.SetField(0, "damage", "1d4");
        editor.SetField(0, "attackAbility", "luck");
        List<CompendiumEditor.Problem> odd = editor.Problems(0);
        Assert.True(odd.Count > 0 && !HasError(odd), "A name the lists don't offer is only a warning: " + string.Join("; ", odd.Select(p => p.Text)));
    }

    [Fact]
    public void Adding()
    {
        var history = new History();
        var editor = new CompendiumEditor(history);
        editor.SetKinds(Forms(), out _);
        editor.AddFile("item", "items/rope.json", Rope, out _);

        int? made = editor.Add("item", "items");
        Assert.True(made is int m && editor.Entries[m].Id == "new-item" && editor.Entries[m].Path == "items/new-item.json"
            && (string?)editor.Entries[m].Value["name"] == "New item", "Add makes an item from the form's defaults");
        Assert.True(editor.Add("item", "items") != null && editor.Of("item").Count == 3 && editor.Entries[^1].Id == "new-item-2", "A second one gets the next free id");
        Assert.True(editor.Add("item", "items", "rope") == null && editor.Add("item", "items", "Big Rope") == null && editor.Add("item", "") == null,
            "A taken id, a bad id or no folder adds nothing");
        int? copied = editor.Copy(0);
        Assert.True(copied is int c && editor.Entries[c].Id == "rope-copy" && (string?)editor.Entries[c].Value["id"] == "rope-copy"
            && (string?)editor.Entries[c].Value["notes"] == "fifty feet", "Copy makes another under a new id");
        Assert.Equal(4, editor.Options()["items"].Count);
        history.Undo();
        history.Undo();
        Assert.Equal(2, editor.Of("item").Count);

        // every kind's new entry is one the game would load
        editor.SetOptions(new Dictionary<string, List<string>> { ["ai"] = new() { "cunning" } });
        foreach (CompendiumEditor.Kind kind in editor.Kinds)
        {
            int? fresh = editor.Add(kind.Form.Id, kind.Form.Folder);
            List<CompendiumEditor.Problem> problems = fresh is int f ? editor.Problems(f) : new();
            Assert.True(fresh != null && problems.Count == 0, $"A new {kind.Form.Id} has nothing wrong: " + string.Join("; ", problems.Select(p => p.Text)));
        }
    }

    [Fact]
    public void InCreate()
    {
        using var scratch = new Scratch();
        var package = new CreatePackage(TestContent.AssetsFolder());
        Assert.True(package.New(Path.Combine(scratch.Folder, "create")), package.Status);
        CompendiumEditor? editor = package.CompendiumEditor();
        Assert.True(editor != null && editor.Entries.All(e => e.Id.StartsWith("example-", StringComparison.Ordinal)),
            "A new package has no definitions of its own but the examples to copy: " + package.CompendiumError);
        Assert.True(package.CompendiumFolders["item"] == "items" && !package.CompendiumFolders.ContainsKey("spell"),
            "Items go at the root; spells need a ruleset folder the new package doesn't have");
        Dictionary<string, List<string>> options = editor!.Options();
        Assert.True(options["items"].Count > 0 && options["abilities"].Count > 0, "The forms offer the game's own items and the ruleset's abilities");

        int rope = editor.Add("item", "items", "rope")!.Value;
        editor.SetField(rope, "name", "Rope");
        editor.SetField(rope, "weight", "10");
        Assert.True(package.History.Dirty && package.Save() && package.Status == "Saved 1 file", package.Status);
        string file = Path.Combine(package.PackagePath, "items", "rope.json");
        string written = File.ReadAllText(file);
        ItemDefinition read = ItemDefinition.Read(ContentNode.Parse("items/rope.json", written));
        Assert.True(read.Name == "Rope" && read.Weight == 10, "The file on disk is one the game reads");
        Assert.True(package.Save() && package.Status == "Nothing to save", package.Status);

        // something the game would refuse isn't written
        editor.SetField(rope, "damage", "zz");
        Assert.True(!package.Save() && package.Status.Contains("not saved") && File.ReadAllText(file) == written, "An item the game can't read isn't saved, and the status says why");
        package.Undo();
        Assert.Null(JsonNode.Parse(editor.ToJson(rope))!["damage"]);

        // opened again, it is read from the folder
        package.Open(package.PackagePath);
        editor = package.CompendiumEditor();
        Assert.True(editor != null && editor.Of("item").Count(i => !editor.Entries[i].Id.StartsWith("example-", StringComparison.Ordinal)) == 1 && editor.Changed().Count == 0, "Reopened, the saved item is there and unchanged");

        // the game's own content opens with nothing wrong and nothing to rewrite
        package.Open(TestContent.AssetsFolder());
        editor = package.CompendiumEditor();
        Assert.True(editor != null && editor.Of("item").Count >= 16 && editor.Of("spell").Count >= 20 && editor.Of("kit").Count == 6 && editor.Of("ai").Count >= 10,
            "The game's items, spells, kits and AI profiles are listed");
        Assert.Equal("rulesets/yorehold/spells", package.CompendiumFolders["spell"]);
        for (int i = 0; i < editor!.Entries.Count; i++)
        {
            List<CompendiumEditor.Problem> problems = editor.Problems(i);
            Assert.False(HasError(problems), editor.Entries[i].Path + ": " + string.Join("; ", problems.Select(p => p.Text)));
        }
        Assert.Empty(editor.Changed());
    }
}
