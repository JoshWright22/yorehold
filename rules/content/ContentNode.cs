using System.Text.Json;

namespace Yorehold.Rules;

/// <summary>
/// One value in a content file, together with the file and the field path it was found at, so
/// every reader can say exactly where a problem is ("levels[4].slots.3").
/// </summary>
public readonly struct ContentNode
{
    public JsonElement Element { get; }
    public string File { get; }
    public string Path { get; }

    public ContentNode(JsonElement element, string file, string path)
    {
        Element = element;
        File = file;
        Path = path;
    }

    /// <summary>Parses a whole file. Strict JSON: no comments, no trailing commas.</summary>
    public static ContentNode Parse(string file, string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return new ContentNode(document.RootElement.Clone(), file, "");
        }
        catch (JsonException error)
        {
            // JsonException counts lines from 0; editors count from 1.
            throw new ContentException(file, "", $"line {error.LineNumber + 1}: not valid JSON ({error.Message})");
        }
    }

    public static ContentNode Read(ContentFiles files, string path)
    {
        return Parse(path, files.ReadText(path));
    }

    public bool IsObject => Element.ValueKind == JsonValueKind.Object;
    public bool IsArray => Element.ValueKind == JsonValueKind.Array;
    public bool IsString => Element.ValueKind == JsonValueKind.String;
    public bool IsNumber => Element.ValueKind == JsonValueKind.Number;
    public bool IsBool => Element.ValueKind is JsonValueKind.True or JsonValueKind.False;
    public bool IsNull => Element.ValueKind == JsonValueKind.Null;
    public bool IsWhole => IsNumber && Element.TryGetInt64(out _);
    public int Count => IsArray ? Element.GetArrayLength() : 0;

    public ContentException Fail(string problem)
    {
        return new ContentException(File, Path, problem);
    }

    public ContentException Fail(string key, string problem)
    {
        return new ContentException(File, Join(key), problem);
    }

    /// <summary>The same value seen from another file or path, for parts read through a shared reader.</summary>
    public ContentNode Moved(string path)
    {
        return new ContentNode(Element, File, path);
    }

    public ContentNode RequireObject(string what)
    {
        if (!IsObject)
        {
            throw Fail(what);
        }
        return this;
    }

    public bool Has(string key)
    {
        return IsObject && Element.TryGetProperty(key, out _);
    }

    public ContentNode? Get(string key)
    {
        if (IsObject && Element.TryGetProperty(key, out JsonElement child))
        {
            return new ContentNode(child, File, Join(key));
        }
        return null;
    }

    public ContentNode At(string key)
    {
        return Get(key) ?? throw Fail(key, "is needed");
    }

    public IEnumerable<ContentNode> Items()
    {
        if (!IsArray)
        {
            throw Fail("is a list");
        }
        var items = new List<ContentNode>();
        int index = 0;
        foreach (JsonElement child in Element.EnumerateArray())
        {
            items.Add(new ContentNode(child, File, $"{Path}[{index++}]"));
        }
        return items;
    }

    public IEnumerable<KeyValuePair<string, ContentNode>> Members()
    {
        if (!IsObject)
        {
            throw Fail("is an object");
        }
        var members = new List<KeyValuePair<string, ContentNode>>();
        foreach (JsonProperty child in Element.EnumerateObject())
        {
            members.Add(new KeyValuePair<string, ContentNode>(child.Name, new ContentNode(child.Value, File, Join(child.Name))));
        }
        return members;
    }

    /// <summary>For the strict kinds: any field not listed is a slip of the pen.</summary>
    public void Only(params string[] known)
    {
        foreach (KeyValuePair<string, ContentNode> member in Members())
        {
            if (Array.IndexOf(known, member.Key) < 0)
            {
                throw member.Value.Fail("unknown field");
            }
        }
    }

    public string AsText(int longest = 100000)
    {
        if (!IsString || Element.GetString()!.Length > longest)
        {
            throw Fail(longest >= 100000 ? "is text" : $"is text of up to {longest} characters");
        }
        // GetString only returns null for a JSON null, which IsString has ruled out.
        return Element.GetString()!;
    }

    /// <summary>A name: text that isn't empty and isn't longer than 64 characters.</summary>
    public string AsName()
    {
        if (!IsString || Element.GetString()!.Length == 0 || Element.GetString()!.Length > 64)
        {
            throw Fail("is a name of 1 to 64 characters");
        }
        return Element.GetString()!;
    }

    public string AsId()
    {
        if (!IsString || !ContentIds.IsId(Element.GetString()!))
        {
            throw Fail("uses a-z, 0-9, - and _");
        }
        return Element.GetString()!;
    }

    public int AsInt(int low = int.MinValue, int high = int.MaxValue)
    {
        if (!IsNumber || !Element.TryGetInt64(out long value) || value < low || value > high)
        {
            throw Fail(low == int.MinValue && high == int.MaxValue ? "is a whole number" : $"is a whole number from {low} to {high}");
        }
        return (int)value;
    }

    public double AsNumber(double low = double.NegativeInfinity, double high = double.PositiveInfinity)
    {
        if (!IsNumber || !Element.TryGetDouble(out double value) || !double.IsFinite(value) || value < low || value > high)
        {
            throw Fail(double.IsInfinity(low) && double.IsInfinity(high) ? "is a number" : $"is a number from {Show(low)} to {Show(high)}");
        }
        return value;
    }

    public bool AsBool()
    {
        if (!IsBool)
        {
            throw Fail("is true or false");
        }
        return Element.GetBoolean();
    }

    public string Text(string key, string fallback, int longest = 100000)
    {
        return Get(key)?.AsText(longest) ?? fallback;
    }

    public string Name(string key, string fallback)
    {
        return Get(key)?.AsName() ?? fallback;
    }

    public int Int(string key, int fallback, int low = int.MinValue, int high = int.MaxValue)
    {
        return Get(key)?.AsInt(low, high) ?? fallback;
    }

    public double Number(string key, double fallback, double low = double.NegativeInfinity, double high = double.PositiveInfinity)
    {
        return Get(key)?.AsNumber(low, high) ?? fallback;
    }

    public bool Bool(string key, bool fallback)
    {
        return Get(key)?.AsBool() ?? fallback;
    }

    /// <summary>An optional list of text values.</summary>
    public List<string> Texts(string key)
    {
        var list = new List<string>();
        ContentNode? found = Get(key);
        if (found == null)
        {
            return list;
        }
        foreach (ContentNode item in found.Value.Items())
        {
            list.Add(item.AsText());
        }
        return list;
    }

    /// <summary>An optional list of names, 1 to 64 characters each.</summary>
    public List<string> Names(string key)
    {
        var list = new List<string>();
        ContentNode? found = Get(key);
        if (found == null)
        {
            return list;
        }
        foreach (ContentNode item in found.Value.Items())
        {
            list.Add(item.AsName());
        }
        return list;
    }

    /// <summary>An optional list of ids (a-z, 0-9, - and _).</summary>
    public List<string> Ids(string key)
    {
        var list = new List<string>();
        ContentNode? found = Get(key);
        if (found == null)
        {
            return list;
        }
        foreach (ContentNode item in found.Value.Items())
        {
            list.Add(item.AsId());
        }
        return list;
    }

    /// <summary>An optional list of story flags: 1 to 64 characters each.</summary>
    public List<string> Flags(string key)
    {
        ContentNode? found = Get(key);
        if (found == null)
        {
            return new List<string>();
        }
        if (!found.Value.IsArray)
        {
            throw found.Value.Fail("is a list of story flags");
        }
        var list = new List<string>();
        foreach (ContentNode item in found.Value.Items())
        {
            if (!item.IsString || item.AsText().Length == 0 || item.AsText().Length > 64)
            {
                throw item.Fail("story flags are 1 to 64 characters");
            }
            list.Add(item.AsText());
        }
        return list;
    }

    /// <summary>The raw JSON of this value, for parts kept as written (an effect on a trap, model settings).</summary>
    public string Raw()
    {
        return Element.GetRawText();
    }

    private string Join(string key)
    {
        return Path.Length == 0 ? key : Path + "." + key;
    }

    private static string Show(double value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
