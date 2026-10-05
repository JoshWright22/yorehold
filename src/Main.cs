using Godot;

namespace Yorehold;

public partial class Main : Node2D
{
    // Placeholder until the play screen (P4): shows the last input so a screenshot run has something to prove.
    private string _lastInput = "no input yet";
    private Vector2 _mouse;
    private bool _clicked;

    public override void _Ready()
    {
        GD.Print($"Yorehold {Rules.Version.Text} ready");
    }

    public override void _Input(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseMotion motion:
                _mouse = motion.Position;
                break;
            case InputEventMouseButton { Pressed: true } button:
                _mouse = button.Position;
                _clicked = true;
                _lastInput = $"{button.ButtonIndex} click at {button.Position.X:0}, {button.Position.Y:0}";
                break;
            case InputEventKey { Pressed: true } key:
                _lastInput = $"key {OS.GetKeycodeString(key.Keycode)}";
                break;
            default:
                return;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        Font font = ThemeDB.FallbackFont;
        DrawString(font, new Vector2(40, 70), $"Yorehold {Rules.Version.Text}", HorizontalAlignment.Left, -1, 32);
        DrawString(font, new Vector2(40, 110), _lastInput, HorizontalAlignment.Left, -1, 20, new Color(0.7f, 0.7f, 0.7f));
        if (_clicked)
        {
            DrawCircle(_mouse, 8, new Color(0.9f, 0.7f, 0.2f));
        }
    }
}
