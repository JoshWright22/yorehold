using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// The whole of story import for one book, as Create > Import and `--import` run it: read the book
/// into a new package's import folder, draft an outline from its layout, have the story model (if
/// there is one) fill it in, and, once the writer has kept or dropped what they want, build the
/// package. Each stage leaves its file, so the review can be closed and opened again.
/// </summary>
public sealed class StoryImport
{
    public const string ImportFolder = "import";
    public const string DroppedFile = "dropped.json";
    public const string ModelLogFile = "model-log.txt";

    private readonly ContentFiles _game;

    public StoryImport(string package, ContentFiles game)
    {
        Package = package;
        _game = game;
    }

    /// <summary>The package folder the book is imported into.</summary>
    public string Package { get; }
    public string Folder => Path.Combine(Package, ImportFolder);
    /// <summary>What it is doing now, in a few words, for a screen to show.</summary>
    public string Stage { get; private set; } = "";
    public Outline? Outline { get; private set; }
    /// <summary>Ids of the entries the writer dropped in review; the build leaves them out.</summary>
    public SortedSet<string> Dropped { get; } = new(StringComparer.Ordinal);
    public List<string> Problems { get; } = new();

    /// <summary>A new folder for a book in the create folder, named after it and not taken yet.</summary>
    public static string FolderFor(string createFolder, string book)
    {
        string name = OutlineBuilder.Slug(BookLayout.CleanTitle(Path.GetFileNameWithoutExtension(book)));
        string folder = Path.Combine(createFolder, name);
        for (int n = 2; Directory.Exists(folder); n++)
        {
            folder = Path.Combine(createFolder, $"{name}-{n}");
        }
        return folder;
    }

    /// <summary>Stages one to three: the book read, drafted and (with a model) read again by the model. Leaves import/outline.json.</summary>
    public async Task Read(string book, IStoryModel? model, CancellationToken cancel = default)
    {
        Problems.Clear();
        Stage = "Reading the book";
        SourceBook source = await Task.Run(() => BookReader.Read(book), cancel);
        source.Save(Folder);
        Stage = "Finding places and pictures";
        Outline draft = BookLayout.Draft(source);
        Outline = draft;
        if (model != null)
        {
            Stage = "The story model is reading it";
            StoryReader.Result read = await new StoryReader(model, _game).Read(source, draft, cancel);
            Outline = read.Outline;
            File.WriteAllLines(Path.Combine(Folder, ModelLogFile),
                new[] { $"calls {read.Calls}, tokens in {read.InputTokens}, out {read.OutputTokens}" }
                    .Concat(read.Notes)
                    .Concat(read.Dropped.Select(d => $"left out {d.Entry}: {d.Why}")));
        }
        Outline.Save(Folder);
        Dropped.Clear();
        SaveDropped();
        Stage = "Ready to review";
    }

    /// <summary>An import read before: its outline and what was dropped from it.</summary>
    public static StoryImport Open(string package, ContentFiles game)
    {
        var import = new StoryImport(package, game) { Outline = Outline.Load(Path.Combine(package, ImportFolder)), Stage = "Ready to review" };
        string dropped = Path.Combine(import.Folder, DroppedFile);
        if (File.Exists(dropped) && JsonNode.Parse(File.ReadAllText(dropped)) is JsonArray ids)
        {
            foreach (JsonNode? id in ids)
            {
                import.Dropped.Add(id?.ToString() ?? "");
            }
        }
        return import;
    }

    public static bool IsImport(string package) => File.Exists(Path.Combine(package, ImportFolder, Outline.FileName));

    public void SetDropped(string id, bool dropped)
    {
        if (dropped ? Dropped.Add(id) : Dropped.Remove(id))
        {
            SaveDropped();
        }
    }

    private void SaveDropped() => File.WriteAllText(Path.Combine(Folder, DroppedFile), CreateJson.Write(CreateJson.Texts(Dropped)) + "\n");

    /// <summary>
    /// Stage four: the kept entries into the package. An entry that names a dropped one (a fight in
    /// a dropped room) goes too. Problems say what stopped it; none means it opens in Create.
    /// </summary>
    public List<string> Build(bool clearPaper = true)
    {
        Problems.Clear();
        if (Outline == null)
        {
            Problems.Add("nothing has been read yet");
            return Problems;
        }
        Stage = "Building the package";
        var kept = new Outline { Title = Outline.Title, System = Outline.System };
        var gone = new HashSet<string>(Dropped, StringComparer.Ordinal);
        bool more = true;
        while (more)
        {
            more = false;
            foreach (OutlineEntry e in Outline.Entries.Where(e => !gone.Contains(e.Id)))
            {
                bool namesGone = new[] { "place", "from", "to", "dialogue" }.Any(key => gone.Contains(e.Text(key))) || gone.Contains(e.Chapter);
                if (namesGone)
                {
                    gone.Add(e.Id);
                    more = true;
                }
            }
        }
        kept.Entries.AddRange(Outline.Entries.Where(e => !gone.Contains(e.Id)));
        try
        {
            Problems.AddRange(new OutlineBuilder(kept, Folder, _game) { ClearPaper = clearPaper }.Build(Package));
        }
        catch (Exception error) when (error is ContentException or IOException or UnauthorizedAccessException)
        {
            Problems.Add(error.Message);
        }
        Stage = Problems.Count == 0 ? "Built" : "Couldn't build";
        return Problems;
    }
}
