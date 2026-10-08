using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The picture of a creature's face, for its cards and its token. It is the image its token names,
/// or else portraits/&lt;creature id&gt;.png, or for a hero portraits/&lt;class&gt;.png, all from the
/// chapter's content, so the faces are the ones its creator drew.
/// </summary>
public static class Portraits
{
    /// <summary>Null when there is no picture, and the card then draws the token's disc.</summary>
    public static Texture2D? Of(World world, int creature)
    {
        Token token = world.Tokens.Tokens[creature];
        WorldCreature who = world.Creatures[creature];
        if (token.Image.Length > 0)
        {
            return PlayerArt.Texture(world.Files, token.Image);
        }
        string id = string.IsNullOrEmpty(who.CreatureId) ? who.Sheet.ClassName.ToLowerInvariant() : who.CreatureId;
        return id.Length > 0 ? PlayerArt.Texture(world.Files, $"portraits/{id}.png") : null;
    }

    /// <summary>
    /// Where that picture is cut for a token or a card: the creature file's focus, kept with the
    /// creature rather than the map token so saves and online play carry nothing new.
    /// </summary>
    public static PictureFocus FocusOf(World world, int creature)
    {
        string id = world.Creatures[creature].CreatureId;
        return string.IsNullOrEmpty(id) ? PictureFocus.Middle
            : world.Chapter.Compendium.Creature(id)?.Token.Framing ?? PictureFocus.Middle;
    }
}
