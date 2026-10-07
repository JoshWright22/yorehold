namespace Yorehold.Rules;

/// <summary>
/// assets/import/schemas.json: the JSON schema of each outline kind's data, as text, for a story
/// model request to ask for exactly that shape.
/// </summary>
public sealed class OutlineSchemas
{
    public const string File = "import/schemas.json";

    private readonly Dictionary<OutlineKind, string> _schemas = new();

    public string Of(OutlineKind kind) => _schemas[kind];

    public static OutlineSchemas Load(ContentFiles files) => Read(ContentNode.Read(files, File));

    public static OutlineSchemas Read(ContentNode node)
    {
        node.RequireObject("a schemas file is a JSON object");
        if (node.At("format").AsText() != "yorehold.import-schemas")
        {
            throw node.Fail("format", "is \"yorehold.import-schemas\"");
        }
        node.At("version").AsInt(1, 1);
        ContentNode kinds = node.At("kinds").RequireObject("kinds is an object");
        var schemas = new OutlineSchemas();
        foreach (OutlineKind kind in Enum.GetValues<OutlineKind>())
        {
            ContentNode schema = kinds.At(Outline.KindName(kind)).RequireObject("a schema is an object");
            if (schema.Text("type", "") != "object")
            {
                throw schema.Fail("type", "is \"object\": every kind's data is an object");
            }
            schemas._schemas[kind] = schema.Raw();
        }
        foreach (KeyValuePair<string, ContentNode> member in kinds.Members())
        {
            if (!Enum.GetValues<OutlineKind>().Any(k => Outline.KindName(k) == member.Key))
            {
                throw kinds.Fail(member.Key, "is not an outline kind");
            }
        }
        return schemas;
    }
}
