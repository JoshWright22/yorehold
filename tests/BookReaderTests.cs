using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Yorehold.Rules.Tests;

/// <summary>
/// The first stage of story import: reading a book into pages, blocks and pictures. The PDF read
/// here is written by the test itself (a two-column page with a heading, a shaded passage and a
/// picture), since no real book's text or pictures belong in this repo.
/// </summary>
public class BookReaderTests
{
    private const string Left = "The road to the mill runs along the river and the party follows it until dusk.";
    private const string Right = "On the far bank a lantern swings from a pole where the ferryman waits for his fare.";

    // A grey 80 x 100 PNG: 8-bit greyscale, one stored (uncompressed) block.
    private static byte[] Png(int width, int height)
    {
        var raw = new List<byte>();
        for (int y = 0; y < height; y++)
        {
            raw.Add(0);
            raw.AddRange(Enumerable.Repeat((byte)128, width));
        }
        var zlib = new List<byte> { 0x78, 0x01 };
        for (int at = 0; at < raw.Count; at += 65535)
        {
            int length = Math.Min(65535, raw.Count - at);
            zlib.Add((byte)(at + length == raw.Count ? 1 : 0));
            zlib.AddRange(BitConverter.GetBytes((ushort)length));
            zlib.AddRange(BitConverter.GetBytes((ushort)~length));
            zlib.AddRange(raw.GetRange(at, length));
        }
        uint a = 1, b = 0;
        foreach (byte value in raw)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }
        zlib.AddRange(BigEndian((b << 16) | a));

