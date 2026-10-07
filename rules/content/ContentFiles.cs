namespace Yorehold.Rules;

/// <summary>
/// The content folders the game reads, as one tree of forward-slash paths ("classes/fighter.json").
/// Folders added later win, so a package or a test can sit on top of the game's own files.
/// </summary>
public class ContentFiles
{
    private readonly List<string> _roots = new();
    private readonly SortedSet<string> _read = new(StringComparer.Ordinal);

    public ContentFiles()
    {
    }

    public ContentFiles(string folder)
    {
        Add(folder);
    }

    public void Add(string folder)
    {
        if (!Directory.Exists(folder))
        {
            throw new ContentException(folder, "", "folder not found");
        }
        _roots.Insert(0, Path.GetFullPath(folder));
    }

    /// <summary>True for a path that stays inside the tree: relative, no "..", no backslashes.</summary>
    public static bool IsContentPath(string path)
    {
        if (path.Length == 0 || path.Contains('\\') || path.Contains(':') || path.StartsWith('/') || path.EndsWith('/'))
        {
            return false;
        }
        foreach (string part in path.Split('/'))
        {
            if (part.Length == 0 || part == "." || part == "..")
            {
                return false;
            }
        }
        return true;
    }

    public bool Exists(string path)
    {
        return Find(path) != null;
    }

    public string ReadText(string path)
    {
        string? found = Find(path);
        if (found == null)
        {
            throw new ContentException(path, "", "missing file");
        }
        _read.Add(path);
        return File.ReadAllText(found);
    }

    /// <summary>A file that isn't text, like a recording.</summary>
    public byte[] ReadBytes(string path)
    {
        string? found = Find(path);
        if (found == null)
        {
            throw new ContentException(path, "", "missing file");
        }
        _read.Add(path);
        return File.ReadAllBytes(found);
    }

    /// <summary>Where on disk a content path is found, the top folder first; null when nowhere.</summary>
    public string? FullPath(string path) => Find(path);

    /// <summary>Every path read so far, so a check can tell which files nothing looked at.</summary>
    public IReadOnlyCollection<string> PathsRead => _read;

    /// <summary>The .json files directly in a folder, as content paths in the same order on every machine.</summary>
    public List<string> List(string folder)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string root in _roots)
        {
            string full = folder.Length == 0 ? root : Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full))
            {
                continue;
            }
            foreach (string file in Directory.GetFiles(full, "*.json"))
            {
                names.Add(Path.GetFileName(file));
            }
        }
        string prefix = folder.Length == 0 ? "" : folder + "/";
        return names.Select(name => prefix + name).ToList();
    }

    /// <summary>The pictures directly in a folder (png, jpg, webp), from every root, as content paths in name order.</summary>
    public List<string> Pictures(string folder)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string root in _roots)
        {
            string full = Path.Combine(root, folder.Replace('/', Path.DirectorySeparatorChar));
            if (!Directory.Exists(full))
            {
                continue;
            }
            foreach (string file in Directory.GetFiles(full))
            {
                if (Path.GetExtension(file).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp")
                {
                    names.Add(Path.GetFileName(file));
                }
            }
        }
        return names.Select(name => folder + "/" + name).ToList();
    }

    /// <summary>"creatures/goblin.json" is "goblin": the file name is the id.</summary>
    public static string Stem(string path)
    {
        int slash = path.LastIndexOf('/');
        string name = slash < 0 ? path : path[(slash + 1)..];
        return name.EndsWith(".json", StringComparison.Ordinal) ? name[..^5] : name;
    }

    private string? Find(string path)
    {
        if (!IsContentPath(path))
        {
            return null;
        }
        foreach (string root in _roots)
        {
            string full = Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(full))
            {
                return full;
            }
        }
        return null;
    }
}
