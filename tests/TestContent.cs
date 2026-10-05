namespace Yorehold.Rules.Tests;

/// <summary>Where the shipped content is, and scratch folders for content written by a test.</summary>
public static class TestContent
{
    // The tests run from tests/bin/..., so walk up to the folder that holds the Godot project.
    public static string AssetsFolder()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder != null && !File.Exists(Path.Combine(folder.FullName, "project.godot")))
        {
            folder = folder.Parent;
        }
        Assert.True(folder != null, $"project.godot not found above {AppContext.BaseDirectory}");
        // The assert above stops the test when it is null.
        return Path.Combine(folder!.FullName, "assets");
    }

    public static ContentFiles Shipped()
    {
        return new ContentFiles(AssetsFolder());
    }

    public static ContentNode Json(string text, string file = "test.json")
    {
        return ContentNode.Parse(file, text);
    }

    /// <summary>The shipped content with a folder of the test's own files on top of it.</summary>
    public static ContentFiles ShippedWith(Scratch scratch)
    {
        ContentFiles files = Shipped();
        files.Add(scratch.Folder);
        return files;
    }

    /// <summary>Runs something that should refuse its content and hands back what it said.</summary>
    public static ContentException Refused(Action load)
    {
        return Assert.Throws<ContentException>(load);
    }
}

/// <summary>A temp folder that is removed when the test is done.</summary>
public sealed class Scratch : IDisposable
{
    public string Folder { get; }

    public Scratch()
    {
        Folder = Path.Combine(Path.GetTempPath(), "yorehold-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
    }

    public Scratch Write(string path, string text)
    {
        string full = Path.Combine(Folder, path.Replace('/', Path.DirectorySeparatorChar));
        // Every path given here has a folder part or sits in the scratch folder itself.
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
        return this;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, true);
        }
        catch (IOException)
        {
            // A locked temp file is not worth failing a test over.
        }
    }
}
