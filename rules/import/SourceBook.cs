using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// A book as the import reads it, before anything is made of it: its pages, the text on them in
/// reading order and the pictures cut out of them. Kept as import/source.json with the pictures in
/// import/pictures/ beside it, so the later stages can be run again without the book. Places are in
/// points from the page's top left corner.
/// </summary>
public sealed class SourceBook
{
    public const string Format = "yorehold.source";
    public const int Version = 1;
    public const string FileName = "source.json";

    public sealed record Block
    {
        /// <summary>"heading" or "text".</summary>
        public string Kind { get; init; } = "text";
        /// <summary>What the book drew behind it: "" for nothing, "shaded" for a filled box, "framed" for a ruled one. Books set passages to read out and notes for whoever runs the game apart this way.</summary>
        public string Box { get; init; } = "";
        public string Text { get; init; } = "";
        public float X { get; init; }
        public float Y { get; init; }
        public float Width { get; init; }
        public float Height { get; init; }
        public string Font { get; init; } = "";
        public float Size { get; init; }
    }

    public sealed class Page
    {
        public int Number;
        public float Width, Height;
        public List<Block> Blocks = new();
    }

    public sealed record Picture
    {
        /// <summary>From the import folder, like "pictures/p7-1.jpg".</summary>
        public string File { get; init; } = "";
        public int Page { get; init; }
        public float X { get; init; }
        public float Y { get; init; }
        public float Width { get; init; }
        public float Height { get; init; }
        public int PixelWidth { get; init; }
        public int PixelHeight { get; init; }
    }

    public string Title = "";
    /// <summary>The file it was read from, without its folder.</summary>
    public string File = "";
    /// <summary>The size most of the book's text is set in; 0 for a book with no sizes.</summary>
    public float BodySize;
    public List<Page> Pages { get; } = new();
    public List<Picture> Pictures { get; } = new();
    /// <summary>What the reader left out and why, for the writer to see.</summary>
    public List<string> Skipped { get; } = new();
    /// <summary>The pictures themselves by File. Filled by the reader; empty on a book read back from source.json.</summary>
    public Dictionary<string, byte[]> PictureFiles { get; } = new();

    public JsonObject ToJson()
    {
        var pages = new JsonArray();
        foreach (Page page in Pages)
        {
            var blocks = new JsonArray();
            foreach (Block block in page.Blocks)
            {
                var entry = new JsonObject { ["kind"] = block.Kind };
                if (block.Box.Length > 0)
                {
                    entry["box"] = block.Box;
                }
                entry["text"] = block.Text;
                entry["at"] = Box(block.X, block.Y, block.Width, block.Height);
                if (block.Font.Length > 0)
                {
                    entry["font"] = block.Font;
                }
                if (block.Size > 0)
                {
                    entry["size"] = Round(block.Size);
                }
                blocks.Add(entry);
            }
            pages.Add(new JsonObject { ["number"] = page.Number, ["width"] = Round(page.Width), ["height"] = Round(page.Height), ["blocks"] = blocks });
        }
        var pictures = new JsonArray();
        foreach (Picture picture in Pictures)
        {
            pictures.Add(new JsonObject
            {
                ["file"] = picture.File,
                ["page"] = picture.Page,
                ["at"] = Box(picture.X, picture.Y, picture.Width, picture.Height),
                ["width"] = picture.PixelWidth,
                ["height"] = picture.PixelHeight,
            });
        }
        return new JsonObject
        {
            ["format"] = Format,
            ["version"] = Version,
            ["title"] = Title,
            ["file"] = File,
            ["bodySize"] = Round(BodySize),
            ["pages"] = pages,
            ["pictures"] = pictures,
            ["skipped"] = CreateJson.Texts(Skipped),
        };
    }

