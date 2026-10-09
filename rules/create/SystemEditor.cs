using System.Text.Json;
using System.Text.Json.Nodes;

namespace Yorehold.Rules;

/// <summary>
/// System mode of Create: one ruleset.json, edited a section at a time. The sections are data
/// (create/system.json), each a few of the file's keys; keys none of them names fall in Other. A
/// section is typed as JSON and taken only when the whole file still reads as a ruleset with the
/// game's own reader, so the system on the bench is always one the game can play. Every edit goes
/// on the history it was given; typing in one section undoes as a whole.
/// </summary>
public sealed class SystemEditor
{
    public const string Other = "other";

    public sealed record Section(string Id, string Name, List<string> Keys);

    private readonly History _history;
    private List<Section> _sections = new();
    private JsonObject _blank = new();
    private JsonObject _value = new();
    private string _saved = "";

    public SystemEditor(History history)
    {
        _history = history;
    }

    /// <summary>"rulesets/dnd5e/ruleset.json", inside the package.</summary>
    public string Path { get; private set; } = "";
    public IReadOnlyList<Section> Sections => _sections;
    /// <summary>The ruleset as it stands; it always reads.</summary>
    public Ruleset Rules { get; private set; } = new();
    public bool Changed => ToJson() != _saved;

    /// <summary>The sections file (create/system.json).</summary>
    public bool SetSections(string text, out string error)
    {
        error = "";
        if (Parse(text) is not JsonObject o || o["sections"] is not JsonArray list || o["blank"] is not JsonObject blank)
        {
            error = "the sections file is an object with a list of sections and a blank system";
            return false;
        }
        var sections = new List<Section>();
        foreach (JsonNode? s in list)
        {
            if (s is not JsonObject entry || !FormJson.IsString(entry["id"], out string? id) || !FormJson.IsString(entry["name"], out string? name)
                || entry["keys"] is not JsonArray keys || keys.Any(k => !FormJson.IsString(k, out _)))
            {
                error = "each section has an id, a name and a list of keys";
                return false;
            }
            if (id == Other || sections.Any(other => other.Id == id))
            {
                error = id + ": listed twice";
                return false;
            }
            sections.Add(new Section(id, name, keys.Select(k => k!.GetValue<string>()).ToList()));
        }
        _sections = sections;
        _blank = blank;
        return true;
    }

    /// <summary>A ruleset.json to edit. False (and why) when the game can't read it.</summary>
    public bool Open(string path, string text, out string error)
    {
        if (Parse(text) is not JsonObject value)
        {
            error = path + ": not a JSON object";
            return false;
        }
        if (Read(path, value, out error) is not Ruleset rules)
        {
            return false;
        }
        Path = path;
        _value = value;
        Rules = rules;
        _saved = ToJson();
        return true;
    }

    /// <summary>The section's keys as one JSON object, the way the box shows it.</summary>
    public string SectionText(string id)
    {
        var part = new JsonObject();
        foreach (string key in KeysOf(id))
        {
            if (_value[key] is JsonNode node)
            {
                part[key] = node.DeepClone();
            }
        }
        return CreateJson.Write(part);
    }

    /// <summary>The keys a section holds now: its own, or for Other every key no section names.</summary>
    public List<string> KeysOf(string id)
    {
        if (id == Other)
        {
            var named = _sections.SelectMany(s => s.Keys).ToHashSet(StringComparer.Ordinal);
            return _value.Select(p => p.Key).Where(k => !named.Contains(k)).ToList();
        }
        return _sections.Find(s => s.Id == id)?.Keys ?? new List<string>();
    }

    /// <summary>
    /// The section typed anew. Taken only if it is a JSON object of the section's keys and the
    /// whole ruleset then reads; otherwise false and why, and nothing changes.
    /// </summary>
    public bool SetSection(string id, string text, out string error)
    {
        if (Parse(text) is not JsonObject part)
        {
            error = "not a JSON object yet";
            return false;
        }
        List<string> keys = KeysOf(id);
        string? stray = part.Select(p => p.Key).FirstOrDefault(key => id == Other ? _sections.Any(s => s.Keys.Contains(key)) : !keys.Contains(key));
        if (stray != null)
        {
            error = $"{stray} belongs in {_sections.Find(s => s.Keys.Contains(stray))?.Name ?? "Other"}";
            return false;
        }
        // the keys stay where they were in the file; new ones go at the end
        var next = new JsonObject();
        foreach ((string key, JsonNode? node) in _value)
        {
            if (!keys.Contains(key))
            {
                next[key] = node?.DeepClone();
            }
            else if (part.ContainsKey(key))
            {
                next[key] = part[key]?.DeepClone();
            }
        }
        foreach ((string key, JsonNode? node) in part)
        {
            if (!next.ContainsKey(key))
            {
                next[key] = node?.DeepClone();
            }
        }
        return Replace(next, "Change " + NameOf(id), "system:" + id, out error);
    }

    /// <summary>Starts again from the blank system in the sections file: a few abilities and plain rolls. One undo step.</summary>
    public bool StartBlank(out string error) => Replace((JsonObject)_blank.DeepClone(), "Start a blank system", "", out error);

    /// <summary>Takes another system's ruleset.json whole, to change from there. One undo step.</summary>
    public bool CopyOf(string text, out string error)
    {
        if (Parse(text) is not JsonObject value)
        {
            error = "not a JSON object";
            return false;
        }
        return Replace(value, "Copy a system", "", out error);
    }

    public string NameOf(string id) => id == Other ? "Other" : _sections.Find(s => s.Id == id)?.Name ?? id;

    public string ToJson() => CreateJson.Write(_value) + "\n";

    public void MarkSaved() => _saved = ToJson();

    private bool Replace(JsonObject next, string label, string mergeKey, out string error)
    {
        if (Read(Path, next, out error) is not Ruleset rules)
        {
            return false;
        }
        JsonObject before = _value;
        Ruleset beforeRules = Rules;
        _history.Perform(label, () => { _value = next; Rules = rules; }, () => { _value = before; Rules = beforeRules; }, mergeKey);
        return true;
    }

    private static Ruleset? Read(string path, JsonObject value, out string error)
    {
        error = "";
        try
        {
            return Ruleset.Read(ContentNode.Parse(path.Length > 0 ? path : "ruleset.json", value.ToJsonString()));
        }
        catch (ContentException problem)
        {
            error = problem.Message;
            return null;
        }
    }

    private static JsonNode? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
