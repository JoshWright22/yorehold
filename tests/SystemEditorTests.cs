namespace Yorehold.Rules.Tests;

public class SystemEditorTests
{
    private static SystemEditor Opened(History history, string system = "rulesets/dnd5e")
    {
        ContentFiles game = TestContent.Shipped();
        var editor = new SystemEditor(history);
        Assert.True(editor.SetSections(game.ReadText("create/system.json"), out string error), error);
        Assert.True(editor.Open(system + "/ruleset.json", game.ReadText(system + "/ruleset.json"), out error), error);
        return editor;
    }

    [Fact]
    public void EveryShippedSystemOpensAndItsSectionsReadBack()
    {
        foreach (string system in new[] { "rulesets/dnd5e", "rulesets/pf2e", "rulesets/fate-accelerated", "rulesets/yorehold" })
        {
            var history = new History();
            SystemEditor editor = Opened(history, system);
            foreach (string id in editor.Sections.Select(s => s.Id).Append(SystemEditor.Other))
            {
                Assert.True(editor.SetSection(id, editor.SectionText(id), out string error), $"{system} {id}: {error}");
            }
            Assert.False(editor.Changed);
        }
    }

    [Fact]
    public void ASectionTheReaderRefusesIsNotTakenAndAnEditUndoes()
    {
        var history = new History();
        SystemEditor editor = Opened(history);
        Assert.False(editor.SetSection("basics", "{ \"version\": 1, \"id\": \"\" }", out string error));
        Assert.Contains("id", error);
        Assert.False(editor.SetSection("basics", "{ \"baseDc\": 12 }", out error));
        Assert.Contains("Rolls and outcomes", error);
        Assert.False(editor.Changed);

        string checks = editor.SectionText("checks").Replace("\"baseDc\": 8", "\"baseDc\": 12");
        Assert.True(editor.SetSection("checks", checks, out error), error);
        Assert.Equal(12, editor.Rules.BaseDc);
        Assert.True(editor.Changed);
        history.Undo();
        Assert.Equal(8, editor.Rules.BaseDc);
        Assert.False(editor.Changed);
    }

    [Fact]
    public void PlainValuesAreFieldsKeptToTheirKind()
    {
        var history = new History();
        SystemEditor editor = Opened(history);
        Assert.Contains("baseDc", editor.FieldKeys("checks"));
        Assert.DoesNotContain("checks", editor.FieldKeys("checks"));
        Assert.DoesNotContain("baseDc", editor.SectionText("checks", true));

        Assert.False(editor.SetField("baseDc", "twelve", out string error));
        Assert.Contains("number", error);
        Assert.True(editor.SetField("baseDc", "12", out error), error);
        Assert.Equal(12, editor.Rules.BaseDc);
        Assert.True(editor.SetField("bonusActions", "false", out error), error);
        Assert.False(editor.Rules.BonusActions);

        // the box without the fields leaves them as they are
        Assert.True(editor.SetSection("checks", editor.SectionText("checks", true), out error, true), error);
        Assert.Equal(12, editor.Rules.BaseDc);
        Assert.False(editor.SetSection("checks", "{ \"baseDc\": 3 }", out error, true));
        Assert.Contains("field", error);
    }

    [Fact]
    public void TheBlankSystemReads()
    {
        SystemEditor editor = Opened(new History());
        Assert.True(editor.StartBlank(out string error), error);
        Assert.Equal(2, editor.Rules.Abilities.Count);
    }

    [Fact]
    public void APackageSavesItsOwnCopyOfTheSystemItChanged()
    {
        using var scratch = new Scratch();
        var package = new CreatePackage(TestContent.AssetsFolder());
        Assert.True(package.New(Path.Combine(scratch.Folder, "create"), "rulesets/dnd5e"), package.Status);
        Assert.False(package.HasOwnSystem);
        SystemEditor editor = package.SystemEditor()!;
        Assert.NotNull(editor);
        Assert.True(package.Save(), package.Status);
        Assert.False(package.HasOwnSystem);

        string basics = editor.SectionText("basics").Replace("\"name\": \"", "\"name\": \"My ");
        Assert.True(editor.SetSection("basics", basics, out string error), error);
        Assert.True(package.Save(), package.Status);
        Assert.True(package.HasOwnSystem);
        Assert.StartsWith("My ", new ContentFiles(package.PackagePath).ReadText("rulesets/dnd5e/ruleset.json").Split("\"name\": \"")[1]);
        package.CheckFiles();
        Assert.DoesNotContain(package.Problems(), p => p.Error);
    }
}
