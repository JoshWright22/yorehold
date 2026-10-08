namespace Yorehold.Rules;

/// <summary>content.json: what a content folder holds and where play starts.</summary>
public class ContentPackage
{
    private static readonly string[] Kinds = { "adventure", "ruleset", "compendium", "character_class", "race", "feat" };

    /// <summary>What the library shows; empty = the file name.</summary>
    public string Name { get; init; } = "";
    /// <summary>Empty = worked out from what the package holds.</summary>
    public string Kind { get; init; } = "";
    public string Id { get; init; } = "";
    public int Revision { get; init; }
    /// <summary>The ruleset version this needs, like "yorehold@1.0"; empty = the game's own.</summary>
    public string Ruleset { get; init; } = "";
    public List<string> Requires { get; init; } = new();
    public List<string> Chapters { get; init; } = new();
    public string DefaultChapter { get; init; } = "";
    public string Theme { get; init; } = "";
    public List<string> Dialogues { get; init; } = new();
    public List<string> Cutscenes { get; init; } = new();

    public static ContentPackage Load(ContentFiles files)
    {
        if (!files.Exists("content.json"))
        {
            throw new ContentException("content.json", "", "missing manifest");
        }
        ContentNode j = ContentNode.Read(files, "content.json");
        j.RequireObject("a manifest is a JSON object");
        if (j.At("format").AsText() != "yorehold.content")
        {
            throw j.Fail("format", "is \"yorehold.content\"");
        }
        if (j.At("version").AsInt() != 1)
        {
            throw j.Fail("version", "unsupported version; this game reads version 1");
        }

        string kind = j.Text("kind", "");
        if (kind.Length > 0 && !Kinds.Contains(kind))
        {
            throw j.Fail("kind", $"unknown kind: {kind}");
        }
        string id = j.Text("id", "");
        if (id.Length > 0 && !ContentIds.IsId(id))
        {
            throw j.Fail("id", "uses a-z, 0-9, - and _");
        }
        List<string> chapters = Paths(j, "chapters");
        if (chapters.Distinct().Count() != chapters.Count)
        {
            throw j.Fail("chapters", "lists a chapter twice");
        }
        // Packages of definitions only have no chapters and need no default.
        string defaultChapter = chapters.Count == 0 ? j.Text("defaultChapter", "") : j.At("defaultChapter").AsText();
        if ((chapters.Count > 0 || defaultChapter.Length > 0) && !chapters.Contains(defaultChapter))
        {
            throw j.Fail("defaultChapter", "must be listed in chapters");
        }
        string theme = j.Text("theme", "");
        if (theme.Length > 0 && !ContentFiles.IsContentPath(theme))
        {
            throw j.Fail("theme", $"expected a relative content path: {theme}");
        }
        return new ContentPackage
        {
            Name = j.Text("name", "", 80),
            Kind = kind,
            Id = id,
            Revision = j.Int("revision", 0, 0),
            Ruleset = j.Text("ruleset", ""),
            Requires = j.Texts("requires"),
            Chapters = chapters,
            DefaultChapter = defaultChapter,
            Theme = theme,
            Dialogues = Paths(j, "dialogues"),
            Cutscenes = Paths(j, "cutscenes"),
        };
    }

    /// <summary>
    /// Loads everything the manifest declares, so a broken package is caught before play: each
    /// chapter, the adventure file if there is one, and the declared dialogues and cutscenes.
    /// own is the package's folder alone when files lays it over other content, so an adventure
    /// file underneath it isn't taken for the package's.
    /// </summary>
    public void Validate(ContentFiles files, ContentFiles? own = null)
    {
        if (Chapters.Count == 0)
        {
            // Nothing to play: the shared definitions still have to load.
            new Compendium().Load(files, RulesFolder.Default, "");
        }
        var ids = new HashSet<string>();
        foreach (string folder in Chapters)
        {
            Chapter chapter = Chapter.Load(files, folder);
            if (!ids.Add(chapter.Id))
            {
                throw new ContentException(folder + "/chapter.json", "id", $"duplicate chapter id {chapter.Id}");
            }
        }
        if ((own ?? files).Exists("adventure.json"))
        {
            Adventure.Load(files);
        }
        if (Theme.Length > 0)
        {
            // The theme is the UI's to read; here it only has to be there and be JSON.
            ContentNode.Read(files, Theme).RequireObject("a theme is a JSON object");
        }
        foreach (string path in Dialogues)
        {
            Dialogue.Read(ContentNode.Read(files, path));
        }
        foreach (string path in Cutscenes)
        {
            Cutscene.Read(ContentNode.Read(files, path));
        }
    }

    private static List<string> Paths(ContentNode j, string key)
    {
        List<string> paths = j.Texts(key);
        foreach (string path in paths.Where(path => !ContentFiles.IsContentPath(path)))
        {
            throw j.Fail(key, $"expected a relative content path: {path}");
        }
        return paths;
    }
}
