using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Util;

namespace Yorehold.Rules;

/// <summary>
/// The first stage of story import: a PDF, a .txt or a .md into a SourceBook. It reads what is on
/// the page and nothing more: no names are guessed and nothing is made up here. What it does judge
/// is the layout: which lines are headings (set larger than the book's body text), which text sits
/// in a box, and the order two columns are read in.
/// </summary>
public static class BookReader
{
    // a picture this much of its page is the paper it is printed on, not an illustration
    public const float BackgroundShare = 0.8f;
    // anything smaller across is an ornament or a bullet
    public const int SmallestPicture = 64;
    // set this much larger than the body, a short line is a heading
    public const float HeadingScale = 1.15f;
    private const int LongestHeading = 90;
    // a block wider than this much of the page is not a column of its own
    private const float WideShare = 0.55f;

    public static bool CanRead(string path)
    {
        string type = Path.GetExtension(path).ToLowerInvariant();
        return type is ".pdf" or ".txt" or ".md";
    }

    public static SourceBook Read(string path)
    {
        if (!File.Exists(path))
        {
            throw new ContentException(Path.GetFileName(path), "", "there is no such file");
        }
        return Read(Path.GetFileName(path), File.ReadAllBytes(path));
    }

    public static SourceBook Read(string name, byte[] bytes)
    {
        switch (Path.GetExtension(name).ToLowerInvariant())
        {
            case ".pdf":
                return ReadPdf(name, bytes);
            case ".txt":
                return ReadText(name, new UTF8Encoding(false).GetString(bytes).TrimStart('﻿'), false);
            case ".md":
                return ReadText(name, new UTF8Encoding(false).GetString(bytes).TrimStart('﻿'), true);
            default:
                throw new ContentException(name, "", "is not a book the import reads; it takes .pdf, .txt and .md");
        }
    }

    // ----- plain text -----

