using System;
using System.Collections.Generic;
using Godot;

namespace Yorehold;

/// <summary>
/// Pan and zoom over the map. Keys and the left stick pan (pan_* actions), the wheel and the
/// zoom_* actions zoom, a drag with any mouse button or one finger pans, two fingers pinch to
/// zoom. A left click or tap that didn't turn into a drag comes out as Tapped. It follows the
/// selected hero until the player pans, and recenter (Home) follows again.
/// </summary>
public partial class PlayCamera : Camera2D
{
    [Export] public float KeyPanSpeed { get; set; } = 900;
    [Export] public float ZoomStep { get; set; } = 1.15f;
    [Export] public float MaxZoom { get; set; } = 4;
    /// <summary>Screen pixels a press may move and still count as a click or tap.</summary>
    [Export] public float DragThreshold { get; set; } = 10;
    /// <summary>How fast it catches up with the hero it follows (higher is snappier).</summary>
    [Export] public float FollowRate { get; set; } = 8;

    /// <summary>Another camera kept on the same spot and zoom (the light map's), the same frame.</summary>
    [Export] public Camera2D? Mirror { get; set; }

    /// <summary>A click or tap at this point on screen.</summary>
    public event Action<Vector2>? Tapped;

    /// <summary>The map in world units; the view's centre stays on it.</summary>
    public Rect2 Bounds { get; set; } = new(0, 0, 1, 1);
    public Vector2? FollowTarget { get; set; }
    public bool Following { get; set; } = true;

    private readonly Dictionary<int, Vector2> _touches = new();
    private bool _pressed;
    private bool _dragging;
    private bool _pinching;
    private bool _panButton;
    private Vector2 _pressAt;
    private float _pinchDistance;
    private Vector2 _pinchMiddle;

    public float CurrentZoom => Zoom.X;

    public float MinZoom
    {
        get
        {
            Vector2 view = GetViewportRect().Size;
            return Mathf.Min(MaxZoom, Mathf.Min(view.X / Bounds.Size.X, view.Y / Bounds.Size.Y));
        }
    }

    public Vector2 ScreenToWorld(Vector2 screen)
    {
        return Position + (screen - GetViewportRect().Size / 2) / CurrentZoom;
    }

    public Vector2 WorldToScreen(Vector2 world)
    {
        return (world - Position) * CurrentZoom + GetViewportRect().Size / 2;
    }

    public void JumpTo(Vector2 world, float zoom)
    {
        Position = world;
        SetZoomLevel(zoom);
        Clamp();
    }

    public void PanByScreen(Vector2 delta)
    {
        Position -= delta / CurrentZoom;
        Following = false;
        Clamp();
    }

