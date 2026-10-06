using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Yorehold.Rules;

/// <summary>How Create writes JSON: two-space indents like the hand-written files, and words in any language left as they are typed.</summary>
public static class CreateJson
{
    // Shared options are made read-only on first use, which needs a resolver set beforehand.
    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static readonly JsonSerializerOptions OneLine = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static string Write(JsonNode node) => node.ToJsonString(Indented);

    /// <summary>A JSON value as one line, the form undo steps keep "ai" entries in.</summary>
    public static string Compact(JsonNode? node) => node == null ? "null" : node.ToJsonString(OneLine);

    public static JsonArray Texts(IEnumerable<string> texts) => new(texts.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());

    /// <summary>The fields of an entry that aren't in known, as a JSON object; empty if there are none.</summary>
    public static string ExtraOf(ContentNode entry, string[] known)
    {
        var rest = new JsonObject();
        foreach (KeyValuePair<string, ContentNode> member in entry.Members())
        {
            if (Array.IndexOf(known, member.Key) < 0)
            {
                rest[member.Key] = JsonNode.Parse(member.Value.Raw());
            }
        }
        return rest.Count == 0 ? "" : Compact(rest);
    }

    /// <summary>Puts back what ExtraOf kept.</summary>
    public static void AddExtra(JsonObject entry, string extra)
    {
        if (extra.Length == 0)
        {
            return;
        }
        // only ExtraOf writes these, so it is an object
        foreach (KeyValuePair<string, JsonNode?> field in JsonNode.Parse(extra)!.AsObject())
        {
            entry[field.Key] = field.Value?.DeepClone();
        }
    }
}