    /// <summary>Paragraphs are split at blank lines and pages at form feeds. In a .md a line of #s is a heading; in either, so is a short line in capitals.</summary>
    public static SourceBook ReadText(string name, string text, bool markdown)
    {
        var book = new SourceBook { File = name, Title = Path.GetFileNameWithoutExtension(name) };
        foreach (string pageText in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\f'))
        {
            var page = new SourceBook.Page { Number = book.Pages.Count + 1 };
            foreach (string paragraph in pageText.Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
            {
                string joined = string.Join(" ", paragraph.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
                if (joined.Length == 0)
                {
                    continue;
                }
                bool heading = false;
                if (markdown && joined.StartsWith('#'))
                {
                    joined = joined.TrimStart('#').Trim();
                    heading = joined.Length > 0;
                }
                else if (joined.Length <= LongestHeading && joined.Any(char.IsLetter) && joined == joined.ToUpperInvariant())
                {
                    heading = true;
                }
                if (joined.Length > 0)
                {
                    page.Blocks.Add(new SourceBook.Block { Kind = heading ? "heading" : "text", Text = joined });
                }
            }
            if (page.Blocks.Count > 0 || book.Pages.Count == 0)
            {
                book.Pages.Add(page);
            }
        }
        string? first = book.Pages.SelectMany(p => p.Blocks).FirstOrDefault(b => b.Kind == "heading")?.Text;
        if (first != null)
        {
            book.Title = first;
        }
        return book;
    }

    // ----- PDF -----

    private sealed class Line
    {
        public string Text = "";
        public string Font = "";
        public float Size;
        public float Left, Top, Right, Bottom;
    }

    private sealed class Draft
    {
        public List<Line> Lines = new();
        public string Box = "";
    }

    public static SourceBook ReadPdf(string name, byte[] bytes)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(bytes);
        }
        catch (Exception error)
        {
            throw new ContentException(name, "", $"is not a PDF the import can open ({error.Message})");
        }
        using (document)
        {
            var book = new SourceBook { File = name, Title = document.Information.Title?.Trim() ?? "" };
            if (book.Title.Length == 0)
            {
                book.Title = Path.GetFileNameWithoutExtension(name);
            }
            var pages = new List<(Page Page, List<Draft> Drafts)>();
            var sizes = new Dictionary<float, int>();
            foreach (Page page in document.GetPages())
            {
                List<Draft> drafts = DraftsOf(page, sizes);
                pages.Add((page, drafts));
                ReadPictures(book, page);
            }
            // the body is whatever size most of the letters are; headings are measured against it
            book.BodySize = sizes.Count == 0 ? 0 : sizes.OrderByDescending(s => s.Value).ThenBy(s => s.Key).First().Key;
            foreach ((Page page, List<Draft> drafts) in pages)
            {
                var made = new SourceBook.Page { Number = page.Number, Width = (float)page.Width, Height = (float)page.Height };
                var blocks = new List<SourceBook.Block>();
                foreach (Draft draft in drafts)
                {
                    blocks.AddRange(BlocksOf(draft, book.BodySize));
                }
                made.Blocks.AddRange(InReadingOrder(blocks, made.Width));
                book.Pages.Add(made);
            }
            if (book.Pages.All(p => p.Blocks.Count == 0))
            {
                book.Skipped.Add("No text was found. A scanned book is pictures of pages and has to be put through text recognition first.");
            }
            return book;
        }
    }

    private static List<Draft> DraftsOf(Page page, Dictionary<float, int> sizes)
    {
        var drafts = new List<Draft>();
        // a drop shadow is the same letters printed twice, a hair apart
        List<Letter> letters = DuplicateOverlappingTextProcessor.Get(page.Letters.Select(WithHeight)).ToList();
        if (letters.All(l => string.IsNullOrWhiteSpace(l.Value)))
        {
            return drafts;
        }
        List<(PdfRectangle Area, string Kind)> boxes = BoxesOf(page);
        IEnumerable<Word> words = DefaultWordExtractor.Instance.GetWords(letters).Where(w => !string.IsNullOrWhiteSpace(w.Text));
        foreach (TextBlock block in RecursiveXYCut.Instance.GetBlocks(words))
        {
            var draft = new Draft();
            foreach (TextLine textLine in block.TextLines)
            {
                List<Letter> inLine = textLine.Words.SelectMany(w => w.Letters).Where(l => !string.IsNullOrWhiteSpace(l.Value)).ToList();
                if (inLine.Count == 0)
                {
                    continue;
                }
                float size = HalfPoint(inLine.Select(l => l.PointSize).OrderBy(s => s).ElementAt(inLine.Count / 2));
                sizes[size] = sizes.GetValueOrDefault(size) + inLine.Count;
                var line = new Line
                {
                    Text = textLine.Text.Trim(),
                    Font = FontName(inLine.GroupBy(l => l.FontName ?? "").OrderByDescending(g => g.Count()).First().Key),
                    Size = size,
                    Left = (float)textLine.BoundingBox.Left,
                    Right = (float)textLine.BoundingBox.Right,
                    Top = (float)(page.Height - textLine.BoundingBox.Top),
                    Bottom = (float)(page.Height - textLine.BoundingBox.Bottom),
                };
                // marks set a little off the baseline of their label (boxes to tick after a name) are the same line
                Line? before = draft.Lines.Count > 0 ? draft.Lines[^1] : null;
                if (before != null && line.Left >= before.Right && Math.Min(line.Bottom, before.Bottom) - Math.Max(line.Top, before.Top) > 0.5f * Math.Min(line.Bottom - line.Top, before.Bottom - before.Top))
                {
                    before.Text += " " + line.Text;
                    before.Right = line.Right;
                    before.Top = Math.Min(before.Top, line.Top);
                    before.Bottom = Math.Max(before.Bottom, line.Bottom);
                }
                else
                {
                    draft.Lines.Add(line);
                }
            }
            if (draft.Lines.Count == 0)
            {
                continue;
            }
            // the smallest box the block's middle lies in: a framed note may itself sit on a shaded page
            PdfPoint middle = block.BoundingBox.Centroid;
            draft.Box = boxes.Where(b => middle.X >= b.Area.Left && middle.X <= b.Area.Right && middle.Y >= b.Area.Bottom && middle.Y <= b.Area.Top).OrderBy(b => b.Area.Area).Select(b => b.Kind).FirstOrDefault() ?? "";
            drafts.Add(draft);
        }
        return drafts;
    }

    /// <summary>Some books' fonts give their letters no height, and lines and columns can't be told apart from flat letters. Such a letter gets the height its size implies.</summary>
    private static Letter WithHeight(Letter letter)
    {
        if (letter.PointSize <= 0 || letter.GlyphRectangle.Height > letter.PointSize * 0.1 || string.IsNullOrWhiteSpace(letter.Value))
        {
            return letter;
        }
        double left = Math.Min(letter.StartBaseLine.X, letter.EndBaseLine.X), right = Math.Max(letter.StartBaseLine.X, letter.EndBaseLine.X);
        if (right - left < 0.01)
        {
            right = left + Math.Max(letter.Width, letter.PointSize * 0.3);
        }
        double baseline = letter.StartBaseLine.Y;
        // a lower-case x is about half the size tall; near enough for telling lines apart
        var box = new PdfRectangle(left, baseline, right, baseline + letter.PointSize * 0.5);
        return new Letter(letter.Value, box, box, letter.StartBaseLine, letter.EndBaseLine, letter.Width, letter.FontSize, letter.FontDetails,
            letter.RenderingMode, letter.StrokeColor, letter.FillColor, letter.PointSize, letter.TextSequence);
    }

    /// <summary>The filled and ruled rectangles on a page that are big enough to hold a paragraph.</summary>
    private static List<(PdfRectangle Area, string Kind)> BoxesOf(Page page)
    {
        var boxes = new List<(PdfRectangle, string)>();
        double pageArea = page.Width * page.Height;
        foreach (PdfPath path in page.Paths)
        {
            if (path.IsClipping)
            {
                continue;
            }
            // one path may hold a frame and a rule under its heading; only the rectangles are boxes
            foreach (PdfSubpath part in path)
            {
                PdfRectangle? bounds = part.IsDrawnAsRectangle ? part.GetBoundingRectangle() : null;
                if (bounds == null || bounds.Value.Width < 80 || bounds.Value.Height < 24 || bounds.Value.Area > pageArea * BackgroundShare)
                {
                    continue;
                }
                if (path.IsFilled && !IsPaper(path.FillColor))
                {
                    boxes.Add((bounds.Value, "shaded"));
                }
                else if (path.IsStroked)
                {
                    boxes.Add((bounds.Value, "framed"));
                }
            }
        }
        return boxes;
    }

    private static bool IsPaper(IColor? color)
    {
        if (color == null)
        {
            return false;
        }
        (double r, double g, double b) = color.ToRGBValues();
        return r > 0.97 && g > 0.97 && b > 0.97;
    }

    /// <summary>A block of the page cut where its lines change between heading and body, so a heading set right on top of its paragraph is still its own block.</summary>
    private static IEnumerable<SourceBook.Block> BlocksOf(Draft draft, float bodySize)
    {
        var run = new List<Line>();
        bool runIsHeading = false;
        foreach (Line line in draft.Lines)
        {
            bool heading = bodySize > 0 && line.Size >= bodySize * HeadingScale && line.Text.Length <= LongestHeading;
            if (run.Count > 0 && (heading != runIsHeading || (heading && Math.Abs(line.Size - run[0].Size) > 0.01f)))
            {
                yield return Join(run, runIsHeading, draft.Box);
                run = new List<Line>();
            }
            runIsHeading = heading;
            run.Add(line);
        }
        if (run.Count > 0)
        {
            yield return Join(run, runIsHeading, draft.Box);
        }
    }

    private static SourceBook.Block Join(List<Line> lines, bool heading, string box)
    {
        var text = new StringBuilder();
        foreach (Line line in lines)
        {
            if (text.Length == 0)
            {
                text.Append(line.Text);
            }
            else if (text.Length > 1 && text[^1] == '-' && char.IsLower(text[^2]) && line.Text.Length > 0 && char.IsLower(line.Text[0]))
            {
                // a word broken over two lines; a real hyphen at a line's end is rare enough to lose
                text.Length--;
                text.Append(line.Text);
            }
            else
            {
                text.Append(' ').Append(line.Text);
            }
        }
        float left = lines.Min(l => l.Left), top = lines.Min(l => l.Top);
        return new SourceBook.Block
        {
            Kind = heading ? "heading" : "text",
            Box = box,
            Text = text.ToString(),
            X = Tenth(left),
            Y = Tenth(top),
            Width = Tenth(lines.Max(l => l.Right) - left),
            Height = Tenth(lines.Max(l => l.Bottom) - top),
            Font = lines.GroupBy(l => l.Font).OrderByDescending(g => g.Count()).First().Key,
            Size = lines[0].Size,
        };
    }

    /// <summary>
    /// Top to bottom, and where the page is set in columns each column whole before the one to its
    /// right. A block across the columns ends the ones above it. Blocks side by side on one row of a
    /// column (the cells of a small table, a name and the boxes to tick after it) become one block.
    /// </summary>
    public static List<SourceBook.Block> InReadingOrder(IEnumerable<SourceBook.Block> blocks, float pageWidth)
    {
        List<SourceBook.Block> all = blocks.ToList();
        // columns are where the paragraphs are: the stretches of the page's width that blocks of several lines cover
        var columns = new List<(float Left, float Right)>();
        foreach (SourceBook.Block block in all.Where(b => b.Height > b.Size * 2.2f && (pageWidth <= 0 || b.Width <= pageWidth * WideShare)).OrderBy(b => b.X))
        {
            if (columns.Count > 0 && block.X < columns[^1].Right - 2)
            {
                columns[^1] = (columns[^1].Left, Math.Max(columns[^1].Right, block.X + block.Width));
            }
            else
            {
                columns.Add((block.X, block.X + block.Width));
            }
        }
        if (columns.Count == 0)
        {
            columns.Add((0, Math.Max(pageWidth, 1)));
        }

        // -1 = across the columns
        int ColumnOf(SourceBook.Block block)
        {
            float left = block.X, right = block.X + block.Width;
            int covered = columns.Count(c => Math.Min(right, c.Right) - Math.Max(left, c.Left) > (c.Right - c.Left) * 0.3f);
            if (covered > 1)
            {
                return -1;
            }
            float middle = (left + right) / 2;
            int nearest = 0;
            for (int index = 1; index < columns.Count; index++)
            {
                if (Away(columns[index], middle) < Away(columns[nearest], middle))
                {
                    nearest = index;
                }
            }
            return nearest;
        }

        var placed = new List<(SourceBook.Block Block, int Column)>();
        foreach (IGrouping<int, SourceBook.Block> column in all.GroupBy(ColumnOf))
        {
            foreach (SourceBook.Block block in column.Key < 0 ? column.ToList() : Rows(column))
            {
                placed.Add((block, column.Key));
            }
        }

        var ordered = new List<SourceBook.Block>();
        var band = new List<(SourceBook.Block Block, int Column)>();
        void Flush()
        {
            ordered.AddRange(band.OrderBy(b => b.Column).ThenBy(b => b.Block.Y).ThenBy(b => b.Block.X).Select(b => b.Block));
            band.Clear();
        }
        foreach ((SourceBook.Block Block, int Column) entry in placed.OrderBy(b => b.Block.Y).ThenBy(b => b.Block.X))
        {
            if (entry.Column < 0)
            {
                Flush();
                ordered.Add(entry.Block);
            }
            else
            {
                band.Add(entry);
            }
        }
        Flush();
        return ordered;
    }

    private static float Away((float Left, float Right) column, float x) => x < column.Left ? column.Left - x : x > column.Right ? x - column.Right : 0;

    private static List<SourceBook.Block> Rows(IEnumerable<SourceBook.Block> column)
    {
        var rows = new List<List<SourceBook.Block>>();
        foreach (SourceBook.Block block in column.OrderBy(b => b.Y).ThenBy(b => b.X))
        {
            List<SourceBook.Block>? row = rows.Count > 0 ? rows[^1] : null;
            SourceBook.Block? first = row?[0];
            bool oneLine = block.Height <= Math.Max(block.Size, 1) * 1.6f;
            if (first != null && oneLine && first.Height <= Math.Max(first.Size, 1) * 1.6f && first.Kind == block.Kind && first.Box == block.Box
                && Math.Min(first.Y + first.Height, block.Y + block.Height) - Math.Max(first.Y, block.Y) > 0.5f * Math.Min(first.Height, block.Height))
            {
                row!.Add(block);
            }
            else
            {
                rows.Add(new List<SourceBook.Block> { block });
            }
        }
        var joined = new List<SourceBook.Block>();
        foreach (List<SourceBook.Block> row in rows)
        {
            if (row.Count == 1)
            {
                joined.Add(row[0]);
                continue;
            }
            List<SourceBook.Block> across = row.OrderBy(b => b.X).ToList();
            float left = across[0].X, top = across.Min(b => b.Y);
            joined.Add(across[0] with
            {
                Text = string.Join(" ", across.Select(b => b.Text)),
                X = left,
                Y = top,
                Width = Tenth(across.Max(b => b.X + b.Width) - left),
                Height = Tenth(across.Max(b => b.Y + b.Height) - top),
            });
        }
        return joined;
    }

    private static void ReadPictures(SourceBook book, Page page)
    {
        int count = 0;
        double pageArea = page.Width * page.Height;
        foreach (IPdfImage image in page.GetImages())
        {
            PdfRectangle bounds = image.BoundingBox;
            if (image.IsImageMask || image.WidthInSamples < SmallestPicture || image.HeightInSamples < SmallestPicture)
            {
                continue;
            }
            if (bounds.Width * bounds.Height > pageArea * BackgroundShare)
            {
                continue;
            }
            byte[] raw = image.RawMemory.ToArray();
            byte[]? file = null;
            string type = "png";
            if (raw.Length > 3 && raw[0] == 0xFF && raw[1] == 0xD8 && raw[2] == 0xFF)
            {
                // a JPEG is kept byte for byte, so nothing is lost to a second compression
                file = raw;
                type = "jpg";
            }
            else if (image.TryGetPng(out byte[]? png) && png != null)
            {
                file = png;
            }
            if (file == null)
            {
                book.Skipped.Add($"Page {page.Number}: a picture is stored in a way the import can't save yet.");
                continue;
            }
            count++;
            string name = $"pictures/p{page.Number}-{count}.{type}";
            book.PictureFiles[name] = file;
            book.Pictures.Add(new SourceBook.Picture
            {
                File = name,
                Page = page.Number,
                X = Tenth(bounds.Left),
                Y = Tenth(page.Height - bounds.Top),
                Width = Tenth(bounds.Width),
                Height = Tenth(bounds.Height),
                PixelWidth = image.WidthInSamples,
                PixelHeight = image.HeightInSamples,
            });
        }
    }

    // places are kept to a tenth of a point, which is what source.json writes
    private static float Tenth(double value) => (float)Math.Round(value, 1);

    private static float HalfPoint(double size) => (float)(Math.Round(size * 2) / 2);

    /// <summary>"ABCDEF+Caxton-Bold" is Caxton-Bold: the six letters only say which of its glyphs the file carries.</summary>
    private static string FontName(string name)
    {
        int plus = name.IndexOf('+');
        return plus == 6 ? name[(plus + 1)..] : name;
    }
}
