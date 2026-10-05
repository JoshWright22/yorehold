using System.Text.Json;

namespace Yorehold.Rules.Tests;

public class ContentTests
{
    // The tests run from tests/bin/..., so walk up to the folder that holds the Godot project.
    private static string AssetsFolder()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder != null && !File.Exists(Path.Combine(folder.FullName, "project.godot")))
        {
            folder = folder.Parent;
        }
        Assert.True(folder != null, $"project.godot not found above {AppContext.BaseDirectory}");
        return Path.Combine(folder!.FullName, "assets");
    }

    [Fact]
    public void EveryJsonFileParses()
    {
        string assets = AssetsFolder();
        string[] files = Directory.GetFiles(assets, "*.json", SearchOption.AllDirectories);
        Assert.True(files.Length > 0, $"no JSON files found under {assets}");

        var problems = new List<string>();
        foreach (string file in files)
        {
            string name = Path.GetRelativePath(assets, file).Replace('\\', '/');
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
            }
            catch (JsonException error)
            {
                // JsonException counts lines from 0; editors count from 1.
                problems.Add($"assets/{name} line {error.LineNumber + 1}: {error.Message}");
            }
        }

        Assert.True(problems.Count == 0, "Content files that do not parse:\n" + string.Join("\n", problems));
    }
}
