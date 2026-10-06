using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// A versioned file: {"format": name, "version": N, "framework": 1, "data": {...}}, the C++
/// client's envelope, so files written by either read in the other. Writes go through a temporary
/// file and keep the last one as path.bak; reads fall back to the .bak when the file is missing
/// or broken. Upgrades from older versions come with the formats that need them (P10).
/// </summary>
public sealed class SaveFormat
{
    private const int FrameworkVersion = 1;

    public SaveFormat(string name, int version)
    {
        Name = name;
        Version = version;
    }

    public string Name { get; }
    public int Version { get; }

    public string Write(JsonNode data)
    {
        var envelope = new JsonObject
        {
            ["format"] = Name,
            ["version"] = Version,
            ["framework"] = FrameworkVersion,
            ["data"] = data,
        };
        return envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>The data inside an envelope; throws with a plain message when it isn't one of ours.</summary>
    public string Read(string text)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text);
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("not JSON: " + error.Message);
        }
        if (root is not JsonObject envelope || envelope["format"]?.GetValueKind() != JsonValueKind.String
            || envelope["format"]!.GetValue<string>() != Name)
        {
            throw new InvalidDataException($"not a {Name} file");
        }
        int version = envelope["version"]?.GetValueKind() == JsonValueKind.Number ? envelope["version"]!.GetValue<int>() : 0;
        int framework = envelope["framework"]?.GetValueKind() == JsonValueKind.Number ? envelope["framework"]!.GetValue<int>() : 1;
        if (version > Version || framework > FrameworkVersion)
        {
            throw new InvalidDataException("saved by a newer version");
        }
        if (version < 1 || framework < 1)
        {
            throw new InvalidDataException("bad version");
        }
        return envelope["data"]?.ToJsonString() ?? throw new InvalidDataException("no data");
    }

    public void WriteFile(string path, JsonNode data)
    {
        string text = Write(data);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, text);
        if (File.Exists(path))
        {
            File.Copy(path, path + ".bak", true);
        }
        File.Move(temporary, path, true);
    }

    public string ReadFile(string path)
    {
        string? problem = null;
        foreach (string candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate))
            {
                continue;
            }
            try
            {
                return Read(File.ReadAllText(candidate));
            }
            catch (InvalidDataException error)
            {
                problem ??= error.Message;
            }
        }
        throw new InvalidDataException(problem ?? "missing");
    }
}
