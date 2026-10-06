using System;
using Godot;
using Yorehold.Rules;

namespace Yorehold;

/// <summary>
/// Plays a cutscene over the map: steers the play camera, draws the letterbox bars, the fade, the
/// captions and titles, and says when it is over. A click, a tap, Space, Enter or Escape skips it.
/// The timing is CutsceneRun's; this only draws it.
/// </summary>
public partial class CutsceneView : Control
{
    private CutsceneRun? _run;
    private PlayCamera? _camera;
    private Font? _font;

    /// <summary>The cutscene ended or was skipped.</summary>
    public event Action? Finished;

    public bool Playing => _run != null;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _font = GetThemeDefaultFont();
    }

    public void Play(Cutscene cutscene, PlayCamera camera)
    {
        _camera = camera;
        camera.Following = false;
        _run = new CutsceneRun(cutscene, camera.Position.ToRules(), camera.CurrentZoom);
        MouseFilter = MouseFilterEnum.Stop; // the map underneath waits
        Visible = true;
        Step(0);
    }

    public override void _Process(double delta)
    {
        if (_run != null)
        {
            Step(delta);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (_run != null && @event is InputEventMouseButton { Pressed: true } or InputEventScreenTouch { Pressed: true })
        {
            Skip();
            AcceptEvent();
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (_run != null && @event is InputEventKey { Pressed: true, Echo: false } key
            && key.Keycode is Key.Space or Key.Enter or Key.Escape or Key.KpEnter)
        {
            Skip();
            GetViewport().SetInputAsHandled();
        }
    }

    public void Skip()
    {
        if (_run == null)
        {
            return;
        }
        _run.Skip();
        Steer();
        End();
    }

    private void Step(double delta)
    {
        _run!.Update(delta);
        _run.TakeEvents(); // no event has a meaning in the game yet; "finished" ends it like the C++ client
        Steer();
        QueueRedraw();
        if (!_run.Running)
        {
            End();
        }
    }

    private void Steer()
    {
        if (_run != null && _camera != null && _run.CameraSteered)
        {
            _camera.JumpTo(_run.CameraPosition.ToGodot(), _run.CameraZoom);
        }
    }

    private void End()
    {
        _run = null;
        MouseFilter = MouseFilterEnum.Ignore;
        if (_camera != null)
        {
            _camera.Following = true;
        }
        QueueRedraw();
        Finished?.Invoke();
    }

    public override void _Draw()
    {
        if (_run == null)
        {
            return;
        }
        Vector2 size = GetViewportRect().Size;
        ContentColor fade = _run.Fade;
        if (fade.A > 0)
        {
            // how far the file fades, but always to ink: its own colour could be off the palette
            DrawRect(new Rect2(Vector2.Zero, size), Palette.Faded(Palette.Ink, fade.A / 255f));
        }
        float bar = size.Y * 0.11f * Yorehold.Rules.Ease.Apply("inOutQuad", _run.Bars);
        if (bar > 0)
        {
            DrawRect(new Rect2(0, 0, size.X, bar), Palette.Ink);
            DrawRect(new Rect2(0, size.Y - bar, size.X, bar), Palette.Ink);
        }
        if (_font == null)
        {
            return;
        }
        foreach (CutsceneText text in _run.Texts())
        {
            int fontSize = text.Title ? 44 : 22;
            Color color = Palette.Faded(text.Title ? Palette.Straw : Palette.Bone, text.Alpha);
            Color outline = Palette.Faded(Palette.Ink, text.Alpha);
            float width = size.X * 0.8f;
            float y = text.Title ? size.Y * 0.5f : size.Y - Math.Max(bar, size.Y * 0.08f) - 40;
            var at = new Vector2(size.X * 0.1f, y);
            DrawMultilineStringOutline(_font, at, text.Text, HorizontalAlignment.Center, width, fontSize, -1, 8, outline);
            DrawMultilineString(_font, at, text.Text, HorizontalAlignment.Center, width, fontSize, -1, color);
        }
    }
}
