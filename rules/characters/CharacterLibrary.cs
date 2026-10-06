using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>One character in the player's library: its choices and what it carries between adventures.</summary>
public sealed class LibraryEntry
{
    /// <summary>The file; empty until written.</summary>
    public string Path { get; set; } = "";
    public CharacterChoices Choices { get; set; } = new();
    public List<Item> Inventory { get; } = new();
    public int Coins { get; set; }
    /// <summary>The adventure save it is playing in; it shows as "away" until that ends.</summary>
    public string Away { get; set; } = "";
    /// <summary>Read from the graveyard: kept to look at, never played again.</summary>
    public bool Retired { get; set; }

    public string FileName => System.IO.Path.GetFileName(Path);
}

/// <summary>
/// The player's characters: one file each in characters/. They belong to the player, not to an
/// adventure. characters/graveyard/ keeps the ones that can't be played any more (dead, or built
/// on a retired ruleset); nothing writes to them again. The file format is the C++ client's, so
/// a library made there opens here.
/// </summary>
public static class CharacterLibrary
{
    public const string GraveyardFolder = "graveyard";
    public static readonly SaveFormat Format = new("yorehold.character", 1);

    public static LibraryEntry Read(string path)
    {
        string name = System.IO.Path.GetFileName(path);
        try
        {
            ContentNode data = ContentNode.Parse(name, Format.ReadFile(path));
            data.RequireObject("a character file is a JSON object");
            var entry = new LibraryEntry
            {
                Path = path,
                Retired = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path)) == GraveyardFolder,
                Choices = CharacterChoices.Read(data.At("choices")),
                Coins = data.Int("coins", 0, 0),
                Away = data.Text("away", ""),
            };
            if (entry.Choices.Name.Length == 0)
            {
                throw data.Fail("choices.name", "a character needs a name");
            }
            if (data.Get("inventory") is ContentNode inventory)
            {
                if (!inventory.IsArray)
                {
                    throw inventory.Fail("is a list");
                }
                entry.Inventory.AddRange(inventory.Items().Select(Item.Read));
            }
            return entry;
        }
        catch (InvalidDataException error)
        {
            throw new ContentException(name, "", error.Message);
        }
    }

    /// <summary>
    /// Every character in the folder by name, then the graveyard's. Broken files are skipped and
    /// listed in problems. A missing folder is just empty.
    /// </summary>
    public static List<LibraryEntry> List(string folder, List<string>? problems = null)
    {
        var living = new List<LibraryEntry>();
        var retired = new List<LibraryEntry>();
        foreach (string dir in new[] { folder, System.IO.Path.Combine(folder, GraveyardFolder) })
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }
            foreach (string file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                try
                {
                    LibraryEntry entry = Read(file);
                    (entry.Retired ? retired : living).Add(entry);
                }
                catch (ContentException error)
                {
                    problems?.Add(error.Message);
                }
            }
        }
        int ByName(LibraryEntry a, LibraryEntry b)
        {
            int name = string.CompareOrdinal(a.Choices.Name, b.Choices.Name);
            return name != 0 ? name : string.CompareOrdinal(a.Path, b.Path);
        }
        living.Sort(ByName);
        retired.Sort(ByName);
        living.AddRange(retired);
        return living;
    }

    /// <summary>
    /// Writes an entry to its file, or a new one to a plain name made from its name ("Ser Ada" to
    /// ser-ada.json, then ser-ada-2.json), and sets its Path. Graveyard files are never written.
    /// </summary>
    public static void Write(string folder, LibraryEntry entry)
    {
        if (entry.Retired || (entry.Path.Length > 0 && System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(entry.Path)) == GraveyardFolder))
        {
            throw new InvalidOperationException("characters in the graveyard can't be changed");
        }
        if (entry.Choices.Name.Length == 0 || entry.Choices.Levels.Count == 0)
        {
            throw new InvalidOperationException("a character needs a name and at least one level");
        }
        Directory.CreateDirectory(folder);
        if (entry.Path.Length == 0)
        {
            string plain = PlainName(entry.Choices.Name);
            entry.Path = FreeFile(folder, plain.Length == 0 ? "character" : plain);
        }
        var inventory = new JsonArray();
        foreach (Item item in entry.Inventory)
        {
            inventory.Add(item.ToJson());
        }
        var data = new JsonObject
        {
            ["choices"] = entry.Choices.ToJson(),
            ["inventory"] = inventory,
            ["coins"] = entry.Coins,
            ["away"] = entry.Away,
        };
        Format.WriteFile(entry.Path, data);
    }

    /// <summary>Moves the file into the graveyard; a later character with the same name gets a new file.</summary>
    public static void Retire(string folder, LibraryEntry entry)
    {
        if (entry.Retired)
        {
            return;
        }
        string graveyard = System.IO.Path.Combine(folder, GraveyardFolder);
        Directory.CreateDirectory(graveyard);
        string to = FreeFile(graveyard, System.IO.Path.GetFileNameWithoutExtension(entry.Path));
        File.Move(entry.Path, to);
        // a backup left behind would come back as a live file
        if (File.Exists(entry.Path + ".bak"))
        {
            File.Delete(entry.Path + ".bak");
        }
        entry.Path = to;
        entry.Retired = true;
    }

    /// <summary>The library file a save names, if it is in the folder; null otherwise.</summary>
    public static LibraryEntry? Find(string folder, string fileName)
    {
        // saves name the file only; anything with a folder in it isn't one of ours
        if (fileName.Length == 0 || System.IO.Path.GetFileName(fileName) != fileName || !fileName.EndsWith(".json", StringComparison.Ordinal))
        {
            return null;
        }
        string path = System.IO.Path.Combine(folder, fileName);
        return File.Exists(path) || File.Exists(path + ".bak") ? Read(path) : null;
    }

    /// <summary>"Ser Ada (2)" to "ser-ada-2"; empty when the name has no letters or digits.</summary>
    public static string PlainName(string name)
    {
        var plain = new System.Text.StringBuilder();
        foreach (char c in name.ToLowerInvariant())
        {
            if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
            {
                plain.Append(c);
            }
            else if (plain.Length > 0 && plain[^1] != '-')
            {
                plain.Append('-');
            }
        }
        string text = plain.ToString().TrimEnd('-');
        return text.Length > 60 ? text[..60] : text;
    }

    // the first of base.json, base-2.json... that isn't taken
    private static string FreeFile(string folder, string plain)
    {
        string path = System.IO.Path.Combine(folder, plain + ".json");
        for (int n = 2; File.Exists(path); n++)
        {
            path = System.IO.Path.Combine(folder, $"{plain}-{n}.json");
        }
        return path;
    }
}
