using System.Text.Json;

namespace Yorehold.Rules;

/// <summary>A hero as a save lists them, for the load screen.</summary>
public sealed record SaveHero(string Name, string ClassName, int Level, int Hp, int Xp, bool Dead);

/// <summary>
/// What a save file holds, read without loading it: where the party is, who is in it and when it
/// was written. A file that can't be read still gets a summary, with Problem saying why, so the
/// load screen can list it and offer to remove it.
/// </summary>
public sealed class SaveSummary
{
    public string Path { get; private init; } = "";
    public string FileName => System.IO.Path.GetFileName(Path);
    /// <summary>The copy the game keeps of the save before the last one.</summary>
    public bool Backup => Path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
    public DateTime Written { get; private init; }
    /// <summary>Why it can't be loaded; empty when it can.</summary>
    public string Problem { get; private set; } = "";
    public string ChapterId { get; private set; } = "";
    public string ChapterFolder { get; private set; } = "";
    /// <summary>The chapter's title from the content, or its id when the content doesn't have it.</summary>
    public string ChapterTitle { get; private set; } = "";
    /// <summary>The party was at camp; this is the chapter they go back to.</summary>
    public string CampReturn { get; private set; } = "";
    public List<SaveHero> Heroes { get; } = new();
    public int Companions { get; private set; }
    public int Flags { get; private set; }
    /// <summary>Fights started so far.</summary>
    public int Fights { get; private set; }

    /// <summary>Reads this one file. It doesn't fall back to the .bak the way loading does.</summary>
    public static SaveSummary Read(string path, ContentFiles? content = null)
    {
        var summary = new SaveSummary
        {
            Path = path,
            Written = File.Exists(path) ? File.GetLastWriteTime(path) : default,
        };
        try
        {
            summary.Fill(ContentNode.Parse(System.IO.Path.GetFileName(path), World.SaveFile.Read(File.ReadAllText(path))), content);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException or ContentException)
        {
            summary.Problem = error.Message;
        }
        return summary;
    }

    /// <summary>Every save in a folder with its backups, the newest first. None when the folder isn't there.</summary>
    public static List<SaveSummary> List(string folder, ContentFiles? content = null)
    {
        var found = new List<SaveSummary>();
        if (!Directory.Exists(folder))
        {
            return found;
        }
        foreach (string file in Directory.GetFiles(folder))
        {
            if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(Read(file, content));
            }
        }
        // a save and its backup written in the same second: the save itself first
        return found.OrderByDescending(s => s.Written).ThenBy(s => s.Backup).ThenBy(s => s.FileName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void Fill(ContentNode data, ContentFiles? content)
    {
        data.RequireObject("a save");
        ChapterId = data.Text("chapterId", "");
        ChapterFolder = data.Text("chapterFolder", "");
        CampReturn = data.Text("campReturn", "");
        ChapterTitle = ChapterId;
        string chapterFile = ChapterFolder + "/chapter.json";
        if (content != null && ChapterFolder.Length > 0 && ContentFiles.IsContentPath(chapterFile) && content.Exists(chapterFile))
        {
            try
            {
                ChapterTitle = ContentNode.Read(content, chapterFile).Text("title", ChapterId);
            }
            catch (ContentException)
            {
                // the title is only for show; the id will do
            }
        }
        Flags = data.Get("flags")?.Count ?? 0;
        Fights = data.Get("fights") is ContentNode fights && fights.IsWhole ? fights.AsInt(0) : 0;
        if (data.Get("companions") is ContentNode companions && companions.IsObject && companions.Get("members") is ContentNode members)
        {
            Companions = members.Count;
        }

        int heroes = data.At("heroes").Count;
        ContentNode creatures = data.At("creatures");
        if (creatures.Count < heroes)
        {
            throw creatures.Fail("fewer creatures than heroes");
        }
        int index = 0;
        foreach (ContentNode creature in creatures.Items())
        {
            if (index++ >= heroes)
            {
                break;
            }
            ContentNode sheet = creature.At("sheet");
            bool dead = sheet.Get("death") is ContentNode death && death.IsObject && death.Bool("dead", false);
            Heroes.Add(new SaveHero(sheet.Text("name", "?"), sheet.Text("className", ""), sheet.Int("level", 1), sheet.Int("hp", 0),
                sheet.Int("xp", 0), dead));
        }
    }
}
