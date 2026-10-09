using System.Linq;
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
    public static Color Night { get; private set; } = Color.Color8(0x0b, 0x0b, 0x0b);
    public static Color Ink { get; private set; } = Color.Color8(0x15, 0x15, 0x15);
    public static Color Dusk { get; private set; } = Color.Color8(0x1f, 0x1f, 0x1f);
    public static Color Iron { get; private set; } = Color.Color8(0x33, 0x33, 0x33);
    public static Color Slate { get; private set; } = Color.Color8(0x4d, 0x4d, 0x4d);
    public static Color Smoke { get; private set; } = Color.Color8(0x70, 0x70, 0x70);
    public static Color Ash { get; private set; } = Color.Color8(0xa0, 0xa0, 0xa0);
    public static Color Sand { get; private set; } = Color.Color8(0xc8, 0xc8, 0xc8);
    public static Color Bone { get; private set; } = Color.Color8(0xed, 0xed, 0xed);
    // gold: the main action, what is picked
    public static Color Straw { get; private set; } = Color.Color8(0xe8, 0xb3, 0x3a);
    public static Color Amber { get; private set; } = Color.Color8(0xc9, 0x9a, 0x2e);
    public static Color Leather { get; private set; } = Color.Color8(0x7a, 0x5d, 0x1c);
    public static Color Sage => Straw;
    // red: danger, enemies, damage
    public static Color Red { get; private set; } = Color.Color8(0xff, 0x5c, 0x5c);
    public static Color Rose { get; private set; } = Color.Color8(0xff, 0x7a, 0x7a);
    public static Color Rust { get; private set; } = Color.Color8(0xd9, 0x44, 0x44);
    public static Color Mauve { get; private set; } = Color.Color8(0x4a, 0x1c, 0x1c);
    public static Color Orchid => Red;
    public static Color Plum => Rust;
    public static Color Shade => Mauve;
    // blue: information, allies, healing
    public static Color Sky { get; private set; } = Color.Color8(0x6c, 0xa6, 0xff);
    public static Color Mint { get; private set; } = Color.Color8(0x8f, 0xbc, 0xff);
    public static Color Blue { get; private set; } = Color.Color8(0x4a, 0x8c, 0xf0);
    public static Color Indigo { get; private set; } = Color.Color8(0x24, 0x47, 0x7a);
    public static Color Moss { get; private set; } = Color.Color8(0x16, 0x29, 0x4a);
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

    // The first font each role's list starts with in the hand-made theme, to know which is which.
    private static readonly (string Role, string First)[] FontRoles = { ("sans", "Inter Tight"), ("book", "Arial"), ("mono", "JetBrains Mono") };

    /// <summary>
    /// Takes the screens' faces from ui/fonts.json (a skin's when one is laid on top): every system
    /// font in the shared theme of a role the file names gets that role's list.
    /// </summary>
    public static void LoadFonts(Rules.ContentFiles files)
    {
        // A headless run draws nothing, and its text server can't size some system fonts: it keeps the theme's own.
        if (!files.Exists(Rules.UiFonts.File) || DisplayServer.GetName() == "headless")
        {
            return;
        }
        Rules.UiFonts fonts;
        try
        {
            fonts = Rules.UiFonts.Read(Rules.ContentNode.Read(files, Rules.UiFonts.File));
        }
        catch (Rules.ContentException error)
        {
            GD.PushWarning($"The screens' fonts can't be read, so they are the game's own: {error.Message}");
            return;
        }
        // a role may start with a font file (the game's own, or one a skin brings); the system names after it are its fallbacks
        var brought = new System.Collections.Generic.Dictionary<string, string>();
        foreach ((string role, System.Collections.Generic.List<string> names) in fonts.Faces)
        {
            string? path = names.FirstOrDefault(Rules.UiFonts.IsFile);
            if (path == null)
            {
                continue;
            }
            if (!files.Exists(path))
            {
                GD.PushWarning($"{Rules.UiFonts.File}: {role} names {path}, which isn't there; the system fonts listed stand in");
                continue;
            }
            brought[role] = path;
        }
        // The game's own files are fonts the engine imported (res://assets/...); a skin's are read
        // from disk. Weights and slants come from the files beside the regular one.
        var loaded = new System.Collections.Generic.Dictionary<string, FontFile?>();
        FontFile? Load(string path)
        {
            if (loaded.TryGetValue(path, out FontFile? known))
            {
                return known;
            }
            FontFile? file = null;
            if (ResourceLoader.Exists("res://assets/" + path))
            {
                file = GD.Load<FontFile>("res://assets/" + path);
            }
            else if (files.FullPath(path) is string full && System.IO.File.Exists(full))
            {
                file = new FontFile();
                if (file.LoadDynamicFont(full) != Error.Ok)
                {
                    file = null;
                }
            }
            loaded[path] = file;
            return file;
        }
        Font? Face(string regular, SystemFont like)
        {
            bool italic = like.FontItalic;
            string[] endings = italic ? new[] { "-It", "-Italic" } : like.FontWeight >= 700 ? new[] { "-Bold", "-Semibold", "-SemiBold" }
                : like.FontWeight >= 600 ? new[] { "-Semibold", "-SemiBold", "-Bold" } : System.Array.Empty<string>();
            foreach (string ending in endings)
            {
                if (regular.Contains("-Regular", System.StringComparison.Ordinal) && Load(regular.Replace("-Regular", ending)) is FontFile own)
                {
                    return own;
                }
            }
            return Load(regular);
        }
        Theme theme = GD.Load<Theme>("res://scenes/hud/hud-theme.tres");
        var swapped = new System.Collections.Generic.Dictionary<SystemFont, Font>();
        // The font a role's system font becomes: the same font with the role's names, or the role's
        // file in the weight the system font asked for.
        Font? Swap(SystemFont font)
        {
            if (swapped.TryGetValue(font, out Font? done))
            {
                return done;
            }
            if (font.FontNames.Length == 0)
            {
                return null;
            }
            foreach ((string role, string first) in FontRoles)
            {
                if (font.FontNames[0] != first || !fonts.Faces.TryGetValue(role, out System.Collections.Generic.List<string>? names))
                {
                    continue;
                }
                Font result = font;
                if (brought.TryGetValue(role, out string? path) && Face(path, font) is Font file)
                {
                    result = file;
                }
                else
                {
                    font.FontNames = names.Where(n => !Rules.UiFonts.IsFile(n)).DefaultIfEmpty(first).ToArray();
                }
                swapped[font] = result;
                return result;
            }
            return null;
        }
        foreach (string type in theme.GetTypeList())
        {
            foreach (string name in theme.GetFontList(type))
            {
                if (theme.GetFont(name, type) is SystemFont font && Swap(font) is Font now && now != font)
                {
                    theme.SetFont(name, type, now);
                }
            }
        }
        if (theme.DefaultFont is SystemFont fallback && Swap(fallback) is Font newDefault)
        {
            theme.DefaultFont = newDefault;
        }
    }

    /// <summary>
    /// Cuts the shared theme's boxes as ui/shapes.json says (a skin's when one is laid on top): its
    /// radius on every corner that is rounded at all, its width on every thin edge, and a hard
    /// shadow. What the file leaves out stays as the theme has it.
    /// </summary>
    public static void LoadShapes(Rules.ContentFiles files)
    {
        if (!files.Exists(Rules.UiShapes.File))
        {
            return;
        }
        Rules.UiShapes shapes;
        try
        {
            shapes = Rules.UiShapes.Read(Rules.ContentNode.Read(files, Rules.UiShapes.File));
        }
        catch (Rules.ContentException error)
        {
            GD.PushWarning($"The screens' shapes can't be read, so they are the game's own: {error.Message}");
            return;
        }
        Theme theme = GD.Load<Theme>("res://scenes/hud/hud-theme.tres");
        var seen = new System.Collections.Generic.HashSet<StyleBoxFlat>();
        foreach (string type in theme.GetTypeList())
        {
            foreach (string name in theme.GetStyleboxList(type))
            {
                if (theme.GetStylebox(name, type) is not StyleBoxFlat box || !seen.Add(box))
                {
                    continue;
                }
                if (shapes.Corners is int radius)
                {
                    foreach (Corner corner in new[] { Corner.TopLeft, Corner.TopRight, Corner.BottomRight, Corner.BottomLeft })
                    {
                        if (box.GetCornerRadius(corner) > 0)
                        {
                            box.SetCornerRadius(corner, radius);
                        }
                    }
                }
                if (shapes.Edges is int edge)
                {
                    foreach (Side side in new[] { Side.Left, Side.Top, Side.Right, Side.Bottom })
                    {
                        // the thin lines only; a thick accent bar keeps its weight
                        if (box.GetBorderWidth(side) == 1)
                        {
                            box.SetBorderWidth(side, edge);
                        }
                    }
                }
                if (shapes.Shadow is int shadow && shadow > 0 && box.BgColor.A > 0)
                {
                    // one hard offset in the darkest grey, no blur
                    box.ShadowSize = 1;
                    box.ShadowOffset = new Vector2(shadow, shadow);
                    box.ShadowColor = new Color(Night, 1);
                }
            }
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

    /// <summary>A colour by its role in ui/colors.json ("greys.paper", "red.main") or as "#rrggbb"; white when it is neither.</summary>
    public static Color Named(string role) => role switch
    {
        "greys.bg-deep" => Night, "greys.bg" => Ink, "greys.panel" => Dusk, "greys.line" => Iron, "greys.soft" => Slate,
        "greys.faint" => Smoke, "greys.muted" => Ash, "greys.paper-2" => Sand, "greys.paper" => Bone,
        "gold.main" => Straw, "gold.mid" => Amber, "gold.dark" => Leather,
        "red.main" => Red, "red.light" => Rose, "red.dark" => Rust, "red.deep" => Mauve,
        "blue.main" => Sky, "blue.light" => Mint, "blue.mid" => Blue, "blue.dark" => Indigo, "blue.deep" => Moss,
        _ when role.StartsWith('#') && Color.HtmlIsValid(role) => Color.FromHtml(role),
        _ => Colors.White,
    };
}
