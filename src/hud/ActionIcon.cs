using Godot;

namespace Yorehold;

/// <summary>
/// An action's picture on the hotbar, drawn from plain shapes until there is icon art. Which shape
/// an action gets is in assets/ui/action-icons.json; one it doesn't name gets its first letter.
/// </summary>
public partial class ActionIcon : Control
{
    [Export] public Color Ink { get; set; } = Palette.Sand;
    /// <summary>The fill inside a shape, a darker palette colour rather than the ink seen through.</summary>
    [Export] public Color Soft { get; set; } = Palette.Rust;

    /// <summary>Greyed: drawn in slate and iron instead of its own colours.</summary>
    public void Grey(bool grey)
    {
        Color ink = grey ? Palette.Slate : Palette.Sand, soft = grey ? Palette.Iron : Palette.Rust;
        if (ink != Ink || soft != Soft)
        {
            Ink = ink;
            Soft = soft;
            QueueRedraw();
        }
    }

    private string _shape = "";
    private string _letter = "";

    public void Show(string shape, string letter)
    {
        if (shape == _shape && letter == _letter)
        {
            return;
        }
        _shape = shape;
        _letter = letter;
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized)
        {
            QueueRedraw();
        }
    }

    private Vector2 P(float x, float y) => new(x * Size.X, y * Size.Y);

    private void Stroke(float width, params Vector2[] points)
    {
        DrawPolyline(points, Ink, width);
    }

    public override void _Draw()
    {
        float w = Mathf.Max(2, Size.X * 0.09f);
        Color soft = Soft;
        switch (_shape)
        {
            case "sword":
                Stroke(w * 1.3f, P(0.3f, 0.7f), P(0.84f, 0.16f));
                Stroke(w, P(0.2f, 0.52f), P(0.48f, 0.8f));
                Stroke(w, P(0.3f, 0.7f), P(0.16f, 0.84f));
                break;
            case "dash":
                for (int i = 0; i < 3; i++)
                {
                    float x = 0.14f + i * 0.24f;
                    Stroke(w, P(x, 0.22f), P(x + 0.26f, 0.5f), P(x, 0.78f));
                }
                break;
            case "shield":
            {
                Vector2[] outline = { P(0.2f, 0.16f), P(0.8f, 0.16f), P(0.8f, 0.52f), P(0.5f, 0.88f), P(0.2f, 0.52f), P(0.2f, 0.16f) };
                DrawColoredPolygon(outline[..5], soft);
                Stroke(w, outline);
                break;
            }
            case "plus":
                Stroke(w * 1.6f, P(0.5f, 0.16f), P(0.5f, 0.84f));
                Stroke(w * 1.6f, P(0.16f, 0.5f), P(0.84f, 0.5f));
                break;
            case "eye":
            case "hidden":
            {
                var upper = new Vector2[13];
                var lower = new Vector2[13];
                for (int i = 0; i <= 12; i++)
                {
                    float t = i / 12.0f;
                    float lift = Mathf.Sin(t * Mathf.Pi) * 0.26f;
                    upper[i] = P(0.1f + t * 0.8f, 0.5f - lift);
                    lower[i] = P(0.1f + t * 0.8f, 0.5f + lift);
                }
                Stroke(w, upper);
                Stroke(w, lower);
                DrawCircle(P(0.5f, 0.5f), Size.X * 0.11f, Ink);
                if (_shape == "hidden")
                {
                    Stroke(w * 1.2f, P(0.18f, 0.84f), P(0.82f, 0.16f));
                }
                break;
            }
            case "push":
                Stroke(w, P(0.12f, 0.5f), P(0.62f, 0.5f));
                Stroke(w, P(0.42f, 0.28f), P(0.64f, 0.5f), P(0.42f, 0.72f));
                Stroke(w * 1.4f, P(0.82f, 0.18f), P(0.82f, 0.82f));
                break;
            case "rings":
                DrawArc(P(0.38f, 0.5f), Size.X * 0.22f, 0, Mathf.Tau, 32, Ink, w);
                DrawArc(P(0.62f, 0.5f), Size.X * 0.22f, 0, Mathf.Tau, 32, Ink, w);
                break;
            case "up":
                Stroke(w * 1.2f, P(0.5f, 0.86f), P(0.5f, 0.2f));
                Stroke(w * 1.2f, P(0.24f, 0.44f), P(0.5f, 0.16f), P(0.76f, 0.44f));
                break;
            case "hourglass":
                DrawColoredPolygon(new[] { P(0.26f, 0.18f), P(0.74f, 0.18f), P(0.5f, 0.5f) }, soft);
                Stroke(w, P(0.22f, 0.16f), P(0.78f, 0.16f), P(0.22f, 0.84f), P(0.78f, 0.84f), P(0.22f, 0.16f));
                break;
            case "flask":
                DrawCircle(P(0.5f, 0.62f), Size.X * 0.24f, soft);
                DrawArc(P(0.5f, 0.62f), Size.X * 0.24f, 0, Mathf.Tau, 32, Ink, w);
                Stroke(w, P(0.42f, 0.4f), P(0.42f, 0.16f), P(0.58f, 0.16f), P(0.58f, 0.4f));
                break;
            default:
            {
                Font font = ThemeDB.FallbackFont;
                int size = Mathf.Max(8, (int)(Size.Y * 0.6f));
                Vector2 measure = font.GetStringSize(_letter, HorizontalAlignment.Left, -1, size);
                DrawString(font, new Vector2((Size.X - measure.X) / 2, Size.Y / 2 + font.GetAscent(size) / 2 - 2), _letter, HorizontalAlignment.Left, -1, size, Ink);
                break;
            }
        }
    }
}
