using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// One creature on the map: its face (or, without a picture, its initial on its colour) with the
/// frame of its side laid over it, a ring when selected and a cross once fallen. The frames are
/// the player's skin, ui/tokens/&lt;side&gt;.png from content or an art pack; without one the side
/// shows as a plain ring in its colour.
/// </summary>
public partial class TokenView : Node2D
{
    [Export] public Color SelectionColor { get; set; } = Palette.Straw;
    [Export] public Color MineColor { get; set; } = Palette.Straw;
    [Export] public Color PartyColor { get; set; } = Palette.Sky;
    [Export] public Color AllyColor { get; set; } = Palette.Leaf;
    [Export] public Color EnemyColor { get; set; } = Palette.Red;
    [Export] public Color NeutralColor { get; set; } = Palette.Smoke;

    private const int Sides = 32;

    private Token _token = new();
    private PictureFocus _focus = PictureFocus.Middle;
    private bool _dead;
    private bool _hero;
    private Texture2D? _picture;
    private Texture2D? _frame;
    private TokenSide _side;
    private TokenSkin _skin = new();

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
    }

    /// <summary>Called each frame with what the rules say about it now.</summary>
    public void Show(Token token, bool hero, bool dead, bool seen, Texture2D? picture, TokenSide side, Texture2D? frame, TokenSkin skin, PictureFocus focus)
    {
        _focus = focus;
        _token = token;
        _hero = hero;
        _dead = dead;
        _picture = picture;
        _side = side;
        _frame = frame;
        _skin = skin;
        Position = token.Position.ToGodot();
        Visible = seen;
        QueueRedraw();
    }

    private Color SideColor => _side switch
    {
        TokenSide.Mine => MineColor,
        TokenSide.Party => PartyColor,
        TokenSide.Ally => AllyColor,
        TokenSide.Enemy => EnemyColor,
        _ => NeutralColor,
    };

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
        bool square = _skin.Square;
        if (_token.Selected)
        {
            if (square)
            {
                DrawRect(new Rect2(-r * 1.14f, -r * 1.14f, r * 2.28f, r * 2.28f), SelectionColor, false, 4);
            }
            else
            {
                DrawArc(Vector2.Zero, r * 1.12f, 0, Mathf.Tau, 48, SelectionColor, 4);
            }
        }

        // the token's own colour under the face, landing on the palette like everything else on the map
        Color fill = Palette.Nearest(_token.Color.ToGodot());
        float face = r * (1 - 2 * (float)_skin.Inset);
        if (square)
        {
            DrawRect(new Rect2(-r, -r, r * 2, r * 2), fill);
        }
        else
        {
            DrawCircle(Vector2.Zero, r, fill);
        }
        if (_picture != null)
        {
            // a square of the picture around its focus, so a wide painting isn't squashed onto the disc
            Vector2 whole = _picture.GetSize();
            (double cx, double cy, double cw, double ch) = _focus.Cut(whole.X, whole.Y, 1);
            if (square)
            {
                DrawTextureRectRegion(_picture, new Rect2(-face, -face, face * 2, face * 2), new Rect2((float)cx, (float)cy, (float)cw, (float)ch));
            }
            else
            {
                // the face is cut round: a many-sided shape with that square laid across it
                var points = new Vector2[Sides];
                var uvs = new Vector2[Sides];
                for (int i = 0; i < Sides; i++)
                {
                    Vector2 way = Vector2.Right.Rotated(Mathf.Tau * i / Sides);
                    points[i] = way * face;
                    Vector2 inSquare = way * 0.5f + new Vector2(0.5f, 0.5f);
                    uvs[i] = new Vector2((float)((cx + inSquare.X * cw) / whole.X), (float)((cy + inSquare.Y * ch) / whole.Y));
                }
                DrawColoredPolygon(points, Colors.White, uvs, _picture);
            }
        }
        else if (_token.Name.Length > 0)
        {
            Font font = ThemeDB.FallbackFont;
            int size = Mathf.Max(8, (int)(r * 0.9f));
            string initial = _token.Name[..1];
            Vector2 measure = font.GetStringSize(initial, HorizontalAlignment.Left, -1, size);
            DrawString(font, new Vector2(-measure.X / 2, font.GetAscent(size) / 2 - 1), initial, HorizontalAlignment.Left, -1, size, Palette.Ink);
        }

        if (_frame != null)
        {
            DrawTextureRect(_frame, new Rect2(-r, -r, r * 2, r * 2), false);
        }
        else if (square)
        {
            DrawRect(new Rect2(-r + 2, -r + 2, r * 2 - 4, r * 2 - 4), SideColor, false, 4);
            DrawRect(new Rect2(-r, -r, r * 2, r * 2), Palette.Ink, false, 1);
        }
        else
        {
            DrawArc(Vector2.Zero, r - 2, 0, Mathf.Tau, 48, SideColor, 4);
            DrawArc(Vector2.Zero, r, 0, Mathf.Tau, 48, Palette.Ink, 1);
        }
    }
}
