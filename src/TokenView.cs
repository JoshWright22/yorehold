using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// One creature on the map: a disc in its colour with its face or, without a picture, its initial,
/// a ring when selected and a cross once fallen.
/// </summary>
public partial class TokenView : Node2D
{
    [Export] public Color SelectionColor { get; set; } = Palette.Straw;
    /// <summary>How much of the disc stays the token's colour around the face.</summary>
    [Export] public float Rim { get; set; } = 5;

    private const int Sides = 32;

    private Token _token = new();
    private bool _dead;
    private bool _hero;
    private Texture2D? _picture;

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
    }

    /// <summary>Called each frame with what the rules say about it now.</summary>
    public void Show(Token token, bool hero, bool dead, bool seen, Texture2D? picture = null)
    {
        _token = token;
        _hero = hero;
        _dead = dead;
        _picture = picture;
        Position = token.Position.ToGodot();
        Visible = seen;
        QueueRedraw();
    }

    public override void _Draw()
    {
        float r = _token.Radius;
        if (_dead)
        {
            float x = r * 0.7f;
            DrawCircle(Vector2.Zero, r, _hero ? Palette.Slate : Palette.Rust);
            DrawLine(new Vector2(-x, -x), new Vector2(x, x), Palette.Ink, 5);
            DrawLine(new Vector2(-x, x), new Vector2(x, -x), Palette.Ink, 5);
            return;
        }
        if (_token.Selected)
        {
            DrawArc(Vector2.Zero, r * 1.12f, 0, Mathf.Tau, 48, SelectionColor, 4);
        }
        // content colours land on the nearest palette colour like everything else on the map
        DrawCircle(Vector2.Zero, r, Palette.Ink);
        DrawCircle(Vector2.Zero, r - 2, Palette.Nearest(_token.Color.ToGodot()));
        if (_picture != null)
        {
            // the face is cut round: a many-sided shape with the picture laid across it
            float inner = r - Rim;
            var points = new Vector2[Sides];
            var uvs = new Vector2[Sides];
            for (int i = 0; i < Sides; i++)
            {
                Vector2 way = Vector2.Right.Rotated(Mathf.Tau * i / Sides);
                points[i] = way * inner;
                uvs[i] = way * 0.5f + new Vector2(0.5f, 0.5f);
            }
            DrawColoredPolygon(points, Colors.White, uvs, _picture);
            return;
        }
        if (_token.Name.Length > 0)
        {
            Font font = ThemeDB.FallbackFont;
            int size = Mathf.Max(8, (int)(r * 0.9f));
            string initial = _token.Name[..1];
            Vector2 measure = font.GetStringSize(initial, HorizontalAlignment.Left, -1, size);
            DrawString(font, new Vector2(-measure.X / 2, font.GetAscent(size) / 2 - 1), initial, HorizontalAlignment.Left, -1, size, Palette.Ink);
        }
    }
}
