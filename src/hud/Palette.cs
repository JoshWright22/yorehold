using Godot;

namespace Yorehold;

/// <summary>
/// The CC-29 palette, the only colours the panels use. hud-theme.tres holds the same values; code
/// that draws its own panel parts picks from here so it matches the theme.
/// </summary>
public static class Palette
{
    public static readonly Color Bone = Color.Color8(0xf2, 0xf0, 0xe5);
    public static readonly Color Ash = Color.Color8(0xb8, 0xb5, 0xb9);
    public static readonly Color Smoke = Color.Color8(0x86, 0x81, 0x88);
    public static readonly Color Slate = Color.Color8(0x64, 0x63, 0x65);
    public static readonly Color Iron = Color.Color8(0x45, 0x44, 0x4f);
    public static readonly Color Dusk = Color.Color8(0x3a, 0x38, 0x58);
    public static readonly Color Ink = Color.Color8(0x21, 0x21, 0x23);
    public static readonly Color Night = Color.Color8(0x35, 0x2b, 0x42);
    public static readonly Color Indigo = Color.Color8(0x43, 0x43, 0x6a);
    public static readonly Color Blue = Color.Color8(0x4b, 0x80, 0xca);
    public static readonly Color Sky = Color.Color8(0x68, 0xc2, 0xd3);
    public static readonly Color Mint = Color.Color8(0xa2, 0xdc, 0xc7);
    public static readonly Color Straw = Color.Color8(0xed, 0xe1, 0x9e);
    public static readonly Color Amber = Color.Color8(0xd3, 0xa0, 0x68);
    public static readonly Color Red = Color.Color8(0xb4, 0x52, 0x52);
    public static readonly Color Plum = Color.Color8(0x6a, 0x53, 0x6e);
    public static readonly Color Shade = Color.Color8(0x4b, 0x41, 0x58);
    public static readonly Color Rust = Color.Color8(0x80, 0x49, 0x3a);
    public static readonly Color Leather = Color.Color8(0xa7, 0x7b, 0x5b);
    public static readonly Color Sand = Color.Color8(0xe5, 0xce, 0xb4);
    public static readonly Color Lime = Color.Color8(0xc2, 0xd3, 0x68);
    public static readonly Color Leaf = Color.Color8(0x8a, 0xb0, 0x60);
    public static readonly Color Teal = Color.Color8(0x56, 0x7b, 0x79);
    public static readonly Color Moss = Color.Color8(0x4e, 0x58, 0x4a);
    public static readonly Color Olive = Color.Color8(0x7b, 0x72, 0x43);
    public static readonly Color Sage = Color.Color8(0xb2, 0xb4, 0x7e);
    public static readonly Color Rose = Color.Color8(0xed, 0xc8, 0xc4);
    public static readonly Color Orchid = Color.Color8(0xcf, 0x8a, 0xcb);
    public static readonly Color Mauve = Color.Color8(0x5f, 0x55, 0x6a);

    /// <summary>A palette colour seen through: the same hue, less of it.</summary>
    public static Color Faded(Color color, float alpha) => new(color.R, color.G, color.B, alpha);

    /// <summary>The hex for bbcode, "#b45252".</summary>
    public static string Hex(Color color) => "#" + color.ToHtml(false);
}
