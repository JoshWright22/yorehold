using System;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>The picture fitted into its box with the 3:4 frame over it; a drag moves the frame, the wheel zooms.</summary>
public partial class CropCanvas : Control
{
    public event Action<PictureFocus>? Moved;

    private Texture2D? _picture;
    private PictureFocus _focus = PictureFocus.Middle;
    private bool _dragging;

    public Texture2D? Picture
    {
        get => _picture;
        set
        {
            _picture = value;
            QueueRedraw();
        }
    }

    public PictureFocus Focus
    {
        get => _focus;
        set
        {
            _focus = value;
            QueueRedraw();
        }
    }

    // where the picture is drawn in the box, kept whole
    private Rect2 Fitted()
    {
        if (_picture == null)
        {
            return new Rect2(Vector2.Zero, Size);
        }
        Vector2 size = _picture.GetSize();
        float scale = Mathf.Min(Size.X / size.X, Size.Y / size.Y);
        Vector2 drawn = size * scale;
        return new Rect2((Size - drawn) / 2, drawn);
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Night);
        DrawRect(new Rect2(Vector2.Zero, Size), Palette.Iron, false, 1);
        if (_picture == null)
        {
            return;
        }
        Rect2 fitted = Fitted();
        DrawTextureRect(_picture, fitted, false);
        Vector2 size = _picture.GetSize();
        float scale = fitted.Size.X / size.X;
        (double x, double y, double w, double h) = _focus.Cut(size.X, size.Y, 0.75);
        var frame = new Rect2(fitted.Position + new Vector2((float)x, (float)y) * scale, new Vector2((float)w, (float)h) * scale);
        // thirds, to place a face by, then the frame itself in amber
        for (int i = 1; i < 3; i++)
        {
            DrawLine(frame.Position + new Vector2(frame.Size.X * i / 3, 0), frame.Position + new Vector2(frame.Size.X * i / 3, frame.Size.Y), Palette.Slate, 1);
            DrawLine(frame.Position + new Vector2(0, frame.Size.Y * i / 3), frame.Position + new Vector2(frame.Size.X, frame.Size.Y * i / 3), Palette.Slate, 1);
        }
        DrawRect(frame, Palette.Straw, false, 2);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_picture == null)
        {
            return;
        }
        Rect2 fitted = Fitted();
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } press:
                _dragging = press.Pressed;
                if (press.Pressed)
                {
                    Aim(press.Position, fitted);
                }
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging:
                Aim(motion.Position, fitted);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel:
                double zoom = Math.Clamp(_focus.Zoom * (wheel.ButtonIndex == MouseButton.WheelUp ? 1.1 : 1 / 1.1), 1, 4);
                Moved?.Invoke(_focus with { Zoom = zoom });
                AcceptEvent();
                break;
        }
    }

    // the frame's middle to where the pointer is, as a share of the picture
    private void Aim(Vector2 at, Rect2 fitted)
    {
        Vector2 share = (at - fitted.Position) / fitted.Size;
        Moved?.Invoke(_focus with { X = Math.Clamp(share.X, 0, 1), Y = Math.Clamp(share.Y, 0, 1) });
    }
}
