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
}
