using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// The pieces the game-facing screens share (title, pause, lobby, creation): big flat buttons
/// with their key on the right, a large heading, and the logo. Sizes are ui/screens.json; colours
/// are the palette's. Flat fills and 1 px lines, like everything else.
/// </summary>
public static class GameScreen
{
    private static ScreenSizes? _sizes;

    public static ScreenSizes Sizes => _sizes ??= ScreenSizes.Load(App.Content());

    private static StyleBoxFlat Box(Color fill, Color line)
    {
        var box = new StyleBoxFlat { BgColor = fill, BorderColor = line, ContentMarginLeft = 16, ContentMarginRight = 16 };
        box.SetBorderWidthAll(1);
        box.SetCornerRadiusAll(0);
        return box;
    }

    /// <summary>A big button: its name on the left, its key (or a fact) dim on the right.</summary>
    public static Button BigButton(string text, string right = "")
    {
        var button = new Button
        {
            Text = text,
            ToggleMode = true,
            Alignment = HorizontalAlignment.Left,
            CustomMinimumSize = new Vector2(0, Sizes.ButtonHeight),
            FocusMode = Control.FocusModeEnum.None,
        };
        button.AddThemeFontSizeOverride("font_size", Sizes.ButtonFont);
        button.AddThemeStyleboxOverride("normal", Box(Palette.Ink, Palette.Iron));
        button.AddThemeStyleboxOverride("hover", Box(Palette.Indigo, Palette.Slate));
        button.AddThemeStyleboxOverride("pressed", Box(Palette.Indigo, Palette.Straw));
        button.AddThemeStyleboxOverride("hover_pressed", Box(Palette.Indigo, Palette.Straw));
        button.AddThemeStyleboxOverride("disabled", Box(Palette.Ink, Palette.Iron));
        button.AddThemeColorOverride("font_color", Palette.Bone);
        button.AddThemeColorOverride("font_hover_color", Palette.Bone);
        button.AddThemeColorOverride("font_pressed_color", Palette.Straw);
        button.AddThemeColorOverride("font_hover_pressed_color", Palette.Straw);
        var fact = new Label
        {
            Text = right,
            ThemeTypeVariation = "DimLabel",
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        fact.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        fact.OffsetLeft = Sizes.BandWidth * 0.45f;
        fact.OffsetRight = -14;
        button.AddChild(fact);
        return button;
    }

    /// <summary>The fact or key shown on a big button's right.</summary>
    public static void SetRight(Button button, string right) => button.GetChild<Label>(0).Text = right;

    /// <summary>Greyed: its name in slate.</summary>
    public static void SetEnabled(Button button, bool enabled)
    {
        button.AddThemeColorOverride("font_color", enabled ? Palette.Bone : Palette.Slate);
        button.AddThemeColorOverride("font_hover_color", enabled ? Palette.Bone : Palette.Slate);
    }

    /// <summary>The content's logo picture (ui/logo.png), or null: the name is then set large instead.</summary>
    public static Texture2D? Logo() => PlayerArt.Texture(App.Content(), "ui/logo.png");
}
