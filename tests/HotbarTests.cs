namespace Yorehold.Rules.Tests;

/// <summary>The bars a player arranges: new actions fill in, moves swap, what is taken off stays off.</summary>
public class HotbarTests
{
    [Fact]
    public void ActionsFillTheBarInOrderTheFirstTime()
    {
        var bar = new Hotbar();
        string[] shown = bar.Layout(new[] { "strike", "dash", "mire" });
        Assert.Equal(new[] { "strike", "dash", "mire" }, shown.Take(3));
        Assert.All(shown.Skip(3), s => Assert.Equal("", s));
    }

    [Fact]
    public void ActionsEveryHeroHasGoOnTheFirstBarAndSpellsOnTheSecond()
    {
        var bar = new Hotbar();
        var always = new HashSet<string> { "strike", "dash" };
        string[] shown = bar.Layout(new[] { "strike", "mire", "dash", "spark" }, always.Contains);
        Assert.Equal(new[] { "strike", "dash", "" }, shown.Take(3));
        Assert.Equal(new[] { "mire", "spark", "" }, shown.Skip(Hotbar.PerBar).Take(3));
        // a full first bar spills over into the second, and the other way round
        var full = new Hotbar();
        string[] many = Enumerable.Range(0, Hotbar.PerBar + 1).Select(i => $"a{i}").ToArray();
        string[] laid = full.Layout(many, _ => true);
        Assert.Equal($"a{Hotbar.PerBar}", laid[Hotbar.PerBar]);
    }

    [Fact]
    public void PuttingAnActionOnTheBarSwapsOrReplaces()
    {
        var bar = new Hotbar();
        bar.Layout(new[] { "strike", "dash", "mire" });
        // dragged from one slot onto another: the two change places
        bar.Put(0, "mire");
        Assert.Equal(new[] { "mire", "dash", "strike" }, bar.Slots.Take(3));
        // dragged from the book onto a free slot further along
        bar.Put(10, "dash");
        Assert.Equal(("", "dash"), (bar.Slots[1], bar.Slots[10]));
    }

    [Fact]
    public void WhatIsTakenOffStaysOffAndNewSpellsFindAGap()
    {
        var bar = new Hotbar();
        bar.Layout(new[] { "strike", "dash" });
        bar.Clear(0);
        string[] shown = bar.Layout(new[] { "strike", "dash", "shield" });
        Assert.Equal(new[] { "shield", "dash" }, shown.Take(2));
        Assert.DoesNotContain("strike", shown);
    }

    [Fact]
    public void ASpellNotPreparedKeepsItsSlotAndComesBack()
    {
        var bar = new Hotbar();
        bar.Layout(new[] { "strike", "mire" });
        Assert.Equal(new[] { "strike", "" }, bar.Layout(new[] { "strike" }).Take(2));
        Assert.Equal(new[] { "strike", "mire" }, bar.Layout(new[] { "strike", "mire" }).Take(2));
    }

    [Fact]
    public void TheBarIsSavedWithTheSheet()
    {
        var sheet = new CharacterSheet { Name = "Wynn" };
        sheet.Hotbar.Layout(new[] { "strike", "dash" });
        sheet.Hotbar.Put(5, "strike");
        CharacterSheet again = CharacterSheet.Read(TestContent.Json(sheet.ToJson().ToJsonString()));
        Assert.Equal(sheet.Hotbar.Slots, again.Hotbar.Slots);
        // taken-off actions stay off after loading
        again.Hotbar.Clear(1);
        Assert.Equal("", again.Hotbar.Layout(new[] { "strike", "dash" })[1]);

        // a sheet saved before there were bars gets every action in order
        CharacterSheet old = CharacterSheet.Read(TestContent.Json("""{"name": "Old"}"""));
        Assert.Equal(new[] { "strike", "dash" }, old.Hotbar.Layout(new[] { "strike", "dash" }).Take(2));
    }
}
