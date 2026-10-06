using System;
using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Placeholder tiles, flat CC-29 fills with a few marks each (seams, tufts, ripples) so a floor
/// doesn't read as one slab, until real tile packs exist. A tile type whose art isn't one of these
/// gets the palette colour nearest its own, flat.
/// </summary>
public static class TileArt
{
    public const int Size = 32;

    /// <summary>One tile per type in a row, in the map's type order.</summary>
    public static ImageTexture Atlas(IReadOnlyList<TileType> types)
    {
        Image image = Image.CreateEmpty(Size * Math.Max(1, types.Count), Size, false, Image.Format.Rgba8);
        for (int t = 0; t < types.Count; t++)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    image.SetPixel(t * Size + x, y, Pixel(types[t], x, y));
                }
            }
        }
        return ImageTexture.CreateFromImage(image);
    }

    private static uint Hash(int x, int y, uint seed)
    {
        uint h = (uint)x * 374761393u + (uint)y * 668265263u + seed * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }

    // true for about one cell in n of a coarse grid, so marks are few and spread out
    private static bool Sparse(int x, int y, int step, int n, uint seed) => Hash(x / step, y / step, seed) % (uint)n == 0;

    private static Color Pixel(TileType type, int x, int y)
    {
        switch (type.Art)
        {
            case "grass":
            {
                // short upright blades on a flat field, two pixels tall, every few cells
                if (Sparse(x, y + 1, 4, 9, 3) && x % 4 == 1 && y % 4 < 2)
                {
                    return Palette.Moss;
                }
                if (Sparse(x, y, 4, 13, 11) && x % 4 == 2 && y % 4 == 2)
                {
                    return Palette.Lime;
                }
                return Palette.Leaf;
            }
            case "dirt":
            {
                if (Sparse(x, y, 3, 9, 5) && x % 3 == 1 && y % 3 == 1)
                {
                    return Palette.Leather;
                }
                if (Sparse(x, y, 4, 11, 7) && x % 4 == 0 && y % 4 == 3)
                {
                    return Palette.Night;
                }
                return Palette.Rust;
            }
            case "stone":
            {
                // flagstones 16 px, every other row shifted, a dark joint and a lit top edge
                int row = y / 16;
                int sx = (x + (row % 2) * 8) % 16;
                if (sx == 0 || y % 16 == 0)
                {
                    return Palette.Iron;
                }
                if (y % 16 == 1)
                {
                    return Palette.Smoke;
                }
                int slab = (x + (row % 2) * 8) / 16;
                return Hash(slab, row, 9) % 3 == 0 ? Palette.Mauve : Palette.Slate;
            }
            case "wood":
            {
                // planks 8 px tall, butt joints at a different place on each
                int plank = y / 8;
                if (y % 8 == 0 || x == (plank * 13) % 32)
                {
                    return Palette.Rust;
                }
                if (y % 8 == 4 && Sparse(x, plank, 6, 3, 13))
                {
                    return Palette.Amber;
                }
                return Palette.Leather;
            }
            case "wall":
            {
                int row = y / 8;
                int bx = (x + (row % 2) * 8) % 16;
                if (bx == 0 || y % 8 == 0)
                {
                    return Palette.Ink;
                }
                if (y % 8 == 1)
                {
                    return Palette.Slate;
                }
                int brick = (x + (row % 2) * 8) / 16;
                return Hash(brick, row, 17) % 4 == 0 ? Palette.Shade : Palette.Iron;
            }
            case "water":
            {
                // ripples: short dashes on every sixth row, moved along each time
                int row = y / 6;
                if (y % 6 == 0 && (x + row * 5) % 12 < 4)
                {
                    return Palette.Sky;
                }
                return Palette.Blue;
            }
            case "tree":
            {
                // a crown in three flat rings, lit from the top left
                float dx = x - 15.5f, dy = y - 15.5f;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > 14.5f)
                {
                    return new Color(0, 0, 0, 0);
                }
                if (d > 12.5f)
                {
                    return Palette.Moss;
                }
                float lx = dx + 5, ly = dy + 5;
                if (lx * lx + ly * ly < 30)
                {
                    return Palette.Leaf;
                }
                return Palette.Teal;
            }
            default:
                return Palette.Nearest(type.Color.ToGodot());
        }
    }
}
