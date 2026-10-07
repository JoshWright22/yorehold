namespace Yorehold.Rules;

/// <summary>A way from one chapter's marker to another's, open once all its flags are set.</summary>
public record Transition(string FromChapter, string ExitMarker, string ToChapter, string EntryMarker, List<string> When);

/// <summary>adventure.json at the content root: the chapters of a journey and how they connect.</summary>
public class Adventure
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    /// <summary>Its picture in the library and behind the title, a content path; empty = none.</summary>
    public string Cover { get; init; } = "";
    public int MinLevel { get; init; } = 1;
    public int MaxLevel { get; init; } = 20;
    public int RecommendedPartySize { get; init; } = 4;
    public List<string> ChapterFolders { get; init; } = new();
    /// <summary>The id each folder's chapter.json gives, in the same order.</summary>
    public List<string> ChapterIds { get; init; } = new();
    public List<Transition> Transitions { get; init; } = new();
    /// <summary>Story flags declared up front. Undeclared flags still work.</summary>
    public List<string> Flags { get; init; } = new();
    /// <summary>The chapter folder the party makes camp in; empty = the shared chapters/camp.</summary>
    public string Camp { get; init; } = "";

    public string FolderOf(string chapterId)
    {
        int index = ChapterIds.IndexOf(chapterId);
        return index < 0 ? "" : ChapterFolders[index];
    }

    /// <summary>The open transition from a marker, given the flags that are set, or null.</summary>
    public Transition? TransitionFrom(string chapterId, string marker, IReadOnlyCollection<string> setFlags)
    {
        return Transitions.Find(t => t.FromChapter == chapterId && t.ExitMarker == marker && t.When.All(setFlags.Contains));
    }

    public string? NextChapter(string chapterId, string marker, IReadOnlyCollection<string> setFlags)
    {
        return TransitionFrom(chapterId, marker, setFlags)?.ToChapter;
    }

    /// <summary>
    /// Reads adventure.json and checks it against its chapters: each loads, ids are different,
    /// levels and party sizes fit, and every transition names markers that are on the maps.
    /// </summary>
    private static string CoverPath(ContentNode cover)
    {
        string path = cover.AsText(500);
        if (path.Length > 0 && !ContentFiles.IsContentPath(path))
        {
            throw cover.Fail("is a picture in the content, like pictures/cover.png");
        }
        return path;
    }

    public static Adventure Load(ContentFiles files, string folder = "")
    {
        ContentNode j = ContentNode.Read(files, folder.Length == 0 ? "adventure.json" : folder + "/adventure.json");
        j.RequireObject("an adventure is a JSON object");

        var folders = new List<string>();
        foreach (ContentNode entry in j.Get("chapters")?.Items() ?? Array.Empty<ContentNode>())
        {
            // A chapter is its folder, or an object with one.
            folders.Add(entry.IsObject ? entry.At("folder").AsText() : entry.AsText());
        }
        if (folders.Count == 0)
        {
            throw j.Fail("chapters", "an adventure needs at least one chapter");
        }

        var transitions = new List<Transition>();
        var sources = new List<ContentNode>();
        foreach (ContentNode t in j.Get("transitions")?.Items() ?? Array.Empty<ContentNode>())
        {
            // "from" and "to" are a chapter id beside exitMarker and entryMarker, or objects holding both.
            (string fromChapter, string exit) = End(t, "from", "exitMarker");
            (string toChapter, string entry) = End(t, "to", "entryMarker");
            transitions.Add(new Transition(fromChapter, exit, toChapter, entry, t.Flags("when")));
            sources.Add(t);
        }

        int minLevel = j.Int("minLevel", 1);
        int maxLevel = j.Int("maxLevel", 20);
        if (minLevel < 1 || maxLevel > 20 || minLevel > maxLevel)
        {
            throw j.Fail("minLevel", "minLevel and maxLevel are 1 to 20, lowest first");
        }
        var adventure = new Adventure
        {
            Id = j.Text("id", ""),
            Title = j.Text("title", ""),
            Description = j.Text("description", ""),
            Cover = j.Get("cover") is ContentNode cover ? CoverPath(cover) : "",
            MinLevel = minLevel,
            MaxLevel = maxLevel,
            RecommendedPartySize = j.Int("recommendedPartySize", 4, 1, 4),
            ChapterFolders = folders,
            Transitions = transitions,
            Flags = j.Flags("flags"),
            Camp = j.Text("camp", ""),
        };

        if (adventure.Camp.Length > 0)
        {
            if (folders.Contains(adventure.Camp))
            {
                throw j.Fail("camp", "the camp is its own chapter, not one of the adventure's");
            }
            Chapter.Load(files, adventure.Camp);
        }

        // Every chapter must load; their maps say which markers exist.
        var chapters = new List<Chapter>();
        foreach (string chapterFolder in folders)
        {
            Chapter chapter = Chapter.Load(files, chapterFolder);
            if (adventure.ChapterIds.Contains(chapter.Id))
            {
                throw j.Fail("chapters", $"two chapters have the id {chapter.Id}");
            }
            if (chapter.Level < minLevel || chapter.Level > maxLevel)
            {
                throw j.Fail("chapters", $"{chapter.Id} is written for level {chapter.Level}, outside the adventure's range");
            }
            // The party travels as one: every chapter seats the same heroes.
            if (chapters.Count > 0 && chapter.Party.Count != chapters[0].Party.Count)
            {
                throw j.Fail("chapters", $"{chapter.Id} seats {chapter.Party.Count} heroes, the first chapter {chapters[0].Party.Count}");
            }
            adventure.ChapterIds.Add(chapter.Id);
            chapters.Add(chapter);
        }

        for (int i = 0; i < transitions.Count; i++)
        {
            Transition t = transitions[i];
            Chapter? from = chapters.Find(c => c.Id == t.FromChapter);
            Chapter? to = chapters.Find(c => c.Id == t.ToChapter);
            if (from == null || to == null)
            {
                throw sources[i].Fail($"names a chapter that isn't in the list: {(from == null ? t.FromChapter : t.ToChapter)}");
            }
            if (from.Map.Marker(t.ExitMarker) == null)
            {
                throw sources[i].Fail($"{t.FromChapter} has no marker \"{t.ExitMarker}\"");
            }
            if (to.Map.Marker(t.EntryMarker) is not Cell arrival)
            {
                throw sources[i].Fail($"{t.ToChapter} has no marker \"{t.EntryMarker}\"");
            }
            if (!to.Map.Walkable(arrival))
            {
                throw sources[i].Fail($"{t.ToChapter}'s marker \"{t.EntryMarker}\" is on a cell nobody can stand on");
            }
        }
        return adventure;
    }

    /// <summary>The chapter folders an adventure.json lists, without checking anything. Empty when there is none.</summary>
    public static List<string> ListedChapters(ContentFiles files, string folder = "")
    {
        string path = folder.Length == 0 ? "adventure.json" : folder + "/adventure.json";
        var listed = new List<string>();
        if (!files.Exists(path))
        {
            return listed;
        }
        try
        {
            foreach (ContentNode entry in ContentNode.Read(files, path).Get("chapters")?.Items() ?? Array.Empty<ContentNode>())
            {
                listed.Add(entry.IsObject ? entry.At("folder").AsText() : entry.AsText());
            }
        }
        catch (ContentException)
        {
            // A broken file belongs to nothing; Load says what is wrong with it.
            listed.Clear();
        }
        return listed;
    }

    private static (string Chapter, string Marker) End(ContentNode transition, string key, string markerKey)
    {
        if (transition.Get(key) is ContentNode end && end.IsObject)
        {
            return (end.Text("chapter", ""), end.Text("marker", ""));
        }
        return (transition.Text(key, ""), transition.Text(markerKey, ""));
    }
}
