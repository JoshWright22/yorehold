namespace Yorehold.Rules;

/// <summary>
/// ui/shapes.json: how the screens' boxes are cut, read at start like the colours and fonts:
/// the radius of the corners that are rounded at all (square ones stay square), the width of the
/// thin edges, and a hard shadow's offset (0 for none). A skin's file changes only what it names.
/// </summary>
public sealed class UiShapes
{
    public const string File = "ui/shapes.json";

    public int? Corners { get; init; }
    public int? Edges { get; init; }
    public int? Shadow { get; init; }

    public static UiShapes Read(ContentNode node)
    {
        node.RequireObject("a shapes file is a JSON object");
        node.Only("format", "version", "about", "corners", "edges", "shadow");
        if (node.At("format").AsText() != "yorehold.shapes")
        {
            throw node.Fail("format", "is \"yorehold.shapes\"");
        }
        return new UiShapes
        {
            Corners = node.Has("corners") ? node.Int("corners", 0, 0, 24) : null,
            Edges = node.Has("edges") ? node.Int("edges", 1, 0, 8) : null,
            Shadow = node.Has("shadow") ? node.Int("shadow", 0, 0, 8) : null,
        };
    }
}
