using System.Diagnostics;
using System.Text;

namespace Yorehold.Rules;

/// <summary>A line of text read off a picture of a page, in that picture's pixels from its top left.</summary>
public sealed record ReadLine(string Text, float X, float Y, float Width, float Height);

/// <summary>What text recognition found on one picture of a page.</summary>
public sealed record ReadPage(int Width, int Height, IReadOnlyList<ReadLine> Lines, string Error = "");

/// <summary>Reads the text off pictures of pages: a scanned book has no text of its own worth having.</summary>
public interface IPageReader
{
    /// <summary>One answer per picture, in the order given.</summary>
    IReadOnlyList<ReadPage> Read(IReadOnlyList<byte[]> pictures);
}

/// <summary>
/// The yorehold-ocr helper beside the game: Windows' own text recognition, run as a separate
/// program because it needs a Windows-only build. Missing on other systems, where scans keep
/// whatever text the PDF carries and the import says so.
/// </summary>
public sealed class OcrHelper : IPageReader
{
    public const string Program = "yorehold-ocr.exe";
    // pages per run: starting the engine costs a few seconds, a long command line fails
    private const int Batch = 16;

    public string Path { get; }

    public OcrHelper(string path)
    {
        Path = path;
    }

    /// <summary>
    /// YOREHOLD_OCR if set, else the program beside the game, else the one the source tree builds
    /// (ocr/bin) above the working folder. Null when there is none.
    /// </summary>
    public static OcrHelper? Find()
    {
        string? set = Environment.GetEnvironmentVariable("YOREHOLD_OCR");
        if (!string.IsNullOrEmpty(set))
        {
            return File.Exists(set) ? new OcrHelper(set) : null;
        }
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }
        string beside = System.IO.Path.Combine(AppContext.BaseDirectory, Program);
        if (File.Exists(beside))
        {
            return new OcrHelper(beside);
        }
        for (DirectoryInfo? folder = new(Directory.GetCurrentDirectory()); folder != null; folder = folder.Parent)
        {
            string built = System.IO.Path.Combine(folder.FullName, "ocr", "bin");
            if (Directory.Exists(built))
            {
                string? found = Directory.EnumerateFiles(built, Program, SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
                if (found != null)
                {
                    return new OcrHelper(found);
                }
            }
        }
        return null;
    }

    public IReadOnlyList<ReadPage> Read(IReadOnlyList<byte[]> pictures)
    {
        var read = new List<ReadPage>();
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "yorehold-ocr-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            for (int start = 0; start < pictures.Count; start += Batch)
            {
                var files = new List<string>();
                for (int i = start; i < Math.Min(pictures.Count, start + Batch); i++)
                {
                    string file = System.IO.Path.Combine(folder, $"page{i}{Extension(pictures[i])}");
                    File.WriteAllBytes(file, pictures[i]);
                    files.Add(file);
                }
                read.AddRange(Run(files));
            }
        }
        finally
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException)
            {
                // a temporary folder left behind is no reason to lose the reading
            }
        }
        return read;
    }

    private List<ReadPage> Run(List<string> files)
    {
        var start = new ProcessStartInfo(Path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (string file in files)
        {
            start.ArgumentList.Add(file);
        }
        using Process process = Process.Start(start) ?? throw new ContentException(Program, "", "could not be started");
        Task<string> errors = process.StandardError.ReadToEndAsync();
        var pages = new List<ReadPage>();
        string? line;
        while ((line = process.StandardOutput.ReadLine()) != null)
        {
            if (line.Trim().Length > 0)
            {
                pages.Add(Parse(line));
            }
        }
        process.WaitForExit();
        if (process.ExitCode != 0 || pages.Count != files.Count)
        {
            throw new ContentException(Program, "", $"stopped without reading the pages ({errors.Result.Trim()})");
        }
        return pages;
    }

    /// <summary>One of the helper's lines of JSON.</summary>
    public static ReadPage Parse(string json)
    {
        ContentNode root = ContentNode.Parse(Program, json).RequireObject("is an object");
        if (root.Text("error", "") is { Length: > 0 } error)
        {
            return new ReadPage(0, 0, Array.Empty<ReadLine>(), error);
        }
        var lines = new List<ReadLine>();
        foreach (ContentNode line in root.Get("lines")?.Items() ?? Enumerable.Empty<ContentNode>())
        {
            lines.Add(new ReadLine(line.Text("text", ""), (float)line.Number("x", 0), (float)line.Number("y", 0), (float)line.Number("w", 0), (float)line.Number("h", 0)));
        }
        return new ReadPage(root.Int("width", 0, 0), root.Int("height", 0, 0), lines);
    }

    private static string Extension(byte[] picture) => picture.Length > 3 && picture[0] == 0xFF && picture[1] == 0xD8 ? ".jpg" : ".png";
}
