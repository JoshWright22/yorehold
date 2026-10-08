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
    /// <summary>Where the books' answer keys are and the history of scores is kept; null for neither.</summary>
    public string? ScoresFolder { get; init; }
    /// <summary>How much of the book this import got into the game, as of the last read or build.</summary>
    public ImportScore? Score { get; private set; }

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
        Stage = "Finding heroes, foes, talk and treasure";
        var game = new Compendium();
        game.Load(_game, "");
        BookCast.Add(source, draft, game.Classes.Keys.ToList());
        Outline = draft;
        if (model != null)
        {
            Stage = "The story model is reading it";
            StoryReader.Result read = await new StoryReader(model, _game).Read(source, draft, cancel);
            Outline = read.Outline;
            string named = model is ChatModel chat ? $"model {chat.Model}, " : "";
            File.WriteAllLines(Path.Combine(Folder, ModelLogFile),
                new[] { $"{named}calls {read.Calls}, tokens in {read.InputTokens}, out {read.OutputTokens}" }
                    .Concat(read.Notes)
                    .Concat(read.Dropped.Select(d => $"left out {d.Entry}: {d.Why}")));
        }
        Outline.Save(Folder);
        Dropped.Clear();
        SaveDropped();
        Rescore(built: false);
        Stage = "Ready to review";
    }

    /// <summary>An import read before: its outline and what was dropped from it.</summary>
    public static StoryImport Open(string package, ContentFiles game, string? scoresFolder = null)
    {
        var import = new StoryImport(package, game)
        {
            Outline = Outline.Load(Path.Combine(package, ImportFolder)),
            Stage = "Ready to review",
            ScoresFolder = scoresFolder,
        };
        string dropped = Path.Combine(import.Folder, DroppedFile);
        if (File.Exists(dropped) && JsonNode.Parse(File.ReadAllText(dropped)) is JsonArray ids)
        {
            foreach (JsonNode? id in ids)
            {
                import.Dropped.Add(id?.ToString() ?? "");
            }
        }
        import.Rescore(built: false);
        return import;
    }

    /// <summary>The outline without what the writer dropped, and without what names something dropped (a fight in a dropped room).</summary>
    public Outline Kept()
    {
        var kept = new Outline { Title = Outline?.Title ?? "", System = Outline?.System ?? "" };
        if (Outline == null)
        {
            return kept;
        }
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
        return kept;
    }

    // The score is a look at the import, never a reason for it to fail: a key that can't be read
    // or a source file gone missing is said in the score's place and the import goes on.
    private void Rescore(bool built)
    {
        Score = null;
        ScoreProblem = "";
        try
        {
            string book = SourceBook.Load(Folder).File;
            ImportKey? key = ScoresFolder != null ? ImportKey.Find(ScoresFolder, book) : null;
            Score = ImportScore.Of(Folder, Kept(), built ? Package : null, key);
            Score.Save(Folder);
            if (built && ScoresFolder != null)
            {
                Score.AddToHistory(ScoresFolder, Package);
            }
        }
        catch (Exception error) when (error is ContentException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ScoreProblem = error.Message;
        }
    }

    /// <summary>Why there is no score, when there is none.</summary>
    public string ScoreProblem { get; private set; } = "";

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
        try
        {
            Problems.AddRange(new OutlineBuilder(Kept(), Folder, _game) { ClearPaper = clearPaper }.Build(Package));
        }
        catch (Exception error) when (error is ContentException or IOException or UnauthorizedAccessException)
        {
            Problems.Add(error.Message);
        }
        if (Problems.Count == 0)
        {
            Rescore(built: true);
        }
        Stage = Problems.Count == 0 ? "Built" : "Couldn't build";
        return Problems;
    }
}
