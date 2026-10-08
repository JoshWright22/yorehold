namespace Yorehold.Rules;

/// <summary>One adventure to play: where its package is ("" for the game's own), what it is, and whose.</summary>
public sealed record AdventureListing(string Package, Adventure? Adventure, string Name, string Source, string Problem)
{
    /// <summary>The name of the rules system its first chapter plays; empty when unknown.</summary>
    public string System { get; init; } = "";
}

/// <summary>
/// Every adventure on this machine: the game's own and each package in the create folder (made in
/// Create or imported from a book). A package that can't be read is listed with why, so it
/// doesn't vanish without a word.
/// </summary>
public static class AdventureLibrary
{
    public const string Game = "The game's";
    public const string Made = "Made here";
    public const string Imported = "Imported";

    public static List<AdventureListing> List(string gameFolder, string createFolder)
    {
        var list = new List<AdventureListing> { Read("", new ContentFiles(gameFolder), Game) };
        if (Directory.Exists(createFolder))
        {
            foreach (string folder in Directory.GetDirectories(createFolder).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (!File.Exists(Path.Combine(folder, "adventure.json")))
                {
                    continue;
                }
                var files = new ContentFiles(gameFolder);
                files.Add(folder);
                list.Add(Read(folder, files, StoryImport.IsImport(folder) ? Imported : Made));
            }
        }
        return list;
    }

    /// <summary>The content a package plays from: the game's with the package on top; just the game's for "".</summary>
    public static ContentFiles FilesOf(string gameFolder, string package)
    {
        var files = new ContentFiles(gameFolder);
        if (package.Length > 0)
        {
            files.Add(package);
        }
        return files;
    }

    private static AdventureListing Read(string package, ContentFiles files, string source)
    {
        string fallback = package.Length == 0 ? "Yorehold" : Path.GetFileName(package.TrimEnd('/', '\\'));
        try
        {
            Adventure adventure = Adventure.Load(files);
            return new AdventureListing(package, adventure, adventure.Title.Length > 0 ? adventure.Title : fallback, source, "")
            {
                System = RulesFolder.SystemOf(files, adventure.ChapterFolders[0]).Name,
            };
        }
        catch (ContentException error)
        {
            return new AdventureListing(package, null, fallback, source, error.Message);
        }
    }
}
