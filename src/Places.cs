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
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", ".dev", "shot-characters"));
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
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", ".dev", "shot-saves", "adventure.json"));
        }
        return ProjectSettings.GlobalizePath("user://saves/adventure.json");
    }
}
