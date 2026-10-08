using System.Text.Json.Nodes;

namespace Yorehold.Rules.Tests;

public class BookCastTests
{
    // A small module laid out the way the old boxed adventures are: a pregenerated hero under their
    // picture, two numbered places, a framed fight box, a talk set as questions and quoted answers,
    // a "Look in the..." find and a locked door to the next place.
    private static SourceBook Module()
    {
        var book = new SourceBook { Title = "The Old Mill", File = "Old_Mill.pdf", BodySize = 10 };
        var heroes = new SourceBook.Page { Number = 1, Width = 600, Height = 800 };
        heroes.Blocks.Add(new SourceBook.Block { Kind = "heading", Text = "MARN", X = 50, Y = 320, Width = 200, Height = 14 });
        heroes.Blocks.Add(new SourceBook.Block { Kind = "text", Text = "Dwarf Fighter", X = 50, Y = 340, Width = 200, Height = 12 });
        heroes.Blocks.Add(new SourceBook.Block { Kind = "text", Text = "Marn swings a hammer for the miller's guild.", X = 50, Y = 360, Width = 200, Height = 12 });
        book.Pictures.Add(new SourceBook.Picture { File = "pictures/p1-1.png", Page = 1, X = 50, Y = 50, Width = 200, Height = 260 });
        book.Pages.Add(heroes);

        var page = new SourceBook.Page { Number = 2, Width = 600, Height = 800 };
        void Add(string kind, string text, string box = "") => page.Blocks.Add(new SourceBook.Block { Kind = kind, Text = text, Box = box, X = 50, Y = page.Blocks.Count * 20, Width = 300, Height = 12 });
        Add("heading", "1: THE RIVER ROAD");
        Add("text", "Two rats gnaw at a sack by the road. The miller's name is Odo, and he waves you over.", "shaded");
        Add("text", "A rat bares its yellow teeth at you.");
        Add("heading", "HANDLING THE FIGHT", "framed");
        Add("text", "If a character attacks a rat, the player needs 11 or better to hit. For each rat, roll 3 dice; if it hits, roll 1 die for damage.", "framed");
        Add("heading", "Rat #1: o o o o", "framed");
        Add("heading", "Rat #2: o o o o", "framed");
        Add("heading", "Talk to the Miller");
        Add("text", "•Who are you?");
        Add("text", "“I'm Odo. This was my mill.”");
        Add("text", "•What happened here?");
        Add("text", "“Rats. Then worse than rats.”");
        Add("heading", "Look in the Sack");
        Add("text", "The sack holds 12 gold pieces and a coil of rope.");
        Add("heading", "Check the Door");
        Add("text", "The mill door is locked. Picking the lock takes a 14 or better. Once it opens, go to Area 2: The Mill.");
        Add("heading", "2: THE MILL");
        Add("text", "The wheel turns though the race is dry.", "shaded");
        book.Pages.Add(page);
        return book;
    }

    private static Outline Cast()
    {
        SourceBook book = Module();
        Outline draft = BookLayout.Draft(book);
        BookCast.Add(book, draft, new[] { "fighter", "cleric" });
        return draft;
    }

    [Fact]
    public void AHeroUnderTheirPictureComesWithTheirClassAndPicture()
    {
        OutlineEntry marn = Assert.Single(Cast().OfKind(OutlineKind.Hero));
        Assert.Equal("Marn", marn.Text("name"));
        Assert.Equal("fighter", marn.Text("class"));
        Assert.Equal("dwarf", marn.Text("race"));
        Assert.Equal("pictures/p1-1.png", marn.Picture);
    }

    [Fact]
    public void AFightBoxGivesTheFoesTheirNumbersAndAFightWhereItStands()
    {
        Outline outline = Cast();
        OutlineEntry rat = Assert.Single(outline.OfKind(OutlineKind.Creature));
        Assert.Equal(4, rat.Data["hp"]!.GetValue<int>());
        Assert.Equal(11, rat.Data["armorClass"]!.GetValue<int>());
        OutlineEntry teeth = outline.Find(rat.Texts("items")[0])!;
        Assert.Equal("Teeth", teeth.Text("name"));
        Assert.Equal("1d6", teeth.Text("damage"));
        OutlineEntry fight = Assert.Single(outline.OfKind(OutlineKind.Encounter));
        Assert.Equal("place-1-the-river-road", fight.Text("place"));
        Assert.Equal(2, fight.Data["creatures"]![0]!["count"]!.GetValue<int>());
    }

    [Fact]
    public void QuestionsAndQuotedAnswersAreATalkWithThePersonTheTextNames()
    {
        Outline outline = Cast();
        OutlineEntry odo = Assert.Single(outline.OfKind(OutlineKind.Npc));
        Assert.Equal("Odo", odo.Text("name"));
        OutlineEntry talk = outline.Find(odo.Text("dialogue"))!;
        var nodes = (JsonArray)talk.Data["nodes"]!;
        Assert.Equal("I'm Odo. This was my mill.", nodes[0]!["text"]!.GetValue<string>());
        Assert.Contains(nodes[0]!["choices"]!.AsArray(), c => c!["text"]!.GetValue<string>() == "What happened here?");
    }

    [Fact]
    public void FindsAndALockedWayToTheNextPlace()
    {
        Outline outline = Cast();
        OutlineEntry sack = Assert.Single(outline.OfKind(OutlineKind.Container));
        Assert.Equal("Sack", sack.Text("name"));
        Assert.Equal(1200, sack.Data["coins"]!.GetValue<int>());
        Assert.Contains("rope", sack.Texts("items"));
        OutlineEntry door = Assert.Single(outline.OfKind(OutlineKind.Link));
        Assert.Equal("locked", door.Text("way"));
        Assert.Equal(14, door.Data["check"]!["difficulty"]!.GetValue<int>());
        Assert.Equal("dex", door.Data["check"]!["skill"]!.GetValue<string>());
    }

    [Fact]
    public void WhatTheLayoutFindsReadsBackAndBuilds()
    {
        using var scratch = new Scratch();
        Outline outline = Outline.Parse("outline.json", Cast().ToJson());
        Directory.CreateDirectory(Path.Combine(scratch.Folder, "import", "pictures"));
        Assert.Empty(new OutlineBuilder(outline, Path.Combine(scratch.Folder, "import"), TestContent.Shipped()).Build(Path.Combine(scratch.Folder, "package")));
    }
}
