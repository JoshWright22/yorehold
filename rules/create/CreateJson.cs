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
}
