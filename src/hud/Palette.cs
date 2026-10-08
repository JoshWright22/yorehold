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
    public static Color Night { get; private set; } = Color.Color8(0x17, 0x17, 0x17);
    public static Color Ink { get; private set; } = Color.Color8(0x1e, 0x1e, 0x1e);
    public static Color Dusk { get; private set; } = Color.Color8(0x26, 0x26, 0x26);
    public static Color Iron { get; private set; } = Color.Color8(0x3a, 0x3a, 0x3a);
    public static Color Slate { get; private set; } = Color.Color8(0x5c, 0x5c, 0x5c);
    public static Color Smoke { get; private set; } = Color.Color8(0x7d, 0x7d, 0x7d);
    public static Color Ash { get; private set; } = Color.Color8(0xb4, 0xb4, 0xb4);
    public static Color Sand { get; private set; } = Color.Color8(0xe9, 0xe9, 0xe9);
    public static Color Bone { get; private set; } = Color.Color8(0xf7, 0xf7, 0xf7);
    // gold: the main action, what is picked
    public static Color Straw { get; private set; } = Color.Color8(0xed, 0xe1, 0x9e);
    public static Color Amber { get; private set; } = Color.Color8(0xc9, 0xb8, 0x66);
    public static Color Leather { get; private set; } = Color.Color8(0x8a, 0x7f, 0x45);
    public static Color Sage => Straw;
    // red: danger, enemies, damage
    public static Color Red { get; private set; } = Color.Color8(0xe0, 0x67, 0x5e);
    public static Color Rose { get; private set; } = Color.Color8(0xe0, 0x8a, 0x82);
    public static Color Rust { get; private set; } = Color.Color8(0x8c, 0x3a, 0x34);
    public static Color Mauve { get; private set; } = Color.Color8(0x5a, 0x24, 0x20);
    public static Color Orchid => Red;
    public static Color Plum => Rust;
    public static Color Shade => Mauve;
    // blue: information, allies, healing
    public static Color Sky { get; private set; } = Color.Color8(0x68, 0xc2, 0xd3);
    public static Color Mint { get; private set; } = Color.Color8(0xa9, 0xdd, 0xe6);
    public static Color Blue { get; private set; } = Color.Color8(0x3f, 0x8a, 0x99);
    public static Color Indigo { get; private set; } = Color.Color8(0x2a, 0x55, 0x60);
    public static Color Moss { get; private set; } = Color.Color8(0x1b, 0x34, 0x39);
    public static Color Leaf => Sky;
    public static Color Lime => Mint;
    public static Color Olive => Blue;
    public static Color Teal => Indigo;

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

    /// <summary>
    /// Takes the screens' colours from ui/colors.json (a skin's when one is laid on top): each role
    /// it names replaces the game's own; one it leaves out stays. Call before anything is drawn.
    /// </summary>
    public static void Load(Rules.ContentFiles files)
    {
        // what each role was before, so the hand-made theme's copies of them can follow
        Color[] before = Roles();
        Rules.UiColors colors;
        try
        {
            colors = Rules.UiColors.Read(Rules.ContentNode.Read(files, Rules.UiColors.File));
        }
        catch (Rules.ContentException error)
        {
            GD.PushWarning($"The screens' colours can't be read, so they are the game's own: {error.Message}");
            return;
        }
        Color Role(string role, Color own) =>
            colors.Roles.TryGetValue(role, out (byte R, byte G, byte B) c) ? Color.Color8(c.R, c.G, c.B) : own;
        Night = Role("greys.bg-deep", Night);
        Ink = Role("greys.bg", Ink);
        Dusk = Role("greys.panel", Dusk);
        Iron = Role("greys.line", Iron);
        Slate = Role("greys.soft", Slate);
        Smoke = Role("greys.faint", Smoke);
        Ash = Role("greys.muted", Ash);
        Sand = Role("greys.paper-2", Sand);
        Bone = Role("greys.paper", Bone);
        Straw = Role("gold.main", Straw);
        Amber = Role("gold.mid", Amber);
        Leather = Role("gold.dark", Leather);
        Red = Role("red.main", Red);
        Rose = Role("red.light", Rose);
        Rust = Role("red.dark", Rust);
        Mauve = Role("red.deep", Mauve);
        Sky = Role("blue.main", Sky);
        Mint = Role("blue.light", Mint);
        Blue = Role("blue.mid", Blue);
        Indigo = Role("blue.dark", Indigo);
        Moss = Role("blue.deep", Moss);
        Color[] after = Roles();
        var changed = new System.Collections.Generic.Dictionary<Color, Color>();
        for (int i = 0; i < before.Length; i++)
        {
            if (before[i] != after[i])
            {
                changed.TryAdd(before[i], after[i]);
            }
        }
        if (changed.Count > 0)
        {
            Recolor(GD.Load<Theme>("res://scenes/hud/hud-theme.tres"), changed);
        }
    }

    // Every role, in one order, to see which a scheme changed.
    private static Color[] Roles() => new[]
    {
        Night, Ink, Dusk, Iron, Slate, Smoke, Ash, Sand, Bone, Straw, Amber, Leather, Red, Rose, Rust, Mauve, Sky, Mint, Blue, Indigo, Moss,
    };

    // The theme the scenes share holds the same colours as the code: each one a scheme changed
    // follows it, in its boxes' fills and edges and its text colours. Alpha stays the theme's.
    private static void Recolor(Theme theme, System.Collections.Generic.Dictionary<Color, Color> changed)
    {
        Color Swap(Color c)
        {
            var opaque = new Color(c.R, c.G, c.B);
            return changed.TryGetValue(opaque, out Color to) ? new Color(to.R, to.G, to.B, c.A) : c;
        }
        foreach (string type in theme.GetTypeList())
        {
            foreach (string name in theme.GetStyleboxList(type))
            {
                if (theme.GetStylebox(name, type) is StyleBoxFlat box)
                {
                    box.BgColor = Swap(box.BgColor);
                    box.BorderColor = Swap(box.BorderColor);
                }
            }
            foreach (string name in theme.GetColorList(type))
            {
                theme.SetColor(name, type, Swap(theme.GetColor(name, type)));
            }
        }
    }

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
