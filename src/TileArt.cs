using System;
using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The tiles of a map in one texture. A tile type shows the picture its creator gave it, else
/// tiles/&lt;kind&gt;.png from the content or an art pack; without one it is a plain palette fill, its own colour or, if it gives none, the usual colour of its kind
/// of ground. The game draws no tile pictures of its own.
/// </summary>
public static class TileArt
{
    /// <summary>The smallest a tile is in the atlas; a bigger picture makes every slot that big.</summary>
    public const int MinSize = 32;

    // the colour a kind of ground gets when its tile type names none
    private static readonly Dictionary<string, Color> Ground = new()
    {
        ["grass"] = Palette.Leaf,
        ["dirt"] = Palette.Rust,
        ["stone"] = Palette.Slate,
        ["wood"] = Palette.Leather,
        ["wall"] = Palette.Iron,
        ["water"] = Palette.Blue,
        ["tree"] = Palette.Teal,
    };

    /// <summary>One tile per type in a row, in the map's type order, Size pixels each.</summary>
    public static (ImageTexture Texture, int Size) Atlas(IReadOnlyList<TileType> types, ContentFiles? files)
    {
        var pictures = new Image?[types.Count];
        int size = MinSize;
        for (int t = 0; t < types.Count; t++)
        {
            // a type with no picture of its own takes an art pack's picture for its kind of ground
            string image = types[t].Image.Length > 0 || types[t].Art.Length == 0 ? types[t].Image : $"tiles/{types[t].Art}.png";
            pictures[t] = PlayerArt.Picture(files, image);
            if (pictures[t] is Image picture)
            {
                size = Math.Max(size, Math.Max(picture.GetWidth(), picture.GetHeight()));
            }
        }
        size = Math.Min(size, 256); // a slot per type in one row has to fit in a texture
        Image atlas = Image.CreateEmpty(size * Math.Max(1, types.Count), size, false, Image.Format.Rgba8);
        for (int t = 0; t < types.Count; t++)
        {
            if (pictures[t] is Image picture)
            {
                Image copy = (Image)picture.Duplicate();
                copy.Convert(Image.Format.Rgba8);
                copy.Resize(size, size, Image.Interpolation.Nearest);
                atlas.BlitRect(copy, new Rect2I(0, 0, size, size), new Vector2I(t * size, 0));
            }
            else
            {
                atlas.FillRect(new Rect2I(t * size, 0, size, size), Fill(types[t]));
            }
        }
        return (ImageTexture.CreateFromImage(atlas), size);
    }

    private static Color Fill(TileType type)
    {
        bool noColour = type.Color == new ContentColor(128, 128, 128);
        if (noColour && Ground.TryGetValue(type.Art, out Color ground))
        {
            return ground;
        }
        return Palette.Nearest(type.Color.ToGodot());
    }
}