    /// <summary>Zooms by factor keeping the world point under anchor (screen) where it is.</summary>
    public void ZoomBy(float factor, Vector2 anchor)
    {
        Vector2 before = ScreenToWorld(anchor);
        SetZoomLevel(CurrentZoom * factor);
        Position += before - ScreenToWorld(anchor);
        Clamp();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventScreenTouch touch:
                if (touch.Pressed)
                {
                    _touches[touch.Index] = touch.Position;
                }
                else
                {
                    _touches.Remove(touch.Index);
                }
                if (_touches.Count >= 2)
                {
                    // a second finger: whatever the first one started is a pinch now, not a tap
                    _pinching = true;
                    _pressed = false;
                    (_pinchDistance, _pinchMiddle) = Pinch();
                }
                else if (_touches.Count == 0)
                {
                    _pinching = false;
                }
                break;
            case InputEventScreenDrag drag:
                _touches[drag.Index] = drag.Position;
                if (_touches.Count >= 2)
                {
                    (float distance, Vector2 middle) = Pinch();
                    if (_pinchDistance > 1 && distance > 1)
                    {
                        ZoomBy(distance / _pinchDistance, middle);
                    }
                    PanByScreen(middle - _pinchMiddle);
                    (_pinchDistance, _pinchMiddle) = (distance, middle);
                }
                break;
            case InputEventMagnifyGesture magnify:
                ZoomBy(magnify.Factor, magnify.Position);
                break;
            case InputEventMouseButton button:
                MouseButton(button);
                break;
            case InputEventMouseMotion motion:
                if (_pinching)
                {
                    break;
                }
                if (_panButton)
                {
                    PanByScreen(motion.Relative);
                }
                else if (_pressed && !_dragging && (motion.Position - _pressAt).Length() > DragThreshold)
                {
                    // catch up the few pixels moved before it counted as a drag
                    _dragging = true;
                    PanByScreen(motion.Position - _pressAt);
                }
                else if (_dragging)
                {
                    PanByScreen(motion.Relative);
                }
                break;
        }
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Vector2 direction = Input.GetVector("pan_left", "pan_right", "pan_up", "pan_down");
        if (direction != Vector2.Zero)
        {
            PanByScreen(-direction * KeyPanSpeed * dt);
        }
        Vector2 middle = GetViewportRect().Size / 2;
        if (Input.IsActionJustPressed("zoom_in"))
        {
            ZoomBy(ZoomStep * ZoomStep, middle);
        }
        if (Input.IsActionJustPressed("zoom_out"))
        {
            ZoomBy(1 / (ZoomStep * ZoomStep), middle);
        }
        if (Input.IsActionJustPressed("recenter"))
        {
            Following = true;
        }
        if (Following && FollowTarget is Vector2 target)
        {
            Position = Position.Lerp(target, 1 - Mathf.Exp(-FollowRate * dt));
        }
        Clamp();
    }

    private void MouseButton(InputEventMouseButton button)
    {
        switch (button.ButtonIndex)
        {
            case Godot.MouseButton.Left:
                if (button.Pressed)
                {
                    _pressed = !_pinching;
                    _dragging = false;
                    _pressAt = button.Position;
                }
                else
                {
                    bool tap = _pressed && !_dragging && !_pinching;
                    _pressed = false;
                    _dragging = false;
                    if (tap)
                    {
                        Tapped?.Invoke(button.Position);
                    }
                }
                break;
            case Godot.MouseButton.Middle:
            case Godot.MouseButton.Right:
                _panButton = button.Pressed;
                break;
            case Godot.MouseButton.WheelUp:
            case Godot.MouseButton.WheelDown:
                if (button.Pressed)
                {
                    float notches = button.Factor > 0 ? button.Factor : 1;
                    float step = Mathf.Pow(ZoomStep, notches);
                    ZoomBy(button.ButtonIndex == Godot.MouseButton.WheelUp ? step : 1 / step, button.Position);
                }
                break;
        }
    }

    private (float Distance, Vector2 Middle) Pinch()
    {
        var points = new List<Vector2>(_touches.Values);
        return (points[0].DistanceTo(points[1]), (points[0] + points[1]) / 2);
    }

    private void SetZoomLevel(float zoom)
    {
        float z = Mathf.Clamp(zoom, MinZoom, MaxZoom);
        Zoom = new Vector2(z, z);
    }

    // when the whole map fits across, it sits in the middle; otherwise the centre stays on the map
    private void Clamp()
    {
        Vector2 seen = GetViewportRect().Size / CurrentZoom;
        float x = seen.X >= Bounds.Size.X ? Bounds.GetCenter().X : Mathf.Clamp(Position.X, Bounds.Position.X, Bounds.End.X);
        float y = seen.Y >= Bounds.Size.Y ? Bounds.GetCenter().Y : Mathf.Clamp(Position.Y, Bounds.Position.Y, Bounds.End.Y);
        Position = new Vector2(x, y);
        if (Mirror != null)
        {
            Mirror.Position = Position;
            Mirror.Zoom = Zoom;
        }
    }
}
