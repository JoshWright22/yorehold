using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Yorehold;

/// <summary>Where the game keeps the player's own files.</summary>
public static class Places
{
    /// <summary>
    /// The character library. A screenshot run gets a folder of its own under ../.dev, emptied when
    /// it starts, so scripts never touch the player's characters and always start from none.
    /// </summary>
    public static string CharactersFolder()
    {
        if (ShotRunner.Running)
        {
            return System.IO.Path.Combine(Workspace(), ".dev", "shot-characters");
        }
        return ProjectSettings.GlobalizePath("user://characters");
    }

    /// <summary>
    /// The adventure's autosave, one file like the C++ client's. A screenshot run keeps its own
    /// under ../.dev, removed as it starts, so a script always begins from the chapter's start.
    /// </summary>
    public static string SaveFile()
    {
        if (ShotRunner.Running)
        {
            return System.IO.Path.Combine(Workspace(), ".dev", "shot-saves", "adventure.json");
        }
        return ProjectSettings.GlobalizePath("user://saves/adventure.json");
    }

    /// <summary>The folder the load screen lists.</summary>
    public static string SavesFolder() => System.IO.Path.GetDirectoryName(SaveFile())!; // SaveFile always has a folder part

    /// <summary>
    /// Where Create makes new adventures and looks for the ones made before. A screenshot run has
    /// its own under ../.dev, emptied as it starts, so New always makes the same first folder.
    /// </summary>
    public static string CreateFolder()
    {
        if (ShotRunner.Running)
        {
            return System.IO.Path.Combine(Workspace(), ".dev", "shot-create");
        }
        return ProjectSettings.GlobalizePath("user://create");
    }

    /// <summary>What account sync keeps the same as the account: the saves and the characters, with sync.json and sync-backup/ in the user folder.</summary>
    public static Rules.SyncFolders SyncFolders() => new(SavesFolder(), CharactersFolder(), ProjectSettings.GlobalizePath("user://"));

    /// <summary>The game's own content folder, which Create lays under every package.</summary>
    public static string GameContent() => PackedContent.Folder();

    /// <summary>
    /// Pictures players put in user://art, a folder per pack, laid over the game's content so
    /// they are used everywhere (tiles/grass.png, portraits/goblin.png). Sorted by name, the last
    /// winning. Screenshot runs read them too: they only read, and the pictures are what is judged.
    /// </summary>
    public static List<string> ArtFolders()
    {
        string root = ProjectSettings.GlobalizePath("user://art");
        if (!System.IO.Directory.Exists(root))
        {
            return new List<string>();
        }
        List<string> packs = System.IO.Directory.GetDirectories(root).ToList();
        packs.Sort(StringComparer.Ordinal);
        return packs;
    }

    /// <summary>A res://assets/ path as a file System.IO can read, which an export has elsewhere.</summary>
    public static string ContentFile(string resPath)
    {
        const string prefix = "res://assets/";
        if (resPath.StartsWith(prefix))
        {
            return System.IO.Path.Combine(GameContent(), resPath[prefix.Length..]);
        }
        return ProjectSettings.GlobalizePath(resPath);
    }

    /// <summary>
    /// The folder screenshot runs keep their files under (in .dev) and take relative paths from:
    /// the one above the project. An exported game has no project folder, so it uses the user folder.
    /// </summary>
    public static string Workspace()
    {
        if (OS.HasFeature("template"))
        {
            return ProjectSettings.GlobalizePath("user://");
        }
        return System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), ".."));
    }

    /// <summary>
    /// settings.json, the C++ client's file name. A screenshot run has its own under ../.dev,
    /// removed as it starts, so every run begins from the shipped settings and keys.
    /// </summary>
    public static string SettingsFile()
    {
        if (ShotRunner.Running)
        {
            return System.IO.Path.Combine(Workspace(), ".dev", "shot-settings", "settings.json");
        }
        return ProjectSettings.GlobalizePath("user://settings.json");
    }
}
