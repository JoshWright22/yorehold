using Godot;

namespace Yorehold;

/// <summary>
/// The game's content as a folder the rules can read with System.IO. From the project that is
/// assets/ itself. An exported game has its files inside the .pck (or the APK), which only Godot
/// can open, so they are copied out to user://content when the game starts.
/// </summary>
public static class PackedContent
{
    private static string? _folder;

    public static string Folder()
    {
        if (_folder != null)
        {
            return _folder;
        }
        if (!OS.HasFeature("template"))
        {
            _folder = ProjectSettings.GlobalizePath("res://assets");
            return _folder;
        }
        // Copied fresh every start: it is a few hundred KB, and an old copy left by another build
        // could hold files this one dropped, which the folder listings would still find.
        string target = ProjectSettings.GlobalizePath("user://content");
        if (System.IO.Directory.Exists(target))
        {
            System.IO.Directory.Delete(target, true);
        }
        int count = Unpack("res://assets", target);
        GD.Print($"Unpacked {count} content files to {target}");
        _folder = target;
        return _folder;
    }

    private static int Unpack(string from, string to)
    {
        System.IO.Directory.CreateDirectory(to);
        int count = 0;
        foreach (string name in DirAccess.GetFilesAt(from))
        {
            // Imported pictures are in the pack only as their import, which the rules never read.
            if (name.EndsWith(".import") || name.EndsWith(".remap") || name.EndsWith(".uid"))
            {
                continue;
            }
            byte[] bytes = FileAccess.GetFileAsBytes(from + "/" + name);
            if (bytes.Length == 0 && FileAccess.GetOpenError() != Error.Ok)
            {
                GD.PushWarning($"Couldn't unpack {from}/{name}: {FileAccess.GetOpenError()}");
                continue;
            }
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(to, name), bytes);
            count++;
        }
        foreach (string folder in DirAccess.GetDirectoriesAt(from))
        {
            count += Unpack(from + "/" + folder, System.IO.Path.Combine(to, folder));
        }
        return count;
    }
}
