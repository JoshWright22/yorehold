using System;
using System.Collections.Generic;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Placeholder tiles painted pixel by pixel, the same as the C++ client's, until real tile packs
/// exist. A tile type whose art isn't one of these gets its flat colour with a little grain.
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
                    image.SetPixel(t * Size + x, y, Pixel(types[t], (uint)(t + 1), x, y));
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

    private static float Random01(int x, int y, uint seed)
    {
        return (Hash(x, y, seed) & 0xFFFFFF) / (float)0x1000000;
    }

    private static Color Shade(int r, int g, int b, float f)
    {
        static int Channel(int v, float f) => (int)Math.Clamp(v * f, 0.0f, 255.0f);
        return Color.Color8((byte)Channel(r, f), (byte)Channel(g, f), (byte)Channel(b, f));
    }

    private static Color Pixel(TileType type, uint seed, int x, int y)
    {
        float grain = 0.9f + 0.2f * Random01(x / 2, y / 2, seed * 31);
        switch (type.Art)
        {
            case "grass":
                return Shade(70, 112, 58, grain * (Random01(x, y, 5) < 0.08f ? 1.25f : 1.0f));
            case "dirt":
                return Shade(120, 92, 62, grain);
            case "stone":
            {
                // flagstones 16 px, every other row shifted, dark grout
                int row = y / 16;
                int sx = (x + (row % 2) * 8) % 16;
                if (sx == 0 || y % 16 == 0)
                {
                    return Color.Color8(58, 56, 60);
                }
                float slab = 0.9f + 0.18f * Random01((x + (row % 2) * 8) / 16, row, 9);
                return Shade(122, 118, 116, grain * slab);
            }
            case "wood":
            {
                int plank = y / 8;
                if (y % 8 == 0 || x == (plank * 13) % 32)
                {
                    return Color.Color8(62, 40, 24);
                }
                return Shade(140, 96, 58, grain * (0.92f + 0.12f * Random01(0, plank, 3)));
            }
            case "wall":
            {
                int row = y / 8;
                if ((x + (row % 2) * 8) % 16 == 0 || y % 8 == 0)
                {
                    return Color.Color8(30, 28, 32);
                }
                float brick = 0.85f + 0.25f * Random01((x + (row % 2) * 8) / 16, row, 17);
                return Shade(92, 84, 86, grain * brick * (y % 8 == 1 ? 1.25f : 1.0f));
            }
            case "water":
            {
                float wave = MathF.Sin((x + y * 0.5f) * 0.6f) * 0.5f + 0.5f;
                return Shade(40, 78, 130, 0.9f + 0.25f * wave * (y % 6 == 0 ? 1.0f : 0.4f));
            }
            case "tree":
            {
                float dx = x - 15.5f, dy = y - 15.5f;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > 14.5f)
                {
                    return new Color(0, 0, 0, 0);
                }
                // lit from the top left, darker rim
                float light = 1.2f - (dx + dy) / 40.0f - d / 40.0f;
                return Shade(40, 92, 46, light * grain);
            }
            default:
                return Shade(type.Color.R, type.Color.G, type.Color.B, grain);
        }
    }
}
