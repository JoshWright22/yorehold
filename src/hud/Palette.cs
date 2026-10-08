using Godot;

namespace Yorehold;

/// <summary>
/// The colours of the game's screens (Josh, 10/7: black and white with three highlights, the same
/// as the website): greys, and gold, red and blue in a few shades. ui/colors.json and the site's
/// theme.css hold the same values; hud-theme.tres too. The role names are older than the scheme:
/// Straw is the highlight (gold), Leaf and Sky are good news and allies (blue), Red is danger.
/// Content colours (a token's, a light's) still land on the Apollo palette the map's art is
/// snapped to, through Nearest.
/// </summary>
public static class Palette
{
    // greys, black to white
    public static readonly Color Night = Color.Color8(0x17, 0x17, 0x17);
    public static readonly Color Ink = Color.Color8(0x1e, 0x1e, 0x1e);
    public static readonly Color Dusk = Color.Color8(0x26, 0x26, 0x26);
    public static readonly Color Iron = Color.Color8(0x3a, 0x3a, 0x3a);
    public static readonly Color Slate = Color.Color8(0x5c, 0x5c, 0x5c);
    public static readonly Color Smoke = Color.Color8(0x7d, 0x7d, 0x7d);
    public static readonly Color Ash = Color.Color8(0xb4, 0xb4, 0xb4);
    public static readonly Color Sand = Color.Color8(0xe9, 0xe9, 0xe9);
    public static readonly Color Bone = Color.Color8(0xf7, 0xf7, 0xf7);
    // gold: the main action, what is picked
    public static readonly Color Straw = Color.Color8(0xed, 0xe1, 0x9e);
    public static readonly Color Amber = Color.Color8(0xc9, 0xb8, 0x66);
    public static readonly Color Leather = Color.Color8(0x8a, 0x7f, 0x45);
    public static readonly Color Sage = Straw;
    // red: danger, enemies, damage
    public static readonly Color Red = Color.Color8(0xe0, 0x67, 0x5e);
    public static readonly Color Rose = Color.Color8(0xe0, 0x8a, 0x82);
    public static readonly Color Rust = Color.Color8(0x8c, 0x3a, 0x34);
    public static readonly Color Mauve = Color.Color8(0x5a, 0x24, 0x20);
    public static readonly Color Orchid = Red;
    public static readonly Color Plum = Rust;
    public static readonly Color Shade = Mauve;
    // blue: information, allies, healing
    public static readonly Color Sky = Color.Color8(0x68, 0xc2, 0xd3);
    public static readonly Color Mint = Color.Color8(0xa9, 0xdd, 0xe6);
    public static readonly Color Blue = Color.Color8(0x3f, 0x8a, 0x99);
    public static readonly Color Indigo = Color.Color8(0x2a, 0x55, 0x60);
    public static readonly Color Moss = Color.Color8(0x1b, 0x34, 0x39);
    public static readonly Color Leaf = Sky;
    public static readonly Color Lime = Mint;
    public static readonly Color Olive = Blue;
    public static readonly Color Teal = Indigo;

    // Apollo (AdamCYounis, 46 colours): what the map's art and content colours are snapped to
    private static readonly Color[] All =
    {
        Color.Color8(0xeb, 0xed, 0xe9), Color.Color8(0xa8, 0xb5, 0xb2), Color.Color8(0x81, 0x97, 0x96), Color.Color8(0x57, 0x72, 0x77),
        Color.Color8(0x39, 0x4a, 0x50), Color.Color8(0x1e, 0x1d, 0x39), Color.Color8(0x15, 0x1d, 0x28), Color.Color8(0x17, 0x20, 0x38),
        Color.Color8(0x25, 0x3a, 0x5e), Color.Color8(0x3c, 0x5e, 0x8b), Color.Color8(0x4f, 0x8f, 0xba), Color.Color8(0xa4, 0xdd, 0xdb),
        Color.Color8(0x73, 0xbe, 0xd3), Color.Color8(0xda, 0x86, 0x3e), Color.Color8(0xa5, 0x30, 0x30), Color.Color8(0x7a, 0x36, 0x7b),
        Color.Color8(0x40, 0x27, 0x51), Color.Color8(0x7a, 0x48, 0x41), Color.Color8(0xad, 0x77, 0x57), Color.Color8(0xc7, 0xcf, 0xcc),
        Color.Color8(0xa8, 0xca, 0x58), Color.Color8(0x75, 0xa7, 0x43), Color.Color8(0x25, 0x56, 0x2e), Color.Color8(0x19, 0x33, 0x2d),
        Color.Color8(0x46, 0x82, 0x32), Color.Color8(0xd0, 0xda, 0x91), Color.Color8(0xdf, 0x84, 0xa5), Color.Color8(0xc6, 0x51, 0x97),
        Color.Color8(0x41, 0x1d, 0x31),
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
