using Godot;

namespace Yorehold;

/// <summary>
/// The Apollo palette (AdamCYounis, 46 colours), the only colours the game draws with. The names
/// are roles kept from the CC-29 palette used before (Josh, 10/7: fewer yellows), so Straw, the
/// highlight, is now a cool blue. hud-theme.tres holds the same values; code that draws its own
/// panel parts picks from here so it matches the theme.
/// </summary>
public static class Palette
{
    public static readonly Color Bone = Color.Color8(0xeb, 0xed, 0xe9);
    public static readonly Color Ash = Color.Color8(0xa8, 0xb5, 0xb2);
    public static readonly Color Smoke = Color.Color8(0x81, 0x97, 0x96);
    public static readonly Color Slate = Color.Color8(0x57, 0x72, 0x77);
    public static readonly Color Iron = Color.Color8(0x39, 0x4a, 0x50);
    public static readonly Color Dusk = Color.Color8(0x1e, 0x1d, 0x39);
    public static readonly Color Ink = Color.Color8(0x15, 0x1d, 0x28);
    public static readonly Color Night = Color.Color8(0x17, 0x20, 0x38);
    public static readonly Color Indigo = Color.Color8(0x25, 0x3a, 0x5e);
    public static readonly Color Blue = Color.Color8(0x3c, 0x5e, 0x8b);
    public static readonly Color Sky = Color.Color8(0x4f, 0x8f, 0xba);
    public static readonly Color Mint = Color.Color8(0xa4, 0xdd, 0xdb);
    public static readonly Color Straw = Color.Color8(0x73, 0xbe, 0xd3);
    public static readonly Color Amber = Color.Color8(0xda, 0x86, 0x3e);
    public static readonly Color Red = Color.Color8(0xa5, 0x30, 0x30);
    public static readonly Color Plum = Color.Color8(0x7a, 0x36, 0x7b);
    public static readonly Color Shade = Color.Color8(0x40, 0x27, 0x51);
    public static readonly Color Rust = Color.Color8(0x7a, 0x48, 0x41);
    public static readonly Color Leather = Color.Color8(0xad, 0x77, 0x57);
    public static readonly Color Sand = Color.Color8(0xc7, 0xcf, 0xcc);
    public static readonly Color Lime = Color.Color8(0xa8, 0xca, 0x58);
    public static readonly Color Leaf = Color.Color8(0x75, 0xa7, 0x43);
    public static readonly Color Teal = Color.Color8(0x25, 0x56, 0x2e);
    public static readonly Color Moss = Color.Color8(0x19, 0x33, 0x2d);
    public static readonly Color Olive = Color.Color8(0x46, 0x82, 0x32);
    public static readonly Color Sage = Color.Color8(0xd0, 0xda, 0x91);
    public static readonly Color Rose = Color.Color8(0xdf, 0x84, 0xa5);
    public static readonly Color Orchid = Color.Color8(0xc6, 0x51, 0x97);
    public static readonly Color Mauve = Color.Color8(0x41, 0x1d, 0x31);

    private static readonly Color[] All =
    {
        Bone, Ash, Smoke, Slate, Iron, Dusk, Ink, Night, Indigo, Blue, Sky, Mint, Straw, Amber, Red,
        Plum, Shade, Rust, Leather, Sand, Lime, Leaf, Teal, Moss, Olive, Sage, Rose, Orchid, Mauve,
        // the rest of Apollo, which no role names yet, so content colours land near what they asked for
        Color.Color8(0x4d, 0x2b, 0x32), Color.Color8(0xc0, 0x94, 0x73), Color.Color8(0xd7, 0xb5, 0x94),
        Color.Color8(0xe7, 0xd5, 0xb3), Color.Color8(0x34, 0x1c, 0x27), Color.Color8(0x60, 0x2c, 0x2c),
        Color.Color8(0x88, 0x4b, 0x2b), Color.Color8(0xbe, 0x77, 0x2b), Color.Color8(0xde, 0x9e, 0x41),
        Color.Color8(0xe8, 0xc1, 0x70), Color.Color8(0x24, 0x15, 0x27), Color.Color8(0x75, 0x24, 0x38),
        Color.Color8(0xcf, 0x57, 0x3c), Color.Color8(0xa2, 0x3e, 0x8c), Color.Color8(0x09, 0x0a, 0x14),
        Color.Color8(0x10, 0x14, 0x1f), Color.Color8(0x20, 0x2e, 0x37),
    };

    /// <summary>The palette colour closest to any colour, for content colours drawn on a panel.</summary>
    public static Color Nearest(Color color)
    {
        Color best = Bone;
        float distance = float.MaxValue;
        foreach (Color c in All)
        {
            float d = (c.R - color.R) * (c.R - color.R) + (c.G - color.G) * (c.G - color.G) + (c.B - color.B) * (c.B - color.B);
            if (d < distance)
            {
                distance = d;
                best = c;
            }
        }
        return best;
    }

    /// <summary>A palette colour seen through: the same hue, less of it.</summary>
    public static Color Faded(Color color, float alpha) => new(color.R, color.G, color.B, alpha);

    /// <summary>The hex for bbcode, "#a53030".</summary>
    public static string Hex(Color color) => "#" + color.ToHtml(false);
}