        var file = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var header = new List<byte>();
        header.AddRange(BigEndian((uint)width));
        header.AddRange(BigEndian((uint)height));
        header.AddRange(new byte[] { 8, 0, 0, 0, 0 });
        Chunk(file, "IHDR", header.ToArray());
        Chunk(file, "IDAT", zlib.ToArray());
        Chunk(file, "IEND", Array.Empty<byte>());
        return file.ToArray();
    }

    private static byte[] BigEndian(uint value) => new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };

    private static void Chunk(List<byte> file, string type, byte[] data)
    {
        byte[] name = System.Text.Encoding.ASCII.GetBytes(type);
        file.AddRange(BigEndian((uint)data.Length));
        file.AddRange(name);
        file.AddRange(data);
        uint crc = 0xFFFFFFFF;
        foreach (byte value in name.Concat(data))
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }
        file.AddRange(BigEndian(~crc));
    }

    // Lines of at most 38 letters, the way a narrow column is set.
    private static void Paragraph(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font, string text, double x, double top)
    {
        var line = new System.Text.StringBuilder();
        foreach (string word in text.Split(' '))
        {
            if (line.Length + word.Length > 38)
            {
                page.AddText(line.ToString().TrimEnd(), 10, new PdfPoint(x, top), font);
                top -= 12;
                line.Clear();
            }
            line.Append(word).Append(' ');
        }
        page.AddText(line.ToString().TrimEnd(), 10, new PdfPoint(x, top), font);
    }

    private static byte[] Book()
    {
        var builder = new PdfDocumentBuilder();
        PdfDocumentBuilder.AddedFont font = builder.AddStandard14Font(Standard14Font.Helvetica);
        PdfPageBuilder page = builder.AddPage(600, 800);
        page.AddText("THE OLD MILL", 20, new PdfPoint(50, 740), font);
        Paragraph(page, font, Left, 50, 700);
        Paragraph(page, font, Right, 330, 700);
        // a shaded passage under the left column, and a picture under the right with a name below it
        page.SetTextAndFillColor(200, 200, 215);
        page.DrawRectangle(new PdfPoint(40, 540), 240, 60, 1, true);
        page.SetTextAndFillColor(0, 0, 0);
        page.AddText("The wheel turns though the race is dry.", 10, new PdfPoint(50, 575), font);
        page.AddPng(Png(80, 100), new PdfRectangle(330, 480, 410, 580));
        page.AddText("MARN", 16, new PdfPoint(330, 455), font);
        return builder.Build();
    }

    private static SourceBook.Block BlockWith(SourceBook book, string words) => book.Pages.SelectMany(p => p.Blocks).First(b => b.Text.Contains(words));

    [Fact]
    public void APdfBecomesBlocksInReadingOrder()
    {
        SourceBook book = BookReader.Read("mill.pdf", Book());

        SourceBook.Page page = Assert.Single(book.Pages);
        Assert.Equal(600, page.Width);
        Assert.Equal(10, book.BodySize);
        List<string> texts = page.Blocks.Select(b => b.Text).ToList();
        // the whole left column is read before the right one
        Assert.Equal(Left, BlockWith(book, "road to the mill").Text);
        Assert.Equal(Right, BlockWith(book, "far bank").Text);
        Assert.True(texts.FindIndex(t => t.Contains("wheel turns")) < texts.FindIndex(t => t.Contains("far bank")), string.Join(" | ", texts));
        Assert.True(texts.IndexOf("THE OLD MILL") < texts.FindIndex(t => t.Contains("road to the mill")), string.Join(" | ", texts));
    }

    [Fact]
    public void LargerLinesAreHeadingsAndBoxesAreSeen()
    {
        SourceBook book = BookReader.Read("mill.pdf", Book());

        SourceBook.Block title = BlockWith(book, "THE OLD MILL");
        Assert.Equal("heading", title.Kind);
        Assert.Equal(20, title.Size);
        Assert.Equal("heading", BlockWith(book, "MARN").Kind);
        SourceBook.Block passage = BlockWith(book, "wheel turns");
        Assert.Equal("text", passage.Kind);
        Assert.Equal("shaded", passage.Box);
        Assert.Equal("", BlockWith(book, "far bank").Box);
        // measured from the top of the page: the title is above the columns
        Assert.True(title.Y < BlockWith(book, "far bank").Y);
    }

    [Fact]
    public void PicturesAreCutOutWithTheirPlace()
    {
        SourceBook book = BookReader.Read("mill.pdf", Book());

        SourceBook.Picture picture = Assert.Single(book.Pictures);
        Assert.Equal("pictures/p1-1.png", picture.File);
        Assert.Equal(1, picture.Page);
        Assert.Equal((80, 100), (picture.PixelWidth, picture.PixelHeight));
        Assert.Equal((330f, 220f, 80f, 100f), (picture.X, picture.Y, picture.Width, picture.Height));
        byte[] file = book.PictureFiles[picture.File];
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, file.Take(4).ToArray());
        // its name is the heading right under it; S4 makes that match
        SourceBook.Block name = BlockWith(book, "MARN");
        Assert.True(name.Y > picture.Y + picture.Height && Math.Abs(name.X - picture.X) < 5);
    }

    private static SourceBook.Block Placed(string text, float x, float y, float width, float height, float size = 10) => new() { Text = text, X = x, Y = y, Width = width, Height = height, Size = size };

    [Fact]
    public void ColumnsNeedNotMeetInTheMiddleAndCellsOnARowAreJoined()
    {
        // a page with art down its right edge: the columns are 25 to 240 and 252 to 467 of 576
        var blocks = new List<SourceBook.Block>
        {
            Placed("left paragraph", 25, 100, 215, 60),
            Placed("right paragraph", 252, 100, 215, 60),
            Placed("Attack:", 258, 200, 30, 5),
            Placed("+1 bonus", 317, 200, 35, 5),
            Placed("Climb:", 366, 200, 29, 5),
            Placed("Jump:", 30, 196, 26, 5),
            Placed("+4 bonus", 89, 196, 35, 5),
            Placed("Orc #1:", 30, 250, 36, 9, 12),
            Placed("o", 99, 254, 7, 5, 12),
            Placed("o", 120, 254, 7, 5, 12),
            Placed("a footer right across the page", 25, 400, 440, 30),
        };

        List<string> read = BookReader.InReadingOrder(blocks, 576).Select(b => b.Text).ToList();

        Assert.Equal(new[] { "left paragraph", "Jump: +4 bonus", "Orc #1: o o", "right paragraph", "Attack: +1 bonus Climb:", "a footer right across the page" }, read);
    }

    [Fact]
    public void ABookIsSavedAndReadBack()
    {
        string folder = Path.Combine(Path.GetTempPath(), "yorehold-import-" + Guid.NewGuid().ToString("N"));
        try
        {
            SourceBook book = BookReader.Read("mill.pdf", Book());
            book.Save(folder);

            Assert.True(File.Exists(Path.Combine(folder, "pictures", "p1-1.png")));
            SourceBook back = SourceBook.Load(folder);
            Assert.Equal(book.Title, back.Title);
            Assert.Equal("mill.pdf", back.File);
            Assert.Equal(book.Pages[0].Blocks, back.Pages[0].Blocks);
            Assert.Equal(book.Pictures, back.Pictures);
            Assert.Empty(back.PictureFiles);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }

    [Fact]
    public void TextAndMarkdownAreReadByParagraph()
    {
        SourceBook text = BookReader.Read("pitch.txt", System.Text.Encoding.UTF8.GetBytes("CHAPTER ONE\r\n\r\nThe mill stood\r\nempty.\r\n\r\nNobody came.\fAfter the break."));
        Assert.Equal(2, text.Pages.Count);
        Assert.Equal(new[] { "heading", "text", "text" }, text.Pages[0].Blocks.Select(b => b.Kind));
        Assert.Equal("The mill stood empty.", text.Pages[0].Blocks[1].Text);
        Assert.Equal("CHAPTER ONE", text.Title);
        Assert.Equal("After the break.", text.Pages[1].Blocks[0].Text);

        SourceBook markdown = BookReader.Read("pitch.md", System.Text.Encoding.UTF8.GetBytes("# The Mill\n\nA short pitch.\n\n## Marn\n\nThe miller."));
        Assert.Equal(new[] { "The Mill", "A short pitch.", "Marn", "The miller." }, markdown.Pages[0].Blocks.Select(b => b.Text));
        Assert.Equal("heading", markdown.Pages[0].Blocks[2].Kind);
        Assert.Equal("The Mill", markdown.Title);
    }

    [Fact]
    public void WhatCannotBeReadSaysSo()
    {
        ContentException type = Assert.Throws<ContentException>(() => BookReader.Read("book.docx", new byte[] { 1, 2, 3 }));
        Assert.Contains(".pdf, .txt and .md", type.Message);
        ContentException broken = Assert.Throws<ContentException>(() => BookReader.Read("book.pdf", new byte[] { 1, 2, 3 }));
        Assert.Contains("book.pdf", broken.Message);
        ContentException format = Assert.Throws<ContentException>(() => SourceBook.Parse("source.json", "{\"format\": \"other\"}"));
        Assert.Contains("format", format.Message);
        Assert.False(BookReader.CanRead("notes.docx"));
        Assert.True(BookReader.CanRead("Book.PDF"));
    }

    // Reads a real book into ../.dev for a look by hand: set YOREHOLD_IMPORT_BOOK to its path.
    // Nothing is checked beyond it reading at all, and without the variable nothing runs.
    [Fact]
    public void ABookNamedInTheEnvironmentIsReadForALook()
    {
        string? path = Environment.GetEnvironmentVariable("YOREHOLD_IMPORT_BOOK");
        string? folder = Environment.GetEnvironmentVariable("YOREHOLD_IMPORT_OUT");
        if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(folder))
        {
            return;
        }
        SourceBook book = BookReader.Read(path);
        book.Save(folder);
        Assert.NotEmpty(book.Pages);
        // and what the layout alone makes of it, next to the source
        BookLayout.Draft(book).Save(folder);
        File.WriteAllLines(Path.Combine(folder, "picture-names.txt"), BookLayout.PictureNames(book).Select(n => $"{n.Key}: {n.Value}"));
        Directory.CreateDirectory(Path.Combine(folder, "cleared"));
        foreach ((string file, byte[] bytes) in book.PictureFiles)
        {
            if (PaperGround.Clear(bytes) is byte[] cleared)
            {
                File.WriteAllBytes(Path.Combine(folder, "cleared", Path.GetFileNameWithoutExtension(file) + ".png"), cleared);
            }
        }
    }
}