    /// <summary>Writes source.json and the pictures into the import folder, making it if need be.</summary>
    public void Save(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (KeyValuePair<string, byte[]> picture in PictureFiles)
        {
            string path = Path.Combine(folder, picture.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllBytes(path, picture.Value);
        }
        System.IO.File.WriteAllText(Path.Combine(folder, FileName), CreateJson.Write(ToJson()));
    }

    public static SourceBook Load(string folder)
    {
        string path = Path.Combine(folder, FileName);
        if (!System.IO.File.Exists(path))
        {
            throw new ContentException(FileName, "", "the import folder has no source.json; read the book first");
        }
        return Parse(FileName, System.IO.File.ReadAllText(path));
    }

    public static SourceBook Parse(string file, string text)
    {
        ContentNode root = ContentNode.Parse(file, text).RequireObject("a source book");
        if (root.Text("format", "") != Format)
        {
            throw root.Fail("format", $"is \"{Format}\"");
        }
        if (root.Int("version", 0, 1) > Version)
        {
            throw root.Fail("version", "was written by a newer version of the game");
        }
        var book = new SourceBook
        {
            Title = root.Text("title", "", 300),
            File = root.Text("file", "", 300),
            BodySize = (float)root.Number("bodySize", 0, 0, 1000),
        };
        foreach (ContentNode entry in root.Get("pages")?.Items() ?? Enumerable.Empty<ContentNode>())
        {
            var page = new Page
            {
                Number = entry.Int("number", book.Pages.Count + 1, 1),
                Width = (float)entry.Number("width", 0, 0),
                Height = (float)entry.Number("height", 0, 0),
            };
            foreach (ContentNode block in entry.Get("blocks")?.Items() ?? Enumerable.Empty<ContentNode>())
            {
                string kind = block.Text("kind", "text", 20);
                if (kind != "heading" && kind != "text")
                {
                    throw block.Fail("kind", "is \"heading\" or \"text\"");
                }
                string box = block.Text("box", "", 20);
                if (box != "" && box != "shaded" && box != "framed")
                {
                    throw block.Fail("box", "is \"shaded\" or \"framed\"");
                }
                float[] at = ReadBox(block);
                page.Blocks.Add(new Block
                {
                    Kind = kind,
                    Box = box,
                    Text = block.Text("text", ""),
                    X = at[0],
                    Y = at[1],
                    Width = at[2],
                    Height = at[3],
                    Font = block.Text("font", "", 200),
                    Size = (float)block.Number("size", 0, 0, 1000),
                });
            }
            book.Pages.Add(page);
        }
        foreach (ContentNode entry in root.Get("pictures")?.Items() ?? Enumerable.Empty<ContentNode>())
        {
            string name = entry.Text("file", "", 300);
            if (name.Length == 0 || name.Contains("..") || Path.IsPathRooted(name))
            {
                throw entry.Fail("file", "is a path inside the import folder");
            }
            float[] at = ReadBox(entry);
            book.Pictures.Add(new Picture
            {
                File = name,
                Page = entry.Int("page", 1, 1),
                X = at[0],
                Y = at[1],
                Width = at[2],
                Height = at[3],
                PixelWidth = entry.Int("width", 0, 0),
                PixelHeight = entry.Int("height", 0, 0),
            });
        }
        book.Skipped.AddRange(root.Texts("skipped"));
        return book;
    }

    private static float[] ReadBox(ContentNode entry)
    {
        var at = new float[4];
        ContentNode? box = entry.Get("at");
        if (box == null)
        {
            return at;
        }
        if (box.Value.Count != 4)
        {
            throw box.Value.Fail("is [x, y, width, height]");
        }
        int index = 0;
        foreach (ContentNode value in box.Value.Items())
        {
            at[index++] = (float)value.AsNumber();
        }
        return at;
    }

    private static JsonArray Box(float x, float y, float width, float height) => new(Round(x), Round(y), Round(width), Round(height));

    // a tenth of a point is finer than anything is placed, and keeps the file readable
    private static double Round(float value) => Math.Round(value, 1);
}
