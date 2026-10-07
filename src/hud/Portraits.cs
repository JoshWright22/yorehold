using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The picture of a creature's face, for its cards and its token. It is the image its token names,
/// or else portraits/&lt;creature id&gt;.png, or for a hero portraits/&lt;class&gt;.png. Read from the content
/// folder as a plain file, like the rest of the content, and kept once read.
/// </summary>
public static class Portraits
{
    private static readonly Dictionary<string, Texture2D?> Loaded = new();

    /// <summary>Null when there is no picture, and the card then draws the token's disc.</summary>
    public static Texture2D? Of(World world, int creature)
    {
        Token token = world.Tokens.Tokens[creature];
        WorldCreature who = world.Creatures[creature];
        if (token.Image.Length > 0)
        {
            return Load(token.Image);
        }
        string id = string.IsNullOrEmpty(who.CreatureId) ? who.Sheet.ClassName.ToLowerInvariant() : who.CreatureId;
        return id.Length > 0 ? Load($"portraits/{id}.png") : null;
    }

    private static Texture2D? Load(string path)
    {
        if (Loaded.TryGetValue(path, out Texture2D? known))
        {
            return known;
        }
        Texture2D? texture = null;
        string file = Places.ContentFile("res://assets/" + path);
        if (System.IO.File.Exists(file))
        {
            Image image = new();
            if (image.Load(file) == Error.Ok)
            {
                texture = ImageTexture.CreateFromImage(image);
            }
            else
            {
                GD.PushWarning($"{path}: not a picture the game can read");
            }
        }
        Loaded[path] = texture;
        return texture;
    }
}
