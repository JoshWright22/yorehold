namespace Yorehold.Rules;

/// <summary>Whose a token is. Each side wears its own frame, ui/tokens/&lt;side&gt;.png in lower case.</summary>
public enum TokenSide
{
    Mine,
    Party,
    Ally,
    Enemy,
    Neutral,
}

/// <summary>
/// How a skin's token frames sit on the faces, from ui/tokens/token.json. A round frame wants the
/// face cut round, a square one wants it square; inset is how far in from the frame's edge the face
/// starts, as a share of its width, so the frame's border covers the face's edge and not more.
/// </summary>
public sealed class TokenSkin
{
    public const string File = "ui/tokens/token.json";

    public bool Square { get; init; }
    public double Inset { get; init; } = 0.1;

    public static string FramePath(TokenSide side) => $"ui/tokens/{side.ToString().ToLowerInvariant()}.png";

    /// <summary>The skin's settings, or the defaults (round, a tenth in) when it has none.</summary>
    public static TokenSkin Load(ContentFiles files)
    {
        return files.Exists(File) ? Read(ContentNode.Read(files, File)) : new TokenSkin();
    }

    public static TokenSkin Read(ContentNode node)
    {
        string shape = node.Text("shape", "round");
        if (shape is not ("round" or "square"))
        {
            throw node.Fail("shape", "is round or square");
        }
        return new TokenSkin { Square = shape == "square", Inset = node.Number("inset", 0.1, 0, 0.45) };
    }
}
